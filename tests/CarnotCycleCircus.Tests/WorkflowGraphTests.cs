using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class WorkflowGraphTests
{
    private readonly TicketStore _ticketStore = new();
    private readonly AgentEventStream _eventStream = new();
    private readonly EmbeddedVectorMemoryStore _memoryStore = new();
    private readonly GraphWorkflowExecutor _executor;

    public WorkflowGraphTests()
    {
        var decomp = new WorkDecompositionEngine(_ticketStore);
        var router = new HandoffRouter(_ticketStore, _eventStream);
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: _ticketStore);
        var consol = new MemoryConsolidationEngine(_memoryStore);

        _executor = new GraphWorkflowExecutor(
            _ticketStore,
            decomp,
            router,
            executionEngine,
            _eventStream,
            consol
        );
    }

    [Fact]
    public void DefaultGraph_ShouldContainAllEightRolesAndFailurePorts()
    {
        var graph = _executor.CurrentGraph;

        graph.Nodes.Count().ShouldBe(8);
        new[]
        {
            AgentRole.RequirementsResearcher,
            AgentRole.TechnicalProductManager,
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            AgentRole.OptimizationEngineer,
            AgentRole.PrincipalQAAnalyst,
            AgentRole.IntegrationEngineer
        }.ShouldBeSubsetOf(graph.Nodes.Select(n => n.Role));

        // Verify Connections: Res -> TPM -> Arch
        graph.Connections.ShouldContain(c => c.SourceNodeId == "node-res" && c.SourcePort == PortType.Output && c.TargetNodeId == "node-tpm");
        graph.Connections.ShouldContain(c => c.SourceNodeId == "node-tpm" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-res");

        // Verify Failure Ports exist on Security, QA, and Integration (including QA/Int -> Arch failure cables)
        graph.Connections.ShouldContain(c => c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");
        graph.Connections.ShouldContain(c => c.SourceNodeId == "node-qa" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-arch");
        graph.Connections.ShouldContain(c => c.SourceNodeId == "node-int" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");
        graph.Connections.ShouldContain(c => c.SourceNodeId == "node-int" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-arch");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ShouldCompleteAllPhasesAndProduceDeliverables()
    {
        var success = await _executor.ExecuteWorkflowAsync(
            "Implement Memory Store",
            "Build multi-tier memory store with vector similarity search"
        );

        success.ShouldBeTrue();
        _executor.CurrentGraph.Nodes.ShouldAllBe(n => n.State == NodeExecutionState.Completed);

        var tickets = _ticketStore.GetAllTickets();
        tickets.ShouldNotBeEmpty();
        tickets.Where(t => t.Type == TicketType.Subtask).ShouldAllBe(t => t.Status == TicketStatus.Done);
        tickets.Sum(t => t.Deliverables.Count).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ShouldCollaborateDiscoveryAndRefineTicketsBeforeAdr()
    {
        var success = await _executor.ExecuteWorkflowAsync(
            "High-Throughput Vector Index",
            "Build high-throughput HNSW vector index with zero GC allocations."
        );

        success.ShouldBeTrue();

        var tickets = _ticketStore.GetAllTickets();
        var epic = tickets.FirstOrDefault(t => t.Type == TicketType.Epic);
        epic.ShouldNotBeNull();
        epic!.Status.ShouldBe(TicketStatus.Done);

        // Verify that Feature stories were refined into 6 subtasks
        var stories = tickets.Where(t => t.Type == TicketType.Feature).ToList();
        stories.ShouldNotBeEmpty();
        stories.ShouldAllBe(s => s.Status == TicketStatus.Done);

        var subtasks = tickets.Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.Count().ShouldBe(6);
        subtasks.ShouldAllBe(s => s.Status == TicketStatus.Done);

        // Verify Handoffs trace the collaborative discovery & refinement lifecycle
        var handoffs = _ticketStore.GetAllHandoffs();
        handoffs.ShouldContain(h => h.FromAgentRole == AgentRole.RequirementsResearcher && h.ToAgentRole == AgentRole.TechnicalProductManager);
        handoffs.ShouldContain(h => h.FromAgentRole == AgentRole.TechnicalProductManager && h.ToAgentRole == AgentRole.LeadArchitect);
        handoffs.ShouldContain(h => h.FromAgentRole == AgentRole.LeadArchitect && h.ToAgentRole == AgentRole.SoftwareDeveloper);

        // Verify deliverables attached: Research Brief, PRD, ADR, Code, STRIDE, Benchmark, QA Scorecard, Release Manifest
        var deliverables = tickets.SelectMany(t => t.Deliverables).ToList();
        deliverables.ShouldContain(d => d.Name.EndsWith("_RESEARCH_BRIEF.md"));
        deliverables.ShouldContain(d => d.Name.EndsWith("_PRD.md"));
        deliverables.ShouldContain(d => d.Name.EndsWith("_ADR.md"));
        deliverables.ShouldContain(d => d.ContentType == "csharp");
        deliverables.ShouldContain(d => d.Name.EndsWith("_STRIDE_Model.md"));
        deliverables.ShouldContain(d => d.Name.EndsWith("_Perf_Profile.md"));
        deliverables.ShouldContain(d => d.Name.EndsWith("_QA_Scorecard.md"));
        deliverables.ShouldContain(d => d.Name.EndsWith("_Release_Manifest.md"));
    }

    [Fact]
    public void UpdateNodePosition_ShouldUpdateCoordinates()
    {
        _executor.UpdateNodePosition("node-dev", 520, 280);

        var devNode = _executor.CurrentGraph.Nodes.First(n => n.Id == "node-dev");
        devNode.X.ShouldBe(520);
        devNode.Y.ShouldBe(280);
    }

    [Fact]
    public void AddNode_ShouldAppendNodeToGraph()
    {
        var customNode = new GraphNode(
            Id: "node-custom",
            Role: AgentRole.SoftwareDeveloper,
            Name: "Junior Dev",
            X: 100,
            Y: 100
        );

        _executor.AddNode(customNode);

        _executor.CurrentGraph.Nodes.ShouldContain(n => n.Id == "node-custom");
    }

    [Fact]
    public void RemoveNode_ShouldRemoveNodeAndConnectedEdges()
    {
        _executor.RemoveNode("node-dev");

        _executor.CurrentGraph.Nodes.ShouldNotContain(n => n.Id == "node-dev");
        _executor.CurrentGraph.Connections.ShouldNotContain(c => c.SourceNodeId == "node-dev" || c.TargetNodeId == "node-dev");
    }

    [Fact]
    public void AddConnection_ValidConnection_ShouldAddEdge()
    {
        var customNode = new GraphNode(
            Id: "node-custom",
            Role: AgentRole.SoftwareDeveloper,
            Name: "Custom Node",
            X: 100,
            Y: 100
        );
        _executor.AddNode(customNode);

        var conn = new PortConnection("node-arch", PortType.Output, "node-custom", PortType.Input);
        _executor.AddConnection(conn);

        _executor.CurrentGraph.Connections.ShouldContain(c => c.SourceNodeId == "node-arch" && c.TargetNodeId == "node-custom");
    }

    [Fact]
    public void ValidateConnection_SelfLoop_ShouldFail()
    {
        var conn = new PortConnection("node-dev", PortType.Output, "node-dev", PortType.Input);
        var valid = _executor.ValidateConnection(conn, out var error);

        valid.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("itself");
    }

    [Fact]
    public void LoadPreset_Rapid_ShouldLoadRapidPreset()
    {
        _executor.LoadPreset("rapid");

        _executor.CurrentGraph.Name.ShouldBe("Rapid Prototype Fast-Track Graph");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WhenNoApiKey_ShouldReturnFalseGracefullyWithoutLockup()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var mockOpenRouter = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService(); // Empty vault
        var resolver = new AgentInferenceResolver(keyVault);
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore, eventStream: eventStream);
        var decomp = new WorkDecompositionEngine(ticketStore);
        var router = new HandoffRouter(ticketStore, eventStream);
        var consol = new MemoryConsolidationEngine(new EmbeddedVectorMemoryStore());

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decomp,
            router,
            executionEngine,
            eventStream,
            consol
        );

        var result = await executor.ExecuteWorkflowAsync("Greenfield Initiative", "Build without key");

        result.ShouldBeFalse();
        executor.IsRunning.ShouldBeFalse();
        executor.CurrentGraph.Nodes.ShouldNotContain(n => n.State == NodeExecutionState.Running);
        eventStream.GetHistory().ShouldContain(m => m.Type == MessageType.Alert && m.Content.Contains("No active OpenRouter API key"));
    }

    [Fact]
    public async Task ExecuteTicketAsync_WhenNoApiKey_ShouldReturnFalseAndMarkTicketBlocked()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var mockOpenRouter = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService(); // Empty vault
        var resolver = new AgentInferenceResolver(keyVault);
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore, eventStream: eventStream);
        var decomp = new WorkDecompositionEngine(ticketStore);
        var router = new HandoffRouter(ticketStore, eventStream);
        var consol = new MemoryConsolidationEngine(new EmbeddedVectorMemoryStore());

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decomp,
            router,
            executionEngine,
            eventStream,
            consol
        );

        var ticket = new TicketItem(
            Id: "TCK-FAIL-01",
            ParentEpicId: null,
            Title: "Task Without Key",
            Description: "Cannot execute without key",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Criteria"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(ticket);

        var result = await executor.ExecuteTicketAsync(ticket.Id);

        result.ShouldBeFalse();
        var updated = ticketStore.GetTicketById(ticket.Id);
        updated!.Status.ShouldBe(TicketStatus.Blocked);
        executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.SoftwareDeveloper).State.ShouldBe(NodeExecutionState.Failed);
    }

    [Fact]
    public async Task ExecuteReadyTicketsAsync_WhenNoApiKey_ShouldHaltLoopAndReturnFalse()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var mockOpenRouter = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService(); // Empty vault
        var resolver = new AgentInferenceResolver(keyVault);
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore, eventStream: eventStream);
        var decomp = new WorkDecompositionEngine(ticketStore);
        var router = new HandoffRouter(ticketStore, eventStream);
        var consol = new MemoryConsolidationEngine(new EmbeddedVectorMemoryStore());

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decomp,
            router,
            executionEngine,
            eventStream,
            consol
        );

        var ticket = new TicketItem(
            Id: "TCK-READY-NOKEY",
            ParentEpicId: null,
            Title: "Ready Task",
            Description: "Desc",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Criteria"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(ticket);

        var result = await executor.ExecuteReadyTicketsAsync();

        result.ShouldBeFalse();
        executor.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithExistingEpicWithoutResearchBrief_ShouldExecuteDiscoveryAndProduceBriefAndPrd()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore, eventStream: eventStream);
        var decomp = new WorkDecompositionEngine(ticketStore);
        var router = new HandoffRouter(ticketStore, eventStream);
        var consol = new MemoryConsolidationEngine(new EmbeddedVectorMemoryStore());

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decomp,
            router,
            executionEngine,
            eventStream,
            consol
        );

        // Pre-create an Epic without research brief or PRD (e.g. from Project Ignition)
        var preEpic = new TicketItem(
            Id: "EPIC-IGNITE-01",
            ParentEpicId: null,
            Title: "Real-time Telemetry Pipeline",
            Description: "Zero-allocation reactive pipeline with bounded channels",
            Type: TicketType.Epic,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["All user stories verified"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(preEpic);

        var success = await executor.ExecuteWorkflowAsync(preEpic.Title, preEpic.Description);

        success.ShouldBeTrue();
        executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.RequirementsResearcher).State.ShouldBe(NodeExecutionState.Completed);
        executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.TechnicalProductManager).State.ShouldBe(NodeExecutionState.Completed);

        var allTickets = ticketStore.GetAllTickets();
        var allDeliverables = allTickets.SelectMany(t => t.Deliverables).ToList();
        allDeliverables.ShouldContain(d => d.Name.EndsWith("_RESEARCH_BRIEF.md"));
        allDeliverables.ShouldContain(d => d.Name.EndsWith("_PRD.md"));
        allDeliverables.ShouldContain(d => d.Name.EndsWith("_ADR.md"));
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithMultiStoryDecomposition_ShouldDrainAllSubtasksAcrossAllStoriesToCompletion()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var decompEngine = new WorkDecompositionEngine(ticketStore);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);

        var mockOpenRouter = new MockOpenRouterClient();
        mockOpenRouter.ResponseFactory = req =>
        {
            var content = req.Messages.LastOrDefault()?.Content ?? "";
            if (content.Contains("Product Requirements Document (PRD)"))
            {
                return """
                # Product Requirements Document (PRD): Multi-Feature System
                ## 1. Executive Summary
                Synthesized brief.
                ## 4. User Stories & Functional Acceptance Criteria
                ```json:user_stories
                [
                  {
                    "title": "Ingestion Channel",
                    "description": "Channel ingestion pipeline",
                    "acceptanceCriteria": ["Criterion 1"]
                  },
                  {
                    "title": "Storage Layer",
                    "description": "Outbox storage engine",
                    "acceptanceCriteria": ["Criterion 2"]
                  }
                ]
                ```
                """;
            }
            return "# Deliverable\n```csharp\npublic class SystemArtifact { }\n```";
        };

        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompEngine,
            handoffRouter,
            executionEngine,
            eventStream,
            consolidationEngine
        );

        var success = await executor.ExecuteWorkflowAsync("Multi-Feature System", "System with multiple stories");

        success.ShouldBeTrue();

        var epic = ticketStore.GetAllTickets().First(t => t.Type == TicketType.Epic);
        epic.Status.ShouldBe(TicketStatus.Done);

        var stories = ticketStore.GetTicketsByEpic(epic.Id).Where(t => t.Type == TicketType.Feature).ToList();
        stories.Count().ShouldBe(2);
        stories.ShouldAllBe(s => s.Status == TicketStatus.Done);

        var subtasks = ticketStore.GetTicketsByEpic(epic.Id).Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.Count().ShouldBe(12); // 6 subtasks per story * 2 stories
        subtasks.ShouldAllBe(s => s.Status == TicketStatus.Done);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WhenReRunningExistingCompletedEpic_ShouldReactivateAndStartAtResearchAndTpm()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var decompEngine = new WorkDecompositionEngine(ticketStore);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);

        var executedRoles = new List<AgentRole>();
        var mockOpenRouter = new MockOpenRouterClient();
        mockOpenRouter.ResponseFactory = req =>
        {
            var sys = req.Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
            if (sys.Contains("Requirements Researcher"))
            {
                executedRoles.Add(AgentRole.RequirementsResearcher);
                return "# Requirements Research Brief\nDomain specifications verified.";
            }
            if (sys.Contains("Technical Product Manager"))
            {
                executedRoles.Add(AgentRole.TechnicalProductManager);
                return "# Product Requirements Document (PRD)\nPRD specifications.";
            }
            return "# Deliverable\n```csharp\npublic class Impl { }\n```";
        };

        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompEngine,
            handoffRouter,
            executionEngine,
            eventStream,
            consolidationEngine
        );

        // Pre-create an Epic that was previously marked Done with existing subtasks
        var existingEpic = new TicketItem(
            Id: "EPIC-DONE-01",
            ParentEpicId: null,
            Title: "Existing Pipeline",
            Description: "A pipeline previously completed",
            Type: TicketType.Epic,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Old AC"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1)
        );
        ticketStore.CreateTicket(existingEpic);

        // Also pre-create an old done story and research ticket
        var oldRes = new TicketItem(
            Id: "RES-OLD-01",
            ParentEpicId: existingEpic.Id,
            Title: "Requirements Research: Existing Pipeline",
            Description: "Old research",
            Type: TicketType.ResearchSpike,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.RequirementsResearcher,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["AC"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1)
        );
        ticketStore.CreateTicket(oldRes);

        var oldStory = new TicketItem(
            Id: "STORY-OLD-01",
            ParentEpicId: existingEpic.Id,
            Title: "Existing Pipeline: Core Engine",
            Description: "Old story",
            Type: TicketType.Feature,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: [oldRes.Id],
            AcceptanceCriteria: ["AC"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1)
        );
        ticketStore.CreateTicket(oldStory);

        var success = await executor.ExecuteWorkflowAsync("Existing Pipeline", "A pipeline previously completed");

        success.ShouldBeTrue();
        executedRoles.ShouldContain(AgentRole.RequirementsResearcher);
        executedRoles.ShouldContain(AgentRole.TechnicalProductManager);
    }

    [Fact]
    public void WorkDecompositionEngine_DeconstructEpic_WhenReDeconstructing_ShouldKeepOnlyResearchReadyAndSubtasksInBacklog()
    {
        var store = new TicketStore();
        var decomp = new WorkDecompositionEngine(store);

        // First deconstruct
        var tickets = decomp.DeconstructEpic("Resilient Ingestion", "Ingestion pipeline");
        var readyInitial = store.GetReadyTickets();
        readyInitial.Where(t => t.AssigneeRole == AgentRole.RequirementsResearcher).ShouldHaveSingleItem();

        // Second deconstruct (e.g. user re-ignites or triggers)
        var ticketsPass2 = decomp.DeconstructEpic("Resilient Ingestion", "Ingestion pipeline");
        var readyPass2 = store.GetReadyTickets();

        // Must STILL only have RequirementsResearcher ready! Architect must NOT be ready!
        readyPass2.Where(t => t.AssigneeRole == AgentRole.RequirementsResearcher).ShouldHaveSingleItem();
    }
}
