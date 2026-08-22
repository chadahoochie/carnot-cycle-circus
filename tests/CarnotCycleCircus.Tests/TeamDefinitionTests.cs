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
        imported.Members.Should().HaveCount(6);
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
}
