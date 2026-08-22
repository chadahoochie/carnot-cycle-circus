using System.Net.Http.Json;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Memory;

public class MemoryLookupTool : IToolDefinition
{
    private readonly IPersistentMemoryStore _memoryStore;

    public MemoryLookupTool(IPersistentMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public string Name => "memory_lookup";
    public string Description => "Searches persistent hierarchical memory for relevant architectural patterns, past decisions, and lessons learned.";
    public IReadOnlyDictionary<string, string> ParameterSchema => new Dictionary<string, string>
    {
        ["query"] = "Search query for memory retrieval",
        ["type"] = "Optional memory type filter (Working, Episodic, Semantic, Procedural)"
    };

    public async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Arguments.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("Missing 'query' parameter");
        }

        MemoryType? typeFilter = null;
        if (context.Arguments.TryGetValue("type", out var typeStr) && Enum.TryParse<MemoryType>(typeStr, true, out var parsedType))
        {
            typeFilter = parsedType;
        }

        var results = await _memoryStore.SearchAsync(query, topK: 3, typeFilter: typeFilter, cancellationToken: cancellationToken);

        if (results.Count == 0)
        {
            return ToolResult.Ok($"No matching memories found for '{query}'.");
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {results.Count} relevant memories for '{query}':");
        foreach (var r in results)
        {
            sb.AppendLine($"- [{r.Entry.Type} | Score: {r.SimilarityScore:P0} | Role: {r.Entry.Role.ToDisplayName()}] {r.Entry.Content}");
        }

        return ToolResult.Ok(sb.ToString(), new Dictionary<string, string>
        {
            ["ResultsCount"] = results.Count.ToString(),
            ["TopScore"] = results[0].SimilarityScore.ToString("F3")
        });
    }
}

public interface IExternalMemoryConnector
{
    Task<bool> PingAsync(string endpointUrl, CancellationToken cancellationToken = default);
    Task<int> SyncExternalAsync(string endpointUrl, IReadOnlyList<MemoryEntry> entries, CancellationToken cancellationToken = default);
}

public class ExternalMemoryConnector : IExternalMemoryConnector
{
    private readonly HttpClient _httpClient;

    public ExternalMemoryConnector(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<bool> PingAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var response = await _httpClient.GetAsync(endpointUrl, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> SyncExternalAsync(string endpointUrl, IReadOnlyList<MemoryEntry> entries, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _httpClient.PostAsJsonAsync(endpointUrl, entries, cts.Token);
            return response.IsSuccessStatusCode ? entries.Count : 0;
        }
        catch
        {
            return 0;
        }
    }
}

public interface IMemoryConsolidationEngine
{
    Task<MemoryEntry> ConsolidateTaskCompletionAsync(TicketItem ticket, IReadOnlyList<AgentMessage> taskMessages, CancellationToken cancellationToken = default);
}

public class MemoryConsolidationEngine : IMemoryConsolidationEngine
{
    private readonly IPersistentMemoryStore _memoryStore;

    public MemoryConsolidationEngine(IPersistentMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public async Task<MemoryEntry> ConsolidateTaskCompletionAsync(
        TicketItem ticket,
        IReadOnlyList<AgentMessage> taskMessages,
        CancellationToken cancellationToken = default)
    {
        var summary = $"Task [{ticket.Id}] '{ticket.Title}' completed by {ticket.AssigneeRole.ToDisplayName()}.\n" +
                      $"Key Deliverables: {ticket.Deliverables.Count} artifacts produced.\n" +
                      $"Acceptance Criteria: {string.Join("; ", ticket.AcceptanceCriteria)}";

        var episodicMemory = new MemoryEntry(
            Id: $"MEM-EP-{Guid.NewGuid().ToString("N")[..6]}",
            Type: MemoryType.Episodic,
            Role: ticket.AssigneeRole,
            Content: summary,
            Embedding: _memoryStore.GenerateEmbedding(summary),
            Importance: 0.85f,
            Tags: new Dictionary<string, string>
            {
                ["TicketId"] = ticket.Id,
                ["TicketType"] = ticket.Type.ToString(),
                ["Role"] = ticket.AssigneeRole.ToString()
            },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        );

        await _memoryStore.StoreAsync(episodicMemory, cancellationToken);
        return episodicMemory;
    }
}

public interface IContextAwareMemoryInjector
{
    Task<string> BuildAugmentedContextAsync(AgentRole role, string query, CancellationToken cancellationToken = default);
}

public class ContextAwareMemoryInjector : IContextAwareMemoryInjector
{
    private readonly IPersistentMemoryStore _memoryStore;

    public ContextAwareMemoryInjector(IPersistentMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public async Task<string> BuildAugmentedContextAsync(AgentRole role, string query, CancellationToken cancellationToken = default)
    {
        var memories = await _memoryStore.SearchAsync(query, topK: 3, roleFilter: role, cancellationToken: cancellationToken);
        if (memories.Count == 0)
        {
            // Try general search without role filter
            memories = await _memoryStore.SearchAsync(query, topK: 2, cancellationToken: cancellationToken);
        }

        if (memories.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n[Hierarchical Persistent Memory Context]");
        foreach (var m in memories)
        {
            sb.AppendLine($"- [{m.Entry.Type} | Relevance: {m.SimilarityScore:P0}]: {m.Entry.Content}");
        }

        return sb.ToString();
    }
}
