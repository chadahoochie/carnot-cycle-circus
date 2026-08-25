using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Harvester;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
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

        report.Should().NotBeNull();
        report.TotalFiles.Should().BeGreaterThan(0);
        report.CSharpFilesCount.Should().BeGreaterThan(0);
        report.Projects.Should().NotBeEmpty();
        report.DetectedPatterns.Should().NotBeEmpty();
        report.QualityInsights.Should().NotBeEmpty();
        report.GeneratedTicketIds.Should().NotBeEmpty();

        // Verify report cached in latest
        var latest = _harvesterService.GetLatestReport();
        latest.Should().BeSameAs(report);

        // Verify Knowledge Map populated with discovered projects
        var map = _knowledgeMap.GetFullMap();
        map.Nodes.Should().Contain(n => n.Attributes.ContainsKey("Framework"));

        // Verify Tickets generated in store
        foreach (var ticketId in report.GeneratedTicketIds)
        {
            var ticket = _ticketStore.GetTicketById(ticketId);
            ticket.Should().NotBeNull();
        }
    }
}
