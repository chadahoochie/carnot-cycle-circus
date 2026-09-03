# CarnotCycleCircus.Core Guidelines

Core domain library and agent orchestration engine for Carnot Cycle Circus.

## Architecture & Responsibilities
- **Domain/Tickets**: `TicketItem`, `HandoffPacket`, `ITicketStore`, `WorkDecompositionEngine`, `HandoffRouter`. Implements hierarchical ticket management, DAG dependency resolution, and inter-agent handoff contracts.
- **Domain/Projects**: `Project`, `IProjectManager`, `IActiveProjectContext` (ADR-0018). First-class project container that scopes tickets, artifacts, telemetry, and approvals via `ProjectId`; separate from application-level concerns (agents, teams, skills, memory, models, standards).
- **Domain/Memory**: Multi-tier persistent memory (Working, Episodic, Semantic, Procedural) with embedded vector similarity and consolidation.
- **Domain/Inference**: Dynamic multi-key OpenRouter inference router, API Key Vault, and offline scenario simulator.
- **Domain/Tools**: Agent tool execution sandbox (`WebSearchTool`, `CSharpSyntaxCheckTool`, `TestRunnerTool`, `MemoryLookupTool`, `AdrWriterTool`).
- **Domain/Graph**: Connectable workflow DAG engine with Input, Output, and Failure/Reject ports, circuit breakers, and loopback remediation.
- **Domain/Docs**: Architectural Decision Records (ADRs) and project documentation generator.
- **Domain/Standards**: Configurable engineering standards policies and quality gates.
- **Domain/Knowledge**: AI knowledge map graph and semantic sub-graph queries.
- **Domain/Events**: Real-time event streaming and message bus (`AgentEventStream`).

## Coding Standards
1. **Records & Value Objects**: Use `record` for DTOs/Entities and `readonly record struct` for identifiers.
2. **Zero Allocation**: Avoid unnecessary object creation in high-frequency loops; use `Span<T>` and `ReadOnlyMemory<T>` where appropriate.
3. **Cancellation & Async**: All asynchronous methods must accept `CancellationToken cancellationToken = default`.
4. **Result Types & Pure Methods**: Prefer functional return types and avoid throwing exceptions for expected business logic validations.
