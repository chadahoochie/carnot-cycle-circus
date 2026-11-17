using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentPersonaTests
{
    private readonly AgentExecutionEngine _executionEngine;

    public AgentPersonaTests()
    {
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        _executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver);
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
    public void PersonaSystemPrompt_ShouldEnforceDeliverableIsolationContract(AgentRole role)
    {
        var persona = AgentPersona.CreateDefault(role);

        persona.ShouldNotBeNull();
        persona.SystemPrompt.ShouldContain("DELIVERABLE ISOLATION CONTRACT");
        persona.SystemPrompt.ShouldContain("professional");
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

        var artifacts = await _executionEngine.ExecuteRoleTaskAsync(role, ticket);

        artifacts.ShouldNotBeEmpty();
        foreach (var artifact in artifacts)
        {
            artifact.Name.ShouldNotBeNullOrWhiteSpace();
            artifact.Content.ShouldNotBeNullOrWhiteSpace();
            artifact.ContentType.ShouldBeOneOf("markdown", "csharp", "json");

            // Deliverables must not contain conversational snark / joke commentary
            artifact.Content.ShouldNotContain("shareholders might experience mild existential discomfort");
            artifact.Content.ShouldNotContain("Devon has to type readonly record struct 400 times");
            artifact.Content.ShouldNotContain("Powered by Cold Brew & Cynicism");
            artifact.Content.ShouldNotContain("circus burns down");
            artifact.Content.ShouldNotContain("It works on my machine");
            artifact.Content.ShouldNotContain("rogue clowns");
            artifact.Content.ShouldNotContain("Otto's blood pressure");
            artifact.Content.ShouldNotContain("Torture-tested by Quinn");
        }
    }
}
