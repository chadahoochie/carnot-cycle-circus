using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Standards;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class StandardsValidatorTests
{
    private readonly StandardsValidator _validator = new();

    [Fact]
    public void ValidateFeatureTicket_WithoutTests_ShouldFailValidation()
    {
        var ticket = new TicketItem(
            Id: "FEAT-1",
            ParentEpicId: null,
            Title: "Add payment gateway",
            Description: "Payment integration",
            Type: TicketType.Feature,
            Status: TicketStatus.Review,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Connect to API"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var result = _validator.ValidateTicketForCompletion(ticket);
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("automated unit tests"));
    }

    [Fact]
    public void ValidateFeatureTicket_WithTests_ShouldPassValidation()
    {
        var ticket = new TicketItem(
            Id: "FEAT-2",
            ParentEpicId: null,
            Title: "Add payment gateway",
            Description: "Payment integration",
            Type: TicketType.Feature,
            Status: TicketStatus.Review,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Connect to API", "Achieve 90% unit test coverage"],
            Deliverables: [new ArtifactItem("PaymentTests.cs", "class PaymentTests {}", "csharp")],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var result = _validator.ValidateTicketForCompletion(ticket);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateArchitecturalCompliance_WithoutAdr_ShouldFailWithArchitecturalViolation()
    {
        var ticket = new TicketItem(
            Id: "SUB-QA-1",
            ParentEpicId: "EPIC-1",
            Title: "[QA] Final Acceptance Validation",
            Description: "QA Verification",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.PrincipalQAAnalyst,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Verify release"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var result = _validator.ValidateArchitecturalCompliance(ticket, Array.Empty<ArtifactItem>());
        result.IsValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Contains("Missing Architectural Decision Record (ADR)"));
    }

    [Fact]
    public void ValidateArchitecturalCompliance_WithAdrAndScaffold_ShouldPass()
    {
        var ticket = new TicketItem(
            Id: "SUB-QA-2",
            ParentEpicId: "EPIC-1",
            Title: "[QA] Final Acceptance Validation",
            Description: "QA Verification",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.PrincipalQAAnalyst,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Verify release"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var upstreamArtifacts = new List<ArtifactItem>
        {
            new("SUB-ARCH_ADR.md", "# ADR-014: High-Performance Architecture\n## Architectural Decision Record", "markdown"),
            new("IOrderPipeline.cs", "public interface IOrderPipeline {}", "csharp")
        };

        var result = _validator.ValidateArchitecturalCompliance(ticket, upstreamArtifacts);
        result.IsValid.Should().BeTrue();
    }
}
