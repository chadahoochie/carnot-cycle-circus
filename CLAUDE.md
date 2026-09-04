# Carnot Cycle Circus - Engineering Assistant Guidelines

> Autonomous Engineering Agent Orchestration Platform built in **.NET 10 / C# 13** — operating at theoretical maximum Carnot efficiency to tame the 6-ring circus of autonomous AI engineering roles.

## 🗺️ Documentation Suite & System Map
- 📖 **Documentation Portal**: [`docs/README.md`](docs/README.md)
- 🤖 **Condensed Machine-Readable Spec**: [`docs/LLMS.txt`](docs/LLMS.txt)
- 🏛️ **Architecture & Topologies**: [`docs/architecture/`](docs/architecture/)
- 📜 **C# 13 & Quality Standards**: [`docs/standards/`](docs/standards/)
- 📋 **Architectural Decision Records**: [`docs/adrs/`](docs/adrs/)
- 🧭 **Developer Guides & Recipes**: [`docs/guides/`](docs/guides/)
- 🔍 **Core & Web API Reference**: [`docs/api/`](docs/api/)

## Build & Test Commands
```bash
# Build entire solution
dotnet build CarnotCycleCircus.slnx

# Run all test suites
dotnet test CarnotCycleCircus.slnx --logger "console;verbosity=normal"

# Run Blazor web application
dotnet run --project src/CarnotCycleCircus.Web
```

## Solution Structure
- `src/CarnotCycleCircus.Core/`: Domain models, ticket management & decomposition engine, persistent memory (OpenViking-style), OpenRouter inference hub, agent tool sandbox, and connectable workflow graph. [Core Guidelines](src/CarnotCycleCircus.Core/CLAUDE.md)
- `src/CarnotCycleCircus.Web/`: Blazor frontend with interactive Ticket Studio (Kanban/CLAW), Memory Inspector, Team Definition Studio, ADR Hub, Standards Manager, Knowledge Map Explorer, and Real-Time Execution Dashboard. [Web Guidelines](src/CarnotCycleCircus.Web/CLAUDE.md)
- `tests/CarnotCycleCircus.Tests/`: xUnit & FluentAssertions test suites verifying tickets, tools, memory, inference, standards, and workflow routing. [Testing Guidelines](tests/CarnotCycleCircus.Tests/CLAUDE.md)
- `skills/`: Preserved engineering, architecture, and .NET best-practice skills.

## Agent Guidance: Preserved Skills Routing
<!-- BEGIN DOTNET-SKILLS COMPRESSED INDEX -->
[dotnet-skills]|IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
|flow:{skim repo patterns -> consult docs/ & skills/ by name -> implement smallest-change -> test -> note conflicts}
|route:
|architecture:{docs/architecture/system-overview.md,engineering-multi-agent-systems-architect,project-structure}
|csharp:{docs/standards/coding-standards.md,csharp-coding-standards,csharp-type-design-performance,csharp-concurrency-patterns,csharp-pro}
|di-config:{microsoft-extensions-dependency-injection,local-tools,package-management}
|skills-index:{skills-index-snippets}
<!-- END DOTNET-SKILLS COMPRESSED INDEX -->

## Core Architecture Invariants
1. **Deliverable Isolation Contract (ADR-0005)**: Witty/cynical agent banter is restricted to chat/thought streams; all deliverables (PRDs, ADRs, C# code, tests, threat models, scorecards) MUST remain 100% professional and standard-compliant.
2. **Immutable Domain & Strong Types (ADR-0001)**: Use `record` for domain models/DTOs and `readonly record struct` for value objects (`TicketId`, `HandoffId`, `AgentRole`). No public property setters.
3. **Embedded Ticket Management (ADR-0007)**: Hierarchical decomposition ($Epics \to Stories \to Subtasks$), CLAW dependency scheduling, and inter-agent handoff contracts (`HandoffPacket`).
4. **Multi-Tier Persistent Memory (ADR-0003)**: Working, Episodic, Semantic, and Procedural memory with embedded 64-dim vector similarity and automated post-task consolidation.
5. **Resilient Graph Execution (ADR-0002)**: Workflow nodes with Input (🟢), Success Output (🔵), and Failure/Reject (🔴) ports with circuit breaker fallbacks.
6. **Inference & Key Vault (ADR-0004)**: Dynamic per-role OpenRouter model routing, client-side credential vaulting, and offline simulation fallback.
