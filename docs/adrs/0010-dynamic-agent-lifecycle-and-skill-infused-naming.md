# ADR-0010: Dynamic Agent Troupe Lifecycle and Skill-Infused Agent Naming Engine

## Status
**Accepted** (2026-08-24)

## Context
In previous iterations, the engineering troupe was bound to a static 6-member team structure (`TechnicalProductManager`, `LeadArchitect`, `SoftwareDeveloper`, `SecurityEngineer`, `OptimizationEngineer`, `PrincipalQAAnalyst`) with hardcoded personas. Users had limited ability to:
1. **Add and Remove Agents**: Expand the team with specialized agents, multiple developers/testers, or adjust troupe size dynamically.
2. **Incorporate Skills at Creation Time**: Combine arbitrary capabilities from the `ISkillRegistry` during agent instantiation and have them reflected in cognitive system prompt directives.
3. **Generate Thematically Consistent Personas**: Produce theatrical, absurd circus-themed agent names that reflect assigned skills, roles, and thermodynamic engineering motifs while upholding the Deliverable Isolation Contract (ADR-0005).

## Decision
We implement a **Dynamic Agent Troupe Lifecycle & Skill-Infused Naming Architecture** (`CarnotCycleCircus.Core.Domain.Agents` and `CarnotCycleCircus.Core.Domain.Teams`):

1. **Unique Agent Member Identification (`AgentMember.Id`)**:
   - Every `AgentMember` record contains an immutable, globally unique identifier (`Id = $"agent-{Guid.NewGuid():N}"[..18]`).
   - Non-destructive mutation (`with { ... }`) preserves member IDs across configuration updates.

2. **Dynamic Troupe Lifecycle Methods**:
   - `EngineeringTeam` and `TeamDefinition` provide immutable mutation methods:
     - `AddMember(AgentMember member)`
     - `RemoveMember(string memberId)`
     - `UpdateMember(AgentMember member)`
     - `GetMemberById(string memberId)` / `GetMembers(AgentRole role)`
   - `ITeamDefinitionManager` exposes troupe lifecycle operations (`AddMemberToCurrentTeam`, `RemoveMemberFromCurrentTeam`, `UpdateMemberInCurrentTeam`) that atomically persist changes to storage and notify UI subscribers via `OnCurrentTeamChanged`.

3. **Circus Agent Name Generator Engine (`IAgentNameGenerator`)**:
   - Integrates circus archetypes (Ringmaster, High Trapeze Artist, Lion Tamer, Fire-Breather, Tightrope Walker, Chaos Clown, Juggler, Acrobat) with specialized skill concepts.
   - Extracts semantic themes, adjectives, and domain keywords from assigned `SkillDefinition` objects to generate:
     - **Skill Nicknames**: e.g., `"Zero-Alloc"`, `"Span-Swallower"`, `"STRIDE-Tamer"`, `"Sub-Nanosecond"`, `"Demonic-Payload"`, `"GraphQL-Contortionist"`.
     - **Skill Surnames**: e.g., `Byte-Trapeze`, `Threat-Shield`, `Tightrope-Overclocker`, `Build-Executioner`, `Schema-Weaver`.
     - **Skill Circus Acts**: e.g., `Span-Swallowing Acrobat`, `Lion Tamer of Unsanitized Input`, `Chaos Clown of Software Torture`.
   - Generates multi-pattern suggestions with rerolling and deterministic seeding for testing.
   - Synthesizes cognitive system prompts embedding all assigned skill instructions while enforcing the **Deliverable Isolation Contract (ADR-0005)**.

4. **Interactive Studio UI (`TeamDefinition.razor`)**:
   - Provides an **"Add Circus Agent"** modal supporting live role selection, multi-skill assignment, instant skill-infused name suggestions, rerolling, alternative suggestion chips, tool binding, and model configuration.
   - Displays assigned skill badges on agent cards and provides per-agent removal with safety controls.

## Alternatives Considered
- **Static Pre-Baked Name Lists**: Rejected because static lists cannot dynamically reflect custom imported skills or user-authored capabilities.
- **Pure LLM Generation on Startup**: Rejected to avoid latency and ensure 100% offline determinism and air-gapped container support.

## Consequences

### Positive
- ✅ Users can freely add, customize, and remove agents from their engineering troupes.
- ✅ Agent names and personas creatively reflect assigned skills in accordance with the application's absurd circus theme.
- ✅ System prompt directives automatically incorporate instructions from assigned skills.
- ✅ Full backwards compatibility and JSON roundtrip serialization across persistent storage.
- ✅ Strict deliverable isolation (ADR-0005) is maintained for all generated prompts.

### Negative / Trade-offs
- ⚠️ Dynamic troupe size requires UI handling for empty or large team lists.
