# ADR-0016: Human-in-the-Loop Approval Gates Between TPM, Architect, and Coder ✋🛡️

## Status
**Accepted** (2026-09-02)

## Context
As autonomous agent teams execute complex engineering initiatives at maximum Carnot efficiency, autonomous execution without verification checkpoints introduces significant risk of divergence:

1. **Scope and Requirements Divergence (TPM ➔ Architect)**:
   - When the Technical Product Manager synthesizes the Product Requirements Document (`*_PRD.md`) and decomposes foundational User Stories (`TicketType.Feature`), assumptions regarding initiative scope, acceptance criteria, and non-functional requirements must be validated by human engineering leadership before architectural design begins.
   - Without an explicit approval checkpoint, the Lead Architect can spend considerable inference cycles designing complex architectures for misaligned or bloated requirements.

2. **Architectural and Implementation Divergence (Architect ➔ Coder)**:
   - When the Lead Architect finalizes the Architectural Decision Record (`*_ADR.md`), scaffolds Clean Architecture contract code files, and decomposes stories into granular technical subtasks (`[Arch]`, `[Dev]`, `[Security]`, `[Opt]`, `[QA]`, `[Integration]`), structural decisions (libraries, APIs, zero-allocation data structures, storage engines) become binding.
   - Allowing the Coder (`SoftwareDeveloper`) to immediately execute implementation code without human sign-off risks code sprawl against suboptimal architectural choices, requiring costly rollbacks or manual refactoring.

## Decision
We establish explicit **Human-in-the-Loop (HITL) Approval Gates** embedded directly into the workflow execution engine and user interface:

### 1. Domain Abstractions & Immutability
Under `CarnotCycleCircus.Core.Domain.Approvals`:
- **`ApprovalGateStage`**: Strongly-typed enum representing gate checkpoints:
  - `TpmToArchitect`: Post-PRD & user story creation, preceding technical backlog refinement and ADR design.
  - `ArchitectToCoder`: Post-ADR & architecture scaffolding, preceding implementation code authoring.
- **`ApprovalStatus`**: Strongly-typed enum (`Pending`, `Approved`, `Rejected`).
- **`ApprovalItemSummary`**: Immutable C# 13 `record` holding categorised summaries of items for human review (`Category`, `Title`, `Details`, `KeyPoints`).
- **`WorkflowApprovalRequest`**: Immutable C# 13 `record` encapsulating the gate metadata, deliverables, approval item summaries, resolution status, and reviewer feedback.
- **`IWorkflowApprovalService` / `WorkflowApprovalService`**:
  - Thread-safe singleton utilizing `TaskCompletionSource<WorkflowApprovalRequest>(RunContinuationsAsynchronously)`.
  - Zero busy waiting: workflow execution asynchronously awaits approval resolution.
  - Cancellation token support for aborting workflows.
  - Per-epic gate persistence and configurable bypass (`RequireUserApproval = false` for CI/CD automation and headless testing).

### 2. Engine Integration (`GraphWorkflowExecutor`)
- **Gate 1 (`TpmToArchitect`)**:
  - Executes immediately after the TPM authors the PRD and extracts feature user stories.
  - Transitions the TPM graph node to `NodeExecutionState.WaitingForApproval`.
  - Summarizes the PRD content, character counts, and each user story's acceptance criteria.
  - If approved, proceeds to Lead Architect refinement. If rejected, cleanly halts the workflow.
- **Gate 2 (`ArchitectToCoder`)**:
  - Executes inside the autonomous CLAW execution loop before any `AgentRole.SoftwareDeveloper` ticket is executed.
  - Transitions the Developer graph node to `NodeExecutionState.WaitingForApproval`.
  - Summarizes the ADR decision, companion C# scaffold files, and all mapped technical subtasks (`[Arch]`, `[Dev]`, `[Security]`, `[Opt]`, `[QA]`, `[Integration]`).
  - If approved, unleashes the Coder to implement domain logic and unit tests. If rejected, cleanly halts the workflow.

### 3. UI Components & Visual Feedback
- **`WorkflowApprovalModal.razor`**:
  - Cross-platform modal presenting the gate stage, initiative epic ID, produced deliverable previews, itemized checklist with acceptance criteria, and next-step actions.
  - Interactive reviewer feedback textarea for providing directives or reasons for rejection.
  - "Approve & Unleash Next Stage" (green) and "Reject & Request Revision" (red) action buttons.
- **Dashboard & Studio Visuals (`ExecutionDashboard.razor`, `TeamStudio.razor`)**:
  - High-visibility amber spotlight banner (`pulse-waiting`) rendering whenever a gate requires human sign-off.
  - Amber pulsating ring on graph nodes in `NodeExecutionState.WaitingForApproval`.
  - Toggle switch in the execution toolbar allowing users to enable/disable human approval gates dynamically.

## Consequences
### Positive
- ✅ Human engineering leadership maintains complete authority over scope definitions and architectural choices prior to code authoring.
- ✅ Structured deliverable summaries present PRD previews, acceptance criteria, ADR decisions, and scaffold files in a single unified prompt.
- ✅ Zero busy waiting: uses asynchronous task completion sources with zero CPU burn during wait states.
- ✅ Rejections cleanly halt the pipeline, preventing wasted API inference calls and code drift.
- ✅ Configurable gate enforcement: can be disabled in headless CI/CD pipelines while defaulting to active in interactive desktop/web UI.
- ✅ 100% compliant with the Deliverable Isolation Contract (ADR-0005) and C# 13 immutability standards.

### Negative / Trade-offs
- ⚠️ Workflows pause until a human reviews the prompt and clicks approve/reject when `RequireUserApproval` is enabled.
