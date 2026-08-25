using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ApiKeyVaultTests
{
    private readonly ApiKeyVaultService _vault = new();

    [Fact]
    public void AddOrUpdateKey_ShouldStoreKeyAndMaskSecret()
    {
        var key = _vault.AddOrUpdateKey("Claude Pro Key", "sk-or-v1-abcdef1234567890", isActive: true);

        key.KeyName.Should().Be("Claude Pro Key");
        key.ApiKeyMasked.Should().Be("sk-o...7890");
        key.IsActive.Should().BeTrue();

        var activeKey = _vault.GetActiveKey();
        activeKey!.KeyId.Should().Be(key.KeyId);
    }

    [Fact]
    public void SetActiveKey_ShouldSwitchGlobalActiveKey()
    {
        var key1 = _vault.AddOrUpdateKey("Key 1", "sk-or-v1-key1111111111", isActive: true);
        var key2 = _vault.AddOrUpdateKey("Key 2", "sk-or-v1-key2222222222", isActive: false);

        _vault.SetActiveKey(key2.KeyId);

        var active = _vault.GetActiveKey();
        active!.KeyId.Should().Be(key2.KeyId);
    }

    [Fact]
    public void DeleteKey_ShouldRemoveFromVault()
    {
        var key = _vault.AddOrUpdateKey("Temporary Key", "sk-or-v1-temp12345678", isActive: false);
        _vault.GetKey(key.KeyId).Should().NotBeNull();

        var deleted = _vault.DeleteKey(key.KeyId);
        deleted.Should().BeTrue();

        _vault.GetKey(key.KeyId).Should().BeNull();
    }

    [Fact]
    public void KeyUpdatedEvent_ShouldTriggerOnAddAndActiveChange()
    {
        ApiKeyVaultEntry? lastUpdated = null;
        _vault.OnKeyUpdated += entry => lastUpdated = entry;

        var key = _vault.AddOrUpdateKey("Event Key", "sk-or-v1-eventkey12345", isActive: true);
        lastUpdated.Should().NotBeNull();
        lastUpdated!.KeyId.Should().Be(key.KeyId);

        var key2 = _vault.AddOrUpdateKey("Event Key 2", "sk-or-v1-eventkey67890", isActive: false);
        _vault.SetActiveKey(key2.KeyId);

        lastUpdated.KeyId.Should().Be(key2.KeyId);
        lastUpdated.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TestKeyConnectionAsync_WithSandboxKey_ShouldReturnTrue()
    {
        var result = await _vault.TestKeyConnectionAsync("sk-or-v1-sandbox-mock-carnot-circus-0001");
        result.Should().BeTrue();
    }

    [Fact]
    public void AgentInferenceResolver_ShouldResolveCorrectModelAndKey()
    {
        var customKey = _vault.AddOrUpdateKey("Custom Key", "sk-or-v1-customkey123", isActive: false);
        var resolver = new AgentInferenceResolver(_vault);

        var team = EngineeringTeam.CreateDefault();
        var memberWithCustomKey = new AgentMember(
            Persona: AgentPersona.CreateDefault(AgentRole.LeadArchitect),
            CustomApiKeyId: customKey.KeyId,
            OverrideModel: "anthropic/claude-3.7-sonnet"
        );

        var (model, apiKey) = resolver.ResolveInferenceParameters(memberWithCustomKey, team);
        model.Should().Be("anthropic/claude-3.7-sonnet");
        apiKey.Should().Be("sk-or-v1-customkey123");
    }
}
