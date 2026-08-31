# CarnotCycleCircus.Web Guidelines

Interactive Blazor frontend application for Carnot Cycle Circus.

## Architecture & Responsibilities
- **Pages/AgentManager.razor**: Agent Studio for creating, customizing, and managing individual AI agent definitions, model assignments, skills, and system prompts.
- **Pages/TeamStudio.razor**: Team Management & DAG Canvas for assembling multi-agent squads, interactive DAG visual layout, port cabling (Input, Output, Failure), circuit breaker policies, and live execution/stepping.
- **Pages/TicketManager.razor**: Embedded Ticket Studio with interactive Kanban board, Epic/Story/Subtask decomposition DAG tree, and inter-agent handoff history drawer.
- **Pages/ExecutionDashboard.razor**: Real-time live execution dashboard with animated graph execution pulses, live key swapper, chat message stream, and session report exporter.
- **Pages/MemoryInspector.razor**: Hierarchical memory inspector, vector search tester, and memory pruner.
- **Pages/DocsAndAdrs.razor**: ADR explorer/editor, C4 architecture diagrams, STRIDE threat models, and markdown bundle exporter.
- **Pages/StandardsManager.razor**: Engineering standards profile editor and ticket policy manager.
- **Pages/KnowledgeMapExplorer.razor**: Interactive visual AI knowledge graph explorer and semantic query interface.
- **Pages/SkillManager.razor & SkillMatrix.razor**: Dynamic skill importer, editor, and agent skill capability matrix.

## UI/UX Standards
1. **Dark Theme**: High-contrast, modern dark palette with neon status indicators (🟢 Ready/Success, 🔵 In Progress, 🔴 Failure/Remediation, 🟡 Review/Blocked).
2. **Reactivity**: Subscribe to `AgentEventStream` and invoke `StateHasChanged()` on the Blazor dispatcher thread to render real-time agent updates.
3. **Accessibility**: Semantic HTML, proper ARIA labels, and responsive layout for mobile/desktop.
