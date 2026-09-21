using System.Collections.Concurrent;
using CarnotCycleCircus.Core.Configuration;
using CarnotCycleCircus.Core.Domain.Agents;
using Microsoft.Extensions.Options;

namespace CarnotCycleCircus.Core.Domain.Events;

public enum MessageType
{
    Chat,
    Thought,
    ToolCall,
    ToolOutput,
    Handoff,
    StateChange,
    Alert,
    ArtifactCreated
}

public record AgentMessage(
    string Id,
    AgentRole? Role,
    string SenderName,
    string Content,
    MessageType Type,
    DateTimeOffset Timestamp,
    string? TicketId = null,
    string? ProjectId = null,
    IReadOnlyDictionary<string, string>? Metadata = null
)
{
    public static AgentMessage Create(
        AgentRole? role,
        string senderName,
        string content,
        MessageType type = MessageType.Chat,
        string? ticketId = null,
        string? projectId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Id: Guid.NewGuid().ToString("N")[..8],
            Role: role,
            SenderName: senderName,
            Content: content,
            Type: type,
            Timestamp: DateTimeOffset.UtcNow,
            TicketId: ticketId,
            ProjectId: projectId,
            Metadata: metadata
        );
}

public interface IAgentEventStream
{
    void Publish(AgentMessage message);
    IReadOnlyList<AgentMessage> GetHistory();
    IReadOnlyList<AgentMessage> GetRecentMessages(string? projectId = null, int count = 100);
    void Clear();
    event Action<AgentMessage>? OnMessagePublished;
}

public class AgentEventStream : IAgentEventStream
{
    private readonly ConcurrentQueue<AgentMessage> _messages = new();
    private readonly int _maxHistorySize;

    public AgentEventStream(IOptions<EventStreamOptions> options)
    {
        _maxHistorySize = options.Value.MaxHistorySize;
    }

    /// <summary>
    /// Parameterless constructor for test scenarios (uses default MaxHistorySize=1000).
    /// Production code should use the IOptions{EventStreamOptions} overload.
    /// </summary>
    public AgentEventStream()
    {
        _maxHistorySize = new EventStreamOptions().MaxHistorySize;
    }

    public event Action<AgentMessage>? OnMessagePublished;

    public void Publish(AgentMessage message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > _maxHistorySize && _messages.TryDequeue(out _))
        {
            // prune oldest
        }

        try
        {
            OnMessagePublished?.Invoke(message);
        }
        catch
        {
            // prevent subscriber errors from disrupting publisher
        }
    }

    public IReadOnlyList<AgentMessage> GetHistory() => _messages.ToArray();

    public IReadOnlyList<AgentMessage> GetRecentMessages(string? projectId = null, int count = 100)
    {
        var messages = _messages.AsEnumerable();
        if (!string.IsNullOrEmpty(projectId))
        {
            messages = messages.Where(m => string.Equals(m.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        }
        return messages.TakeLast(count).ToList();
    }

    public void Clear()
    {
        while (_messages.TryDequeue(out _)) { }
    }
}
