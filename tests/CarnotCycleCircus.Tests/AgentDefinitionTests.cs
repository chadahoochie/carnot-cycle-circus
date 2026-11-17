using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Storage;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentDefinitionTests
{
    [Fact]
    public void DefaultAgents_ShouldContainAllCircusRoles()
    {
        var manager = new AgentDefinitionManager();
        var allAgents = manager.GetAllAgents();

        allAgents.Count().ShouldBe(9);
        allAgents.Select(a => a.Persona.Role).Distinct().Count().ShouldBe(9);

        foreach (var role in Enum.GetValues<AgentRole>())
        {
            var agent = manager.GetAgentForRole(role);
            agent.ShouldNotBeNull();
            agent!.Persona.Role.ShouldBe(role);
            agent.Persona.Name.ShouldNotBeNullOrWhiteSpace();
            agent.Persona.SystemPrompt.ShouldNotBeNullOrWhiteSpace();
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

        created.ShouldNotBeNull();
        created.Id.ShouldStartWith("agent-");
        created.Persona.Name.ShouldBe("Devon 'Coldbrew' Crashdump");
        created.EffectiveModel.ShouldBe("anthropic/claude-3.7-sonnet");

        manager.GetAllAgents().Count().ShouldBe(10);
        notifiedAgents.ShouldNotBeNull();
        notifiedAgents!.Count().ShouldBe(10);
    }

    [Fact]
    public void DuplicateAgent_ShouldCreateIndependentCopy()
    {
        var manager = new AgentDefinitionManager();
        var original = manager.GetAgentForRole(AgentRole.LeadArchitect)!;

        var duplicate = manager.DuplicateAgent(original.Id, "Archduke Barnaby (Clone)");

        duplicate.ShouldNotBeNull();
        duplicate.Id.ShouldNotBe(original.Id);
        duplicate.Persona.Name.ShouldBe("Archduke Barnaby (Clone)");
        duplicate.Persona.Role.ShouldBe(original.Persona.Role);

        manager.GetAllAgents().Count().ShouldBe(10);
        manager.GetAgent(duplicate.Id).ShouldNotBeNull();
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
        retrieved.ShouldNotBeNull();
        retrieved!.Persona.Name.ShouldBe("Cyber Sentinel Prime");
        retrieved.EffectiveModel.ShouldBe("anthropic/claude-3.7-sonnet");
    }

    [Fact]
    public void DeleteAgent_ShouldRemoveAgentUnlessLastRemaining()
    {
        var manager = new AgentDefinitionManager();
        var extra = manager.CreateAgent(AgentRole.IntegrationEngineer, "Temporary Integration Bot", "Prompt");

        manager.GetAllAgents().Count().ShouldBe(10);

        var deleted = manager.DeleteAgent(extra.Id);
        deleted.ShouldBeTrue();
        manager.GetAllAgents().Count().ShouldBe(9);
        manager.GetAgent(extra.Id).ShouldBeNull();
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

            reloaded.ShouldNotBeNull();
            reloaded!.Persona.Name.ShouldBe("Madame Genevieve 'Zero-Alloc' Byte-Trapeze");
            reloaded.EffectiveModel.ShouldBe("qwen/qwen-2.5-coder-32b-instruct");
            manager2.GetAllAgents().Count().ShouldBe(10);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
