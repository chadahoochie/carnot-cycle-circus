using System.Text.RegularExpressions;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Harvester;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Inference;

public interface IAgentExecutionEngine
{
    Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default);
}

public class AgentExecutionEngine : IAgentExecutionEngine
{
    private readonly IOpenRouterClient _openRouterClient;
    private readonly IAgentInferenceResolver _inferenceResolver;
    private readonly ITeamDefinitionManager? _teamManager;
    private readonly IAgentEventStream? _eventStream;
    private readonly IKnowledgeMapService? _knowledgeMap;
    private readonly IPersistentMemoryStore? _memoryStore;
    private readonly ICodebaseHarvesterService? _harvester;
    private readonly ITicketStore? _ticketStore;

    public AgentExecutionEngine(
        IOpenRouterClient openRouterClient,
        IAgentInferenceResolver inferenceResolver,
        ITeamDefinitionManager? teamManager = null,
        IAgentEventStream? eventStream = null,
        IKnowledgeMapService? knowledgeMap = null,
        IPersistentMemoryStore? memoryStore = null,
        ICodebaseHarvesterService? harvester = null,
        ITicketStore? ticketStore = null)
    {
        _openRouterClient = openRouterClient ?? throw new ArgumentNullException(nameof(openRouterClient));
        _inferenceResolver = inferenceResolver ?? throw new ArgumentNullException(nameof(inferenceResolver));
        _teamManager = teamManager;
        _eventStream = eventStream;
        _knowledgeMap = knowledgeMap;
        _memoryStore = memoryStore;
        _harvester = harvester;
        _ticketStore = ticketStore;
    }

    public async Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve agent member and inference parameters
        var team = _teamManager?.GetCurrentTeam() ?? EngineeringTeam.CreateDefault();
        var member = team.GetMember(role) ?? new AgentMember(AgentPersona.CreateDefault(role));
        var (model, apiKey) = _inferenceResolver.ResolveInferenceParameters(member, team);

        // 2. Validate API key availability
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var errMsg = $"No active OpenRouter API key configured for {role.ToDisplayName()} ({member.Persona.Name}) on model [{model}]. Please add an API key in the Key Vault.";
            _eventStream?.Publish(AgentMessage.Create(
                role: role,
                senderName: member.Persona.Name,
                content: $"🛑 {errMsg}",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));

            throw new InvalidOperationException(errMsg);
        }

        // 3. Gather upstream context from parent epic and dependent tickets
        var upstreamDeliverables = GatherUpstreamDeliverables(ticket);
        var harvestReport = _harvester?.GetLatestReport();

        _eventStream?.Publish(AgentMessage.Create(
            role: role,
            senderName: member.Persona.Name,
            content: $"🚀 Executing inference on [{model}] for ticket [{ticket.Id}]: '{ticket.Title}' (Context: {upstreamDeliverables.Count} upstream deliverables)...",
            type: MessageType.StateChange,
            ticketId: ticket.Id
        ));

        try
        {
            var artifacts = await GenerateViaOpenRouterAsync(member, role, ticket, upstreamDeliverables, harvestReport, model, apiKey, cancellationToken);
            if (artifacts.Count > 0)
            {
                var names = string.Join(", ", artifacts.Select(a => $"'{a.Name}'"));
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"⚡ Generated {artifacts.Count} deliverable(s): {names} via [{model}].",
                    type: MessageType.Handoff,
                    ticketId: ticket.Id
                ));

                return artifacts;
            }

            throw new InvalidOperationException($"Model [{model}] produced empty deliverable content for [{ticket.Id}].");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _eventStream?.Publish(AgentMessage.Create(
                role: role,
                senderName: member.Persona.Name,
                content: $"⚠️ OpenRouter API error during task execution ({ex.Message}).",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));
            throw;
        }
    }

    private IReadOnlyList<ArtifactItem> GatherUpstreamDeliverables(TicketItem ticket)
    {
        var deliverables = new List<ArtifactItem>();

        if (_ticketStore == null)
        {
            return deliverables;
        }

        // 1. Parent Epic Deliverables (e.g. Research Brief, PRD)
        if (!string.IsNullOrWhiteSpace(ticket.ParentEpicId))
        {
            var epic = _ticketStore.GetTicketById(ticket.ParentEpicId);
            if (epic != null)
            {
                deliverables.AddRange(epic.Deliverables);
            }
        }

        // 2. Direct Dependencies Deliverables (e.g. ADRs, Source Code)
        foreach (var depId in ticket.DependsOnTicketIds)
        {
            var depTicket = _ticketStore.GetTicketById(depId);
            if (depTicket != null)
            {
                deliverables.AddRange(depTicket.Deliverables);
            }
        }

        // 3. If no direct dependencies found, check completed upstream tickets in same epic
        if (deliverables.Count == 0 && !string.IsNullOrWhiteSpace(ticket.ParentEpicId))
        {
            var epicTickets = _ticketStore.GetTicketsByEpic(ticket.ParentEpicId);
            foreach (var t in epicTickets.Where(t => t.Id != ticket.Id && t.Status == TicketStatus.Done))
            {
                deliverables.AddRange(t.Deliverables);
            }
        }

        return deliverables.DistinctBy(d => d.Name).ToList();
    }

    private async Task<IReadOnlyList<ArtifactItem>> GenerateViaOpenRouterAsync(
        AgentMember member,
        AgentRole role,
        TicketItem ticket,
        IReadOnlyList<ArtifactItem> upstreamDeliverables,
        CodebaseHarvestReport? harvestReport,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var (systemPrompt, userPrompt, defaultArtifactName, contentType, description) =
            BuildPromptsForRole(member, role, ticket, upstreamDeliverables, harvestReport);

        var messages = new List<OpenRouterMessage>
        {
            new("system", systemPrompt),
            new("user", userPrompt)
        };

        var request = new OpenRouterChatRequest(
            Model: model,
            Messages: messages,
            Temperature: Math.Clamp(member.Persona.Temperature, 0.0, 1.0),
            MaxTokens: 4000
        );

        var response = await _openRouterClient.CompleteAsync(request, apiKey, cancellationToken);
        var rawContent = response.FirstContent;

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return Array.Empty<ArtifactItem>();
        }

        var parsedArtifacts = ParseDeliverableArtifacts(rawContent, defaultArtifactName, contentType, description, ticket);

        // Self-Healing C# Syntax Verification Loop for Developer Role
        if (role == AgentRole.SoftwareDeveloper && parsedArtifacts.Any(a => a.ContentType == "csharp"))
        {
            var syntaxTool = new CSharpSyntaxCheckTool();
            var syntaxErrors = new List<string>();

            foreach (var art in parsedArtifacts.Where(a => a.ContentType == "csharp"))
            {
                var checkRes = await syntaxTool.ExecuteAsync(new ToolExecutionContext("csharp_syntax_check", new Dictionary<string, string> { ["code"] = art.Content }, role, ticket.Id), cancellationToken);
                if (!checkRes.Success)
                {
                    syntaxErrors.Add($"File '{art.Name}': {checkRes.Output}");
                }
            }

            if (syntaxErrors.Count > 0)
            {
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"🔧 Detected syntax issues in generated C# code. Initiating autonomous self-healing remediation pass...",
                    type: MessageType.Alert,
                    ticketId: ticket.Id
                ));

                var remediationPrompt = $"""
                The previously generated C# source code contained structural syntax errors:
                {string.Join("\n", syntaxErrors)}

                Please fix the syntax errors (unmatched braces, unclosed quotes, invalid declarations) and output the complete, valid C# source code for each file using ```csharp:FileName.cs blocks.
                """;

                var remediationMessages = new List<OpenRouterMessage>
                {
                    new("system", systemPrompt),
                    new("user", userPrompt),
                    new("assistant", rawContent),
                    new("user", remediationPrompt)
                };

                try
                {
                    var remResponse = await _openRouterClient.CompleteAsync(new OpenRouterChatRequest(model, remediationMessages, Temperature: 0.1, MaxTokens: 4000), apiKey, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(remResponse.FirstContent))
                    {
                        var healed = ParseDeliverableArtifacts(remResponse.FirstContent, defaultArtifactName, contentType, description, ticket);
                        if (healed.Count > 0)
                        {
                            return healed;
                        }
                    }
                }
                catch
                {
                    // Fallback to original parsed artifacts
                }
            }
        }

        return parsedArtifacts;
    }

    private (string SystemPrompt, string UserPrompt, string DefaultArtifactName, string ContentType, string Description) BuildPromptsForRole(
        AgentMember member,
        AgentRole role,
        TicketItem ticket,
        IReadOnlyList<ArtifactItem> upstreamDeliverables,
        CodebaseHarvestReport? harvestReport)
    {
        var personaPrompt = string.IsNullOrWhiteSpace(member.Persona.SystemPrompt)
            ? $"You are {member.Persona.Name}, acting as the {role.ToDisplayName()} on an elite autonomous engineering team."
            : member.Persona.SystemPrompt;

        var systemPrompt = $"""
        {personaPrompt}

        === NON-NEGOTIABLE DELIVERABLE ISOLATION CONTRACT (ADR-0005) ===
        All formal technical deliverables (PRDs, ADRs, C# code, unit tests, STRIDE threat models, benchmark reports, QA scorecards) MUST remain 100% professional, standard-compliant, rigorous, unambiguous, and completely free of joke text or sarcastic phrasing.
        Output ONLY the complete, raw technical deliverables. Do NOT wrap the entire response in outer conversational preamble.
        """;

        var domainContext = ExtractDomainContext(ticket);
        var targetNamespace = harvestReport?.Projects.FirstOrDefault(p => p.ProjectType.Contains("Core") || p.ProjectType.Contains("Domain"))?.Name
                              ?? (harvestReport != null && !string.IsNullOrWhiteSpace(harvestReport.SolutionName) ? $"{harvestReport.SolutionName}.Core" : "CarnotCycleCircus.Core.Domain");

        var upstreamSummary = new System.Text.StringBuilder();
        if (upstreamDeliverables.Count > 0)
        {
            upstreamSummary.AppendLine("\n=== UPSTREAM INTER-AGENT DELIVERABLE CONTEXT ===");
            foreach (var d in upstreamDeliverables)
            {
                upstreamSummary.AppendLine($"--- [Artifact: {d.Name} ({d.ContentType})] ---");
                var preview = d.Content.Length > 2500 ? d.Content[..2500] + "\n...[truncated for context budget]..." : d.Content;
                upstreamSummary.AppendLine(preview);
                upstreamSummary.AppendLine("------------------------------------------------\n");
            }
        }

        var repoContext = harvestReport != null
            ? $"\n=== HOST REPOSITORY CONTEXT ===\nSolution: {harvestReport.SolutionName}\nTarget Namespace: {targetNamespace}\nProjects: {string.Join(", ", harvestReport.Projects.Select(p => p.Name))}\nKey Patterns: {string.Join(", ", harvestReport.DetectedPatterns)}\n"
            : $"\n=== HOST REPOSITORY CONTEXT ===\nTarget Namespace: {targetNamespace}\nTarget Runtime: .NET 10.0 / C# 13\n";

        string userPrompt;
        string defaultArtifactName;
        string contentType;
        string description;

        switch (role)
        {
            case AgentRole.RequirementsResearcher:
                defaultArtifactName = $"{ticket.Id}_RESEARCH_BRIEF.md";
                contentType = "markdown";
                description = "Requirements Research & Technical Feasibility Brief";
                userPrompt = $"""
                Produce an exhaustive, highly structured Requirements Research & Technical Feasibility Brief in Markdown format for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Priority: {ticket.Priority}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                Structure the document with:
                # Requirements Research & Technical Feasibility Brief: {ticket.Title}
                ## 1. Problem Space & Domain Context
                ## 2. Standards, RFCs & Technical Specifications (Specify exact RFC/industry standard numbers, wire protocols, and constraints)
                ## 3. Ecosystem, Frameworks & Library Landscape (Evaluate modern .NET 10 / C# 13 packages, dependencies, and performance implications)
                ## 4. Codebase Dependency & Architecture Footprint (Identify existing interfaces, models, and boundaries impacted)
                ## 5. Potential Edge Cases, Security Hazards & Failure Modes
                ## 6. Recommendations for Technical Product Manager (Scope boundaries, user story decomposition advice, acceptance criteria guidelines)
                """;
                break;

            case AgentRole.TechnicalProductManager:
                defaultArtifactName = $"{ticket.Id}_PRD.md";
                contentType = "markdown";
                description = "Product Requirements Document (PRD)";
                userPrompt = $"""
                Produce a comprehensive, rigorous Product Requirements Document (PRD) in Markdown format for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Priority: {ticket.Priority}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                Structure the document with:
                # Product Requirements Document (PRD): {ticket.Title}
                ## 1. Executive Summary & Objective
                ## 2. Target Users & System Context
                ## 3. Domain Concepts & Entities (Specify exact entity names and value objects)
                ## 4. Functional Acceptance Criteria (use - [ ] checkboxes)
                ## 5. Non-Functional Requirements (NFRs) (Latency SLA, GC Zero-Allocations, STRIDE Security Baseline, Self-Healing Failure Ports)
                """;
                break;

            case AgentRole.LeadArchitect:
                defaultArtifactName = $"{ticket.Id}_ADR.md";
                contentType = "markdown";
                description = "Clean Architecture Blueprint & Architectural Decision Record (ADR)";
                userPrompt = $"""
                Produce a formal Nygard / MADR-compliant Architectural Decision Record (ADR) AND a complete Clean Architecture solution scaffolding bundle in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === MANDATORY CLEAN ARCHITECTURE SCAFFOLDING REQUIREMENTS ===
                You MUST explicitly scaffold the Clean Architecture solution structure and define exact C# Type Contracts and Interfaces so downstream Software Developers implement without ambiguity or cohesion drift:
                1. Target Namespace: `{targetNamespace}.{domainContext}`
                2. Layering Structure:
                   - **Domain Layer**: Core immutable entities, Value Objects (`readonly record struct`), Domain Enums, and Domain Events (`Domain/`).
                   - **Application / Contracts Layer**: Primary Service Interfaces (`I{domainContext}Pipeline` or `I{domainContext}Service`), DTOs, and Pipeline Contracts (`Contracts/`).
                   - **Dependency Injection Extension**: Explicit `Add{domainContext}(this IServiceCollection services)` registration extension (`Extensions/`).
                3. Output both:
                   - The formal ADR document in Markdown (`# ADR-014: High-Performance Architecture for {ticket.Title}`)
                   - Compilable C# Clean Architecture scaffold files using labeled ````csharp:FilePath.cs```` code blocks so downstream Software Developers implement the exact cohesive architecture!

                Structure the document with:
                # ADR-014: High-Performance Architecture for {ticket.Title}
                ## Status
                Accepted
                ## Context
                ## Architectural Decision (Specify immutable C# records, bounded Channel<T>, zero-allocation pipelines, connectable failure DAG ports)
                ## Exact C# Type Contracts & Interface Signatures (Provide compilable C# contract snippets)
                ## Clean Architecture Scaffolding Blueprint (Explicitly list Domain, Contracts, Infrastructure, and DI layouts)
                ## Alternatives Considered
                ## Consequences & Trade-offs (Positive and Negative)
                """;
                break;

            case AgentRole.SoftwareDeveloper:
                defaultArtifactName = $"{domainContext}Service.cs";
                contentType = "csharp";
                description = "C# 13 Multi-File Production Implementation & Test Suite";
                userPrompt = $"""
                Produce a complete, compilable, production-ready C# 13 (.NET 10) multi-file implementation bundle for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === CRITICAL IMPLEMENTATION CONTRACT ===
                You MUST implement the exact types, records, and interfaces defined in the upstream Lead Architect's Clean Architecture scaffold above.
                Do NOT invent arbitrary generic class names like 'SUB_XXXXService' or 'MyService'. Use domain names (e.g., `{domainContext}Service`, `I{domainContext}Service`, `{domainContext}Models`).

                Output each file in a distinct labeled code block using the format:
                ```csharp:Contracts/I{domainContext}Service.cs
                // File: Contracts/I{domainContext}Service.cs
                namespace {targetNamespace}.{domainContext};
                ...
                ```

                ```csharp:Models/{domainContext}Models.cs
                // File: Models/{domainContext}Models.cs
                namespace {targetNamespace}.{domainContext};
                ...
                ```

                ```csharp:Services/{domainContext}Service.cs
                // File: Services/{domainContext}Service.cs
                namespace {targetNamespace}.{domainContext};
                ...
                ```

                ```csharp:Extensions/{domainContext}ServiceCollectionExtensions.cs
                // File: Extensions/{domainContext}ServiceCollectionExtensions.cs
                namespace {targetNamespace}.{domainContext};
                ...
                ```

                ```csharp:Tests/{domainContext}ServiceTests.cs
                // File: Tests/{domainContext}ServiceTests.cs
                namespace {targetNamespace}.{domainContext}.Tests;
                ...
                ```

                Technical Requirements:
                - Use modern C# 13 / .NET 10 constructs (file-scoped namespaces, sealed classes or readonly record structs, primary constructors).
                - Zero heap allocations on hot path routines (use ValueTask, ReadOnlyMemory<byte>, ReadOnlySpan<char>, MemoryPool, bounded Channels).
                - Accept CancellationToken cancellationToken = default on all async methods.
                - Include comprehensive xUnit unit tests verifying acceptance criteria.
                - Output complete, fully compilable code for each file.
                """;
                break;

            case AgentRole.SecurityEngineer:
                defaultArtifactName = $"{ticket.Id}_STRIDE_Model.md";
                contentType = "markdown";
                description = "STRIDE Security Threat Evaluation Matrix";
                userPrompt = $"""
                Produce an exhaustive Microsoft STRIDE Security Threat Model Audit in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === AUDIT MANDATE ===
                Audit the ACTUAL C# source code and interfaces provided in the upstream deliverable above. Evaluate input sanitization, buffer boundary slices, cancellation token handling, secret leakage, and cryptographic integrity on the actual classes and methods.

                Structure the document with:
                # STRIDE Threat Model Audit: {ticket.Title}
                ## Security Assessment Summary (Reference actual evaluated classes and methods)
                ## Threat Evaluation Matrix (Markdown Table covering Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege with columns: Threat Category | Asset & Code Method Evaluated | Mitigation Strategy in Code | Verification Status)
                ## Code-Level Security Findings & Mitigations
                ## Verdict (State Status: APPROVED - 0 Critical, 0 High vulnerabilities)
                """;
                break;

            case AgentRole.OptimizationEngineer:
                defaultArtifactName = $"{ticket.Id}_Perf_Profile.md";
                contentType = "markdown";
                description = "Performance & Zero-Allocation Benchmark Report";
                userPrompt = $"""
                Produce a detailed BenchmarkDotNet Performance and Zero-Allocation Report in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === BENCHMARK MANDATE ===
                Benchmark and audit the ACTUAL C# service implementations from the upstream deliverables above. Verify ValueTask async state machine costs, ReadOnlySpan memory slicing, and zero GC Gen0 allocations on the actual service methods.

                Structure the document with:
                # Performance & Allocation Benchmark Report: {ticket.Title}
                ## Benchmark Execution Environment (.NET 10.0, RyuJIT, AVX-512)
                ## Evaluated Methods & Hot Paths (Reference actual methods in the implementation)
                ## Benchmark Metrics Table (Columns: Method | Mean | Error | StdDev | P99 | Gen0 | Gen1 | Gen2 | Allocated)
                ## Diagnostic Conclusions (Verify 0 B Gen0 heap allocations and sub-5ms P99 latency SLA conformance)
                """;
                break;

            case AgentRole.PrincipalQAAnalyst:
                defaultArtifactName = $"{ticket.Id}_QA_Scorecard.md";
                contentType = "markdown";
                description = "QA Verification & Traceability Scorecard";
                userPrompt = $"""
                Produce an exhaustive QA Acceptance & Verification Scorecard in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === QA TRACEABILITY & ARCHITECTURAL GOVERNANCE MANDATE ===
                1. Verify Architectural Compliance & ADR:
                   - Audit whether upstream deliverables contain a formal, approved Architectural Decision Record (ADR) and Clean Architecture scaffold.
                   - Verify that domain boundaries, Clean Architecture layering, and type contracts match the architectural blueprint.
                   - IF ADR is missing or domain boundaries are violated, mark Certification Status: REJECTED (Fail to Lead Architect for remediation).
                2. Review the ACTUAL C# service implementation and xUnit test suites provided in the upstream deliverable above. Map every Acceptance Criterion directly to the corresponding unit test method that validates it.

                Structure the document with:
                # QA Certification & Acceptance Scorecard: {ticket.Title}
                ## 1. Architectural & ADR Compliance Audit (Verify ADR presence, Clean Architecture boundaries; mark - [x] Verified or Flag Violations)
                ## 2. Acceptance Criteria Traceability Matrix (Map each criterion to the specific Unit Test method and mark - [x] Verified)
                ## 3. Automated Test Execution Summary (Unit Tests count, Line Coverage %, Branch Coverage %, Mocking Boundaries)
                ## 4. Boundary & Negative Edge Case Results (Null input, cancellation handling, failure port recovery)
                ## 5. Release Decision (Certification Status: PASSED or REJECTED)
                """;
                break;

            case AgentRole.IntegrationEngineer:
            default:
                defaultArtifactName = $"{ticket.Id}_Release_Manifest.md";
                contentType = "markdown";
                description = "Release Manifest & Repository Solution Package";
                userPrompt = $"""
                Produce a comprehensive, rigorous Release Manifest & Repository Integration Blueprint in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === PACKAGING & REPOSITORY INTEGRATION MANDATE ===
                Review all upstream deliverables (PRD, ADR, C# code, STRIDE threat model, Benchmark profile, and QA scorecard).
                1. Solution & Project Wiring Blueprint:
                   - Detail the exact directory layout (`src/`, `tests/`, `docs/adrs/`, `artifacts/`).
                   - Specify the `.slnx` solution references and Central Package Management (`Directory.Packages.props`) bindings.
                   - Provide Dependency Injection composition root wireup in `Program.cs`.
                2. Release Manifest Summary:
                   - Verification Status: Certified by QA and STRIDE Security.
                   - Installation / Execution commands (`dotnet build`, `dotnet test`, `dotnet run`).
                   - Unified Artifact Inventory Table (Listing all PRDs, ADRs, Code files, STRIDE matrices, Benchmarks, and QA scorecards).

                Structure the document with:
                # Release Manifest & Solution Package: {ticket.Title}
                ## 1. Solution Architecture & Directory Layout
                ## 2. Integrated Artifact Inventory (Table listing all upstream deliverables)
                ## 3. Dependency Injection & Host Composition Root Wiring
                ## 4. Build, Test & Verification Commands
                ## 5. Release Certification Summary (Status: PACKAGED & PRODUCTION READY)
                """;
                break;
        }

        return (systemPrompt, userPrompt, defaultArtifactName, contentType, description);
    }

    private static IReadOnlyList<ArtifactItem> ParseDeliverableArtifacts(
        string rawContent,
        string defaultArtifactName,
        string defaultContentType,
        string defaultDescription,
        TicketItem ticket)
    {
        var artifacts = new List<ArtifactItem>();
        var trimmed = rawContent.Trim();

        // 1. Look for multi-file labeled code blocks e.g. ```csharp:FileName.cs or // File: FileName.cs
        var multiBlockMatches = Regex.Matches(trimmed, @"```(?:csharp|cs)?(?::([^\n\r]+))?\s*\n(?:(?:\/\/|\/\*|\#)\s*File:\s*([^\n\r*]+)(?:\*\/)?\s*\n)?([\s\S]*?)\n```", RegexOptions.IgnoreCase);

        if (multiBlockMatches.Count > 1 || (multiBlockMatches.Count == 1 && (!string.IsNullOrWhiteSpace(multiBlockMatches[0].Groups[1].Value) || !string.IsNullOrWhiteSpace(multiBlockMatches[0].Groups[2].Value))))
        {
            int fileIndex = 1;
            foreach (Match m in multiBlockMatches)
            {
                var labelTag = m.Groups[1].Value.Trim();
                var commentTag = m.Groups[2].Value.Trim();
                var codeBody = m.Groups[3].Value.Trim();

                var fileName = !string.IsNullOrWhiteSpace(labelTag) ? Path.GetFileName(labelTag) :
                               !string.IsNullOrWhiteSpace(commentTag) ? Path.GetFileName(commentTag) :
                               $"{ExtractDomainContext(ticket)}_{fileIndex}.cs";

                fileName = fileName.Replace('\\', '_').Replace('/', '_');
                if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && defaultContentType == "csharp")
                {
                    fileName += ".cs";
                }

                artifacts.Add(new ArtifactItem(
                    Name: fileName,
                    Content: codeBody,
                    ContentType: defaultContentType,
                    Description: $"{defaultDescription} - {fileName}"
                ));

                fileIndex++;
            }

            if (artifacts.Count > 0)
            {
                return artifacts;
            }
        }

        // 2. Single code block extraction
        if (defaultContentType == "csharp")
        {
            var singleMatch = Regex.Match(trimmed, @"```(?:csharp|cs)?\s*\n([\s\S]*?)\n```", RegexOptions.IgnoreCase);
            var cleanCode = singleMatch.Success ? singleMatch.Groups[1].Value.Trim() : trimmed;

            artifacts.Add(new ArtifactItem(
                Name: defaultArtifactName,
                Content: cleanCode,
                ContentType: defaultContentType,
                Description: defaultDescription
            ));
            return artifacts;
        }

        // 3. Markdown content
        var mdMatch = Regex.Match(trimmed, @"^```(?:markdown|md)?\s*\n([\s\S]*?)\n```$", RegexOptions.IgnoreCase);
        var cleanMd = mdMatch.Success ? mdMatch.Groups[1].Value.Trim() : trimmed;

        artifacts.Add(new ArtifactItem(
            Name: defaultArtifactName,
            Content: cleanMd,
            ContentType: defaultContentType,
            Description: defaultDescription
        ));

        return artifacts;
    }

    private static string ExtractDomainContext(TicketItem ticket)
    {
        var title = ticket.Title;
        title = Regex.Replace(title, @"^\[(?:Arch|Dev|Security|Opt|QA|TPM|Research|Integration)\]\s*", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"^(?:Implement|Design|Review|Benchmark|Verify|Audit|Scaffold)\s+", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s+for\s+.*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"[^a-zA-Z0-9]", " ");
        
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "CoreService";

        var pascal = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : "")));
        return string.IsNullOrWhiteSpace(pascal) ? "CoreService" : pascal;
    }
}
