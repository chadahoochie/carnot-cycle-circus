# Carnot Cycle Circus - Autonomous Agent & Assistant Guide 🎪🤖

> Autonomous Engineering Agent Orchestration Platform built in **.NET 10 / C# 13** with interactive **Blazor** UI.

## 🗺️ Documentation Portal & LLM System Specification
- 📖 **Documentation Portal**: [`docs/README.md`](docs/README.md)
- 🤖 **Condensed Machine-Readable Spec**: [`docs/LLMS.txt`](docs/LLMS.txt)
- 🏛️ **Architecture & Topologies**: [`docs/architecture/`](docs/architecture/)
- 📜 **C# 13 & Quality Standards**: [`docs/standards/`](docs/standards/)
- 📋 **Architectural Decision Records**: [`docs/adrs/`](docs/adrs/)
- 🧭 **Developer Guides & Recipes**: [`docs/guides/`](docs/guides/)
- 🔍 **Core & Web API Reference**: [`docs/api/`](docs/api/)

---

## ⚡ Agent Guidance & Skills Routing
<!-- BEGIN DOTNET-SKILLS COMPRESSED INDEX -->
[dotnet-skills]|IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
|flow:{skim repo patterns -> consult docs/ & skills/ by name -> implement smallest-change -> test -> note conflicts}
|route:
|architecture:{docs/architecture/system-overview.md,engineering-multi-agent-systems-architect,project-structure}
|csharp:{docs/standards/coding-standards.md,csharp-coding-standards,csharp-type-design-performance,csharp-concurrency-patterns,csharp-pro}
|di-config:{microsoft-extensions-dependency-injection,local-tools,package-management}
|skills-index:{skills-index-snippets}
<!-- END DOTNET-SKILLS COMPRESSED INDEX -->

---

## 🚨 Non-Negotiable Core Rules for Agents & LLMs

1. **Deliverable Isolation Contract (ADR-0005)**:
   - Conversational chat dialogue and thought logs may feature cynical/witty persona banter.
   - **All formal deliverables (PRDs, ADRs, C# code, unit tests, STRIDE threat models, benchmark reports, QA scorecards, and ticket metadata) MUST remain 100% professional, standard-compliant, rigorous, unambiguous, and completely free of joke text or sarcastic phrasing.**
2. **Immutability & Type Safety (ADR-0001)**:
   - Domain models, DTOs, and handoff packets MUST be immutable C# `record` types or `readonly record struct` value objects.
   - Setters (`set;`) are banned on domain entities. Use non-destructive mutation (`with { ... }`).
3. **Zero-Allocation & Async Dogma**:
   - Hot path routines must utilize `ValueTask`, `ReadOnlyMemory<byte>`, `ReadOnlySpan<char>`, and bounded `Channel<T>`.
   - Every asynchronous method MUST accept `CancellationToken cancellationToken = default`.
4. **Docs-as-Code & ADRs**:
   - Architectural decisions must be accompanied by an ADR in `docs/adrs/`.

---

## 🛠️ Build & Test Commands
```bash
# Build entire solution
dotnet build CarnotCycleCircus.slnx

# Run all test suites
dotnet test CarnotCycleCircus.slnx --logger "console;verbosity=normal"

# Launch Blazor web application
dotnet run --project src/CarnotCycleCircus.Web
```
