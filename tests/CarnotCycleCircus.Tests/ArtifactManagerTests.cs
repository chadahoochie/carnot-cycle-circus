namespace CarnotCycleCircus.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Artifacts;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Domain.Tickets;
using Xunit;

public class ArtifactManagerTests : IDisposable
{
    private readonly string _tempDataDir;
    private readonly string _tempArtifactsDir;
    private readonly CarnotStorageOptions _storageOptions;
    private readonly FilePersistentStorageService _storageService;
    private readonly TicketStore _ticketStore;
    private readonly ArtifactManager _artifactManager;

    public ArtifactManagerTests()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), $"carnot_test_data_{Guid.NewGuid():N}");
        _tempArtifactsDir = Path.Combine(Path.GetTempPath(), $"carnot_test_art_{Guid.NewGuid():N}");
        _storageOptions = new CarnotStorageOptions
        {
            DataDirectory = _tempDataDir,
            ArtifactsDirectory = _tempArtifactsDir
        };
        _storageService = new FilePersistentStorageService(_storageOptions);
        _ticketStore = new TicketStore(_storageService);
        _artifactManager = new ArtifactManager(_storageService, _ticketStore);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDataDir)) Directory.Delete(_tempDataDir, true);
            if (Directory.Exists(_tempArtifactsDir)) Directory.Delete(_tempArtifactsDir, true);
        }
        catch { }
    }

    [Fact]
    public void CategorizeArtifact_ShouldIdentifyProperCategory()
    {
        Assert.Equal("Research", ArtifactManager.CategorizeArtifact("EPIC-1_RESEARCH_BRIEF.md", "Requirements Research Brief", AgentRole.RequirementsResearcher));
        Assert.Equal("PRD", ArtifactManager.CategorizeArtifact("EPIC-1_PRD.md", "Product Requirements Document", AgentRole.TechnicalProductManager));
        Assert.Equal("ADR", ArtifactManager.CategorizeArtifact("SUB-1_ADR.md", "Architecture Decision", AgentRole.LeadArchitect));
        Assert.Equal("Code", ArtifactManager.CategorizeArtifact("SUB-2_Implementation.cs", "Service Code", AgentRole.SoftwareDeveloper));
        Assert.Equal("Security", ArtifactManager.CategorizeArtifact("SUB-3_STRIDE_Model.md", "Threat Model", AgentRole.SecurityEngineer));
        Assert.Equal("Benchmark", ArtifactManager.CategorizeArtifact("SUB-4_Perf_Profile.md", "Benchmark Profile", AgentRole.OptimizationEngineer));
        Assert.Equal("QA", ArtifactManager.CategorizeArtifact("SUB-5_QA_Scorecard.md", "QA Acceptance", AgentRole.PrincipalQAAnalyst));
        Assert.Equal("Release", ArtifactManager.CategorizeArtifact("SUB-6_Release_Manifest.md", "Release Package", AgentRole.IntegrationEngineer));
    }

    [Fact]
    public async Task SaveDeliverableArtifactAsync_ShouldWriteToTicketAndCategoryFolder()
    {
        var ticket = new TicketItem(
            Id: "SUB-TEST01",
            ParentEpicId: "EPIC-1",
            Title: "Test Feature",
            Description: "Test Description",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: [],
            AcceptanceCriteria: ["Passes tests"],
            Deliverables: [],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverable = new ArtifactItem(
            Name: "SUB-TEST01_Implementation.cs",
            Content: "public class TestService { }",
            ContentType: "csharp",
            Description: "C# Implementation"
        );

        var path = await _artifactManager.SaveDeliverableArtifactAsync(ticket, deliverable);

        Assert.True(File.Exists(path));
        var codeCategorizedPath = Path.Combine(_tempArtifactsDir, "code", "SUB-TEST01_Implementation.cs");
        Assert.True(File.Exists(codeCategorizedPath));
        var content = await File.ReadAllTextAsync(codeCategorizedPath);
        Assert.Equal("public class TestService { }", content);
    }

    [Fact]
    public async Task ExportAllDeliverablesAsync_ShouldExportAllTicketDeliverables()
    {
        var ticket1 = new TicketItem(
            Id: "SUB-DEV99",
            ParentEpicId: "EPIC-1",
            Title: "Dev Ticket",
            Description: "Dev Work",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Medium,
            DependsOnTicketIds: [],
            AcceptanceCriteria: ["Working code"],
            Deliverables: [
                new ArtifactItem("SUB-DEV99_Implementation.cs", "public class DevClass { }", "csharp", "Implementation")
            ],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var ticket2 = new TicketItem(
            Id: "SUB-ARCH99",
            ParentEpicId: "EPIC-1",
            Title: "Arch Ticket",
            Description: "Arch Work",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Medium,
            DependsOnTicketIds: [],
            AcceptanceCriteria: ["ADR written"],
            Deliverables: [
                new ArtifactItem("SUB-ARCH99_ADR.md", "# ADR 99", "markdown", "ADR")
            ],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        _ticketStore.CreateTicket(ticket1);
        _ticketStore.CreateTicket(ticket2);

        var exportedCount = await _artifactManager.ExportAllDeliverablesAsync();
        Assert.Equal(2, exportedCount);

        var allArtifacts = _artifactManager.GetAllArtifacts();
        Assert.Equal(2, allArtifacts.Count);

        var devArtifacts = _artifactManager.GetArtifactsByRole(AgentRole.SoftwareDeveloper);
        Assert.Single(devArtifacts);
        Assert.Equal("SUB-DEV99_Implementation.cs", devArtifacts[0].Name);

        var adrArtifacts = _artifactManager.GetArtifactsByCategory("ADR");
        Assert.Single(adrArtifacts);
        Assert.Equal("SUB-ARCH99_ADR.md", adrArtifacts[0].Name);
    }

    [Fact]
    public async Task TicketStore_FlushAsync_ShouldAutomaticallyWriteDeliverablesToDisk()
    {
        var ticket = new TicketItem(
            Id: "SUB-AUTO01",
            ParentEpicId: "EPIC-2",
            Title: "Auto Flush Ticket",
            Description: "Testing automatic disk persistence",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SecurityEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Critical,
            DependsOnTicketIds: [],
            AcceptanceCriteria: ["Security threat model verified"],
            Deliverables: [
                new ArtifactItem("SUB-AUTO01_STRIDE_Model.md", "# Threat Model", "markdown", "STRIDE Matrix")
            ],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        _ticketStore.CreateTicket(ticket);
        await _ticketStore.FlushAsync();

        var expectedTicketFile = Path.Combine(_tempArtifactsDir, "tickets", "SUB-AUTO01", "SUB-AUTO01_STRIDE_Model.md");
        Assert.True(File.Exists(expectedTicketFile));

        var expectedSecFile = Path.Combine(_tempArtifactsDir, "security", "SUB-AUTO01_STRIDE_Model.md");
        Assert.True(File.Exists(expectedSecFile));
    }
}
