using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Approvals;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class WorkflowApprovalGateTests
{
    private readonly TicketStore _ticketStore = new();
    private readonly AgentEventStream _eventStream = new();
    private readonly EmbeddedVectorMemoryStore _memoryStore = new();

    private (GraphWorkflowExecutor executor, WorkflowApprovalService approvalService) CreateExecutor(bool requireApproval = true)
    {
        var decomp = new WorkDecompositionEngine(_ticketStore);
        var router = new HandoffRouter(_ticketStore, _eventStream);
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: _ticketStore);
        var consol = new MemoryConsolidationEngine(_memoryStore);
        var approvalService = new WorkflowApprovalService(requireUserApproval: requireApproval);

        var executor = new GraphWorkflowExecutor(
            _ticketStore,
            decomp,
            router,
            executionEngine,
            _eventStream,
            consol,
            approvalService: approvalService
        );

        return (executor, approvalService);
    }

    [Fact]
    public void ApprovalService_WhenApprovalRequired_IsGateApprovedReturnsFalseInitially()
    {
        var service = new WorkflowApprovalService(requireUserApproval: true);

        service.IsGateApproved("EPIC-101", ApprovalGateStage.TpmToArchitect).Should().BeFalse();
        service.IsGateApproved("EPIC-101", ApprovalGateStage.ArchitectToCoder).Should().BeFalse();
        service.CurrentPendingRequest.Should().BeNull();
    }

    [Fact]
    public async Task ApprovalService_WhenApprovalDisabled_AutoApprovesImmediately()
    {
        var service = new WorkflowApprovalService(requireUserApproval: false);

        var request = new WorkflowApprovalRequest(
            Id: "APPR-1",
            EpicId: "EPIC-AUTO",
            Stage: ApprovalGateStage.TpmToArchitect,
            GateTitle: "Test Auto Gate",
            GateDescription: "Description",
            NextStepDescription: "Next step",
            PrecedingRole: AgentRole.TechnicalProductManager,
            ProceedingRole: AgentRole.LeadArchitect,
            ItemsToApprove: [],
            Deliverables: []
        );

        var result = await service.RequestApprovalAsync(request);

        result.Status.Should().Be(ApprovalStatus.Approved);
        service.IsGateApproved("EPIC-AUTO", ApprovalGateStage.TpmToArchitect).Should().BeTrue();
        service.CurrentPendingRequest.Should().BeNull();
    }

    [Fact]
    public async Task ApprovalService_Approve_ResolvesPendingRequest()
    {
        var service = new WorkflowApprovalService(requireUserApproval: true);

        var request = new WorkflowApprovalRequest(
            Id: "APPR-TEST-1",
            EpicId: "EPIC-MANUAL",
            Stage: ApprovalGateStage.TpmToArchitect,
            GateTitle: "TPM to Architect Gate",
            GateDescription: "Review PRD",
            NextStepDescription: "Scaffold ADR",
            PrecedingRole: AgentRole.TechnicalProductManager,
            ProceedingRole: AgentRole.LeadArchitect,
            ItemsToApprove: [new ApprovalItemSummary("PRD", "PRD Doc", "Details", ["Point 1"])],
            Deliverables: []
        );

        WorkflowApprovalRequest? requestedEvent = null;
        WorkflowApprovalRequest? resolvedEvent = null;
        service.OnApprovalRequested += req => requestedEvent = req;
        service.OnApprovalResolved += req => resolvedEvent = req;

        var requestTask = service.RequestApprovalAsync(request);

        service.CurrentPendingRequest.Should().NotBeNull();
        service.CurrentPendingRequest!.Id.Should().Be("APPR-TEST-1");
        requestedEvent.Should().NotBeNull();
        requestedEvent!.Id.Should().Be("APPR-TEST-1");

        service.Approve("APPR-TEST-1", "Approved by human ringmaster.");

        var resolved = await requestTask;
        resolved.Status.Should().Be(ApprovalStatus.Approved);
        resolved.UserFeedback.Should().Be("Approved by human ringmaster.");
        service.IsGateApproved("EPIC-MANUAL", ApprovalGateStage.TpmToArchitect).Should().BeTrue();
        service.CurrentPendingRequest.Should().BeNull();
        resolvedEvent.Should().NotBeNull();
        resolvedEvent!.Status.Should().Be(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task ApprovalService_Reject_ResolvesWithRejectedStatusAndFeedback()
    {
        var service = new WorkflowApprovalService(requireUserApproval: true);

        var request = new WorkflowApprovalRequest(
            Id: "APPR-TEST-REJ",
            EpicId: "EPIC-REJ",
            Stage: ApprovalGateStage.ArchitectToCoder,
            GateTitle: "Architect to Coder Gate",
            GateDescription: "Review ADR",
            NextStepDescription: "Coder implementation",
            PrecedingRole: AgentRole.LeadArchitect,
            ProceedingRole: AgentRole.SoftwareDeveloper,
            ItemsToApprove: [],
            Deliverables: []
        );

        var requestTask = service.RequestApprovalAsync(request);

        service.Reject("APPR-TEST-REJ", "Scope too complex. Simplify architecture.");

        var resolved = await requestTask;
        resolved.Status.Should().Be(ApprovalStatus.Rejected);
        resolved.UserFeedback.Should().Be("Scope too complex. Simplify architecture.");
        service.IsGateApproved("EPIC-REJ", ApprovalGateStage.ArchitectToCoder).Should().BeFalse();
        service.CurrentPendingRequest.Should().BeNull();
    }

    [Fact(Timeout = 15000)]
    public async Task Workflow_Gate1_And_Gate2_FullApprovalLifecycle_ShouldSucceed()
    {
        var (executor, approvalService) = CreateExecutor(requireApproval: true);

        // Start workflow asynchronously
        var workflowTask = executor.ExecuteWorkflowAsync(
            "Implement Resilient Cache Tier",
            "Zero-allocation in-memory cache with LRU eviction policy"
        );

        // 1. Wait for Gate 1: TPM -> Lead Architect
        while (approvalService.CurrentPendingRequest == null)
        {
            await Task.Delay(15);
        }

        var gate1 = approvalService.CurrentPendingRequest;
        gate1.Should().NotBeNull();
        gate1!.Stage.Should().Be(ApprovalGateStage.TpmToArchitect);
        gate1.GateTitle.Should().Contain("PRD & User Story");
        gate1.PrecedingRole.Should().Be(AgentRole.TechnicalProductManager);
        gate1.ProceedingRole.Should().Be(AgentRole.LeadArchitect);
        gate1.ItemsToApprove.Should().NotBeEmpty();
        gate1.ItemsToApprove.Should().Contain(i => i.Category.Contains("PRD"));
        gate1.ItemsToApprove.Should().Contain(i => i.Category.Contains("User Story"));
        gate1.Deliverables.Should().NotBeEmpty();

        var tpmNode = executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.TechnicalProductManager);
        tpmNode.State.Should().Be(NodeExecutionState.WaitingForApproval);

        // Approve Gate 1
        approvalService.Approve(gate1.Id, "PRD approved by Lead Engineer.");

        // 2. Wait for Gate 2: Lead Architect -> Coder (SoftwareDeveloper)
        while (approvalService.CurrentPendingRequest == null)
        {
            await Task.Delay(15);
        }

        var gate2 = approvalService.CurrentPendingRequest;
        gate2.Should().NotBeNull();
        gate2!.Stage.Should().Be(ApprovalGateStage.ArchitectToCoder);
        gate2.GateTitle.Should().Contain("Architecture Design");
        gate2.PrecedingRole.Should().Be(AgentRole.LeadArchitect);
        gate2.ProceedingRole.Should().Be(AgentRole.SoftwareDeveloper);
        gate2.ItemsToApprove.Should().NotBeEmpty();
        gate2.ItemsToApprove.Should().Contain(i => i.Category.Contains("ADR"));
        gate2.ItemsToApprove.Should().Contain(i => i.Category.Contains("Technical Subtask"));

        var devNode = executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.SoftwareDeveloper);
        devNode.State.Should().Be(NodeExecutionState.WaitingForApproval);

        // Approve Gate 2
        approvalService.Approve(gate2.Id, "Architecture approved. Unleash the coder!");

        // Workflow should now finish to completion
        var workflowSuccess = await workflowTask;
        workflowSuccess.Should().BeTrue();

        executor.CurrentGraph.Nodes.Should().OnlyContain(n => n.State == NodeExecutionState.Completed);
        var subtasks = _ticketStore.GetAllTickets().Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.Should().NotBeEmpty();
        subtasks.Should().OnlyContain(t => t.Status == TicketStatus.Done);
    }

    [Fact(Timeout = 15000)]
    public async Task Workflow_Gate1_Rejection_ShouldHaltWorkflowBeforeArchitectRuns()
    {
        var (executor, approvalService) = CreateExecutor(requireApproval: true);

        var workflowTask = executor.ExecuteWorkflowAsync(
            "Implement Experimental Feature",
            "Risky refactor of core loop"
        );

        // Wait for Gate 1
        while (approvalService.CurrentPendingRequest == null)
        {
            await Task.Delay(15);
        }

        var gate1 = approvalService.CurrentPendingRequest;
        gate1.Should().NotBeNull();
        gate1!.Stage.Should().Be(ApprovalGateStage.TpmToArchitect);

        // Reject Gate 1
        approvalService.Reject(gate1.Id, "Business priority shifted. Initiative canceled.");

        var workflowSuccess = await workflowTask;
        workflowSuccess.Should().BeFalse();

        // Lead Architect and Coder subtasks should not have been executed
        var allTickets = _ticketStore.GetAllTickets();
        var subtasks = allTickets.Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.Should().BeEmpty();
    }

    [Fact(Timeout = 15000)]
    public async Task Workflow_Gate2_Rejection_ShouldHaltWorkflowBeforeCoderRuns()
    {
        var (executor, approvalService) = CreateExecutor(requireApproval: true);

        var workflowTask = executor.ExecuteWorkflowAsync(
            "Implement Complex Event Sourcing",
            "Full event-sourcing with Kafka projection"
        );

        // Wait for Gate 1 and approve
        while (approvalService.CurrentPendingRequest == null)
        {
            await Task.Delay(15);
        }
        approvalService.Approve(approvalService.CurrentPendingRequest!.Id, "PRD looks good.");

        // Wait for Gate 2
        while (approvalService.CurrentPendingRequest == null)
        {
            await Task.Delay(15);
        }

        var gate2 = approvalService.CurrentPendingRequest;
        gate2.Should().NotBeNull();
        gate2!.Stage.Should().Be(ApprovalGateStage.ArchitectToCoder);

        // Reject Gate 2
        approvalService.Reject(gate2.Id, "ADR rejected. Do not use Kafka; use local Channels.");

        var workflowSuccess = await workflowTask;
        workflowSuccess.Should().BeFalse();

        // Coder subtasks should remain unexecuted (not Done)
        var devSubtasks = _ticketStore.GetAllTickets()
            .Where(t => t.Type == TicketType.Subtask && t.AssigneeRole == AgentRole.SoftwareDeveloper)
            .ToList();

        devSubtasks.Should().NotBeEmpty();
        devSubtasks.Should().OnlyContain(t => t.Status != TicketStatus.Done);
    }
}
