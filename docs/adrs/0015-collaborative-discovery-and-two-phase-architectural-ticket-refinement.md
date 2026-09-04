# ADR-0015: Collaborative Discovery and Two-Phase Architectural Ticket Refinement 🤝🏛️

## Status
**Accepted** (2026-08-29)

## Context
In previous iterations (ADR-0007, ADR-0014), project decomposition and architectural authoring exhibited two critical synchronization gaps:

1. **Disconnected Discovery at Project Ignition**:
   - The Requirements Researcher executed a standalone spike and handed off a static markdown brief.
   - The Technical Product Manager (TPM) was forced to decompose user stories and generate technical subtasks monolithically, without true collaborative refinement with the Research Analyst.
   - When starting new initiatives from the Project Ignition Wizard or custom prompt, domain research was frequently bypassed or treated as a separate, uncoordinated artifact.

2. **Premature Technical Subtask Generation & Lack of Architect Refinement**:
   - The TPM (or work decomposition engine) generated granular technical subtasks (`[Arch]`, `[Dev]`, `[Security]`, `[Opt]`, `[QA]`, `[Integration]`) immediately upon Epic creation before any technical backlog review took place.
   - The **Lead Architect** was immediately assigned to author an Architectural Decision Record (ADR) without having first refined the user stories, evaluated technical boundaries, established concrete technical acceptance criteria, or configured dependency graphs.

## Decision
We establish a formalized two-stage collaboration and refinement workflow across the platform:

1. **Collaborative Project Ignition & Co-Discovery (PM + Research Analyst)**:
   - When a new project or initiative is started, the **Technical Product Manager** and **Requirements Researcher** execute a collaborative discovery cycle.
   - The Research Analyst investigates RFCs, industry standards, third-party package dependencies, and harvested codebase boundaries, generating a Feasibility Brief (`*_RESEARCH_BRIEF.md`).
   - The TPM synthesizes research findings to frame business goals, non-functional requirements, and foundational User Stories (`TicketType.Feature`) within the Product Requirements Document (`*_PRD.md`).
   - The decomposition engine creates only the high-level Epic and User Stories (`DeconstructEpicIntoUserStories`), explicitly avoiding premature technical subtask generation.

2. **Two-Phase Architectural Orchestration (Lead Architect)**:
   - **Phase 2A (Backlog Refinement & Decomposition)**:
     - The Lead Architect receives the PRD, Feasibility Brief, and User Stories.
     - The Architect performs a formal technical grooming pass (`RefineStoryIntoTechnicalSubtasks`), decomposing each User Story into atomic, role-specific technical subtasks:
       1. `[Arch] Design ADR & Scaffold Clean Architecture`
       2. `[Dev] Implement Domain Models, Service & Tests`
       3. `[Security] STRIDE Threat Model & Code Audit`
       4. `[Opt] Latency Bottleneck & Allocation Audit`
       5. `[QA] Test Strategy & Final Acceptance Validation`
       6. `[Integration] Solution Packaging & Repository Integration`
     - Precise CLAW dependency mappings (`DependsOnTicketIds`) and technical acceptance criteria are established and stored in `ITicketStore`.
   - **Phase 2B (Architecture Design, ADR & Scaffolding)**:
     - The Lead Architect executes the ready Architecture subtask, producing the Nygard Architectural Decision Record (`*_ADR.md`) and compilable Clean Architecture scaffold files (Domain immutable records, Application contracts, DI extensions).
     - Downstream developers, security engineers, optimization engineers, and QA analysts execute against refined, dependency-locked subtasks and unambiguous ADR contracts.

3. **Interface and Engine Upgrades**:
   - Updated `IWorkDecompositionEngine` to provide explicit `DeconstructEpicIntoUserStories` and `RefineStoryIntoTechnicalSubtasks` methods alongside backward-compatible orchestration wrappers.
   - Updated `GraphWorkflowExecutor` to execute the collaborative discovery handoff and the two-phase architectural refinement sequence.

## Consequences
### Positive
- ✅ Clean separation of concerns between product-level user stories (PM/Researcher) and engineering-level technical subtasks (Lead Architect).
- ✅ Lead Architect explicitly reviews and structures technical requirements before making binding architectural decisions.
- ✅ Downstream engineering agents (Dev, Security, Optimization, QA, Integration) receive well-defined, dependency-linked subtasks grounded in both the PRD and the Architect's ADR.
- ✅ 100% compliant with the Deliverable Isolation Contract (ADR-0005) and zero-allocation asynchronous orchestration.

### Negative / Trade-offs
- ⚠️ Adds an explicit refinement step in the ticket lifecycle prior to implementation.
