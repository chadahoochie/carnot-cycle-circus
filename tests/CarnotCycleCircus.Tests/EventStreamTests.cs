using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class EventStreamTests
{
    private readonly AgentEventStream _stream = new();

    [Fact]
    public void PublishMessage_ShouldNotifySubscribersAndStoreInHistory()
    {
        AgentMessage? received = null;
        _stream.OnMessagePublished += msg => received = msg;

        var message = AgentMessage.Create(
            role: AgentRole.LeadArchitect,
            senderName: "Archibald",
            content: "Architecture verified",
            type: MessageType.Chat
        );

        _stream.Publish(message);

        received.Should().NotBeNull();
        received!.Content.Should().Be("Architecture verified");
        _stream.GetHistory().Should().ContainSingle();
    }

    [Fact]
    public void Clear_ShouldEmptyHistory()
    {
        _stream.Publish(AgentMessage.Create(null, "System", "Message 1"));
        _stream.Publish(AgentMessage.Create(null, "System", "Message 2"));

        _stream.GetHistory().Should().HaveCount(2);

        _stream.Clear();
        _stream.GetHistory().Should().BeEmpty();
    }
}
