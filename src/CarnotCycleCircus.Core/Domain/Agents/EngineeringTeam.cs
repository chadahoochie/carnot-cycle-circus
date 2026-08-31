using CarnotCycleCircus.Core.Domain.Graph;

namespace CarnotCycleCircus.Core.Domain.Agents;

public record AgentMember(
    AgentPersona Persona,
    string? CustomApiKeyId = null,
    string? OverrideModel = null,
    bool IsEnabled = true,
    string? Id = null
)
{
    public string Id { get; init; } = !string.IsNullOrWhiteSpace(Id) ? Id : $"agent-{Guid.NewGuid():N}"[..18];
    public string EffectiveModel => !string.IsNullOrWhiteSpace(OverrideModel) ? OverrideModel : (Persona.DefaultModel ?? string.Empty);
    public bool HasModel => !string.IsNullOrWhiteSpace(EffectiveModel);
}

public record EngineeringTeam(
    string Id,
    string Name,
    string Description,
    WorkflowGraph Graph,
    IReadOnlyList<AgentMember> Members,
    string DefaultFallbackModel = "",
    string? ActiveGlobalApiKeyId = null
)
{
    public static EngineeringTeam CreateDefault()
    {
        var graph = WorkflowGraph.CreateDefaultEngineeringCircus();
        var defaultMembers = Enum.GetValues<AgentRole>().Select(r => new AgentMember(
            AgentPersona.CreateDefault(r) with
            {
                DefaultModel = r switch
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
                }
            },
            Id: $"agent-{r.ToString().ToLowerInvariant()}"
        )).ToList();

        return new EngineeringTeam(
            Id: "team-standard-circus",
            Name: "🎪 The Full 6-Ring Circus",
            Description: "The complete engineering squad: TPM, Lead Architect, Software Developer, Security Engineer, Optimization Engineer, Principal QA Analyst, Release Integrator, and Requirements Researcher.",
            Graph: graph,
            Members: defaultMembers,
            DefaultFallbackModel: string.Empty
        );
    }

    public AgentMember? GetMember(AgentRole role) =>
        Members.FirstOrDefault(m => m.Persona.Role == role && m.IsEnabled) ??
        Members.FirstOrDefault(m => m.Persona.Role == role);

    public IReadOnlyList<AgentMember> GetMembers(AgentRole role) =>
        Members.Where(m => m.Persona.Role == role).ToList();

    public AgentMember? GetMemberById(string memberId) =>
        Members.FirstOrDefault(m => m.Id == memberId);

    public AgentMember? GetMemberForNode(GraphNode node, IEnumerable<AgentMember>? definedAgents = null)
    {
        if (!string.IsNullOrEmpty(node.AgentId))
        {
            var byId = GetMemberById(node.AgentId);
            if (byId != null) return byId;

            if (definedAgents != null)
            {
                var defined = definedAgents.FirstOrDefault(a => a.Id == node.AgentId);
                if (defined != null) return defined;
            }
        }
        return GetMember(node.Role);
    }

    public EngineeringTeam AddMember(AgentMember member) =>
        this with { Members = [.. Members, member] };

    public EngineeringTeam RemoveMember(string memberId) =>
        this with { Members = Members.Where(m => m.Id != memberId && m.Persona.Name != memberId).ToList() };

    public EngineeringTeam UpdateMember(AgentMember member) =>
        this with { Members = Members.Select(m => (m.Id == member.Id || (m.Persona.Role == member.Persona.Role && m.Persona.Name == member.Persona.Name)) ? member : m).ToList() };

    public EngineeringTeam WithGraph(WorkflowGraph graph) =>
        this with { Graph = graph };
}
