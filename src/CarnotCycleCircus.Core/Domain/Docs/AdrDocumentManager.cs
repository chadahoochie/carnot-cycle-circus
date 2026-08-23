using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Domain.Storage;

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
    private readonly IPersistentStorageService? _storageService;
    private const string AdrsFileName = "adrs.json";
    private const string DocsFileName = "docs.json";

    public AdrDocumentManager(IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;

        var loadedFromStorage = LoadFromStorage();
        if (!loadedFromStorage)
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
            var savedAdrs = _storageService.LoadJsonAsync<List<ArchitecturalDecisionRecord>>(AdrsFileName).GetAwaiter().GetResult();
            var savedDocs = _storageService.LoadJsonAsync<List<ProjectDocument>>(DocsFileName).GetAwaiter().GetResult();

            var loadedAny = false;
            if (savedAdrs != null && savedAdrs.Count > 0)
            {
                foreach (var a in savedAdrs) _adrs[a.Id] = a;
                loadedAny = true;
            }

            if (savedDocs != null && savedDocs.Count > 0)
            {
                foreach (var d in savedDocs) _docs[d.Id] = d;
                loadedAny = true;
            }

            return loadedAny;
        }
        catch
        {
            return false;
        }
    }

    private void SaveToStorage()
    {
        if (_storageService == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _storageService.SaveJsonAsync(AdrsFileName, _adrs.Values.ToList());
                await _storageService.SaveJsonAsync(DocsFileName, _docs.Values.ToList());

                // Also persist markdown versions in artifacts directory
                foreach (var adr in _adrs.Values)
                {
                    await _storageService.SaveTextAsync($"artifacts/adrs/{adr.Id}.md", adr.ToMarkdown());
                }
            }
            catch
            {
                // Ignore transient write errors
            }
        });
    }

    private void SeedDefaults()
    {
        var adr1 = new ArchitecturalDecisionRecord(
            Id: "ADR-001",
            Title: "Adopt Immutable Record Types for Domain & Handoff Payloads",
            Status: AdrStatus.Accepted,
            Context: "Multi-agent systems pass concurrent state between different autonomous agents. Mutable state risks race conditions, corrupted audit histories, and existential developer panic at 2:00 AM.",
            Decision: "All domain entities, DTOs, and HandoffPacket models must be immutable C# records or readonly record structs. Mutating state is considered a severe character flaw.",
            AlternativesConsidered: [
                "Mutable POCO classes with setters (rejected: Devon cannot be trusted with setters)",
                "F# immutables with C# interop (rejected: team refuses to learn another syntax)",
                "Passing raw unvalidated JSON strings (rejected: Sari threatened a hunger strike)"
            ],
            ConsequencesPositive: [
                "Thread-safe execution across background channels without locks",
                "Built-in value equality and non-destructive mutation via 'with' expressions",
                "Tamper-proof event stream audit trails that make compliance auditors weep with joy"
            ],
            ConsequencesNegative: [
                "Requires typing 'record' instead of 'class' and coping with immutability"
            ],
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAt: DateTimeOffset.UtcNow.AddDays(-2)
        );
        _adrs[adr1.Id] = adr1;

        var adr2 = new ArchitecturalDecisionRecord(
            Id: "ADR-002",
            Title: "Connectable DAG Workflow Engine with Dedicated Failure Ports",
            Status: AdrStatus.Accepted,
            Context: "Autonomous engineering workflows frequently fail QA or Security checks. Standard linear pipelines abort completely on failure and waste everyone's morning.",
            Decision: "Implement a connectable graph topology where every node exposes 🟢 Input, 🔵 Success Output, and 🔴 Failure/Reject ports with automated self-healing loopbacks.",
            AlternativesConsidered: [
                "Linear waterfall execution (rejected: failure is not a bug, it is our daily lifestyle)",
                "Full actor mesh with arbitrary gossip protocol (rejected: nobody can debug it)",
                "Praying that code works on the first try (rejected: mathematically impossible)"
            ],
            ConsequencesPositive: [
                "Deterministic self-healing loops between QA/Security and Developer",
                "Visual representation in Blazor Canvas with animated pulses",
                "Explicit circuit breaking before thermal meltdown occurs"
            ],
            ConsequencesNegative: [
                "Graph cycle detection is required so agents don't argue for infinity"
            ],
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt: DateTimeOffset.UtcNow.AddDays(-1)
        );
        _adrs[adr2.Id] = adr2;

        var adr3 = new ArchitecturalDecisionRecord(
            Id: "ADR-003",
            Title: "Prohibition of Coffee Deprivation for Senior Developers",
            Status: AdrStatus.Accepted,
            Context: "Empirical studies in the circus demonstrate that Devon's syntax error rate increases exponentially with cold brew depletion.",
            Decision: "Mandate continuous automated caffeine injection into developer context before running compilation checks.",
            AlternativesConsidered: [
                "Energy drinks (rejected: causes over-engineering of switch statements)",
                "Meditation (rejected: does not resolve race conditions)"
            ],
            ConsequencesPositive: [
                "Zero Gen0 allocations on hot path",
                "Morale elevated by 42%"
            ],
            ConsequencesNegative: [
                "Increased typing speed increases keyboard wear-and-tear"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr3.Id] = adr3;

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
            # STRIDE Security Threat Assessment (Paranoid Edition)

            1. **Spoofing**: Agent roles are cryptographically tagged in `HandoffPacket` objects with strict role validation (no rogue clowns).
            2. **Tampering**: All handoffs and ticket records are immutable C# records.
            3. **Repudiation**: `IAgentEventStream` provides an append-only in-memory telemetry log of every deed and misdeed.
            4. **Information Disclosure**: API keys are isolated in the client-side `ApiKeyVault` and masked.
            5. **Denial of Service**: Execution engine enforces DAG depth limits and circuit breakers before CPU catches fire.
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
        SaveToStorage();
        return adr;
    }

    public bool DeleteAdr(string id)
    {
        var removed = _adrs.TryRemove(id, out _);
        if (removed) SaveToStorage();
        return removed;
    }

    public IReadOnlyList<ProjectDocument> GetAllDocs() =>
        _docs.Values.OrderBy(d => d.Title).ToList();

    public ProjectDocument? GetDoc(string id) =>
        _docs.TryGetValue(id, out var doc) ? doc : null;

    public ProjectDocument SaveDoc(ProjectDocument doc)
    {
        _docs[doc.Id] = doc;
        SaveToStorage();
        return doc;
    }

    public bool DeleteDoc(string id)
    {
        var removed = _docs.TryRemove(id, out _);
        if (removed) SaveToStorage();
        return removed;
    }

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
