using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class WorkDecompositionTests
{
    private readonly TicketStore _ticketStore = new();
    private readonly WorkDecompositionEngine _engine;

    public WorkDecompositionTests()
    {
        _engine = new WorkDecompositionEngine(_ticketStore);
    }

    [Fact]
    public void DeconstructEpic_ShouldCreateEpicStoryAndSubtasksAcrossAllRoles()
    {
        var result = _engine.DeconstructEpic(
            "Build Distributed PubSub",
            "Implement high-throughput in-memory pub/sub channels"
        );

        result.Should().NotBeEmpty();
        var epic = result.First(t => t.Type == TicketType.Epic);
        epic.Title.Should().Be("Build Distributed PubSub");

        var subtasks = result.Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.Should().HaveCount(6);

        // Verify role coverage
        subtasks.Select(s => s.AssigneeRole).Should().Contain([
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            AgentRole.OptimizationEngineer,
            AgentRole.PrincipalQAAnalyst,
            AgentRole.IntegrationEngineer
        ]);

        // Verify DAG ordering: Dev depends on Arch, Security & Opt depend on Dev, QA depends on Sec & Opt, Integration depends on QA
        var archSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.LeadArchitect);
        var devSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.SoftwareDeveloper);
        var secSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.SecurityEngineer);
        var optSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.OptimizationEngineer);
        var qaSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.PrincipalQAAnalyst);
        var intSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.IntegrationEngineer);

        devSubtask.DependsOnTicketIds.Should().Contain(archSubtask.Id);
        secSubtask.DependsOnTicketIds.Should().Contain(devSubtask.Id);
        optSubtask.DependsOnTicketIds.Should().Contain(devSubtask.Id);
        qaSubtask.DependsOnTicketIds.Should().Contain(secSubtask.Id);
        qaSubtask.DependsOnTicketIds.Should().Contain(optSubtask.Id);
        intSubtask.DependsOnTicketIds.Should().Contain(qaSubtask.Id);
    }

    [Fact]
    public void DeconstructEpicIntoUserStories_ShouldCreateEpicAndUserStories_WithoutPrematureSubtasks()
    {
        var researchBrief = new ArtifactItem(
            Name: "RESEARCH_BRIEF.md",
            Content: "# Research Brief: Distributed PubSub",
            ContentType: "markdown",
            Description: "RFC Feasibility Analysis"
        );

        var tickets = _engine.DeconstructEpicIntoUserStories(
            "Build Distributed PubSub",
            "Implement high-throughput in-memory pub/sub channels",
            researchBrief
        );

        tickets.Should().HaveCount(2);
        var epic = tickets.First(t => t.Type == TicketType.Epic);
        epic.Deliverables.Should().Contain(d => d.Name == "RESEARCH_BRIEF.md");
        epic.AssigneeRole.Should().Be(AgentRole.TechnicalProductManager);

        var story = tickets.First(t => t.Type == TicketType.Feature);
        story.ParentEpicId.Should().Be(epic.Id);
        story.AssigneeRole.Should().Be(AgentRole.LeadArchitect);
        story.Status.Should().Be(TicketStatus.InProgress);
        story.CreatedByRole.Should().Be(AgentRole.TechnicalProductManager);

        // Ensure subtasks are NOT prematurely created during TPM story generation phase
        tickets.Where(t => t.Type == TicketType.Subtask).Should().BeEmpty();
    }

    [Fact]
    public void RefineStoryIntoTechnicalSubtasks_ShouldDecomposeStory_IntoSixTechnicalSubtasks()
    {
        var story = new TicketItem(
            Id: "STORY-TEST-001",
            ParentEpicId: "EPIC-TEST",
            Title: "Distributed PubSub Engine",
            Description: "High-throughput in-memory pub/sub channels",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Zero-allocation channel buffers"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var subtasks = _engine.RefineStoryIntoTechnicalSubtasks(story);

        subtasks.Should().HaveCount(6);
        subtasks.Should().OnlyContain(s => s.Type == TicketType.Subtask);
        subtasks.Should().OnlyContain(s => s.ParentEpicId == "EPIC-TEST");

        // The first subtask (Arch ADR) must be Ready; downstream subtasks must be in Backlog
        var adrSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.LeadArchitect);
        adrSubtask.Status.Should().Be(TicketStatus.Ready);

        var otherSubtasks = subtasks.Where(s => s.AssigneeRole != AgentRole.LeadArchitect).ToList();
        otherSubtasks.Should().OnlyContain(s => s.Status == TicketStatus.Backlog);
    }
}
