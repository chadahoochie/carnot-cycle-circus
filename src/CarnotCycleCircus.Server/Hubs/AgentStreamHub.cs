using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using Microsoft.AspNetCore.SignalR;

namespace CarnotCycleCircus.Server.Hubs;

public class AgentStreamHub : Hub
{
    private readonly IAgentEventStream _eventStream;

    public AgentStreamHub(IAgentEventStream eventStream)
    {
        _eventStream = eventStream;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Caller.SendAsync("Connected", new { connectionId = Context.ConnectionId, status = "Ready" });
    }

    public Task BroadcastMessage(string senderName, string content, string role)
    {
        if (Enum.TryParse<AgentRole>(role, true, out var parsedRole))
        {
            _eventStream.Publish(AgentMessage.Create(parsedRole, senderName, content, MessageType.Chat));
        }

        return Task.CompletedTask;
    }
}
