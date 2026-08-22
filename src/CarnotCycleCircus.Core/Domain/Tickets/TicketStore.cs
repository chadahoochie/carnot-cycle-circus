using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Tickets;

public interface ITicketStore
{
    IReadOnlyList<TicketItem> GetAllTickets();
    TicketItem? GetTicketById(string id);
    IReadOnlyList<TicketItem> GetTicketsByEpic(string parentEpicId);
    IReadOnlyList<TicketItem> GetTicketsByStatus(TicketStatus status);
    IReadOnlyList<TicketItem> GetTicketsByAssignee(AgentRole role);
    TicketItem CreateTicket(TicketItem ticket);
    TicketItem UpdateTicket(TicketItem ticket);
    bool DeleteTicket(string id);
    bool AreDependenciesSatisfied(string ticketId);
    IReadOnlyList<TicketItem> GetReadyTickets();
    void RecordHandoff(HandoffPacket handoff);
    IReadOnlyList<HandoffPacket> GetHandoffsForTicket(string ticketId);
    IReadOnlyList<HandoffPacket> GetAllHandoffs();
    void Clear();

    event Action<TicketItem>? OnTicketChanged;
    event Action<HandoffPacket>? OnHandoffRecorded;
}

public class TicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<string, TicketItem> _tickets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<HandoffPacket> _handoffs = new();

    public event Action<TicketItem>? OnTicketChanged;
    public event Action<HandoffPacket>? OnHandoffRecorded;

    public IReadOnlyList<TicketItem> GetAllTickets() =>
        _tickets.Values.OrderBy(t => t.CreatedAt).ToList();

    public TicketItem? GetTicketById(string id) =>
        _tickets.TryGetValue(id, out var ticket) ? ticket : null;

    public IReadOnlyList<TicketItem> GetTicketsByEpic(string parentEpicId) =>
        _tickets.Values.Where(t => string.Equals(t.ParentEpicId, parentEpicId, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<TicketItem> GetTicketsByStatus(TicketStatus status) =>
        _tickets.Values.Where(t => t.Status == status).ToList();

    public IReadOnlyList<TicketItem> GetTicketsByAssignee(AgentRole role) =>
        _tickets.Values.Where(t => t.AssigneeRole == role).ToList();

    public TicketItem CreateTicket(TicketItem ticket)
    {
        _tickets[ticket.Id] = ticket;
        OnTicketChanged?.Invoke(ticket);
        return ticket;
    }

    public TicketItem UpdateTicket(TicketItem ticket)
    {
        _tickets[ticket.Id] = ticket;
        OnTicketChanged?.Invoke(ticket);
        return ticket;
    }

    public bool DeleteTicket(string id)
    {
        var removed = _tickets.TryRemove(id, out var deleted);
        if (removed && deleted != null)
        {
            OnTicketChanged?.Invoke(deleted);
        }
        return removed;
    }

    public bool AreDependenciesSatisfied(string ticketId)
    {
        var ticket = GetTicketById(ticketId);
        if (ticket == null || ticket.DependsOnTicketIds.Count == 0)
        {
            return true;
        }

        foreach (var depId in ticket.DependsOnTicketIds)
        {
            var depTicket = GetTicketById(depId);
            if (depTicket == null || depTicket.Status != TicketStatus.Done)
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<TicketItem> GetReadyTickets()
    {
        return _tickets.Values
            .Where(t => t.Status is TicketStatus.Backlog or TicketStatus.Ready)
            .Where(t => AreDependenciesSatisfied(t.Id))
            .ToList();
    }

    public void RecordHandoff(HandoffPacket handoff)
    {
        _handoffs.Enqueue(handoff);
        OnHandoffRecorded?.Invoke(handoff);
    }

    public IReadOnlyList<HandoffPacket> GetHandoffsForTicket(string ticketId) =>
        _handoffs.Where(h => string.Equals(h.TicketId, ticketId, StringComparison.OrdinalIgnoreCase)).OrderBy(h => h.Timestamp).ToList();

    public IReadOnlyList<HandoffPacket> GetAllHandoffs() =>
        _handoffs.OrderBy(h => h.Timestamp).ToList();

    public void Clear()
    {
        _tickets.Clear();
        while (_handoffs.TryDequeue(out _)) { }
    }
}
