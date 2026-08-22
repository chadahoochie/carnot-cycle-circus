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
    public void DefaultGraph_ShouldContainAllSixRolesAndFailurePorts()
    {
        var graph = _executor.CurrentGraph;

        graph.Nodes.Should().HaveCount(6);
        graph.Nodes.Select(n => n.Role).Should().Contain([
            AgentRole.TechnicalProductManager,
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            AgentRole.OptimizationEngineer,
            AgentRole.PrincipalQAAnalyst
        ]);

        // Verify Failure Ports exist on Security and QA
        graph.Connections.Should().Contain(c => c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");
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
}
