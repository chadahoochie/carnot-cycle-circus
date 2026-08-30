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
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class AdrDocumentManager : IAdrDocumentManager
{
    private readonly ConcurrentDictionary<string, ArchitecturalDecisionRecord> _adrs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProjectDocument> _docs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
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

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_storageService == null) return;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await _storageService.SaveJsonAsync(AdrsFileName, _adrs.Values.ToList(), cancellationToken);
            await _storageService.SaveJsonAsync(DocsFileName, _docs.Values.ToList(), cancellationToken);

            // Also persist markdown versions in artifacts directory
            foreach (var adr in _adrs.Values)
            {
                await _storageService.SaveTextAsync($"artifacts/adrs/{adr.Id}.md", adr.ToMarkdown(), cancellationToken);
            }
        }
        catch
        {
            // Ignore transient write errors
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

        var adr9 = new ArchitecturalDecisionRecord(
            Id: "ADR-009",
            Title: "Secure Key Storage, AEAD Envelope Encryption, and Master Key Derivation",
            Status: AdrStatus.Accepted,
            Context: "API credentials stored on disk or transferred across multi-agent pipelines risk exfiltration, tampering, or unauthorized harvesting.",
            Decision: "Implement AES-256-GCM AEAD envelope encryption at rest, PBKDF2-HMAC-SHA256 (310,000 iter) master key derivation, zero-downtime key rotation, and encrypted backup export packages.",
            AlternativesConsidered: [
                "Plaintext JSON storage (rejected: critical security violation)",
                "Windows-only DPAPI (rejected: breaks Linux container compatibility)",
                "Unauthenticated AES-CBC (rejected: vulnerable to padding oracles and lacks AEAD integrity verification)"
            ],
            ConsequencesPositive: [
                "Cryptographic confidentiality and integrity guaranteed at rest via AES-256-GCM AEAD",
                "Cross-platform support in Linux/Docker and air-gapped environments",
                "Master key rotation and encrypted backup export/import tools built-in"
            ],
            ConsequencesNegative: [
                "Master key loss makes stored credentials unrecoverable without original secrets"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr9.Id] = adr9;

        var adr10 = new ArchitecturalDecisionRecord(
            Id: "ADR-010",
            Title: "Dynamic Agent Troupe Lifecycle and Skill-Infused Agent Naming Engine",
            Status: AdrStatus.Accepted,
            Context: "Static 6-agent troupes prevented users from customizing troupe composition, creating multi-specialist squads, or infusing assigned skills into cognitive directives.",
            Decision: "Implement dynamic agent addition/removal, unique member IDs, skill-infused absurd circus agent name generation, and automated prompt synthesis enforcing the Deliverable Isolation Contract.",
            AlternativesConsidered: [
                "Static pre-baked name lists (rejected: cannot dynamically reflect custom imported skills)",
                "Pure LLM name generation on startup (rejected: adds latency and fails in air-gapped environments)"
            ],
            ConsequencesPositive: [
                "Dynamic troupe composition with atomic persistent storage",
                "Theatrical skill-infused circus names and cognitive prompts",
                "Strict deliverable isolation maintained across all personas"
            ],
            ConsequencesNegative: [
                "Dynamic troupe sizing requires UI handling for variable team sizes"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr10.Id] = adr10;

        var adr11 = new ArchitecturalDecisionRecord(
            Id: "ADR-011",
            Title: "Project Ignition Wizard, Codebase Harvester, and Zero-Setup Showcase Arena",
            Status: AdrStatus.Accepted,
            Context: "Technical users require frictionless onboarding with time-to-first-dopamine under 60 seconds, supporting both greenfield blueprints and brownfield codebase harvesting.",
            Decision: "Implement IProjectBlueprintService for 1-click curated project ignition, ICodebaseHarvesterService for local repo scanning and tech debt backlog generation, and IShowcaseDemoService for instant 0-key interactive swarm simulations.",
            AlternativesConsidered: [
                "Manual ticket creation only (rejected: high friction for new users)",
                "External CLI tool only (rejected: breaks web UI first-class discoverability)"
            ],
            ConsequencesPositive: [
                "Sub-60s onboarding time for technical users",
                "Automated ingestion of local repositories into 4-tier vector memory and knowledge graph",
                "Zero-key offline showcase arena for immediate evaluation"
            ],
            ConsequencesNegative: [
                "Local file system inspection requires directory read permissions"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr11.Id] = adr11;

        var adr12 = new ArchitecturalDecisionRecord(
            Id: "ADR-012",
            Title: "OpenRouter Dynamic Model Ingestion, Local Persistent Caching, Strength & Cost Categorization, and Favorites System",
            Status: AdrStatus.Accepted,
            Context: "Agent inference models previously relied on hardcoded strings, while OpenRouter continuously publishes hundreds of models with diverse capabilities and pricing tiers.",
            Decision: "Implement IModelCatalogService with 24h persistent caching, automated token cost tiering (Free, Budget, Standard, Premium), 6-discipline engineering strength area classification, 1-click favorites management, and role-based agent recommendations.",
            AlternativesConsidered: [
                "Raw API querying on every page load (rejected: introduces latency, rate limiting, and breaks offline mode)",
                "Static hardcoded enum lists (rejected: fails to support new AI model releases without recompiling code)"
            ],
            ConsequencesPositive: [
                "Dynamic access to 300+ models with resilient offline fallback",
                "1-click favorites eliminate cognitive overload during agent configuration",
                "Transparent token pricing and role recommendations optimize multi-agent squads"
            ],
            ConsequencesNegative: [
                "Model classification heuristics require periodic updates as model naming conventions evolve"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr12.Id] = adr12;

        var adr13 = new ArchitecturalDecisionRecord(
            Id: "ADR-013",
            Title: "Multi-File Deliverable Generation, Autonomous Syntax Self-Healing, and Inter-Agent Context Pipeline",
            Status: AdrStatus.Accepted,
            Context: "Complex enterprise .NET architectures require modular multi-file code structures (Models, Services, DI Extensions, Unit Tests), resilient syntax verification, upstream deliverable context across DAG nodes, and first-class PRD tracking.",
            Decision: "Implement multi-file csharp:FileName.cs parsing in AgentExecutionEngine, CSharpSyntaxCheckTool autonomous self-healing loop with low-temp remediation prompts, recursive GatherUpstreamDeliverables context injection, and first-class PRD categorization in ArtifactManager and ArtifactsHub.",
            AlternativesConsidered: [
                "Single monolithic file generation (rejected: violates .NET clean architecture and test separation)",
                "Fail-fast pipeline abort on syntax errors (rejected: creates avoidable pipeline rejections when immediate remediation resolves defects)",
                "Passing entire ticket store to prompts (rejected: causes token context window exhaustion)"
            ],
            ConsequencesPositive: [
                "Software Developer agents produce clean modular multi-file bundles matching enterprise conventions",
                "Self-healing syntax loop eliminates avoidable compilation failure rejections before review",
                "Strict upstream context continuity ensures reviewers evaluate actual upstream code and architectural decisions",
                "PRDs are tracked as first-class deliverables with repository disk sync"
            ],
            ConsequencesNegative: [
                "Self-healing loop incurs additional LLM completion call when syntax errors are detected in live inference",
                "Upstream deliverable injection increases prompt token payload, requiring truncation limits on large artifacts"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr13.Id] = adr13;

        var adr14 = new ArchitecturalDecisionRecord(
            Id: "ADR-0014",
            Title: "Dedicated Requirements Researcher Agent and Upstream Discovery DAG Stage",
            Status: AdrStatus.Accepted,
            Context: "Monolithic TPM discovery overloaded context windows with raw search outputs and mixed convergent discovery with structured ticket decomposition, leading to hallucination risks before architectural design.",
            Decision: "Introduce RequirementsResearcher (Rachel 'DeepDive' Reference) as Stage 1 of the DAG prior to TPM, generating formal _RESEARCH_BRIEF.md deliverables grounded in RFCs and codebase boundaries.",
            AlternativesConsidered: [
                "Monolithic TPM with integrated web search tools (rejected: token budget exhaustion and prompt distraction)",
                "Ad-hoc dynamic subagent tool calls (rejected: loses explicit DAG visualization and dedicated failure recovery cabling)",
                "Optional unverified research spikes (rejected: ungrounded assumptions cascade into developer code generation)"
            ],
            ConsequencesPositive: [
                "Clean cognitive separation between exploratory requirement scouting and structured epic decomposition",
                "Downstream TPM PRDs are grounded in real RFC standards and harvested repository context",
                "First-class visual observability and failure recovery cables on the workflow canvas",
                "Research briefs are versioned and categorized under artifacts/research/"
            ],
            ConsequencesNegative: [
                "Adds one sequential LLM inference hop before ticket decomposition during full-lifecycle workflows"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr14.Id] = adr14;

        var adr15 = new ArchitecturalDecisionRecord(
            Id: "ADR-0015",
            Title: "Collaborative Discovery and Two-Phase Architectural Ticket Refinement",
            Status: AdrStatus.Accepted,
            Context: "Project ignition lacked collaborative synergy between PM and Research Analyst, while work decomposition prematurely generated static subtasks before the Lead Architect had an opportunity to refine technical requirements and dependencies.",
            Decision: "Formalize collaborative discovery between PM and Research Analyst at project ignition, and institute a two-phase Lead Architect lifecycle where technical backlog refinement precedes ADR authoring and Clean Architecture scaffolding.",
            AlternativesConsidered: [
                "TPM-only story and subtask decomposition (rejected: bypasses architect evaluation of technical boundaries and dependency graphs)",
                "Immediate ADR generation without story grooming (rejected: architectural blueprints drift from concrete engineering subtasks)"
            ],
            ConsequencesPositive: [
                "Clear separation between product user stories and granular technical subtasks",
                "Lead Architect refines backlog and locks dependency graphs before authoring ADRs",
                "Downstream engineering roles receive dependency-ordered, contract-grounded subtasks"
            ],
            ConsequencesNegative: [
                "Adds explicit refinement stage to workflow before architectural scaffolding"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr15.Id] = adr15;

        var adr16 = new ArchitecturalDecisionRecord(
            Id: "ADR-016",
            Title: "Photino.Blazor Desktop Client, Headless Docker Server & Local ~/.carnot Multi-Mount Storage",
            Status: AdrStatus.Accepted,
            Context: ".NET MAUI lacks official Linux desktop support, while containerized deployment required running only the headless agent engine with isolated volume mounts. Local desktop execution required home directory state resolution and direct interaction with target codebases.",
            Decision: "Decouple into shared Razor UI (CarnotCycleCircus.UI), native cross-platform desktop using Photino.Blazor (CarnotCycleCircus.Desktop) on Linux/macOS/Windows, headless Docker server (CarnotCycleCircus.Server) with SignalR streaming, and multi-mount storage defaulting to ~/.carnot/data and ~/.carnot/artifacts.",
            AlternativesConsidered: [
                ".NET MAUI desktop on Linux (rejected: lacks official Microsoft support and stable Linux BlazorWebView)",
                "Monolithic Blazor Server only (rejected: requires full web browser overhead and lacks headless container operation)",
                "Electron.NET desktop wrapper (rejected: excessive memory footprint compared to Photino's lightweight ~40MB WebKitGTK shell)"
            ],
            ConsequencesPositive: [
                "Native Linux desktop window with native OS folder picker dialogs",
                "Dedicated headless Docker container with explicit data and artifacts volume mounts",
                "Local persistence homed in ~/.carnot with direct workspace directory interaction"
            ],
            ConsequencesNegative: [
                "Requires WebKitGTK installed on Linux distributions"
            ],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
        _adrs[adr16.Id] = adr16;

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
