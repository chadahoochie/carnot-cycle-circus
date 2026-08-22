# C# 13 & .NET 10 Coding Standards 💻⚡

## 1. Overview & Core Philosophy

All C# code in **Carnot Cycle Circus** must adhere to modern .NET 10 and C# 13 best practices. Code must be **immutable by default**, **thread-safe**, **zero-allocation on hot paths**, and **fully deterministic**.

---

## 2. Immutability & Type Design Rules

### Rule 1: Use `record` for Domain Entities & DTOs
All domain models, event messages, and handoff packets must be immutable C# records. Mutable property setters (`set;`) are strictly forbidden.

```csharp
// ✅ CORRECT: Immutable record with primary constructor
public record TicketItem(
    string Id,
    string Title,
    TicketStatus Status,
    AgentRole AssigneeRole,
    DateTimeOffset CreatedAt
);

// ❌ INCORRECT: Mutable class with public setters
public class TicketItem
{
    public string Id { get; set; }
    public string Title { get; set; }
}
```

### Rule 2: Use `readonly record struct` for Value Objects & IDs
Small value types and identifiers should be declared as `readonly record struct` to avoid unnecessary heap allocations:

```csharp
// ✅ CORRECT: Value object
public readonly record struct TicketId(string Value);
```

### Rule 3: Use Non-Destructive Mutation (`with`)
Never modify existing objects in-place. Use C# `with` expressions to clone and update state:

```csharp
// ✅ CORRECT: Non-destructive copy
public TicketItem WithStatus(TicketStatus newStatus) =>
    this with { Status = newStatus };
```

---

## 3. Performance & Zero-Allocation Dogma

### Rule 4: Zero Allocations on Hot Paths
High-frequency loops and telemetry pipelines must avoid allocating objects on the garbage collection (GC) heap.

- Use `ReadOnlyMemory<T>` and `ReadOnlySpan<T>` for slicing buffers, parsing strings, and passing byte arrays.
- Use `ValueTask` or `ValueTask<T>` for asynchronous methods on hot paths that frequently complete synchronously.
- Prefer `Span<char>` over `string.Substring()` or string concatenation in parsers.

```csharp
// ✅ CORRECT: Zero-allocation span parsing
public static bool HasValidPrefix(ReadOnlySpan<char> token)
{
    return token.StartsWith("HO-", StringComparison.Ordinal);
}
```

---

## 4. Asynchronous & Concurrency Standards

### Rule 5: Always Pass `CancellationToken`
Every asynchronous method signature must accept a `CancellationToken cancellationToken = default` parameter and propagate it to all downstream async operations.

```csharp
// ✅ CORRECT
public async Task<MemoryEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    // ...
}

// ❌ INCORRECT: Missing cancellation token
public async Task<MemoryEntry?> GetByIdAsync(string id)
```

### Rule 6: Never Block with `.Result` or `.Wait()`
Never block asynchronous code synchronously. Blocking leads to thread-pool starvation and deadlocks in ASP.NET Core and Blazor.

```csharp
// ✅ CORRECT
var result = await client.CompleteAsync(request, apiKey, cancellationToken);

// ❌ INCORRECT: Synchronous blocking
var result = client.CompleteAsync(request, apiKey).Result;
```

### Rule 7: Thread-Safe State Storage
All shared in-memory state stores must use thread-safe data structures:
- `ConcurrentDictionary<TKey, TValue>` with explicit string comparers (`StringComparer.OrdinalIgnoreCase`).
- `ConcurrentQueue<T>` or `ConcurrentBag<T>`.
- `System.Threading.Channels.Channel<T>` for producer-consumer pipelines.

---

## 5. Error Handling & Result Patterns

### Rule 8: Result Types over Business Exceptions
Do not throw exceptions for expected business logic validations. Use typed result objects (e.g. `ValidationResult`, `ToolResult`). Reserve exceptions strictly for exceptional, unrecoverable system failures.

```csharp
// ✅ CORRECT: Functional result type
public record ValidationResult(bool IsValid, IReadOnlyList<string> Violations)
{
    public static ValidationResult Success() => new(true, Array.Empty<string>());
    public static ValidationResult Failure(params string[] violations) => new(false, violations);
}
```

---

## 6. Compiler & Repository Configuration

- **`TreatWarningsAsErrors`**: Enabled in `Directory.Build.props`. All compiler warnings must be fixed.
- **Nullability**: `<Nullable>enable</Nullable>` is enforced across the entire solution. Null-forgiving operators (`!`) must be used only when proven non-null by surrounding invariants.
- **Central Package Management (CPM)**: All NuGet package versions are declared centrally in `Directory.Packages.props`. Never specify `Version="..."` in `.csproj` files.
