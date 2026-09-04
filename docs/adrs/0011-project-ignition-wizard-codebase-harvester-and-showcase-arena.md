# ADR-0011: Project Ignition Wizard, Codebase Harvester, and Zero-Setup Showcase Arena

## Status
**Accepted**

## Context
When developers and technical users adopt Carnot Cycle Circus, they face two distinct operational paradigms with varying friction:
1. **Greenfield Development ("From Scratch")**: Users want to build a new system or feature but face cognitive overload trying to manually create Epics, configure 6 agent personas, wire CLAW dependencies, and seed architectural rules.
2. **Brownfield Development ("Existing Project")**: Users want autonomous agents to assist in an existing repository or solution, but transcribing codebase architecture, package dependencies, test structures, and tech debt into agent memory is tedious and error-prone.
3. **First-Run Time-to-Value**: Users evaluate tools in seconds. Requiring API keys, model configuration, and database connection strings before showing value causes immediate user churn.

## Decision
Implement a **Dual-Track Quickstart & Ignition Architecture** backed by three foundational services:
1. **`IProjectBlueprintService` (Ignition Wizard)**:
   - Provides curated 1-click architectural blueprints (High-Throughput IoT, E-Commerce Saga, Zero-Trust Identity, Distributed CQRS, Chaos Benchmark Arena) and custom prompt ignition.
   - Automatically decomposes Epics into 5 atomic technical subtasks, creates an initial MADR/Nygard ADR, populates AI knowledge maps, seeds semantic memory, and activates the optimal squad archetype.
2. **`ICodebaseHarvesterService` (Codebase Harvester, Directory Explorer & Tech Debt Radar)**:
   - Recursively inspects local repositories, `.sln`/`.csproj` project files, and package dependencies.
   - Provides interactive directory browsing, breadcrumb traversal, standard Docker mount detection (`/workspace`, `/app`, `/data`), and 1-click solution auto-discovery.
   - Automatically detects architectural patterns (Blazor, xUnit, Zero-Allocation, OpenTelemetry, Redis, EF Core).
   - Generates actionable improvement backlogs (STRIDE security audit, zero-allocation benchmarking, test suite expansion, ADR generation).
   - Ingests discovered components into the semantic memory tier and knowledge graph.
3. **`IShowcaseDemoService` (Zero-Setup Interactive Showcase Arena)**:
   - Enables 1-click execution of pre-configured offline simulation scenarios (60-second Full Swarm Sprint, Friday 4:59 PM Meltdown & Remediation, Nanosecond Shootout) without requiring API keys or external inference credentials.
4. **Ringmaster Quickstart UI Banner & Top Bar Actions**:
   - Hero banner prominently embedded into the home view (`TeamDefinition.razor`) and persistent quick actions in `MainLayout.razor` providing immediate access across the platform.

## Consequences

### Positive
- **Instant Time-to-Dopamine (< 60s)**: Users can see the full 6-agent swarm collaborating and producing artifacts on minute 1 with zero setup.
- **Seamless Greenfield & Brownfield Workflows**: Single-click initialization for both brand new projects and existing repositories.
- **Interactive Directory Navigation in Docker**: 1-click mount point shortcuts (e.g. `🐳 /workspace`), breadcrumbs, and auto-discovered solutions eliminate manual path typing.
- **Automated Context Ingestion**: Codebase Harvester directly populates the 4-tier vector memory and knowledge graph without manual prompting.
- **Zero-Allocation & Async Dogma Preserved**: All services adhere to C# 13 immutable records, async cancellation tokens, and zero-allocation hot paths.

### Negative / Trade-offs
- File system scanning is constrained by local directory access permissions and volume mounts in containerized environments.

## References
- [`IProjectBlueprintService.cs`](file:///home/chad/source/dotnet/carnot-cycle-circus/src/CarnotCycleCircus.Core/Domain/Blueprints/ProjectBlueprintService.cs)
- [`ICodebaseHarvesterService.cs`](file:///home/chad/source/dotnet/carnot-cycle-circus/src/CarnotCycleCircus.Core/Domain/Harvester/CodebaseHarvesterService.cs)
- [`IShowcaseDemoService.cs`](file:///home/chad/source/dotnet/carnot-cycle-circus/src/CarnotCycleCircus.Core/Domain/Showcase/ShowcaseDemoService.cs)
- [ADR-0001: Immutable Record Types](0001-immutable-record-types-for-domain-and-handoff-payloads.md)
- [ADR-0005: Deliverable Isolation Contract](0005-deliverable-isolation-contract-for-agent-personas.md)
