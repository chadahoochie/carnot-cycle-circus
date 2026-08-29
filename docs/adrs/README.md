# Architectural Decision Records (ADRs) Catalog 🏛️📋

This directory contains the formal **Architectural Decision Records (ADRs)** for **Carnot Cycle Circus**.

ADRs record key architectural decisions, the context behind them, alternatives evaluated, and positive/negative trade-offs.

---

## 📑 ADR Index & Status Matrix

| ADR # | Title | Status | Date | Decision Summary |
| :--- | :--- | :--- | :--- | :--- |
| [**ADR-0001**](0001-immutable-record-types-for-domain-and-handoff-payloads.md) | Adopt Immutable Record Types for Domain & Handoff Payloads | **Accepted** | 2026-08-19 | Use C# `record` and `readonly record struct` across all domain entities to eliminate concurrency race conditions and enforce non-destructive mutations. |
| [**ADR-0002**](0002-connectable-dag-workflow-with-failure-ports.md) | Connectable DAG Workflow Engine with Dedicated Failure Ports | **Accepted** | 2026-08-20 | Expose Input (🟢), Output (🔵), and Failure (🔴) ports on workflow nodes to support self-healing remediation loops without aborting execution. |
| [**ADR-0003**](0003-hierarchical-persistent-memory-and-embedded-vector-store.md) | Hierarchical Persistent Memory & Embedded Vector Store | **Accepted** | 2026-08-20 | Implement a 4-tier memory architecture (Working, Episodic, Semantic, Procedural) with zero-dependency 64-dim cosine vector similarity search. |
| [**ADR-0004**](0004-multi-key-openrouter-routing-and-key-vault-isolation.md) | Multi-Key OpenRouter Inference Routing & Key Vault Isolation | **Accepted** | 2026-08-21 | Enable per-role model assignment via OpenRouter with client-side credential vaulting, live mid-flight swapping, and offline simulation fallback. |
| [**ADR-0005**](0005-deliverable-isolation-contract-for-agent-personas.md) | Deliverable Isolation Contract for Agent Personas | **Accepted** | 2026-08-21 | Strict separation: agent banter and cynical dialogue are restricted to chat/thought streams, while technical deliverables must remain 100% professional and standard-compliant. |
| [**ADR-0006**](0006-in-memory-reactive-event-stream-for-real-time-telemetry.md) | In-Memory Reactive Event Stream for Real-Time Telemetry | **Accepted** | 2026-08-21 | High-throughput, thread-safe message bus (`IAgentEventStream`) for live Blazor UI updates, session recording, and audit replay. |
| [**ADR-0007**](0007-embedded-ticket-management-and-dag-decomposition.md) | Embedded Ticket Management & DAG Work Decomposition | **Accepted** | 2026-08-21 | First-class ticket management ($Epics \to Stories \to Subtasks$) with automated TPM/Architect decomposition and structured `HandoffPacket` payloads. |
| [**ADR-0008**](0008-persistent-volume-stack-and-autonomous-self-improvement.md) | Persistent Storage Engine & Autonomous Self-Improvement Docker Stack | **Accepted** | 2026-08-22 | File-backed atomic persistent storage across named Docker volumes (`carnot_data`, `carnot_artifacts`, `carnot_skills`) and continuous autonomous self-improvement loop. |
| [**ADR-0009**](0009-secure-key-vault-storage-and-envelope-encryption.md) | Secure Key Storage, AEAD Envelope Encryption, and Master Key Derivation | **Accepted** | 2026-08-22 | Authenticated AES-256-GCM AEAD envelope encryption at rest, PBKDF2-HMAC-SHA256 master key derivation with 310,000 iterations, master key rotation, and encrypted backup export. |
| [**ADR-0010**](0010-dynamic-agent-lifecycle-and-skill-infused-naming.md) | Dynamic Agent Troupe Lifecycle and Skill-Infused Agent Naming Engine | **Accepted** | 2026-08-24 | Enable dynamic addition/removal of agents, unique member IDs, skill-infused absurd circus agent name generation, and automated prompt synthesis. |
| [**ADR-0011**](0011-project-ignition-wizard-codebase-harvester-and-showcase-arena.md) | Project Ignition Wizard, Codebase Harvester, and Zero-Setup Showcase Arena | **Accepted** | 2026-08-24 | Dual-track onboarding architecture: 1-click curated project blueprints, local codebase & tech debt harvesting, and zero-key interactive showcase arena. |
| [**ADR-0012**](0012-openrouter-model-catalog-caching-categorization-and-favorites.md) | OpenRouter Dynamic Model Ingestion, Local Persistent Caching, Strength & Cost Categorization, and Favorites System | **Accepted** | 2026-08-25 | Dynamic model ingestion with persistent caching, 4-tier token cost classification, 6-discipline engineering strength area mapping, and 1-click favorites management. |
| [**ADR-0013**](0013-multi-file-deliverables-syntax-self-healing-and-upstream-context-pipeline.md) | Multi-File Deliverable Generation, Autonomous Syntax Self-Healing, and Inter-Agent Context Pipeline | **Accepted** | 2026-08-28 | Software Developer multi-file C# bundles (Contracts, Services, Extensions, Tests), CSharpSyntaxCheckTool autonomous self-healing loop, upstream deliverable context injection across DAG roles, and first-class PRD categorization. |

---

## 📝 Authoring New ADRs

To propose a new ADR:
1. Copy [template.md](template.md).
2. Number the new record sequentially (e.g. `0008-your-title.md`).
3. Set initial status to `Proposed`.
4. Register the new ADR in this `README.md` index and in `AdrDocumentManager.cs`.
