# ADR-0005: Deliverable Isolation Contract for Agent Personas

## Status
**Accepted** (2026-08-21)

## Context
Engaging agent personalities (witty dialogue, cynical banter, coffee jokes, paranoid security commentary) enhance human engagement and system observability in the real-time UI. However, if persona quirks bleed into formal engineering deliverables (PRDs, ADRs, C# source code, unit tests, STRIDE matrices, QA scorecards), the generated artifacts become unusable in production environments, breaking automated compilers, linters, and enterprise compliance.

## Decision
We enforce a strict **Deliverable Isolation Contract** embedded directly into all agent persona prompts and validated across the system:

> **The Deliverable Isolation Contract**:
> Agent banter, cynical observations, and humorous dialogue are strictly permitted in conversational chat streams, thought logs, and informal status updates. **ALL formal deliverables (PRDs, ADRs, C# code, test suites, threat models, benchmark reports, and QA scorecards) MUST remain 100% professional, rigorous, standard-compliant, unambiguous, and completely free of joke text or sarcastic phrasing.**

## Alternatives Considered
- **Banning Personas Completely (Boring Mode)**: Rejected because distinct persona perspectives (e.g. paranoid security auditor, zero-allocation fanatic) drive better specialized reasoning and user engagement.
- **Unrestricted Persona Leaks**: Rejected because sarcastic code comments and joke variable names ruin production code quality.

## Consequences

### Positive
- ✅ Preserves rich, engaging agent personalities in the UI and event stream.
- ✅ Guarantees clean, enterprise-ready, production-grade technical deliverables.
- ✅ Unambiguous evaluation boundary for automated quality gates and linters.

### Negative / Trade-offs
- ⚠️ Prompts must explicitly reiterate the deliverable isolation boundary for every agent role.
