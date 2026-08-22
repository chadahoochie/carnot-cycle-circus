# LLM & Autonomous Agent Interaction Guide 🤖📋

This guide defines how **Large Language Models (LLMs)**, autonomous coding agents, and external orchestrators should interact with the **Carnot Cycle Circus** codebase, execute tools, and structure deliverables.

---

## 1. Core Operating Principles for LLMs

When generating code, analyzing tickets, or reviewing deliverables in this repository, LLMs must adhere to three non-negotiable rules:

1. **Deliverable Isolation Contract (ADR-0005)**:
   - Chat dialogue, internal monologue, and thought logs may be witty, cynical, or comedic.
   - **All formal deliverables (PRDs, ADRs, C# code, STRIDE models, Benchmark tables, QA scorecards, and Ticket fields) MUST remain 100% professional, standard-compliant, rigorous, and completely free of joke text or sarcastic phrasing.**
2. **Immutable Domain Architecture (ADR-0001)**:
   - Generate C# `record` types for entities/DTOs and `readonly record struct` for value objects.
   - Never generate mutable classes with public property setters for domain models.
   - Use non-destructive mutation (`with { ... }`).
3. **Performance & Concurrency Dogma**:
   - Use `ReadOnlyMemory<byte>`, `ReadOnlySpan<char>`, `ValueTask`, and `System.Threading.Channels.Channel<T>`.
   - Every asynchronous method must take `CancellationToken cancellationToken = default`.

---

## 2. Prompt Context Ingestion Schema

When preparing prompt contexts for agent roles, orchestrators should inject structured context blocks:

```markdown
# Role Persona
{{Persona.SystemPrompt}}

# Deliverable Isolation Contract
DELIVERABLE ISOLATION CONTRACT: All technical deliverables MUST remain strictly professional, unambiguous, rigorous, and completely free of joke text or sarcastic phrasing.

# Current Ticket Context
- Ticket ID: {{Ticket.Id}}
- Type: {{Ticket.Type}}
- Title: {{Ticket.Title}}
- Description: {{Ticket.Description}}
- Acceptance Criteria:
{{Ticket.AcceptanceCriteria}}

# Hierarchical Persistent Memory Context
{{InjectedMemories}}

# AI Knowledge Map Context
{{InjectedKnowledgeNodes}}
```

---

## 3. Tool Execution Schemas

Agents have access to specialized tools via the `IToolDefinition` interface. Tool calls and responses follow JSON parameter contracts:

### 3.1 `csharp_syntax_check`
- **Purpose**: Verifies structural syntax, balanced braces, and parentheses before emitting C# code.
- **Parameters**: `{"code": "<csharp_code_snippet>"}`
- **Response**: `{"status": "ok", "message": "Syntax Check Passed", "metadata": {"LinesCount": "42"}}`

### 3.2 `test_runner`
- **Purpose**: Executes automated unit/integration test suites against code artifacts.
- **Parameters**: `{"testSuite": "<suite_filter_expression>"}`
- **Response**: `{"status": "ok", "message": "All Acceptance Criteria Tests PASSED", "metadata": {"Passed": "18", "Coverage": "96.4%"}}`

### 3.3 `memory_lookup`
- **Purpose**: Queries hierarchical persistent memory for past decisions, patterns, and lessons learned.
- **Parameters**: `{"query": "<search_query>", "type": "<Optional: Working|Episodic|Semantic|Procedural>"}`
- **Response**: Formatted list of top matching memory entries with similarity scores.

### 3.4 `adr_writer`
- **Purpose**: Generates standardized Architectural Decision Records in MADR/Nygard markdown format.
- **Parameters**: `{"title": "<title>", "context": "<context>", "decision": "<decision>", "consequences": "<tradeoffs>"}`
- **Response**: Rendered markdown string and generated `ADR-XXXX` ID.

### 3.5 `web_search`
- **Purpose**: Searches technical documentation and architectural best practices.
- **Parameters**: `{"query": "<technical_search_term>"}`

---

## 4. Code Generation Checklist for LLMs

Before emitting C# code for this codebase, verify against this checklist:
- [ ] Uses C# 13 syntax (primary constructors, collection expressions `[...]`, pattern matching).
- [ ] All entities are immutable `record` or `readonly record struct`.
- [ ] No public property setters (`set;`).
- [ ] All async methods accept `CancellationToken cancellationToken = default`.
- [ ] No synchronous blocking (`.Result`, `.Wait()`).
- [ ] Hot path methods use `ValueTask` and `Span<T>` / `Memory<T>`.
- [ ] Thread-safe collections used for in-memory stores (`ConcurrentDictionary`, `ConcurrentQueue`).
- [ ] 0 compiler warnings (compiles under `TreatWarningsAsErrors`).
- [ ] Accompanied by xUnit + FluentAssertions test suite.
