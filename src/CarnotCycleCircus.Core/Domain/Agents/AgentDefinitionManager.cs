using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Agents;

public class AgentDefinitionManager : IAgentDefinitionManager
{
    private readonly ConcurrentDictionary<string, AgentMember> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPersistentStorageService? _storageService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const string AgentsFileName = "agents.json";

    public event Action<IReadOnlyList<AgentMember>>? OnAgentsChanged;

    public AgentDefinitionManager(IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;

        var loaded = LoadFromStorage();
        if (!loaded)
        {
            SeedDefaultAgents();
            SaveToStorage();
        }
    }

    private void SeedDefaultAgents()
    {
        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var persona = AgentPersona.CreateDefault(role);
            var member = new AgentMember(
                Persona: persona,
                Id: $"agent-{role.ToString().ToLowerInvariant()}"
            );
            _agents[member.Id] = member;
        }
    }

    private bool LoadFromStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var saved = _storageService.LoadJsonAsync<List<AgentMember>>(AgentsFileName).GetAwaiter().GetResult();
            if (saved != null && saved.Count > 0)
            {
                foreach (var a in saved)
                {
                    _agents[a.Id] = a;
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
            await _storageService.SaveJsonAsync(AgentsFileName, _agents.Values.ToList(), cancellationToken);
        }
        catch
        {
            // Ignore transient write errors
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void SaveToStorage()
    {
        if (_storageService == null) return;
        _ = FlushAsync();
    }

    public IReadOnlyList<AgentMember> GetAllAgents() =>
        _agents.Values.OrderBy(a => a.Persona.Role).ThenBy(a => a.Persona.Name).ToList();

    public AgentMember? GetAgent(string id) =>
        _agents.TryGetValue(id, out var agent) ? agent : null;

    public AgentMember? GetAgentForRole(AgentRole role) =>
        _agents.Values.FirstOrDefault(a => a.Persona.Role == role);

    public IReadOnlyList<AgentMember> GetAgentsByRole(AgentRole role) =>
        _agents.Values.Where(a => a.Persona.Role == role).ToList();

    public AgentMember SaveAgent(AgentMember agent)
    {
        _agents[agent.Id] = agent;
        SaveToStorage();
        OnAgentsChanged?.Invoke(GetAllAgents());
        return agent;
    }

    public bool DeleteAgent(string id)
    {
        var removed = _agents.TryRemove(id, out _);
        if (removed)
        {
            SaveToStorage();
            OnAgentsChanged?.Invoke(GetAllAgents());
        }
        return removed;
    }

    public AgentMember CreateAgent(
        AgentRole role,
        string name,
        string systemPrompt,
        string primaryModel = "",
        string fallbackModel = "",
        double temperature = 0.1,
        IReadOnlyList<string>? allowedTools = null,
        IReadOnlyList<string>? assignedSkillIds = null,
        string? customApiKeyId = null)
    {
        var defaultPersona = AgentPersona.CreateDefault(role);
        var tools = allowedTools != null && allowedTools.Count > 0
            ? allowedTools
            : defaultPersona.AllowedToolNames;

        var persona = new AgentPersona(
            Role: role,
            Name: string.IsNullOrWhiteSpace(name) ? defaultPersona.Name : name.Trim(),
            SystemPrompt: string.IsNullOrWhiteSpace(systemPrompt) ? defaultPersona.SystemPrompt : systemPrompt.Trim(),
            DefaultModel: primaryModel,
            FallbackModel: fallbackModel,
            Temperature: temperature,
            AllowedToolNames: tools,
            AssignedSkillIds: assignedSkillIds ?? Array.Empty<string>()
        );

        var member = new AgentMember(
            Persona: persona,
            CustomApiKeyId: customApiKeyId,
            OverrideModel: string.IsNullOrWhiteSpace(primaryModel) ? null : primaryModel,
            IsEnabled: true,
            Id: $"agent-{Guid.NewGuid():N}"[..18]
        );

        SaveAgent(member);
        return member;
    }

    public AgentMember DuplicateAgent(string sourceAgentId, string newName)
    {
        var source = GetAgent(sourceAgentId) ?? GetAllAgents().FirstOrDefault() ?? new AgentMember(AgentPersona.CreateDefault(AgentRole.SoftwareDeveloper));
        var duplicated = source with
        {
            Id = $"agent-{Guid.NewGuid():N}"[..18],
            Persona = source.Persona with
            {
                Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Persona.Name} (Copy)" : newName.Trim()
            }
        };

        SaveAgent(duplicated);
        return duplicated;
    }
}
