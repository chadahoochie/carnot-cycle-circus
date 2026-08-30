using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
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
    public void AllArchetypes_ShouldContainExpectedPresets()
    {
        var archetypes = TeamArchetypes.AllArchetypes;
        archetypes.Should().HaveCount(6);

        archetypes.Select(a => a.ArchetypeName).Should().BeEquivalentTo([
            "Balanced",
            "MoveFastBreakProduction",
            "IvoryTowerCathedrals",
            "SecurityHardened",
            "HighPerformance",
            "ChaosMonkeyRodeo"
        ]);
    }

    [Theory]
    [InlineData("Balanced")]
    [InlineData("MoveFastBreakProduction")]
    [InlineData("IvoryTowerCathedrals")]
    [InlineData("SecurityHardened")]
    [InlineData("HighPerformance")]
    [InlineData("ChaosMonkeyRodeo")]
    public void LoadArchetype_ShouldSwitchCurrentTeam_AndRaiseEvent(string archetypeName)
    {
        EngineeringTeam? notifiedTeam = null;
        _manager.OnCurrentTeamChanged += team => notifiedTeam = team;

        var loaded = _manager.LoadArchetype(archetypeName);

        loaded.Should().NotBeNull();
        _manager.GetCurrentTeam().Id.Should().Be(loaded.Id);
        notifiedTeam.Should().NotBeNull();
        notifiedTeam!.Id.Should().Be(loaded.Id);
    }

    [Fact]
    public void MoveFastBreakProduction_ShouldDisableSecurityAndQA_ByDefault()
    {
        var team = _manager.LoadArchetype("MoveFastBreakProduction");
        var engTeam = team.ToEngineeringTeam();

        var dev = engTeam.GetMember(AgentRole.SoftwareDeveloper);
        var tpm = engTeam.GetMember(AgentRole.TechnicalProductManager);
        var sec = engTeam.GetMember(AgentRole.SecurityEngineer);
        var qa = engTeam.GetMember(AgentRole.PrincipalQAAnalyst);

        dev.Should().NotBeNull();
        tpm.Should().NotBeNull();
        sec.Should().NotBeNull();
        qa.Should().NotBeNull();

        dev!.IsEnabled.Should().BeTrue();
        tpm!.IsEnabled.Should().BeTrue();
        sec!.IsEnabled.Should().BeFalse();
        qa!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ExportAndImport_ShouldPreserveTeamConfiguration()
    {
        var original = _manager.LoadArchetype("SecurityHardened");
        var json = _manager.ExportToJson(original.Id);

        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("SecurityHardened");

        var imported = _manager.ImportFromJson(json);
        imported.Should().NotBeNull();
        imported.Members.Should().HaveCount(8);
        imported.DefaultFallbackModel.Should().Be(original.DefaultFallbackModel);
    }

    [Fact]
    public void UpdateMember_ShouldPersistModifications_AndNotifySubscribers()
    {
        var initial = _manager.GetCurrentTeam();
        var devMember = initial.GetMember(AgentRole.SoftwareDeveloper);
        devMember.Should().NotBeNull();

        var updatedMember = devMember! with
        {
            OverrideModel = "anthropic/claude-3.7-sonnet",
            IsEnabled = true
        };

        var updatedMembers = initial.Members
            .Select(m => m.Persona.Role == AgentRole.SoftwareDeveloper ? updatedMember : m)
            .ToList();

        var teamDef = new TeamDefinition(
            Id: initial.Id,
            Name: initial.Name,
            Description: initial.Description,
            ArchetypeName: "Custom",
            Members: updatedMembers,
            DefaultFallbackModel: initial.DefaultFallbackModel,
            CreatedAt: DateTimeOffset.UtcNow
        );

        EngineeringTeam? notified = null;
        _manager.OnCurrentTeamChanged += t => notified = t;

        _manager.SaveTeam(teamDef);

        var current = _manager.GetCurrentTeam();
        var currentDev = current.GetMember(AgentRole.SoftwareDeveloper);
        currentDev.Should().NotBeNull();
        currentDev!.EffectiveModel.Should().Be("anthropic/claude-3.7-sonnet");
        notified.Should().NotBeNull();
    }

    [Fact]
    public void AddMemberToCurrentTeam_ShouldIncreaseMemberCount_AndNotifySubscribers()
    {
        var initial = _manager.GetCurrentTeam();
        var initialCount = initial.Members.Count;

        var newPersona = new AgentPersona(
            Role: AgentRole.SoftwareDeveloper,
            Name: "Madame Genevieve 'Zero-Alloc' Byte-Trapeze (Senior Developer)",
            SystemPrompt: "Zero-alloc code directives only.",
            DefaultModel: "qwen/qwen-2.5-coder-32b-instruct",
            FallbackModel: "anthropic/claude-3.7-sonnet",
            Temperature: 0.1,
            AllowedToolNames: ["csharp_syntax_check", "test_runner"],
            AssignedSkillIds: ["skill-csharp-standards", "skill-perf-benchmarks"]
        );

        var newMember = new AgentMember(newPersona);

        EngineeringTeam? notified = null;
        _manager.OnCurrentTeamChanged += t => notified = t;

        _manager.AddMemberToCurrentTeam(newMember);

        var current = _manager.GetCurrentTeam();
        current.Members.Should().HaveCount(initialCount + 1);
        current.Members.Should().Contain(m => m.Persona.Name == newPersona.Name);
        notified.Should().NotBeNull();
        notified!.Members.Should().HaveCount(initialCount + 1);
    }

    [Fact]
    public void RemoveMemberFromCurrentTeam_ShouldRemoveMember_AndNotifySubscribers()
    {
        var newPersona = new AgentPersona(
            Role: AgentRole.SecurityEngineer,
            Name: "Archduke Ignatius 'Zero-Trust' Airgap-Sentinel (Security Engineer)",
            SystemPrompt: "Airgap all inputs.",
            DefaultModel: "openai/o3-mini",
            FallbackModel: "deepseek/deepseek-r1",
            Temperature: 0.0,
            AllowedToolNames: ["web_search"],
            AssignedSkillIds: ["skill-stride-modeling"]
        );

        var newMember = new AgentMember(newPersona);
        _manager.AddMemberToCurrentTeam(newMember);

        var countAfterAdd = _manager.GetCurrentTeam().Members.Count;

        EngineeringTeam? notified = null;
        _manager.OnCurrentTeamChanged += t => notified = t;

        var removed = _manager.RemoveMemberFromCurrentTeam(newMember.Id);
        removed.Should().BeTrue();

        var current = _manager.GetCurrentTeam();
        current.Members.Should().HaveCount(countAfterAdd - 1);
        current.Members.Should().NotContain(m => m.Id == newMember.Id);
        notified.Should().NotBeNull();
    }

    [Fact]
    public void AgentPersona_WithAssignedSkills_ShouldRoundtripJsonSerialization()
    {
        var persona = new AgentPersona(
            Role: AgentRole.PrincipalQAAnalyst,
            Name: "Madame Medusa 'Demonic-Payload' Build-Executioner (Principal QA Analyst)",
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
            ArchetypeName: "Custom",
            Members: [member],
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
    public void TeamDefinitionManager_Constructor_ShouldHaveMatchingMemberIdsBetweenCurrentTeamAndStoredArchetypes()
    {
        var manager = new TeamDefinitionManager();
        var currentTeam = manager.GetCurrentTeam();
        var storedTeam = manager.GetTeam(currentTeam.Id);

        storedTeam.Should().NotBeNull();
        currentTeam.Members.Should().HaveCount(storedTeam!.Members.Count);

        for (int i = 0; i < currentTeam.Members.Count; i++)
        {
            currentTeam.Members[i].Id.Should().Be(storedTeam.Members[i].Id);
            currentTeam.Members[i].Persona.Name.Should().Be(storedTeam.Members[i].Persona.Name);
        }
    }

    [Theory]
    [InlineData(AgentRole.RequirementsResearcher)]
    [InlineData(AgentRole.TechnicalProductManager)]
    [InlineData(AgentRole.LeadArchitect)]
    [InlineData(AgentRole.SoftwareDeveloper)]
    [InlineData(AgentRole.SecurityEngineer)]
    [InlineData(AgentRole.OptimizationEngineer)]
    [InlineData(AgentRole.PrincipalQAAnalyst)]
    [InlineData(AgentRole.IntegrationEngineer)]
    public void AgentPersona_CreateDefault_ShouldNotSelectDefaultModel(AgentRole role)
    {
        var persona = AgentPersona.CreateDefault(role);
        persona.DefaultModel.Should().BeEmpty();
        persona.FallbackModel.Should().BeEmpty();
    }

    [Fact]
    public void AgentMember_WithNoModel_ShouldReportEmptyEffectiveModelAndHasModelFalse()
    {
        var persona = AgentPersona.CreateDefault(AgentRole.SoftwareDeveloper);
        var member = new AgentMember(persona);

        member.EffectiveModel.Should().BeEmpty();
        member.HasModel.Should().BeFalse();
    }

    [Fact]
    public void UpdateMember_UnderArchetype_ShouldPersistSelectedModelAndArchetypeName()
    {
        var manager = new TeamDefinitionManager();
        var loaded = manager.LoadArchetype("SecurityHardened");
        loaded.ArchetypeName.Should().Be("SecurityHardened");

        var current = manager.GetCurrentTeam();
        current.ArchetypeName.Should().Be("SecurityHardened");

        var secMember = current.GetMember(AgentRole.SecurityEngineer);
        secMember.Should().NotBeNull();
        secMember!.EffectiveModel.Should().Be("openai/o3-mini");

        // Custom override for Security Engineer
        var updated = secMember with
        {
            OverrideModel = "anthropic/claude-3.7-sonnet",
            Persona = secMember.Persona with { DefaultModel = "anthropic/claude-3.7-sonnet" }
        };

        manager.UpdateMemberInCurrentTeam(updated);

        var refreshed = manager.GetCurrentTeam();
        var refreshedSec = refreshed.GetMember(AgentRole.SecurityEngineer);
        refreshedSec.Should().NotBeNull();
        refreshedSec!.EffectiveModel.Should().Be("anthropic/claude-3.7-sonnet");
        refreshed.ArchetypeName.Should().Be("SecurityHardened");
    }

    [Fact]
    public async Task ArchetypeSwitching_ShouldApplyArchetypeModels_AndPersistAcrossStorageReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"carnot_team_test_{Guid.NewGuid():N}");
        try
        {
            var options = new CarnotStorageOptions { DataDirectory = tempDir, EnableAtomicWrites = true };
            var storage = new FilePersistentStorageService(options);
            var manager = new TeamDefinitionManager(storage);

            // Switch to HighPerformance
            var hpTeam = manager.LoadArchetype("HighPerformance");
            await manager.FlushAsync();

            var current = manager.GetCurrentTeam();
            current.ArchetypeName.Should().Be("HighPerformance");
            var dev = current.GetMember(AgentRole.SoftwareDeveloper);
            dev!.EffectiveModel.Should().Be("anthropic/claude-3.7-sonnet");

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

            restartedTeam.Id.Should().Be(hpTeam.Id);
            restartedTeam.ArchetypeName.Should().Be("HighPerformance");
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
            ArchetypeName: "Custom",
            Members: [unconfiguredMember],
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
}
