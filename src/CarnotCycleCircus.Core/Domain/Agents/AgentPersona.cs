namespace CarnotCycleCircus.Core.Domain.Agents;

public record AgentPersona(
    AgentRole Role,
    string Name,
    string SystemPrompt,
    string DefaultModel,
    string FallbackModel,
    double Temperature,
    IReadOnlyList<string> AllowedToolNames
)
{
    public static AgentPersona CreateDefault(AgentRole role) => role switch
    {
        AgentRole.TechnicalProductManager => new(
            Role: role,
            Name: "Tara (TPM)",
            SystemPrompt: "You are the Technical Product Manager (TPM). Your primary responsibility is deconstructing high-level product goals and epics into well-specified user stories, defining strict acceptance criteria, estimating complexity, and aligning deliverable timelines.",
            DefaultModel: "openai/gpt-4o",
            FallbackModel: "anthropic/claude-3.5-haiku",
            Temperature: 0.2,
            AllowedToolNames: ["web_search", "memory_lookup"]
        ),
        AgentRole.LeadArchitect => new(
            Role: role,
            Name: "Archibald (Lead Architect)",
            SystemPrompt: "You are the Lead Architect. You govern system topology, domain boundaries, API contracts, design patterns, and Architectural Decision Records (ADRs). You deconstruct user stories into technical subtasks with clear DAG dependencies.",
            DefaultModel: "anthropic/claude-3.7-sonnet",
            FallbackModel: "openai/gpt-4o",
            Temperature: 0.1,
            AllowedToolNames: ["web_search", "csharp_syntax_check", "memory_lookup", "adr_writer"]
        ),
        AgentRole.SoftwareDeveloper => new(
            Role: role,
            Name: "Devon (Senior Developer)",
            SystemPrompt: "You are the Senior Software Developer. You implement features according to architectural specifications, write idiomatic C# 13 / .NET 10 code, create comprehensive unit tests, and maintain zero-allocation standards.",
            DefaultModel: "qwen/qwen-2.5-coder-32b-instruct",
            FallbackModel: "anthropic/claude-3.7-sonnet",
            Temperature: 0.1,
            AllowedToolNames: ["csharp_syntax_check", "test_runner", "memory_lookup"]
        ),
        AgentRole.SecurityEngineer => new(
            Role: role,
            Name: "Sari (Security Engineer)",
            SystemPrompt: "You are the Principal Security Engineer. You perform STRIDE threat modeling, review code for secret leakage, validate input sanitization, check authentication/authorization boundaries, and reject insecure implementations with remediation notes.",
            DefaultModel: "openai/o3-mini",
            FallbackModel: "deepseek/deepseek-r1",
            Temperature: 0.0,
            AllowedToolNames: ["web_search", "csharp_syntax_check", "memory_lookup"]
        ),
        AgentRole.OptimizationEngineer => new(
            Role: role,
            Name: "Otto (Optimization Engineer)",
            SystemPrompt: "You are the Performance & Optimization Engineer. You audit latency, memory allocations, garbage collection pressure, lock contention, and algorithmic complexity. You propose zero-allocation and vectorization optimizations.",
            DefaultModel: "anthropic/claude-3.7-sonnet",
            FallbackModel: "openai/gpt-4o",
            Temperature: 0.0,
            AllowedToolNames: ["csharp_syntax_check", "test_runner", "memory_lookup"]
        ),
        AgentRole.PrincipalQAAnalyst => new(
            Role: role,
            Name: "Quinn (Principal QA Analyst)",
            SystemPrompt: "You are the Principal QA Analyst. You design end-to-end test strategies, verify acceptance criteria against deliverables, find edge-case failures, run test suites, and provide rigorous quality scorecards.",
            DefaultModel: "deepseek/deepseek-r1",
            FallbackModel: "openai/o3-mini",
            Temperature: 0.1,
            AllowedToolNames: ["test_runner", "memory_lookup", "csharp_syntax_check"]
        ),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
