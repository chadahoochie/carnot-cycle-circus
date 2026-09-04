# ADR-0017: System Area Separation, Team Archetype Elimination, and Agent-Bound Squad CLAWs 🎪🏗️

## Status
**Accepted** (2026-08-30)

## Context
In earlier iterations of Carnot Cycle Circus, team composition relied on hardcoded "Team Archetypes" (`TeamArchetypes.AllArchetypes`) which coupled agent roles to preset behavioral molds ("Balanced", "MoveFastBreakProduction", "SecurityHardened", etc.). This created several architectural bottlenecks:
1. **Coupling & Ambiguity**: Defining agents was conflated with team archetypes rather than maintaining a clean, independent catalog of configured agents with specific models, prompts, tools, and skills.
2. **CLAW Inflexibility**: Workflow execution graphs were global or archetype-templated rather than first-class properties of individual engineering squads.
3. **Execution Safety**: Execution dashboards allowed unleashing agents without explicitly selecting an active squad.
4. **Ticket Dashboard Clutter**: The ticket backlog UI contained redundant epic decomposition triggers that bypassed the project ignition and team workflow pipelines.

## Decision
We establish clear, decoupled boundaries between system areas:

1. **Agent Management (`IAgentDefinitionManager`, `/agents`)**:
   - Manages the centralized, persistent catalog of all defined agents (`agents.json`).
   - Owns agent persona attributes: circus role, absurd agent name, specialized skills, inference model assignments, API key overrides, and tool sandboxing.
   - Provides full CRUD, duplication, and role defaults.

2. **Team Management (`ITeamDefinitionManager`, `/teams`)**:
   - Manages distinct engineering squads (`TeamDefinition`, `teams.json`, `active-team-id.json`).
   - Completely eliminates `TeamArchetypes` and `ArchetypeName` in favor of squads embedding their own `WorkflowGraph Graph`.
   - Each CLAW node in the graph can be bound to a specific defined agent (`GraphNode.AgentId`).
   - Provides visual CLAW canvas, cable wiring (Output, Failure, Input ports), squad cloning, and JSON export/import.

3. **Execution Governance (`ExecutionDashboard.razor`, `/dashboard`)**:
   - Mandates selecting an active engineering squad prior to unleashing or single-stepping workflows.
   - Unleash and Poke actions are disabled until a valid squad is selected.

4. **Ticket Backlog Cleanup (`TicketManager.razor`, `/tickets`)**:
   - Removed the quick epic decomposition form from the ticket dashboard, routing project initialization through the dedicated Project Ignition Studio (`/`).

## Alternatives Considered
- *Retain archetypes as metadata tags on teams*: Rejected because archetypes imposed rigid constraints and conflated agent personas with squad topology.
- *Store workflow graphs only in a global workflow service*: Rejected because each engineering squad needs its own tailored routing, port cables, and circuit breaker policies.

## Consequences
### Positive
- ✅ Clean separation of concerns between agent definition, squad topology, and execution telemetry.
- ✅ Full flexibility to compose custom CLAW workflows with specific agent assignments per node.
- ✅ Execution safety enforced through mandatory squad selection.
- ✅ Cleaner ticket dashboard focused exclusively on backlog inspection and inter-agent handoff tracking.

### Negative / Trade-offs
- ⚠️ Existing JSON configurations containing legacy archetype names are migrated to squad definitions.
