namespace CarnotCycleCircus.Core.Domain.Agents;

public enum AgentRole
{
    RequirementsResearcher,
    TechnicalProductManager,
    LeadArchitect,
    SoftwareDeveloper,
    SecurityEngineer,
    OptimizationEngineer,
    PrincipalQAAnalyst,
    IntegrationEngineer
}

public static class AgentRoleExtensions
{
    public static string ToDisplayName(this AgentRole role) => role switch
    {
        AgentRole.RequirementsResearcher => "Requirements Researcher",
        AgentRole.TechnicalProductManager => "Technical Product Manager",
        AgentRole.LeadArchitect => "Lead Architect",
        AgentRole.SoftwareDeveloper => "Software Developer",
        AgentRole.SecurityEngineer => "Security Engineer",
        AgentRole.OptimizationEngineer => "Optimization Engineer",
        AgentRole.PrincipalQAAnalyst => "Principal QA Analyst",
        AgentRole.IntegrationEngineer => "Integration Engineer",
        _ => role.ToString()
    };

    public static string ToEmoji(this AgentRole role) => role switch
    {
        AgentRole.RequirementsResearcher => "🔬",
        AgentRole.TechnicalProductManager => "🎯",
        AgentRole.LeadArchitect => "🏛️",
        AgentRole.SoftwareDeveloper => "💻",
        AgentRole.SecurityEngineer => "🛡️",
        AgentRole.OptimizationEngineer => "⚡",
        AgentRole.PrincipalQAAnalyst => "🧪",
        AgentRole.IntegrationEngineer => "📦",
        _ => "🤖"
    };

    public static string ToColorHex(this AgentRole role) => role switch
    {
        AgentRole.RequirementsResearcher => "#6366f1", // Indigo
        AgentRole.TechnicalProductManager => "#38bdf8", // Sky Blue
        AgentRole.LeadArchitect => "#a855f7",           // Purple
        AgentRole.SoftwareDeveloper => "#10b981",       // Emerald Green
        AgentRole.SecurityEngineer => "#ef4444",        // Red
        AgentRole.OptimizationEngineer => "#f59e0b",    // Amber
        AgentRole.PrincipalQAAnalyst => "#ec4899",      // Pink
        AgentRole.IntegrationEngineer => "#06b6d4",    // Cyan
        _ => "#94a3b8"
    };

    public static string ToDefaultModel(this AgentRole role) => string.Empty;
}
