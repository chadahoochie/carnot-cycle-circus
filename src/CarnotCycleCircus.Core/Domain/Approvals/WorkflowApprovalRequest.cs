using System.Text.Json.Serialization;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;

namespace CarnotCycleCircus.Core.Domain.Approvals;

[method: JsonConstructor]
public record WorkflowApprovalRequest(
    string Id,
    string EpicId,
    string? ProjectId,
    ApprovalGateStage Stage,
    string GateTitle,
    string GateDescription,
    string NextStepDescription,
    AgentRole PrecedingRole,
    AgentRole ProceedingRole,
    IReadOnlyList<ApprovalItemSummary> ItemsToApprove,
    IReadOnlyList<ArtifactItem> Deliverables,
    ApprovalStatus Status = ApprovalStatus.Pending,
    string? UserFeedback = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? ResolvedAt = null
)
{
    public WorkflowApprovalRequest(
        string Id,
        string EpicId,
        ApprovalGateStage Stage,
        string GateTitle,
        string GateDescription,
        string NextStepDescription,
        AgentRole PrecedingRole,
        AgentRole ProceedingRole,
        IReadOnlyList<ApprovalItemSummary> ItemsToApprove,
        IReadOnlyList<ArtifactItem> Deliverables,
        ApprovalStatus Status = ApprovalStatus.Pending,
        string? UserFeedback = null,
        DateTimeOffset CreatedAt = default,
        DateTimeOffset? ResolvedAt = null)
        : this(Id, EpicId, null, Stage, GateTitle, GateDescription, NextStepDescription, PrecedingRole, ProceedingRole, ItemsToApprove, Deliverables, Status, UserFeedback, CreatedAt, ResolvedAt)
    {
    }

    public WorkflowApprovalRequest WithApproval(string? feedback = null) =>
        this with
        {
            Status = ApprovalStatus.Approved,
            UserFeedback = feedback,
            ResolvedAt = DateTimeOffset.UtcNow
        };

    public WorkflowApprovalRequest WithRejection(string reason) =>
        this with
        {
            Status = ApprovalStatus.Rejected,
            UserFeedback = reason,
            ResolvedAt = DateTimeOffset.UtcNow
        };
}
