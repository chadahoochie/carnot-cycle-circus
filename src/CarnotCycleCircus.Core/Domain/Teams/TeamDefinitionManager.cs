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
        Name: "Full-Lifecycle Balanced Circus",
        Description: "Comprehensive 6-agent engineering squad covering requirements, architecture, zero-allocation C# dev, STRIDE security, performance benchmarks, and automated QA.",
        ArchetypeName: "Balanced",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(AgentPersona.CreateDefault(r))).ToList(),
        DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition SecurityHardened => new(
        Id: "archetype-security",
        Name: "Zero-Day Security Hardened Squad",
        Description: "Prioritizes rigorous static/dynamic analysis, STRIDE threat modeling, and defensive coding with reasoning models.",
        ArchetypeName: "SecurityHardened",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r is AgentRole.SecurityEngineer or AgentRole.PrincipalQAAnalyst ? "openai/o3-mini" : AgentRoleExtensions.ToDefaultModel(r)
            }
        )).ToList(),
        DefaultFallbackModel: "openai/o3-mini",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition HighPerformance => new(
        Id: "archetype-performance",
        Name: "Ultra-Low-Latency Performance Crew",
        Description: "Optimized for high-throughput messaging, Span/Memory zero allocations, and micro-benchmarking.",
        ArchetypeName: "HighPerformance",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r is AgentRole.OptimizationEngineer or AgentRole.SoftwareDeveloper ? "anthropic/claude-3.7-sonnet" : AgentRoleExtensions.ToDefaultModel(r)
            }
        )).ToList(),
        DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static IReadOnlyList<TeamDefinition> AllArchetypes => [
        BalancedCircus,
        SecurityHardened,
        HighPerformance
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
