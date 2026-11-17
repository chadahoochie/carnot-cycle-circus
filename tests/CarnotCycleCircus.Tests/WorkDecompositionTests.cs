using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
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

        result.ShouldNotBeEmpty();
        var epic = result.First(t => t.Type == TicketType.Epic);
        epic.Title.ShouldBe("Build Distributed PubSub");

        var researchSpike = result.First(t => t.Type == TicketType.ResearchSpike);
        researchSpike.AssigneeRole.ShouldBe(AgentRole.RequirementsResearcher);
        researchSpike.Status.ShouldBe(TicketStatus.Ready);
        researchSpike.DependsOnTicketIds.ShouldBeEmpty();

        var featureStory = result.First(t => t.Type == TicketType.Feature);
        featureStory.AssigneeRole.ShouldBe(AgentRole.TechnicalProductManager);
        featureStory.Status.ShouldBe(TicketStatus.Backlog);
        featureStory.DependsOnTicketIds.ShouldContain(researchSpike.Id);

        var subtasks = result.Where(t => t.Type == TicketType.Subtask).ToList();
        subtasks.Count().ShouldBe(6);

        // Verify role coverage
        new[]
        {
            AgentRole.LeadArchitect,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            AgentRole.OptimizationEngineer,
            AgentRole.PrincipalQAAnalyst,
            AgentRole.IntegrationEngineer
        }.ShouldBeSubsetOf(subtasks.Select(s => s.AssigneeRole));

        // Verify CLAW ordering: Arch depends on Feature Story, Dev depends on Arch, Security & Opt depend on Dev, QA depends on Sec & Opt, Integration depends on QA
        var archSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.LeadArchitect);
        var devSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.SoftwareDeveloper);
        var secSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.SecurityEngineer);
        var optSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.OptimizationEngineer);
        var qaSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.PrincipalQAAnalyst);
        var intSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.IntegrationEngineer);

        archSubtask.DependsOnTicketIds.ShouldContain(featureStory.Id);
        devSubtask.DependsOnTicketIds.ShouldContain(archSubtask.Id);
        secSubtask.DependsOnTicketIds.ShouldContain(devSubtask.Id);
        optSubtask.DependsOnTicketIds.ShouldContain(devSubtask.Id);
        qaSubtask.DependsOnTicketIds.ShouldContain(secSubtask.Id);
        qaSubtask.DependsOnTicketIds.ShouldContain(optSubtask.Id);
        intSubtask.DependsOnTicketIds.ShouldContain(qaSubtask.Id);
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

        tickets.Count().ShouldBe(3);
        var epic = tickets.First(t => t.Type == TicketType.Epic);
        epic.Deliverables.ShouldContain(d => d.Name == "RESEARCH_BRIEF.md");
        epic.AssigneeRole.ShouldBe(AgentRole.TechnicalProductManager);

        var research = tickets.First(t => t.Type == TicketType.ResearchSpike);
        research.Status.ShouldBe(TicketStatus.Done);
        research.AssigneeRole.ShouldBe(AgentRole.RequirementsResearcher);

        var story = tickets.First(t => t.Type == TicketType.Feature);
        story.ParentEpicId.ShouldBe(epic.Id);
        story.AssigneeRole.ShouldBe(AgentRole.TechnicalProductManager);
        story.Status.ShouldBe(TicketStatus.Ready);
        story.CreatedByRole.ShouldBe(AgentRole.TechnicalProductManager);

        // Ensure subtasks are NOT prematurely created during TPM story generation phase
        tickets.Where(t => t.Type == TicketType.Subtask).ShouldBeEmpty();
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
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Zero-allocation channel buffers"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var subtasks = _engine.RefineStoryIntoTechnicalSubtasks(story);

        subtasks.Count().ShouldBe(6);
        subtasks.ShouldAllBe(s => s.Type == TicketType.Subtask);
        subtasks.ShouldAllBe(s => s.ParentEpicId == "EPIC-TEST");

        // The first subtask (Arch ADR) must be Ready; downstream subtasks must be in Backlog
        var adrSubtask = subtasks.First(s => s.AssigneeRole == AgentRole.LeadArchitect);
        adrSubtask.Status.ShouldBe(TicketStatus.Ready);

        var otherSubtasks = subtasks.Where(s => s.AssigneeRole != AgentRole.LeadArchitect).ToList();
        otherSubtasks.ShouldAllBe(s => s.Status == TicketStatus.Backlog);
    }

    [Fact]
    public void SyncUserStoriesFromPrd_WithJsonManifest_ShouldExtractMultipleFeatureStories()
    {
        var epicId = "EPIC-TEST-MULTI";
        var epicTicket = new TicketItem(
            Id: epicId,
            ParentEpicId: null,
            Title: "High-Throughput Telemetry Pipeline",
            Description: "Real-time telemetry engine",
            Type: TicketType.Epic,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Process events at sub-5ms"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(epicTicket);

        // Initial single placeholder story
        var initialStory = new TicketItem(
            Id: "STORY-INIT-001",
            ParentEpicId: epicId,
            Title: "High-Throughput Telemetry Pipeline: Core Engine & Protocols",
            Description: "Initial placeholder story",
            Type: TicketType.Feature,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Initial criteria"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(initialStory);

        var prdContent = """
        # Product Requirements Document (PRD): High-Throughput Telemetry Pipeline
        ## 1. Executive Summary
        Building scalable telemetry pipeline.
        ## 4. User Stories
        Detailed text...
        ```json:user_stories
        [
          {
            "title": "Ingestion Channel & Backpressure",
            "description": "Bounded channel queue with backpressure",
            "acceptanceCriteria": ["Sub-5ms latency", "Drop policy on overflow"]
          },
          {
            "title": "Payload Validation & Anomaly Scoring",
            "description": "Zero-allocation schema validator",
            "acceptanceCriteria": ["Reject invalid schemas", "Zero heap allocations"]
          },
          {
            "title": "Storage Outbox & Real-time Stream",
            "description": "Durable persistence and SignalR stream",
            "acceptanceCriteria": ["At-least-once outbox delivery", "SignalR client broadcast"]
          }
        ]
        ```
        """;

        var result = _engine.SyncUserStoriesFromPrd(epicId, prdContent);

        result.Count().ShouldBe(3);
        result.ShouldAllBe(s => s.Type == TicketType.Feature);
        result.ShouldAllBe(s => s.ParentEpicId == epicId);
        result.ShouldAllBe(s => s.Status == TicketStatus.Done);

        // Story 1 upgraded existing ticket ID
        result[0].Id.ShouldBe("STORY-INIT-001");
        result[0].Title.ShouldContain("Ingestion Channel & Backpressure");
        result[0].AcceptanceCriteria.ShouldContain("Sub-5ms latency");

        // Stories 2 and 3 created as new tickets
        result[1].Title.ShouldContain("Payload Validation & Anomaly Scoring");
        result[2].Title.ShouldContain("Storage Outbox & Real-time Stream");
    }

    [Fact]
    public void SyncUserStoriesFromPrd_WithMarkdownHeadings_ShouldExtractMultipleFeatureStories()
    {
        var epicId = "EPIC-TEST-MD";
        var epicTicket = new TicketItem(
            Id: epicId,
            ParentEpicId: null,
            Title: "Distributed Order Saga",
            Description: "Order saga orchestrator",
            Type: TicketType.Epic,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Saga compensation"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(epicTicket);

        var prdContent = """
        # Product Requirements Document (PRD): Distributed Order Saga
        ## 4. User Stories & Functional Acceptance Criteria

        ### User Story 1: Saga State Coordinator
        - Description: Coordinate multi-stage order workflow with idempotent compensation.
        - Acceptance Criteria:
          - [ ] State persisted with optimistic locking
          - [ ] Timeout compensation triggers after 30s

        ### User Story 2: Payment Gateway Adapter
        - Description: Secure adapter communicating with third-party payment APIs.
        - Acceptance Criteria:
          - [ ] Idempotency key included on all POST calls
          - [ ] Zero secret leakage in logs
        """;

        var result = _engine.SyncUserStoriesFromPrd(epicId, prdContent);

        result.Count().ShouldBe(2);
        result[0].Title.ShouldContain("Saga State Coordinator");
        result[0].AcceptanceCriteria.ShouldContain("State persisted with optimistic locking");
        result[1].Title.ShouldContain("Payment Gateway Adapter");
        result[1].AcceptanceCriteria.ShouldContain("Zero secret leakage in logs");
    }
}
