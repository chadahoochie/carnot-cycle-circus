using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class TicketStoreTests
{
    private readonly TicketStore _store = new();

    [Fact]
    public void CreateTicket_ShouldStoreAndRetrieveTicket()
    {
        var ticket = new TicketItem(
            Id: "TCK-001",
            ParentEpicId: null,
            Title: "Test Feature",
            Description: "Feature Description",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Criteria 1"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        _store.CreateTicket(ticket);
        var retrieved = _store.GetTicketById("TCK-001");

        retrieved.ShouldNotBeNull();
        retrieved!.Title.ShouldBe("Test Feature");
        retrieved.Status.ShouldBe(TicketStatus.Ready);
    }

    [Fact]
    public void AreDependenciesSatisfied_ShouldReturnFalse_WhenDependencyNotDone()
    {
        var dep = new TicketItem(
            Id: "DEP-001",
            ParentEpicId: null,
            Title: "Prerequisite Architecture",
            Description: "Arch ADR",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR complete"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _store.CreateTicket(dep);

        var dependent = new TicketItem(
            Id: "DEV-001",
            ParentEpicId: null,
            Title: "Implementation",
            Description: "Dev task",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: ["DEP-001"],
            AcceptanceCriteria: ["Tests pass"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _store.CreateTicket(dependent);

        _store.AreDependenciesSatisfied("DEV-001").ShouldBeFalse();

        // Complete dependency
        _store.UpdateTicket(dep.WithStatus(TicketStatus.Done));
        _store.AreDependenciesSatisfied("DEV-001").ShouldBeTrue();
    }

    [Fact]
    public void RecordHandoff_ShouldStoreAndRetrieveHandoff()
    {
        var handoff = HandoffPacket.Create(
            ticketId: "TCK-100",
            fromRole: AgentRole.LeadArchitect,
            toRole: AgentRole.SoftwareDeveloper,
            contextSummary: "ADR approved",
            actionRequested: "Begin C# implementation"
        );

        _store.RecordHandoff(handoff);
        var handoffs = _store.GetHandoffsForTicket("TCK-100");

        handoffs.ShouldHaveSingleItem();
        handoffs[0].FromAgentRole.ShouldBe(AgentRole.LeadArchitect);
        handoffs[0].ToAgentRole.ShouldBe(AgentRole.SoftwareDeveloper);
    }
}
