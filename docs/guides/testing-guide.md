# Testing Guide & Quality Assurance 🧪🎯

## 1. Test Architecture & Tooling

The testing suite for Carnot Cycle Circus is located in `tests/CarnotCycleCircus.Tests/`. It is built with:
- **[xUnit](https://xunit.net/)** (2.9.2): Modern, extensible test framework.
- **[FluentAssertions](https://fluentassertions.com/)** (8.0.1): Expressive, human-readable assertion syntax.
- **Microsoft.NET.Test.Sdk** & **coverlet.collector**: Test discovery and code coverage analysis.

---

## 2. Test Suite Map

The test project contains 14 specialized test suites covering all core domain services:

| Test File | Target Service / Component | Key Verification Scenarios |
| :--- | :--- | :--- |
| `TicketStoreTests.cs` | `TicketStore` | Ticket CRUD, state machine transitions, dependency DAG ordering, and query filters. |
| `WorkDecompositionTests.cs` | `WorkDecompositionEngine` | Automated TPM/Architect deconstruction of Epics into granular technical subtasks with DAG edges. |
| `HandoffRouterTests.cs` | `HandoffRouter` | Success handoffs, failure remediation loopbacks, and DAG downstream activation. |
| `WorkflowGraphTests.cs` | `GraphWorkflowExecutor` | End-to-end DAG execution, failure port routing, retry counters, and circuit breaker tripping. |
| `PersistentMemoryTests.cs` | `EmbeddedVectorMemoryStore` | Multi-tier storage, 64-dim vector cosine similarity search, composite scoring, and pruning. |
| `ToolSandboxTests.cs` | Tool Sandbox (`IToolDefinition`) | Execution and parameter validation for `WebSearch`, `CSharpSyntaxCheck`, `TestRunner`, `MemoryLookup`, and `AdrWriter`. |
| `ApiKeyVaultTests.cs` | `ApiKeyVaultService` | Credential storage, masking, role-to-key resolution, and connection testing. |
| `OpenRouterClientTests.cs` | `OpenRouterClient` & Resolver | Multi-key resolution hierarchy, request formatting, and sandbox fallback. |
| `AdrDocumentManagerTests.cs` | `AdrDocumentManager` | ADR authoring, markdown rendering, status lifecycle, and bundle export. |
| `StandardsValidatorTests.cs` | `StandardsValidator` | Quality gate enforcement for Features, Bugs (RCA + regression tests), and Epics. |
| `KnowledgeMapTests.cs` | `KnowledgeMapService` | Concept graph creation, edge relationships, and sub-graph query extraction. |
| `SkillImporterTests.cs` | `SkillImporter` & Registry | `SKILL.md` frontmatter parsing, JSON deserialization, and role assignment. |
| `AgentPersonaTests.cs` | `AgentPersona` & Teams | Persona defaults, temperature validation, deliverable isolation prompt enforcement, and archetypes. |
| `EventStreamTests.cs` | `AgentEventStream` | Real-time message streaming, subscriber notification, and bounded queue trimming. |

---

## 3. Running Test Suites

```bash
# Run all test suites
dotnet test CarnotCycleCircus.slnx --logger "console;verbosity=normal"

# Run tests with detailed test case names
dotnet test CarnotCycleCircus.slnx --logger "console;verbosity=detailed"

# Run a specific test class
dotnet test --filter "FullyQualifiedName~WorkflowGraphTests"

# Run a specific test method
dotnet test --filter "FullyQualifiedName~WorkflowGraphTests.ExecuteWorkflowAsync_WithFailureSimulation_ShouldRouteToRemediationAndRecover"
```

---

## 4. Testing Best Practices & Conventions

### 4.1 Isolated State
Each test method must instantiate fresh in-memory instances of stores and services:

```csharp
[Fact]
public void CreateTicket_ShouldStoreTicketAndRaiseEvent()
{
    // Arrange
    var store = new TicketStore();
    var ticket = new TicketItem(
        Id: "TICK-001",
        ParentEpicId: null,
        Title: "Test Ticket",
        Description: "Description",
        Type: TicketType.Feature,
        Status: TicketStatus.Ready,
        AssigneeRole: AgentRole.SoftwareDeveloper,
        CreatedByRole: AgentRole.TechnicalProductManager,
        Priority: TicketPriority.High,
        DependsOnTicketIds: Array.Empty<string>(),
        AcceptanceCriteria: ["Must pass test"],
        Deliverables: Array.Empty<ArtifactItem>(),
        Metadata: new Dictionary<string, string>(),
        CreatedAt: DateTimeOffset.UtcNow
    );

    // Act
    var created = store.CreateTicket(ticket);

    // Assert
    created.Should().NotBeNull();
    store.GetTicketById("TICK-001").Should().Be(ticket);
}
```

### 4.2 Determinism & Time Handling
- Avoid `Thread.Sleep` in tests; use `Task.Delay` or asynchronous completions.
- Never depend on live external network connections during unit tests (use sandbox keys or mock clients).
- Name tests using the `MethodName_Condition_ExpectedResult` convention.
