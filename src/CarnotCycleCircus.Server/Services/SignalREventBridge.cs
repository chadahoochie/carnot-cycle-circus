using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace CarnotCycleCircus.Server.Services;

public class SignalREventBridge : BackgroundService
{
    private readonly IAgentEventStream _eventStream;
    private readonly IHubContext<AgentStreamHub> _hubContext;

    public SignalREventBridge(IAgentEventStream eventStream, IHubContext<AgentStreamHub> hubContext)
    {
        _eventStream = eventStream;
        _hubContext = hubContext;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _eventStream.OnMessagePublished += async message =>
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveAgentMessage", message, stoppingToken);
            }
            catch
            {
                // Ignore transient client send exceptions
            }
        };

        return Task.CompletedTask;
    }
}
