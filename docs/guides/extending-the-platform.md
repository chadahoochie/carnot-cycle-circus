# Extending the Platform: Developer Recipes 🛠️🧩

This guide provides concrete, step-by-step recipes for extending **Carnot Cycle Circus** with new tools, team archetypes, memory connectors, quality gate policies, and knowledge nodes.

---

## Recipe 1: Adding a Custom Agent Tool

To add a new tool that agents can invoke during task execution:

1. Implement `IToolDefinition` in `src/CarnotCycleCircus.Core/Domain/Tools/`:

```csharp
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Tools;

public class SqliteSchemaInspectorTool : IToolDefinition
{
    public string Name => "sqlite_schema_inspector";
    public string Description => "Inspects SQLite database schema tables, columns, and foreign keys.";
    public IReadOnlyDictionary<string, string> ParameterSchema => new Dictionary<string, string>
    {
        ["connectionString"] = "SQLite connection string or file path"
    };

    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Arguments.TryGetValue("connectionString", out var connStr) || string.IsNullOrWhiteSpace(connStr))
        {
            return Task.FromResult(ToolResult.Fail("Missing 'connectionString' argument."));
        }

        // Inspection logic...
        var output = "Tables: Users (Id, Email), Orders (Id, UserId, Total)";
        return Task.FromResult(ToolResult.Ok(output, new Dictionary<string, string> { ["TablesCount"] = "2" }));
    }
}
```

2. Register the tool in `src/CarnotCycleCircus.Core/Extensions/ServiceCollectionExtensions.cs`:

```csharp
services.AddSingleton<IToolDefinition, SqliteSchemaInspectorTool>();
```

3. Add the tool name (`sqlite_schema_inspector`) to the `AllowedToolNames` list of the target persona in `AgentPersona.cs`.

---

## Recipe 2: Creating a New Team Archetype

To add a pre-configured team archetype with specific model mappings and persona temperatures:

1. Add the definition in `src/CarnotCycleCircus.Core/Domain/Teams/TeamDefinitionManager.cs`:

```csharp
public static class TeamArchetypes
{
    public static TeamDefinition EmbeddedFirmwareSquad => new(
        Id: "archetype-firmware",
        Name: "🔌 Embedded Real-Time Firmware Crew",
        Description: "Specialized team optimized for bare-metal C/C++, FreeRTOS, zero heap allocations, and hardware register manipulation.",
        ArchetypeName: "EmbeddedFirmware",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = "anthropic/claude-3.7-sonnet",
                Temperature = 0.05
            }
        )).ToList(),
        DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
        CreatedAt: DateTimeOffset.UtcNow
    );

    // Add to AllArchetypes list
    public static IReadOnlyList<TeamDefinition> AllArchetypes => [
        BalancedCircus,
        MoveFastBreakProduction,
        IvoryTowerCathedrals,
        SecurityHardened,
        HighPerformance,
        ChaosMonkeyRodeo,
        EmbeddedFirmwareSquad // <-- Added here
    ];
}
```

---

## Recipe 3: Implementing a Custom Quality Gate Policy

To configure customized ticket compliance checks:

1. Instantiate or modify an `EngineeringStandardsProfile`:

```csharp
var customProfile = new EngineeringStandardsProfile(
    Name: "FinTech Compliance Standard",
    MinimumCodeCoveragePercent: 95.0,
    RequireUnitTestsForFeatures: true,
    RequireRcaForBugs: true,
    RequireRegressionTestForBugs: true,
    RequireAdrForEpics: true,
    RequireStrideSecurityReview: true,
    RequireZeroAllocationAudit: true
);

// Apply to validator
standardsValidator.CurrentProfile = customProfile;
```

2. To add new validation rules, extend `StandardsValidator.ValidateTicketForCompletion`:

```csharp
if (ticket.Type == TicketType.Feature && CurrentProfile.RequireStrideSecurityReview)
{
    var hasSecurityReview = ticket.Deliverables.Any(d => d.Name.Contains("STRIDE", StringComparison.OrdinalIgnoreCase));
    if (!hasSecurityReview)
    {
        violations.Add("Feature ticket requires an attached STRIDE security evaluation before completion.");
    }
}
```

---

## Recipe 4: Adding AI Knowledge Map Nodes & Relationships

To teach the system new architectural patterns, security rules, or anti-patterns:

```csharp
var node = new KnowledgeNode(
    Id: "KN-007",
    Label: "Actor Concurrency Model",
    Category: "Pattern",
    Summary: "Encapsulate mutable state within single-threaded actor mailboxes to eliminate locks.",
    Attributes: new Dictionary<string, string> { ["Paradigm"] = "Actor Model", ["Framework"] = "Akka.NET" }
);

knowledgeMapService.AddOrUpdateNode(node);
knowledgeMapService.AddEdge("KN-007", "KN-002", "Extends");
```

Agent queries matching `"actor"` or `"concurrency"` will now automatically pull this node into prompt contexts.
