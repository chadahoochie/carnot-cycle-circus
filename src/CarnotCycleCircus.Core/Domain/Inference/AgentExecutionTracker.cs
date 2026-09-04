using System.Collections.Concurrent;
using System.Text;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Inference;

public class AgentExecutionTracker : IAgentExecutionTracker
{
    private readonly ConcurrentDictionary<string, AgentExecutionTrace> _traces = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StringBuilder> _streamBuffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public AgentExecutionTrace? CurrentExecution { get; private set; }
    public AgentExecutionTrace? LastExecution { get; private set; }

    public event Action<AgentExecutionTrace?>? OnExecutionUpdated;

    public void StartExecution(
        AgentRole role,
        string roleName,
        string ticketId,
        string ticketTitle,
        string primaryModel,
        string? fallbackModel,
        string? systemPrompt,
        string? userPrompt,
        IReadOnlyList<string> upstreamDeliverables)
    {
        lock (_lock)
        {
            var buffer = new StringBuilder();
            _streamBuffers[ticketId] = buffer;

            var trace = new AgentExecutionTrace(
                Role: role,
                RoleName: roleName,
                TicketId: ticketId,
                TicketTitle: ticketTitle,
                PrimaryModel: primaryModel,
                FallbackModel: fallbackModel,
                ActiveModel: primaryModel,
                IsFallbackActive: false,
                FailoverReason: null,
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CurrentPhase: "Initiating LLM inference...",
                ChunksReceived: 0,
                FullStreamOutput: string.Empty,
                LiveSnippet: string.Empty,
                SystemPrompt: systemPrompt,
                UserPrompt: userPrompt,
                UpstreamDeliverableNames: upstreamDeliverables ?? Array.Empty<string>(),
                ErrorMessage: null,
                IsSuccess: false,
                IsRunning: true
            );

            _traces[ticketId] = trace;
            CurrentExecution = trace;
            OnExecutionUpdated?.Invoke(CurrentExecution);
        }
    }

    public void AppendChunk(string ticketId, string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;

        lock (_lock)
        {
            if (!_traces.TryGetValue(ticketId, out var existing)) return;

            var buffer = _streamBuffers.GetOrAdd(ticketId, _ => new StringBuilder());
            if (buffer.Length < 1_000_000)
            {
                buffer.Append(chunk);
            }

            var fullText = buffer.ToString();
            var snippet = fullText.Length <= 600 ? fullText : fullText[^600..];

            var updated = existing with
            {
                ChunksReceived = existing.ChunksReceived + 1,
                FullStreamOutput = fullText,
                LiveSnippet = snippet,
                CurrentPhase = $"Streaming deliverable ({existing.ChunksReceived + 1} token chunks)"
            };

            _traces[ticketId] = updated;
            if (CurrentExecution?.TicketId == ticketId)
            {
                CurrentExecution = updated;
            }

            OnExecutionUpdated?.Invoke(CurrentExecution);
        }
    }

    public void RecordFailover(string ticketId, string fallbackModel, string reason)
    {
        lock (_lock)
        {
            if (!_traces.TryGetValue(ticketId, out var existing)) return;

            var updated = existing with
            {
                ActiveModel = fallbackModel,
                IsFallbackActive = true,
                FailoverReason = reason,
                CurrentPhase = $"Autonomous failover to [{fallbackModel}] ({reason})"
            };

            _traces[ticketId] = updated;
            if (CurrentExecution?.TicketId == ticketId)
            {
                CurrentExecution = updated;
            }

            OnExecutionUpdated?.Invoke(CurrentExecution);
        }
    }

    public void SetCurrentPhase(string ticketId, string phase)
    {
        lock (_lock)
        {
            if (!_traces.TryGetValue(ticketId, out var existing)) return;

            var updated = existing with { CurrentPhase = phase };
            _traces[ticketId] = updated;

            if (CurrentExecution?.TicketId == ticketId)
            {
                CurrentExecution = updated;
            }

            OnExecutionUpdated?.Invoke(CurrentExecution);
        }
    }

    public void CompleteExecution(string ticketId, bool success, string? errorMessage = null)
    {
        lock (_lock)
        {
            if (_traces.TryGetValue(ticketId, out var existing))
            {
                var updated = existing with
                {
                    CompletedAt = DateTimeOffset.UtcNow,
                    IsRunning = false,
                    IsSuccess = success,
                    ErrorMessage = errorMessage,
                    CurrentPhase = success ? "Delivered task deliverable" : $"Failed: {errorMessage ?? "Unknown error"}"
                };

                _traces[ticketId] = updated;
                LastExecution = updated;
            }
            else if (CurrentExecution?.TicketId == ticketId)
            {
                LastExecution = CurrentExecution with
                {
                    CompletedAt = DateTimeOffset.UtcNow,
                    IsRunning = false,
                    IsSuccess = success,
                    ErrorMessage = errorMessage
                };
            }

            if (CurrentExecution?.TicketId == ticketId)
            {
                CurrentExecution = null;
            }

            _streamBuffers.TryRemove(ticketId, out _);
            OnExecutionUpdated?.Invoke(CurrentExecution);
        }
    }

    public AgentExecutionTrace? GetExecutionForTicket(string ticketId)
    {
        if (string.IsNullOrWhiteSpace(ticketId)) return null;
        return _traces.TryGetValue(ticketId, out var trace) ? trace : null;
    }

    public IReadOnlyList<AgentExecutionTrace> GetAllTraces() =>
        _traces.Values.OrderByDescending(t => t.StartedAt).ToList();
}
