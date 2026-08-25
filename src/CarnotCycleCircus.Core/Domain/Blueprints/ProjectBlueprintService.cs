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
    string RecommendedArchetype,
    string EpicTitle,
    string EpicDescription,
    IReadOnlyList<string> InitialGoals,
    IReadOnlyList<string> KeyPatterns,
    IReadOnlyList<string> SecurityRules
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
    ProjectBlueprint? GetBlueprint(string id);
    Task<BlueprintGenerationResult> LaunchBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default);
    Task<BlueprintGenerationResult> LaunchCustomProjectAsync(string projectTitle, string projectDescription, string targetStack, string archetypeName = "Balanced", CancellationToken cancellationToken = default);
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

    private static readonly IReadOnlyList<ProjectBlueprint> Blueprints =
    [
        new(
            Id: "iot-ingestion-pipeline",
            Title: "⚡ High-Throughput IoT Telemetry & Ingestion Pipeline",
            Tagline: "Zero-allocation event pipeline processing millions of sensor telemetry packets per second.",
            Category: "High Performance & Real-Time",
            TargetStack: ".NET 10 / C# 13, Channels, ValueTask, Memory<byte>, Redis Streams",
            RecommendedArchetype: "HighPerformance",
            EpicTitle: "High-Throughput IoT Telemetry & Ingestion Pipeline",
            EpicDescription: "Design and implement a blisteringly fast, zero-allocation ingestion pipeline for IoT telemetry payloads with backpressure channels and real-time anomaly detection.",
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
                "HMAC-SHA256 device payload signature verification",
                "Strict rate-limiting and burst throttling per device ID"
            ]
        ),
        new(
            Id: "ecommerce-checkout-saga",
            Title: "🛒 Resilient E-Commerce Order & Payment Saga",
            Tagline: "Fault-tolerant distributed checkout workflow with idempotent payments and automated compensations.",
            Category: "Distributed Systems & Cloud",
            TargetStack: "ASP.NET Core, EF Core, Stripe / Payment Gateway, Outbox Pattern, PostgreSQL",
            RecommendedArchetype: "Balanced",
            EpicTitle: "Resilient E-Commerce Order & Payment Saga Workflow",
            EpicDescription: "Build a rock-solid checkout orchestration engine with transactional outbox, idempotent payment gateway integration, and automated compensation routines on failure.",
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
                "Zero storage of raw PAN / CVV (PCI-DSS tokenization only)",
                "Cryptographically verified webhook signatures with timestamp freshness"
            ]
        ),
        new(
            Id: "zero-trust-identity-vault",
            Title: "🛡️ Zero-Trust Identity, Token Vault & RBAC Gateway",
            Tagline: "Defense-in-depth API gateway with AEAD envelope encryption and fine-grained claims authorization.",
            Category: "Security & Cryptography",
            TargetStack: "ASP.NET Core, Microsoft Entra / OAuth2, AES-256-GCM, PBKDF2, Redis Rate Limiter",
            RecommendedArchetype: "SecurityHardened",
            EpicTitle: "Zero-Trust Identity, Token Vault & RBAC Gateway",
            EpicDescription: "Engineer a high-assurance identity gateway featuring AEAD envelope encryption at rest, short-lived JWT token issuance, STRIDE threat mitigations, and distributed token revocation.",
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
            Id: "distributed-cqrs-eventsourcing",
            Title: "🏛️ Distributed CQRS & Event-Sourced Aggregate Core",
            Tagline: "Event-sourced domain aggregate roots with asynchronous projection read-models and snapshotting.",
            Category: "Enterprise Architecture",
            TargetStack: ".NET 10, Marten / Cosmos DB, Immutable Records, MediatR, Projections",
            RecommendedArchetype: "IvoryTowerCathedrals",
            EpicTitle: "Distributed CQRS & Event-Sourced Aggregate Engine",
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
            RecommendedArchetype: "ChaosMonkeyRodeo",
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

    public IReadOnlyList<ProjectBlueprint> GetAvailableBlueprints() => Blueprints;

    public ProjectBlueprint? GetBlueprint(string id) =>
        Blueprints.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task<BlueprintGenerationResult> LaunchBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        var blueprint = GetBlueprint(blueprintId) ?? Blueprints[0];

        // 1. Switch to recommended squad archetype
        _teamManager.LoadArchetype(blueprint.RecommendedArchetype);

        // 2. Deconstruct Epic and generate DAG subtasks
        var createdTickets = _decompositionEngine.DeconstructEpic(
            blueprint.EpicTitle,
            blueprint.EpicDescription,
            TicketPriority.Critical
        );
        var epicTicket = createdTickets.First(t => t.Type == TicketType.Epic);

        // 3. Register architectural decision record (ADR)
        var adrId = $"ADR-0{(Random.Shared.Next(20, 99))}";
        var adr = new ArchitecturalDecisionRecord(
            Id: adrId,
            Title: $"Architectural Foundation for {blueprint.Title}",
            Status: AdrStatus.Proposed,
            Context: $"New engineering initiative kicked off from blueprint '{blueprint.Title}'. Requirements: {string.Join(", ", blueprint.InitialGoals)}",
            Decision: $"Adopt {blueprint.TargetStack}. Enforce {string.Join(", ", blueprint.KeyPatterns)} with strict deliverable isolation.",
            AlternativesConsidered: [
                "Legacy monolithic synchronous implementation (rejected: fails scalability requirements)",
                "Third-party proprietary black box (rejected: vendor lock-in and security exposure)"
            ],
            ConsequencesPositive: [
                "Deterministic architecture with verified zero-allocation boundaries",
                "Automated STRIDE threat mitigations integrated from day one",
                "Full testability and observable telemetry streams"
            ],
            ConsequencesNegative: [
                "Requires rigorous discipline from developers and adherence to ADR contracts"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrManager.SaveAdr(adr);

        // 4. Seed Knowledge Map Nodes
        foreach (var pattern in blueprint.KeyPatterns)
        {
            var nodeId = $"KN-BP-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
                Id: nodeId,
                Label: pattern,
                Category: "Pattern",
                Summary: $"Core architectural pattern mandated for {blueprint.Title}.",
                Attributes: new Dictionary<string, string>
                {
                    ["Project"] = blueprint.Title,
                    ["Category"] = blueprint.Category,
                    ["Stack"] = blueprint.TargetStack
                }
            ));
        }

        foreach (var rule in blueprint.SecurityRules)
        {
            var nodeId = $"KN-SEC-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
                Id: nodeId,
                Label: rule,
                Category: "SecurityRule",
                Summary: $"Mandatory security governance rule for {blueprint.Title}.",
                Attributes: new Dictionary<string, string>
                {
                    ["Project"] = blueprint.Title,
                    ["Governance"] = "STRIDE / AppSec"
                }
            ));
        }

        // 5. Seed Semantic Memory for Agent Ingestion
        var memContent = $"Project Blueprint '{blueprint.Title}' initialized.\nTarget Stack: {blueprint.TargetStack}\nKey Goals: {string.Join("; ", blueprint.InitialGoals)}";
        await _memoryStore.StoreAsync(new MemoryEntry(
            Id: $"MEM-BP-{Guid.NewGuid().ToString("N")[..6]}",
            Type: MemoryType.Semantic,
            Role: AgentRole.TechnicalProductManager,
            Content: memContent,
            Embedding: _memoryStore.GenerateEmbedding(memContent),
            Importance: 0.95f,
            Tags: new Dictionary<string, string> { ["BlueprintId"] = blueprint.Id, ["Category"] = blueprint.Category },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ), cancellationToken);

        // 6. Broadcast Launch Event to Telemetry Bus
        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.TechnicalProductManager,
            senderName: "🎪 Ringmaster",
            content: $"Launched blueprint '{blueprint.Title}'! Epic {epicTicket.Id} created with {createdTickets.Count - 1} subtasks and ADR {adrId} published.",
            type: MessageType.StateChange,
            ticketId: epicTicket.Id
        ));

        return new BlueprintGenerationResult(
            EpicId: epicTicket.Id,
            AdrId: adrId,
            CreatedTickets: createdTickets,
            Summary: $"Blueprint '{blueprint.Title}' ignited successfully! {createdTickets.Count} tickets generated across 6 engineering roles, ADR {adrId} established, and squad switched to '{blueprint.RecommendedArchetype}'."
        );
    }

    public async Task<BlueprintGenerationResult> LaunchCustomProjectAsync(
        string projectTitle,
        string projectDescription,
        string targetStack,
        string archetypeName = "Balanced",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectTitle)) projectTitle = "Custom Engineering Initiative";
        if (string.IsNullOrWhiteSpace(projectDescription)) projectDescription = "Autonomous development initiative decomposed by TPM and Lead Architect.";
        if (string.IsNullOrWhiteSpace(targetStack)) targetStack = ".NET 10 / C# 13 Clean Architecture";

        // 1. Switch squad archetype
        _teamManager.LoadArchetype(archetypeName);

        // 2. Deconstruct Epic
        var createdTickets = _decompositionEngine.DeconstructEpic(
            projectTitle,
            $"{projectDescription} (Target Stack: {targetStack})",
            TicketPriority.High
        );
        var epicTicket = createdTickets.First(t => t.Type == TicketType.Epic);

        // 3. Register initial ADR
        var adrId = $"ADR-0{(Random.Shared.Next(20, 99))}";
        var adr = new ArchitecturalDecisionRecord(
            Id: adrId,
            Title: $"Architectural Specification for {projectTitle}",
            Status: AdrStatus.Proposed,
            Context: $"Custom project '{projectTitle}' initiated. Target Stack: {targetStack}. Scope: {projectDescription}",
            Decision: $"Implement {projectTitle} conforming to modern zero-allocation C# standards and STRIDE threat mitigation.",
            AlternativesConsidered: [
                "Ad-hoc unstructured implementation without ADRs (rejected: violates engineering standards)",
                "Synchronous monolithic blocking pipeline (rejected: fails scalability criteria)"
            ],
            ConsequencesPositive: [
                "Modular and maintainable codebase",
                "Automated test verification and quality gates",
                "Full deliverable traceability across tickets"
            ],
            ConsequencesNegative: [
                "Initial architectural overhead"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrManager.SaveAdr(adr);

        // 4. Seed Knowledge Node
        var nodeId = $"KN-CUSTOM-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
        _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
            Id: nodeId,
            Label: projectTitle,
            Category: "Concept",
            Summary: $"{projectDescription} (Stack: {targetStack})",
            Attributes: new Dictionary<string, string>
            {
                ["Stack"] = targetStack,
                ["Archetype"] = archetypeName
            }
        ));

        // 5. Seed Memory
        var mem = $"Custom Project '{projectTitle}' created.\nDescription: {projectDescription}\nStack: {targetStack}";
        await _memoryStore.StoreAsync(new MemoryEntry(
            Id: $"MEM-CUST-{Guid.NewGuid().ToString("N")[..6]}",
            Type: MemoryType.Semantic,
            Role: AgentRole.TechnicalProductManager,
            Content: mem,
            Embedding: _memoryStore.GenerateEmbedding(mem),
            Importance: 0.9f,
            Tags: new Dictionary<string, string> { ["Type"] = "CustomProject" },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ), cancellationToken);

        // 6. Broadcast Launch Event
        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.TechnicalProductManager,
            senderName: "🎪 Ringmaster",
            content: $"Custom project '{projectTitle}' launched! Epic {epicTicket.Id} generated with {createdTickets.Count - 1} subtasks.",
            type: MessageType.StateChange,
            ticketId: epicTicket.Id
        ));

        return new BlueprintGenerationResult(
            EpicId: epicTicket.Id,
            AdrId: adrId,
            CreatedTickets: createdTickets,
            Summary: $"Project '{projectTitle}' ignited successfully! {createdTickets.Count} tickets generated across engineering roles with ADR {adrId} established."
        );
    }
}
