# ADR-0019: Transition from Directed Acyclic Graph (DAG) to Closed-Loop Agent Workflow (CLAW)

## Status
**Accepted** (2026-09-03)

## Context
Early architectural documentation and UI labels in Carnot Cycle Circus referenced the workflow execution topology as a Directed Acyclic Graph (DAG). However, this terminology introduces a direct mathematical contradiction with the platform's core self-healing architecture:
1. **Mathematical Inaccuracy of "Acyclic"**: A DAG by definition prohibits cycles. In Carnot Cycle Circus, nodes expose dedicated Failure/Reject ports (🔴) that route rejected deliverables and diagnostic findings backward upstream (e.g., from Principal QA Analyst or Security Engineer back to Software Developer or Lead Architect) for iterative remediation. These loopbacks form directed cycles in the graph topology.
2. **Control Theory and Autonomous Systems Alignment**: Software engineering workflows are fundamentally closed-loop systems: deliverables are measured against verifiable acceptance criteria and test suites, and errors are fed back upstream to modulate subsequent generation attempts until convergence or circuit-breaker termination (`MaxRetries`).
3. **Cognitive Clarity**: Retaining the term "DAG" confuses engineers evaluating the architecture, as standard DAG schedulers (e.g., Airflow, Make) fail or abort on cyclic edges, whereas Carnot Cycle Circus treats feedback loops as first-class citizens.

## Decision
We formally adopt the term and acronym **CLAW (Closed-Loop Agent Workflow)** across all documentation, UI components, and domain specifications:
1. **Domain Representation**:
   - The topology is designated as a **Closed-Loop Agent Workflow (CLAW)**.
   - Core domain models remain centered around `WorkflowGraph`, `GraphNode`, and `PortConnection`, explicitly formalizing input, output, and feedback remediation edges.
2. **UI & Documentation Normalization**:
   - All references to "DAG Canvas", "DAG Nodes", "DAG Scheduler", and "Decomposition DAG" in UI components and documentation portals are renamed to **CLAW Canvas**, **CLAW Nodes**, **CLAW Scheduler**, and **CLAW Decomposition**.
   - The ticket view tab is normalized to `claw`.

## Alternatives Considered
- **Directed Cyclic Graph (DCG)**: Evaluated for pure mathematical precision. Rejected because it only indicates cyclicity without conveying the purpose of the loops (remediation, verification, and autonomous closed-loop convergence).
- **Control Flow Graph (CFG)**: Evaluated for compiler theory alignment. Rejected because CFGs typically represent instruction-level imperative flow rather than high-level multi-agent deliverable exchange.
- **StateGraph**: Evaluated for alignment with frameworks like LangGraph. Rejected because CLAW directly emphasizes the autonomous closed feedback loop and pairs cleanly with Carnot cycle thermodynamics.

## Consequences

### Positive
- ✅ Resolves the graph theory contradiction between acyclicity and remediation loopbacks.
- ✅ Accurately communicates the self-healing, closed-loop nature of the multi-agent pipeline.
- ✅ Provides a memorable, concise acronym (CLAW) for developer tooling, UI navigation, and technical documentation.

### Negative / Trade-offs
- ⚠️ Requires updating documentation references, ADR cross-links, and UI string literals across the solution.
