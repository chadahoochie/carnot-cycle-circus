namespace CarnotCycleCircus.Core.Domain.Tickets;

public enum TicketType
{
    Epic,
    Feature,
    Bug,
    ResearchSpike,
    Subtask
}

public enum TicketStatus
{
    Backlog,
    Ready,
    InProgress,
    Review,
    Remediating,
    Done,
    Blocked
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}

public static class TicketStatusExtensions
{
    public static string ToColorHex(this TicketStatus status) => status switch
    {
        TicketStatus.Backlog => "#64748b",      // Slate
        TicketStatus.Ready => "#0284c7",        // Sky Blue
        TicketStatus.InProgress => "#3b82f6",   // Blue
        TicketStatus.Review => "#eab308",       // Yellow
        TicketStatus.Remediating => "#ef4444",  // Red
        TicketStatus.Done => "#22c55e",         // Green
        TicketStatus.Blocked => "#a855f7",      // Purple
        _ => "#94a3b8"
    };

    public static string ToBadgeClass(this TicketStatus status) => status switch
    {
        TicketStatus.Backlog => "badge-secondary",
        TicketStatus.Ready => "badge-info",
        TicketStatus.InProgress => "badge-primary",
        TicketStatus.Review => "badge-warning",
        TicketStatus.Remediating => "badge-danger",
        TicketStatus.Done => "badge-success",
        TicketStatus.Blocked => "badge-dark",
        _ => "badge-light"
    };
}
