# ADR-0008: Persistent Storage Engine & Autonomous Self-Improvement Docker Stack

## Status
**Accepted** (2026-08-22)

## Context
Multi-agent autonomous engineering platforms generate significant persistent state during multi-turn workflows:
1. **Hierarchical Memories**: Working, episodic, semantic, and procedural memory vector stores.
2. **Ticket Backlogs & Handoffs**: Work item states, acceptance criteria, deliverables, and inter-agent review packets.
3. **Architectural Documents & ADRs**: Formal architecture decisions and system documentation bundles.
4. **AI Knowledge Maps & Custom Skills**: Evolving domain concept graphs and role-assigned skill definitions.
5. **API Key Vault**: Configured inference credentials.

Running purely in-memory meant that container stops or restarts wiped all accumulated learnings and deliverables. Furthermore, to fulfill the requirement of continuous self-improvement, the platform requires an autonomous distillation engine that extracts reusable patterns from workflow completions and failure remediations, persisting them permanently into mounted storage volumes.

## Decision
We implement a file-backed **Persistent Storage Engine** (`IPersistentStorageService` / `FilePersistentStorageService`) and an **Autonomous Self-Improvement Engine** (`ISelfImprovementEngine` / `SelfImprovementEngine`) packaged as a production Docker stack:

1. **Volume Mount Architecture**:
   - `/app/data`: Root persistent volume (`carnot_data`) storing JSON databases (`memories.json`, `tickets.json`, `handoffs.json`, `knowledgemap.json`, `skills.json`, `teams.json`, `adrs.json`, `keys.json`).
   - `/app/data/artifacts`: Subdirectory volume (`carnot_artifacts`) storing generated markdown deliverables, ADR exports, and PRDs.
   - `/app/data/skills`: Subdirectory volume (`carnot_skills`) storing imported YAML/Markdown skill definitions.
   - Optional `carnot_redis` volume for external distributed caching.

2. **Atomic Write Guarantee**:
   All persistent state flushes utilize temporary file writes (`.tmp.{guid}`) followed by atomic filesystem replacements (`File.Move(..., overwrite: true)`), preventing file corruption during power outages or container terminations.

3. **Autonomous Self-Improvement Loop**:
   - `AutonomousSelfImprovementWorker`: Background service triggering periodic knowledge distillation (default: 300s).
   - `SelfImprovementEngine`: Automatically synthesizes defensive knowledge rules from failure handoffs (e.g. security rejections, edge case bugs), generates procedural execution recipes for agents, reinforces semantic domain rules, and prunes decayed working memory.
   - Post-workflow trigger: Every workflow execution automatically runs a post-run distillation pass.

4. **Container Health & Observability**:
   - Built-in `/health` and `/api/storage/health` endpoints verifying volume writeability, file counts, and byte utilization.
   - Web UI `SelfImprovementStudio` for inspecting volume manifests and manually triggering distillation cycles.

## Alternatives Considered
- **Stateless Ephemeral Containers**: Rejected because historical agent learnings, custom skills, and tickets are lost on restart.
- **Mandatory Heavy Relational DB (PostgreSQL / MSSQL)**: Rejected to preserve lightweight deployment, fast zero-dependency local testing, and minimal operational overhead.
- **External Cloud Blob Storage Only**: Rejected to ensure air-gapped local development and zero cloud lock-in.

## Consequences

### Positive
- ✅ State persistence across container restarts, image upgrades, and host reboots.
- ✅ Continuous autonomous learning: system gets smarter with every completed workflow and resolved failure.
- ✅ Zero-dependency local execution remains intact (runs with in-memory or local `./data` fallback in tests).
- ✅ Built-in health check, automated backups, and docker-compose orchestration.

### Negative / Trade-offs
- ⚠️ Concurrent write coordination is handled via internal process locks; distributed multi-node clustering requires external storage connector sync.
