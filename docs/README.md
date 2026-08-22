# Carnot Cycle Circus Documentation Portal 🎪⚡

Welcome to the comprehensive documentation suite for **Carnot Cycle Circus**, an Autonomous Engineering Agent Orchestration Platform built in **.NET 10 / C# 13** with an interactive **Blazor** frontend — operating at theoretical maximum Carnot thermodynamic efficiency to tame the chaotic 6-ring circus of autonomous engineering agents.

This documentation is designed to enable both **human engineers** (rapid onboarding, architectural comprehension, extension recipes) and **Large Language Models / Autonomous Agents** (deterministic context retrieval, explicit coding contracts, machine-readable schemas).

---

## 🗺️ Documentation Directory Map

```
docs/
├── README.md                      # Central Documentation Portal (this file)
├── LLMS.txt                       # Machine-readable condensed system summary for LLM context
│
├── architecture/                  # Deep architectural specifications & system topologies
│   ├── system-overview.md         # End-to-end architecture, C4 diagrams, and layer boundaries
│   ├── agent-orchestration.md     # 6 core roles, persona contracts, workflow DAG, & failure ports
│   ├── ticket-system.md           # Hierarchical ticket engine, DAG scheduling, & HandoffPackets
│   ├── memory-system.md           # 4-tier OpenViking memory model, vector search, & consolidation
│   ├── inference-and-security.md  # OpenRouter client, Key Vault, offline simulation, & security
│   └── knowledge-and-skills.md    # Knowledge map graph, semantic extraction, & dynamic skill registry
│
├── standards/                     # Non-negotiable engineering standards & governance policies
│   ├── coding-standards.md        # C# 13 / .NET 10 zero-allocation dogma, immutability, & async rules
│   ├── quality-gates.md           # Feature/Bug/Spike policies, RCA rules, STRIDE audits, & coverage
│   └── documentation-standards.md # MADR/Nygard ADR rules, docs-as-code principles, & maintenance
│
├── adrs/                          # Architectural Decision Records (MADR / Nygard format)
│   ├── README.md                  # ADR index, lifecycle status matrix, & decision overview
│   ├── template.md                # Standard template for proposing new ADRs
│   ├── 0001-immutable-record-types-for-domain-and-handoff-payloads.md
│   ├── 0002-connectable-dag-workflow-with-failure-ports.md
│   ├── 0003-hierarchical-persistent-memory-and-embedded-vector-store.md
│   ├── 0004-multi-key-openrouter-routing-and-key-vault-isolation.md
│   ├── 0005-deliverable-isolation-contract-for-agent-personas.md
│   ├── 0006-in-memory-reactive-event-stream-for-real-time-telemetry.md
│   └── 0007-embedded-ticket-management-and-dag-decomposition.md
│
├── guides/                        # Step-by-step developer and agent execution guides
│   ├── developer-onboarding.md    # Quickstart, local build, test execution, & project tour
│   ├── extending-the-platform.md  # Adding roles, custom tools, archetypes, & external connectors
│   ├── llm-agent-guide.md         # LLM context protocol, prompt schemas, & tool execution contracts
│   └── testing-guide.md           # Test suite architecture, xUnit, FluentAssertions, & test recipes
│
└── api/                           # Exhaustive technical reference for Core & Web libraries
    ├── core-domain-reference.md   # Core interfaces, domain models, services, & DI registrations
    └── web-components-reference.md# Blazor pages, UI modals, state binding, & event subscriptions
```

---

## ⚡ Fast Track by Audience

### For New Developers & Contributors
1. Read the [Developer Onboarding Guide](guides/developer-onboarding.md) to set up your environment, build the solution, and run the test suite.
2. Skim the [System Overview](architecture/system-overview.md) to understand the layered architecture and solution structure.
3. Review [Coding Standards](standards/coding-standards.md) before writing any C# code.
4. Learn how to add new features via [Extending the Platform](guides/extending-the-platform.md).

### For Software & Solution Architects
1. Study the [System Overview](architecture/system-overview.md) and [Agent Orchestration Engine](architecture/agent-orchestration.md).
2. Review the [Architectural Decision Records (ADRs)](adrs/README.md) to understand why key technical decisions were made.
3. Review the [Quality Gates & Standards](standards/quality-gates.md) to enforce automated compliance.

### For LLMs & Autonomous Coding Agents
1. Ingest [LLMS.txt](LLMS.txt) for a condensed, high-density overview of all interfaces, types, and constraints.
2. Adhere strictly to the [Deliverable Isolation Contract](adrs/0005-deliverable-isolation-contract-for-agent-personas.md).
3. Follow the [LLM & Agent Interaction Guide](guides/llm-agent-guide.md) for tool schemas and handoff protocol execution.
4. Consult the [Core Domain Reference](api/core-domain-reference.md) for exact method signatures and invariants.

---

## 🎪 Quick System Summary

| Subsystem | Primary Namespace / Project | Core Responsibilities |
| :--- | :--- | :--- |
| **Core Domain** | `CarnotCycleCircus.Core.Domain.Agents` | Defines 6 autonomous engineering roles, persona contracts, and team manifests. |
| **Ticket Engine** | `CarnotCycleCircus.Core.Domain.Tickets` | Hierarchical work breakdown (Epics $\to$ Stories $\to$ Subtasks), DAG scheduling, and `HandoffPacket` routing. |
| **Memory System** | `CarnotCycleCircus.Core.Domain.Memory` | 4-tier persistent memory (Working, Episodic, Semantic, Procedural) with embedded 64-dim vector cosine similarity. |
| **Inference Hub** | `CarnotCycleCircus.Core.Domain.Inference` | OpenRouter multi-key router, client-side Key Vault, and offline simulated scenario engine. |
| **Tool Sandbox** | `CarnotCycleCircus.Core.Domain.Tools` | Sandboxed tools: `web_search`, `csharp_syntax_check`, `test_runner`, `adr_writer`, `memory_lookup`. |
| **Workflow Graph** | `CarnotCycleCircus.Core.Domain.Graph` | Visual connectable DAG engine with Input, Output, and Failure/Reject ports, circuit breakers, and loopbacks. |
| **Docs & ADR Hub** | `CarnotCycleCircus.Core.Domain.Docs` | MADR/Nygard ADR management, C4 diagrams, STRIDE threat models, and markdown bundle exporter. |
| **Standards Engine**| `CarnotCycleCircus.Core.Domain.Standards` | Ticket completion validation, RCA enforcement for bugs, and quality gates. |
| **Knowledge Maps** | `CarnotCycleCircus.Core.Domain.Knowledge` | AI Knowledge graph mapping concepts, patterns, security rules, and sub-graph context extraction. |
| **Skills Registry** | `CarnotCycleCircus.Core.Domain.Skills` | Dynamic `SKILL.md` parser, skill-to-role assignment matrix, and capability registry. |
| **Event Stream** | `CarnotCycleCircus.Core.Domain.Events` | High-throughput, thread-safe real-time telemetry event bus (`IAgentEventStream`). |
| **Blazor Frontend** | `CarnotCycleCircus.Web` | Interactive web dashboard: Kanban, Workflow Canvas, Memory Inspector, Key Vault, ADR Hub, and Replay. |
