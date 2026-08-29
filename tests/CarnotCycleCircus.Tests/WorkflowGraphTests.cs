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
        var sim = new SimulatedScenarioEngine();
        var consol = new MemoryConsolidationEngine(_memoryStore);

        _executor = new GraphWorkflowExecutor(
            _ticketStore,
            decomp,
            router,
            sim,
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
    public async Task ExecuteWorkflowAsync_WithFailureSimulation_ShouldRouteToRemediatingAndRecover()
    {
        var success = await _executor.ExecuteWorkflowAsync(
            "Implement Resilient Gateway",
            "Build resilient gateway",
            triggerFailureSimulation: true
        );

        success.Should().BeTrue();
        _eventStream.GetHistory().Should().Contain(m => m.Type == MessageType.Alert && m.Content.Contains("REJECTED"));
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
        var customNode = new GraphNode("node-custom", AgentRole.SecurityEngineer, "Custom Sec Auditor", 300, 300);
        _executor.AddNode(customNode);

        _executor.CurrentGraph.Nodes.Should().Contain(n => n.Id == "node-custom");
    }

    [Fact]
    public void RemoveNode_ShouldRemoveNodeAndCascadeDeleteAttachedConnections()
    {
        var initialConnections = _executor.CurrentGraph.Connections.Count;
        _executor.CurrentGraph.Connections.Should().Contain(c => c.SourceNodeId == "node-sec" || c.TargetNodeId == "node-sec");

        _executor.RemoveNode("node-sec");

        _executor.CurrentGraph.Nodes.Should().NotContain(n => n.Id == "node-sec");
        _executor.CurrentGraph.Connections.Should().NotContain(c => c.SourceNodeId == "node-sec" || c.TargetNodeId == "node-sec");
        _executor.CurrentGraph.Connections.Count.Should().BeLessThan(initialConnections);
    }

    [Fact]
    public void ValidateConnection_ShouldValidatePortRulesAndAcyclicConstraints()
    {
        // Self-loop validation
        var selfLoop = new PortConnection("node-dev", PortType.Output, "node-dev", PortType.Input);
        _executor.ValidateConnection(selfLoop, out var errSelf).Should().BeFalse();
        errSelf.Should().Contain("itself");

        // Invalid source port (Input cannot be source)
        var invalidSource = new PortConnection("node-dev", PortType.Input, "node-qa", PortType.Input);
        _executor.ValidateConnection(invalidSource, out var errSource).Should().BeFalse();
        errSource.Should().Contain("Source port must be");

        // Invalid target port (Output cannot be target)
        var invalidTarget = new PortConnection("node-dev", PortType.Output, "node-qa", PortType.Output);
        _executor.ValidateConnection(invalidTarget, out var errTarget).Should().BeFalse();
        errTarget.Should().Contain("Target port must be");

        // Duplicate connection
        var duplicate = new PortConnection("node-tpm", PortType.Output, "node-arch", PortType.Input);
        _executor.ValidateConnection(duplicate, out var errDup).Should().BeFalse();
        errDup.Should().Contain("already exists");

        // Valid new connection
        var valid = new PortConnection("node-tpm", PortType.Output, "node-sec", PortType.Input);
        _executor.ValidateConnection(valid, out var errValid).Should().BeTrue();
        errValid.Should().BeNull();
    }

    [Fact]
    public void UpdatePolicy_ShouldMutateFailurePolicy()
    {
        var newPolicy = new FailurePolicy(MaxRetries: 7, CircuitBreakerEnabled: false, FallbackRole: AgentRole.SecurityEngineer);
        _executor.UpdatePolicy(newPolicy);

        _executor.CurrentGraph.Policy.MaxRetries.Should().Be(7);
        _executor.CurrentGraph.Policy.CircuitBreakerEnabled.Should().BeFalse();
        _executor.CurrentGraph.Policy.FallbackRole.Should().Be(AgentRole.SecurityEngineer);
    }

    [Theory]
    [InlineData("preset-rapid", 3)]
    [InlineData("preset-zero-trust", 5)]
    [InlineData("preset-performance", 4)]
    [InlineData("preset-standard", 8)]
    public void LoadPreset_ShouldConfigureCorrectGraphTopology(string presetId, int expectedNodeCount)
    {
        _executor.LoadPreset(presetId);

        _executor.CurrentGraph.Nodes.Should().HaveCount(expectedNodeCount);
        _executor.CurrentGraph.Connections.Should().NotBeEmpty();
    }
}
