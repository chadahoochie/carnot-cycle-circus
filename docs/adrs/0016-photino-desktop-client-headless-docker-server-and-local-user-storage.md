# ADR-0016: Photino.Blazor Desktop Client, Headless Docker Server & Local ~/.carnot Multi-Mount Storage

## Status
**Accepted** (2026-08-29)

## Context
Carnot Cycle Circus previously operated as a monolithic Blazor Server application (`CarnotCycleCircus.Web`). As autonomous engineering workflows grew, several key operational and platform requirements emerged:

1. **Native Cross-Platform Desktop Support on Linux**:
   - .NET MAUI does not have official Microsoft support on Linux desktop (`net10.0-linux`). Community GTK/Avalonia wrappers suffer from stability and `BlazorWebView` compatibility issues.
   - Engineers working on Linux (Ubuntu, Debian, Fedora, Arch) require a first-class, high-performance native desktop window with native OS folder browsing dialogs to select code repositories.

2. **Decoupled Headless Agent Server in Docker**:
   - Users require the ability to run only the server portion hosting the agents in containerized environments (Docker / Kubernetes / remote daemons) without running the web UI inside the container.
   - Persistent storage in Docker requires explicit volume mounts separating server metadata/state (`/carnot/data`) and generated deliverables (`/carnot/artifacts`), with an optional workspace mount (`/workspace`) for inspecting external host codebases.

3. **Local User Profile Storage (`~/.carnot`) & Direct Target Workspace Interaction**:
   - When running locally outside containers, all persistent state (API key vault, vector memory, ticket databases, team configurations) should reside in `~/.carnot/data`, with deliverables in `~/.carnot/artifacts`.
   - The application must allow selecting any target repository on disk, interacting directly with project code, and deploying compiled binaries to `~/.carnot/bin`.

## Decision
We decouple the platform architecture into four distinct components and implement a multi-mount storage model:

1. **Shared UI Razor Class Library (`src/CarnotCycleCircus.UI`)**:
   - Houses all Blazor pages (Execution Dashboard, Workflow Canvas, Kanban Ticket Manager, Memory Inspector, Model Catalog, Docs/ADR Hub), modals, and themes.
   - Defines the `INativeFolderPicker` abstraction for native file dialog integration.

2. **Cross-Platform Native Desktop Application (`src/CarnotCycleCircus.Desktop`)**:
   - Built with **Photino.Blazor** (v3.2.0) using WebKitGTK on Linux, WKWebView on macOS, and WebView2 on Windows.
   - Consumes ~40MB of RAM and provides zero-latency native desktop windows with native OS folder selection dialogs (Zenity, KDialog, AppleScript, Win32).

3. **Headless Agent Host Server (`src/CarnotCycleCircus.Server`)**:
   - Lightweight ASP.NET Core service providing Minimal REST APIs for tickets, blueprints, model catalog, encrypted vault, and self-improvement loops.
   - Implements `AgentStreamHub` (`/hubs/agent-stream`) and `SignalREventBridge` for real-time WebSocket event streaming to connected desktop or remote clients.

4. **Multi-Mount Docker Topology**:
   - `-v ~/.carnot/data:/carnot/data`: Persistent server metadata, encrypted secrets, vector memories, backlog.
   - `-v ~/.carnot/artifacts:/carnot/artifacts`: Generated deliverables (ADRs, code snippets, STRIDE models, QA scorecards).
   - `-v /path/to/my-repo:/workspace`: Optional target host codebase mount.
   - Base Docker image includes the **.NET 10 SDK** so agent tools (`CSharpSyntaxCheckTool`, `TestRunnerTool`) can compile and test code inside the container.

5. **Storage Resolution Hierarchy (`CarnotStorageOptions`)**:
   - `DataDirectory`: `CARNOT_DATA_DIR` $\to$ `~/.carnot/data` $\to$ `./data`.
   - `ArtifactsDirectory`: `CARNOT_ARTIFACTS_DIR` $\to$ `~/.carnot/artifacts` $\to$ `./artifacts`.
   - `WorkspaceDirectory`: `CARNOT_WORKSPACE_DIR` $\to$ `/workspace` $\to$ Selected Target Directory.

## Consequences

### Positive
- ✅ First-class native Linux desktop client via Photino.Blazor with native folder dialogs.
- ✅ 100% UI code reuse across desktop and web deployments via `CarnotCycleCircus.UI`.
- ✅ Clean headless Docker deployment with explicit separation of server data, deliverables, and host workspaces.
- ✅ Direct interaction with any local repository on the user's filesystem.
- ✅ Support for local binary installation into `~/.carnot/bin/`.

### Negative / Trade-offs
- ⚠️ Photino desktop client requires WebKitGTK installed on Linux systems (`libwebkit2gtk-4.1` / `libwebkit2gtk-4.0`), standard on all modern desktop distributions.
