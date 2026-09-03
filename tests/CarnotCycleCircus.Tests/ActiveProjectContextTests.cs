using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Projects;
using CarnotCycleCircus.Core.Domain.Storage;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ActiveProjectContextTests
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
    public void SetActiveProject_ShouldRaiseOnActiveProjectChanged()
    {
        var context = new ActiveProjectContext();
        var project = Project.Create("Chaos Benchmark Arena", "Load testing project");

        Project? raised = null;
        var raiseCount = 0;
        context.OnActiveProjectChanged += p =>
        {
            raised = p;
            raiseCount++;
        };

        context.SetActiveProject(project);

        raised.Should().Be(project);
        raiseCount.Should().Be(1);
    }

    [Fact]
    public void CurrentProject_And_CurrentProjectId_ShouldReflectActiveSelection()
    {
        var context = new ActiveProjectContext();
        var project = Project.Create("E-Commerce Saga", "Order saga orchestration");

        context.SetActiveProject(project);

        context.CurrentProject.Should().Be(project);
        context.CurrentProjectId.Should().Be(project.Id);
    }

    [Fact]
    public void HasActiveProject_ShouldReflectWhetherAProjectIsSelected()
    {
        var context = new ActiveProjectContext();
        context.HasActiveProject.Should().BeFalse();

        context.SetActiveProject(Project.Create("Zero-Trust Identity", "Auth project"));
        context.HasActiveProject.Should().BeTrue();
    }

    [Fact]
    public void SettingNull_ShouldDeselectActiveProject()
    {
        var context = new ActiveProjectContext();
        context.SetActiveProject(Project.Create("IoT Ingestion", "Sensor pipeline"));

        context.SetActiveProject(null);

        context.HasActiveProject.Should().BeFalse();
        context.CurrentProject.Should().BeNull();
        context.CurrentProjectId.Should().BeNull();
    }

    [Fact]
    public async Task FlushAsync_ThenReload_ShouldRestoreActiveProjectAcrossRestarts()
    {
        var storage = new MockStorageService();
        var projectManager = new ProjectManager(storage);
        var project = await projectManager.CreateAsync("Distributed CQRS", "Ledger project");

        var context = new ActiveProjectContext(projectManager, storage);
        context.SetActiveProject(project);
        await context.FlushAsync();

        var restoredContext = new ActiveProjectContext(projectManager, storage);

        restoredContext.CurrentProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task OnProjectDeleted_ShouldClearActiveProjectIfItWasSelected()
    {
        var projectManager = new ProjectManager();
        var project = await projectManager.CreateAsync("Ephemeral Project", "About to be deleted");

        var context = new ActiveProjectContext(projectManager);
        context.SetActiveProject(project);

        await projectManager.DeleteAsync(project.Id);

        context.HasActiveProject.Should().BeFalse();
    }
}
