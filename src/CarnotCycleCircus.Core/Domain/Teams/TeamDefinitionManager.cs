using System.Collections.Concurrent;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Storage;

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
        new(Id, Name, Description, Members, DefaultFallbackModel, ArchetypeName: ArchetypeName);

    public TeamDefinition AddMember(AgentMember member) =>
        this with { Members = [.. Members, member] };

    public TeamDefinition RemoveMember(string memberId) =>
        this with { Members = Members.Where(m => m.Id != memberId && m.Persona.Name != memberId).ToList() };

    public TeamDefinition UpdateMember(AgentMember member) =>
        this with { Members = Members.Select(m => (m.Id == member.Id || (m.Persona.Role == member.Persona.Role && m.Persona.Name == member.Persona.Name)) ? member : m).ToList() };
}

public static class TeamArchetypes
{
    public static TeamDefinition BalancedCircus => new(
        Id: "archetype-balanced",
        Name: "🎪 The Full 6-Ring Circus (Balanced)",
        Description: "The complete engineering squad: TPM invents fantasy deadlines, Architect builds cathedral abstractions, Dev drinks coffee, Security panics, Optimizer counts nanoseconds, and QA destroys everything.",
        ArchetypeName: "Balanced",
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r switch
                {
                    AgentRole.RequirementsResearcher => "anthropic/claude-3.7-sonnet",
                    AgentRole.TechnicalProductManager => "openai/gpt-4o",
                    AgentRole.LeadArchitect => "anthropic/claude-3.7-sonnet",
                    AgentRole.SoftwareDeveloper => "qwen/qwen-2.5-coder-32b-instruct",
                    AgentRole.SecurityEngineer => "openai/o3-mini",
                    AgentRole.OptimizationEngineer => "anthropic/claude-3.7-sonnet",
                    AgentRole.PrincipalQAAnalyst => "deepseek/deepseek-r1",
                    AgentRole.IntegrationEngineer => "anthropic/claude-3.7-sonnet",
                    _ => "anthropic/claude-3.7-sonnet"
                }
            }
        )).ToList(),
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
                DefaultModel = r is AgentRole.OptimizationEngineer or AgentRole.SoftwareDeveloper ? "anthropic/claude-3.7-sonnet" : (r == AgentRole.SecurityEngineer ? "openai/o3-mini" : "anthropic/claude-3.7-sonnet"),
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
                DefaultModel = r == AgentRole.PrincipalQAAnalyst ? "deepseek/deepseek-r1" : (r == AgentRole.SoftwareDeveloper ? "qwen/qwen-2.5-coder-32b-instruct" : "anthropic/claude-3.7-sonnet"),
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
    void AddMemberToCurrentTeam(AgentMember member);
    bool RemoveMemberFromCurrentTeam(string memberId);
    void UpdateMemberInCurrentTeam(AgentMember member);
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    event Action<EngineeringTeam>? OnCurrentTeamChanged;
}

public class TeamDefinitionManager : ITeamDefinitionManager
{
    private readonly ConcurrentDictionary<string, TeamDefinition> _teams = new(StringComparer.OrdinalIgnoreCase);
    private EngineeringTeam _currentTeam;
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string TeamsFileName = "teams.json";
    private const string ActiveTeamFileName = "active-team-id.json";

    public event Action<EngineeringTeam>? OnCurrentTeamChanged;

    public TeamDefinitionManager(IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;

        var loaded = LoadFromStorage();
        if (!loaded)
        {
            foreach (var archetype in TeamArchetypes.AllArchetypes)
            {
                _teams[archetype.Id] = archetype;
            }
            _currentTeam = _teams[TeamArchetypes.BalancedCircus.Id].ToEngineeringTeam();
            SaveToStorage();
        }
        else
        {
            _currentTeam ??= _teams.Values.FirstOrDefault()?.ToEngineeringTeam() ?? TeamArchetypes.BalancedCircus.ToEngineeringTeam();
        }
    }

    private bool LoadFromStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var saved = _storageService.LoadJsonAsync<List<TeamDefinition>>(TeamsFileName).GetAwaiter().GetResult();
            var activeId = _storageService.LoadJsonAsync<string>(ActiveTeamFileName).GetAwaiter().GetResult();

            if (saved != null && saved.Count > 0)
            {
                foreach (var t in saved) _teams[t.Id] = t;
                if (!string.IsNullOrEmpty(activeId) && _teams.TryGetValue(activeId, out var activeTeam))
                {
                    _currentTeam = activeTeam.ToEngineeringTeam();
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_storageService == null) return;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await _storageService.SaveJsonAsync(TeamsFileName, _teams.Values.ToList(), cancellationToken);
            await _storageService.SaveJsonAsync(ActiveTeamFileName, _currentTeam.Id, cancellationToken);
        }
        catch
        {
            // Ignore transient write error
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void SaveToStorage()
    {
        if (_storageService == null) return;
        _ = Task.Run(async () =>
        {
            await FlushAsync();
        });
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
        SaveToStorage();
        return team;
    }

    public bool DeleteTeam(string id)
    {
        var removed = _teams.TryRemove(id, out _);
        if (removed) SaveToStorage();
        return removed;
    }

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
        SaveToStorage();
    }

    public void AddMemberToCurrentTeam(AgentMember member)
    {
        var current = GetTeam(_currentTeam.Id) ?? new TeamDefinition(
            Id: _currentTeam.Id,
            Name: _currentTeam.Name,
            Description: _currentTeam.Description,
            ArchetypeName: _currentTeam.ArchetypeName,
            Members: _currentTeam.Members,
            DefaultFallbackModel: _currentTeam.DefaultFallbackModel,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var updated = current.AddMember(member);
        SaveTeam(updated);
        SetCurrentTeam(updated);
    }

    public bool RemoveMemberFromCurrentTeam(string memberId)
    {
        var current = GetTeam(_currentTeam.Id) ?? new TeamDefinition(
            Id: _currentTeam.Id,
            Name: _currentTeam.Name,
            Description: _currentTeam.Description,
            ArchetypeName: _currentTeam.ArchetypeName,
            Members: _currentTeam.Members,
            DefaultFallbackModel: _currentTeam.DefaultFallbackModel,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var updated = current.RemoveMember(memberId);
        if (updated.Members.Count == current.Members.Count) return false;

        SaveTeam(updated);
        SetCurrentTeam(updated);
        return true;
    }

    public void UpdateMemberInCurrentTeam(AgentMember member)
    {
        var current = GetTeam(_currentTeam.Id) ?? new TeamDefinition(
            Id: _currentTeam.Id,
            Name: _currentTeam.Name,
            Description: _currentTeam.Description,
            ArchetypeName: _currentTeam.ArchetypeName,
            Members: _currentTeam.Members,
            DefaultFallbackModel: _currentTeam.DefaultFallbackModel,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var updated = current.UpdateMember(member);
        SaveTeam(updated);
        SetCurrentTeam(updated);
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
