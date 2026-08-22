using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Memory;

public enum MemoryType
{
    Working,     // Short-term transient execution steps and scratchpad
    Episodic,    // Past task executions, lessons learned, and handoffs
    Semantic,    // Domain concepts, architectural patterns, and rules
    Procedural   // Reusable workflows, tool execution templates, and scripts
}

public record MemoryEntry(
    string Id,
    MemoryType Type,
    AgentRole Role,
    string Content,
    IReadOnlyList<float> Embedding,
    float Importance, // 0.0 to 1.0
    IReadOnlyDictionary<string, string> Tags,
    DateTimeOffset Timestamp,
    DateTimeOffset LastAccessedAt
)
{
    public MemoryEntry Touch() => this with { LastAccessedAt = DateTimeOffset.UtcNow };
}
