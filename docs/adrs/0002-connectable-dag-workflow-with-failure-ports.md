# ADR-0002: Connectable DAG Workflow Engine with Dedicated Failure Ports

## Status
**Accepted** (2026-08-20)

## Context
Standard agent orchestration frameworks commonly employ linear waterfall pipelines or unconstrained mesh chatter. Linear pipelines fail catastrophically when a downstream review agent (such as Security Engineer or Principal QA Analyst) rejects a deliverable: the entire pipeline aborts, discarding prior architectural work and requiring manual re-initiation. Conversely, unconstrained mesh networks risk infinite circular chatter and lack deterministic visibility.

## Decision
We implement a connectable **Directed Acyclic Graph (DAG) Workflow Engine** (`CarnotCycleCircus.Core.Domain.Graph`) where each node exposes three explicit typed ports:
1. **Input Port (🟢)**: Receives incoming tickets and handoff packets.
2. **Success Output Port (🔵)**: Emits approved deliverables to downstream consumers upon passing quality gates.
3. **Failure / Remediation Port (🔴)**: Emits rejection findings and routes tickets back to the fixing agent (e.g. Developer) in a deterministic loopback.

Additionally, we enforce an explicit `FailurePolicy` with configurable circuit breakers (`MaxRetries`) to prevent runaway infinite loops.

## Alternatives Considered
- **Linear Waterfall Execution**: Rejected because software engineering requires iterative remediation loops.
- **Unconstrained Mesh / Actor Gossip**: Rejected because unbounded agent conversations lack visual traceability and lead to unpredictable token consumption.
- **Exception Throwing on Failure**: Rejected because failure and rejection are standard engineering states, not runtime anomalies.

## Consequences

### Positive
- ✅ Deterministic self-healing remediation cycles without restarting the full workflow.
- ✅ Clear visual representation on the interactive Blazor canvas with real-time port activity pulses.
- ✅ Circuit breaker protections preventing runaway token spend.

### Negative / Trade-offs
- ⚠️ Requires graph cycle detection and explicit retry tracking on nodes.
- ⚠️ State machines must accommodate `Remediating` lifecycle phases.
