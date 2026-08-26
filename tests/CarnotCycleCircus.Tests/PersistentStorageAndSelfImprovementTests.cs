using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Learning;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Skills;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class PersistentStorageAndSelfImprovementTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly CarnotStorageOptions _options;
    private readonly IPersistentStorageService _storageService;

    public PersistentStorageAndSelfImprovementTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"carnot_test_{Guid.NewGuid():N}");
        _options = new CarnotStorageOptions
        {
            DataDirectory = _testTempDir,
            EnableAtomicWrites = true,
            SelfImprovementIntervalSeconds = 60,
            AutoRunSelfImprovementOnStartup = false
        };
        _storageService = new FilePersistentStorageService(_options);
    }

    [Fact]
    public async Task StorageService_SaveAndLoadJson_ShouldPreserveDataIntegrity()
    {
        // Arrange
        var testData = new SampleTestData("Carnot Cycle", 0.001);

        // Act
        await _storageService.SaveJsonAsync("test/sample.json", testData);
        var loaded = await _storageService.LoadJsonAsync<SampleTestData>("test/sample.json");

        // Assert
        var exists = await _storageService.FileExistsAsync("test/sample.json");
        exists.Should().BeTrue();
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Carnot Cycle");
        loaded.Entropy.Should().Be(0.001);
    }

    private record SampleTestData(string Name, double Entropy);

    [Fact]
    public async Task StorageService_GetStorageHealth_ShouldReportAccurateMetrics()
    {
        // Arrange
        await _storageService.SaveTextAsync("file1.txt", "Carnot Engine");
        await _storageService.SaveTextAsync("artifacts/file2.txt", "Entropy Data");

        // Act
        var health = await _storageService.GetStorageHealthAsync();

        // Assert
        health.IsHealthy.Should().BeTrue();
        health.RootDirectory.Should().Be(_testTempDir);
        health.TotalFilesCount.Should().Be(2);
        health.TotalSizeBytes.Should().BeGreaterThan(0);
        health.Files.Should().Contain(f => f.RelativePath.Contains("file1.txt"));
    }

    [Fact]
    public async Task PersistentMemoryStore_WithStorage_ShouldPersistAcrossInstances()
    {
        // Arrange instance 1
        var memStore1 = new EmbeddedVectorMemoryStore(_storageService);
        var entry = new MemoryEntry(
            Id: "MEM-TEST-01",
            Type: MemoryType.Semantic,
            Role: AgentRole.LeadArchitect,
            Content: "Persistent vector memory verification",
            Embedding: memStore1.GenerateEmbedding("Persistent vector memory verification"),
            Importance: 0.9f,
            Tags: new Dictionary<string, string> { ["Env"] = "Test" },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        );

        // Act
        await memStore1.StoreAsync(entry);

        // Allow async file flush
        await Task.Delay(100);

        // Assert instance 2 loads same entry from storage
        var memStore2 = new EmbeddedVectorMemoryStore(_storageService);
        var retrieved = await memStore2.GetByIdAsync("MEM-TEST-01");

        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be("Persistent vector memory verification");
        retrieved.Role.Should().Be(AgentRole.LeadArchitect);
    }

    [Fact]
    public async Task TicketStore_WithStorage_ShouldPersistTicketsAndHandoffs()
    {
        // Arrange instance 1
        var ticketStore1 = new TicketStore(_storageService);
        var ticket = new TicketItem(
            Id: "TCK-PERSIST-1",
            ParentEpicId: null,
            Title: "Persistent Ticket",
            Description: "Testing persistent ticket store",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Must persist across restarts"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var handoff = HandoffPacket.Create(
            ticketId: "TCK-PERSIST-1",
            fromRole: AgentRole.LeadArchitect,
            toRole: AgentRole.SoftwareDeveloper,
            contextSummary: "ADR ready for implementation",
            actionRequested: "Build feature"
        );

        // Act
        ticketStore1.CreateTicket(ticket);
        ticketStore1.RecordHandoff(handoff);

        await Task.Delay(100);

        // Assert instance 2 loads persisted data
        var ticketStore2 = new TicketStore(_storageService);
        var loadedTicket = ticketStore2.GetTicketById("TCK-PERSIST-1");
        var loadedHandoffs = ticketStore2.GetHandoffsForTicket("TCK-PERSIST-1");

        loadedTicket.Should().NotBeNull();
        loadedTicket!.Title.Should().Be("Persistent Ticket");
        loadedHandoffs.Should().HaveCount(1);
        loadedHandoffs[0].ContextSummary.Should().Be("ADR ready for implementation");
    }

    [Fact]
    public async Task SelfImprovementEngine_RunCycle_ShouldDistillFailureLessonsAndSynthesizeRules()
    {
        // Arrange
        var memStore = new EmbeddedVectorMemoryStore(_storageService);
        var ticketStore = new TicketStore(_storageService);
        var knowledgeMap = new KnowledgeMapService(_storageService);
        var eventStream = new AgentEventStream();

        // Simulate a failure remediation handoff
        var failureHandoff = HandoffPacket.Create(
            ticketId: "TCK-FAIL-01",
            fromRole: AgentRole.SecurityEngineer,
            toRole: AgentRole.SoftwareDeveloper,
            contextSummary: "Input parameter was unescaped",
            actionRequested: "Sanitize input parameter",
            remediationNotes: "Enforce regex validation on all query params"
        );
        ticketStore.RecordHandoff(failureHandoff);

        // Add completed ticket
        var completedTicket = new TicketItem(
            Id: "TCK-DONE-01",
            ParentEpicId: null,
            Title: "Auth Service",
            Description: "Zero-allocation auth pipeline",
            Type: TicketType.Feature,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.Critical,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["100% test pass"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(completedTicket);

        var engine = new SelfImprovementEngine(memStore, ticketStore, knowledgeMap, eventStream, _storageService);

        // Act
        var report = await engine.RunSelfImprovementCycleAsync();

        // Assert
        report.TotalCyclesRun.Should().Be(1);
        report.DistilledInsights.Should().NotBeEmpty();
        report.ProceduralRecipesGenerated.Should().BeGreaterThan(0);
        report.SemanticRulesReinforced.Should().BeGreaterThan(0);

        // Verify knowledge map received new learned insight
        var node = knowledgeMap.GetNode("KN-LI-REM-TCK-FAIL-01");
        node.Should().NotBeNull();
        node!.Category.Should().Be("LearnedInsight");
        node.Summary.Should().Contain("remediation required: 'Enforce regex validation on all query params'");

        // Verify markdown report was generated in storage
        var exists = await _storageService.FileExistsAsync("artifacts/LEARNED_INSIGHTS.md");
        exists.Should().BeTrue();
        var content = await _storageService.LoadTextAsync("artifacts/LEARNED_INSIGHTS.md");
        content.Should().Contain("Autonomous Self-Improvement & Continuous Learning Report");
        content.Should().Contain("KN-LI-REM-TCK-FAIL-01");
    }

    [Fact]
    public async Task AdrAndSkillRegistry_WithStorage_ShouldPersistAcrossRestarts()
    {
        // Arrange
        var adrManager1 = new AdrDocumentManager(_storageService);
        var newAdr = new ArchitecturalDecisionRecord(
            Id: "ADR-099",
            Title: "Persistent Volume Docker Stack",
            Status: AdrStatus.Accepted,
            Context: "Application requires container persistence.",
            Decision: "Implement named volumes and auto-saving JSON repositories.",
            AlternativesConsidered: ["Pure in-memory", "External relational database"],
            ConsequencesPositive: ["High efficiency", "State preserved"],
            ConsequencesNegative: ["Disk I/O required"],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        var importer = new SkillImporter();
        var skillRegistry1 = new SkillRegistry(importer, _storageService);
        var newSkill = new SkillDefinition(
            Id: "skill-docker-persistence",
            Name: "Docker Persistence Master",
            Description: "Manages Docker named volumes and self-improving loops.",
            Instructions: "Ensure volumes are mounted at /app/data.",
            RecommendedTools: ["csharp_syntax_check", "test_runner"],
            Category: "DevOps",
            AssignedRoles: [AgentRole.LeadArchitect, AgentRole.SoftwareDeveloper]
        );

        // Act
        adrManager1.SaveAdr(newAdr);
        skillRegistry1.RegisterSkill(newSkill);

        await Task.Delay(250);

        // Assert instance 2 loads persisted data
        var adrManager2 = new AdrDocumentManager(_storageService);
        var loadedAdr = adrManager2.GetAdr("ADR-099");
        loadedAdr.Should().NotBeNull();
        loadedAdr!.Title.Should().Be("Persistent Volume Docker Stack");

        var skillRegistry2 = new SkillRegistry(importer, _storageService);
        var loadedSkill = skillRegistry2.GetSkill("skill-docker-persistence");
        loadedSkill.Should().NotBeNull();
        loadedSkill!.Name.Should().Be("Docker Persistence Master");
        loadedSkill.AssignedRoles.Should().Contain(AgentRole.LeadArchitect);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }
}
