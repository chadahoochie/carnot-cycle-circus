using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Showcase;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ShowcaseDemoServiceTests
{
    private readonly TicketStore _ticketStore = new();
    private readonly WorkDecompositionEngine _decompositionEngine;
    private readonly HandoffRouter _handoffRouter;
    private readonly SimulatedScenarioEngine _scenarioEngine = new();
    private readonly AgentEventStream _eventStream = new();
    private readonly EmbeddedVectorMemoryStore _memoryStore = new();
    private readonly MemoryConsolidationEngine _memoryConsolidation;
    private readonly GraphWorkflowExecutor _workflowExecutor;
    private readonly ShowcaseDemoService _showcaseService;

    public ShowcaseDemoServiceTests()
    {
        _decompositionEngine = new WorkDecompositionEngine(_ticketStore);
        _handoffRouter = new HandoffRouter(_ticketStore, _eventStream);
        _memoryConsolidation = new MemoryConsolidationEngine(_memoryStore);
        _workflowExecutor = new GraphWorkflowExecutor(
            _ticketStore,
            _decompositionEngine,
            _handoffRouter,
            _scenarioEngine,
            _eventStream,
            _memoryConsolidation
        );
        _showcaseService = new ShowcaseDemoService(_workflowExecutor, _eventStream);
    }

    [Fact]
    public void GetScenarios_ShouldReturnCuratedScenarios()
    {
        var scenarios = _showcaseService.GetScenarios();
        scenarios.Should().NotBeEmpty();
        scenarios.Count.Should().BeGreaterThanOrEqualTo(3);

        var fullCircus = _showcaseService.GetScenario("full-circus-sprint");
        fullCircus.Should().NotBeNull();
        fullCircus!.HighlightPersona.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunShowcaseAsync_ShouldExecuteWorkflowEndToEnd()
    {
        var success = await _showcaseService.RunShowcaseAsync("full-circus-sprint");
        success.Should().BeTrue();

        // Verify tickets were created and processed
        var allTickets = _ticketStore.GetAllTickets();
        allTickets.Should().NotBeEmpty();
    }
}
