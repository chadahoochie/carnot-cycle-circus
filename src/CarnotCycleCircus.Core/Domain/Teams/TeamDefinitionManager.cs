using System.Collections.Concurrent;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Teams;

public record TeamDefinition(
    string Id,
    string Name,
    string Description,
    WorkflowGraph Graph,
    IReadOnlyList<AgentMember> Members,
    string DefaultFallbackModel,
    DateTimeOffset CreatedAt,
    string? ActiveGlobalApiKeyId = null
)
{
    public EngineeringTeam ToEngineeringTeam() =>
        new(Id, Name, Description, Graph ?? WorkflowGraph.CreateDefaultEngineeringCircus(), Members ?? [], DefaultFallbackModel ?? string.Empty, ActiveGlobalApiKeyId);

    public TeamDefinition AddMember(AgentMember member) =>
        this with { Members = [.. Members, member] };

    public TeamDefinition RemoveMember(string memberId) =>
        this with { Members = Members.Where(m => m.Id != memberId && m.Persona.Name != memberId).ToList() };

    public TeamDefinition UpdateMember(AgentMember member) =>
        this with { Members = Members.Select(m => (m.Id == member.Id || (m.Persona.Role == member.Persona.Role && m.Persona.Name == member.Persona.Name)) ? member : m).ToList() };

    public TeamDefinition WithGraph(WorkflowGraph graph) =>
        this with { Graph = graph };

    public static string GetDefaultModelForRole(AgentRole role) => role switch
    {
        AgentRole.RequirementsResearcher => "openai/gpt-4o-mini",
        AgentRole.TechnicalProductManager => "openai/gpt-4o",
        AgentRole.LeadArchitect => "openai/gpt-4o",
        AgentRole.SoftwareDeveloper => "qwen/qwen-2.5-coder-32b-instruct",
        AgentRole.SecurityEngineer => "openai/o3-mini",
        AgentRole.OptimizationEngineer => "deepseek/deepseek-r1",
        AgentRole.PrincipalQAAnalyst => "deepseek/deepseek-r1",
        AgentRole.IntegrationEngineer => "openai/gpt-4o",
        _ => "openai/gpt-4o"
    };

    public static TeamDefinition CreateDefaultCircusTeam() => new(
        Id: "team-balanced",
        Name: "🎪 The Balanced 6-Ring Circus",
        Description: "The complete engineering troupe: TPM, Lead Architect, Software Developer, Security Engineer, Optimization Engineer, Principal QA Analyst, Release Integrator, and Requirements Researcher.",
        Graph: WorkflowGraph.CreateDefaultEngineeringCircus(),
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with { DefaultModel = GetDefaultModelForRole(r) },
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList(),
        DefaultFallbackModel: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition CreateRapidPrototypeTeam() => new(
        Id: "team-move-fast",
        Name: "⚡ Move Fast & Break Production",
        Description: "Streamlined fast-feedback squad: TPM -> Senior Developer -> Principal QA.",
        Graph: WorkflowGraph.CreateRapidPrototype(),
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with { DefaultModel = GetDefaultModelForRole(r) },
            IsEnabled: r is AgentRole.TechnicalProductManager or AgentRole.SoftwareDeveloper or AgentRole.PrincipalQAAnalyst,
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList(),
        DefaultFallbackModel: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition CreateIvoryTowerTeam() => new(
        Id: "team-ivory-tower",
        Name: "🏛️ Ivory Tower Architecture Guild",
        Description: "Heavy architecture and ADR focus: TPM -> Lead Architect -> Developer -> Integration.",
        Graph: WorkflowGraph.CreateDefaultEngineeringCircus(),
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with { DefaultModel = GetDefaultModelForRole(r) },
            IsEnabled: r is AgentRole.TechnicalProductManager or AgentRole.LeadArchitect or AgentRole.SoftwareDeveloper or AgentRole.IntegrationEngineer,
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList(),
        DefaultFallbackModel: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition CreateZeroTrustTeam() => new(
        Id: "team-security-hardened",
        Name: "🛡️ Zero-Trust Security Bunker",
        Description: "High-assurance security-gated squad with dedicated Security Engineer threat modeling prior to QA.",
        Graph: WorkflowGraph.CreateZeroTrustSecurityCircus(),
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with { DefaultModel = GetDefaultModelForRole(r) },
            IsEnabled: r is AgentRole.TechnicalProductManager or AgentRole.LeadArchitect or AgentRole.SoftwareDeveloper or AgentRole.SecurityEngineer or AgentRole.PrincipalQAAnalyst,
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList(),
        DefaultFallbackModel: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition CreateHighPerformanceTeam() => new(
        Id: "team-high-performance",
        Name: "🏎️ Zero-Allocation Zealots",
        Description: "Performance-focused squad featuring Optimization Engineer allocation profiling and nano-benchmarking.",
        Graph: WorkflowGraph.CreateHighPerformanceCircus(),
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with { DefaultModel = GetDefaultModelForRole(r) },
            IsEnabled: r is AgentRole.TechnicalProductManager or AgentRole.SoftwareDeveloper or AgentRole.OptimizationEngineer or AgentRole.PrincipalQAAnalyst,
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList(),
        DefaultFallbackModel: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static TeamDefinition CreateChaosMonkeyTeam() => new(
        Id: "team-chaos-monkey",
        Name: "🐒 Chaos Monkey Rodeo",
        Description: "Adversarial stress-testing and fault injection troupe targeting edge cases.",
        Graph: WorkflowGraph.CreateHighPerformanceCircus(),
        Members: Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with { DefaultModel = GetDefaultModelForRole(r) },
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList(),
        DefaultFallbackModel: string.Empty,
        CreatedAt: DateTimeOffset.UtcNow
    );

    public static IReadOnlyList<TeamDefinition> DefaultPresets => [
        CreateDefaultCircusTeam(),
        CreateRapidPrototypeTeam(),
        CreateIvoryTowerTeam(),
        CreateZeroTrustTeam(),
        CreateHighPerformanceTeam(),
        CreateChaosMonkeyTeam()
    ];
}

public interface ITeamDefinitionManager
{
    IReadOnlyList<TeamDefinition> GetAllTeams();
    TeamDefinition? GetTeam(string id);
    TeamDefinition SaveTeam(TeamDefinition team);
    bool DeleteTeam(string id);
    TeamDefinition CreateTeam(string name, string description = "", string? baseTeamId = null);
    TeamDefinition DuplicateTeam(string sourceTeamId, string newName);
    bool SwitchToTeam(string teamId);
    TeamDefinition? GetCurrentTeamDefinition();
    EngineeringTeam GetCurrentTeam();
    void SetCurrentTeam(TeamDefinition team);
    void AddMemberToCurrentTeam(AgentMember member);
    bool RemoveMemberFromCurrentTeam(string memberId);
    void UpdateMemberInCurrentTeam(AgentMember member);
    void UpdateCurrentTeamGraph(WorkflowGraph graph);
    string ExportToJson(string teamId);
    TeamDefinition ImportFromJson(string json);
    Task FlushAsync(CancellationToken cancellationToken = default);

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
            foreach (var preset in TeamDefinition.DefaultPresets)
            {
                _teams[preset.Id] = preset;
            }
            var defaultTeam = _teams[TeamDefinition.CreateDefaultCircusTeam().Id];
            _currentTeam = defaultTeam.ToEngineeringTeam();
            SaveToStorage();
        }
        else
        {
            _currentTeam ??= _teams.Values.FirstOrDefault()?.ToEngineeringTeam() ?? TeamDefinition.CreateDefaultCircusTeam().ToEngineeringTeam();
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
                foreach (var t in saved)
                {
                    var sanitized = t with
                    {
                        Graph = t.Graph ?? WorkflowGraph.CreateDefaultEngineeringCircus(),
                        Members = t.Members ?? []
                    };
                    _teams[sanitized.Id] = sanitized;
                }
                if (!string.IsNullOrEmpty(activeId) && _teams.TryGetValue(activeId, out var activeTeam))
                {
                    _currentTeam = activeTeam.ToEngineeringTeam();
                }
                else
                {
                    _currentTeam = _teams.Values.First().ToEngineeringTeam();
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
        if (string.IsNullOrWhiteSpace(id)) return false;

        var removed = _teams.TryRemove(id, out _);
        if (removed)
        {
            if (string.Equals(_currentTeam.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                var nextTeam = _teams.Values.FirstOrDefault() ?? TeamDefinition.CreateDefaultCircusTeam();
                _teams.TryAdd(nextTeam.Id, nextTeam);
                SetCurrentTeam(nextTeam);
            }
            else
            {
                OnCurrentTeamChanged?.Invoke(_currentTeam);
            }
            SaveToStorage();
        }
        return removed;
    }

    public bool SwitchToTeam(string teamId)
    {
        if (_teams.TryGetValue(teamId, out var team))
        {
            _currentTeam = team.ToEngineeringTeam();
            OnCurrentTeamChanged?.Invoke(_currentTeam);
            SaveToStorage();
            return true;
        }
        return false;
    }

    public TeamDefinition CreateTeam(string name, string description = "", string? baseTeamId = null)
    {
        var baseTeam = (!string.IsNullOrEmpty(baseTeamId) ? GetTeam(baseTeamId) : null)
            ?? GetTeam(_currentTeam.Id)
            ?? TeamDefinition.CreateDefaultCircusTeam();

        var newTeam = baseTeam with
        {
            Id = $"team-{Guid.NewGuid().ToString("N")[..6]}",
            Name = string.IsNullOrWhiteSpace(name) ? $"{baseTeam.Name} (Custom)" : name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? baseTeam.Description : description.Trim(),
            Graph = baseTeam.Graph with { Id = $"graph-{Guid.NewGuid().ToString("N")[..6]}", Name = $"{name} Workflow DAG" },
            CreatedAt = DateTimeOffset.UtcNow
        };

        SaveTeam(newTeam);
        SetCurrentTeam(newTeam);
        return newTeam;
    }

    public TeamDefinition DuplicateTeam(string sourceTeamId, string newName)
    {
        var source = GetTeam(sourceTeamId) ?? GetTeam(_currentTeam.Id) ?? TeamDefinition.CreateDefaultCircusTeam();
        var duplicated = source with
        {
            Id = $"team-{Guid.NewGuid().ToString("N")[..6]}",
            Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} (Copy)" : newName.Trim(),
            Members = source.Members.Select(m => m with { Id = $"agent-{Guid.NewGuid():N}"[..18] }).ToList(),
            Graph = source.Graph with { Id = $"graph-{Guid.NewGuid().ToString("N")[..6]}", Name = $"{newName} Workflow DAG" },
            CreatedAt = DateTimeOffset.UtcNow
        };

        SaveTeam(duplicated);
        SetCurrentTeam(duplicated);
        return duplicated;
    }

    public TeamDefinition? GetCurrentTeamDefinition() =>
        GetTeam(_currentTeam.Id);

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
            Graph: _currentTeam.Graph,
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
            Graph: _currentTeam.Graph,
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
            Graph: _currentTeam.Graph,
            Members: _currentTeam.Members,
            DefaultFallbackModel: _currentTeam.DefaultFallbackModel,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var updated = current.UpdateMember(member);
        SaveTeam(updated);
        SetCurrentTeam(updated);
    }

    public void UpdateCurrentTeamGraph(WorkflowGraph graph)
    {
        var current = GetTeam(_currentTeam.Id);
        if (current != null)
        {
            var updated = current.WithGraph(graph);
            SaveTeam(updated);
            SetCurrentTeam(updated);
        }
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

        var imported = team with
        {
            Id = $"team-import-{Guid.NewGuid().ToString("N")[..6]}",
            Graph = team.Graph ?? WorkflowGraph.CreateDefaultEngineeringCircus()
        };
        SaveTeam(imported);
        SetCurrentTeam(imported);
        return imported;
    }
}
