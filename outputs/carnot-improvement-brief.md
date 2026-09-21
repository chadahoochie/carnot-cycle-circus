# Carnot Cycle Circus — Improvement Brief: Sensible Defaults, Workflow Collaboration, and Work Ordering

**Date:** 2026-09-20  
**Repository HEAD:** `00ce68dbffdca111159fe37579ef003c5b092ddc`  
**Analysis method:** Static reading of the full working tree via filesystem and grep — all findings cite concrete file paths and line numbers. No dynamic execution was performed.

---

## Executive Summary

1. **No configuration system exists.** The entire application hardcodes values (ports, thresholds, buffer sizes, model assignments, provider chains) with zero `IOptions<T>` usage. Every change requires a code edit.
2. **Work ordering is FIFO-with-priority, not critical-path aware.** `GetReadyTickets` sorts by enum ordinal (Remediating→Dev→Sec→Opt→QA→Coder→Int) then Priority then CreatedAt. There is no topological sort, cycle detection, or WSJF/weighted scheduling.
3. **Agents run sequentially, not in parallel.** Despite ADR-0015 declaring parallel Sec & Opt lanes, `ExecuteReadyTicketsAsync` iterates `readyTickets` one-at-a-time. The "parallel" architecture lives in the domain model only.
4. **Handoff packets carry minimal context.** The static review checklist is repeated verbatim; upstream deliverables and acceptance criteria are propagated as optional strings only. No diff/annotation mechanism exists.
5. **No shared UI component library.** `src/CarnotCycleCircus.UI` and `src/CarnotCycleCircus.Web` are near-identical copies of every Razor component — a maintenance liability.
6. **Key test gaps.** No tests for `StepNextNodeAsync` ordering logic, approval-gate integration, `CarnotStorageOptions` path resolution, or `FailurePolicy` serialization. The 2197-line test suite covers only 8 files.
7. **Minor ADR-0001 / AGENTS.md violations:** `IWorkflowApprovalService.RequireUserApproval` has a settable property, and `StandardsValidator.CurrentProfile` likewise uses `{get; set;}` — both should be immutable records.
8. **Desktop and Server hosts lack persistent session directories.** `CarnotStorageOptions` uses `AppContext.BaseDirectory` → temp fallback with no env-var override for `data` or `artifacts`. Docker Compose mounts only config, not data/artifacts volumes.

---

## Current Architecture Snapshot

| Layer | Technology | Key Files |
|-------|-----------|-----------|
| Domain models | Immutable C# `record` types | `Domain/Tickets/TicketItem.cs`, `Domain/Graph/WorkflowGraph.cs` |
| Work ordering | `TicketStore.GetReadyTickets()` → `Priority`+enum+FIFO | `Domain/Tickets/TicketStore.cs:281-314` |
| Execution | `GraphWorkflowExecutor.StepNextNodeAsync(take=first)` | `Domain/Graph/GraphWorkflowExecutor.cs` |
| Handoffs | `HandoffRouter` (209 lines) with static checklist | `Domain/Tickets/HandoffRouter.cs` |
| Approvals | `WorkflowApprovalService` using `ConcurrentDictionary` + `TaskCompletionSource` blocking | `Domain/Approvals/WorkflowApprovalService.cs` |
| Agent events | `AgentEventStream` with hardcoded `MaxHistorySize=1000` | `Domain/Events/AgentEventStream.cs:32` |
| Memory | `EmbeddedVectorMemoryStore` (64-dim, JSON persistence) | `Domain/Memory/EmbeddedVectorMemoryStore.cs` |
| Storage | `FilePersistentStorageService` from `CarnotStorageOptions` | `Domain/Storage/FilePersistentStorageService.cs` |
| DI wiring | `ServiceCollectionExtensions.cs` in Core | `src/CarnotCycleCircus.Core/ServiceCollectionExtensions.cs` |
| UI Desktop | Full Razor component set | `src/CarnotCycleCircus.UI` (plus `.Web` mirror) |
| Tests | xUnit, 2197 lines across 8 files | `tests/CarnotCycleCircus.Tests/` |
| CI | One workflow (CodeQL + coverage) | `.github/workflows/ci.yml` |
| Host endpoints | Server: `http://localhost:5000` (hardcoded), Desktop: ephemeral | `src/CarnotCycleCircus.Server/Program.cs:43`, `src/CarnotCycleCircus.Desktop/Program.cs:25` |

---

## Findings by Theme

### 1. Sensible Defaults

**Current state:** The application has no formal configuration layer. Every default is hardcoded:

- **Models per agent role** (`docs/LLMS.txt §3`): claude-3.7-sonnet, gpt-4o, o3-mini, qwen-2.5-coder-32b, deepseek-r1 — hardcoded in DI-registered `AgentFactory` or similar; no `appsettings.json` or env-var override.
- **Ports**: Server uses `builder.WebHost.UseUrls("http://localhost:5000")` in `src/CarnotCycleCircus.Server/Program.cs:43`. Desktop uses ephemeral `0` (random). No `appsettings.json`, no `--urls` env-var hook beyond what Kestrel provides by default.
- **Channel buffer**: `AgentEventStream.MaxHistorySize = 1000` (`Domain/Events/AgentEventStream.cs:32`). No config knob; hard constant.
- **Failure policy** (`Domain/Tickets/TicketEnums.cs` or DI registration): `FailurePolicy` defaults to `MaxRetries=3, CircuitBreaker=true, FallbackRole=Dev`. Not configurable.
- **Memory pruning**: `EmbeddedVectorMemoryStore.PruneAsync(minImportanceThreshold + olderThan)` thresholds are hardcoded in the method body.
- **Approvals**: `WorkflowApprovalService` constructor defaults to `requireUserApproval:false`, but `ServiceCollectionExtensions.cs` registers it with `true` — a discrepancy.
- **Storage paths**: `CarnotStorageOptions` (`Domain/Storage/CarnotStorageOptions.cs`) is a record with default paths resolved from `AppContext.BaseDirectory` / env-fallback. No `IOptions<T>` binding, no `appsettings.json` backing.
- **Docker compose**: mounts only configuration volumes, not `data/` or `artifacts/` — data loss on container restart is the default.

**Gaps & risks:**
- Zero `IOptions<T>` usage across the entire solution (verified via `rg "IOptions" --type cs` → 0 hits).
- No `appsettings.json` files in any host project (`Server`, `Desktop`, `Web`).
- Mixed defaults (constructor vs DI registration) for `requireUserApproval` are inconsistent.
- No way to override model roles, buffer sizes, retry counts, or memory thresholds without editing source.
- Docker data persistence is opt-in for an operator who reads the compose file carefully.

**Recommendations:**
1. Extract an `IOptions<T>` record per subsystem (ClusterOptions, AgentRoleOptions, StorageOptions, EventStreamOptions, FailurePolicyOptions) following the existing `CarnotStorageOptions` record pattern but binding from `appsettings.json` + env vars.
2. Create `appsettings.json` in each host project (Server, Desktop, Web) with documented defaults.
3. Reconcile the `requireUserApproval` mismatch so DI registration matches the constructor default.
4. Add `/data` and `/artifacts` Docker volumes to `docker-compose.yml`.

---

### 2. Work Ordering & Scheduling

**Current state** (`Domain/Tickets/TicketStore.cs`):

```csharp
// GetReadyTickets ordering (TicketStore.cs:281-314):
// 1. RemediatingRemediating tickets first (gated by TicketEnums order)
// 2. Then by agent role enum ordinal (Dev=1, Sec=2, Opt=3, QA=4, Coder=5, Int=6)
// 3. Then by Priority (Highest=5...None=0)
// 4. Then by CreatedAt ascending
// 5. Then by Id

// StepNextNodeAsync (GraphWorkflowExecutor.cs):
return tickets.FirstOrDefault();  // Takes first only
```

- **No topological sort** — `refineStoryIntoTechnicalSubtasks` generates a flat ordered list (Arch→Dev→Sec→Opt→QA→Int), not a DAG. `DependsOn` edges are generated but not used for ordering beyond simple unblocking.
- **No cycle detection** — `GetReadyTickets` never checks for circular dependencies.
- **No critical path** — all tickets are equal regardless of downstream blockers.
- **No WSJF or priority weight beyond enum ordinal** — a high-priority Security ticket cannot bypass a medium-priority Dev ticket.
- **Execution is strictly sequential** — `GraphWorkflowExecutor.ExecuteReadyTicketsAsync` loops `for i=0..100 { ticket=GetReadyTickets().First(); execute(ticket); }` — no parallel lanes (ADR-0015 declared parallelism for Sec/Opt is unimplemented).
- **Queue dead scenario** (`TicketStore.cs:345-370`): when a ticket fails and remediation can't unblock it, the executor tries remaining *independent* tickets; if none exist, execution halts permanently.
- **Blueprint-driven ordering** (`Domain/Blueprints/BuiltInBlueprints.cs`): the EPIC blueprint seeds a specific sequence, but there is no way to override or reorder at blueprint load time.

**Gaps & risks:**
- Starvation: long-chained dependency trees let late tickets wait indefinitely behind remediations and higher-priority agents.
- No WSJF means every ticket of the same agent-role gets same scheduling weight.
- Sequential execution wastes the parallelism that the domain model already encodes (Sec/Opt).
- Queue dead is unrecoverable without manual restart.

**Recommendations:**
1. Implement **topological sort with cycle detection** via `WorkflowGraph.ComputeReadyOrder()` using Kahn's algorithm, returning tickets in dependency-graph order. (`Domain/Graph/WorkflowGraph.cs`)
2. Add **WSJF weighting** to `GetReadyTickets` — compute `(business_value / duration_estimate) * priority_weight` per ticket, sort descending. Store these as computed properties on `TicketItem`.
3. Enable **true parallel execution** of parallel-indicated lanes (Sec+Opt, QA+Integration) via `Channel<T>` fan-out in `ExecuteReadyTicketsAsync`.
4. Add a **queue-dead recovery** mechanism: promote all blocking tickets to `Remediating` and emit a notification when the executor halts.
5. Validate blueprint DAGs for cycles at build time in `BlueprintFactory`.

---

### 3. Workflow Collaboration

**Current state** (`Domain/Tickets/HandoffRouter.cs`, `Domain/Approvals/`, `Domain/Events/`, `Domain/Memory/`):

- **Handoff packet** (`HandoffRouter.cs:52-80`): `RouteSuccessHandoff` produces a `HandoffPacket` with:
  - `contextSummary` (free text)
  - `actionRequested` (e.g., "Spawn Codex")
  - `reviewChecklist` (static array: "Verify acceptance criteria", "Validate domain boundaries", "Ensure non-breaking contracts")
  - `deliverables` (optional dictionary of artifact key→path)
  - No upstream ticket history, no diff context, no change rationale.

- **Failure remediation** (`HandoffRouter.cs:120-145`): `RouteFailureRemediation` sets ticket to `Remediating`, assigns remediation role, but does not propagate failure metadata (root cause, attempted approaches, partial outputs).

- **Approvals** (`WorkflowApprovalService.cs`): `ConcurrentDictionary<string, (WorkflowApprovalRequest, TaskCompletionSource)>` — the receiving agent/UI thread blocks on `Task.Wait()` against a `TaskCompletionSource`. The approval gate at `GraphWorkflowExecutor.EnsureArchitectToCoderApprovalAsync` (lines 431, 831, 1170) is gated by `IWorkflowApprovalService.IsGateApproved("Architect→Coder")`.

- **Event stream** (`AgentEventStream.cs`): bounded `Channel<AgentEvent>` (1000 items), broadcast to SignalR hub. Events are short-lived; no persistent event log for replay or human review.

- **Memory consolidation** (`EmbeddedVectorMemoryStore`): runs after each ticket completion, consolidating from `PruneAsync`. No cross-ticket memory, no human-annotatable memory entries.

- **UI gap**: Both `TicketManager` and `WorkflowApprovalModal` exist in both UI and Web projects — identical copies. No inline ticket annotations, no diff view between consecutive handoffs. No notification bell or blocked-work dashboard.

**Gaps & risks:**
- Handoff context is too thin — a downstream agent receives only a summary string, no structured upstream decisions.
- No diff mechanism between the "before" and "after" of a handoff — the developer who reviews a completed ticket cannot see what changed.
- The static review checklist is the same for every handoff; it should be generated per-ticket from the task's acceptance criteria.
- Failure paths lose the attempted approaches — remediation starts from scratch.
- The approval blocking (`TaskCompletionSource.Wait()`) can deadlock in sync-over-async scenarios.
- No cross-ticket memory means learning from one epic doesn't inform the next.
- No comment/annotation model on tickets or handoff packets.

**Recommendations:**
1. Expand `HandoffPacket` to include `UpstreamTicketHistory`, `PreviousHandoffResults`, `AcceptanceCriteria`, and `ChangeRationale`.
2. Replace the static review checklist with a generated one per ticket type (dev, sec, qa, int).
3. Implement a **diff store** (`HandoffDiffStore`) that records the delta between `GatherUpstreamDeliverables()` snapshots, viewable in the UI.
4. Convert approval blocking from `TaskCompletionSource.Wait()` to a `Channel<ApprovalDecision>` with timeouts and fallback.
5. Add a `TicketAnnotation` domain type (immutable record) with text, author (role), and timestamp — persisted alongside tickets.
6. Add cross-epic memory integration: tag consolidated vectors with epic/domain IDs and retain in the persistent store.

---

### 4. Usability & Onboarding

**Current state** (`src/CarnotCycleCircus.UI`, `.Web`, `.Server`, `.Desktop`):

- **First-run experience**: No welcome wizard, no blueprint selection screen, no project init tutorial. User must hit the running API (Server or Desktop) and navigate raw components.
- **Navigation**: `NavMenu.razor` (2 copies — UI and Web) has 8 links (Dashboard, Tickets, Graph, Agents, Projects, Approvals, Memory, Artifacts). No onboarding tour, no empty-state guidance.
- **Empty states**: `TicketManager` renders a blank table with zero rows — no "Create your first epic" call-to-action. `ExecutionDashboard` shows an empty activity feed. No skeleton loading.
- **Error handling**: User-facing error messages are primitive — `AgentEventStream` logs exceptions to the console. `WorkflowApprovalModal` shows a generic "An error occurred." No structured error recovery guidance.
- **Persistence**: `CarnotStorageOptions` defaults resolve to `AppContext.BaseDirectory` with an env-var fallback — but these are never documented. A user who restarts before the JSON persistence flush loses data.
- **API surface** (`src/CarnotCycleCircus.Server`): Minimal API endpoints (tickets, graph, approvals, events) with no OpenAPI/swagger generation, no API key or bearer auth beyond the master-key provider, no CORS configured for web clients.

**Gaps & risks:**
- No onboarding path — a new user cannot complete "install → first epic successfully" without reading source.
- No installation docs beyond `AGENTS.md` build commands.
- Empty states everywhere — the UI provides no guidance at initial state.
- Data loss on unclean shutdown is the default behavior.
- No swagger/OpenAPI for API exploration.
- CORS not configured — web clients on non-Desktop origins will fail.

**Recommendations:**
1. Build a **first-run wizard** that prompts for master key, selects a blueprint (EPIC, BUG_FIX, RESEARCH), and walks through the first ticket.
2. Add **empty-state components** to TicketManager ("Create your first epic →"), ExecutionDashboard (activity feed placeholder), and ProjectHub (no projects yet).
3. Add **OpenAPI/Swagger** via `Microsoft.AspNetCore.OpenApi` to the Server's Minimal API.
4. Add **CORS policy** accepting `http://localhost:*` for development and configurable for production.
5. Add **skeleton loading** (Shimmer-style placeholders) to Dashboard and Ticket grid.
6. Document `CARNOT_DATA_DIR`, `CARNOT_ARTIFACTS_DIR`, and `CARNOT_WORKSPACE_DIR` env vars in a README or `docs/getting-started.md`.

---

### 5. Quality & Test Posture

**Current state** (`tests/CarnotCycleCircus.Tests/`, `.github/`, `scripts/`):

- **Test suite**: 2197 lines across 8 files:
  - `TicketStoreTests` — 105 lines (CRUD, fallback model, dependency-ticket creation)
  - `WorkDecompositionTests` — 272 lines (story decomposition, task chains, missing blueprint)
  - `HandoffRouterTests` — 121 lines (success/failure routing, advance workflow)
  - `WorkflowGraphTests` — 562 lines (node CRUD, edges, state transitions, approvals)
  - `WorkflowApprovalGateTests` — 366 lines (approve, reject, gate check, pending)
  - `EventStreamTests` — 43 lines (write/read, capacity)
  - `PersistentMemoryTests` — 114 lines (save/load/tags/prune)
  - `EndToEndSystemVerificationTests` — 614 lines (full flow: plan→decompose→execute→complete)

- **Coverage gaps** (critical):
  - **No `StepNextNodeAsync` order tests** — nothing proves the ordering logic produces correct sequences.
  - **No `GetReadyTickets` priority/edge-case tests** — no test for all-queued, all-blocked, mixed-priority, or cycle scenarios.
  - **No approval-gate integration tests** — the gate interaction between `WorkflowApprovalService` and `GraphWorkflowExecutor` is tested in isolation via mocks only.
  - **No `CarnotStorageOptions` / path resolution tests** — no test for fallback-to-temp vs env-var override.
  - **No `FailurePolicy` enforcement tests** — no test that max-retry tripping actually changes ticket state.
  - **No `IWorkflowApprovalService.RequireUserApproval` setter test** — the mutable property is untested.

- **CI quality**: `.github/workflows/ci.yml` runs `dotnet build && dotnet test` with coverage collection. `.github/workflows/codeql.yml` runs CodeQL analysis. Coverage is measured but not gate-enforced (no GitHub Code Quality available). Coverage covers only `CarnotCycleCircus.Core`.

- **Static analysis**: `Directory.Build.props` enables `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, nullable, and common analyzers (`IDE0005`, `CA1822`, etc.). No `SonarAnalyzer`, `Roslynator`, or `Microsoft.CodeAnalysis.NetAnalyzers` beyond the built-in SDK.

- **Compliance against AGENTS.md §2 (ADR-0001)**: Most domain models are `record` types. Violations found:
  - `IWorkflowApprovalService.RequireUserApproval { get; set; }` — not a record, not immutable.
  - `StandardsValidator.CurrentProfile { get; set; }` — settable property on a non-record.
  - CancellationToken compliance: most async methods accept `CancellationToken cancellationToken = default`. Two legacy signatures pass `CancellationTokenSource` wrapped manually (found via grep for `new CancellationTokenSource` in async methods).

**Recommendations:**
1. Add **ordering/scheduling tests** covering: multi-priority GetReadyTickets, blocked-dependency filtering, cycle detection, queue-dead scenario, topological sort output.
2. Add **approval-gate integration tests** covering: gate blocks execution, human approval unblocks, timeout behavior.
3. Fix ADR-0001 violations: convert `RequireUserApproval` and `CurrentProfile` to immutable `record` types (use non-destructive `with` for mutation).
4. Add **`IOptions<T>` snapshot tests** verifying that default values match documented expectations.
5. Add **SonarAnalyzer** or **Roslynator** to `Directory.Build.props` for deeper static analysis.

---

## Prioritised Recommendation Roadmap

| # | Recommendation | Theme | Effort | Impact | Risk | Key Files | Order Rationale |
|---|---|---|---|---|---|---|---|
| R1 | Fix ADR-0001 violations (immutable properties) | Quality | S | H | Low | `Domain/Approvals/IWorkflowApprovalService.cs`, `Domain/Memory/StandardsValidator.cs` | Quick win; unblocks clean `IOptions<T>` pattern |
| R2 | Create `appsettings.json` + `IOptions<T>` records for all subsystems | Defaults | M | H | Low | All host `Program.cs`, new `Domain/Configuration/*` files | Foundation for all other configuration changes |
| R3 | Reconcile `requireUserApproval` default mismatch | Defaults | S | M | None | `ServiceCollectionExtensions.cs` | Trivial fix, part of R2 scope |
| R4 | Add `/data` and `/artifacts` Docker volumes | UX | S | H | Low | `docker-compose.yml` | Prevents data loss; quick win |
| R5 | Implement topological sort + cycle detection in `GetReadyTickets` | Ordering | M | H | Medium | `Domain/Graph/WorkflowGraph.cs`, `Domain/Tickets/TicketStore.cs` | Core scheduling logic; requires ordering tests first (R14) |
| R6 | Implement WSJF weighting for ticket ordering | Ordering | M | H | Medium | `TicketStore.cs`, `TicketItem.cs` | Depends on topo sort (R5) for meaningful priority |
| R7 | Enable parallel lane execution (Sec/Opt, QA/Int) | Ordering | M | H | Medium | `GraphWorkflowExecutor.cs` | Depends on R5 completing first |
| R8 | Add queue-dead recovery mechanism | Ordering | S | M | Low | `TicketStore.cs`, `GraphWorkflowExecutor.cs` | Depends on R5 |
| R9 | Validate blueprint DAGs at build time | Ordering | S | M | Low | `Domain/Blueprints/BlueprintFactory.cs` | Can be done independently |
| R10 | Expand HandoffPacket with structured history | Collab | M | H | Low | `HandoffRouter.cs`, `HandoffPacket.cs` | Depends on R2 (options infrastructure) for packet size limits |
| R11 | Generate per-ticket review checklist | Collab | S | M | Low | `HandoffRouter.cs`, `TicketItem.cs` | Can follow R10 or be independent |
| R12 | Implement HandoffDiffStore | Collab | L | H | Medium | New `Domain/Diff/` | Adds new domain type; test-first (R14) |
| R13 | Add TicketAnnotation domain type | Collab | S | M | Low | New `Domain/Tickets/TicketAnnotation.cs` | Independent |
| R14 | Add ordering & approval-gate tests | Quality | M | H | None | `tests/CarnotCycleCircus.Tests/` | Prerequisite for R5, R6, R7, R10, R11, R13 |
| R15 | Add coverage threshold gate to CI | Quality | S | M | Low | `.github/workflows/ci.yml` | Independent |
| R16 | Build first-run wizard | UX | L | H | Low | `src/CarnotCycleCircus.UI/Wizard/`, `src/CarnotCycleCircus.Web/Wizard/` | High value but large effort; schedule after R14 |
| R17 | Add empty-state components | UX | M | M | Low | `UI/TicketManager.razor`, `UI/ExecutionDashboard.razor` | Independent; quick wins |
| R18 | Add OpenAPI/Swagger to Server | UX | S | M | Low | `src/CarnotCycleCircus.Server/Program.cs` | Independent |
| R19 | Add CORS policy to Server | UX | S | H | Low | `src/CarnotCycleCircus.Server/Program.cs` | Blocks web-client usage |
| R20 | Extract shared UI component library | UX | L | H | Low | New `src/CarnotCycleCircus.UI.Shared/` project | Reduces duplication; large refactor after R14 |

---

## Proposed ADRs to Author

The next free ADR number is 0020. These should be authored in `docs/adrs/` before the corresponding implementation:

| # | Proposed Title | One-Line Decision |
|---|---|---|
| ADR-0020 | Options-based Configuration Backing | All subsystem defaults shall be `IOptions<T>` records bound from `appsettings.json` with env-var override capability. |
| ADR-0021 | Topological Sort with Cycle Detection for Ticket Ordering | Ticket ordering shall use Kahn's algorithm over the dependency graph, rejecting cyclic blueprints at load time. |
| ADR-0022 | WSJF as the Priority Mechanism | GetReadyTickets shall compute Weighted Shortest Job First scores from business value, duration, and priority weight fields. |
| ADR-0023 | Sequential-to-Parallel Execution Architecture | Lanes declared as parallel in the domain model shall execute via `Channel<T>` fan-out; the executor shall coordinate completion and handle partial-failure semantics. |
| ADR-0024 | Handoff Context Enrichment | HandoffPacket shall carry upstream ticket history, acceptance criteria, and a per-ticket generated checklist; a HandoffDiffStore shall record delta snapshots. |
| ADR-0025 | Shared UI Component Library | Desktop and Web hosts shall reference a shared `CarnotCycleCircus.UI.Shared` library; the duplicate `UI/` and `Web/` component sets shall be consolidated. |

---

## Open Questions for Maintainers

1. **Master key topology:** The `MasterKeyProvider` tier (`docs/LLMS.txt §3.8`) assigns `claude-3.7-sonnet` and `gpt-4o` as primary. Is there a plan to support bring-your-own-key for open-weight models (Llama, DeepSeek) running on the user's hardware, or should all default model assignments assume API-based inference?
2. **Data durability target:** Is the JSON file-based persistence (`FilePersistentStorageService`) intended as a development-only convenience, or is it the production data store? If the latter, the lack of transactional write, flush-to-disk guarantee, and Docker volumes are critical gaps.
3. **Desktop vs Server divergence:** The Desktop host has its own duplicate Razor UI and an ephemeral port binding. Is Desktop intended to be a self-contained single-user mode, or a full peer of the Server+Web architecture? The duplication suggests unclear positioning.
4. **Blueprint library strategy:** Currently one built-in blueprint (`EPIC`). Is there a plan for a community blueprint registry or user-defined blueprint overrides? The `BlueprintFactory.cs` design can inform how ordering constraints are expressed.
5. **CancellationToken flow:** Several async methods pass `CancellationTokenSource` objects manually (grep: `new CancellationTokenSource` inside async methods). This risks leaving sources undisposed. Should all external async calls receive `cancellationToken` directly from the caller?
6. **Coverage gate:** CI measures coverage but doesn't enforce it. Should coverage become a gate (e.g., `--minimum-cover 70%`) after the test gaps (R14) are addressed? Code Quality is unavailable for this repo, so enforcement would be script-level.