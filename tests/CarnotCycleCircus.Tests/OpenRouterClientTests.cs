using CarnotCycleCircus.Core.Domain.Inference;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class OpenRouterClientTests
{
    private readonly OpenRouterClient _client = new();

    [Fact]
    public async Task CompleteAsync_WithSandboxKey_ShouldReturnOfflineSimulatedResponse()
    {
        var request = new OpenRouterChatRequest(
            Model: "anthropic/claude-3.7-sonnet",
            Messages: [new OpenRouterMessage("user", "Hello world from test")]
        );

        var response = await _client.CompleteAsync(request, "sk-or-v1-sandbox-mock-carnot-circus-0001");

        response.Should().NotBeNull();
        response.Model.Should().Be("anthropic/claude-3.7-sonnet");
        response.FirstContent.Should().Contain("Sandbox Output");
        response.Usage.Should().NotBeNull();
        response.Usage!.TotalTokens.Should().BeGreaterThan(0);
    }
}
