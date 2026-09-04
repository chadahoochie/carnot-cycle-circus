# ADR-0014: Dedicated Requirements Researcher Agent and Upstream Discovery CLAW Stage 🔬📋

## Status
**Accepted** (2026-08-28)

## Context
Prior to this architectural evolution, the autonomous software engineering lifecycle began directly with the **Technical Product Manager (TPM)** agent. When an initiative or Epic was ignited, the TPM was responsible for simultaneously:
1. Conducting exploratory domain research, RFC retrieval, and external library evaluation via web search and memory lookups.
2. Generating a formal, structured Product Requirements Document (PRD) with Gherkin acceptance criteria and non-functional requirements.
3. Decomposing the Epic into user stories and downstream technical execution subtasks.

### Problems Identified with Monolithic TPM Discovery:
- **Context Window Pollution & Token Budget Depletion**: Raw web search outputs, API documentation scrapes, and RFC specifications consumed excessive token budget, leading to instruction-following degradation on downstream acceptance criteria formatting.
- **Cognitive Coupling (Exploratory vs. Structured)**: Research is an exploratory, convergent cognitive task; writing rigorous acceptance criteria and decomposing ticket CLAW workflows is a structured, generative task.
- **Upstream Hallucination Risk ("Garbage In, Garbage Out")**: Ambiguous prompts led to hallucinated domain models or unviable library assumptions before the Lead Architect or Software Developer received work.
- **Lack of Upstream Verification Gate**: Operators could not inspect domain research or feasibility findings prior to the generation of 6+ technical execution subtasks.

## Decision
We introduce a dedicated **Requirements Researcher Agent** (`AgentRole.RequirementsResearcher`) positioned as **Stage 1 (Upstream Discovery & Feasibility)** in the Closed-Loop Agent Workflow (CLAW) before the Technical Product Manager:

1. **New Autonomous Role (`AgentRole.RequirementsResearcher`)**:
   - **Persona**: `Rachel "DeepDive" Reference (Requirements Researcher)` — detail-hound investigative scout cross-referencing RFCs, specs, and library ecosystems under the Deliverable Isolation Contract (ADR-0005).
   - **Default Model**: `anthropic/claude-3.7-sonnet` (with fallback to `openai/gpt-4o`).
   - **Allowed Tools**: `web_search`, `memory_lookup`.
   - **Primary Deliverable**: `*_RESEARCH_BRIEF.md` (Requirements Research & Technical Feasibility Brief).

2. **Upstream Discovery CLAW Pipeline Stage (`WorkflowGraph` & `GraphWorkflowExecutor`)**:
   - Updated `WorkflowGraph.CreateDefaultEngineeringCircus()` to 8 specialized nodes:
     $$\text{Research} \to \text{TPM} \to \text{Lead Architect} \to \text{Software Developer} \to \text{Security} \parallel \text{Optimization} \to \text{QA} \to \text{Integration}$$
   - Added remediation failure cable from `node-tpm` (Failure) $\to$ `node-res` (Input) if the TPM determines research is ambiguous or incomplete.
   - `GraphWorkflowExecutor.ExecuteWorkflowAsync` executes the Requirements Researcher prior to TPM decomposition and attaches the resulting `_RESEARCH_BRIEF.md` deliverable to the parent Epic.

3. **Upstream Context Synthesis into PRD (`AgentExecutionEngine`)**:
   - The TPM's user prompt automatically ingests the upstream `_RESEARCH_BRIEF.md` via `GatherUpstreamDeliverables`.
   - Grounds PRD generation in actual RFC constraints, verified .NET 10 package dependencies, and codebase boundaries extracted by `ICodebaseHarvesterService`.

4. **Artifacts Hub & Storage Categorization (`ArtifactManager`)**:
   - Formalized `"Research"` artifact categorization mapping `*_RESEARCH_BRIEF.md`, `RESEARCH-*`, and `RequirementsResearcher` deliverables to `artifacts/research/`.
   - Added Research category badge (`#6366f1` Indigo) and category filtering in `ArtifactsHub.razor`.

## Alternatives Considered
- **Single Monolithic TPM Agent**: Rejected due to token exhaustion and cognitive role interference between exploratory searching and structured ticket decomposition.
- **Dynamic Subagent Delegation by TPM**: Rejected in favor of an explicit visual CLAW stage (Option A) to ensure first-class visual observability, independent step execution, and clear telemetry logging on the visual canvas.
- **Optional / Bypassable Research**: Preserved for rapid prototypes via `CreateRapidPrototype()`, while full-lifecycle standard circus defaults to complete discovery-first rigor.

## Consequences
### Positive
- ✅ Clean cognitive separation between exploratory requirement discovery and structured work decomposition.
- ✅ TPM PRDs are grounded in real RFC standards, .NET 10 framework capabilities, and harvested codebase context.
- ✅ Complete visual traceability on the Rube Goldberg Canvas with dedicated `node-res` status and failure loopbacks.
- ✅ Research briefs are versioned and exported to `artifacts/research/` for auditability and compliance.
- ✅ 100% test coverage across all role execution, CLAW routing, and artifact categorization suites.

### Negative / Trade-offs
- ⚠️ Adds one sequential LLM inference hop before ticket decomposition during full epic workflows.
