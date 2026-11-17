using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Approvals;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
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

        service.IsGateApproved("EPIC-101", ApprovalGateStage.TpmToArchitect).ShouldBeFalse();
        service.IsGateApproved("EPIC-101", ApprovalGateStage.ArchitectToCoder).ShouldBeFalse();
        service.CurrentPendingRequest.ShouldBeNull();
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

        result.Status.ShouldBe(ApprovalStatus.Approved);
        service.IsGateApproved("EPIC-AUTO", ApprovalGateStage.TpmToArchitect).ShouldBeTrue();
        service.CurrentPendingRequest.ShouldBeNull();
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

        service.CurrentPendingRequest.ShouldNotBeNull();
        service.CurrentPendingRequest!.Id.ShouldBe("APPR-TEST-1");
        requestedEvent.ShouldNotBeNull();
        requestedEvent!.Id.ShouldBe("APPR-TEST-1");

        service.Approve("APPR-TEST-1", "Approved by human ringmaster.");

        var resolved = await requestTask;
        resolved.Status.ShouldBe(ApprovalStatus.Approved);
        resolved.UserFeedback.ShouldBe("Approved by human ringmaster.");
        service.IsGateApproved("EPIC-MANUAL", ApprovalGateStage.TpmToArchitect).ShouldBeTrue();
        service.CurrentPendingRequest.ShouldBeNull();
        resolvedEvent.ShouldNotBeNull();
        resolvedEvent!.Status.ShouldBe(ApprovalStatus.Approved);
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
        resolved.Status.ShouldBe(ApprovalStatus.Rejected);
        resolved.UserFeedback.ShouldBe("Scope too complex. Simplify architecture.");
        service.IsGateApproved("EPIC-REJ", ApprovalGateStage.ArchitectToCoder).ShouldBeFalse();
        service.CurrentPendingRequest.ShouldBeNull();
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
        gate1.ShouldNotBeNull();
        gate1!.Stage.ShouldBe(ApprovalGateStage.TpmToArchitect);
        gate1.GateTitle.ShouldContain("PRD & User Story");
        gate1.PrecedingRole.ShouldBe(AgentRole.TechnicalProductManager);
        gate1.ProceedingRole.ShouldBe(AgentRole.LeadArchitect);
        gate1.ItemsToApprove.ShouldNotBeEmpty();
        gate1.ItemsToApprove.ShouldContain(i => i.Category.Contains("PRD"));
        gate1.ItemsToApprove.ShouldContain(i => i.Category.Contains("User Story"));
        gate1.Deliverables.ShouldNotBeEmpty();

        var tpmNode = executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.TechnicalProductManager);
        tpmNode.State.ShouldBe(NodeExecutionState.WaitingForApproval);

        // Approve Gate 1
        approvalService.Approve(gate1.Id, "PRD approved by Lead Engineer.");

        // 2. Wait for Gate 2: Lead Architect -> Coder (SoftwareDeveloper)
        while (approvalService.CurrentPendingRequest == null)
        {
            await Task.Delay(15);
        }

        var gate2 = approvalService.CurrentPendingRequest;
        gate2.ShouldNotBeNull();
        gate2!.Stage.ShouldBe(ApprovalGateStage.ArchitectToCoder);
        gate2.GateTitle.ShouldContain("Architecture Design");
        gate2.PrecedingRole.ShouldBe(AgentRole.LeadArchitect);
        gate2.ProceedingRole.ShouldBe(AgentRole.SoftwareDeveloper);
        gate2.ItemsToApprove.ShouldNotBeEmpty();
        gate2.ItemsToApprove.ShouldContain(i => i.Category.Contains("ADR"));
        gate2.ItemsToApprove.ShouldContain(i => i.Category.Contains("Technical Subtask"));

        var devNode = executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.SoftwareDeveloper);
        devNode.State.ShouldBe(NodeExecutionState.WaitingForApproval);

        // Approve Gate 2
        approvalService.Approve(gate2.Id, "Architecture approved. Unleash the coder!");

        // Workflow should now finish to completion
        var workflowSuccess = await workflowTask;
        workflowSuccess.ShouldBeTrue();

        executor.CurrentGraph.Nodes.ShouldAllBe(n => n.State == NodeExecutionState.Completed);
        var subtasks = _ticketStore.GetAllTickets().Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.ShouldNotBeEmpty();
        subtasks.ShouldAllBe(t => t.Status == TicketStatus.Done);
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
        gate1.ShouldNotBeNull();
        gate1!.Stage.ShouldBe(ApprovalGateStage.TpmToArchitect);

        // Reject Gate 1
        approvalService.Reject(gate1.Id, "Business priority shifted. Initiative canceled.");

        var workflowSuccess = await workflowTask;
        workflowSuccess.ShouldBeFalse();

        // Lead Architect and Coder subtasks should not have been executed
        var allTickets = _ticketStore.GetAllTickets();
        var subtasks = allTickets.Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.ShouldBeEmpty();

        var epic = allTickets.FirstOrDefault(t => t.Type == TicketType.Epic);
        epic.ShouldNotBeNull();
        epic!.Status.ShouldBe(TicketStatus.Review);
        epic.AssigneeRole.ShouldBe(AgentRole.EndUser);
        epic.Metadata.ShouldContainKey("RejectionReason");
        epic.Metadata["RejectionReason"].ShouldBe("Business priority shifted. Initiative canceled.");
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
        gate2.ShouldNotBeNull();
        gate2!.Stage.ShouldBe(ApprovalGateStage.ArchitectToCoder);

        // Reject Gate 2
        approvalService.Reject(gate2.Id, "ADR rejected. Do not use Kafka; use local Channels.");

        var workflowSuccess = await workflowTask;
        workflowSuccess.ShouldBeFalse();

        // Developer subtask should remain unexecuted (not Done) and routed to In Review assigned to EndUser
        var devOrReviewSubtasks = _ticketStore.GetAllTickets()
            .Where(t => t.Type == TicketType.Subtask && t.AssigneeRole is AgentRole.EndUser or AgentRole.SoftwareDeveloper)
            .ToList();

        devOrReviewSubtasks.ShouldNotBeEmpty();
        devOrReviewSubtasks.ShouldAllBe(t => t.Status != TicketStatus.Done);
        devOrReviewSubtasks.ShouldContain(t => t.Status == TicketStatus.Review && t.AssigneeRole == AgentRole.EndUser);
        var rejectedTicket = devOrReviewSubtasks.First(t => t.Status == TicketStatus.Review && t.AssigneeRole == AgentRole.EndUser);
        rejectedTicket.Metadata.ShouldContainKey("RejectionReason");
        rejectedTicket.Metadata["RejectionReason"].ShouldBe("ADR rejected. Do not use Kafka; use local Channels.");
    }

    [Fact]
    public void AgentRole_EndUser_ShouldHaveCorrectMetadataAndDefaults()
    {
        AgentRole.EndUser.ToEmoji().ShouldBe("👤");
        AgentRole.EndUser.ToColorHex().ShouldBe("#f59e0b");
        AgentRole.EndUser.ToDisplayName().ShouldBe("End User");

        var persona = AgentPersona.CreateDefault(AgentRole.EndUser);
        persona.Name.ShouldBe("Major Korben \"Meat-Popsicle\" Dallas (End User)");
        persona.Role.ShouldBe(AgentRole.EndUser);
        persona.SystemPrompt.ShouldContain("meat popsicle");

        var generator = new AgentNameGenerator();
        var suggestedName = generator.GenerateSuggestedName(AgentRole.EndUser);
        suggestedName.ShouldNotBeNullOrWhiteSpace();
        suggestedName.ShouldContain("(End User)");
    }

    [Fact]
    public async Task ApprovalService_RejectedRequest_ShouldBeRetained_AndCanBeReApproved()
    {
        var service = new WorkflowApprovalService(true);
        var request = new WorkflowApprovalRequest(
            Id: "GATE-REJ-01",
            EpicId: "EPIC-TEST-01",
            ProjectId: "proj-123",
            Stage: ApprovalGateStage.TpmToArchitect,
            GateTitle: "Initial Gate",
            GateDescription: "Description",
            NextStepDescription: "Next",
            PrecedingRole: AgentRole.TechnicalProductManager,
            ProceedingRole: AgentRole.LeadArchitect,
            ItemsToApprove: [],
            Deliverables: [],
            CreatedAt: DateTimeOffset.UtcNow
        );

        var task = service.RequestApprovalAsync(request);

        service.CurrentPendingRequest.ShouldNotBeNull();
        service.Reject("GATE-REJ-01", "Requires revision on criteria 3.");

        var rejectedRes = await task;
        rejectedRes.Status.ShouldBe(ApprovalStatus.Rejected);
        rejectedRes.UserFeedback.ShouldBe("Requires revision on criteria 3.");

        // Verify rejected retention
        service.CurrentPendingRequest.ShouldBeNull();
        service.RejectedRequests.Count().ShouldBe(1);
        service.GetRequestById("GATE-REJ-01").ShouldNotBeNull();
        service.GetRequestById("GATE-REJ-01")!.Status.ShouldBe(ApprovalStatus.Rejected);
        service.GetLatestPendingOrRejectedRequestForProject("proj-123").ShouldNotBeNull();
        service.GetLatestPendingOrRejectedRequestForProject("proj-123")!.Id.ShouldBe("GATE-REJ-01");
        service.IsGateApproved("EPIC-TEST-01", ApprovalGateStage.TpmToArchitect).ShouldBeFalse();

        // Now re-approve the previously rejected request
        WorkflowApprovalRequest? resolvedEvent = null;
        service.OnApprovalResolved += r => resolvedEvent = r;

        var approved = service.Approve("GATE-REJ-01", "Revised criteria accepted.");
        approved.ShouldBeTrue();

        service.RejectedRequests.ShouldBeEmpty();
        service.IsGateApproved("EPIC-TEST-01", ApprovalGateStage.TpmToArchitect).ShouldBeTrue();
        resolvedEvent.ShouldNotBeNull();
        resolvedEvent!.Status.ShouldBe(ApprovalStatus.Approved);
        resolvedEvent.UserFeedback.ShouldBe("Revised criteria accepted.");
    }
}
