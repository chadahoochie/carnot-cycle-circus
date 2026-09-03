namespace CarnotCycleCircus.Core.Domain.Projects;

public enum ProjectStatus
{
    Active,
    Paused,
    Completed,
    Archived
}

/// <summary>
/// A project scopes units of work — tickets, artifacts, telemetry, and approvals.
/// Application-level concerns (teams, agents, skills, memory, models) are not project-scoped.
/// </summary>
public record Project(
    string Id,
    string Name,
    string Description,
    ProjectStatus Status,
    string? TeamId,
    string? WorkspaceDirectory,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt = null
)
{
    public bool IsActive => Status == ProjectStatus.Active;

    public Project WithStatus(ProjectStatus newStatus) =>
        this with { Status = newStatus };

    public Project WithTeam(string? teamId) =>
        this with { TeamId = teamId };

    public Project Touch() =>
        this with { LastActivityAt = DateTimeOffset.UtcNow };

    public static Project Create(
        string name,
        string description,
        string? teamId = null,
        string? workspaceDirectory = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Id: $"proj-{Guid.NewGuid():N}"[..18],
            Name: name,
            Description: description,
            Status: ProjectStatus.Active,
            TeamId: teamId,
            WorkspaceDirectory: workspaceDirectory,
            Metadata: metadata ?? new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivityAt: DateTimeOffset.UtcNow
        );
}
