# System Architecture & Overview 🏛️

## 1. Executive Summary

**Carnot Cycle Circus** is an autonomous engineering agent orchestration platform designed to model, coordinate, and execute complete software engineering lifecycles. Built in **.NET 10 (C# 13)** with an interactive **ASP.NET Core Blazor** web interface, it organizes software engineering into six specialized autonomous roles working concurrently or in structured Directed Acyclic Graph (DAG) pipelines.

The platform resolves common multi-agent coordination problems through:
1. **Embedded Ticket Management**: Hierarchical work decomposition ($Epics \to Stories \to Subtasks$) with explicit dependency scheduling.
2. **Structured Handoff Contracts**: Formal `HandoffPacket` payloads passing deliverables, context, review checklists, and remediation instructions.
3. **Connectable Visual Workflow Canvas**: Multi-port nodes exposing Input, Success Output, and Failure/Reject ports for self-healing loops.
4. **Hierarchical Persistent Memory (OpenViking-Style)**: 4-tier memory architecture (Working, Episodic, Semantic, Procedural) with local 64-dimensional vector similarity search.
5. **Dynamic OpenRouter Inference & Key Vault**: Multi-model assignment with per-role API keys, mid-flight swapping, and offline simulation fallback.
6. **Governance & Standards Engine**: Configurable acceptance criteria, STRIDE threat audits, zero-allocation policies, and automated ADR generation.

---

## 2. C4 Architecture Models

### 2.1 C4 Level 1: System Context Diagram

```mermaid
C4Context
    title System Context Diagram - Carnot Cycle Circus

    Person(developer, "Software Engineer / Operator", "Configures teams, creates tickets, designs workflow DAGs, triggers agent execution, and inspects memory & telemetry.")
    
    System(circus, "Carnot Cycle Circus", ".NET 10 / Blazor autonomous agent orchestration platform with embedded ticket engine, persistent memory, and workflow DAG.")
    
    System_Ext(openrouter, "OpenRouter API Gateway", "Unified multi-model LLM inference provider (Claude 3.7, GPT-4o, DeepSeek-R1, Qwen 2.5 Coder, o3-mini).")
    System_Ext(ext_memory, "External Vector Stores (Optional)", "OpenViking / Mem0 / Qdrant REST endpoints for enterprise-scale vector synchronization.")

    Rel(developer, circus, "Interacts via Web UI", "HTTPS / Blazor Interactive Server")
    Rel(circus, openrouter, "Per-agent model completion requests", "HTTPS / REST (Bearer Auth)")
    Rel(circus, ext_memory, "Syncs episodic & semantic vectors", "HTTPS / JSON")
```

### 2.2 C4 Level 2: Container Diagram

```mermaid
C4Container
    title Container Diagram - Carnot Cycle Circus Components

    Person(user, "Engineer", "Uses Web Browser")

    Container_Boundary(c1, "Carnot Cycle Circus Application") {
        Container(web, "Blazor Web Frontend", "ASP.NET Core 10 / Blazor Interactive Server", "Provides Kanban Studio, Visual DAG Canvas, Execution Dashboard, Memory Inspector, Key Vault Modal, and ADR Hub.")
        
        Container_Boundary(core, "CarnotCycleCircus.Core (Domain & Engine)") {
            Component(tickets, "Ticket & Handoff Engine", "ITicketStore, IWorkDecompositionEngine, IHandoffRouter", "Manages Epics, Stories, Subtasks, DAG dependency resolution, and inter-agent handoff packets.")
            Component(memory, "Hierarchical Memory System", "IPersistentMemoryStore, IMemoryConsolidationEngine", "4-tier memory, 64-dim vector cosine similarity search, and automated task consolidation.")
            Component(orchestrator, "Graph Workflow Orchestrator", "IGraphWorkflowExecutor, WorkflowGraph", "Executes DAG nodes, tracks execution states, routes failure ports, and applies circuit breakers.")
            Component(inference, "Inference & Key Vault Hub", "IApiKeyVaultService, IOpenRouterClient, ISimulatedScenarioEngine", "Resolves keys and models per agent, routes LLM completions, or executes offline deterministic simulations.")
            Component(governance, "Standards & ADR Hub", "IStandardsValidator, IAdrDocumentManager", "Validates ticket policies, generates MADR/Nygard ADRs, and formats documentation bundles.")
            Component(knowledge, "Knowledge Maps & Skills", "IKnowledgeMapService, ISkillRegistry", "Maintains AI concept graphs, extracts sub-graphs, and registers role capabilities.")
            Component(stream, "Real-Time Event Stream", "IAgentEventStream", "Thread-safe in-memory pub/sub message bus broadcasting telemetry and agent banter.")
        }
    }

    System_Ext(openrouter, "OpenRouter API Gateway", "Unified multi-model LLM inference provider (Claude 3.7, GPT-4o, DeepSeek-R1, Qwen 2.5 Coder, o3-mini).")

    Rel(user, web, "Navigates & interacts", "HTTPS")
    Rel(web, orchestrator, "Triggers workflow runs", "In-Process C#")
    Rel(web, tickets, "Queries & updates tickets", "In-Process C#")
    Rel(web, stream, "Subscribes for live UI updates", "C# Action Events")
    Rel(orchestrator, tickets, "Deconstructs work & routes handoffs", "In-Process C#")
    Rel(orchestrator, inference, "Requests LLM deliverables", "In-Process C#")
    Rel(orchestrator, memory, "Queries context & stores consolidated memory", "In-Process C#")
    Rel(orchestrator, governance, "Enforces quality gates & records ADRs", "In-Process C#")
    Rel(inference, openrouter, "Sends completions requests", "HTTPS / JSON")
```

---

## 3. Layered Solution Structure

The repository follows a clean, decoupled architecture:

```
carnot-cycle-circus/
├── CarnotCycleCircus.slnx               # Modern XML-based solution manifest
├── Directory.Build.props               # Central compiler warnings, C# 13, TreatWarningsAsErrors
├── Directory.Packages.props            # Central Package Management (CPM)
│
├── src/
│   ├── CarnotCycleCircus.Core/         # Core Domain & Orchestration Library (.NET 10.0)
│   │   ├── Domain/
│   │   │   ├── Agents/                 # AgentRole, AgentPersona, EngineeringTeam
│   │   │   ├── Artifacts/              # ArtifactDescriptor, ArtifactManager, IArtifactManager
│   │   │   ├── Blueprints/             # ProjectBlueprintService
│   │   │   ├── Docs/                   # AdrDocumentManager, ArchitecturalDecisionRecord
│   │   │   ├── Events/                 # AgentEventStream, AgentMessage, ArtifactItem
│   │   │   ├── Graph/                  # WorkflowGraph, GraphWorkflowExecutor, Ports
│   │   │   ├── Harvester/              # CodebaseHarvesterService
│   │   │   ├── Inference/              # ApiKeyVaultService, OpenRouterClient, SimulatedScenarioEngine, ModelCatalogService
│   │   │   ├── Knowledge/              # KnowledgeMapService, KnowledgeNode, KnowledgeEdge
│   │   │   ├── Learning/               # SelfImprovementEngine, AutonomousSelfImprovementWorker
│   │   │   ├── Memory/                 # MemoryEntry, PersistentMemoryStore, MemoryServices
│   │   │   ├── Security/               # EncryptedPayload, MasterKeyProvider, AesGcmKeyEncryptor
│   │   │   ├── Showcase/               # ShowcaseDemoService
│   │   │   ├── Skills/                 # SkillRegistry, SkillImporter, SkillDefinition
│   │   │   ├── Standards/              # StandardsValidator, EngineeringStandardsProfile
│   │   │   ├── Storage/                # CarnotStorageOptions, FilePersistentStorageService
│   │   │   ├── Teams/                  # TeamDefinitionManager, TeamArchetypes
│   │   │   ├── Tickets/                # TicketItem, TicketStore, WorkDecompositionEngine, HandoffRouter
│   │   │   └── Tools/                  # IToolDefinition, WebSearch, CSharpSyntaxCheck, TestRunner
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs # Centralized DI registration
│   │
│   └── CarnotCycleCircus.Web/          # Blazor Web Frontend (.NET 10.0)
│       ├── Components/
│       │   ├── Layout/                 # MainLayout, NavMenu
│       │   ├── Pages/                  # 13 Interactive Blazor Pages (ArtifactsHub, Canvas, Dashboard, etc.)
│       │   └── Modals/                 # KeyVaultModal, ProjectIgnitionModal, CodebaseHarvesterModal, ShowcaseModal, TicketModal
│       ├── Program.cs                  # Host configuration & pipeline setup
│       └── wwwroot/                    # Modern dark-theme stylesheets & UI assets
│
├── tests/
│   └── CarnotCycleCircus.Tests/        # Comprehensive Unit & Integration Tests (xUnit + FluentAssertions)
│       └── [17 Test Suites]           # Complete coverage across all domain services
│
├── skills/                             # Preserved engineering skills & .NET standards
└── docs/                               # Comprehensive Human & LLM Documentation Suite
```

---

## 4. End-to-End Execution Flow

When a user initiates an Epic execution (e.g. from the **Execution Dashboard** or **Workflow Canvas**), the system orchestrates the following sequence:

```mermaid
sequenceDiagram
    autonumber
    actor User as User / Operator
    participant Executor as GraphWorkflowExecutor
    participant TPM as TPM (Barnum B. Buzzword)
    participant Decomp as WorkDecompositionEngine
    participant Store as TicketStore
    participant Arch as Architect (Archduke Archibald)
    participant Dev as Developer (Devon Crashdump)
    participant Sec as Security (Sari Sandbox)
    participant Optimizer as Optimizer (Otto-Cycle Overclock)
    participant QA as QA Analyst (Quinn Build-Executioner)
    participant Handoff as HandoffRouter
    participant Mem as MemorySystem
    participant Bus as AgentEventStream

    User->>Executor: ExecuteWorkflowAsync("User Auth & Token Rotation", desc)
    Executor->>Bus: Publish Workflow Started Event

    %% 1. TPM Phase
    Executor->>TPM: Activate TPM Node
    TPM->>Decomp: DeconstructEpic("User Auth...", desc)
    Decomp->>Store: Create Epic + User Story 1
    Decomp->>Store: Create 5 Subtasks (Arch, Dev, Sec, Opt, QA) with DAG dependencies
    TPM->>TPM: Generate PRD Deliverable Artifact
    TPM->>Store: Attach PRD Artifact to Epic Ticket
    TPM->>Bus: Publish PRD & Decomposition banter

    %% 2. Architect Phase
    Executor->>Arch: Activate Lead Architect Node (Subtask 1, Injected PRD context)
    Arch->>Arch: Produce ADR-014 with exact C# type & interface contracts
    Arch->>Store: Attach ADR Artifact to Subtask 1
    Arch->>Handoff: RouteSuccessHandoff(Subtask 1 -> Subtask 2 [Dev])
    Handoff->>Store: Record HandoffPacket
    Handoff->>Store: AdvanceWorkflowOnTicketCompletion(Subtask 1 -> Done)
    Handoff->>Store: Subtask 2 [Dev] status -> Ready
    Arch->>Mem: ConsolidateTaskCompletionAsync(Subtask 1)

    %% 3. Developer Phase
    Executor->>Dev: Activate Software Developer Node (Subtask 2, Injected ADR context)
    Dev->>Dev: Author modular multi-file C# bundle (Models, Service, DI, xUnit Tests)
    Dev->>Dev: Autonomous syntax self-healing pass (CSharpSyntaxCheckTool)
    Dev->>Store: Attach Multi-File C# Code Artifacts to Subtask 2
    Dev->>Handoff: RouteSuccessHandoff(Subtask 2 -> Sec & Opt)
    Handoff->>Store: AdvanceWorkflowOnTicketCompletion(Subtask 2 -> Done)
    Handoff->>Store: Subtask 3 [Sec] & Subtask 4 [Opt] status -> Ready
    Dev->>Mem: ConsolidateTaskCompletionAsync(Subtask 2)

    %% 4. Parallel Review Phase
    par Security Review
        Executor->>Sec: Activate Security Node (Subtask 3)
        alt Failure Simulation Active
            Sec->>Handoff: RouteFailureRemediation(Subtask 3 -> Dev, "Vulnerability Detected")
            Handoff->>Store: Subtask 3 status -> Remediating (Assigned to Dev)
            Dev->>Dev: Remediate input validation
            Dev->>Sec: Return remediated code
        end
        Sec->>Sec: Produce STRIDE Threat Model Matrix (Approved)
        Sec->>Handoff: AdvanceWorkflowOnTicketCompletion(Subtask 3 -> Done)
    and Optimization Review
        Executor->>Optimizer: Activate Optimizer Node (Subtask 4)
        Optimizer->>Optimizer: Benchmark hot paths (< 5ms P99, 0 Gen0 Allocations)
        Optimizer->>Handoff: AdvanceWorkflowOnTicketCompletion(Subtask 4 -> Done)
    end

    %% 5. QA Phase
    Note over Store: Subtask 5 [QA] dependencies satisfied (Sec & Opt Done)
    Handoff->>Store: Subtask 5 [QA] status -> Ready
    Executor->>QA: Activate Principal QA Analyst Node (Subtask 5)
    QA->>QA: Execute test runner, verify 100% acceptance criteria, scorecard
    QA->>Store: Attach QA Scorecard Artifact
    QA->>Handoff: AdvanceWorkflowOnTicketCompletion(Subtask 5 -> Done)
    QA->>Mem: ConsolidateTaskCompletionAsync(Subtask 5)

    %% 6. Release & Completion
    Executor->>Store: Mark Parent Story & Epic as Done
    Executor->>Bus: Publish Workflow Completed Event (🏆 100% Thermodynamic Efficiency)
    Executor-->>User: Return Execution Result (Success)
```

---

## 5. Dependency Injection Architecture

All core services are registered as singletons using `IServiceCollection` extension methods in `CarnotCycleCircus.Core.Extensions.ServiceCollectionExtensions`:

```csharp
public static IServiceCollection AddCarnotCycleCircusCore(this IServiceCollection services)
{
    // Event Stream & Message Bus
    services.AddSingleton<IAgentEventStream, AgentEventStream>();

    // Ticket Management & Work Decomposition Engine
    services.AddSingleton<ITicketStore, TicketStore>();
    services.AddSingleton<IWorkDecompositionEngine, WorkDecompositionEngine>();
    services.AddSingleton<IHandoffRouter, HandoffRouter>();

    // Memory Layer
    services.AddSingleton<IPersistentMemoryStore, EmbeddedVectorMemoryStore>();
    services.AddSingleton<IExternalMemoryConnector, ExternalMemoryConnector>();
    services.AddSingleton<IMemoryConsolidationEngine, MemoryConsolidationEngine>();
    services.AddSingleton<IContextAwareMemoryInjector, ContextAwareMemoryInjector>();

    // Inference & Key Vault
    services.AddSingleton<IApiKeyVaultService, ApiKeyVaultService>();
    services.AddSingleton<IOpenRouterClient, OpenRouterClient>();
    services.AddSingleton<IAgentInferenceResolver, AgentInferenceResolver>();
    services.AddSingleton<ISimulatedScenarioEngine, SimulatedScenarioEngine>();

    // Tools Sandbox
    services.AddSingleton<IToolDefinition, WebSearchTool>();
    services.AddSingleton<IToolDefinition, CSharpSyntaxCheckTool>();
    services.AddSingleton<IToolDefinition, TestRunnerTool>();
    services.AddSingleton<IToolDefinition, AdrWriterTool>();
    services.AddSingleton<IToolDefinition, MemoryLookupTool>();

    // ADR & Documentation Hub
    services.AddSingleton<IAdrDocumentManager, AdrDocumentManager>();

    // Deliverables & Artifacts Hub
    services.AddSingleton<IArtifactManager, ArtifactManager>();

    // Standards & Quality Gates
    services.AddSingleton<IStandardsValidator, StandardsValidator>();

    // AI Knowledge Maps
    services.AddSingleton<IKnowledgeMapService, KnowledgeMapService>();

    // Teams & Archetypes
    services.AddSingleton<ITeamDefinitionManager, TeamDefinitionManager>();

    // Skills & Importer
    services.AddSingleton<ISkillImporter, SkillImporter>();
    services.AddSingleton<ISkillRegistry, SkillRegistry>();

    // Graph Orchestrator & Workflow Executor
    services.AddSingleton<IGraphWorkflowExecutor, GraphWorkflowExecutor>();

    return services;
}
```
