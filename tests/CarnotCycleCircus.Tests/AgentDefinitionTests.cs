using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Storage;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentDefinitionTests
{
    [Fact]
    public void DefaultAgents_ShouldContainAllEightCircusRoles()
    {
        var manager = new AgentDefinitionManager();
        var allAgents = manager.GetAllAgents();

        allAgents.Should().HaveCount(8);
        allAgents.Select(a => a.Persona.Role).Distinct().Should().HaveCount(8);

        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var agent = manager.GetAgentForRole(role);
            agent.Should().NotBeNull();
            agent!.Persona.Role.Should().Be(role);
            agent.Persona.Name.Should().NotBeNullOrWhiteSpace();
            agent.Persona.SystemPrompt.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void CreateAgent_ShouldAddAgentAndFireEvent()
    {
        var manager = new AgentDefinitionManager();
        IReadOnlyList<AgentMember>? notifiedAgents = null;
        manager.OnAgentsChanged += agents => notifiedAgents = agents;

        var created = manager.CreateAgent(
            role: AgentRole.SoftwareDeveloper,
            name: "Devon 'Coldbrew' Crashdump",
            systemPrompt: "Write high-performance zero-allocation C# code.",
            primaryModel: "anthropic/claude-3.7-sonnet",
            fallbackModel: "openai/o3-mini",
            temperature: 0.1,
            allowedTools: ["csharp_syntax_check", "test_runner"],
            assignedSkillIds: ["skill-csharp-standards"]
        );

        created.Should().NotBeNull();
        created.Id.Should().StartWith("agent-");
        created.Persona.Name.Should().Be("Devon 'Coldbrew' Crashdump");
        created.EffectiveModel.Should().Be("anthropic/claude-3.7-sonnet");

        manager.GetAllAgents().Should().HaveCount(9);
        notifiedAgents.Should().NotBeNull();
        notifiedAgents!.Should().HaveCount(9);
    }

    [Fact]
    public void DuplicateAgent_ShouldCreateIndependentCopy()
    {
        var manager = new AgentDefinitionManager();
        var original = manager.GetAgentForRole(AgentRole.LeadArchitect)!;

        var duplicate = manager.DuplicateAgent(original.Id, "Archduke Barnaby (Clone)");

        duplicate.Should().NotBeNull();
        duplicate.Id.Should().NotBe(original.Id);
        duplicate.Persona.Name.Should().Be("Archduke Barnaby (Clone)");
        duplicate.Persona.Role.Should().Be(original.Persona.Role);

        manager.GetAllAgents().Should().HaveCount(9);
        manager.GetAgent(duplicate.Id).Should().NotBeNull();
    }

    [Fact]
    public void SaveAgent_ShouldUpdateExistingAgent()
    {
        var manager = new AgentDefinitionManager();
        var agent = manager.GetAgentForRole(AgentRole.SecurityEngineer)!;

        var updated = agent with
        {
            OverrideModel = "anthropic/claude-3.7-sonnet",
            Persona = agent.Persona with { Name = "Cyber Sentinel Prime" }
        };

        manager.SaveAgent(updated);

        var retrieved = manager.GetAgent(agent.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Persona.Name.Should().Be("Cyber Sentinel Prime");
        retrieved.EffectiveModel.Should().Be("anthropic/claude-3.7-sonnet");
    }

    [Fact]
    public void DeleteAgent_ShouldRemoveAgentUnlessLastRemaining()
    {
        var manager = new AgentDefinitionManager();
        var extra = manager.CreateAgent(AgentRole.IntegrationEngineer, "Temporary Integration Bot", "Prompt");

        manager.GetAllAgents().Should().HaveCount(9);

        var deleted = manager.DeleteAgent(extra.Id);
        deleted.Should().BeTrue();
        manager.GetAllAgents().Should().HaveCount(8);
        manager.GetAgent(extra.Id).Should().BeNull();
    }

    [Fact]
    public async Task Persistence_ShouldReloadAgentsAcrossManagerInstances()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"carnot_agent_test_{Guid.NewGuid():N}");
        try
        {
            var options = new CarnotStorageOptions { DataDirectory = tempDir, EnableAtomicWrites = true };
            var storage = new FilePersistentStorageService(options);
            var manager1 = new AgentDefinitionManager(storage);

            var customAgent = manager1.CreateAgent(
                role: AgentRole.SoftwareDeveloper,
                name: "Madame Genevieve 'Zero-Alloc' Byte-Trapeze",
                systemPrompt: "Zero-alloc code directives only.",
                primaryModel: "qwen/qwen-2.5-coder-32b-instruct"
            );
            await manager1.FlushAsync();

            // Reload via new manager instance
            var manager2 = new AgentDefinitionManager(storage);
            var reloaded = manager2.GetAgent(customAgent.Id);

            reloaded.Should().NotBeNull();
            reloaded!.Persona.Name.Should().Be("Madame Genevieve 'Zero-Alloc' Byte-Trapeze");
            reloaded.EffectiveModel.Should().Be("qwen/qwen-2.5-coder-32b-instruct");
            manager2.GetAllAgents().Should().HaveCount(9);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
