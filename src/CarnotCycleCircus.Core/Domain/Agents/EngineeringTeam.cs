using CarnotCycleCircus.Core.Domain.Teams;

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
    IReadOnlyList<AgentMember> Members,
    string DefaultFallbackModel = "anthropic/claude-3.7-sonnet",
    string? ActiveGlobalApiKeyId = null,
    string ArchetypeName = "Balanced"
)
{
    public static EngineeringTeam CreateDefault() =>
        TeamArchetypes.BalancedCircus.ToEngineeringTeam();

    public AgentMember? GetMember(AgentRole role) =>
        Members.FirstOrDefault(m => m.Persona.Role == role && m.IsEnabled) ??
        Members.FirstOrDefault(m => m.Persona.Role == role);

    public IReadOnlyList<AgentMember> GetMembers(AgentRole role) =>
        Members.Where(m => m.Persona.Role == role).ToList();

    public AgentMember? GetMemberById(string memberId) =>
        Members.FirstOrDefault(m => m.Id == memberId);

    public EngineeringTeam AddMember(AgentMember member) =>
        this with { Members = [.. Members, member] };

    public EngineeringTeam RemoveMember(string memberId) =>
        this with { Members = Members.Where(m => m.Id != memberId && m.Persona.Name != memberId).ToList() };

    public EngineeringTeam UpdateMember(AgentMember member) =>
        this with { Members = Members.Select(m => (m.Id == member.Id || (m.Persona.Role == member.Persona.Role && m.Persona.Name == member.Persona.Name)) ? member : m).ToList() };
}
