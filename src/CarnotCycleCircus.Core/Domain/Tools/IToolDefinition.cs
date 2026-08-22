using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Tools;

public record ToolExecutionContext(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    AgentRole InvokingRole,
    string? TicketId = null
);

public record ToolResult(
    bool Success,
    string Output,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, string>? Metadata = null
)
{
    public static ToolResult Ok(string output, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(Success: true, Output: output, ErrorMessage: null, Metadata: metadata);

    public static ToolResult Fail(string error, string? output = null, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(Success: false, Output: output ?? string.Empty, ErrorMessage: error, Metadata: metadata);
}

public interface IToolDefinition
{
    string Name { get; }
    string Description { get; }
    IReadOnlyDictionary<string, string> ParameterSchema { get; }
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default);
}
