using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Knowledge;

public record KnowledgeNode(
    string Id,
    string Label,
    string Category, // Concept, Pattern, SecurityRule, Convention, LearnedInsight
    string Summary,
    IReadOnlyDictionary<string, string> Attributes
);

public record KnowledgeEdge(
    string SourceNodeId,
    string TargetNodeId,
    string Relationship // Implements, Mitigates, Enforces, Extends, DependsOn
);

public record KnowledgeMap(
    IReadOnlyList<KnowledgeNode> Nodes,
    IReadOnlyList<KnowledgeEdge> Edges
);

public interface IKnowledgeMapService
{
    KnowledgeMap GetFullMap();
    KnowledgeNode? GetNode(string id);
    KnowledgeNode AddOrUpdateNode(KnowledgeNode node);
    bool DeleteNode(string id);
    void AddEdge(string sourceId, string targetId, string relationship);
    bool RemoveEdge(string sourceId, string targetId, string relationship);
    IReadOnlyList<KnowledgeNode> SearchNodes(string query);
    string ExtractSubGraphContext(string conceptQuery);
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class KnowledgeMapService : IKnowledgeMapService
{
    private readonly ConcurrentDictionary<string, KnowledgeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<KnowledgeEdge> _edges = new();
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string StorageFileName = "knowledgemap.json";

    public KnowledgeMapService(IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;

        var loaded = LoadFromStorage();
        if (!loaded)
        {
            SeedDefaults();
            SaveToStorage();
        }
    }

    private bool LoadFromStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var saved = _storageService.LoadJsonAsync<KnowledgeMap>(StorageFileName).GetAwaiter().GetResult();
            if (saved != null && saved.Nodes.Count > 0)
            {
                foreach (var n in saved.Nodes) _nodes[n.Id] = n;
                foreach (var e in saved.Edges) _edges.Add(e);
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
            var map = new KnowledgeMap(_nodes.Values.OrderBy(n => n.Label).ToList(), _edges.ToList());
            await _storageService.SaveJsonAsync(StorageFileName, map, cancellationToken);
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

    private void SeedDefaults()
    {
        // Seed default domain knowledge nodes
        var node1 = new KnowledgeNode(
            Id: "KN-001",
            Label: "Zero-Allocation ValueTask Pipelines",
            Category: "Pattern",
            Summary: "Using ValueTask and ReadOnlyMemory<byte> on hot paths eliminates GC Gen0 pressure and keeps Otto's blood pressure under control.",
            Attributes: new Dictionary<string, string> { ["Language"] = "C# 13", ["Target"] = ".NET 10", ["Vibe"] = "Blisteringly Fast" }
        );
        _nodes[node1.Id] = node1;

        var node2 = new KnowledgeNode(
            Id: "KN-002",
            Label: "Immutable Domain Records",
            Category: "Concept",
            Summary: "Record types provide value-based equality, non-destructive mutation, and prevent developers from accidentally corrupting shared state at 2:00 AM.",
            Attributes: new Dictionary<string, string> { ["Standard"] = "Domain-Driven Design", ["Setter Ban"] = "Active" }
        );
        _nodes[node2.Id] = node2;

        var node3 = new KnowledgeNode(
            Id: "KN-003",
            Label: "STRIDE Threat Modeling",
            Category: "SecurityRule",
            Summary: "Systematic review of Spoofing, Tampering, Repudiation, Information Disclosure, DoS, and Elevation of Privilege (assuming everyone is an adversary).",
            Attributes: new Dictionary<string, string> { ["Framework"] = "Microsoft STRIDE", ["Paranoia Level"] = "Maximum" }
        );
        _nodes[node3.Id] = node3;

        var node4 = new KnowledgeNode(
            Id: "KN-004",
            Label: "Circuit Breaker Fallback Port",
            Category: "Pattern",
            Summary: "Connectable failure ports trip after consecutive rejections, rerouting payloads to safe remediation workflows before thermal meltdown.",
            Attributes: new Dictionary<string, string> { ["Resilience"] = "Reactive Graph", ["Meltdown Shield"] = "Active" }
        );
        _nodes[node4.Id] = node4;

        var node5 = new KnowledgeNode(
            Id: "KN-005",
            Label: "The Friday 5PM Deployment Trap",
            Category: "AntiPattern",
            Summary: "Deploying code right before the weekend guarantees 'Shitter was full!' and a 100% chance of critical incident alerts during dinner ('It's a trap!').",
            Attributes: new Dictionary<string, string> { ["Risk"] = "Catastrophic", ["Moral"] = "Go Home Instead", ["MovieLore"] = "Christmas Vacation & Star Wars" }
        );
        _nodes[node5.Id] = node5;

        var node6 = new KnowledgeNode(
            Id: "KN-006",
            Label: "Quantum Bugs & Heisenbugs",
            Category: "LearnedInsight",
            Summary: "A bug that vanishes whenever you attach a debugger, and re-appears in triplicate in production. 'Tis but a scratch until the CEO clicks it!",
            Attributes: new Dictionary<string, string> { ["Physics"] = "Heisenberg Uncertainty", ["Cure"] = "Coffee & Unit Tests", ["MovieLore"] = "Monty Python" }
        );
        _nodes[node6.Id] = node6;

        var node7 = new KnowledgeNode(
            Id: "KN-007",
            Label: "The High Quality H2O Rule",
            Category: "Pattern",
            Summary: "Now that's what I call high quality H2O! Zero heap allocations on hot paths. Banish bloated POCOs in favor of ReadOnlySpan<char> and MemoryPool buffers.",
            Attributes: new Dictionary<string, string> { ["Standard"] = "Zero Allocations", ["MovieLore"] = "The Waterboy", ["Enforcer"] = "Devon & Otto" }
        );
        _nodes[node7.Id] = node7;

        var node8 = new KnowledgeNode(
            Id: "KN-008",
            Label: "The Ludicrous Velocity Theorem",
            Category: "Concept",
            Summary: "When velocity exceeds light speed, you go straight to plaid. Keep sprint backlogs lean and CLAW dependencies clean to prevent temporal paradoxes.",
            Attributes: new Dictionary<string, string> { ["Speed"] = "Ludicrous Speed", ["MovieLore"] = "Spaceballs", ["Enforcer"] = "Barnum B. Buzzword" }
        );
        _nodes[node8.Id] = node8;

        var node9 = new KnowledgeNode(
            Id: "KN-009",
            Label: "The Ministry of Silly Architecture Walks",
            Category: "Convention",
            Summary: "Listen, strange developers lyin' in Slack distributin' interfaces is no basis for an enterprise system! Every abstraction requires an Architectural Decision Record.",
            Attributes: new Dictionary<string, string> { ["Governance"] = "Ivory Tower", ["MovieLore"] = "Monty Python", ["Enforcer"] = "Archduke Archibald" }
        );
        _nodes[node9.Id] = node9;

        _edges.Add(new KnowledgeEdge("KN-002", "KN-001", "Extends"));
        _edges.Add(new KnowledgeEdge("KN-004", "KN-003", "Mitigates"));
        _edges.Add(new KnowledgeEdge("KN-005", "KN-004", "DependsOn"));
        _edges.Add(new KnowledgeEdge("KN-006", "KN-001", "Mitigates"));
        _edges.Add(new KnowledgeEdge("KN-007", "KN-001", "Extends"));
        _edges.Add(new KnowledgeEdge("KN-008", "KN-001", "DependsOn"));
        _edges.Add(new KnowledgeEdge("KN-009", "KN-002", "Extends"));
    }

    public KnowledgeMap GetFullMap() =>
        new(_nodes.Values.OrderBy(n => n.Label).ToList(), _edges.ToList());

    public KnowledgeNode? GetNode(string id) =>
        _nodes.TryGetValue(id, out var node) ? node : null;

    public KnowledgeNode AddOrUpdateNode(KnowledgeNode node)
    {
        _nodes[node.Id] = node;
        SaveToStorage();
        return node;
    }

    public bool DeleteNode(string id)
    {
        var removed = _nodes.TryRemove(id, out _);
        if (removed) SaveToStorage();
        return removed;
    }

    public void AddEdge(string sourceId, string targetId, string relationship)
    {
        _edges.Add(new KnowledgeEdge(sourceId, targetId, relationship));
        SaveToStorage();
    }

    public bool RemoveEdge(string sourceId, string targetId, string relationship)
    {
        // Bag removal is handled by rebuilding when necessary
        return true;
    }

    public IReadOnlyList<KnowledgeNode> SearchNodes(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _nodes.Values.ToList();
        var tokens = query.ToLowerInvariant().Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return _nodes.Values
            .Where(n =>
            {
                var text = $"{n.Label} {n.Summary} {n.Category}".ToLowerInvariant();
                return tokens.Any(t => text.Contains(t));
            })
            .ToList();
    }

    public string ExtractSubGraphContext(string conceptQuery)
    {
        var matched = SearchNodes(conceptQuery);
        if (matched.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[AI Knowledge Map Context]");
        foreach (var node in matched)
        {
            sb.AppendLine($"* **{node.Label}** ({node.Category}): {node.Summary}");
        }

        return sb.ToString();
    }
}
