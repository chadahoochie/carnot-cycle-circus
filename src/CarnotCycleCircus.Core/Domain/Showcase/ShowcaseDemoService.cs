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
            Title: "🚀 Ludicrous Speed Swarm Sprint (They've Gone to Plaid!)",
            Tagline: "Watch all 6 roles decompose, architect, code, audit, optimize, and verify an end-to-end feature at Mach 10.",
            Description: "Barnum yells 'Ludicrous speed, GO!' as Devon, Archibald, Sari, Otto, and Quinn ship an immutable ValueTask pipeline with bounded channels and automated STRIDE verification.",
            HighlightPersona: "The Whole Troupe (Barnum & Devon)",
            SimulateRemediation: false
        ),
        new(
            Id: "friday-panic-meltdown",
            Title: "⚔️ 'Tis But a Scratch: The Friday 4:59 PM Meltdown & Self-Healing Loopback",
            Tagline: "QA and Security violently reject broken code and slice off limbs; Developer screams 'Just a flesh wound!' and remediates via DAG failure ports.",
            Description: "Simulates an unhandled null exception and leaked secrets. Quinn ('That's a lot of nuts!') and Sari ('It's a trap!') trip the circuit breaker and route the payload back to Devon ('Like a glove!') for remediation.",
            HighlightPersona: "Quinn (QA), Sari (Security) & Devon (Dev)",
            SimulateRemediation: true
        ),
        new(
            Id: "nanosecond-shootout",
            Title: "🕵️ Super Troopers 'Enhance' Optimization Shootout",
            Tagline: "Otto enhances memory allocations down to zero on the hot path ('So I got sub-nanosecond latency goin' for me, which is nice').",
            Description: "Demonstrates hot path profiling, converting LINQ aggregations to ReadOnlySpan<char> and MemoryPool slicing with zero heap overhead and zero Gen0 collections.",
            HighlightPersona: "Otto (Optimization Engineer)",
            SimulateRemediation: false
        ),
        new(
            Id: "holy-hand-grenade-security",
            Title: "💣 The Holy Hand Grenade STRIDE Security Audit",
            Tagline: "Sari audits the system with zero tolerance for prompt injection or open ports ('Nobody expects the Spanish Inquisition!').",
            Description: "First shalt thou take out the holy pin. Then shalt thou count to three, no more, no less. Performs cryptographic envelope inspection and token sanitization.",
            HighlightPersona: "Sari \"Tinfoil\" Sandbox",
            SimulateRemediation: false
        ),
        new(
            Id: "high-quality-h2o-refactor",
            Title: "💧 High Quality H2O: Devon Banishes Heap Allocations",
            Tagline: "Mama says the Garbage Collector is ornery 'cause it's got all them heap allocations and no buffer pooling.",
            Description: "Devon refactors bloated legacy POCOs into ultra-pure readonly record structs and bounded Channels while drinking cold brew at 800 WPM.",
            HighlightPersona: "Devon \"Coldbrew\" Crashdump",
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
