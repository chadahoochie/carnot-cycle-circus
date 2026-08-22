namespace CarnotCycleCircus.Core.Domain.Agents;

public record AgentMember(
    AgentPersona Persona,
    string? CustomApiKeyId = null,
    string? OverrideModel = null,
    bool IsEnabled = true
)
{
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
        Members.FirstOrDefault(m => m.Persona.Role == role);
}
