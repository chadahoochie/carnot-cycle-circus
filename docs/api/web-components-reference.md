# Web Components & UI Reference 🎨💻

This document provides a technical reference for the interactive **Blazor Web Components** in **`CarnotCycleCircus.Web`**.

---

## 1. Routing & Page Directory

| Route | Razor Component | Description |
| :--- | :--- | :--- |
| **`/`**, **`/teams`**, **`/canvas`** | `TeamStudio.razor` | Unified Team Management & Interactive CLAW Canvas: squad creation/cloning, visual node placement, Input/Output/Failure port cabling, circuit breaker policies, and live execution/stepping. |
| **`/agents`** | `AgentManager.razor` | Centralized Agent Studio for defining and customizing individual agent personas, system prompts, role models, and skill assignments. |
| **`/dashboard`** | `ExecutionDashboard.razor` | Live workflow orchestrator, animated execution pulse map, real-time banter feed, quick-key swapper, and session report exporter. |
| **`/artifacts`** | `ArtifactsHub.razor` | Real-time repository-synced deliverables hub (PRDs, ADRs, Code bundles, STRIDE models, Benchmark profiles, and QA scorecards) with category filtering and 1-click disk synchronization. |
| **`/tickets`** | `TicketManager.razor` | Embedded ticket studio: Kanban board, backlog manager, dependency CLAW visualizer, and handoff history. |
| **`/memory`** | `MemoryInspector.razor` | Hierarchical memory viewer (Working, Episodic, Semantic, Procedural), 64-dim vector search tester, and memory pruner. |
| **`/models`** | `ModelCatalog.razor` | OpenRouter model catalog browser: cost tier classification, engineering strength mapping, pricing calculations ($/1M tokens), and 1-click favorites management. |
| **`/docs`** | `DocsAndAdrs.razor` | ADR editor/explorer, C4 system architecture diagrams, STRIDE threat models, and markdown bundle exporter. |
| **`/standards`** | `StandardsManager.razor` | Quality gates editor, test coverage sliders, and ticket completion compliance policies. |
| **`/knowledge`** | `KnowledgeMapExplorer.razor` | Visual concept graph explorer, node/edge relationship viewer, and semantic sub-graph query tester. |
| **`/skills`** | `SkillManager.razor` | Dynamic `SKILL.md` parser, URL importer, and custom capability editor. |
| **`/skill-matrix`** | `SkillMatrix.razor` | Interactive agent capability grid with one-click skill assignment toggles per role. |
| **`/self-improvement`** | `SelfImprovementStudio.razor` | Autonomous self-improvement loop: failure lesson distillation, anti-pattern detection, and persistent rule synthesis. |

---

## 2. Shared Layout & Modals

### 2.1 `MainLayout.razor`
- **Header**: Displays system branding, active team name, live message stream counter, and active API Key quick-status.
- **Key Vault Trigger**: Houses the global Key Vault button that opens `KeyVaultModal`.
- **Navigation**: Collapsible sidebar linking to all 13 platform views.

### 2.2 `KeyVaultModal.razor`
- In-memory and disk-encrypted client-side key storage dialog (`ApiKeyVaultService`).
- Allows adding named OpenRouter API keys with AES-256-GCM AEAD encryption.
- Masks raw credentials with asterisk obfuscation.
- Performs live key connectivity tests against OpenRouter's auth endpoint.
- Master key rotation and passphrase-encrypted backup export/import.
- One-click active key switching.

### 2.3 `ProjectIgnitionModal.razor`
- 1-Click Curated Blueprint Launcher (IoT Telemetry Pipeline, E-Commerce Saga, Zero-Trust Identity, Distributed CQRS, Chaos Benchmark Arena) and Custom Project Starter.

### 2.4 `CodebaseHarvesterModal.razor`
- Local directory / solution scanner extracting project dependencies, architecture patterns, and technical debt audit into prioritized backlog tickets.

### 2.5 `ShowcaseModal.razor`
- Interactive zero-key live demo runner showcasing end-to-end swarm execution, failure remediation loopbacks, and nanosecond memory benchmarks.

### 2.6 `TicketModal.razor`
- Interactive dialog for creating and editing `TicketItem` entities.
- Fields: Title, Description, Type (`Epic`, `Feature`, `Bug`, `Spike`, `Subtask`), Priority, Assignee Role, Acceptance Criteria, and Dependency selectors (`DependsOn`).

### 2.7 `TicketCard.razor`
- Compact visual card for tickets inside Kanban columns and backlog lists.
- Badges: Assignee emoji/color, ticket type, priority, and dependency fulfillment indicators.

### 2.8 `ChaosQuoteModal.razor`
- Cult-classic comedy quote browser and chaos generator (Spaceballs, Spinal Tap, Silicon Valley, Ghostbusters, Monty Python).

---

## 3. UI Reactivity & Event Stream Pattern

All interactive pages subscribe to `IAgentEventStream` and `ITicketStore` events upon initialization and unsubscribe upon disposal (`IDisposable`):

```csharp
@implements IDisposable
@inject IAgentEventStream EventStream
@inject ITicketStore TicketStore

@code {
    protected override void OnInitialized()
    {
        EventStream.OnMessagePublished += HandleMessagePublished;
        TicketStore.OnTicketChanged += HandleTicketChanged;
    }

    private void HandleMessagePublished(AgentMessage message)
    {
        // Invoke on Blazor UI dispatcher thread to prevent cross-thread synchronization issues
        InvokeAsync(StateHasChanged);
    }

    private void HandleTicketChanged(TicketItem ticket)
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        EventStream.OnMessagePublished -= HandleMessagePublished;
        TicketStore.OnTicketChanged -= HandleTicketChanged;
    }
}
```

---

## 4. UI Design System & Styling (`wwwroot/css/app.css`)

- **Palette**: Dark modern palette (`#0f172a`, `#1e293b`, `#334155`).
- **Role Accents**:
  - Technical Product Manager: `#38bdf8` (Sky Blue)
  - Lead Architect: `#a855f7` (Purple)
  - Software Developer: `#10b981` (Emerald Green)
  - Security Engineer: `#ef4444` (Red)
  - Optimization Engineer: `#f59e0b` (Amber)
  - Principal QA Analyst: `#ec4899` (Pink)
- **Status Indicators**: Neon badges for `Ready` (Green), `InProgress` (Blue), `Remediating` (Orange/Red), `Done` (Green).
