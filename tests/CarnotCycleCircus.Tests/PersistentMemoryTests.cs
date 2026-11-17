using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class PersistentMemoryTests
{
    private readonly EmbeddedVectorMemoryStore _store = new();

    [Fact]
    public async Task StoreAndSearch_ShouldRetrieveByCosineSimilarity()
    {
        await _store.StoreAsync(new MemoryEntry(
            Id: "MEM-1",
            Type: MemoryType.Semantic,
            Role: AgentRole.SecurityEngineer,
            Content: "Authentication tokens must be validated with HMAC-SHA256 signatures.",
            Embedding: Array.Empty<float>(),
            Importance: 0.9f,
            Tags: new Dictionary<string, string> { ["Domain"] = "Security" },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ));

        await _store.StoreAsync(new MemoryEntry(
            Id: "MEM-2",
            Type: MemoryType.Procedural,
            Role: AgentRole.OptimizationEngineer,
            Content: "Optimize garbage collection Gen0 by preallocating fixed buffer pools.",
            Embedding: Array.Empty<float>(),
            Importance: 0.8f,
            Tags: new Dictionary<string, string> { ["Domain"] = "Perf" },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ));

        var secResults = await _store.SearchAsync("authentication tokens signature", topK: 1);
        secResults.ShouldNotBeEmpty();
        secResults[0].Entry.Id.ShouldBe("MEM-1");

        var perfResults = await _store.SearchAsync("garbage collection buffer pools", topK: 1);
        perfResults.ShouldNotBeEmpty();
        perfResults[0].Entry.Id.ShouldBe("MEM-2");
    }

    [Fact]
    public async Task PruneAsync_ShouldRemoveLowImportanceDecayedMemories()
    {
        await _store.StoreAsync(new MemoryEntry(
            Id: "MEM-OLD",
            Type: MemoryType.Working,
            Role: AgentRole.SoftwareDeveloper,
            Content: "Temporary debug print statement",
            Embedding: Array.Empty<float>(),
            Importance: 0.1f,
            Tags: new Dictionary<string, string>(),
            Timestamp: DateTimeOffset.UtcNow.AddDays(-5),
            LastAccessedAt: DateTimeOffset.UtcNow.AddDays(-5)
        ));

        await _store.StoreAsync(new MemoryEntry(
            Id: "MEM-KEEP",
            Type: MemoryType.Semantic,
            Role: AgentRole.LeadArchitect,
            Content: "Core domain architecture principles",
            Embedding: Array.Empty<float>(),
            Importance: 0.95f,
            Tags: new Dictionary<string, string>(),
            Timestamp: DateTimeOffset.UtcNow.AddDays(-5),
            LastAccessedAt: DateTimeOffset.UtcNow.AddDays(-5)
        ));

        var pruned = await _store.PruneAsync(0.3f, TimeSpan.FromDays(1));
        pruned.ShouldBe(1);

        var remaining = await _store.GetAllAsync();
        remaining.Where(m => m.Id == "MEM-KEEP").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ConsolidationEngine_ShouldCreateEpisodicMemoryUponTaskCompletion()
    {
        var consolidation = new MemoryConsolidationEngine(_store);
        var ticket = new TicketItem(
            Id: "TCK-50",
            ParentEpicId: null,
            Title: "Implement Fast Parser",
            Description: "Fast parser",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Zero allocations", "100% test coverage"],
            Deliverables: [new ArtifactItem("Parser.cs", "class Parser {}")],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var episodic = await consolidation.ConsolidateTaskCompletionAsync(ticket, Array.Empty<AgentMessage>());

        episodic.ShouldNotBeNull();
        episodic.Type.ShouldBe(MemoryType.Episodic);
        episodic.Content.ShouldContain("Implement Fast Parser");

        var retrieved = await _store.GetByIdAsync(episodic.Id);
        retrieved.ShouldNotBeNull();
    }
}
