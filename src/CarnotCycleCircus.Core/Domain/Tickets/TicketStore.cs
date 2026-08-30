using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Storage;

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
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    event Action<TicketItem>? OnTicketChanged;
    event Action<HandoffPacket>? OnHandoffRecorded;
}

public class TicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<string, TicketItem> _tickets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<HandoffPacket> _handoffs = new();
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string TicketsFileName = "tickets.json";
    private const string HandoffsFileName = "handoffs.json";

    public event Action<TicketItem>? OnTicketChanged;
    public event Action<HandoffPacket>? OnHandoffRecorded;

    public TicketStore(IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;
        LoadFromStorage();
    }

    private void LoadFromStorage()
    {
        if (_storageService == null) return;
        try
        {
            var savedTickets = _storageService.LoadJsonAsync<List<TicketItem>>(TicketsFileName).GetAwaiter().GetResult();
            if (savedTickets != null)
            {
                foreach (var t in savedTickets) _tickets[t.Id] = t;
            }

            var savedHandoffs = _storageService.LoadJsonAsync<List<HandoffPacket>>(HandoffsFileName).GetAwaiter().GetResult();
            if (savedHandoffs != null)
            {
                foreach (var h in savedHandoffs) _handoffs.Enqueue(h);
            }
        }
        catch
        {
            // Fallback to empty store
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_storageService == null) return;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await _storageService.SaveJsonAsync(TicketsFileName, _tickets.Values.ToList(), cancellationToken);
            await _storageService.SaveJsonAsync(HandoffsFileName, _handoffs.ToList(), cancellationToken);

            // Persist each deliverable to disk in artifacts/
            foreach (var ticket in _tickets.Values)
            {
                foreach (var del in ticket.Deliverables)
                {
                    if (string.IsNullOrWhiteSpace(del.Name) || del.Content == null) continue;

                    // Write to ticket-specific artifact directory
                    await _storageService.SaveTextAsync($"artifacts/tickets/{ticket.Id}/{del.Name}", del.Content, cancellationToken);

                    // Also write categorized artifact copies for instant discovery
                    if (del.Name.EndsWith("_ADR.md", StringComparison.OrdinalIgnoreCase) || (del.Description?.Contains("ADR", StringComparison.OrdinalIgnoreCase) ?? false) || ticket.AssigneeRole == AgentRole.LeadArchitect)
                    {
                        await _storageService.SaveTextAsync($"artifacts/adrs/{del.Name}", del.Content, cancellationToken);
                    }
                    else if (del.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || (del.Description?.Contains("Implementation", StringComparison.OrdinalIgnoreCase) ?? false) || ticket.AssigneeRole == AgentRole.SoftwareDeveloper)
                    {
                        await _storageService.SaveTextAsync($"artifacts/code/{del.Name}", del.Content, cancellationToken);
                    }
                    else if (del.Name.EndsWith("_STRIDE_Model.md", StringComparison.OrdinalIgnoreCase) || (del.Description?.Contains("STRIDE", StringComparison.OrdinalIgnoreCase) ?? false) || ticket.AssigneeRole == AgentRole.SecurityEngineer)
                    {
                        await _storageService.SaveTextAsync($"artifacts/security/{del.Name}", del.Content, cancellationToken);
                    }
                    else if (del.Name.EndsWith("_Perf_Profile.md", StringComparison.OrdinalIgnoreCase) || (del.Description?.Contains("Benchmark", StringComparison.OrdinalIgnoreCase) ?? false) || ticket.AssigneeRole == AgentRole.OptimizationEngineer)
                    {
                        await _storageService.SaveTextAsync($"artifacts/benchmarks/{del.Name}", del.Content, cancellationToken);
                    }
                    else if (del.Name.EndsWith("_QA_Scorecard.md", StringComparison.OrdinalIgnoreCase) || (del.Description?.Contains("QA", StringComparison.OrdinalIgnoreCase) ?? false) || ticket.AssigneeRole == AgentRole.PrincipalQAAnalyst)
                    {
                        await _storageService.SaveTextAsync($"artifacts/qa/{del.Name}", del.Content, cancellationToken);
                    }
                }
            }
        }
        catch
        {
            // Non-fatal write failure
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void SaveToStorage()
    {
        if (_storageService == null) return;
        _ = Task.Run(async () =>
        {
            await FlushAsync();
        });
    }

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
        SaveToStorage();
        return ticket;
    }

    public TicketItem UpdateTicket(TicketItem ticket)
    {
        _tickets[ticket.Id] = ticket;
        OnTicketChanged?.Invoke(ticket);
        SaveToStorage();
        return ticket;
    }

    public bool DeleteTicket(string id)
    {
        var removed = _tickets.TryRemove(id, out var deleted);
        if (removed && deleted != null)
        {
            OnTicketChanged?.Invoke(deleted);
            SaveToStorage();
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
            .Where(t => t.Status is TicketStatus.Backlog or TicketStatus.Ready or TicketStatus.Remediating)
            .Where(t => AreDependenciesSatisfied(t.Id))
            .OrderByDescending(t => t.Status == TicketStatus.Remediating) // Remediations take highest precedence
            .ThenByDescending(t => t.Priority)                           // Critical -> High -> Medium -> Low
            .ThenBy(t => (int)t.AssigneeRole)                            // 8-agent pecking order: Res -> TPM -> Arch -> Dev -> Sec -> Opt -> QA -> Int
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();
    }

    public void RecordHandoff(HandoffPacket handoff)
    {
        _handoffs.Enqueue(handoff);
        OnHandoffRecorded?.Invoke(handoff);
        SaveToStorage();
    }

    public IReadOnlyList<HandoffPacket> GetHandoffsForTicket(string ticketId) =>
        _handoffs.Where(h => string.Equals(h.TicketId, ticketId, StringComparison.OrdinalIgnoreCase)).OrderBy(h => h.Timestamp).ToList();

    public IReadOnlyList<HandoffPacket> GetAllHandoffs() =>
        _handoffs.OrderBy(h => h.Timestamp).ToList();

    public void Clear()
    {
        _tickets.Clear();
        while (_handoffs.TryDequeue(out _)) { }
        SaveToStorage();
    }
}
