using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Blueprints;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ProjectBlueprintServiceTests
{
    private readonly TicketStore _ticketStore = new();
    private readonly WorkDecompositionEngine _decompositionEngine;
    private readonly AdrDocumentManager _adrManager = new();
    private readonly KnowledgeMapService _knowledgeMap = new();
    private readonly EmbeddedVectorMemoryStore _memoryStore = new();
    private readonly TeamDefinitionManager _teamManager = new();
    private readonly AgentEventStream _eventStream = new();
    private readonly ProjectBlueprintService _blueprintService;

    public ProjectBlueprintServiceTests()
    {
        _decompositionEngine = new WorkDecompositionEngine(_ticketStore);
        _blueprintService = new ProjectBlueprintService(
            _decompositionEngine,
            _ticketStore,
            _adrManager,
            _knowledgeMap,
            _memoryStore,
            _teamManager,
            _eventStream
        );
    }

    [Fact]
    public void GetAvailableBlueprints_ShouldReturnCuratedList()
    {
        var blueprints = _blueprintService.GetAvailableBlueprints();
        blueprints.Should().NotBeEmpty();
        blueprints.Count.Should().BeGreaterThanOrEqualTo(5);

        var iotBp = _blueprintService.GetBlueprint("iot-ingestion-pipeline");
        iotBp.Should().NotBeNull();
        iotBp!.RecommendedArchetype.Should().Be("HighPerformance");
        iotBp.KeyPatterns.Should().NotBeEmpty();
        iotBp.SecurityRules.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LaunchBlueprintAsync_ShouldCreateTicketsAdrKnowledgeAndSwitchTeam()
    {
        var result = await _blueprintService.LaunchBlueprintAsync("ecommerce-checkout-saga");

        result.Should().NotBeNull();
        result.EpicId.Should().StartWith("EPIC-");
        result.AdrId.Should().StartWith("ADR-");
        result.CreatedTickets.Count.Should().BeGreaterThanOrEqualTo(6);

        // Verify Epic ticket exists in store
        var epicTicket = _ticketStore.GetTicketById(result.EpicId);
        epicTicket.Should().NotBeNull();
        epicTicket!.AssigneeRole.Should().Be(AgentRole.TechnicalProductManager);

        // Verify ADR exists in manager
        var adr = _adrManager.GetAdr(result.AdrId);
        adr.Should().NotBeNull();
        adr!.Title.Should().Contain("Resilient E-Commerce");

        // Verify Knowledge Map has new nodes
        var map = _knowledgeMap.GetFullMap();
        map.Nodes.Should().Contain(n => n.Attributes.ContainsKey("Project") && n.Attributes["Project"].Contains("E-Commerce"));

        // Verify Active Team Archetype switched
        var currentTeam = _teamManager.GetCurrentTeam();
        currentTeam.Should().NotBeNull();
    }

    [Fact]
    public async Task LaunchCustomProjectAsync_ShouldGenerateCustomTicketsAndAdrs()
    {
        var result = await _blueprintService.LaunchCustomProjectAsync(
            projectTitle: "Real-time Auction Microservice",
            projectDescription: "Bidding engine with sub-millisecond locks and SignalR feeds",
            targetStack: ".NET 10 / C# 13, Redis Streams, SignalR",
            archetypeName: "HighPerformance"
        );

        result.Should().NotBeNull();
        result.EpicId.Should().StartWith("EPIC-");
        result.AdrId.Should().StartWith("ADR-");
        result.CreatedTickets.Should().NotBeEmpty();

        var epic = _ticketStore.GetTicketById(result.EpicId);
        epic.Should().NotBeNull();
        epic!.Title.Should().Be("Real-time Auction Microservice");

        var adr = _adrManager.GetAdr(result.AdrId);
        adr.Should().NotBeNull();
        adr!.Title.Should().Contain("Real-time Auction Microservice");
    }
}
