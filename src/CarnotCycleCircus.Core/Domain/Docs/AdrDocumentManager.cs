using System.Collections.Concurrent;

namespace CarnotCycleCircus.Core.Domain.Docs;

public enum AdrStatus
{
    Draft,
    Proposed,
    Accepted,
    Rejected,
    Deprecated,
    Superseded
}

public record ArchitecturalDecisionRecord(
    string Id,
    string Title,
    AdrStatus Status,
    string Context,
    string Decision,
    IReadOnlyList<string> AlternativesConsidered,
    IReadOnlyList<string> ConsequencesPositive,
    IReadOnlyList<string> ConsequencesNegative,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
)
{
    public string ToMarkdown() => $"""
    # {Id}: {Title}

    ## Status
    **{Status}** (Updated: {UpdatedAt:yyyy-MM-dd})

    ## Context
    {Context}

    ## Decision
    {Decision}

    ## Alternatives Considered
    {string.Join("\n", AlternativesConsidered.Select(a => $"- {a}"))}

    ## Consequences
    ### Positive
    {string.Join("\n", ConsequencesPositive.Select(p => $"- ✅ {p}"))}

    ### Negative / Trade-offs
    {string.Join("\n", ConsequencesNegative.Select(n => $"- ⚠️ {n}"))}
    """;
}

public record ProjectDocument(
    string Id,
    string Title,
    string Category, // C4Diagram, ApiSpec, StrideThreatModel, PerformanceBudget, QaTestPlan
    string ContentMarkdown,
    DateTimeOffset UpdatedAt
);

public interface IAdrDocumentManager
{
    IReadOnlyList<ArchitecturalDecisionRecord> GetAllAdrs();
    ArchitecturalDecisionRecord? GetAdr(string id);
    ArchitecturalDecisionRecord SaveAdr(ArchitecturalDecisionRecord adr);
    bool DeleteAdr(string id);

    IReadOnlyList<ProjectDocument> GetAllDocs();
    ProjectDocument? GetDoc(string id);
    ProjectDocument SaveDoc(ProjectDocument doc);
    bool DeleteDoc(string id);

    string ExportCompleteMarkdownBundle();
}

public class AdrDocumentManager : IAdrDocumentManager
{
    private readonly ConcurrentDictionary<string, ArchitecturalDecisionRecord> _adrs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProjectDocument> _docs = new(StringComparer.OrdinalIgnoreCase);

    public AdrDocumentManager()
    {
        // Seed default ADRs
        var adr1 = new ArchitecturalDecisionRecord(
            Id: "ADR-001",
            Title: "Adopt Immutable Record Types for Domain & Handoff Payloads",
            Status: AdrStatus.Accepted,
            Context: "Multi-agent systems pass concurrent state between different autonomous agents. Mutable state risks race conditions and corrupted audit histories.",
            Decision: "All domain entities, DTOs, and HandoffPacket models must be immutable C# records or readonly record structs.",
            AlternativesConsidered: [
                "Mutable POCO classes with setters (rejected due to thread-safety risks)",
                "F# immutables with C# interop (rejected to maintain single pure C# 13 codebase)"
            ],
            ConsequencesPositive: [
                "Thread-safe execution across background channels",
                "Built-in value equality and non-destructive mutation via 'with' expressions",
                "Tamper-proof event stream audit trails"
            ],
            ConsequencesNegative: [
                "Requires explicit state machine copying during status transitions"
            ],
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt: DateTimeOffset.UtcNow.AddDays(-2)
        );
        _adrs[adr1.Id] = adr1;

        var adr2 = new ArchitecturalDecisionRecord(
            Id: "ADR-002",
            Title: "Connectable DAG Workflow Engine with Dedicated Failure Ports",
            Status: AdrStatus.Accepted,
            Context: "Autonomous workflows frequently fail QA or Security checks. Standard linear pipelines abort completely on failure.",
            Decision: "Implement a connectable graph topology where every node exposes 🟢 Input, 🔵 Success Output, and 🔴 Failure/Reject ports with automated remediation loopbacks.",
            AlternativesConsidered: [
                "Linear waterfall execution (rejected - no automatic recovery)",
                "Full actor mesh with arbitrary message passing (rejected - lacks deterministic observability)"
            ],
            ConsequencesPositive: [
                "Deterministic self-healing loops between QA/Security and Developer",
                "Visual representation in Blazor Canvas",
                "Explicit circuit breaking after retry thresholds"
            ],
            ConsequencesNegative: [
                "Graph cycle detection is required to prevent infinite ping-pong loops"
            ],
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt: DateTimeOffset.UtcNow.AddDays(-1)
        );
        _adrs[adr2.Id] = adr2;

        // Seed default system documentation
        var c4Doc = new ProjectDocument(
            Id: "DOC-C4",
            Title: "C4 System Architecture Model",
            Category: "C4Diagram",
            ContentMarkdown: """
            ```mermaid
            C4Context
                title System Context Diagram for Carnot Cycle Circus
                Person(engineer, "Software Engineer", "Configures teams, triggers workflows, inspects tickets & memory.")
                System(circus, "Carnot Cycle Circus", "Autonomous engineering agent orchestration platform in .NET 10 / Blazor.")
                System_Ext(openrouter, "OpenRouter AI Hub", "Provides multi-model LLM inference via unified API.")
                System_Ext(external_mem, "External Vector DB", "Optional OpenViking / Mem0 / Qdrant storage.")

                Rel(engineer, circus, "Interacts with", "HTTPS / Blazor")
                Rel(circus, openrouter, "Per-agent inference requests", "HTTPS / Bearer Auth")
                Rel(circus, external_mem, "Persists / queries vectors", "REST")
            ```
            """,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _docs[c4Doc.Id] = c4Doc;

        var strideDoc = new ProjectDocument(
            Id: "DOC-STRIDE",
            Title: "System STRIDE Threat Model",
            Category: "StrideThreatModel",
            ContentMarkdown: """
            # STRIDE Security Threat Assessment

            1. **Spoofing**: Agent roles are cryptographically tagged in `HandoffPacket` objects with strict role validation.
            2. **Tampering**: All handoffs and ticket records are immutable C# records.
            3. **Repudiation**: `IAgentEventStream` provides an append-only in-memory telemetry log.
            4. **Information Disclosure**: API keys are isolated in the client-side `ApiKeyVault` and masked.
            5. **Denial of Service**: Execution engine enforces DAG depth limits and circuit breakers.
            6. **Elevation of Privilege**: Tool sandbox permissions are strictly partitioned per role.
            """,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _docs[strideDoc.Id] = strideDoc;
    }

    public IReadOnlyList<ArchitecturalDecisionRecord> GetAllAdrs() =>
        _adrs.Values.OrderBy(a => a.Id).ToList();

    public ArchitecturalDecisionRecord? GetAdr(string id) =>
        _adrs.TryGetValue(id, out var adr) ? adr : null;

    public ArchitecturalDecisionRecord SaveAdr(ArchitecturalDecisionRecord adr)
    {
        _adrs[adr.Id] = adr;
        return adr;
    }

    public bool DeleteAdr(string id) => _adrs.TryRemove(id, out _);

    public IReadOnlyList<ProjectDocument> GetAllDocs() =>
        _docs.Values.OrderBy(d => d.Title).ToList();

    public ProjectDocument? GetDoc(string id) =>
        _docs.TryGetValue(id, out var doc) ? doc : null;

    public ProjectDocument SaveDoc(ProjectDocument doc)
    {
        _docs[doc.Id] = doc;
        return doc;
    }

    public bool DeleteDoc(string id) => _docs.TryRemove(id, out _);

    public string ExportCompleteMarkdownBundle()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Carnot Cycle Circus - Project Documentation Bundle");
        sb.AppendLine($"*Generated at {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*\n");

        sb.AppendLine("---");
        sb.AppendLine("## 1. Architectural Decision Records (ADRs)\n");
        foreach (var adr in GetAllAdrs())
        {
            sb.AppendLine(adr.ToMarkdown());
            sb.AppendLine("\n---\n");
        }

        sb.AppendLine("## 2. System Architecture & Threat Models\n");
        foreach (var doc in GetAllDocs())
        {
            sb.AppendLine($"### {doc.Title} ({doc.Category})\n");
            sb.AppendLine(doc.ContentMarkdown);
            sb.AppendLine("\n---\n");
        }

        return sb.ToString();
    }
}
