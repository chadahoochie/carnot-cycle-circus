namespace CarnotCycleCircus.Core.Domain.Projects;

/// <summary>
/// Tracks the currently active project. Project-scoped UI pages and services
/// use this to determine which project's data to display and operate on.
/// </summary>
public interface IActiveProjectContext
{
    /// <summary>The currently active project, or null if none is selected.</summary>
    Project? CurrentProject { get; }

    /// <summary>The active project ID, or null.</summary>
    string? CurrentProjectId { get; }

    /// <summary>Whether a project is currently active.</summary>
    bool HasActiveProject { get; }

    /// <summary>Sets the active project. Pass null to deselect.</summary>
    void SetActiveProject(Project? project);

    /// <summary>Raised when the active project changes.</summary>
    event Action<Project?>? OnActiveProjectChanged;
}
