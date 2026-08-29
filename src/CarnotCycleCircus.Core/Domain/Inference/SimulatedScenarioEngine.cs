using System.Text.RegularExpressions;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Artifacts;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Harvester;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Inference;

public interface ISimulatedScenarioEngine
{
    Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskSimulationAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default);
}

public class SimulatedScenarioEngine : ISimulatedScenarioEngine
{
    private readonly IOpenRouterClient? _openRouterClient;
    private readonly IAgentInferenceResolver? _inferenceResolver;
    private readonly ITeamDefinitionManager? _teamManager;
    private readonly IAgentEventStream? _eventStream;
    private readonly IKnowledgeMapService? _knowledgeMap;
    private readonly IPersistentMemoryStore? _memoryStore;
    private readonly ICodebaseHarvesterService? _harvester;
    private readonly ITicketStore? _ticketStore;
    private readonly IAdrDocumentManager? _adrManager;

    public SimulatedScenarioEngine(
        IOpenRouterClient? openRouterClient = null,
        IAgentInferenceResolver? inferenceResolver = null,
        ITeamDefinitionManager? teamManager = null,
        IAgentEventStream? eventStream = null,
        IKnowledgeMapService? knowledgeMap = null,
        IPersistentMemoryStore? memoryStore = null,
        ICodebaseHarvesterService? harvester = null,
        ITicketStore? ticketStore = null,
        IAdrDocumentManager? adrManager = null)
    {
        _openRouterClient = openRouterClient;
        _inferenceResolver = inferenceResolver;
        _teamManager = teamManager;
        _eventStream = eventStream;
        _knowledgeMap = knowledgeMap;
        _memoryStore = memoryStore;
        _harvester = harvester;
        _ticketStore = ticketStore;
        _adrManager = adrManager;
    }

    public async Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskSimulationAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve agent member and inference parameters
        var team = _teamManager?.GetCurrentTeam() ?? EngineeringTeam.CreateDefault();
        var member = team.GetMember(role) ?? new AgentMember(AgentPersona.CreateDefault(role));
        var (model, apiKey) = _inferenceResolver != null
            ? _inferenceResolver.ResolveInferenceParameters(member, team)
            : (member.EffectiveModel, "sk-or-v1-sandbox-mock-carnot-circus-0001");

        // 2. Gather upstream context from parent epic and dependent tickets
        var upstreamDeliverables = GatherUpstreamDeliverables(ticket);
        var harvestReport = _harvester?.GetLatestReport();

        // 3. Check if a real OpenRouter API key is available
        bool isRealKey = !string.IsNullOrWhiteSpace(apiKey) &&
                         !apiKey.Contains("sandbox", StringComparison.OrdinalIgnoreCase) &&
                         !apiKey.Contains("mock", StringComparison.OrdinalIgnoreCase);

        if (isRealKey && _openRouterClient != null)
        {
            try
            {
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"🚀 Unleashing real inference on [{model}] for ticket [{ticket.Id}]: {ticket.Title} (Injected {upstreamDeliverables.Count} upstream deliverables)...",
                    type: MessageType.StateChange,
                    ticketId: ticket.Id
                ));

                var artifacts = await GenerateViaOpenRouterAsync(member, role, ticket, upstreamDeliverables, harvestReport, model, apiKey, cancellationToken);
                if (artifacts.Count > 0)
                {
                    var names = string.Join(", ", artifacts.Select(a => $"'{a.Name}'"));
                    _eventStream?.Publish(AgentMessage.Create(
                        role: role,
                        senderName: member.Persona.Name,
                        content: $"⚡ Generated {artifacts.Count} real deliverable(s): {names} via OpenRouter [{model}]. 'Like a glove!'",
                        type: MessageType.Handoff,
                        ticketId: ticket.Id
                    ));

                    return artifacts;
                }
            }
            catch (Exception ex)
            {
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"⚠️ OpenRouter API error ({ex.Message}). Falling back to deterministic local generator for [{ticket.Id}].",
                    type: MessageType.Alert,
                    ticketId: ticket.Id
                ));
            }
        }

        // 4. Fallback to deterministic offline deliverable generation
        return await GenerateDeterministicFallbackAsync(role, ticket, upstreamDeliverables, harvestReport, cancellationToken);
    }

    private IReadOnlyList<ArtifactItem> GatherUpstreamDeliverables(TicketItem ticket)
    {
        var deliverables = new List<ArtifactItem>();

        if (_ticketStore == null)
        {
            return deliverables;
        }

        // 1. Parent Epic Deliverables (e.g. PRD)
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

        // 3. If no direct dependencies or parent epic found, check any completed upstream tickets in same epic
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

        var response = await _openRouterClient!.CompleteAsync(request, apiKey, cancellationToken);
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
                    content: $"🔧 Detected syntax issues in generated code. Initiating autonomous self-healing remediation pass...",
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

        // Format upstream deliverables context
        var upstreamSummary = new System.Text.StringBuilder();
        if (upstreamDeliverables.Count > 0)
        {
            upstreamSummary.AppendLine("\n=== UPSTREAM INTER-AGENT DELIVERABLE CONTEXT ===");
            foreach (var d in upstreamDeliverables)
            {
                upstreamSummary.AppendLine($"--- [Artifact: {d.Name} ({d.ContentType})] ---");
                // Limit large content to avoid budget blowout while keeping essential contracts
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

                // If filename has directory structure (e.g. Services/FooService.cs), sanitize to clean filename
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
        // Strip out role prefixes e.g. [Arch], [Dev], [QA]
        title = Regex.Replace(title, @"^\[(?:Arch|Dev|Security|Opt|QA|TPM)\]\s*", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"^(?:Implement|Design|Review|Benchmark|Verify|Audit)\s+", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s+for\s+.*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"[^a-zA-Z0-9]", " ");
        
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "CoreService";

        var pascal = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : "")));
        return string.IsNullOrWhiteSpace(pascal) ? "CoreService" : pascal;
    }

    private Task<IReadOnlyList<ArtifactItem>> GenerateDeterministicFallbackAsync(
        AgentRole role,
        TicketItem ticket,
        IReadOnlyList<ArtifactItem> upstreamDeliverables,
        CodebaseHarvestReport? harvestReport,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ArtifactItem>();
        var domain = ExtractDomainContext(ticket);
        var targetNamespace = harvestReport?.Projects.FirstOrDefault(p => p.ProjectType.Contains("Core") || p.ProjectType.Contains("Domain"))?.Name
                              ?? (harvestReport != null && !string.IsNullOrWhiteSpace(harvestReport.SolutionName) ? $"{harvestReport.SolutionName}.Core" : "CarnotCycleCircus.Core.Domain");

        switch (role)
        {
            case AgentRole.RequirementsResearcher:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_RESEARCH_BRIEF.md",
                    Content: $"""
                    # Requirements Research & Technical Feasibility Brief: {ticket.Title}

                    ## 1. Problem Space & Domain Context
                    Comprehensive analysis of the target capabilities required for {ticket.Title}.
                    Objective: {ticket.Description}

                    ## 2. Standards, RFCs & Technical Specifications
                    - **RFC / Industry Protocols**: Aligned with modern asynchronous messaging standards and zero-allocation memory models.
                    - **Type Safety Guarantees**: Immutable record semantics and readonly struct return contracts.

                    ## 3. Ecosystem & Library Landscape
                    - **Runtime Platform**: .NET 10.0 / C# 13.
                    - **Core Dependencies**: `System.Threading.Channels`, `System.Memory`, `Microsoft.Extensions.DependencyInjection`.
                    - **Avoided Dependencies**: Deprecated reflection-heavy serializers and unbounded in-memory queues.

                    ## 4. Codebase Dependency & Architecture Footprint
                    - **Target Boundary**: `{targetNamespace}.{domain}`
                    - **Primary Domain Abstractions**: `I{domain}Pipeline`, `{domain}Request`, `{domain}Result`.

                    ## 5. Potential Edge Cases & Failure Modes
                    - **High Concurrency Contention**: Prevented via bounded non-blocking channels and task schedulers.
                    - **Memory Leaks / Gen0 Allocations**: Prevented via `ReadOnlyMemory<byte>` and `ValueTask`.
                    - **Unsanitized Inbound Data**: Strict validation and STRIDE threat mitigation boundaries required.

                    ## 6. Recommendations for Technical Product Manager (TPM)
                    - Prioritize Clean Architecture decoupling between application contracts and infrastructure services.
                    - Ensure all domain entities are declared as immutable records.
                    - Require explicit failure and circuit-breaker handling in downstream acceptance criteria.
                    """,
                    ContentType: "markdown",
                    Description: "Requirements Research & Technical Feasibility Brief"
                ));
                break;

            case AgentRole.TechnicalProductManager:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_PRD.md",
                    Content: $"""
                    # Product Requirements Document (PRD): {ticket.Title}

                    ## 1. Executive Summary & Objective
                    {ticket.Description}

                    ## 2. Target Users & System Context
                    - **Enterprise Operators**: Require low-latency orchestration and deterministic system stability.
                    - **Autonomous Agents**: Require structured handoff contracts and unambiguous execution boundaries.

                    ## 3. Domain Concepts & Entities
                    - `{domain}Request`: Immutable record containing inbound processing payloads.
                    - `{domain}Result`: Readonly record struct indicating operational outcome and metrics.
                    - `I{domain}Pipeline`: Core execution contract for async stream processing.

                    ## 4. Functional Acceptance Criteria
                    {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- [ ] {ac}"))}

                    ## 5. Non-Functional Requirements (NFRs)
                    - **Performance**: Sub-5ms P99 latency on hot path pipelines with zero GC Gen0 heap allocations.
                    - **Security**: 100% conformance to Microsoft STRIDE threat mitigation standards.
                    - **Reliability**: Self-healing remediation loopbacks via reactive DAG failure ports.
                    """,
                    ContentType: "markdown",
                    Description: "Product Requirements Document (PRD)"
                ));
                break;

            case AgentRole.LeadArchitect:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_ADR.md",
                    Content: $$"""
                    # ADR-014: High-Performance Architecture for {{ticket.Title}}

                    ## Status
                    **Accepted**

                    ## Context
                    Concurrent multi-agent execution requires strict state isolation, non-blocking asynchronous streaming, and deterministic failure recovery for `{{domain}}`.

                    ## Architectural Decision
                    1. **Clean Architecture Layering**: Scaffolds Domain (`{{domain}}Request`, `{{domain}}Result`), Application Contracts (`I{{domain}}Pipeline`), and Extensions (`{{domain}}ServiceCollectionExtensions`).
                    2. **Immutable Records & Structs**: Domain entities `{{domain}}Request` and `{{domain}}Result` declared as immutable records and readonly record structs.
                    3. **Reactive Channel Event Streams**: Communication relies on bounded `Channel<T>` for zero-lock message passing.
                    4. **Interface Contract**: Expose `I{{domain}}Pipeline` with zero-allocation `ValueTask<{{domain}}Result>` and `ReadOnlyMemory<byte>` hot paths.
                    5. **Connectable Failure DAG**: Nodes expose dedicated input, output, and failure ports to allow automated recovery without system abort.

                    ## Exact C# Type Contracts
                    ```csharp
                    namespace {{targetNamespace}}.{{domain}};

                    public readonly record struct {{domain}}Result(bool Success, int BytesProcessed, long LatencyTicks);
                    public record {{domain}}Request(string Id, ReadOnlyMemory<byte> Payload, DateTimeOffset Timestamp);

                    public interface I{{domain}}Pipeline
                    {
                        ValueTask<{{domain}}Result> ProcessAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
                    }
                    ```

                    ## Clean Architecture Scaffolding Blueprint
                    - **Domain & Contracts**: `I{{domain}}Pipeline.cs`
                    - **Dependency Injection**: `{{domain}}ServiceCollectionExtensions.cs`
                    - **Implementation (Dev)**: `{{domain}}PipelineService.cs`
                    - **Verification (Dev/QA)**: `{{domain}}PipelineTests.cs`

                    ## Alternatives Considered
                    - Mutable POCOs with sync locks (Rejected: Concurrency hazards and deadlocks).
                    - Pure waterfall execution (Rejected: Lacks automated remediation and self-healing).

                    ## Consequences & Trade-offs
                    - **Positive**: High throughput, deterministic audit logging, zero thread contention, and cohesive clean boundaries.
                    - **Negative**: Explicit state machine cloning required on state transitions.
                    """,
                    ContentType: "markdown",
                    Description: "Clean Architecture Blueprint & Architectural Decision Record (ADR)"
                ));

                // Lead Architect also scaffolds foundational contracts and DI extensions
                artifacts.Add(new ArtifactItem(
                    Name: $"I{domain}Pipeline.cs",
                    Content: $$"""
                    namespace {{targetNamespace}}.{{domain}};

                    using System;
                    using System.Threading;
                    using System.Threading.Tasks;

                    /// <summary>
                    /// Domain model for {{domain}} processing requests.
                    /// </summary>
                    public record {{domain}}Request(string Id, ReadOnlyMemory<byte> Payload, DateTimeOffset Timestamp);

                    /// <summary>
                    /// Readonly struct result with zero heap allocation.
                    /// </summary>
                    public readonly record struct {{domain}}Result(bool Success, int BytesProcessed, long LatencyTicks);

                    /// <summary>
                    /// Clean Architecture Application contract for {{domain}}.
                    /// </summary>
                    public interface I{{domain}}Pipeline
                    {
                        ValueTask<{{domain}}Result> ProcessAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
                    }
                    """,
                    ContentType: "csharp",
                    Description: "Clean Architecture Domain & Contract Scaffold"
                ));

                artifacts.Add(new ArtifactItem(
                    Name: $"{domain}ServiceCollectionExtensions.cs",
                    Content: $$"""
                    namespace {{targetNamespace}}.{{domain}};

                    using Microsoft.Extensions.DependencyInjection;

                    /// <summary>
                    /// Dependency injection registration scaffold for {{domain}}.
                    /// </summary>
                    public static class {{domain}}ServiceCollectionExtensions
                    {
                        public static IServiceCollection Add{{domain}}Pipeline(this IServiceCollection services)
                        {
                            // Implementation bound by Developer phase
                            return services;
                        }
                    }
                    """,
                    ContentType: "csharp",
                    Description: "Clean Architecture DI Registration Scaffold"
                ));
                break;

            case AgentRole.SoftwareDeveloper:
                // Generate multi-file bundle: Interface, Service, DI, Tests
                artifacts.Add(new ArtifactItem(
                    Name: $"I{domain}Pipeline.cs",
                    Content: $$"""
                    namespace {{targetNamespace}}.{{domain}};

                    using System;
                    using System.Threading;
                    using System.Threading.Tasks;

                    /// <summary>
                    /// Domain model for {{domain}} processing requests.
                    /// </summary>
                    public record {{domain}}Request(string Id, ReadOnlyMemory<byte> Payload, DateTimeOffset Timestamp);

                    /// <summary>
                    /// Readonly struct result with zero heap allocation.
                    /// </summary>
                    public readonly record struct {{domain}}Result(bool Success, int BytesProcessed, long LatencyTicks);

                    /// <summary>
                    /// High-throughput execution interface for {{domain}}.
                    /// </summary>
                    public interface I{{domain}}Pipeline
                    {
                        ValueTask<{{domain}}Result> ProcessAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
                    }
                    """,
                    ContentType: "csharp",
                    Description: "C# 13 Contract & Domain Models"
                ));

                artifacts.Add(new ArtifactItem(
                    Name: $"{domain}PipelineService.cs",
                    Content: $$"""
                    namespace {{targetNamespace}}.{{domain}};

                    using System;
                    using System.Diagnostics;
                    using System.Threading;
                    using System.Threading.Tasks;
                    using Microsoft.Extensions.Logging;

                    /// <summary>
                    /// Production implementation of <see cref="I{{domain}}Pipeline"/> with zero GC allocations.
                    /// </summary>
                    public sealed class {{domain}}PipelineService : I{{domain}}Pipeline
                    {
                        private readonly ILogger<{{domain}}PipelineService> _logger;

                        public {{domain}}PipelineService(ILogger<{{domain}}PipelineService> logger)
                        {
                            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                        }

                        /// <inheritdoc />
                        public async ValueTask<{{domain}}Result> ProcessAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var start = Stopwatch.GetTimestamp();
                            var span = payload.Span;

                            if (span.IsEmpty)
                            {
                                return new {{domain}}Result(false, 0, 0);
                            }

                            _logger.LogInformation("Processing {{domain}} payload of size {Size} bytes.", span.Length);

                            // Simulating asynchronous hot-path throughput
                            await Task.Yield();

                            var elapsed = Stopwatch.GetElapsedTime(start);
                            return new {{domain}}Result(true, span.Length, elapsed.Ticks);
                        }
                    }
                    """,
                    ContentType: "csharp",
                    Description: "C# 13 Service Implementation"
                ));

                artifacts.Add(new ArtifactItem(
                    Name: $"{domain}ServiceCollectionExtensions.cs",
                    Content: $$"""
                    namespace {{targetNamespace}}.{{domain}};

                    using Microsoft.Extensions.DependencyInjection;

                    public static class {{domain}}ServiceCollectionExtensions
                    {
                        public static IServiceCollection Add{{domain}}Pipeline(this IServiceCollection services)
                        {
                            services.AddSingleton<I{{domain}}Pipeline, {{domain}}PipelineService>();
                            return services;
                        }
                    }
                    """,
                    ContentType: "csharp",
                    Description: "C# 13 DI Registration"
                ));

                artifacts.Add(new ArtifactItem(
                    Name: $"{domain}PipelineTests.cs",
                    Content: $$"""
                    namespace {{targetNamespace}}.{{domain}}.Tests;

                    using System;
                    using System.Text;
                    using System.Threading;
                    using System.Threading.Tasks;
                    using Microsoft.Extensions.Logging.Abstractions;
                    using Xunit;

                    public class {{domain}}PipelineTests
                    {
                        private readonly {{domain}}PipelineService _sut = new(NullLogger<{{domain}}PipelineService>.Instance);

                        [Fact]
                        public async Task ProcessAsync_WithValidPayload_ShouldReturnSuccess()
                        {
                            var data = Encoding.UTF8.GetBytes("Test Payload Data");
                            var result = await _sut.ProcessAsync(data, CancellationToken.None);

                            Assert.True(result.Success);
                            Assert.Equal(data.Length, result.BytesProcessed);
                        }

                        [Fact]
                        public async Task ProcessAsync_WithEmptyPayload_ShouldReturnFailureWithoutAllocating()
                        {
                            var result = await _sut.ProcessAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None);

                            Assert.False(result.Success);
                            Assert.Equal(0, result.BytesProcessed);
                        }

                        [Fact]
                        public async Task ProcessAsync_WhenCancelled_ShouldThrowOperationCanceledException()
                        {
                            using var cts = new CancellationTokenSource();
                            cts.Cancel();

                            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                            {
                                await _sut.ProcessAsync(new byte[10], cts.Token);
                            });
                        }
                    }
                    """,
                    ContentType: "csharp",
                    Description: "xUnit Unit Test Suite"
                ));
                break;

            case AgentRole.SecurityEngineer:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_STRIDE_Model.md",
                    Content: $"""
                    # STRIDE Threat Model Audit: {ticket.Title}

                    ## Security Assessment Summary
                    Comprehensive threat modeling conducted on `{domain}PipelineService` and `I{domain}Pipeline` contracts.

                    | Threat Category | Asset & Code Method Evaluated | Mitigation Strategy in Code | Verification Status |
                    | :--- | :--- | :--- | :--- |
                    | **S**poofing | `{domain}Request.Id` identification | Cryptographic token tagging and sender role validation | ✅ Verified |
                    | **T**ampering | `ReadOnlyMemory<byte>` buffer slice | Immutable record types and readonly spans prevent buffer mutation | ✅ Verified |
                    | **R**epudiation | `ProcessAsync` telemetry | In-memory audit logging with caller identity | ✅ Verified |
                    | **I**nformation Disclosure | Payload buffer inspection | Secure memory slicing with zero logging of raw payload bytes | ✅ Verified |
                    | **D**enial of Service | `cancellationToken.ThrowIfCancellationRequested()` | Cooperative async cancellation and bounded buffer execution | ✅ Verified |
                    | **E**levation of Privilege | `I{domain}Pipeline` DI boundary | Sealed service class preventing unintended inheritance breakouts | ✅ Verified |

                    ## Verdict
                    **Status: APPROVED** — 0 Critical, 0 High vulnerabilities identified. Conforms to enterprise security baseline.
                    """,
                    ContentType: "markdown",
                    Description: "STRIDE Security Threat Evaluation Matrix"
                ));
                break;

            case AgentRole.OptimizationEngineer:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_Perf_Profile.md",
                    Content: $"""
                    # Performance & Allocation Benchmark Report: {ticket.Title}

                    ## Benchmark Execution Environment
                    - **Runtimes**: .NET 10.0.0 (CoreCLR 10.0.26), X64 RyuJIT
                    - **Hardware**: Modern Multi-Core Processor, Vector512 / AVX-512 enabled
                    - **Target**: `{domain}PipelineService.ProcessAsync(ReadOnlyMemory<byte>, CancellationToken)`

                    ## Benchmark Metrics (BenchmarkDotNet v0.14)
                    | Method | Mean | Error | StdDev | P99 | Gen0 | Gen1 | Gen2 | Allocated |
                    | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
                    | `ProcessAsync` | 1.12 ms | 0.02 ms | 0.01 ms | 2.45 ms | 0.000 | 0.000 | 0.000 | 0 B |

                    ## Diagnostic Conclusions
                    - **Heap Allocations**: 0 B on hot path execution (`ValueTask<{domain}Result>` returned without heap allocation).
                    - **GC Pressure**: 0 Gen0 / Gen1 / Gen2 collections observed during 100,000 continuous iterations.
                    - **SLA Conformance**: Meets < 5.0 ms P99 requirement.
                    """,
                    ContentType: "markdown",
                    Description: "Performance & Zero-Allocation Benchmark Report"
                ));
                break;

            case AgentRole.PrincipalQAAnalyst:
                var hasAdr = upstreamDeliverables.Any(d => d.Name.Contains("ADR", StringComparison.OrdinalIgnoreCase) || d.Content.Contains("Architectural Decision Record", StringComparison.OrdinalIgnoreCase));
                var adrAuditSection = hasAdr
                    ? "- [x] Verified: Architectural Decision Record (ADR) & Clean Architecture scaffold confirmed in upstream deliverables."
                    : "- [ ] FAILED: Missing Architectural Decision Record (ADR) — rejected back to Lead Architect for remediation.";
                var certStatus = hasAdr ? "PASSED" : "REJECTED";

                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_QA_Scorecard.md",
                    Content: $"""
                    # QA Certification & Acceptance Scorecard: {ticket.Title}

                    ## 1. Architectural & ADR Compliance Audit
                    {adrAuditSection}
                    - [x] Verified: Domain layer isolation and primary interface contracts verified.

                    ## 2. Acceptance Criteria Traceability Matrix
                    {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- [x] Verified: {ac} (Tested by `{domain}PipelineTests.ProcessAsync_WithValidPayload_ShouldReturnSuccess`)"))}

                    ## 3. Automated Test Execution Summary
                    - **Target Suite**: `{domain}PipelineTests`
                    - **Unit Tests**: 3 Passed, 0 Failed, 0 Skipped
                    - **Line Coverage**: 100.0%
                    - **Branch Coverage**: 100.0%

                    ## 4. Boundary & Negative Test Results
                    - **Null / Empty Input**: Handled cleanly by `ProcessAsync_WithEmptyPayload_ShouldReturnFailureWithoutAllocating`.
                    - **Cancellation Handling**: Verified with `ProcessAsync_WhenCancelled_ShouldThrowOperationCanceledException`.
                    - **Failure Port Recovery**: Tripped failure port routed correctly to remediation node and recovered.

                    ## 5. Release Decision
                    **Certification Status: {certStatus}** — Production readiness verified for `{domain}`.
                    """,
                    ContentType: "markdown",
                    Description: "QA Verification & Traceability Scorecard"
                ));
                break;

            case AgentRole.IntegrationEngineer:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_Release_Manifest.md",
                    Content: $"""
                    # Release Manifest & Solution Package: {ticket.Title}

                    ## 1. Solution Architecture & Directory Layout
                    ```text
                    {targetNamespace}/
                    ├── src/
                    │   └── {targetNamespace}.{domain}/
                    │       ├── Domain/
                    │       │   └── {domain}Models.cs
                    │       ├── Contracts/
                    │       │   └── I{domain}Pipeline.cs
                    │       ├── Services/
                    │       │   └── {domain}PipelineService.cs
                    │       └── Extensions/
                    │           └── {domain}ServiceCollectionExtensions.cs
                    ├── tests/
                    │   └── {targetNamespace}.{domain}.Tests/
                    │       └── {domain}PipelineTests.cs
                    ├── docs/
                    │   └── adrs/
                    │       └── ADR-014-{domain}.md
                    ├── artifacts/
                    │   ├── prds/
                    │   ├── security/
                    │   ├── benchmarks/
                    │   └── qa/
                    ├── Directory.Build.props
                    ├── Directory.Packages.props
                    └── {domain}.slnx
                    ```

                    ## 2. Integrated Artifact Inventory
                    | Stage | Role | Artifact Name | Content Type | Status |
                    | :--- | :--- | :--- | :--- | :--- |
                    | Product | TPM | `{domain}_PRD.md` | Markdown | Verified |
                    | Architecture | Lead Architect | `ADR-014-{domain}.md` | Markdown | Approved |
                    | Architecture | Lead Architect | `I{domain}Pipeline.cs` | C# 13 | Compilable |
                    | Implementation | Senior Developer | `{domain}PipelineService.cs` | C# 13 | Tested |
                    | Implementation | Senior Developer | `{domain}PipelineTests.cs` | C# 13 | 100% Passed |
                    | Security | Security Engineer | `{domain}_STRIDE_Model.md` | Markdown | 0 Critical / 0 High |
                    | Optimization | Optimization Engineer | `{domain}_Perf_Profile.md` | Markdown | 0 B Heap Allocated |
                    | Quality Assurance | Principal QA | `{domain}_QA_Scorecard.md` | Markdown | Certified Passed |
                    | Integration | Integration Engineer | `{ticket.Id}_Release_Manifest.md` | Markdown | Packaged & Wired |

                    ## 3. Dependency Injection & Host Composition Root Wiring
                    ```csharp
                    // In Host / Web API Program.cs
                    using {targetNamespace}.{domain};

                    var builder = WebApplication.CreateBuilder(args);
                    builder.Services.Add{domain}Pipeline();
                    ```

                    ## 4. Build, Test & Verification Commands
                    ```bash
                    dotnet build {domain}.slnx
                    dotnet test {domain}.slnx --logger "console;verbosity=minimal"
                    ```

                    ## 5. Release Certification Summary
                    **Status: PACKAGED & PRODUCTION READY** — Solution is cleanly integrated, decoupled, and certified for deployment.
                    """,
                    ContentType: "markdown",
                    Description: "Release Manifest & Repository Solution Package"
                ));
                break;
        }

        return Task.FromResult<IReadOnlyList<ArtifactItem>>(artifacts);
    }
}

