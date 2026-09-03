# ADR-0018: Project Scoping and Application-Level vs. Project-Scoped Separation

## Status
**Accepted** (2026-09-02)

## Context
Carnot Cycle Circus previously modeled all tickets, artifacts, telemetry, ADRs, and approval gates as a single global workspace. As users composed multiple independent initiatives (e.g. an IoT ingestion pipeline and a separate zero-trust identity overhaul) using the same running instance, several problems emerged:
1. **Cross-Contamination**: Tickets, event stream messages, artifacts, and ADRs from unrelated initiatives were interleaved with no isolation boundary, making the Ticket Studio, Panic & Telemetry Dashboard, Artifacts Hub, and Docs & ADR Hub unusable once more than one initiative was in flight.
2. **Ambiguous Administration Boundary**: There was no clear line between application-level configuration (agent roster, squad DAG topology, skills, model catalog, key vault, standards, knowledge maps, persistent memory) and the scoped units of work those agents actually produce.
3. **No Organizational Container**: There was no first-class entity to group related tickets/artifacts/telemetry/approvals, track their lifecycle (`Active`, `Paused`, `Completed`, `Archived`), or optionally bind them to a specific engineering squad.

## Decision
We introduce **Project** (`CarnotCycleCircus.Core.Domain.Projects.Project`) as a first-class domain entity that scopes units of work, and draw a clean line between application-level and project-scoped concerns.

### Scoping Classification
| Application-Level (no project context required) | Project-Scoped (requires an active project) |
|---|---|
| Agent Studio (`/agents`) | Ticket Manager (`/tickets`) |
| Team Management (`/teams`) | Panic & Telemetry Dashboard (`/dashboard`) |
| Skills Hub & Skill Matrix (`/skills`, `/skills/matrix`) | Artifacts Hub (`/artifacts`) |
| Model Catalog (`/models`) | Docs & ADR Hub (`/docs`) |
| Self-Improvement Studio (`/persistence`) | Workflow Execution |
| Memory Inspector (`/memory`) | Approval Gates |
| Standards Manager (`/standards`) | |
| Knowledge Map Explorer (`/knowledge`) | |
| Key Vault (modal) | |

### Core Domain
- `Project` record: `Id`, `Name`, `Description`, `Status` (`ProjectStatus`), `TeamId?`, `WorkspaceDirectory?`, `Metadata`, `CreatedAt`, `LastActivityAt?`.
- `IProjectManager` / `ProjectManager`: CRUD lifecycle backed by `projects.json`, following the established `TeamDefinitionManager` persistence pattern.
- `IActiveProjectContext` / `ActiveProjectContext`: Tracks the currently selected project for the session, persists the last-active project ID to `active-project.json` for continuity across restarts, and raises `OnActiveProjectChanged` for reactive UI updates.

### Scoped Entities
`ProjectId` (nullable string) is added to: `TicketItem`, `HandoffPacket`, `AgentMessage`, `ArtifactDescriptor`, `WorkflowApprovalRequest`, `ArchitecturalDecisionRecord`, and `ProjectDocument`. Backward-compatible constructor overloads are retained where existing call sites construct these records without a project.

### Service & Execution Propagation
`WorkDecompositionEngine`, `HandoffRouter`, `ArtifactManager`, `WorkflowApprovalService`, `AdrDocumentManager`, `GraphWorkflowExecutor`, `AgentExecutionEngine`, `ProjectBlueprintService`, and `CodebaseHarvesterService` all stamp `ProjectId` onto entities they create, falling back to `IActiveProjectContext.CurrentProjectId` when an explicit project isn't supplied (e.g. manual ticket creation, ad-hoc handoffs).

### UI
- `ProjectHub.razor` (`/projects`): Project CRUD, activation, and lifecycle management.
- `ProjectSwitcher.razor`: Sidebar component above navigation links showing the active project with a quick-switch dropdown.
- `NavMenu.razor`: Split into **Project Work** and **Administration** groups per the scoping classification above.
- Project-scoped pages (`TicketManager`, `ExecutionDashboard`, `ArtifactsHub`, `DocsAndAdrs`) inject `IActiveProjectContext` directly, filter all reads through `GetByProject`/`GetRecentMessages(projectId)`-style APIs, and render a "select a project" prompt in place of their content when no project is active.

### Storage Strategy
Flat `projects.json` (project catalog) + `active-project.json` (session continuity) + `ProjectId` filtering on existing entity stores (`tickets.json`, `handoffs.json`, `adrs.json`, `docs.json`). No per-project database or schema split.

## Alternatives Considered
- **Multi-tenant workspace-per-project (separate storage roots)**: Rejected as over-engineering for a local-first desktop/self-hosted application; a flat `ProjectId` filter achieves the same isolation with far less operational complexity.
- **Embed a project graph directly inside `TeamDefinition`**: Rejected because it would re-couple squad topology (an application-level, reusable concern) to a specific initiative's lifecycle, contradicting the separation established in [ADR-0017](0017-system-area-separation-and-team-archetype-elimination.md).
- **Migrate existing unscoped data into a default project**: Rejected in favor of a clean-slate start; existing `tickets.json`, artifact files, and event history are orphaned rather than silently reassigned, avoiding incorrect project attribution.

## Consequences

### Positive
- ✅ Multiple independent initiatives can run concurrently in a single instance without ticket, telemetry, artifact, or ADR bleed-through.
- ✅ Clear, enforceable boundary between application-level administration and project-scoped work, reinforcing the separation established in ADR-0017.
- ✅ Optional `TeamId` association lets a project be executed by any compatible squad without hard-coupling.
- ✅ Session continuity: the active project survives an app restart via `active-project.json`.

### Negative / Trade-offs
- ⚠️ **Breaking change**: pre-existing `tickets.json`, artifact files, and event stream history predate `ProjectId` and are effectively orphaned (unscoped) after this change; they will not surface in any project-scoped view.
- ⚠️ Knowledge Maps remain application-level for now; per-project knowledge graphs would require extending `KnowledgeNode` with the same `ProjectId` pattern in a future ADR.
- ⚠️ `GraphWorkflowExecutor.ExecuteReadyTicketsAsync()` still processes all globally ready tickets rather than being hard-restricted to the active project; UI entry points scope what they display and create, but the underlying executor is not itself project-partitioned.
