using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Tickets;

namespace CarnotCycleCircus.Core.Domain.Showcase;

public record ShowcaseScenario(
    string Id,
    string Title,
    string Tagline,
    string Description,
    string HighlightPersona,
    bool SimulateRemediation
);

public interface IShowcaseDemoService
{
    IReadOnlyList<ShowcaseScenario> GetScenarios();
    ShowcaseScenario? GetScenario(string id);
    Task<bool> RunShowcaseAsync(string scenarioId, CancellationToken cancellationToken = default);
}

public class ShowcaseDemoService : IShowcaseDemoService
{
    private readonly IGraphWorkflowExecutor _workflowExecutor;
    private readonly IAgentEventStream _eventStream;

    public ShowcaseDemoService(
        IGraphWorkflowExecutor workflowExecutor,
        IAgentEventStream eventStream)
    {
        _workflowExecutor = workflowExecutor;
        _eventStream = eventStream;
    }

    private static readonly IReadOnlyList<ShowcaseScenario> Scenarios =
    [
        new(
            Id: "full-circus-sprint",
            Title: "🎪 60-Second Full Autonomous Swarm Sprint",
            Tagline: "Watch all 6 engineering roles decompose, architect, code, audit, optimize, and verify an end-to-end feature.",
            Description: "Refactor legacy payment controller to C# 13 zero-allocation ValueTask pipeline with bounded channels and automated STRIDE threat verification.",
            HighlightPersona: "The Whole Troupe",
            SimulateRemediation: false
        ),
        new(
            Id: "friday-panic-meltdown",
            Title: "🚨 The Friday 4:59 PM Panic & Self-Healing Loopback",
            Tagline: "Watch QA and Security violently reject broken code and force the developer to fix sins via reactive DAG failure ports.",
            Description: "Simulates an unhandled null exception and secret leakage. Quinn (QA) and Sari (Security) trigger the reactive circuit breaker and route the payload back to Devon (Dev) for remediation.",
            HighlightPersona: "Quinn (QA) & Sari (Security)",
            SimulateRemediation: true
        ),
        new(
            Id: "nanosecond-shootout",
            Title: "⚡ The Nanosecond Optimization Shootout",
            Tagline: "Otto eliminates 12 GC Gen0 allocations with extreme prejudice on the hot path.",
            Description: "Demonstrates hot path profiling, converting LINQ aggregations to ReadOnlySpan<char> and MemoryPool slicing with zero heap overhead.",
            HighlightPersona: "Otto (Optimization Engineer)",
            SimulateRemediation: false
        )
    ];

    public IReadOnlyList<ShowcaseScenario> GetScenarios() => Scenarios;

    public ShowcaseScenario? GetScenario(string id) =>
        Scenarios.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task<bool> RunShowcaseAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        var scenario = GetScenario(scenarioId) ?? Scenarios[0];

        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.TechnicalProductManager,
            senderName: "🍿 Showcase Arena",
            content: $"Starting Showcase: '{scenario.Title}'! Fasten seatbelts, zero API keys required.",
            type: MessageType.StateChange
        ));

        return await _workflowExecutor.ExecuteWorkflowAsync(
            scenario.Title,
            scenario.Description,
            triggerFailureSimulation: scenario.SimulateRemediation,
            cancellationToken: cancellationToken
        );
    }
}
