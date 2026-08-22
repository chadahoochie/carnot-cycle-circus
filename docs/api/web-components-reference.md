# Web Components & UI Reference 🎨💻

This document provides a technical reference for the interactive **Blazor Web Components** in **`CarnotCycleCircus.Web`**.

---

## 1. Routing & Page Directory

| Route | Razor Component | Description |
| :--- | :--- | :--- |
| **`/`** | `ExecutionDashboard.razor` | Live workflow orchestrator, animated execution pulse map, real-time banter feed, quick-key swapper, and session report exporter. |
| **`/tickets`** | `TicketManager.razor` | Embedded ticket studio: Kanban board, backlog manager, dependency DAG visualizer, and handoff history. |
| **`/canvas`** | `WorkflowCanvas.razor` | Visual drag-and-drop workflow canvas: node placement, Input/Output/Failure port cabling, and live state highlights. |
| **`/memory`** | `MemoryInspector.razor` | Hierarchical memory viewer (Working, Episodic, Semantic, Procedural), 64-dim vector search tester, and memory pruner. |
| **`/teams`** | `TeamDefinition.razor` | Roster manager, archetype loader (6 presets), persona prompt editor, and per-role model/key overrides. |
| **`/docs`** | `DocsAndAdrs.razor` | ADR editor/explorer, C4 system architecture diagrams, STRIDE threat models, and markdown bundle exporter. |
| **`/standards`** | `StandardsManager.razor` | Quality gates editor, test coverage sliders, and ticket completion compliance policies. |
| **`/knowledge`** | `KnowledgeMapExplorer.razor` | Visual concept graph explorer, node/edge relationship viewer, and semantic sub-graph query tester. |
| **`/skills`** | `SkillManager.razor` | Dynamic `SKILL.md` parser, URL importer, and custom capability editor. |
| **`/skill-matrix`** | `SkillMatrix.razor` | Interactive agent capability grid with one-click skill assignment toggles per role. |

---

## 2. Shared Layout & Modals

### 2.1 `MainLayout.razor`
- **Header**: Displays system branding, active team name, live message stream counter, and active API Key quick-status.
- **Key Vault Trigger**: Houses the global Key Vault button that opens `KeyVaultModal`.
- **Navigation**: Collapsible sidebar linking to all 10 platform views.

### 2.2 `KeyVaultModal.razor`
- In-memory client-side key storage dialog.
- Allows adding named OpenRouter API keys.
- Masks raw credentials with asterisk obfuscation.
- Performs live key connectivity tests against OpenRouter's auth endpoint.
- One-click active key switching.

### 2.3 `TicketModal.razor`
- Interactive dialog for creating and editing `TicketItem` entities.
- Fields: Title, Description, Type (`Epic`, `Feature`, `Bug`, `Spike`, `Subtask`), Priority, Assignee Role, Acceptance Criteria, and Dependency selectors (`DependsOn`).

### 2.4 `TicketCard.razor`
- Compact visual card for tickets inside Kanban columns and backlog lists.
- Badges: Assignee emoji/color, ticket type, priority, and dependency fulfillment indicators.

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
