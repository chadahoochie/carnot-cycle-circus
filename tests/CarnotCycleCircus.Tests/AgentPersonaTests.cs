using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentPersonaTests
{
    private readonly SimulatedScenarioEngine _scenarioEngine = new();

    [Theory]
    [InlineData(AgentRole.TechnicalProductManager)]
    [InlineData(AgentRole.LeadArchitect)]
    [InlineData(AgentRole.SoftwareDeveloper)]
    [InlineData(AgentRole.SecurityEngineer)]
    [InlineData(AgentRole.OptimizationEngineer)]
    [InlineData(AgentRole.PrincipalQAAnalyst)]
    public void PersonaSystemPrompt_ShouldEnforceDeliverableIsolationContract(AgentRole role)
    {
        var persona = AgentPersona.CreateDefault(role);

        persona.Should().NotBeNull();
        persona.SystemPrompt.Should().Contain("DELIVERABLE ISOLATION CONTRACT");
        persona.SystemPrompt.Should().Contain("professional");
    }

    [Theory]
    [InlineData(AgentRole.TechnicalProductManager)]
    [InlineData(AgentRole.LeadArchitect)]
    [InlineData(AgentRole.SoftwareDeveloper)]
    [InlineData(AgentRole.SecurityEngineer)]
    [InlineData(AgentRole.OptimizationEngineer)]
    [InlineData(AgentRole.PrincipalQAAnalyst)]
    public async Task RoleDeliverables_ShouldBeProfessionalAndSyntacticallyValid(AgentRole role)
    {
        var ticket = new TicketItem(
            Id: "TICK-TEST-100",
            ParentEpicId: null,
            Title: "Zero-Allocation Event Stream",
            Description: "Implement high-throughput event streaming with bounded channels.",
            Type: TicketType.Feature,
            Status: TicketStatus.InProgress,
            AssigneeRole: role,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: [
                "Zero Gen0 heap allocations on hot path",
                "Sub-5ms P99 latency",
                "STRIDE security compliance"
            ],
            Deliverables: Array.Empty<Core.Domain.Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(role, ticket);

        artifacts.Should().NotBeEmpty();
        foreach (var artifact in artifacts)
        {
            artifact.Name.Should().NotBeNullOrWhiteSpace();
            artifact.Content.Should().NotBeNullOrWhiteSpace();
            artifact.ContentType.Should().BeOneOf("markdown", "csharp", "json");

            // Deliverables must not contain conversational snark / joke commentary
            artifact.Content.Should().NotContain("shareholders might experience mild existential discomfort");
            artifact.Content.Should().NotContain("Devon has to type readonly record struct 400 times");
            artifact.Content.Should().NotContain("Powered by Cold Brew & Cynicism");
            artifact.Content.Should().NotContain("circus burns down");
            artifact.Content.Should().NotContain("It works on my machine");
            artifact.Content.Should().NotContain("rogue clowns");
            artifact.Content.Should().NotContain("Otto's blood pressure");
            artifact.Content.Should().NotContain("Torture-tested by Quinn");
        }
    }
}
