# ADR-0001: Adopt Immutable Record Types for Domain & Handoff Payloads

## Status
**Accepted** (2026-08-19)

## Context
Multi-agent engineering systems coordinate complex state transitions across concurrent asynchronous tasks and background threads. When domain models (such as tickets, handoff packets, agent personas, and memory entries) are declared as mutable classes with public property setters, the system becomes vulnerable to:
1. **Race Conditions**: Concurrent modifications to ticket state across background workers.
2. **Audit Log Corruption**: Historical messages or event stream entries mutating retrospectively.
3. **Implicit Side Effects**: Methods modifying input objects unexpectedly rather than returning fresh state.

## Decision
We mandate that all domain entities, data transfer objects (DTOs), event messages, and handoff payloads in `CarnotCycleCircus.Core` be declared as immutable C# `record` types or `readonly record struct` value objects.

All state transitions must use C# non-destructive mutation (`with { ... }`) syntax. Mutable properties (`{ get; set; }`) are banned from the domain model.

## Alternatives Considered
- **Mutable POCO classes with synchronization locks**: Rejected due to high lock contention, deadlock hazards, and cognitive overhead.
- **F# domain library with C# interop**: Rejected to avoid cross-language build complexity and preserve unified C# 13 tooling.
- **Passing unvalidated JSON strings**: Rejected due to lack of compile-time type safety and serialization overhead.

## Consequences

### Positive
- ✅ Thread-safe execution across background channels and asynchronous workers without manual lock management.
- ✅ Value equality and concise non-destructive cloning via `with` expressions.
- ✅ Immutable audit trail for telemetry and session playback.

### Negative / Trade-offs
- ⚠️ Developers must write explicit copy methods (e.g. `ticket.WithStatus(...)`) rather than mutating properties directly.
- ⚠️ Slight allocation overhead during state transitions, mitigated by lightweight record struct designs where applicable.
