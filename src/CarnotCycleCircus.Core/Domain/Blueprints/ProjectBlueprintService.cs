using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;

namespace CarnotCycleCircus.Core.Domain.Blueprints;

public record ProjectBlueprint(
    string Id,
    string Title,
    string Tagline,
    string Category,
    string TargetStack,
    string RecommendedTeamId,
    string EpicTitle,
    string EpicDescription,
    IReadOnlyList<string> InitialGoals,
    IReadOnlyList<string> KeyPatterns,
    IReadOnlyList<string> SecurityRules
);

public record ProjectIgnitionRequest(
    string Title,
    string Description,
    string TargetStack,
    string? TeamId = null,
    TicketPriority Priority = TicketPriority.High,
    IReadOnlyList<string>? KeyGoals = null,
    IReadOnlyList<string>? ArchitecturePatterns = null,
    IReadOnlyList<string>? SecurityGuardrails = null
);

public record BlueprintGenerationResult(
    string EpicId,
    string AdrId,
    IReadOnlyList<TicketItem> CreatedTickets,
    string Summary
);

public interface IProjectBlueprintService
{
    IReadOnlyList<ProjectBlueprint> GetAvailableBlueprints();
    IReadOnlyList<ProjectBlueprint> GetSuggestedInitiatives();
    ProjectBlueprint? GetBlueprint(string id);
    Task<BlueprintGenerationResult> LaunchProjectAsync(ProjectIgnitionRequest request, CancellationToken cancellationToken = default);
    Task<BlueprintGenerationResult> LaunchBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default);
    Task<BlueprintGenerationResult> LaunchCustomProjectAsync(string projectTitle, string projectDescription, string targetStack, string? teamId = null, CancellationToken cancellationToken = default);
}

public class ProjectBlueprintService : IProjectBlueprintService
{
    private readonly IWorkDecompositionEngine _decompositionEngine;
    private readonly ITicketStore _ticketStore;
    private readonly IAdrDocumentManager _adrManager;
    private readonly IKnowledgeMapService _knowledgeMap;
    private readonly IPersistentMemoryStore _memoryStore;
    private readonly ITeamDefinitionManager _teamManager;
    private readonly IAgentEventStream _eventStream;

    public ProjectBlueprintService(
        IWorkDecompositionEngine decompositionEngine,
        ITicketStore ticketStore,
        IAdrDocumentManager adrManager,
        IKnowledgeMapService knowledgeMap,
        IPersistentMemoryStore memoryStore,
        ITeamDefinitionManager teamManager,
        IAgentEventStream eventStream)
    {
        _decompositionEngine = decompositionEngine;
        _ticketStore = ticketStore;
        _adrManager = adrManager;
        _knowledgeMap = knowledgeMap;
        _memoryStore = memoryStore;
        _teamManager = teamManager;
        _eventStream = eventStream;
    }

    private static readonly IReadOnlyList<ProjectBlueprint> StarterSuggestions =
    [
        new(
            Id: "realtime-telemetry-pipeline",
            Title: "⚡ High-Throughput Telemetry Ingestion Pipeline",
            Tagline: "Zero-allocation reactive pipeline with bounded channels, buffer pooling, and backpressure.",
            Category: "High Performance & Real-Time",
            TargetStack: ".NET 10 / C# 13, Channels, ValueTask, MemoryPool<byte>",
            RecommendedTeamId: "team-high-performance",
            EpicTitle: "High-Throughput Telemetry Ingestion Pipeline",
            EpicDescription: "Design and implement a high-throughput, zero-allocation ingestion pipeline for real-time telemetry with backpressure channels and anomaly detection.",
            InitialGoals: [
                "Achieve sub-5ms P99 processing latency per telemetry payload.",
                "Zero GC Gen0 heap allocations on the hot path using ReadOnlySpan and MemoryPool.",
                "Resilient circuit breaking and fallback dead-letter channels."
            ],
            KeyPatterns: [
                "System.Threading.Channels bounded producer-consumer queue",
                "ValueTask async state machine optimization",
                "MemoryPool<byte> buffer pooling and slicing"
            ],
            SecurityRules: [
                "HMAC-SHA256 payload signature verification",
                "Strict rate-limiting and burst throttling per client ID"
            ]
        ),
        new(
            Id: "distributed-order-saga",
            Title: "🛒 Resilient Distributed Order & Payment Saga",
            Tagline: "Fault-tolerant distributed workflow with idempotent payment processing and compensation handlers.",
            Category: "Distributed Systems & Cloud",
            TargetStack: "ASP.NET Core, EF Core, Outbox Pattern, PostgreSQL",
            RecommendedTeamId: "team-standard-circus",
            EpicTitle: "Resilient Distributed Order & Payment Saga Workflow",
            EpicDescription: "Build a reliable checkout and order orchestration engine with transactional outbox, idempotent payment gateway integration, and automated compensation routines.",
            InitialGoals: [
                "Guarantee exactly-once payment processing semantics via idempotency keys.",
                "Transactional outbox pattern ensuring 100% order state consistency.",
                "Automated rollback and compensation events for declined authorizations."
            ],
            KeyPatterns: [
                "Distributed Saga with compensation transactions",
                "Transactional Outbox pattern for reliable event publishing",
                "Immutable OrderAggregate domain record types"
            ],
            SecurityRules: [
                "Zero storage of raw PAN / CVV (tokenization only)",
                "Cryptographically verified webhook signatures with timestamp freshness"
            ]
        ),
        new(
            Id: "zero-trust-identity-gateway",
            Title: "🛡️ Zero-Trust Identity, Token Vault & RBAC Gateway",
            Tagline: "Defense-in-depth API gateway with AEAD envelope encryption and fine-grained claims authorization.",
            Category: "Security & Cryptography",
            TargetStack: "ASP.NET Core, Microsoft Entra / OAuth2, AES-256-GCM, Redis Rate Limiter",
            RecommendedTeamId: "team-zero-trust",
            EpicTitle: "Zero-Trust Identity, Token Vault & RBAC Gateway",
            EpicDescription: "Engineer a high-assurance identity gateway featuring AEAD envelope encryption at rest, short-lived token issuance, STRIDE threat mitigations, and distributed token revocation.",
            InitialGoals: [
                "AES-256-GCM AEAD envelope encryption with key rotation policies.",
                "Zero secret leakage in diagnostic logs or error traces.",
                "Strict role-based access control (RBAC) with sub-millisecond cache validation."
            ],
            KeyPatterns: [
                "Cryptographic envelope encryption with ephemeral data keys",
                "Claims-based authorization middleware with policy handlers",
                "Constant-time token hash verification against timing attacks"
            ],
            SecurityRules: [
                "Full Microsoft STRIDE threat modeling on all public endpoints",
                "Zero-trust credential validation on every inter-service call"
            ]
        ),
        new(
            Id: "cqrs-eventsourcing-core",
            Title: "🏛️ Distributed CQRS & Event-Sourced Aggregate Core",
            Tagline: "Event-sourced domain aggregate roots with asynchronous projection read-models and snapshotting.",
            Category: "Enterprise Architecture",
            TargetStack: ".NET 10, Immutable Records, Marten / Cosmos DB, Projections",
            RecommendedTeamId: "team-standard-circus",
            EpicTitle: "Distributed CQRS & Event-Sourced Aggregate Core",
            EpicDescription: "Architect a clean event-sourced core where state is derived exclusively from an append-only event stream, paired with asynchronous read-model projections and optimistic concurrency controls.",
            InitialGoals: [
                "Deterministic aggregate state reconstruction from immutable domain events.",
                "High-performance read-model projections decoupled from command dispatch.",
                "Comprehensive Architectural Decision Records (ADRs) signed and verified."
            ],
            KeyPatterns: [
                "CQRS (Command Query Responsibility Segregation)",
                "Event Sourcing aggregate root with deterministic event replay",
                "Optimistic concurrency control with version tags"
            ],
            SecurityRules: [
                "Immutable event streams preventing unauthorized revisionism",
                "PII scrubbing and crypto-shredding compliance in domain events"
            ]
        ),
        new(
            Id: "chaos-benchmark-arena",
            Title: "🧪 Autonomous Chaos Testing & Performance Benchmark Arena",
            Tagline: "Automated fault injection, latency perturbation, and allocation regression harness.",
            Category: "Testing & Reliability",
            TargetStack: "xUnit, FluentAssertions, BenchmarkDotNet, Testcontainers, OpenTelemetry",
            RecommendedTeamId: "team-rapid-prototype",
            EpicTitle: "Autonomous Chaos Testing & Performance Benchmark Arena",
            EpicDescription: "Develop an automated chaos injection and performance benchmarking test suite that stresses systems with network partitions, memory pressure, and fuzzing payloads.",
            InitialGoals: [
                "Automated chaos tests verifying graceful degradation and circuit breaking.",
                "Continuous BenchmarkDotNet regression gates integrated into CI pipelines.",
                "100% acceptance criteria verification across high-entropy edge cases."
            ],
            KeyPatterns: [
                "Chaos engineering fault injection interceptors",
                "Automated memory allocation benchmarking with BenchmarkDotNet",
                "Fuzzing test generators with boundary value perturbation"
            ],
            SecurityRules: [
                "Containment boundaries preventing chaos tests from escaping staging sandbox",
                "Safe teardown of test containers and synthetic artifacts"
            ]
        )
    ];

    public IReadOnlyList<ProjectBlueprint> GetAvailableBlueprints() => StarterSuggestions;
    public IReadOnlyList<ProjectBlueprint> GetSuggestedInitiatives() => StarterSuggestions;

    public ProjectBlueprint? GetBlueprint(string id) =>
        StarterSuggestions.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task<BlueprintGenerationResult> LaunchProjectAsync(
        ProjectIgnitionRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Custom Engineering Initiative" : request.Title.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? "Autonomous development initiative decomposed into cohesive domain models, application services, and verified deliverables."
            : request.Description.Trim();
        var targetStack = string.IsNullOrWhiteSpace(request.TargetStack) ? ".NET 10 / C# 13 Clean Architecture" : request.TargetStack.Trim();

        // 1. Resolve and set squad / team
        var teamToUse = (!string.IsNullOrWhiteSpace(request.TeamId) ? _teamManager.GetTeam(request.TeamId) : null)
            ?? _teamManager.GetCurrentTeamDefinition()
            ?? _teamManager.GetAllTeams().FirstOrDefault();

        if (teamToUse != null)
        {
            _teamManager.SetCurrentTeam(teamToUse);
        }
        var teamName = teamToUse?.Name ?? "Circus Troupe";

        // 2. Deconstruct Epic and generate DAG subtasks
        var createdTickets = _decompositionEngine.DeconstructEpic(
            title,
            $"{description} (Target Stack: {targetStack})",
            request.Priority
        );
        var epicTicket = createdTickets.First(t => t.Type == TicketType.Epic);

        // 3. Register initial Architectural Decision Record (ADR)
        var adrId = $"ADR-0{(Random.Shared.Next(20, 99))}";
        var keyGoals = request.KeyGoals ?? [
            $"Deliver high-performance {title} matching production standards.",
            "Guarantee zero heap allocation hot paths and resilient failure handling.",
            "Enforce strict STRIDE threat modeling and comprehensive QA verification."
        ];
        var keyPatterns = request.ArchitecturePatterns ?? [
            "Clean Architecture with strict Domain/Application boundary separation",
            "Immutable C# record types and sealed class hierarchies",
            "Resilient Channel-based asynchronous message routing"
        ];
        var securityRules = request.SecurityGuardrails ?? [
            "Validate all inputs against STRIDE threat classifications",
            "Zero secret leakage in exceptions, logs, or handoff packets"
        ];

        var adrMarkdown = $"""
        # {adrId}: Architecture & Scaffolding Blueprint for {title}

        ## Status
        **Accepted** (Automated Project Ignition)

        ## Context
        The engineering troupe has initiated development for **{title}**.
        Target Stack: `{targetStack}`
        Assigned Squad: `{teamName}`

        ### Primary Goals
        {string.Join("\n", keyGoals.Select(g => $"- {g}"))}

        ## Decision
        We adopt a high-throughput, zero-allocation Clean Architecture pattern tailored for autonomous multi-agent execution.

        ### Key Patterns & Guardrails
        {string.Join("\n", keyPatterns.Select(p => $"- **{p}**"))}

        ### Security & Compliance Governance
        {string.Join("\n", securityRules.Select(s => $"- {s}"))}

        ## Consequences
        - **Positive**: High velocity execution with strict boundary isolation and full ticket traceability.
        - **Verification**: Enforced by Lead Architect and QA Analyst verification suites.
        """;

        _adrManager.SaveAdr(new ArchitecturalDecisionRecord(
            Id: adrId,
            Title: $"{title} Blueprint & Topology",
            Status: AdrStatus.Accepted,
            Context: $"The engineering troupe has initiated development for {title}. Target Stack: {targetStack}. Assigned Squad: {teamName}. Goals: {string.Join("; ", keyGoals)}",
            Decision: $"Adopt high-throughput zero-allocation Clean Architecture. Patterns: {string.Join("; ", keyPatterns)}",
            AlternativesConsidered: ["Monolithic legacy architecture", "Ad-hoc unstructured codebase"],
            ConsequencesPositive: keyGoals,
            ConsequencesNegative: ["Requires strict adherence to zero-allocation contracts"],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        ));

        // 4. Populate Knowledge Map Nodes
        var knId = $"KN-PROJ-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
        _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
            Id: knId,
            Label: title,
            Category: "Initiative",
            Summary: description,
            Attributes: new Dictionary<string, string>
            {
                ["Stack"] = targetStack,
                ["Team"] = teamName,
                ["EpicId"] = epicTicket.Id
            }
        ));

        foreach (var pattern in keyPatterns)
        {
            var pNodeId = $"KN-PAT-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
                Id: pNodeId,
                Label: pattern,
                Category: "Pattern",
                Summary: $"Architectural pattern for {title}.",
                Attributes: new Dictionary<string, string>
                {
                    ["Project"] = title,
                    ["Stack"] = targetStack
                }
            ));
        }

        foreach (var rule in securityRules)
        {
            var sNodeId = $"KN-SEC-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
                Id: sNodeId,
                Label: rule,
                Category: "SecurityRule",
                Summary: $"Security governance rule for {title}.",
                Attributes: new Dictionary<string, string>
                {
                    ["Project"] = title,
                    ["Governance"] = "STRIDE / AppSec"
                }
            ));
        }

        // 5. Seed Semantic Memory
        var memContent = $"""
        Project Initiative '{title}' initialized:
        - Description: {description}
        - Target Stack: {targetStack}
        - Squad: {teamName}
        - Key Goals: {string.Join("; ", keyGoals)}
        - Core Patterns: {string.Join("; ", keyPatterns)}
        """;

        await _memoryStore.StoreAsync(new MemoryEntry(
            Id: $"MEM-PROJ-{Guid.NewGuid().ToString("N")[..6]}",
            Type: MemoryType.Semantic,
            Role: AgentRole.TechnicalProductManager,
            Content: memContent,
            Embedding: _memoryStore.GenerateEmbedding(memContent),
            Importance: 0.95f,
            Tags: new Dictionary<string, string> { ["Project"] = title, ["Type"] = "ProjectIgnition" },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ), cancellationToken);

        // 6. Broadcast Launch Event to Telemetry Bus
        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.TechnicalProductManager,
            senderName: "🎪 Ringmaster",
            content: $"Launched initiative '{title}'! Epic {epicTicket.Id} generated with {createdTickets.Count - 1} subtasks across 6 roles, ADR {adrId} established, and squad assigned to '{teamName}'.",
            type: MessageType.StateChange,
            ticketId: epicTicket.Id
        ));

        return new BlueprintGenerationResult(
            EpicId: epicTicket.Id,
            AdrId: adrId,
            CreatedTickets: createdTickets,
            Summary: $"Project '{title}' ignited successfully! {createdTickets.Count} tickets generated across engineering roles with ADR {adrId} established."
        );
    }

    public Task<BlueprintGenerationResult> LaunchBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        var suggestion = GetBlueprint(blueprintId) ?? StarterSuggestions[0];
        return LaunchProjectAsync(new ProjectIgnitionRequest(
            Title: suggestion.Title,
            Description: suggestion.EpicDescription,
            TargetStack: suggestion.TargetStack,
            TeamId: suggestion.RecommendedTeamId,
            Priority: TicketPriority.Critical,
            KeyGoals: suggestion.InitialGoals,
            ArchitecturePatterns: suggestion.KeyPatterns,
            SecurityGuardrails: suggestion.SecurityRules
        ), cancellationToken);
    }

    public Task<BlueprintGenerationResult> LaunchCustomProjectAsync(
        string projectTitle,
        string projectDescription,
        string targetStack,
        string? teamId = null,
        CancellationToken cancellationToken = default)
    {
        return LaunchProjectAsync(new ProjectIgnitionRequest(
            Title: projectTitle,
            Description: projectDescription,
            TargetStack: targetStack,
            TeamId: teamId,
            Priority: TicketPriority.High
        ), cancellationToken);
    }
}
