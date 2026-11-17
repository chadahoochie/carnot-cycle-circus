using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Harvester;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class CodebaseHarvesterServiceTests
{
    private readonly KnowledgeMapService _knowledgeMap = new();
    private readonly EmbeddedVectorMemoryStore _memoryStore = new();
    private readonly TicketStore _ticketStore = new();
    private readonly WorkDecompositionEngine _decompositionEngine;
    private readonly AdrDocumentManager _adrManager = new();
    private readonly AgentEventStream _eventStream = new();
    private readonly CodebaseHarvesterService _harvesterService;

    public CodebaseHarvesterServiceTests()
    {
        _decompositionEngine = new WorkDecompositionEngine(_ticketStore);
        _harvesterService = new CodebaseHarvesterService(
            _knowledgeMap,
            _memoryStore,
            _ticketStore,
            _decompositionEngine,
            _adrManager,
            _eventStream
        );
    }

    [Fact]
    public async Task HarvestDirectoryAsync_ShouldScanCurrentWorkspaceAndExtractInsights()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var report = await _harvesterService.HarvestDirectoryAsync(currentDir, autoGenerateBacklog: true);

        report.ShouldNotBeNull();
        report.TotalFiles.ShouldBeGreaterThan(0);
        report.CSharpFilesCount.ShouldBeGreaterThan(0);
        report.Projects.ShouldNotBeEmpty();
        report.DetectedPatterns.ShouldNotBeEmpty();
        report.QualityInsights.ShouldNotBeEmpty();
        report.GeneratedTicketIds.ShouldNotBeEmpty();

        // Verify report cached in latest
        var latest = _harvesterService.GetLatestReport();
        latest.ShouldBeSameAs(report);

        // Verify Knowledge Map populated with discovered projects
        var map = _knowledgeMap.GetFullMap();
        map.Nodes.ShouldContain(n => n.Attributes.ContainsKey("Framework"));

        // Verify Tickets generated in store
        foreach (var ticketId in report.GeneratedTicketIds)
        {
            var ticket = _ticketStore.GetTicketById(ticketId);
            ticket.ShouldNotBeNull();
        }
    }

    [Fact]
    public void GetStandardMountShortcuts_ShouldIncludeWorkspaceAndCurrentApp()
    {
        var shortcuts = _harvesterService.GetStandardMountShortcuts();

        shortcuts.ShouldNotBeEmpty();
        shortcuts.ShouldContain(s => s.FullPath == "/workspace");
        shortcuts.ShouldContain(s => s.FullPath == Directory.GetCurrentDirectory());
    }

    [Fact]
    public void InspectDirectory_WithValidDirectory_ShouldReturnSubdirectoriesAndDiscoveredSolutions()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var inspection = _harvesterService.InspectDirectory(currentDir);

        inspection.ShouldNotBeNull();
        inspection.IsAccessible.ShouldBeTrue();
        inspection.CurrentPath.ShouldBe(currentDir);
        inspection.QuickMountShortcuts.ShouldNotBeEmpty();
        inspection.DiscoveredSolutions.ShouldNotBeNull();
    }

    [Fact]
    public void InspectDirectory_WithNonExistentDirectory_ShouldReturnAccessibleFalse()
    {
        var bogusPath = "/non/existent/directory/path/that/never/exists";
        var inspection = _harvesterService.InspectDirectory(bogusPath);

        inspection.ShouldNotBeNull();
        inspection.IsAccessible.ShouldBeFalse();
        inspection.ErrorMessage.ShouldNotBeNullOrEmpty();
    }
}
