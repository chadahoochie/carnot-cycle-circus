using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;

namespace CarnotCycleCircus.Core.Domain.Tickets;

public interface IHandoffRouter
{
    HandoffPacket RouteSuccessHandoff(
        string ticketId,
        AgentRole fromRole,
        AgentRole toRole,
        string summary,
        string actionRequested,
        IReadOnlyList<ArtifactItem>? deliverables = null);

    HandoffPacket RouteFailureRemediation(
        string ticketId,
        AgentRole rejectingRole,
        AgentRole remediationRole,
        string rejectionReason,
        string remediationInstructions);

    IReadOnlyList<TicketItem> AdvanceWorkflowOnTicketCompletion(string completedTicketId);
}

public class HandoffRouter : IHandoffRouter
{
    private readonly ITicketStore _ticketStore;
    private readonly IAgentEventStream _eventStream;

    public HandoffRouter(ITicketStore ticketStore, IAgentEventStream eventStream)
    {
        _ticketStore = ticketStore;
        _eventStream = eventStream;
    }

    public HandoffPacket RouteSuccessHandoff(
        string ticketId,
        AgentRole fromRole,
        AgentRole toRole,
        string summary,
        string actionRequested,
        IReadOnlyList<ArtifactItem>? deliverables = null)
    {
        var packet = HandoffPacket.Create(
            ticketId: ticketId,
            fromRole: fromRole,
            toRole: toRole,
            contextSummary: summary,
            actionRequested: actionRequested,
            artifacts: deliverables,
            reviewChecklist: ["Verify acceptance criteria", "Validate domain boundaries", "Ensure non-breaking contracts"]
        );

        _ticketStore.RecordHandoff(packet);

        _eventStream.Publish(AgentMessage.Create(
            role: fromRole,
            senderName: fromRole.ToDisplayName(),
            content: $"📦 Handoff packet [{packet.HandoffId}] dispatched to {toRole.ToDisplayName()}: {summary}",
            type: MessageType.Handoff,
            ticketId: ticketId
        ));

        return packet;
    }

    public HandoffPacket RouteFailureRemediation(
        string ticketId,
        AgentRole rejectingRole,
        AgentRole remediationRole,
        string rejectionReason,
        string remediationInstructions)
    {
        var ticket = _ticketStore.GetTicketById(ticketId);
        if (ticket != null)
        {
            var updated = ticket.WithStatus(TicketStatus.Remediating).WithAssignee(remediationRole);
            _ticketStore.UpdateTicket(updated);
        }

        var packet = HandoffPacket.Create(
            ticketId: ticketId,
            fromRole: rejectingRole,
            toRole: remediationRole,
            contextSummary: $"REJECTION / REMEDIATION: {rejectionReason}",
            actionRequested: $"Fix reported deficiencies: {remediationInstructions}",
            reviewChecklist: ["Review failure findings", "Apply fix in codebase", "Re-run verification"],
            remediationNotes: remediationInstructions
        );

        _ticketStore.RecordHandoff(packet);

        _eventStream.Publish(AgentMessage.Create(
            role: rejectingRole,
            senderName: rejectingRole.ToDisplayName(),
            content: $"🚨 REJECTED ticket {ticketId} - routed to {remediationRole.ToDisplayName()} for remediation: {rejectionReason}",
            type: MessageType.Alert,
            ticketId: ticketId
        ));

        return packet;
    }

    public IReadOnlyList<TicketItem> AdvanceWorkflowOnTicketCompletion(string completedTicketId)
    {
        var ticket = _ticketStore.GetTicketById(completedTicketId);
        if (ticket == null) return Array.Empty<TicketItem>();

        var updated = ticket.WithStatus(TicketStatus.Done);
        _ticketStore.UpdateTicket(updated);

        _eventStream.Publish(AgentMessage.Create(
            role: ticket.AssigneeRole,
            senderName: ticket.AssigneeRole.ToDisplayName(),
            content: $"✅ Completed ticket [{ticket.Id}] {ticket.Title}",
            type: MessageType.StateChange,
            ticketId: completedTicketId
        ));

        // Propagate deliverables to parent Epic
        if (!string.IsNullOrWhiteSpace(ticket.ParentEpicId) && ticket.Deliverables.Count > 0)
        {
            var epic = _ticketStore.GetTicketById(ticket.ParentEpicId);
            if (epic != null)
            {
                var newDeliverables = ticket.Deliverables
                    .Where(d => !epic.Deliverables.Any(ed => ed.Name == d.Name))
                    .ToList();
                if (newDeliverables.Count > 0)
                {
                    _ticketStore.UpdateTicket(epic.WithDeliverables(newDeliverables));
                }
            }
        }

        // Find all tickets that were waiting on this ticket and are now ready
        var activatedTickets = new List<TicketItem>();
        var allTickets = _ticketStore.GetAllTickets();

        foreach (var candidate in allTickets.Where(t => t.Status is TicketStatus.Backlog && t.DependsOnTicketIds.Contains(completedTicketId, StringComparer.OrdinalIgnoreCase)))
        {
            if (_ticketStore.AreDependenciesSatisfied(candidate.Id))
            {
                var readyTicket = candidate.WithStatus(TicketStatus.Ready);
                _ticketStore.UpdateTicket(readyTicket);
                activatedTickets.Add(readyTicket);

                _eventStream.Publish(AgentMessage.Create(
                    role: readyTicket.AssigneeRole,
                    senderName: "Ticket Engine",
                    content: $"🚀 Dependencies satisfied for [{readyTicket.Id}] {readyTicket.Title}. Assigned to {readyTicket.AssigneeRole.ToDisplayName()} (Status: Ready).",
                    type: MessageType.StateChange,
                    ticketId: readyTicket.Id
                ));
            }
        }

        // Check if parent story / epic has all subtasks completed
        if (!string.IsNullOrEmpty(ticket.ParentEpicId))
        {
            var epicTickets = _ticketStore.GetTicketsByEpic(ticket.ParentEpicId);
            var subtasks = epicTickets.Where(t => t.Type == TicketType.Subtask).ToList();
            if (subtasks.Count > 0 && subtasks.All(t => t.Status == TicketStatus.Done))
            {
                foreach (var parentItem in epicTickets.Where(t => t.Type != TicketType.Subtask && t.Status != TicketStatus.Done))
                {
                    var completedParent = parentItem.WithStatus(TicketStatus.Done);
                    _ticketStore.UpdateTicket(completedParent);
                    _eventStream.Publish(AgentMessage.Create(
                        role: completedParent.AssigneeRole,
                        senderName: "Ticket Engine",
                        content: $"🏆 All subtasks finished for [{completedParent.Id}] {completedParent.Title}! Marked as Done.",
                        type: MessageType.StateChange,
                        ticketId: completedParent.Id
                    ));
                }

                var epicTicket = _ticketStore.GetTicketById(ticket.ParentEpicId);
                if (epicTicket != null && epicTicket.Status != TicketStatus.Done)
                {
                    var completedEpic = epicTicket.WithStatus(TicketStatus.Done);
                    _ticketStore.UpdateTicket(completedEpic);
                    _eventStream.Publish(AgentMessage.Create(
                        role: completedEpic.AssigneeRole,
                        senderName: "Ticket Engine",
                        content: $"🏆 All subtasks finished for Epic [{completedEpic.Id}] {completedEpic.Title}! Marked as Done.",
                        type: MessageType.StateChange,
                        ticketId: completedEpic.Id
                    ));
                }
            }
        }

        return activatedTickets;
    }
}
