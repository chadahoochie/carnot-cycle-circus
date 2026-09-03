namespace CarnotCycleCircus.Core.Domain.Projects;

/// <summary>
/// Manages the lifecycle of projects. Projects scope tickets, artifacts,
/// telemetry, and approvals. Application-level concerns are not project-scoped.
/// </summary>
public interface IProjectManager
{
    IReadOnlyList<Project> GetAll();
    Project? GetById(string projectId);

    ValueTask<Project> CreateAsync(
        string name,
        string description,
        string? teamId = null,
        string? workspaceDirectory = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    ValueTask<Project> UpdateAsync(
        Project project,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    event Action<Project>? OnProjectCreated;
    event Action<Project>? OnProjectUpdated;
    event Action<string>? OnProjectDeleted;
}
