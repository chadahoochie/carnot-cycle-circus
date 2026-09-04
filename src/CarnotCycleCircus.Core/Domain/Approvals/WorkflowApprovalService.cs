using System.Collections.Concurrent;

namespace CarnotCycleCircus.Core.Domain.Approvals;

public class WorkflowApprovalService : IWorkflowApprovalService
{
    private readonly ConcurrentDictionary<string, (WorkflowApprovalRequest Request, TaskCompletionSource<WorkflowApprovalRequest> Tcs)> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, WorkflowApprovalRequest> _rejectedRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _approvedGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorkflowApprovalRequest> _history = new();
    private readonly object _lock = new();

    public bool RequireUserApproval { get; set; }
    public WorkflowApprovalRequest? CurrentPendingRequest { get; private set; }

    public IReadOnlyList<WorkflowApprovalRequest> RejectedRequests
    {
        get
        {
            lock (_lock)
            {
                return _rejectedRequests.Values.OrderByDescending(r => r.ResolvedAt ?? r.CreatedAt).ToList();
            }
        }
    }

    public IReadOnlyList<WorkflowApprovalRequest> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }
    }

    public IReadOnlyList<WorkflowApprovalRequest> GetHistoryByProject(string projectId)
    {
        lock (_lock)
        {
            return _history.Where(r => string.Equals(r.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public WorkflowApprovalRequest? GetCurrentPendingRequestForProject(string? projectId)
    {
        if (CurrentPendingRequest == null) return null;
        if (string.IsNullOrEmpty(projectId)) return CurrentPendingRequest;
        return string.Equals(CurrentPendingRequest.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) ? CurrentPendingRequest : null;
    }

    public WorkflowApprovalRequest? GetLatestPendingOrRejectedRequestForProject(string? projectId)
    {
        var pending = GetCurrentPendingRequestForProject(projectId);
        if (pending != null) return pending;

        lock (_lock)
        {
            return _rejectedRequests.Values
                .Where(r => string.IsNullOrEmpty(projectId) || string.Equals(r.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.ResolvedAt ?? r.CreatedAt)
                .FirstOrDefault();
        }
    }

    public WorkflowApprovalRequest? GetRequestForTicket(string ticketId)
    {
        if (string.IsNullOrWhiteSpace(ticketId)) return null;

        if (CurrentPendingRequest != null && IsRequestMatchingTicket(CurrentPendingRequest, ticketId))
        {
            return CurrentPendingRequest;
        }

        lock (_lock)
        {
            var inRejected = _rejectedRequests.Values.FirstOrDefault(r => IsRequestMatchingTicket(r, ticketId));
            if (inRejected != null) return inRejected;

            return _history.AsEnumerable().Reverse().FirstOrDefault(r => IsRequestMatchingTicket(r, ticketId));
        }
    }

    public WorkflowApprovalRequest? GetRequestById(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId)) return null;

        if (CurrentPendingRequest?.Id == requestId) return CurrentPendingRequest;
        if (_pendingRequests.TryGetValue(requestId, out var pendingEntry)) return pendingEntry.Request;
        if (_rejectedRequests.TryGetValue(requestId, out var rejectedReq)) return rejectedReq;

        lock (_lock)
        {
            return _history.FirstOrDefault(r => r.Id == requestId);
        }
    }

    private static bool IsRequestMatchingTicket(WorkflowApprovalRequest req, string ticketId)
    {
        if (string.Equals(req.EpicId, ticketId, StringComparison.OrdinalIgnoreCase)) return true;
        if (req.ItemsToApprove.Any(i => i.Title.Contains(ticketId, StringComparison.OrdinalIgnoreCase) || (i.Details != null && i.Details.Contains(ticketId, StringComparison.OrdinalIgnoreCase)))) return true;
        return false;
    }

    public event Action<WorkflowApprovalRequest>? OnApprovalRequested;
    public event Action<WorkflowApprovalRequest>? OnApprovalResolved;

    public WorkflowApprovalService(bool requireUserApproval = true)
    {
        RequireUserApproval = requireUserApproval;
    }

    public async Task<WorkflowApprovalRequest> RequestApprovalAsync(
        WorkflowApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!RequireUserApproval)
        {
            var autoApproved = request.WithApproval("Auto-approved (Unattended Execution Mode)");
            _approvedGates[$"{request.EpicId}_{request.Stage}"] = true;
            lock (_lock)
            {
                _history.Add(autoApproved);
            }
            OnApprovalResolved?.Invoke(autoApproved);
            return autoApproved;
        }

        var tcs = new TaskCompletionSource<WorkflowApprovalRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.Id] = (request, tcs);
        CurrentPendingRequest = request;

        lock (_lock)
        {
            _history.Add(request);
        }

        OnApprovalRequested?.Invoke(request);

        using var registration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(request.Id, out var entry))
            {
                if (CurrentPendingRequest?.Id == request.Id)
                {
                    CurrentPendingRequest = null;
                }
                entry.Tcs.TrySetCanceled(cancellationToken);
            }
        });

        try
        {
            var result = await tcs.Task;
            return result;
        }
        finally
        {
            if (CurrentPendingRequest?.Id == request.Id)
            {
                CurrentPendingRequest = null;
            }
            _pendingRequests.TryRemove(request.Id, out _);
        }
    }

    public bool Approve(string requestId, string? feedback = null)
    {
        if (_pendingRequests.TryGetValue(requestId, out var entry))
        {
            var resolved = entry.Request.WithApproval(feedback);
            _approvedGates[$"{resolved.EpicId}_{resolved.Stage}"] = true;
            _rejectedRequests.TryRemove(requestId, out _);

            lock (_lock)
            {
                var idx = _history.FindIndex(r => r.Id == requestId);
                if (idx >= 0)
                {
                    _history[idx] = resolved;
                }
                else
                {
                    _history.Add(resolved);
                }
            }

            if (CurrentPendingRequest?.Id == requestId)
            {
                CurrentPendingRequest = null;
            }

            entry.Tcs.TrySetResult(resolved);
            OnApprovalResolved?.Invoke(resolved);
            return true;
        }

        // Support approving previously rejected or revision-requested gate
        WorkflowApprovalRequest? target = null;
        if (_rejectedRequests.TryGetValue(requestId, out var rejectedReq))
        {
            target = rejectedReq;
        }
        else
        {
            lock (_lock)
            {
                target = _history.FirstOrDefault(r => r.Id == requestId);
            }
        }

        if (target != null)
        {
            var resolved = target.WithApproval(feedback);
            _approvedGates[$"{resolved.EpicId}_{resolved.Stage}"] = true;
            _rejectedRequests.TryRemove(requestId, out _);

            lock (_lock)
            {
                var idx = _history.FindIndex(r => r.Id == requestId);
                if (idx >= 0)
                {
                    _history[idx] = resolved;
                }
                else
                {
                    _history.Add(resolved);
                }
            }

            OnApprovalResolved?.Invoke(resolved);
            return true;
        }

        return false;
    }

    public bool Reject(string requestId, string reason)
    {
        if (_pendingRequests.TryGetValue(requestId, out var entry))
        {
            var resolved = entry.Request.WithRejection(reason);
            _approvedGates[$"{resolved.EpicId}_{resolved.Stage}"] = false;
            _rejectedRequests[requestId] = resolved;

            lock (_lock)
            {
                var idx = _history.FindIndex(r => r.Id == requestId);
                if (idx >= 0)
                {
                    _history[idx] = resolved;
                }
                else
                {
                    _history.Add(resolved);
                }
            }

            if (CurrentPendingRequest?.Id == requestId)
            {
                CurrentPendingRequest = null;
            }

            entry.Tcs.TrySetResult(resolved);
            OnApprovalResolved?.Invoke(resolved);
            return true;
        }

        return false;
    }

    public bool IsGateApproved(string epicId, ApprovalGateStage stage)
    {
        if (string.IsNullOrWhiteSpace(epicId)) return true;
        return _approvedGates.TryGetValue($"{epicId}_{stage}", out var approved) && approved;
    }

    public void ResetGate(string epicId, ApprovalGateStage stage)
    {
        if (!string.IsNullOrWhiteSpace(epicId))
        {
            _approvedGates.TryRemove($"{epicId}_{stage}", out _);
            foreach (var kvp in _rejectedRequests)
            {
                if (string.Equals(kvp.Value.EpicId, epicId, StringComparison.OrdinalIgnoreCase) && kvp.Value.Stage == stage)
                {
                    _rejectedRequests.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    public void ResetAllGates()
    {
        _approvedGates.Clear();
        _rejectedRequests.Clear();
        CurrentPendingRequest = null;
    }
}
