using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Projects;
using CarnotCycleCircus.Core.Domain.Storage;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ProjectManagerTests
{
    private sealed class MockStorageService : IPersistentStorageService
    {
        public Dictionary<string, string> Files { get; } = new();
        public CarnotStorageOptions Options { get; } = new();

        public Task SaveJsonAsync<T>(string relativePath, T data, CancellationToken cancellationToken = default)
        {
            Files[relativePath] = JsonSerializer.Serialize(data);
            return Task.CompletedTask;
        }

        public Task<T?> LoadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
        {
            if (Files.TryGetValue(relativePath, out var json))
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(json));
            }
            return Task.FromResult<T?>(default);
        }

        public Task SaveTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
        {
            Files[relativePath] = content;
            return Task.CompletedTask;
        }

        public Task<string?> LoadTextAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Files.TryGetValue(relativePath, out var content);
            return Task.FromResult(content);
        }

        public Task<bool> FileExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Files.ContainsKey(relativePath));
        }

        public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Files.Remove(relativePath));
        }

        public Task<IReadOnlyList<string>> ListFilesAsync(string relativeDirectory = "", string searchPattern = "*.*", CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> list = Files.Keys.ToList();
            return Task.FromResult(list);
        }

        public Task<StorageHealthReport> GetStorageHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StorageHealthReport(true, "mock", 0, Files.Count, []));
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldAddProjectAndRaiseCreatedEvent()
    {
        var manager = new ProjectManager();
        Project? raised = null;
        manager.OnProjectCreated += p => raised = p;

        var project = await manager.CreateAsync("Order Gateway", "New API gateway project");

        project.Id.ShouldStartWith("proj-");
        project.Status.ShouldBe(ProjectStatus.Active);
        manager.GetById(project.Id).ShouldBe(project);
        raised.ShouldBe(project);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPersistChangesAndTouchLastActivityAt()
    {
        var manager = new ProjectManager();
        var project = await manager.CreateAsync("Payments", "Payments squad project");
        var originalTouch = project.LastActivityAt;

        Project? raised = null;
        manager.OnProjectUpdated += p => raised = p;

        var updated = await manager.UpdateAsync(project.WithStatus(ProjectStatus.Paused));

        updated.Status.ShouldBe(ProjectStatus.Paused);
        updated.LastActivityAt.ShouldNotBeNull();
        updated.LastActivityAt.Value.ShouldBeGreaterThanOrEqualTo(originalTouch ?? DateTimeOffset.MinValue);
        manager.GetById(project.Id)!.Status.ShouldBe(ProjectStatus.Paused);
        raised.ShouldBe(updated);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveProjectAndRaiseDeletedEvent()
    {
        var manager = new ProjectManager();
        var project = await manager.CreateAsync("Deprecated Service", "Slated for removal");

        string? deletedId = null;
        manager.OnProjectDeleted += id => deletedId = id;

        await manager.DeleteAsync(project.Id);

        manager.GetById(project.Id).ShouldBeNull();
        deletedId.ShouldBe(project.Id);
    }

    [Fact]
    public async Task CreateAsync_WithTeamAssociation_ShouldPersistTeamId()
    {
        var manager = new ProjectManager();
        var project = await manager.CreateAsync("IoT Ingestion", "Ingestion pipeline", teamId: "team-1");

        project.TeamId.ShouldBe("team-1");
        manager.GetById(project.Id)!.TeamId.ShouldBe("team-1");
    }

    [Fact]
    public void WithStatus_And_WithTeam_ShouldTransitionStateImmutably()
    {
        var project = Project.Create("Zero-Trust Identity", "Auth overhaul");

        var paused = project.WithStatus(ProjectStatus.Paused);
        paused.Status.ShouldBe(ProjectStatus.Paused);
        project.Status.ShouldBe(ProjectStatus.Active);

        var withTeam = project.WithTeam("team-42");
        withTeam.TeamId.ShouldBe("team-42");
        project.TeamId.ShouldBeNull();
    }

    [Fact]
    public async Task FlushAsync_ThenReload_ShouldRoundTripProjectsThroughStorage()
    {
        var storage = new MockStorageService();
        var manager = new ProjectManager(storage);
        var project = await manager.CreateAsync("Distributed CQRS", "Event-sourced ledger project");

        await manager.FlushAsync();

        var reloaded = new ProjectManager(storage);
        reloaded.GetById(project.Id).ShouldNotBeNull();
        reloaded.GetById(project.Id)!.Name.ShouldBe("Distributed CQRS");
    }

    [Fact]
    public async Task GetAll_ShouldOrderByLastActivityThenCreatedAtDescending()
    {
        var manager = new ProjectManager();
        var first = await manager.CreateAsync("Older Project", "Created first");
        await Task.Delay(5);
        var second = await manager.CreateAsync("Newer Project", "Created second");

        var all = manager.GetAll();

        all.First().Id.ShouldBe(second.Id);
        all.Last().Id.ShouldBe(first.Id);
    }
}
