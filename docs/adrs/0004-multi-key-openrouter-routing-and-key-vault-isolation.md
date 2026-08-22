# ADR-0004: Multi-Key OpenRouter Inference Routing & Key Vault Isolation

## Status
**Accepted** (2026-08-21)

## Context
Autonomous engineering teams comprise roles with disparate cognitive and performance requirements. Using a single LLM model or global API credential causes:
1. **Suboptimal Model Selection**: Overpaying with expensive reasoning models for simple boilerplate tasks, or under-powering complex architectural decisions with small code models.
2. **Credential Risk**: Hardcoding API keys or exposing raw keys in shared configuration and server logs.
3. **Lack of Key Flexibility**: Inability to rotate, test, or switch API keys during a live workflow without restarting the service.

## Decision
We implement a **Multi-Model OpenRouter Inference Router** paired with a client-side **API Key Vault** (`CarnotCycleCircus.Core.Domain.Inference`):
1. **Per-Role Model Configuration**: Each role defines a primary model (e.g. `claude-3.7-sonnet` for Lead Architect, `o3-mini` for Security, `qwen-2.5-coder-32b` for Dev, `deepseek-r1` for QA) and a fallback model with calibrated temperature settings.
2. **Client-Side Key Vault (`ApiKeyVaultService`)**: Stores named API keys in memory, masks credentials in UI/logs, and provides live mid-flight swapping.
3. **Resolution Hierarchy**: Credentials resolve dynamically: `Role Key` $\to$ `Team Global Key` $\to$ `Active Vault Key` $\to$ `Default Sandbox Key`.
4. **Deterministic Simulation Fallback (`SimulatedScenarioEngine`)**: When using mock/sandbox keys, the engine automatically generates complete, valid engineering deliverables offline.

## Alternatives Considered
- **Direct Single-Provider SDKs (e.g. OpenAI SDK only)**: Rejected because single providers do not offer the multi-model diversity required across specialized roles.
- **Environment Variable Keys Only**: Rejected because it prevents mid-flight interactive key swapping and multi-tenant isolation.

## Consequences

### Positive
- ✅ Cost and performance optimization by assigning the ideal model to each engineering role.
- ✅ Safe key handling with masked UI representations and zero hardcoded secrets.
- ✅ Deterministic offline testing without requiring live API keys.

### Negative / Trade-offs
- ⚠️ OpenRouter API introduces an external HTTP dependency for live completions.
