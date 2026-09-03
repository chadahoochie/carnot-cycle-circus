namespace CarnotCycleCircus.Core.Domain.Approvals;

public record ApprovalItemSummary(
    string Category,
    string Title,
    string Details,
    IReadOnlyList<string>? KeyPoints = null
);
