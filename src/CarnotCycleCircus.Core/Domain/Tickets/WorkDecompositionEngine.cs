using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Tickets;

public interface IWorkDecompositionEngine
{
    IReadOnlyList<TicketItem> DeconstructEpic(
        string epicTitle,
        string epicDescription,
        TicketPriority priority = TicketPriority.High);

    IReadOnlyList<TicketItem> DeconstructStoryIntoTechnicalSubtasks(
        TicketItem userStory);
}

public class WorkDecompositionEngine : IWorkDecompositionEngine
{
    private readonly ITicketStore _ticketStore;

    public WorkDecompositionEngine(ITicketStore ticketStore)
    {
        _ticketStore = ticketStore;
    }

    public IReadOnlyList<TicketItem> DeconstructEpic(
        string epicTitle,
        string epicDescription,
        TicketPriority priority = TicketPriority.High)
    {
        var epicId = $"EPIC-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var createdTickets = new List<TicketItem>();

        // 1. Create the Epic ticket
        var epicTicket = new TicketItem(
            Id: epicId,
            ParentEpicId: null,
            Title: epicTitle,
            Description: epicDescription,
            Type: TicketType.Epic,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: priority,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: [
                "All user stories and subtasks are implemented and verified.",
                "Security STRIDE review passes with 0 critical or high findings.",
                "Performance benchmarks satisfy latency and zero-allocation requirements.",
                "Quality assurance test suite achieves 100% pass rate on acceptance criteria."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["EpicType"] = "EngineeringDecomposition" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(epicTicket);
        createdTickets.Add(epicTicket);

        // 2. TPM generates primary User Stories / Feature Tickets under this Epic
        var story1Id = $"STORY-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var story1 = new TicketItem(
            Id: story1Id,
            ParentEpicId: epicId,
            Title: $"{epicTitle}: Core Engine & Protocols",
            Description: $"TPM Requirement: Implement foundational capabilities, interfaces, and state lifecycle for {epicTitle}.",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: priority,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: [
                "Domain models and value objects are immutable records.",
                "Public API contracts are strongly typed and documented.",
                "Handles asynchronous cancellation tokens and thread-safe execution."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["StoryNumber"] = "1" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(story1);
        createdTickets.Add(story1);

        // 3. Lead Architect immediately deconstructs Story into granular technical subtasks
        var subtasks = DeconstructStoryIntoTechnicalSubtasks(story1);
        createdTickets.AddRange(subtasks);

        return createdTickets;
    }

    public IReadOnlyList<TicketItem> DeconstructStoryIntoTechnicalSubtasks(TicketItem userStory)
    {
        var subtasks = new List<TicketItem>();
        var parentEpicId = userStory.ParentEpicId ?? userStory.Id;

        // Subtask 1: Architecture Design & ADR
        var adrSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var adrSubtask = new TicketItem(
            Id: adrSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Arch] Design ADR & System Boundaries for {userStory.Title}",
            Description: "Lead Architect produces Nygard Architectural Decision Record, defining domain boundaries, zero-allocation protocols, and resilience policies.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: userStory.DependsOnTicketIds,
            AcceptanceCriteria: [
                "ADR documents context, decision, alternatives, positive and negative trade-offs.",
                "Defines immutable records, value objects, and cancellation token contracts.",
                "Approved and registered in ADR Document Hub."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "Architecture" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(adrSubtask);
        subtasks.Add(adrSubtask);

        // Subtask 2: Implementation & Unit Tests
        var devSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var devSubtask = new TicketItem(
            Id: devSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Dev] Implement Feature & Unit Tests for {userStory.Title}",
            Description: "Senior Developer writes C# 13 / .NET 10 implementation, validates syntax, and creates comprehensive unit tests.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [adrSubtaskId],
            AcceptanceCriteria: [
                "Code follows clean modern C# standards and compiles cleanly.",
                "Implements required business logic with zero-allocation considerations.",
                "Accompanied by xUnit test suite."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "Implementation" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(devSubtask);
        subtasks.Add(devSubtask);

        // Subtask 3: Security Review & STRIDE Threat Model
        var secSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var secSubtask = new TicketItem(
            Id: secSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Security] STRIDE Threat Model & Vulnerability Audit for {userStory.Title}",
            Description: "Security Engineer analyzes code for secret exposure, permission boundaries, input validation, and STRIDE threats.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SecurityEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [devSubtaskId],
            AcceptanceCriteria: [
                "STRIDE threat model completed and documented.",
                "No secret leakage or unchecked input vectors.",
                "Security signoff or remediation reject packet issued."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "Security" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(secSubtask);
        subtasks.Add(secSubtask);

        // Subtask 4: Optimization & Performance Profiling
        var optSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var optSubtask = new TicketItem(
            Id: optSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Opt] Latency Bottleneck & Allocation Audit for {userStory.Title}",
            Description: "Optimization Engineer reviews hot paths, audits heap allocations, memory spans, and asymptotic time complexity.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.OptimizationEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [devSubtaskId],
            AcceptanceCriteria: [
                "Memory allocations audited and hot-path allocations minimized.",
                "Zero-allocation Span/Memory patterns applied where critical.",
                "Performance review signoff recorded."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "Optimization" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(optSubtask);
        subtasks.Add(optSubtask);

        // Subtask 5: QA Test Strategy & Acceptance Validation
        var qaSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var qaSubtask = new TicketItem(
            Id: qaSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[QA] Test Strategy & Final Acceptance Validation for {userStory.Title}",
            Description: "Principal QA Analyst validates all acceptance criteria, executes automated test suites, verifies edge cases, and certifies release readiness.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.PrincipalQAAnalyst,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [secSubtaskId, optSubtaskId],
            AcceptanceCriteria: [
                "100% acceptance criteria validated against actual deliverables.",
                "Automated test runner confirms passing test suite.",
                "Final quality scorecard approved."
            ],
            Deliverables: Array.Empty<Events.ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "QA" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(qaSubtask);
        subtasks.Add(qaSubtask);

        return subtasks;
    }
}
