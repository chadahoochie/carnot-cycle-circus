using CarnotCycleCircus.Core.Domain.Agents;
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
}
