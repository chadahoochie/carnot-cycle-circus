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

        var pipelineBp = _blueprintService.GetBlueprint("realtime-telemetry-pipeline");
        pipelineBp.Should().NotBeNull();
        pipelineBp!.RecommendedTeamId.Should().Be("team-high-performance");
        pipelineBp.KeyPatterns.Should().NotBeEmpty();
        pipelineBp.SecurityRules.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LaunchBlueprintAsync_ShouldCreateTicketsAdrKnowledgeAndSwitchTeam()
    {
        var result = await _blueprintService.LaunchBlueprintAsync("distributed-order-saga");

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
        adr!.Title.Should().Contain("Order & Payment Saga");

        // Verify Knowledge Map has new nodes
        var map = _knowledgeMap.GetFullMap();
        map.Nodes.Should().Contain(n => n.Attributes.ContainsKey("Project") && n.Attributes["Project"].Contains("Order & Payment Saga"));

        // Verify Active Team switched
        var currentTeam = _teamManager.GetCurrentTeam();
        currentTeam.Should().NotBeNull();
    }

    [Fact]
    public async Task LaunchProjectAsync_WithDynamicRequest_ShouldGenerateCustomTicketsAndAdrs()
    {
        var result = await _blueprintService.LaunchProjectAsync(new ProjectIgnitionRequest(
            Title: "Real-time Auction Microservice",
            Description: "Bidding engine with sub-millisecond locks and SignalR feeds",
            TargetStack: ".NET 10 / C# 13, Redis Streams, SignalR",
            TeamId: "team-high-performance",
            KeyGoals: ["Sub-millisecond bid execution", "Zero allocation hot path"],
            ArchitecturePatterns: ["Channels bounded queue", "Immutable state records"],
            SecurityGuardrails: ["HMAC signature check"]
        ));

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

        var map = _knowledgeMap.GetFullMap();
        map.Nodes.Should().Contain(n => n.Attributes.ContainsKey("Stack") && n.Attributes["Stack"].Contains("Redis Streams"));
    }

    [Fact]
    public async Task LaunchCustomProjectAsync_ShouldGenerateCustomTicketsAndAdrs()
    {
        var result = await _blueprintService.LaunchCustomProjectAsync(
            projectTitle: "Real-time Auction Microservice",
            projectDescription: "Bidding engine with sub-millisecond locks and SignalR feeds",
            targetStack: ".NET 10 / C# 13, Redis Streams, SignalR",
            teamId: "team-high-performance"
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

    [Fact]
    public async Task LaunchProjectAsync_WithExplicitTeamId_ShouldKeepDefinedTeamWithoutResettingCustomModels()
    {
        // Setup a custom defined team with custom model on Software Developer
        var customTeam = _teamManager.CreateTeam("Alpha Dev Squad", "Specialized Squad", "team-balanced");
        var devMember = customTeam.Members.First(m => m.Persona.Role == AgentRole.SoftwareDeveloper);
        var customizedDev = devMember with
        {
            OverrideModel = "anthropic/claude-3.5-sonnet",
            Persona = devMember.Persona with { SystemPrompt = "Custom Strict Zero-Allocation Prompt" }
        };
        _teamManager.UpdateMemberInCurrentTeam(customizedDev);

        // Verify team has the custom model
        _teamManager.GetCurrentTeam().Members.First(m => m.Persona.Role == AgentRole.SoftwareDeveloper)
            .EffectiveModel.Should().Be("anthropic/claude-3.5-sonnet");

        // Launch project with this explicit defined TeamId
        var result = await _blueprintService.LaunchProjectAsync(new ProjectIgnitionRequest(
            Title: "Custom Swarm Initiative",
            Description: "Initiative with custom team",
            TargetStack: ".NET 10",
            TeamId: customTeam.Id
        ));

        result.Should().NotBeNull();

        // Verify active team is still the customized squad and dev still has custom model
        var activeTeam = _teamManager.GetCurrentTeam();
        activeTeam.Id.Should().Be(customTeam.Id);
        activeTeam.Members.First(m => m.Persona.Role == AgentRole.SoftwareDeveloper)
            .EffectiveModel.Should().Be("anthropic/claude-3.5-sonnet");
        activeTeam.Members.First(m => m.Persona.Role == AgentRole.SoftwareDeveloper)
            .Persona.SystemPrompt.Should().Be("Custom Strict Zero-Allocation Prompt");
    }
}
