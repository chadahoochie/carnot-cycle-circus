using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Projects;

public class ActiveProjectContext : IActiveProjectContext
{
    private Project? _currentProject;
    private readonly IProjectManager? _projectManager;
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string ActiveProjectFileName = "active-project.json";

    public event Action<Project?>? OnActiveProjectChanged;

    public Project? CurrentProject => _currentProject;
    public string? CurrentProjectId => _currentProject?.Id;
    public bool HasActiveProject => _currentProject != null;

    public ActiveProjectContext(IProjectManager? projectManager = null, IPersistentStorageService? storageService = null)
    {
        _projectManager = projectManager;
        _storageService = storageService;

        LoadFromStorage();

        if (_projectManager != null)
        {
            _projectManager.OnProjectUpdated += updated =>
            {
                if (_currentProject?.Id == updated.Id)
                {
                    _currentProject = updated;
                    OnActiveProjectChanged?.Invoke(_currentProject);
                }
            };

            _projectManager.OnProjectDeleted += deletedId =>
            {
                if (_currentProject?.Id == deletedId)
                {
                    SetActiveProject(null);
                }
            };
        }
    }

    private void LoadFromStorage()
    {
        if (_storageService == null) return;
        try
        {
            var activeId = _storageService.LoadJsonAsync<string>(ActiveProjectFileName).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(activeId))
            {
                var project = _projectManager?.GetById(activeId);
                if (project != null)
                {
                    _currentProject = project;
                }
            }
        }
        catch
        {
            // Ignore load error
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_storageService == null) return;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await _storageService.SaveJsonAsync(ActiveProjectFileName, _currentProject?.Id ?? string.Empty, cancellationToken);
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

    public void SetActiveProject(Project? project)
    {
        _currentProject = project;
        SaveToStorage();
        OnActiveProjectChanged?.Invoke(_currentProject);
    }

    private void SaveToStorage()
    {
        if (_storageService == null) return;
        _ = Task.Run(async () =>
        {
            await FlushAsync();
        });
    }
}
