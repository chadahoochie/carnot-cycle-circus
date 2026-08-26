# ADR-0012: OpenRouter Dynamic Model Ingestion, Local Persistent Caching, Strength & Cost Categorization, and Favorites System 🎭⚡

## Status
**Accepted** (2026-08-25)

## Context
Carnot Cycle Circus coordinates 6 specialized engineering agent roles (Technical Product Manager, Lead Architect, Senior Developer, Security Engineer, Optimization Engineer, Principal QA Analyst) powered by OpenRouter inference models.

Prior to this architecture:
1. Agent inference models were hardcoded string constants in `AgentPersona.cs` and `TeamDefinition.razor`.
2. OpenRouter hosts over 300+ frontier and open-weight models with constantly changing capabilities, context windows, and token pricing.
3. Querying the OpenRouter API on every UI render introduces network latency, rate-limiting exposure, and breaks offline/sandbox development environments.
4. Users had no unified interface to browse available models, star go-to favorites, filter by specialized engineering strength areas (Code Generation, Deep Reasoning, Low-Latency Fallbacks, TPM Orchestration, Security Audit, Multimodal Vision), or compare real-time pricing tiers.

## Decision
We implement a comprehensive **OpenRouter Model Catalog & Management System** consisting of:

1. **Live Ingestion & Multi-Tier Caching (`IModelCatalogService` & `OpenRouterModelCatalogService`)**:
   - Ingests raw model definitions, context lengths, architectures, and pricing from `https://openrouter.ai/api/v1/models`.
   - Caches parsed specifications in memory and persists atomically to `data/openrouter-models-cache.json` via `IPersistentStorageService`.
   - Enforces a 24-hour cache TTL with on-demand manual refresh (`🔄 Sync OpenRouter Catalog`).
   - Provides a resilient offline/sandbox curated fallback catalog when network or API credentials are unavailable.

2. **Automated Multi-Dimensional Categorization**:
   - **Cost Tier (`ModelCostTier`)**:
     - `Free`: `$0.00 / 1M` prompt tokens (e.g. `:free` endpoints).
     - `Budget`: `≤ $1.00 / 1M` prompt tokens (e.g., Gemini 2.0 Flash, DeepSeek V3/R1, Qwen 2.5 Coder).
     - `Standard`: `$1.01 – $5.00 / 1M` prompt tokens (e.g., Claude 3.7 Sonnet, GPT-4o).
     - `Premium`: `> $5.00 / 1M` prompt tokens (e.g., OpenAI o1, frontier models).
   - **Strength Areas (`ModelStrengthArea`)**:
     - `CodeGeneration`: C# code synthesis, refactoring, Span/Memory zero-allocation optimizations.
     - `DeepReasoning`: Multi-step chain-of-thought verification, architecture boundaries, STRIDE threat modeling, QA edge cases.
     - `LowLatencyFallback`: High-throughput sub-second responses, fast failovers.
     - `GeneralOrchestration`: TPM project planning, tool invocation, structured decomposition.
     - `SecurityAudit`: Threat vector identification, compliance gatekeeping.
     - `MultimodalVision`: Modality image parsing and UI mockups.

3. **Favorites & Role Recommendations Management**:
   - 1-click ⭐ favorite toggle persisted across sessions.
   - Intelligent role-matching engine recommending optimal models for each of the 6 agent roles.
   - Dynamic `<optgroup>` model selection in the Agent Studio and Add Circus Agent modals.

4. **Dedicated Blazor Management Studio (`/models`)**:
   - Interactive search, filter pills by strength area and cost tier, provider filtering, context size badges, exact pricing calculations ($/1M prompt and completion tokens), and an interactive sandbox test console.

## Alternatives Considered
- **Direct API querying on every page load**: Rejected due to latency, network unreliability, and rate limiting.
- **Static hardcoded enum lists**: Rejected because the AI model ecosystem updates weekly and breaks new model adoption without recompiling code.
- **Storing raw unclassified JSON**: Rejected because raw OpenRouter payloads lack domain-specific categorization for autonomous agent roles.

## Consequences
### Positive
- ✅ Dynamic access to hundreds of OpenRouter models with zero code modifications needed when new models release.
- ✅ Offline and sandbox development fully supported with zero network dependencies.
- ✅ Curated favorites drastically reduce cognitive overload when configuring agent troupes.
- ✅ Transparent token cost tracking ($/1M tokens) enables cost-effective multi-agent swarms.
- ✅ Role-based recommendations guide users to the best models for each engineering discipline.

### Negative / Trade-offs
- ⚠️ Model classification heuristics rely on OpenRouter metadata, description keywords, and architecture tags which may require periodic tuning.
