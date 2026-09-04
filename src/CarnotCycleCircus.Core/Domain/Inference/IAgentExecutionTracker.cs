using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Inference;

public record AgentExecutionTrace(
    AgentRole Role,
    string RoleName,
    string TicketId,
    string TicketTitle,
    string PrimaryModel,
    string? FallbackModel,
    string ActiveModel,
    bool IsFallbackActive,
    string? FailoverReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string CurrentPhase,
    int ChunksReceived,
    string FullStreamOutput,
    string LiveSnippet,
    string? SystemPrompt,
    string? UserPrompt,
    IReadOnlyList<string> UpstreamDeliverableNames,
    string? ErrorMessage,
    bool IsSuccess,
    bool IsRunning
);

public interface IAgentExecutionTracker
{
    AgentExecutionTrace? CurrentExecution { get; }
    AgentExecutionTrace? LastExecution { get; }
    event Action<AgentExecutionTrace?>? OnExecutionUpdated;

    void StartExecution(
        AgentRole role,
        string roleName,
        string ticketId,
        string ticketTitle,
        string primaryModel,
        string? fallbackModel,
        string? systemPrompt,
        string? userPrompt,
        IReadOnlyList<string> upstreamDeliverables);

    void AppendChunk(string ticketId, string chunk);
    void RecordFailover(string ticketId, string fallbackModel, string reason);
    void SetCurrentPhase(string ticketId, string phase);
    void CompleteExecution(string ticketId, bool success, string? errorMessage = null);
    AgentExecutionTrace? GetExecutionForTicket(string ticketId);
    IReadOnlyList<AgentExecutionTrace> GetAllTraces();
}
