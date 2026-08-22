namespace CarnotCycleCircus.Core.Domain.Agents;

public enum AgentRole
{
    TechnicalProductManager,
    LeadArchitect,
    SoftwareDeveloper,
    SecurityEngineer,
    OptimizationEngineer,
    PrincipalQAAnalyst
}

public static class AgentRoleExtensions
{
    public static string ToDisplayName(this AgentRole role) => role switch
    {
        AgentRole.TechnicalProductManager => "Technical Product Manager",
        AgentRole.LeadArchitect => "Lead Architect",
        AgentRole.SoftwareDeveloper => "Software Developer",
        AgentRole.SecurityEngineer => "Security Engineer",
        AgentRole.OptimizationEngineer => "Optimization Engineer",
        AgentRole.PrincipalQAAnalyst => "Principal QA Analyst",
        _ => role.ToString()
    };

    public static string ToEmoji(this AgentRole role) => role switch
    {
        AgentRole.TechnicalProductManager => "🎯",
        AgentRole.LeadArchitect => "🏛️",
        AgentRole.SoftwareDeveloper => "💻",
        AgentRole.SecurityEngineer => "🛡️",
        AgentRole.OptimizationEngineer => "⚡",
        AgentRole.PrincipalQAAnalyst => "🧪",
        _ => "🤖"
    };

    public static string ToColorHex(this AgentRole role) => role switch
    {
        AgentRole.TechnicalProductManager => "#38bdf8", // Sky Blue
        AgentRole.LeadArchitect => "#a855f7",           // Purple
        AgentRole.SoftwareDeveloper => "#10b981",       // Emerald Green
        AgentRole.SecurityEngineer => "#ef4444",        // Red
        AgentRole.OptimizationEngineer => "#f59e0b",    // Amber
        AgentRole.PrincipalQAAnalyst => "#ec4899",      // Pink
        _ => "#94a3b8"
    };

    public static string ToDefaultModel(this AgentRole role) => role switch
    {
        AgentRole.TechnicalProductManager => "openai/gpt-4o",
        AgentRole.LeadArchitect => "anthropic/claude-3.7-sonnet",
        AgentRole.SoftwareDeveloper => "qwen/qwen-2.5-coder-32b-instruct",
        AgentRole.SecurityEngineer => "openai/o3-mini",
        AgentRole.OptimizationEngineer => "anthropic/claude-3.7-sonnet",
        AgentRole.PrincipalQAAnalyst => "deepseek/deepseek-r1",
        _ => "anthropic/claude-3.7-sonnet"
    };
}
