# Carnot Cycle Circus - Engineering Assistant Guidelines

Autonomous Engineering Agent Orchestration Platform built in **.NET 10 / C# 13** with interactive **Blazor** UI.

## Build & Test Commands
```bash
# Build entire solution
dotnet build CarnotCycleCircus.slnx

# Run all test suites
dotnet test CarnotCycleCircus.slnx --logger "console;verbosity=detailed"

# Run Blazor web application
dotnet run --project src/CarnotCycleCircus.Web
```

## Solution Structure
- `src/CarnotCycleCircus.Core/`: Domain models, ticket management & decomposition engine, persistent memory (OpenViking-style), OpenRouter inference hub, agent tool sandbox, and connectable workflow graph. [Core Guidelines](src/CarnotCycleCircus.Core/CLAUDE.md)
- `src/CarnotCycleCircus.Web/`: Blazor frontend with interactive Ticket Studio (Kanban/DAG), Memory Inspector, Team Definition Studio, ADR Hub, Standards Manager, Knowledge Map Explorer, and Real-Time Execution Dashboard. [Web Guidelines](src/CarnotCycleCircus.Web/CLAUDE.md)
- `tests/CarnotCycleCircus.Tests/`: xUnit & FluentAssertions test suites verifying tickets, tools, memory, inference, standards, and workflow routing. [Testing Guidelines](tests/CarnotCycleCircus.Tests/CLAUDE.md)
- `skills/`: Preserved engineering, architecture, and .NET best-practice skills.

## Agent Guidance: Preserved Skills Routing
<!-- BEGIN DOTNET-SKILLS COMPRESSED INDEX -->
[dotnet-skills]|IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
|flow:{skim repo patterns -> consult skills/ by name -> implement smallest-change -> note conflicts}
|route:
|architecture:{engineering-multi-agent-systems-architect,project-structure}
|csharp:{csharp-coding-standards,csharp-type-design-performance,csharp-concurrency-patterns,csharp-pro}
|di-config:{microsoft-extensions-dependency-injection,local-tools,package-management}
|skills-index:{skills-index-snippets}
<!-- END DOTNET-SKILLS COMPRESSED INDEX -->

## Core Architecture Principles
1. **Immutable Domain & Strong Types**: Use `record` for domain models/DTOs and `readonly record struct` for value objects (`TicketId`, `HandoffId`, `AgentRole`).
2. **Embedded Ticket Management**: Hierarchical decomposition ($Epics \to Stories/Features/Bugs \to Subtasks$), DAG dependency scheduling, and inter-agent handoff contracts (`HandoffPacket`).
3. **Multi-Tier Persistent Memory**: Working, Episodic, Semantic, and Procedural memory with embedded vector similarity and consolidation.
4. **Resilient Graph Execution**: Workflow nodes with Input, Success Output, and Failure/Reject ports with circuit breaker fallbacks.
