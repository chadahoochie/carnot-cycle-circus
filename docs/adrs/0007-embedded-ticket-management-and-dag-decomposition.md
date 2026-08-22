# ADR-0007: Embedded Ticket Management & DAG Work Decomposition

## Status
**Accepted** (2026-08-21)

## Context
Unstructured agent frameworks pass unstructured natural language prompts between agents without tracking milestones, dependencies, or completion status. In enterprise engineering, work must be decomposed hierarchically ($Epics \to Stories \to Subtasks$), scheduled according to explicit prerequisites, and verified against non-negotiable acceptance criteria.

## Decision
We embed a first-class **Ticket Management & Work Decomposition Engine** directly into the core runtime (`CarnotCycleCircus.Core.Domain.Tickets`):
1. **Hierarchical Backlog**: Tickets are structured into `Epic`, `Feature`, `Bug`, `Spike`, and `Subtask` types.
2. **Automated TPM/Architect Decomposition**:
   - `DeconstructEpic`: TPM creates Epic and core user story.
   - `DeconstructStoryIntoTechnicalSubtasks`: Lead Architect generates 5 atomic subtasks with explicit DAG dependencies (`DependsOnTicketIds`).
3. **Structured Inter-Agent Handoffs**: Work transitions through formal `HandoffPacket` payloads containing deliverables, context summaries, and review checklists.
4. **State Machine & DAG Scheduling**: `HandoffRouter` evaluates dependency satisfaction and transitions downstream backlog tickets to `Ready` when prerequisite tickets complete.

## Alternatives Considered
- **External Issue Tracker APIs (Jira / GitHub Issues)**: Rejected as mandatory runtime dependencies to allow air-gapped, zero-latency local execution. (External synchronizers can be added as adapters).
- **Ad-Hoc Multi-Agent Chat Prompts**: Rejected due to lack of milestone tracking, dependency resolution, and quality gate enforcement.

## Consequences

### Positive
- ✅ Structured work tracking with visual Kanban board, backlog view, and dependency DAG visualization.
- ✅ Automated translation from high-level objectives to executable subtasks.
- ✅ Strict accountability with assigned roles and deliverable verification.

### Negative / Trade-offs
- ⚠️ Adds domain modeling complexity around ticket state transitions and DAG resolution.
