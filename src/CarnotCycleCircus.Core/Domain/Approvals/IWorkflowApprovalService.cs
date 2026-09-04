namespace CarnotCycleCircus.Core.Domain.Approvals;

public interface IWorkflowApprovalService
{
    bool RequireUserApproval { get; set; }
    WorkflowApprovalRequest? CurrentPendingRequest { get; }
    IReadOnlyList<WorkflowApprovalRequest> RejectedRequests { get; }
    IReadOnlyList<WorkflowApprovalRequest> History { get; }
    IReadOnlyList<WorkflowApprovalRequest> GetHistoryByProject(string projectId);
    WorkflowApprovalRequest? GetCurrentPendingRequestForProject(string? projectId);
    WorkflowApprovalRequest? GetLatestPendingOrRejectedRequestForProject(string? projectId);
    WorkflowApprovalRequest? GetRequestForTicket(string ticketId);
    WorkflowApprovalRequest? GetRequestById(string requestId);

    event Action<WorkflowApprovalRequest>? OnApprovalRequested;
    event Action<WorkflowApprovalRequest>? OnApprovalResolved;

    Task<WorkflowApprovalRequest> RequestApprovalAsync(
        WorkflowApprovalRequest request,
        CancellationToken cancellationToken = default);

    bool Approve(string requestId, string? feedback = null);
    bool Reject(string requestId, string reason);
    bool IsGateApproved(string epicId, ApprovalGateStage stage);
    void ResetGate(string epicId, ApprovalGateStage stage);
    void ResetAllGates();
}
