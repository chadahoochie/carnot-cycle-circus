namespace CarnotCycleCircus.Core.Domain.Approvals;

public enum ApprovalGateStage
{
    TpmToArchitect,
    ArchitectToCoder
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected
}
