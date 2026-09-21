# Provenance Sidecar — Carnot Cycle Circus Improvement Brief

**Generated:** 2026-09-20  
**Repository HEAD:** `00ce68dbffdca111159fe37579ef003c5b092ddc`  
**Working tree:** clean static read (no mutations made)

## Research Lanes Run

| Lane | Focus Area | Status |
|------|-----------|--------|
| defaults | Sensible defaults (IOptions<T>, ports, buffers, model roles, thresholds, Docker volumes) | ✅ Completed |
| ordering | Work ordering & scheduling (GetReadyTickets, StepNextNodeAsync, topological sort, parallel execution, queue-dead, blueprints) | ✅ Completed |
| collab | Workflow collaboration (HandoffRouter, approvals, event stream, memory, ticket annotations, diff store, UI) | ✅ Completed |
| ux | Usability & onboarding (first-run wizard, empty states, OpenAPI, CORS, persistence, skeleton loading, shared UI library) | ✅ Completed |
| quality | Quality & test posture (test coverage gaps, CI, static analysis, AGENTS.md compliance, ADR-0001 violations) | ✅ Completed |

All five lanes completed via direct static reading. No subagent workflow was used (previous attempt failed due to OpenRouter in-flight budget exhaustion).

## Source Files Cited in the Brief

- `src/CarnotCycleCircus.Server/Program.cs:43` — hardcoded port 5000
- `src/CarnotCycleCircus.Desktop/Program.cs:25` — ephemeral port 0
- `src/CarnotCycleCircus.Core/Domain/Tickets/TicketStore.cs:281-314` — GetReadyTickets ordering logic
- `src/CarnotCycleCircus.Core/Domain/Tickets/TicketStore.cs:345-370` — queue-dead scenario
- `src/CarnotCycleCircus.Core/Domain/Tickets/HandoffRouter.cs:52-80` — RouteSuccessHandoff packet structure
- `src/CarnotCycleCircus.Core/Domain/Tickets/HandoffRouter.cs:120-145` — RouteFailureRemediation
- `src/CarnotCycleCircus.Core/Domain/Graph/GraphWorkflowExecutor.cs` — StepNextNodeAsync and ExecuteReadyTicketsAsync
- `src/CarnotCycleCircus.Core/Domain/Graph/GraphWorkflowExecutor.cs:431,831,1170` — approval gates
- `src/CarnotCycleCircus.Core/Domain/Approvals/WorkflowApprovalService.cs` — approval service implementation
- `src/CarnotCycleCircus.Core/Domain/Events/AgentEventStream.cs:32` — MaxHistorySize=1000
- `src/CarnotCycleCircus.Core/Domain/Memory/EmbeddedVectorMemoryStore.cs` — memory prune thresholds
- `src/CarnotCycleCircus.Core/Domain/Storage/CarnotStorageOptions.cs` — storage options record
- `src/CarnotCycleCircus.Core/Domain/Storage/FilePersistentStorageService.cs` — file persistence
- `src/CarnotCycleCircus.Core/ServiceCollectionExtensions.cs` — DI registrations
- `src/CarnotCycleCircus.Core/Domain/Blueprints/BuiltInBlueprints.cs` — EPIC blueprint
- `src/CarnotCycleCircus.Core/Domain/Blueprints/BlueprintFactory.cs` — blueprint factory
- `src/CarnotCycleCircus.Core/Domain/Tickets/TicketItem.cs` — ticket domain model
- `src/CarnotCycleCircus.Core/Domain/Tickets/TicketEnums.cs` — FailurePolicy / role enums
- `src/CarnotCycleCircus.Core/Domain/Memory/StandardsValidator.cs` — `CurrentProfile` setter violation
- `src/CarnotCycleCircus.Core/Domain/Approvals/IWorkflowApprovalService.cs` — `RequireUserApproval` setter violation
- `src/CarnotCycleCircus.UI/*.razor` — Desktop Razor components
- `src/CarnotCycleCircus.Web/*.razor` — Web Razor components (near-identical copies)
- `tests/CarnotCycleCircus.Tests/*.cs` — test files (8 files, 2197 lines)
- `.github/workflows/ci.yml` — CI pipeline
- `.github/workflows/codeql.yml` — CodeQL analysis
- `docs/AGENTS.md` — project agent guide & non-negotiable rules
- `docs/LLMS.txt` — condensed machine-readable spec
- `docs/adrs/` — architectural decision records
- `Directory.Build.props` — static analysis configuration
- `Directory.Packages.props` — package version management
- `docker-compose.yml` — Docker orchestration
- `global.json` — SDK version pinning

## Methodology

All findings derive from static reading of the working tree at the git HEAD indicated above. Tools used: `bash` (ls, grep, find, rg, git), `read` (file inspection). No code was executed, no dynamic analysis was performed. File paths and line numbers were verified by reading the current state of each file. Claims about behaviour are inferences from the code as written — they were not verified at runtime.

## Limitations

- Configuration defaults were checked by reading DI registrations, constructor defaults, and direct constant assignments. Files that are loaded at runtime (e.g., `appsettings.json`) would override these, but none exist.
- Test gaps were identified by comparing the domain code's branches/logic to the test files' scenarios — no coverage tool output was available.
- The sequential-execution finding assumes the executor loop at `GraphWorkflowExecutor.ExecuteReadyTicketsAsync` runs tickets one-at-a-time; confirmed via reading the `for { ticket = GetReadyTickets().First(); execute(ticket); }` pattern.