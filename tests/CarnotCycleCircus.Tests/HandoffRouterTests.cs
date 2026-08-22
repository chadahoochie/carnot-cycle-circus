using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class HandoffRouterTests
{
    private readonly TicketStore _ticketStore = new();
    private readonly AgentEventStream _eventStream = new();
    private readonly HandoffRouter _router;

    public HandoffRouterTests()
    {
        _router = new HandoffRouter(_ticketStore, _eventStream);
    }

    [Fact]
    public void RouteSuccessHandoff_ShouldRecordHandoffAndEmitMessage()
    {
        var packet = _router.RouteSuccessHandoff(
            "TCK-200",
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            "ADR ready",
            "Implement feature"
        );

        packet.Should().NotBeNull();
        packet.TicketId.Should().Be("TCK-200");
        packet.FromAgentRole.Should().Be(AgentRole.LeadArchitect);
        packet.ToAgentRole.Should().Be(AgentRole.SoftwareDeveloper);

        _eventStream.GetHistory().Should().ContainSingle(m => m.Type == MessageType.Handoff);
    }

    [Fact]
    public void RouteFailureRemediation_ShouldUpdateStatusToRemediatingAndEmitAlert()
    {
        var ticket = new TicketItem(
            Id: "TCK-FAIL",
            ParentEpicId: null,
            Title: "Security Audit",
            Description: "Audit",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.SecurityEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean scan"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(ticket);

        var packet = _router.RouteFailureRemediation(
            "TCK-FAIL",
            AgentRole.SecurityEngineer,
            AgentRole.SoftwareDeveloper,
            "Hardcoded API secret found",
            "Remove secret and use environment variables"
        );

        packet.RemediationNotes.Should().Contain("environment variables");
        var updated = _ticketStore.GetTicketById("TCK-FAIL");
        updated!.Status.Should().Be(TicketStatus.Remediating);
        updated.AssigneeRole.Should().Be(AgentRole.SoftwareDeveloper);

        _eventStream.GetHistory().Should().Contain(m => m.Type == MessageType.Alert);
    }

    [Fact]
    public void AdvanceWorkflowOnTicketCompletion_ShouldActivateDependentTickets()
    {
        var task1 = new TicketItem(
            Id: "T1",
            ParentEpicId: null,
            Title: "Task 1",
            Description: "First task",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Medium,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Done"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(task1);

        var task2 = new TicketItem(
            Id: "T2",
            ParentEpicId: null,
            Title: "Task 2",
            Description: "Second task",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Medium,
            DependsOnTicketIds: ["T1"],
            AcceptanceCriteria: ["Done"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(task2);

        var activated = _router.AdvanceWorkflowOnTicketCompletion("T1");

        activated.Should().ContainSingle();
        activated[0].Id.Should().Be("T2");
        activated[0].Status.Should().Be(TicketStatus.Ready);
    }
}
