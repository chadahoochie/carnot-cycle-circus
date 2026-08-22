# CarnotCycleCircus.Web Guidelines

Interactive Blazor frontend application for Carnot Cycle Circus.

## Architecture & Responsibilities
- **Pages/TicketManager.razor**: Embedded Ticket Studio with interactive Kanban board, Epic/Story/Subtask decomposition DAG tree, and inter-agent handoff history drawer.
- **Pages/WorkflowCanvas.razor**: Visual drag-and-drop workflow graph editor with Input, Output, and Failure ports.
- **Pages/ExecutionDashboard.razor**: Real-time live execution dashboard with animated graph execution pulses, live key swapper, chat message stream, and session report exporter.
- **Pages/MemoryInspector.razor**: Hierarchical memory inspector, vector search tester, and memory pruner.
- **Pages/TeamDefinition.razor**: Team roster management, persona customization, OpenRouter model picker, and archetype loader.
- **Pages/DocsAndAdrs.razor**: ADR explorer/editor, C4 architecture diagrams, STRIDE threat models, and markdown bundle exporter.
- **Pages/StandardsManager.razor**: Engineering standards profile editor and ticket policy manager.
- **Pages/KnowledgeMapExplorer.razor**: Interactive visual AI knowledge graph explorer and semantic query interface.
- **Pages/SkillManager.razor & SkillMatrix.razor**: Dynamic skill importer, editor, and agent skill capability matrix.

## UI/UX Standards
1. **Dark Theme**: High-contrast, modern dark palette with neon status indicators (🟢 Ready/Success, 🔵 In Progress, 🔴 Failure/Remediation, 🟡 Review/Blocked).
2. **Reactivity**: Subscribe to `AgentEventStream` and invoke `StateHasChanged()` on the Blazor dispatcher thread to render real-time agent updates.
3. **Accessibility**: Semantic HTML, proper ARIA labels, and responsive layout for mobile/desktop.
