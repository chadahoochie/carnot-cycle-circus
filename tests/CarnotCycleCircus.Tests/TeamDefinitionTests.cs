using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Teams;
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
        imported.Members.Should().HaveCount(7);
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
}
