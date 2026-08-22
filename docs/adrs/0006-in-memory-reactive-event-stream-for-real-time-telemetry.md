# ADR-0006: In-Memory Reactive Event Stream for Real-Time Telemetry

## Status
**Accepted** (2026-08-21)

## Context
A rich autonomous agent platform requires real-time observability: human operators must be able to watch thought streams, tool invocations, handoff packets, and state changes as they occur. Traditional polling architectures create high CPU overhead and sluggish UI reactivity. Furthermore, external message brokers (RabbitMQ, Kafka, Redis) introduce operational complexity.

## Decision
We implement a lightweight, thread-safe in-memory pub/sub message bus: **`AgentEventStream`** (`CarnotCycleCircus.Core.Domain.Events`).
1. **Thread-Safe Publishing**: Uses `ConcurrentQueue<AgentMessage>` bounded at 1,000 messages to prevent memory leaks while preserving recent execution history.
2. **Synchronous C# Action Dispatch**: Subscribed Blazor UI components receive live events immediately and trigger non-blocking re-rendering on the dispatcher thread.
3. **Structured Event Types**: Emits typed messages: `Chat`, `Thought`, `ToolCall`, `ToolOutput`, `Handoff`, `StateChange`, `Alert`, `ArtifactCreated`.
4. **Session Replay**: Supports full event stream serialization and replay for post-incident audits and demonstration reviews.

## Alternatives Considered
- **SignalR Hubs over WebSockets**: Evaluated, but unnecessary for Blazor Interactive Server where in-process C# event dispatching provides lower latency and zero serialization overhead.
- **External Message Brokers (RabbitMQ / Redis Pub/Sub)**: Rejected for standalone local architecture to maintain zero external infrastructure dependencies.

## Consequences

### Positive
- ✅ Sub-millisecond event delivery to Blazor UI components with zero network serialization.
- ✅ Bounded ring-buffer storage prevents memory exhaustion during extended runs.
- ✅ Subscriber exceptions are isolated and cannot disrupt publishing pipelines.

### Negative / Trade-offs
- ⚠️ Event stream is in-memory and ephemeral unless explicitly exported to disk/session bundles.
