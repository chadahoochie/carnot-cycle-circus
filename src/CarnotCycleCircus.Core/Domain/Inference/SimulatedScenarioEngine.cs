using System.Text.RegularExpressions;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;

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

    public SimulatedScenarioEngine(
        IOpenRouterClient? openRouterClient = null,
        IAgentInferenceResolver? inferenceResolver = null,
        ITeamDefinitionManager? teamManager = null,
        IAgentEventStream? eventStream = null,
        IKnowledgeMapService? knowledgeMap = null,
        IPersistentMemoryStore? memoryStore = null)
    {
        _openRouterClient = openRouterClient;
        _inferenceResolver = inferenceResolver;
        _teamManager = teamManager;
        _eventStream = eventStream;
        _knowledgeMap = knowledgeMap;
        _memoryStore = memoryStore;
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

        // 2. Check if a real OpenRouter API key is available
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
                    content: $"🚀 Unleashing real inference on [{model}] for ticket [{ticket.Id}]: {ticket.Title}...",
                    type: MessageType.StateChange,
                    ticketId: ticket.Id
                ));

                var artifact = await GenerateViaOpenRouterAsync(member, role, ticket, model, apiKey, cancellationToken);
                if (artifact != null)
                {
                    _eventStream?.Publish(AgentMessage.Create(
                        role: role,
                        senderName: member.Persona.Name,
                        content: $"⚡ Generated real deliverable '{artifact.Name}' ({artifact.ContentType}) via OpenRouter [{model}]. 'Like a glove!'",
                        type: MessageType.Handoff,
                        ticketId: ticket.Id
                    ));

                    return [artifact];
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

        // 3. Fallback to deterministic offline deliverable generation
        return await GenerateDeterministicFallbackAsync(role, ticket, cancellationToken);
    }

    private async Task<ArtifactItem?> GenerateViaOpenRouterAsync(
        AgentMember member,
        AgentRole role,
        TicketItem ticket,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var (systemPrompt, userPrompt, artifactName, contentType, description) = BuildPromptsForRole(member, role, ticket);

        var messages = new List<OpenRouterMessage>
        {
            new("system", systemPrompt),
            new("user", userPrompt)
        };

        var request = new OpenRouterChatRequest(
            Model: model,
            Messages: messages,
            Temperature: Math.Clamp(member.Persona.Temperature, 0.0, 1.0),
            MaxTokens: 3500
        );

        var response = await _openRouterClient!.CompleteAsync(request, apiKey, cancellationToken);
        var rawContent = response.FirstContent;

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        var cleanedContent = CleanContent(rawContent, contentType);

        return new ArtifactItem(
            Name: artifactName,
            Content: cleanedContent,
            ContentType: contentType,
            Description: description
        );
    }

    private (string SystemPrompt, string UserPrompt, string ArtifactName, string ContentType, string Description) BuildPromptsForRole(
        AgentMember member,
        AgentRole role,
        TicketItem ticket)
    {
        var personaPrompt = string.IsNullOrWhiteSpace(member.Persona.SystemPrompt)
            ? $"You are {member.Persona.Name}, acting as the {role.ToDisplayName()} on an elite autonomous engineering team."
            : member.Persona.SystemPrompt;

        var systemPrompt = $"""
        {personaPrompt}

        === NON-NEGOTIABLE DELIVERABLE ISOLATION CONTRACT (ADR-0005) ===
        All formal technical deliverables (PRDs, ADRs, C# code, unit tests, STRIDE threat models, benchmark reports, QA scorecards) MUST remain 100% professional, standard-compliant, rigorous, unambiguous, and completely free of joke text or sarcastic phrasing.
        Output ONLY the complete, raw technical deliverable. Do NOT wrap the entire response in outer markdown conversation or preamble.
        """;

        string userPrompt;
        string artifactName;
        string contentType;
        string description;

        switch (role)
        {
            case AgentRole.TechnicalProductManager:
                artifactName = $"{ticket.Id}_PRD.md";
                contentType = "markdown";
                description = "Product Requirements Document (PRD)";
                userPrompt = $"""
                Produce a comprehensive, rigorous Product Requirements Document (PRD) in Markdown format for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Priority: {ticket.Priority}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}

                Structure the document with:
                # Product Requirements Document (PRD): {ticket.Title}
                ## 1. Executive Summary & Objective
                ## 2. Target Users & System Context
                ## 3. Functional Acceptance Criteria (use - [ ] checkboxes)
                ## 4. Non-Functional Requirements (NFRs) (Latency, Heap Allocations, Security, Failure Port Loopbacks)
                """;
                break;

            case AgentRole.LeadArchitect:
                artifactName = $"{ticket.Id}_ADR.md";
                contentType = "markdown";
                description = "Architectural Decision Record (ADR)";
                userPrompt = $"""
                Produce a formal Nygard / MADR-compliant Architectural Decision Record (ADR) in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}

                Structure the document with:
                # ADR-014: High-Performance Architecture for {ticket.Title}
                ## Status
                Accepted
                ## Context
                ## Architectural Decision (Specify immutable C# records, bounded Channel<T>, zero-allocation pipelines, connectable failure DAG ports)
                ## Alternatives Considered
                ## Consequences & Trade-offs (Positive and Negative)
                """;
                break;

            case AgentRole.SoftwareDeveloper:
                artifactName = $"{ticket.Id}_Implementation.cs";
                contentType = "csharp";
                description = "C# 13 Zero-Allocation Service Implementation";
                userPrompt = $"""
                Produce a complete, compilable, production-ready C# 13 (.NET 10) service implementation for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}

                Technical Requirements:
                - Use modern C# 13 / .NET 10 constructs (file-scoped namespaces, sealed classes or readonly record structs, primary constructors).
                - Zero heap allocations on hot path routines (use ValueTask, ReadOnlyMemory<byte>, ReadOnlySpan<char>, MemoryPool, bounded Channels).
                - Accept CancellationToken cancellationToken = default on all async methods.
                - Include XML documentation comments.
                - Output compilable, valid C# source code.
                """;
                break;

            case AgentRole.SecurityEngineer:
                artifactName = $"{ticket.Id}_STRIDE_Model.md";
                contentType = "markdown";
                description = "STRIDE Security Threat Evaluation Matrix";
                userPrompt = $"""
                Produce a complete Microsoft STRIDE Security Threat Model Audit in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}

                Structure the document with:
                # STRIDE Threat Model Audit: {ticket.Title}
                ## Security Assessment Summary
                ## Threat Evaluation Matrix (Markdown Table covering Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege with columns: Threat Category | Finding & Asset Evaluation | Mitigation Strategy | Verification)
                ## Verdict (State Status: APPROVED - 0 Critical, 0 High vulnerabilities)
                """;
                break;

            case AgentRole.OptimizationEngineer:
                artifactName = $"{ticket.Id}_Perf_Profile.md";
                contentType = "markdown";
                description = "Performance & Zero-Allocation Benchmark Report";
                userPrompt = $"""
                Produce a detailed BenchmarkDotNet Performance and Zero-Allocation Report in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}

                Structure the document with:
                # Performance & Allocation Benchmark Report: {ticket.Title}
                ## Benchmark Execution Environment (.NET 10.0, RyuJIT, AVX-512)
                ## Benchmark Metrics Table (Columns: Method | Mean | Error | StdDev | P99 | Gen0 | Gen1 | Gen2 | Allocated)
                ## Diagnostic Conclusions (Verify 0 B Gen0 heap allocations and sub-5ms P99 latency SLA conformance)
                """;
                break;

            case AgentRole.PrincipalQAAnalyst:
            default:
                artifactName = $"{ticket.Id}_QA_Scorecard.md";
                contentType = "markdown";
                description = "QA Verification & Traceability Scorecard";
                userPrompt = $"""
                Produce an exhaustive QA Acceptance & Verification Scorecard in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}

                Structure the document with:
                # QA Certification & Acceptance Scorecard: {ticket.Title}
                ## 1. Acceptance Criteria Traceability Matrix (Mark - [x] Verified for each criterion)
                ## 2. Automated Test Execution Summary (Unit Tests, Integration Tests, Line Coverage %, Branch Coverage %)
                ## 3. Boundary & Negative Edge Case Results (Null input, cancellation handling, failure port recovery)
                ## 4. Release Decision (Certification Status: PASSED)
                """;
                break;
        }

        return (systemPrompt, userPrompt, artifactName, contentType, description);
    }

    private static string CleanContent(string raw, string contentType)
    {
        var trimmed = raw.Trim();
        if (contentType == "csharp")
        {
            var match = Regex.Match(trimmed, @"```(?:csharp|cs)?\s*\n([\s\S]*?)\n```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }
        else if (contentType == "markdown")
        {
            var match = Regex.Match(trimmed, @"^```(?:markdown|md)?\s*\n([\s\S]*?)\n```$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }
        return trimmed;
    }

    private Task<IReadOnlyList<ArtifactItem>> GenerateDeterministicFallbackAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<ArtifactItem>();

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

                    ## 3. Functional Acceptance Criteria
                    {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- [ ] {ac}"))}

                    ## 4. Non-Functional Requirements (NFRs)
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
                    Content: $"""
                    # ADR-014: High-Performance Architecture for {ticket.Title}

                    ## Status
                    **Accepted**

                    ## Context
                    Concurrent multi-agent execution requires strict state isolation, non-blocking asynchronous streaming, and deterministic failure recovery.

                    ## Architectural Decision
                    1. **Immutable Records & Structs**: Domain entities, payloads, and DTOs are declared as immutable C# records or readonly record structs.
                    2. **Reactive Channel Event Streams**: Communication relies on bounded `System.Threading.Channels.Channel<T>` for zero-lock message passing.
                    3. **Connectable Failure DAG**: Nodes expose dedicated input, output, and failure ports to allow automated recovery without system abort.

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
                var serviceName = ticket.Id.Replace("-", "_");
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_Implementation.cs",
                    Content: $$"""
                    namespace CarnotCycleCircus.Services;

                    using System;
                    using System.Threading;
                    using System.Threading.Tasks;
                    using Microsoft.Extensions.Logging;

                    /// <summary>
                    /// High-throughput service implementation for {{ticket.Title}}.
                    /// Utilizes zero-allocation ValueTask pipelines and memory slicing.
                    /// </summary>
                    public sealed class {{serviceName}}Service
                    {
                        private readonly ILogger<{{serviceName}}Service> _logger;

                        public {{serviceName}}Service(ILogger<{{serviceName}}Service> logger)
                        {
                            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                        }

                        /// <summary>
                        /// Executes payload processing on the hot path with zero heap allocations.
                        /// </summary>
                        /// <param name="payload">Read-only memory buffer containing payload bytes.</param>
                        /// <param name="cancellationToken">Cancellation token.</param>
                        /// <returns>ValueTask indicating success.</returns>
                        public async ValueTask<bool> ExecuteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var span = payload.Span;
                            _logger.LogInformation("Processing payload of size {Size} bytes with zero heap allocations.", span.Length);

                            // Simulating asynchronous hot-path throughput
                            await Task.Yield();

                            return true;
                        }
                    }
                    """,
                    ContentType: "csharp",
                    Description: "C# 13 Zero-Allocation Service Implementation"
                ));
                break;

            case AgentRole.SecurityEngineer:
                artifacts.Add(new ArtifactItem(
                    Name: $"{ticket.Id}_STRIDE_Model.md",
                    Content: $"""
                    # STRIDE Threat Model Audit: {ticket.Title}

                    ## Security Assessment Summary
                    Comprehensive threat modeling conducted across system boundaries, payload channels, and role permissions.

                    | Threat Category | Finding & Asset Evaluation | Mitigation Strategy | Verification |
                    | :--- | :--- | :--- | :--- |
                    | **S**poofing | Agent role identity verification | Cryptographic token tagging on HandoffPackets | ✅ Verified |
                    | **T**ampering | In-flight payload integrity | Immutable C# records prevent mutation | ✅ Verified |
                    | **R**epudiation | Execution audit trail | Append-only in-memory telemetry stream | ✅ Verified |
                    | **I**nformation Disclosure | Sensitive credential handling | API keys masked in logs, stored in secure vault | ✅ Verified |
                    | **D**enial of Service | Cascading workflow failures | Depth limits and circuit breakers on DAG nodes | ✅ Verified |
                    | **E**levation of Privilege | Sandbox boundary breakout | Strict role-based tool execution scopes | ✅ Verified |

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

                    ## Benchmark Metrics (BenchmarkDotNet v0.14)
                    | Method | Mean | Error | StdDev | P99 | Gen0 | Gen1 | Gen2 | Allocated |
                    | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
                    | `ExecuteAsync` | 1.42 ms | 0.03 ms | 0.02 ms | 3.10 ms | 0.000 | 0.000 | 0.000 | 0 B |

                    ## Diagnostic Conclusions
                    - **Heap Allocations**: 0 B on hot path execution path.
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
                    {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- [x] Verified: {ac}"))}

                    ## 2. Automated Test Execution Summary
                    - **Unit Tests**: 18 Passed, 0 Failed, 0 Skipped
                    - **Integration Tests**: 4 Passed, 0 Failed, 0 Skipped
                    - **Line Coverage**: 96.4%
                    - **Branch Coverage**: 94.2%

                    ## 3. Boundary & Negative Test Results
                    - **Null / Empty Input**: Handled with `ArgumentNullException` / empty validation pass.
                    - **Cancellation Handling**: `OperationCanceledException` propagated cleanly without resource leaks.
                    - **Failure Port Recovery**: Tripped failure port routed correctly to remediation node and recovered.

                    ## 4. Release Decision
                    **Certification Status: PASSED** — Production readiness verified.
                    """,
                    ContentType: "markdown",
                    Description: "QA Verification & Traceability Scorecard"
                ));
                break;
        }

        return Task.FromResult<IReadOnlyList<ArtifactItem>>(artifacts);
    }
}
