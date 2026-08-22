# CarnotCycleCircus.Tests Guidelines

Unit and integration test suites for Carnot Cycle Circus.

## Frameworks & Tooling
- **xUnit** for test execution.
- **FluentAssertions** for expressive and maintainable assertions.
- **Microsoft.NET.Test.Sdk** and **coverlet.collector** for test discovery and code coverage.

## Test Suite Structure
- `TicketStoreTests.cs`: Ticket CRUD, state machine transitions, dependency DAG ordering, and validation rules.
- `WorkDecompositionTests.cs`: TPM and Lead Architect hierarchical work decomposition and subtask generation.
- `HandoffRouterTests.cs`: Inter-agent handoff packet generation, DAG progression, and failure loop remediation.
- `ToolSandboxTests.cs`: Execution and sandboxing of tools (`WebSearch`, `CSharpSyntaxCheck`, `TestRunner`, `MemoryLookup`, `AdrWriter`).
- `PersistentMemoryTests.cs`: Multi-tier memory operations, cosine similarity vector search, and consolidation.
- `ApiKeyVaultTests.cs`: Credential storage, per-role key mapping, and batch swapping.
- `OpenRouterClientTests.cs`: Multi-key routing, auth headers, and simulation fallbacks.
- `AdrDocumentManagerTests.cs`: ADR lifecycle transitions, markdown formatting, and export.
- `StandardsValidatorTests.cs`: Feature/Bug/Spike compliance checks against configured standards.
- `KnowledgeMapTests.cs`: Graph building, edge queries, and context-efficient sub-graph retrieval.
- `SkillImporterTests.cs`: SKILL.md, YAML, and JSON parsing and capability validation.
- `WorkflowGraphTests.cs`: Success path routing, failure/reject port routing, and circuit breaker tripping.
- `EventStreamTests.cs`: Message streaming, observer subscriptions, and replay log validation.

## Testing Best Practices
1. **Isolated State**: Create fresh in-memory stores and mock/stub services for every test.
2. **Determinism**: Avoid Thread.Sleep; use async task completions or mock time providers.
3. **Descriptive Names**: Name tests using `Given_When_Then` or `MethodName_Condition_ExpectedResult` conventions.
