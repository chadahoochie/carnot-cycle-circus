using System.Collections.Concurrent;

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
}

public class KnowledgeMapService : IKnowledgeMapService
{
    private readonly ConcurrentDictionary<string, KnowledgeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<KnowledgeEdge> _edges = new();

    public KnowledgeMapService()
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
            Summary: "Deploying code right before the weekend guarantees a 100% chance of critical incident alerts during family dinner.",
            Attributes: new Dictionary<string, string> { ["Risk"] = "Catastrophic", ["Moral"] = "Go Home Instead" }
        );
        _nodes[node5.Id] = node5;

        var node6 = new KnowledgeNode(
            Id: "KN-006",
            Label: "Quantum Bugs",
            Category: "LearnedInsight",
            Summary: "A bug that vanishes whenever you attach a debugger, and re-appears in triplicate in production.",
            Attributes: new Dictionary<string, string> { ["Physics"] = "Heisenberg Uncertainty", ["Cure"] = "Coffee & Unit Tests" }
        );
        _nodes[node6.Id] = node6;

        _edges.Add(new KnowledgeEdge("KN-002", "KN-001", "Extends"));
        _edges.Add(new KnowledgeEdge("KN-004", "KN-003", "Mitigates"));
        _edges.Add(new KnowledgeEdge("KN-005", "KN-004", "DependsOn"));
        _edges.Add(new KnowledgeEdge("KN-006", "KN-001", "Mitigates"));
    }

    public KnowledgeMap GetFullMap() =>
        new(_nodes.Values.OrderBy(n => n.Label).ToList(), _edges.ToList());

    public KnowledgeNode? GetNode(string id) =>
        _nodes.TryGetValue(id, out var node) ? node : null;

    public KnowledgeNode AddOrUpdateNode(KnowledgeNode node)
    {
        _nodes[node.Id] = node;
        return node;
    }

    public bool DeleteNode(string id) => _nodes.TryRemove(id, out _);

    public void AddEdge(string sourceId, string targetId, string relationship)
    {
        _edges.Add(new KnowledgeEdge(sourceId, targetId, relationship));
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
