using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Projects;

public class ProjectManager : IProjectManager
{
    private readonly ConcurrentDictionary<string, Project> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string ProjectsFileName = "projects.json";

    public event Action<Project>? OnProjectCreated;
    public event Action<Project>? OnProjectUpdated;
    public event Action<string>? OnProjectDeleted;

    public ProjectManager(IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;
        LoadFromStorage();
    }

    private bool LoadFromStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var saved = _storageService.LoadJsonAsync<List<Project>>(ProjectsFileName).GetAwaiter().GetResult();
            if (saved != null && saved.Count > 0)
            {
                foreach (var p in saved)
                {
                    _projects[p.Id] = p;
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_storageService == null) return;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await _storageService.SaveJsonAsync(ProjectsFileName, _projects.Values.ToList(), cancellationToken);
        }
        catch
        {
            // Ignore transient write error
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

    public IReadOnlyList<Project> GetAll() =>
        _projects.Values.OrderByDescending(p => p.LastActivityAt ?? p.CreatedAt).ToList();

    public Project? GetById(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;
        return _projects.TryGetValue(projectId, out var project) ? project : null;
    }

    public ValueTask<Project> CreateAsync(
        string name,
        string description,
        string? teamId = null,
        string? workspaceDirectory = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var project = Project.Create(name, description, teamId, workspaceDirectory, metadata);
        _projects[project.Id] = project;
        SaveToStorage();
        OnProjectCreated?.Invoke(project);
        return new ValueTask<Project>(project);
    }

    public ValueTask<Project> UpdateAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        var updated = project.Touch();
        _projects[updated.Id] = updated;
        SaveToStorage();
        OnProjectUpdated?.Invoke(updated);
        return new ValueTask<Project>(updated);
    }

    public ValueTask DeleteAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return ValueTask.CompletedTask;

        if (_projects.TryRemove(projectId, out _))
        {
            SaveToStorage();
            OnProjectDeleted?.Invoke(projectId);
        }
        return ValueTask.CompletedTask;
    }
}
