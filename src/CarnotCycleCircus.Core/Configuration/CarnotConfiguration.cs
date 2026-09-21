namespace CarnotCycleCircus.Core.Configuration;

/// <summary>
/// Options for the agent event stream (bounded Channel{T}-based message bus).
/// Maps to the "EventStream" section of appsettings.json.
/// </summary>
public record EventStreamOptions
{
    /// <summary>Maximum number of in-memory messages retained. Oldest are pruned. Default: 1000.</summary>
    public int MaxHistorySize { get; init; } = 1000;
}

/// <summary>
/// Options for the per-agent-role failure policy defaults.
/// Maps to the "FailurePolicy" section of appsettings.json.
/// </summary>
public record FailurePolicyOptions
{
    /// <summary>Default maximum retry count when a ticket fails. Default: 3.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Whether the circuit breaker is enabled system-wide. Default: true.</summary>
    public bool CircuitBreakerEnabled { get; init; } = true;

    /// <summary>Default fallback agent role name (string) when all retries exhausted. Default: "SoftwareDeveloper".</summary>
    public string FallbackRole { get; init; } = "SoftwareDeveloper";
}

/// <summary>
/// Options for agent role → model assignments.
/// Maps to the "AgentModels" section of appsettings.json.
/// </summary>
public record AgentModelOptions
{
    /// <summary>Model identifier for the TechnicalProductManager role. Default: "claude-3.7-sonnet".</summary>
    public string TechnicalProductManager { get; init; } = "claude-3.7-sonnet";

    /// <summary>Model identifier for the LeadArchitect role. Default: "claude-3.7-sonnet".</summary>
    public string LeadArchitect { get; init; } = "claude-3.7-sonnet";

    /// <summary>Model identifier for the SoftwareDeveloper role. Default: "gpt-4o".</summary>
    public string SoftwareDeveloper { get; init; } = "gpt-4o";

    /// <summary>Model identifier for the SecurityEngineer role. Default: "o3-mini".</summary>
    public string SecurityEngineer { get; init; } = "o3-mini";

    /// <summary>Model identifier for the OptimizationEngineer role. Default: "qwen-2.5-coder-32b".</summary>
    public string OptimizationEngineer { get; init; } = "qwen-2.5-coder-32b";

    /// <summary>Model identifier for the PrincipalQAAnalyst role. Default: "deepseek-r1".</summary>
    public string PrincipalQAAnalyst { get; init; } = "deepseek-r1";

    /// <summary>Model identifier for the IntegrationSpecialist role. Default: "claude-3.7-sonnet".</summary>
    public string IntegrationSpecialist { get; init; } = "claude-3.7-sonnet";
}

/// <summary>
/// Root configuration section that groups all Carnot Cycle Circus subsystems.
/// Maps to the "Carnot" section of appsettings.json.
/// </summary>
public record CarnotConfiguration
{
    /// <summary>Event stream (message bus) options.</summary>
    public EventStreamOptions EventStream { get; init; } = new();

    /// <summary>Failure policy defaults.</summary>
    public FailurePolicyOptions FailurePolicy { get; init; } = new();

    /// <summary>Agent role → model assignments.</summary>
    public AgentModelOptions AgentModels { get; init; } = new();

    /// <summary>Master key provider tier name. Maps to the provider key in the inference resolver.</summary>
    public string MasterKeyTier { get; init; } = "";
}