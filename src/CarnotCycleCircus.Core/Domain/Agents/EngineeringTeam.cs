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
    public string EffectiveModel => OverrideModel ?? Persona.DefaultModel;
}

public record EngineeringTeam(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<AgentMember> Members,
    string DefaultFallbackModel = "anthropic/claude-3.7-sonnet",
    string? ActiveGlobalApiKeyId = null
)
{
    public static EngineeringTeam CreateDefault()
    {
        var members = Enum.GetValues<AgentRole>()
            .Select(role => new AgentMember(AgentPersona.CreateDefault(role)))
            .ToList();

        return new EngineeringTeam(
            Id: "team-default-circus",
            Name: "Carnot High-Efficiency Engineering Crew",
            Description: "Full-spectrum autonomous engineering team covering Product, Architecture, Dev, Security, Optimization, and QA.",
            Members: members
        );
    }

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
        this with { Members = Members.Select(m => m.Id == member.Id ? member : m).ToList() };
}
