using System.Collections.Concurrent;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Teams;

public record TeamDefinition(
    string Id,
    string Name,
    string Description,
    string ArchetypeName,
    IReadOnlyList<AgentMember> Members,
    string DefaultFallbackModel,
    DateTimeOffset CreatedAt
)
{
    public EngineeringTeam ToEngineeringTeam() =>
        new(Id, Name, Description, Members, DefaultFallbackModel);
}

public static class TeamArchetypes
{
    public static TeamDefinition BalancedCircus => new(
        Id: "archetype-balanced",
        Name: "🎪 The Full 6-Ring Circus (Balanced)",
        Description: "The complete engineering squad: TPM invents fantasy deadlines, Architect builds cathedral abstractions, Dev drinks coffee, Security panics, Optimizer counts nanoseconds, and QA destroys everything.",
        ArchetypeName: "Balanced",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(AgentPersona.CreateDefault(r))).ToList(),
        DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition MoveFastBreakProduction => new(
        Id: "archetype-cowboy",
        Name: "🤠 Move Fast & Break Production (Cowboy Mode)",
        Description: "Who needs QA or Security when you have unyielding confidence? Maximum temperature, zero safety nets, thoughts-and-prayers architecture. Deploys straight to prod on Friday 4:59 PM.",
        ArchetypeName: "MoveFastBreakProduction",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r == AgentRole.SoftwareDeveloper ? "qwen/qwen-2.5-coder-32b-instruct" : "anthropic/claude-3.5-haiku",
                Temperature = 0.7
            },
            IsEnabled: r is AgentRole.SoftwareDeveloper or AgentRole.TechnicalProductManager
        )).ToList(),
        DefaultFallbackModel: "qwen/qwen-2.5-coder-32b-instruct",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition IvoryTowerCathedrals => new(
        Id: "archetype-cathedral",
        Name: "🏛️ Ivory Tower Cathedral Builders (Enterprise Edition)",
        Description: "500 layers of abstraction for a Hello World application. Every boolean flag requires a factory, an interface, and an Architectural Decision Record signed in triplicate.",
        ArchetypeName: "IvoryTowerCathedrals",
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

    public static TeamDefinition SecurityHardened => new(
        Id: "archetype-security",
        Name: "🛡️ Paranoid Zero-Trust Bunker",
        Description: "No code will ever be merged, therefore no vulnerabilities can ever exist. Pure mathematical security perfection. Powered by deep reasoning models that assume your query is an advanced persistent threat.",
        ArchetypeName: "SecurityHardened",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r is AgentRole.SecurityEngineer or AgentRole.PrincipalQAAnalyst ? "openai/o3-mini" : "deepseek/deepseek-r1",
                Temperature = 0.0
            }
        )).ToList(),
        DefaultFallbackModel: "openai/o3-mini",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition HighPerformance => new(
        Id: "archetype-performance",
        Name: "⚡ Zero-Allocation Zealots (Nano-Benchmarkers)",
        Description: "Garbage collection is outlawed by imperial decree. If you allocate a single byte on the heap, you are exiled. P99 latency target: -2 milliseconds (executes before the user clicks).",
        ArchetypeName: "HighPerformance",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r is AgentRole.OptimizationEngineer or AgentRole.SoftwareDeveloper ? "anthropic/claude-3.7-sonnet" : AgentRoleExtensions.ToDefaultModel(r),
                Temperature = 0.0
            }
        )).ToList(),
        DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition ChaosMonkeyRodeo => new(
        Id: "archetype-chaos",
        Name: "🧪 Chaos Monkey Rodeo (QA Dictatorship)",
        Description: "Where QA analysts hold absolute totalitarian power. Quinn will feed emojis, 10GB null strings, and negative infinity into every endpoint until the developer breaks down in tears.",
        ArchetypeName: "ChaosMonkeyRodeo",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r == AgentRole.PrincipalQAAnalyst ? "deepseek/deepseek-r1" : AgentRoleExtensions.ToDefaultModel(r),
                Temperature = r == AgentRole.PrincipalQAAnalyst ? 0.3 : 0.1
            }
        )).ToList(),
        DefaultFallbackModel: "deepseek/deepseek-r1",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static IReadOnlyList<TeamDefinition> AllArchetypes => [
        BalancedCircus,
        MoveFastBreakProduction,
        IvoryTowerCathedrals,
        SecurityHardened,
        HighPerformance,
        ChaosMonkeyRodeo
    ];
}

public interface ITeamDefinitionManager
{
    IReadOnlyList<TeamDefinition> GetAllTeams();
    TeamDefinition? GetTeam(string id);
    TeamDefinition SaveTeam(TeamDefinition team);
    bool DeleteTeam(string id);
    TeamDefinition LoadArchetype(string archetypeName);
    string ExportToJson(string teamId);
    TeamDefinition ImportFromJson(string json);
    EngineeringTeam GetCurrentTeam();
    void SetCurrentTeam(TeamDefinition team);

    event Action<EngineeringTeam>? OnCurrentTeamChanged;
}

public class TeamDefinitionManager : ITeamDefinitionManager
{
    private readonly ConcurrentDictionary<string, TeamDefinition> _teams = new(StringComparer.OrdinalIgnoreCase);
    private EngineeringTeam _currentTeam;

    public event Action<EngineeringTeam>? OnCurrentTeamChanged;

    public TeamDefinitionManager()
    {
        foreach (var archetype in TeamArchetypes.AllArchetypes)
        {
            _teams[archetype.Id] = archetype;
        }

        _currentTeam = TeamArchetypes.BalancedCircus.ToEngineeringTeam();
    }

    public IReadOnlyList<TeamDefinition> GetAllTeams() =>
        _teams.Values.OrderBy(t => t.Name).ToList();

    public TeamDefinition? GetTeam(string id) =>
        _teams.TryGetValue(id, out var team) ? team : null;

    public TeamDefinition SaveTeam(TeamDefinition team)
    {
        _teams[team.Id] = team;
        if (_currentTeam.Id == team.Id)
        {
            _currentTeam = team.ToEngineeringTeam();
            OnCurrentTeamChanged?.Invoke(_currentTeam);
        }
        return team;
    }

    public bool DeleteTeam(string id) => _teams.TryRemove(id, out _);

    public TeamDefinition LoadArchetype(string archetypeName)
    {
        var archetype = TeamArchetypes.AllArchetypes.FirstOrDefault(a => string.Equals(a.ArchetypeName, archetypeName, StringComparison.OrdinalIgnoreCase))
            ?? TeamArchetypes.BalancedCircus;

        var customTeam = archetype with
        {
            Id = $"team-{Guid.NewGuid().ToString("N")[..6]}",
            Name = $"{archetype.Name} (Active Instance)",
            CreatedAt = DateTimeOffset.UtcNow
        };

        SaveTeam(customTeam);
        SetCurrentTeam(customTeam);
        return customTeam;
    }

    public EngineeringTeam GetCurrentTeam() => _currentTeam;

    public void SetCurrentTeam(TeamDefinition team)
    {
        _currentTeam = team.ToEngineeringTeam();
        OnCurrentTeamChanged?.Invoke(_currentTeam);
    }

    public string ExportToJson(string teamId)
    {
        var team = GetTeam(teamId) ?? _teams.Values.First();
        return JsonSerializer.Serialize(team, new JsonSerializerOptions { WriteIndented = true });
    }

    public TeamDefinition ImportFromJson(string json)
    {
        var team = JsonSerializer.Deserialize<TeamDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize team definition JSON.");

        var imported = team with { Id = $"team-import-{Guid.NewGuid().ToString("N")[..6]}" };
        SaveTeam(imported);
        SetCurrentTeam(imported);
        return imported;
    }
}
