# Developer Onboarding Guide 🧭🚀

Welcome to **Carnot Cycle Circus**! This guide walks you through setting up your development environment, building the solution, running tests, exploring the codebase, and launching the interactive Blazor UI.

---

## 1. Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/)** (`10.0.300+` or newer).
- **Modern Web Browser** (Chrome, Firefox, Edge, Safari).
- **Git** version control.
- *(Optional)* **OpenRouter API Key** if you want to run live LLM completions (an offline simulation engine runs automatically if no key is provided).

---

## 2. 60-Second Quickstart

```bash
# 1. Clone the repository
git clone https://github.com/chadahoochie/carnot-cycle-circus.git
cd carnot-cycle-circus

# 2. Build the solution
dotnet build CarnotCycleCircus.slnx

# 3. Run all unit and integration test suites
dotnet test CarnotCycleCircus.slnx --logger "console;verbosity=normal"

# 4. Launch the Native Desktop Client (Linux / macOS / Windows)
dotnet run --project src/CarnotCycleCircus.Desktop

# Or launch the Headless Agent Server in Docker
docker compose up -d

# Or launch the Blazor Web Application
dotnet run --project src/CarnotCycleCircus.Web
```

Open your browser and navigate to **`http://localhost:5000`** (or the URL printed in your console).

---

## 3. Repository Map & Key Directories

```
carnot-cycle-circus/
├── CarnotCycleCircus.slnx               # Solution definition
├── Directory.Build.props               # Central build properties & compiler warnings
├── Directory.Packages.props            # Central Package Management (CPM)
│
├── src/
│   ├── CarnotCycleCircus.Core/         # Core domain library & orchestrator
│   │   ├── Domain/Agents/              # 8 engineering roles, personas, & team manifests
│   │   ├── Domain/Tickets/             # Hierarchical tickets, DAG engine, & handoff router
│   │   ├── Domain/Memory/              # 4-tier OpenViking memory & embedded vector store
│   │   ├── Domain/Inference/           # OpenRouter client, Key Vault, & simulation engine
│   │   ├── Domain/Graph/               # Visual DAG workflow graph & executor
│   │   ├── Domain/Docs/                # ADR manager & documentation bundle generator
│   │   ├── Domain/Standards/           # Quality gates & ticket validation engine
│   │   ├── Domain/Knowledge/           # AI knowledge map graph & semantic extraction
│   │   ├── Domain/Skills/              # Dynamic SKILL.md parser & skill matrix
│   │   ├── Domain/Tools/               # Sandboxed executable agent tools
│   │   ├── Domain/Storage/             # Persistent storage engine (~/.carnot)
│   │   └── Domain/Events/              # Real-time pub/sub event stream
│   │
│   ├── CarnotCycleCircus.UI/           # Shared Razor Class Library (Pages, Modals, Themes)
│   ├── CarnotCycleCircus.Desktop/      # Native Desktop App (Photino.Blazor)
│   ├── CarnotCycleCircus.Server/       # Headless Docker Agent Host (Minimal API + SignalR)
│   └── CarnotCycleCircus.Web/          # Blazor Interactive Server Web Host
│
├── tests/
│   └── CarnotCycleCircus.Tests/        # 30+ xUnit & FluentAssertions test suites
│
├── scripts/                            # Local install & Docker scripts
├── skills/                             # Preserved engineering skills & .NET standards
└── docs/                               # Comprehensive Human & LLM Documentation Suite
```

---

## 4. Key Concepts in 5 Minutes

1. **The 6 Roles**: The system organizes engineering into **Technical Product Manager (TPM)**, **Lead Architect**, **Software Developer**, **Security Engineer**, **Optimization Engineer**, and **Principal QA Analyst**.
2. **Work Decomposition**: When you submit an objective, the TPM creates an Epic and core user stories. The Lead Architect breaks user stories down into five atomic technical subtasks with DAG dependency edges (`DependsOnTicketIds`).
3. **Connectable DAG Canvas**: Workflows are visual node graphs. Each node has an **Input (🟢)**, **Output (🔵)**, and **Failure (🔴)** port. If Security or QA rejects a deliverable, the failure port trips and loops back to Developer for remediation.
4. **Hierarchical Memory**: The system stores working, episodic, semantic, and procedural memories, indexing them using local 64-dimensional vector embeddings and cosine similarity.
5. **Key Vault & OpenRouter**: Manage named API keys in memory, assign optimal models per role, swap keys live mid-flight, or run in zero-cost offline simulation mode.

---

## 5. Daily Development Workflows

### Running Single Test Suites
```bash
# Run ticket store tests
dotnet test --filter "FullyQualifiedName~TicketStoreTests"

# Run persistent memory tests
dotnet test --filter "FullyQualifiedName~PersistentMemoryTests"

# Run workflow graph tests
dotnet test --filter "FullyQualifiedName~WorkflowGraphTests"
```

### Navigating the Blazor UI
- **`/` (Dashboard)**: Start automated or step-by-step workflow runs, monitor live agent dialogue, and view execution pulses.
- **`/tickets`**: View backlog, Kanban board, and dependency DAG visualizer.
- **`/canvas`**: Edit visual graph topology, adjust nodes, and connect ports.
- **`/memory`**: Inspect memory tiers, test semantic vector queries, and trigger pruning.
- **`/teams`**: Switch team archetypes (e.g. *Balanced Circus*, *Paranoid Security Bunker*, *Zero-Allocation Zealots*).
- **`/docs`**: Explore ADRs, C4 diagrams, and export Markdown bundles.
- **`/standards`**: Configure ticket quality gate policies.
- **`/knowledge`**: Explore the AI concept knowledge graph.
- **`/skills` & `/skill-matrix`**: Import custom `SKILL.md` files and toggle role capabilities.

---

## 6. Local Troubleshooting

- **Build errors with warnings**: `TreatWarningsAsErrors` is enabled. Resolve all compiler warnings.
- **Port already in use**: If port 5000 is occupied, set `--urls "http://localhost:5050"` when running `dotnet run`.
- **API Key testing**: Open the **Key Vault Modal** (click the key icon in the top navbar) and click **Test Connection** to verify your OpenRouter key.
