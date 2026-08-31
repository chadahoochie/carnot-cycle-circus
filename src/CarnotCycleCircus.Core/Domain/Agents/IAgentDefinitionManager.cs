namespace CarnotCycleCircus.Core.Domain.Agents;

public interface IAgentDefinitionManager
{
    IReadOnlyList<AgentMember> GetAllAgents();
    AgentMember? GetAgent(string id);
    AgentMember? GetAgentForRole(AgentRole role);
    IReadOnlyList<AgentMember> GetAgentsByRole(AgentRole role);
    AgentMember SaveAgent(AgentMember agent);
    bool DeleteAgent(string id);
    AgentMember CreateAgent(
        AgentRole role,
        string name,
        string systemPrompt,
        string primaryModel = "",
        string fallbackModel = "",
        double temperature = 0.1,
        IReadOnlyList<string>? allowedTools = null,
        IReadOnlyList<string>? assignedSkillIds = null,
        string? customApiKeyId = null
    );
    AgentMember DuplicateAgent(string sourceAgentId, string newName);
    Task FlushAsync(CancellationToken cancellationToken = default);

    event Action<IReadOnlyList<AgentMember>>? OnAgentsChanged;
}
