# ADR-0003: Hierarchical Persistent Memory & Embedded Vector Store

## Status
**Accepted** (2026-08-20)

## Context
Multi-turn autonomous engineering agents suffer from two memory pitfalls:
1. **Amnesia**: Forgetting previous architectural decisions, API contracts, or bug fix history between tickets.
2. **Context Window Exhaustion**: Appending full conversation histories to prompt contexts, resulting in runaway token costs, high latency, and LLM attention dilution.

External vector databases (e.g. Pinecone, Milvus, Qdrant) introduce heavy external dependencies, API keys, and deployment friction for local development and standalone CLI/Web runs.

## Decision
We implement a 4-tier **Hierarchical Persistent Memory Architecture** (`CarnotCycleCircus.Core.Domain.Memory`) inspired by OpenViking:
1. **Working Memory**: Transient execution steps and scratchpad.
2. **Episodic Memory**: Summaries of completed tickets, deliverables, and lessons learned.
3. **Semantic Memory**: Domain concepts, architectural patterns, and rules.
4. **Procedural Memory**: Reusable workflow templates and tool execution recipes.

The core library ships with an **Embedded Vector Store** (`EmbeddedVectorMemoryStore`) that computes 64-dimensional word-hash vector embeddings and composite similarity scores (cosine similarity + keyword overlap + importance weight) without any external services or native C++ binaries. For enterprise deployments, an `IExternalMemoryConnector` provides optional REST synchronization to external vector databases.

## Alternatives Considered
- **Stateless Agent Execution**: Rejected due to repeated mistakes and loss of architectural context across tickets.
- **Mandatory External Vector DB (e.g. Qdrant / PgVector)**: Rejected to preserve lightweight local development, instant test execution, and zero Docker prerequisites.
- **Pure In-Memory String Grep**: Rejected because semantic concept queries fail without vector similarity.

## Consequences

### Positive
- ✅ Zero-dependency, out-of-the-box semantic search across all four memory tiers.
- ✅ Automated task consolidation keeps context compact and high-signal.
- ✅ Automated memory pruning ensures bounded RAM footprint.

### Negative / Trade-offs
- ⚠️ 64-dimensional word-hash embeddings have lower semantic nuance than high-dimensional neural embeddings (e.g. `text-embedding-3-small`), though this is mitigated by composite token overlap scoring.
