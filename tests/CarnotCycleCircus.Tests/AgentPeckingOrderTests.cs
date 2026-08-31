using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentPeckingOrderTests
{
    [Fact]
    public void GetReadyTickets_ShouldPrioritizeRemediatingTickets_BeforeReadyAndBacklog()
    {
        var store = new TicketStore();

        var readyTicket = new TicketItem(
            Id: "TCK-READY",
            ParentEpicId: null,
            Title: "Standard Ready Feature",
            Description: "Ready to be worked on",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.RequirementsResearcher,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["AC1"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10)
        );

        var remediatingTicket = new TicketItem(
            Id: "TCK-REMEDIATING",
            ParentEpicId: null,
            Title: "Security Vulnerability Remediation",
            Description: "Urgent fix rejected by Security",
            Type: TicketType.Bug,
            Status: TicketStatus.Remediating,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.SecurityEngineer,
            Priority: TicketPriority.Medium,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["AC2"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        store.CreateTicket(readyTicket);
        store.CreateTicket(remediatingTicket);

        var readyTickets = store.GetReadyTickets();

        readyTickets.Should().HaveCount(2);
        readyTickets[0].Id.Should().Be("TCK-REMEDIATING");
        readyTickets[1].Id.Should().Be("TCK-READY");
    }

    [Fact]
    public void GetReadyTickets_ShouldOrderTickets_ByAgentRolePeckingOrder()
    {
        var store = new TicketStore();

        // Create tickets across all 8 roles with identical priority
        var roles = new[]
        {
            AgentRole.IntegrationEngineer,       // 7
            AgentRole.SoftwareDeveloper,         // 3
            AgentRole.PrincipalQAAnalyst,        // 6
            AgentRole.RequirementsResearcher,    // 0
            AgentRole.LeadArchitect,             // 2
            AgentRole.OptimizationEngineer,      // 5
            AgentRole.TechnicalProductManager,   // 1
            AgentRole.SecurityEngineer           // 4
        };

        foreach (var role in roles)
        {
            store.CreateTicket(new TicketItem(
                Id: $"TCK-{role}",
                ParentEpicId: null,
                Title: $"Task for {role}",
                Description: "Task description",
                Type: TicketType.Subtask,
                Status: TicketStatus.Ready,
                AssigneeRole: role,
                CreatedByRole: AgentRole.TechnicalProductManager,
                Priority: TicketPriority.Medium,
                DependsOnTicketIds: Array.Empty<string>(),
                AcceptanceCriteria: ["AC"],
                Deliverables: Array.Empty<ArtifactItem>(),
                Metadata: new Dictionary<string, string>(),
                CreatedAt: DateTimeOffset.UtcNow
            ));
        }

        var readyTickets = store.GetReadyTickets();

        readyTickets.Should().HaveCount(8);
        readyTickets.Select(t => t.AssigneeRole).Should().ContainInOrder(
            AgentRole.RequirementsResearcher,
            AgentRole.TechnicalProductManager,
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            AgentRole.OptimizationEngineer,
            AgentRole.PrincipalQAAnalyst,
            AgentRole.IntegrationEngineer
        );
    }

    [Fact]
    public async Task ExecuteTicketAsync_WithMultiOutputConnections_ShouldRouteHandoffsToAllDownstreamRoles()
    {
        var store = new TicketStore();
        var eventStream = new AgentEventStream();
        var router = new HandoffRouter(store, eventStream);
        var mockClient = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockClient, resolver, ticketStore: store);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(store);

        var executor = new GraphWorkflowExecutor(
            store,
            decompositionEngine,
            router,
            executionEngine,
            eventStream,
            consolidationEngine
        );

        // Developer node has outbound connections to both Security (node-sec) and Optimization (node-opt)
        var devTicket = new TicketItem(
            Id: "TCK-DEV-ROUTING",
            ParentEpicId: "EPIC-TEST",
            Title: "Implement High-Performance Core",
            Description: "Zero-allocation core engine",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean C# code"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        store.CreateTicket(devTicket);

        var executed = await executor.ExecuteTicketAsync(devTicket.Id);
        executed.Should().BeTrue();

        var handoffs = store.GetHandoffsForTicket(devTicket.Id);

        // Must route handoffs to BOTH Security AND Optimization downstream roles!
        handoffs.Should().Contain(h => h.FromAgentRole == AgentRole.SoftwareDeveloper && h.ToAgentRole == AgentRole.SecurityEngineer);
        handoffs.Should().Contain(h => h.FromAgentRole == AgentRole.SoftwareDeveloper && h.ToAgentRole == AgentRole.OptimizationEngineer);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ShouldRecordHandoffPackets_AcrossAllEightStages()
    {
        var store = new TicketStore();
        var eventStream = new AgentEventStream();
        var router = new HandoffRouter(store, eventStream);
        var mockClient = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockClient, resolver, ticketStore: store);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(store);

        var executor = new GraphWorkflowExecutor(
            store,
            decompositionEngine,
            router,
            executionEngine,
            eventStream,
            consolidationEngine
        );

        var success = await executor.ExecuteWorkflowAsync(
            "End-to-End Raft Engine",
            "Build complete Raft consensus engine across all 8 agent roles."
        );

        success.Should().BeTrue();

        var allHandoffs = store.GetAllHandoffs();
        allHandoffs.Should().NotBeEmpty();

        // Verify handoff chain across all 8 roles
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.RequirementsResearcher && h.ToAgentRole == AgentRole.TechnicalProductManager);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.TechnicalProductManager && h.ToAgentRole == AgentRole.LeadArchitect);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.LeadArchitect && h.ToAgentRole == AgentRole.SoftwareDeveloper);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.SoftwareDeveloper && h.ToAgentRole == AgentRole.SecurityEngineer);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.SoftwareDeveloper && h.ToAgentRole == AgentRole.OptimizationEngineer);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.SecurityEngineer && h.ToAgentRole == AgentRole.PrincipalQAAnalyst);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.OptimizationEngineer && h.ToAgentRole == AgentRole.PrincipalQAAnalyst);
        allHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.PrincipalQAAnalyst && h.ToAgentRole == AgentRole.IntegrationEngineer);
    }

    [Fact]
    public async Task ExecuteReadyTicketsAsync_ShouldExecuteTicketsInStrictTopologicalPeckingOrder_FromResearchToIntegration()
    {
        var store = new TicketStore();
        var eventStream = new AgentEventStream();
        var router = new HandoffRouter(store, eventStream);
        var mockClient = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockClient, resolver, ticketStore: store);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(store);

        var executor = new GraphWorkflowExecutor(
            store,
            decompositionEngine,
            router,
            executionEngine,
            eventStream,
            consolidationEngine
        );

        // Deconstruct an Epic: starts with Research Spike (Ready) and all downstream tickets (Backlog)
        var tickets = decompositionEngine.DeconstructEpic(
            "Autonomous Token Bucket Limiter",
            "Zero-allocation rate limiter with atomic leaky bucket algorithm"
        );

        // Initially, ONLY the Research ticket is ready!
        var readyInitial = store.GetReadyTickets();
        readyInitial.Should().ContainSingle(t => t.AssigneeRole == AgentRole.RequirementsResearcher);

        // Execute all ready tickets incrementally through completion
        var executed = await executor.ExecuteReadyTicketsAsync();
        executed.Should().BeTrue();

        var allTickets = store.GetAllTickets();
        allTickets.Where(t => t.Type == TicketType.Subtask).Should().OnlyContain(t => t.Status == TicketStatus.Done);
        allTickets.Where(t => t.Type == TicketType.Feature).Should().OnlyContain(t => t.Status == TicketStatus.Done);
        allTickets.Where(t => t.Type == TicketType.ResearchSpike).Should().OnlyContain(t => t.Status == TicketStatus.Done);
        allTickets.Where(t => t.Type == TicketType.Epic).Should().OnlyContain(t => t.Status == TicketStatus.Done);
    }

    [Fact]
    public async Task AgentExecutionEngine_WithLargeADRAndRemediationDirectives_ShouldPreserveFullContextWithoutTruncation()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Key", "sk-or-v1-validkey-1234567890", isActive: true);

        var store = new TicketStore();
        var eventStream = new AgentEventStream();
        var teamManager = new TeamDefinitionManager();
        var resolver = new AgentInferenceResolver(keyVault);

        // Create a large 10,000-character ADR document
        var largeAdrContent = "# ADR-099: Massive Architectural Decision Record\n" + new string('A', 8000) + "\npublic interface ICompleteService { ValueTask ExecuteAsync(); }\n";

        var archTicket = new TicketItem(
            Id: "SUB-ARCH-LARGE",
            ParentEpicId: "EPIC-LARGE",
            Title: "Architectural Decision Record",
            Description: "Large ADR document",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Full ADR"],
            Deliverables: [new ArtifactItem("SUB-ARCH-LARGE_ADR.md", largeAdrContent, "markdown", "ADR")],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        store.CreateTicket(archTicket);

        // Record a failure remediation handoff with specific critical instructions
        var remediationHandoff = HandoffPacket.Create(
            ticketId: "SUB-DEV-LARGE",
            fromRole: AgentRole.SecurityEngineer,
            toRole: AgentRole.SoftwareDeveloper,
            contextSummary: "ReDoS vulnerability detected in regex parsing routine.",
            actionRequested: "Refactor to non-backtracking GeneratedRegexAttribute.",
            remediationNotes: "MUST NOT use dynamic Regex constructor. Implement [GeneratedRegex] with 200ms timeout."
        );
        store.RecordHandoff(remediationHandoff);

        var devTicket = new TicketItem(
            Id: "SUB-DEV-LARGE",
            ParentEpicId: "EPIC-LARGE",
            Title: "Implement Rate Limiter Service",
            Description: "Implement service matching ADR",
            Type: TicketType.Subtask,
            Status: TicketStatus.Remediating,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Critical,
            DependsOnTicketIds: ["SUB-ARCH-LARGE"],
            AcceptanceCriteria: ["Compiles cleanly"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        store.CreateTicket(devTicket);

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream,
            ticketStore: store
        );

        await engine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, devTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Verify large ADR was NOT truncated with the old 2500 character limit
        userMessage.Should().Contain(new string('A', 8000));
        userMessage.Should().Contain("ICompleteService");

        // Verify critical remediation directive was injected
        userMessage.Should().Contain("🚨 CRITICAL REMEDIATION DIRECTIVE");
        userMessage.Should().Contain("MUST NOT use dynamic Regex constructor");
    }
}
