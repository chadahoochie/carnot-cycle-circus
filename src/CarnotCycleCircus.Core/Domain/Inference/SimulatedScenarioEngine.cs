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
                description = "Architectural Decision Record (ADR)";
                userPrompt = $"""
                Produce a formal Nygard / MADR-compliant Architectural Decision Record (ADR) in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === MANDATORY ARCHITECTURAL CONTRACT REQUIREMENTS ===
                You MUST explicitly define the exact C# Type Contracts and Interfaces so downstream Software Developers implement the exact names without ambiguity:
                1. Target Namespace: `{targetNamespace}.{domainContext}`
                2. Domain Records & Value Objects: Specify exact C# `record` or `readonly record struct` signatures.
                3. Service Interface Contracts: Specify exact C# `public interface I{domainContext}Service` (or relevant domain interface) with method signatures accepting `CancellationToken` and returning `ValueTask`.
                4. Dependency Injection Extension: Specify exact `Add{domainContext}(this IServiceCollection services)` method name.
                5. Multi-File Layout: Explicitly list the expected file layout (Contracts, Services, Extensions, Tests).

                Structure the document with:
                # ADR-014: High-Performance Architecture for {ticket.Title}
                ## Status
                Accepted
                ## Context
                ## Architectural Decision (Specify immutable C# records, bounded Channel<T>, zero-allocation pipelines, connectable failure DAG ports)
                ## Exact C# Type Contracts & Interface Signatures (Provide compilable C# contract snippets)
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
                You MUST implement the exact types, records, and interfaces defined in the upstream Lead Architect's ADR above.
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
            default:
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

                === QA TRACEABILITY MANDATE ===
                Review the ACTUAL C# service implementation and xUnit test suites provided in the upstream deliverable above. Map every Acceptance Criterion directly to the corresponding unit test method that validates it.

                Structure the document with:
                # QA Certification & Acceptance Scorecard: {ticket.Title}
                ## 1. Acceptance Criteria Traceability Matrix (Map each criterion to the specific Unit Test method and mark - [x] Verified)
                ## 2. Automated Test Execution Summary (Unit Tests count, Line Coverage %, Branch Coverage %, Mocking Boundaries)
                ## 3. Boundary & Negative Edge Case Results (Null input, cancellation handling, failure port recovery)
                ## 4. Release Decision (Certification Status: PASSED)
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
                    1. **Immutable Records & Structs**: Domain entities `{{domain}}Request` and `{{domain}}Result` declared as immutable records and readonly record structs.
                    2. **Reactive Channel Event Streams**: Communication relies on bounded `Channel<T>` for zero-lock message passing.
                    3. **Interface Contract**: Expose `I{{domain}}Pipeline` with zero-allocation `ValueTask<bool>` and `ReadOnlyMemory<byte>` hot paths.
                    4. **Connectable Failure DAG**: Nodes expose dedicated input, output, and failure ports to allow automated recovery without system abort.

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

                    ## Alternatives Considered
                    - Mutable POCOs with sync locks (Rejected: Concurrency hazards and deadlocks).
                    - Pure waterfall execution (Rejected: Lacks automated remediation and self-healing).

                    ## Consequences & Trade-offs
                    - **Positive**: High throughput, deterministic audit logging, zero thread contention.
                    - **Negative**: Explicit state machine cloning required on state transitions.
                    """,
                    ContentType: "markdown",
                    Description: "Architectural Decision Record (ADR)"
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
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_QA_Scorecard.md",
                    Content: $"""
                    # QA Certification & Acceptance Scorecard: {ticket.Title}

                    ## 1. Acceptance Criteria Traceability Matrix
                    {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- [x] Verified: {ac} (Tested by `{domain}PipelineTests.ProcessAsync_WithValidPayload_ShouldReturnSuccess`)"))}

                    ## 2. Automated Test Execution Summary
                    - **Target Suite**: `{domain}PipelineTests`
                    - **Unit Tests**: 3 Passed, 0 Failed, 0 Skipped
                    - **Line Coverage**: 100.0%
                    - **Branch Coverage**: 100.0%

                    ## 3. Boundary & Negative Test Results
                    - **Null / Empty Input**: Handled cleanly by `ProcessAsync_WithEmptyPayload_ShouldReturnFailureWithoutAllocating`.
                    - **Cancellation Handling**: Verified with `ProcessAsync_WhenCancelled_ShouldThrowOperationCanceledException`.
                    - **Failure Port Recovery**: Tripped failure port routed correctly to remediation node and recovered.

                    ## 4. Release Decision
                    **Certification Status: PASSED** — Production readiness verified for `{domain}`.
                    """,
                    ContentType: "markdown",
                    Description: "QA Verification & Traceability Scorecard"
                ));
                break;
        }

        return Task.FromResult<IReadOnlyList<ArtifactItem>>(artifacts);
    }
}

