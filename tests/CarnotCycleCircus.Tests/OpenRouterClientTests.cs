using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Teams;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class OpenRouterClientTests
{
    private readonly OpenRouterClient _client = new();

    [Fact]
    public async Task CompleteAsync_WithEmptyApiKey_ShouldThrowInvalidOperationException()
    {
        var request = new OpenRouterChatRequest(
            Model: "anthropic/claude-3.7-sonnet",
            Messages: [new OpenRouterMessage("user", "Hello world from test")]
        );

        var act = () => _client.CompleteAsync(request, "");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*API key is required*");
    }

    [Fact]
    public void AgentInferenceResolver_WhenNoKeyConfigured_ShouldReturnEmptyString()
    {
        var keyVault = new ApiKeyVaultService(); // unseeded
        var resolver = new AgentInferenceResolver(keyVault);
        var team = EngineeringTeam.CreateDefault();
        var member = team.GetMember(AgentRole.SoftwareDeveloper)!;

        var (model, apiKey) = resolver.ResolveInferenceParameters(member, team);

        model.Should().NotBeNullOrWhiteSpace();
        apiKey.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sk-or-v1-sandbox-mock-123")]
    public async Task FetchModelsAsync_WithNullEmptyOrMockKey_ShouldReturnEmptyListWithoutRemoteCall(string? apiKey)
    {
        var models = await _client.FetchModelsAsync(apiKey);
        models.Should().BeEmpty();
    }
}
