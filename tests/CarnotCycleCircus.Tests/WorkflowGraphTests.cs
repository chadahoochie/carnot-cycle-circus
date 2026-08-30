using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
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

        graph.Nodes.Should().HaveCount(8);
        graph.Nodes.Select(n => n.Role).Should().Contain([
            AgentRole.RequirementsResearcher,
            AgentRole.TechnicalProductManager,
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            AgentRole.OptimizationEngineer,
            AgentRole.PrincipalQAAnalyst,
            AgentRole.IntegrationEngineer
        ]);

        // Verify Connections: Res -> TPM -> Arch
        graph.Connections.Should().Contain(c => c.SourceNodeId == "node-res" && c.SourcePort == PortType.Output && c.TargetNodeId == "node-tpm");
        graph.Connections.Should().Contain(c => c.SourceNodeId == "node-tpm" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-res");

        // Verify Failure Ports exist on Security, QA, and Integration (including QA/Int -> Arch failure cables)
        graph.Connections.Should().Contain(c => c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");
        graph.Connections.Should().Contain(c => c.SourceNodeId == "node-qa" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-arch");
        graph.Connections.Should().Contain(c => c.SourceNodeId == "node-int" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");
        graph.Connections.Should().Contain(c => c.SourceNodeId == "node-int" && c.SourcePort == PortType.Failure && c.TargetNodeId == "node-arch");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ShouldCompleteAllPhasesAndProduceDeliverables()
    {
        var success = await _executor.ExecuteWorkflowAsync(
            "Implement Memory Store",
            "Build multi-tier memory store with vector similarity search"
        );

        success.Should().BeTrue();
        _executor.CurrentGraph.Nodes.Should().OnlyContain(n => n.State == NodeExecutionState.Completed);

        var tickets = _ticketStore.GetAllTickets();
        tickets.Should().NotBeEmpty();
        tickets.Where(t => t.Type == TicketType.Subtask).Should().OnlyContain(t => t.Status == TicketStatus.Done);
        tickets.Sum(t => t.Deliverables.Count).Should().BeGreaterThan(0);
    }

    [Fact]
    public void UpdateNodePosition_ShouldUpdateCoordinates()
    {
        _executor.UpdateNodePosition("node-dev", 520, 280);

        var devNode = _executor.CurrentGraph.Nodes.First(n => n.Id == "node-dev");
        devNode.X.Should().Be(520);
        devNode.Y.Should().Be(280);
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

        _executor.CurrentGraph.Nodes.Should().Contain(n => n.Id == "node-custom");
    }

    [Fact]
    public void RemoveNode_ShouldRemoveNodeAndConnectedEdges()
    {
        _executor.RemoveNode("node-dev");

        _executor.CurrentGraph.Nodes.Should().NotContain(n => n.Id == "node-dev");
        _executor.CurrentGraph.Connections.Should().NotContain(c => c.SourceNodeId == "node-dev" || c.TargetNodeId == "node-dev");
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

        _executor.CurrentGraph.Connections.Should().Contain(c => c.SourceNodeId == "node-arch" && c.TargetNodeId == "node-custom");
    }

    [Fact]
    public void ValidateConnection_SelfLoop_ShouldFail()
    {
        var conn = new PortConnection("node-dev", PortType.Output, "node-dev", PortType.Input);
        var valid = _executor.ValidateConnection(conn, out var error);

        valid.Should().BeFalse();
        error.Should().Contain("itself");
    }

    [Fact]
    public void LoadPreset_Rapid_ShouldLoadRapidPreset()
    {
        _executor.LoadPreset("rapid");

        _executor.CurrentGraph.Name.Should().Be("Rapid Prototype Fast-Track Graph");
    }
}
