using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Security;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class TeamDefinitionTests
{
    private readonly ITeamDefinitionManager _manager = new TeamDefinitionManager();

    [Fact]
    public void DefaultPresets_ShouldContainExpectedSquads()
    {
        var presets = TeamDefinition.DefaultPresets;
        presets.Should().HaveCount(6);

        presets.Select(p => p.Id).Should().BeEquivalentTo([
            "team-balanced",
            "team-move-fast",
            "team-ivory-tower",
            "team-security-hardened",
            "team-high-performance",
            "team-chaos-monkey"
        ]);

        foreach (var preset in presets)
        {
            preset.Graph.Should().NotBeNull();
            preset.Graph.Nodes.Should().NotBeEmpty();
            preset.Members.Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData("team-balanced")]
    [InlineData("team-move-fast")]
    [InlineData("team-ivory-tower")]
    [InlineData("team-security-hardened")]
    [InlineData("team-high-performance")]
    [InlineData("team-chaos-monkey")]
    public void SwitchToTeam_ShouldSwitchCurrentTeam_AndRaiseEvent(string teamId)
    {
        EngineeringTeam? notifiedTeam = null;
        _manager.OnCurrentTeamChanged += team => notifiedTeam = team;

        var switched = _manager.SwitchToTeam(teamId);

        switched.Should().BeTrue();
        _manager.GetCurrentTeam().Id.Should().Be(teamId);
        notifiedTeam.Should().NotBeNull();
        notifiedTeam!.Id.Should().Be(teamId);
    }

    [Fact]
    public void MoveFast_Preset_ShouldHaveConfiguredWorkflowGraph()
    {
        var team = _manager.GetTeam("team-move-fast");
        team.Should().NotBeNull();

        var engTeam = team!.ToEngineeringTeam();
        engTeam.Graph.Should().NotBeNull();
        engTeam.Graph.Nodes.Should().HaveCountGreaterThanOrEqualTo(3);

        var dev = engTeam.GetMember(AgentRole.SoftwareDeveloper);
        var tpm = engTeam.GetMember(AgentRole.TechnicalProductManager);

        dev.Should().NotBeNull();
        tpm.Should().NotBeNull();
        dev!.IsEnabled.Should().BeTrue();
        tpm!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ExportAndImport_ShouldPreserveTeamConfigurationAndGraph()
    {
        var original = _manager.GetTeam("team-security-hardened");
        original.Should().NotBeNull();

        var json = _manager.ExportToJson(original!.Id);

        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("team-security-hardened");

        var imported = _manager.ImportFromJson(json);
        imported.Should().NotBeNull();
        imported.Members.Should().HaveCount(original.Members.Count);
        imported.Graph.Nodes.Should().HaveCount(original.Graph.Nodes.Count);
        imported.Graph.Connections.Should().HaveCount(original.Graph.Connections.Count);
        imported.DefaultFallbackModel.Should().Be(original.DefaultFallbackModel);
    }

    [Fact]
    public void UpdateCurrentTeamGraph_ShouldPersistModifications_AndNotifySubscribers()
    {
        var initial = _manager.GetCurrentTeam();
        var customNode = new GraphNode("node-custom-test", AgentRole.SoftwareDeveloper, "Custom Zero-Alloc Node", 120, 150);
        var customConn = new PortConnection("node-tpm", PortType.Output, "node-custom-test", PortType.Input);

        var updatedGraph = initial.Graph with
        {
            Nodes = [.. initial.Graph.Nodes, customNode],
            Connections = [.. initial.Graph.Connections, customConn]
        };

        EngineeringTeam? notified = null;
        _manager.OnCurrentTeamChanged += t => notified = t;

        _manager.UpdateCurrentTeamGraph(updatedGraph);

        var current = _manager.GetCurrentTeam();
        current.Graph.Nodes.Should().Contain(n => n.Id == "node-custom-test");
        current.Graph.Connections.Should().Contain(c => c.TargetNodeId == "node-custom-test");
        notified.Should().NotBeNull();
    }

    [Fact]
    public void GetMemberForNode_WithAgentBinding_ShouldResolveAgentFromRoster()
    {
        var persona = AgentPersona.CreateDefault(AgentRole.SoftwareDeveloper) with
        {
            Name = "Madame Genevieve 'Zero-Alloc' Trapeze",
            SystemPrompt = "Zero-allocation routines only.",
            DefaultModel = "anthropic/claude-3.7-sonnet"
        };
        var customAgent = new AgentMember(persona);

        var team = _manager.GetCurrentTeam();
        var nodeWithAgent = new GraphNode(
            Id: "node-trapeze",
            Role: AgentRole.SoftwareDeveloper,
            Name: "Trapeze Node",
            X: 100,
            Y: 100,
            AgentId: customAgent.Id
        );

        var resolved = team.GetMemberForNode(nodeWithAgent, [customAgent]);

        resolved.Should().NotBeNull();
        resolved.Persona.Name.Should().Be("Madame Genevieve 'Zero-Alloc' Trapeze");
        resolved.EffectiveModel.Should().Be("anthropic/claude-3.7-sonnet");
    }

    [Fact]
    public void AgentPersona_WithAssignedSkills_ShouldRoundtripJsonSerialization()
    {
        var persona = new AgentPersona(
            Role: AgentRole.PrincipalQAAnalyst,
            Name: "Madame Medusa 'Demonic-Payload' Executioner",
            SystemPrompt: "Torture test with null payloads.",
            DefaultModel: "deepseek/deepseek-r1",
            FallbackModel: "openai/o3-mini",
            Temperature: 0.1,
            AllowedToolNames: ["test_runner"],
            AssignedSkillIds: ["skill-edge-case-torture"]
        );

        var member = new AgentMember(persona);
        var team = new TeamDefinition(
            Id: "team-skill-test",
            Name: "QA Dictatorship",
            Description: "Skill-infused team",
            Members: [member],
            Graph: WorkflowGraph.CreateDefaultEngineeringCircus(),
            DefaultFallbackModel: "deepseek/deepseek-r1",
            CreatedAt: DateTimeOffset.UtcNow
        );

        _manager.SaveTeam(team);
        var json = _manager.ExportToJson(team.Id);

        json.Should().Contain("skill-edge-case-torture");
        json.Should().Contain("Demonic-Payload");

        var imported = _manager.ImportFromJson(json);
        imported.Members.Should().HaveCount(1);
        imported.Members[0].Persona.AssignedSkillIds.Should().Contain("skill-edge-case-torture");
        imported.Members[0].Persona.Name.Should().Be(persona.Name);
    }

    [Fact]
    public async Task TeamSwitching_AndCustomGraph_ShouldPersistAcrossStorageReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"carnot_team_test_{Guid.NewGuid():N}");
        try
        {
            var options = new CarnotStorageOptions { DataDirectory = tempDir, EnableAtomicWrites = true };
            var storage = new FilePersistentStorageService(options);
            var manager = new TeamDefinitionManager(storage);

            // Switch to HighPerformance
            manager.SwitchToTeam("team-high-performance").Should().BeTrue();
            await manager.FlushAsync();

            var current = manager.GetCurrentTeam();
            current.Id.Should().Be("team-high-performance");
            var dev = current.GetMember(AgentRole.SoftwareDeveloper);
            dev!.EffectiveModel.Should().Be("qwen/qwen-2.5-coder-32b-instruct");

            // Custom change to Developer on HighPerformance
            var customizedDev = dev with
            {
                OverrideModel = "deepseek/deepseek-r1",
                Persona = dev.Persona with { DefaultModel = "deepseek/deepseek-r1" }
            };
            manager.UpdateMemberInCurrentTeam(customizedDev);
            await manager.FlushAsync();

            // Simulate app restart / new manager instance loading from storage
            var restartedManager = new TeamDefinitionManager(storage);
            var restartedTeam = restartedManager.GetCurrentTeam();

            restartedTeam.Id.Should().Be("team-high-performance");
            var restartedDev = restartedTeam.GetMember(AgentRole.SoftwareDeveloper);
            restartedDev.Should().NotBeNull();
            restartedDev!.EffectiveModel.Should().Be("deepseek/deepseek-r1");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenAgentHasNoModelSelected_ShouldThrowAndPublishAlert()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey123456789012345678901234567890", isActive: true);
        var eventStream = new AgentEventStream();

        // Team with an agent who has no model selected
        var unconfiguredPersona = AgentPersona.CreateDefault(AgentRole.SoftwareDeveloper);
        var unconfiguredMember = new AgentMember(unconfiguredPersona);
        var customTeam = new TeamDefinition(
            Id: "team-unconfigured",
            Name: "Unconfigured Squad",
            Description: "Team without models",
            Members: [unconfiguredMember],
            Graph: WorkflowGraph.CreateDefaultEngineeringCircus(),
            DefaultFallbackModel: "",
            CreatedAt: DateTimeOffset.UtcNow
        );

        var teamManager = new TeamDefinitionManager();
        teamManager.SaveTeam(customTeam);
        teamManager.SetCurrentTeam(customTeam);

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "TCK-DEV-001",
            ParentEpicId: null,
            Title: "Write Zero-Alloc Service",
            Description: "Implementation without model",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Compilable code"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        Func<Task> act = async () => await engine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, ticket);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No inference model selected for Software Developer*");

        eventStream.GetHistory().Should().Contain(m =>
            m.Type == MessageType.Alert &&
            m.Content.Contains("No inference model selected for Software Developer"));
    }

    [Fact]
    public void CreateTeam_AndSwitchToTeam_ShouldPreserveIndividualSquads()
    {
        var teamManager = new TeamDefinitionManager();

        // 1. Create custom teams
        var squadA = teamManager.CreateTeam("Team Alpha", "Alpha squad", "team-balanced");
        var squadB = teamManager.CreateTeam("Team Beta", "Beta squad", "team-high-performance");

        // 2. Customize Team Alpha Software Developer
        var devA = squadA.Members.First(m => m.Persona.Role == AgentRole.SoftwareDeveloper);
        var customizedDevA = devA with
        {
            OverrideModel = "meta-llama/llama-3.3-70b-instruct",
            Persona = devA.Persona with { SystemPrompt = "Alpha Custom Prompt" }
        };
        teamManager.SetCurrentTeam(squadA);
        teamManager.UpdateMemberInCurrentTeam(customizedDevA);

        // 3. Switch to Team Beta and verify
        teamManager.SwitchToTeam(squadB.Id).Should().BeTrue();
        teamManager.GetCurrentTeam().Id.Should().Be(squadB.Id);

        // 4. Switch back to Team Alpha and verify custom model was NOT reset to defaults
        teamManager.SwitchToTeam(squadA.Id).Should().BeTrue();
        var currentAlpha = teamManager.GetCurrentTeam();
        currentAlpha.Id.Should().Be(squadA.Id);
        var devAfterSwitch = currentAlpha.Members.First(m => m.Persona.Role == AgentRole.SoftwareDeveloper);
        devAfterSwitch.EffectiveModel.Should().Be("meta-llama/llama-3.3-70b-instruct");
        devAfterSwitch.Persona.SystemPrompt.Should().Be("Alpha Custom Prompt");
    }

    [Fact]
    public void DuplicateTeam_ShouldCreateIndependentCopy_WithUniqueMemberIds()
    {
        var teamManager = new TeamDefinitionManager();
        var sourceSquad = teamManager.CreateTeam("Original Squad", "Balanced Squad", "team-balanced");

        var duplicated = teamManager.DuplicateTeam(sourceSquad.Id, "Duplicated Squad");

        duplicated.Should().NotBeNull();
        duplicated.Id.Should().NotBe(sourceSquad.Id);
        duplicated.Name.Should().Be("Duplicated Squad");
        duplicated.Members.Count.Should().Be(sourceSquad.Members.Count);
        duplicated.Graph.Nodes.Count.Should().Be(sourceSquad.Graph.Nodes.Count);
        duplicated.Members.Select(m => m.Id).Should().NotIntersectWith(sourceSquad.Members.Select(m => m.Id));
    }

    [Fact]
    public void DeleteTeam_WhenActiveSquadDeleted_ShouldSwitchToRemainingSquad_AndNotifyEvent()
    {
        var teamManager = new TeamDefinitionManager();
        var customSquad = teamManager.CreateTeam("Doomed Squad", "Squad to be deleted", "team-balanced");
        teamManager.SwitchToTeam(customSquad.Id).Should().BeTrue();
        teamManager.GetCurrentTeam().Id.Should().Be(customSquad.Id);

        EngineeringTeam? notifiedTeam = null;
        teamManager.OnCurrentTeamChanged += team => notifiedTeam = team;

        var deleted = teamManager.DeleteTeam(customSquad.Id);

        deleted.Should().BeTrue();
        teamManager.GetAllTeams().Should().NotContain(t => t.Id == customSquad.Id);
        teamManager.GetCurrentTeam().Id.Should().NotBe(customSquad.Id);
        notifiedTeam.Should().NotBeNull();
        notifiedTeam!.Id.Should().NotBe(customSquad.Id);
    }

    [Fact]
    public void DeleteTeam_WhenNonActiveSquadDeleted_ShouldRemoveFromList_AndKeepActiveSquad()
    {
        var teamManager = new TeamDefinitionManager();
        var squadA = teamManager.CreateTeam("Active Squad", "Keep this squad", "team-balanced");
        var squadB = teamManager.CreateTeam("Inactive Squad", "Delete this squad", "team-move-fast");

        teamManager.SwitchToTeam(squadA.Id).Should().BeTrue();

        EngineeringTeam? notifiedTeam = null;
        teamManager.OnCurrentTeamChanged += team => notifiedTeam = team;

        var deleted = teamManager.DeleteTeam(squadB.Id);

        deleted.Should().BeTrue();
        teamManager.GetAllTeams().Should().NotContain(t => t.Id == squadB.Id);
        teamManager.GetAllTeams().Should().Contain(t => t.Id == squadA.Id);
        teamManager.GetCurrentTeam().Id.Should().Be(squadA.Id);
        notifiedTeam.Should().NotBeNull();
        notifiedTeam!.Id.Should().Be(squadA.Id);
    }

    [Fact]
    public void DeleteTeam_WithInvalidOrEmptyId_ShouldReturnFalse()
    {
        var teamManager = new TeamDefinitionManager();
        teamManager.DeleteTeam("").Should().BeFalse();
        teamManager.DeleteTeam("non-existent-team-id-999").Should().BeFalse();
    }

    [Fact]
    public async Task AgentExecutionEngine_ShouldResolveModelFromActiveTeamNodeAgentBinding()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey123456789012345678901234567890", isActive: true);
        var eventStream = new AgentEventStream();
        var agentDefManager = new AgentDefinitionManager();

        // 1. Create custom Architect agent with Google Gemini model
        var customArchAgent = agentDefManager.CreateAgent(
            role: AgentRole.LeadArchitect,
            name: "Archduke Gemini Abstraction",
            systemPrompt: "Architectural rules only.",
            primaryModel: "google/gemini-2.0-flash-001"
        );

        // 2. Create custom squad where Lead Architect node binds to customArchAgent
        var teamManager = new TeamDefinitionManager();
        var squad = teamManager.CreateTeam("Gemini Architecture Squad", "Squad using Gemini", "team-balanced");
        var updatedNodes = squad.Graph.Nodes.Select(n => n.Role == AgentRole.LeadArchitect ? n with { AgentId = customArchAgent.Id } : n).ToList();
        teamManager.UpdateCurrentTeamGraph(squad.Graph with { Nodes = updatedNodes });

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: teamManager,
            agentDefManager: agentDefManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "SUB-ARCH-001",
            ParentEpicId: "EPIC-001",
            Title: "Design Clean Architecture Blueprint",
            Description: "ADR design",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean abstractions"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);
        deliverables.Should().NotBeEmpty();

        mockClient.LastRequest.Should().NotBeNull();
        mockClient.LastRequest!.Model.Should().Be("google/gemini-2.0-flash-001");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenAgentModelUpdatedInAgentStudio_ShouldUseUpdatedModel()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey123456789012345678901234567890", isActive: true);
        var eventStream = new AgentEventStream();
        var agentDefManager = new AgentDefinitionManager();
        var teamManager = new TeamDefinitionManager();

        // 1. Update Lead Architect in Agent Studio to DeepSeek
        var archAgent = agentDefManager.GetAgentForRole(AgentRole.LeadArchitect);
        archAgent.Should().NotBeNull();
        var updatedArch = archAgent! with
        {
            OverrideModel = "deepseek/deepseek-chat",
            Persona = archAgent.Persona with { DefaultModel = "deepseek/deepseek-chat" }
        };
        agentDefManager.SaveAgent(updatedArch);

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: teamManager,
            agentDefManager: agentDefManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "SUB-ARCH-002",
            ParentEpicId: "EPIC-001",
            Title: "Design Distributed Pipeline",
            Description: "ADR design",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean abstractions"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);
        deliverables.Should().NotBeEmpty();

        mockClient.LastRequest.Should().NotBeNull();
        mockClient.LastRequest!.Model.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public void AgentInferenceResolver_WhenEffectiveModelEmpty_ShouldFallbackToSquadDefaultFallbackModel()
    {
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-testkey12345", isActive: true);
        var resolver = new AgentInferenceResolver(keyVault);

        var unconfiguredPersona = AgentPersona.CreateDefault(AgentRole.SoftwareDeveloper);
        var unconfiguredMember = new AgentMember(unconfiguredPersona);

        var squadWithFallback = EngineeringTeam.CreateDefault() with
        {
            DefaultFallbackModel = "meta-llama/llama-3.3-70b-instruct"
        };

        var (model, apiKey) = resolver.ResolveInferenceParameters(unconfiguredMember, squadWithFallback);

        model.Should().Be("meta-llama/llama-3.3-70b-instruct");
        apiKey.Should().Be("sk-or-v1-testkey12345");
    }
}
