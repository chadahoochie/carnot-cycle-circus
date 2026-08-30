using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;

namespace CarnotCycleCircus.Core.Domain.Tickets;

public interface IWorkDecompositionEngine
{
    /// <summary>
    /// Phase 1 (Product Manager & Research Analyst): Deconstructs an Epic into foundational User Stories (Features) with PRD context.
    /// </summary>
    IReadOnlyList<TicketItem> DeconstructEpicIntoUserStories(
        string epicTitle,
        string epicDescription,
        ArtifactItem? researchBrief = null,
        TicketPriority priority = TicketPriority.High);

    /// <summary>
    /// Phase 2 (Lead Architect): Grooms and refines a User Story into granular technical execution subtasks
    /// (Architecture, Dev, Security, Optimization, QA, Integration) prior to architecture design.
    /// </summary>
    IReadOnlyList<TicketItem> RefineStoryIntoTechnicalSubtasks(
        TicketItem userStory,
        IReadOnlyList<ArtifactItem>? upstreamContext = null);

    /// <summary>
    /// Full-cycle decomposition combining story generation and technical refinement.
    /// </summary>
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

    public IReadOnlyList<TicketItem> DeconstructEpicIntoUserStories(
        string epicTitle,
        string epicDescription,
        ArtifactItem? researchBrief = null,
        TicketPriority priority = TicketPriority.High)
    {
        var existingEpic = _ticketStore.GetAllTickets().FirstOrDefault(t => t.Type == TicketType.Epic && string.Equals(t.Title, epicTitle, StringComparison.OrdinalIgnoreCase));
        var epicId = existingEpic?.Id ?? $"EPIC-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var createdTickets = new List<TicketItem>();

        // 1. Create or update the Epic ticket
        var epicTicket = existingEpic ?? new TicketItem(
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
            Deliverables: researchBrief != null ? [researchBrief] : Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["EpicType"] = "EngineeringDecomposition" },
            CreatedAt: DateTimeOffset.UtcNow
        );

        if (existingEpic == null)
        {
            _ticketStore.CreateTicket(epicTicket);
        }
        else if (researchBrief != null && !epicTicket.Deliverables.Any(d => d.Name == researchBrief.Name))
        {
            epicTicket = epicTicket.WithDeliverable(researchBrief);
            _ticketStore.UpdateTicket(epicTicket);
        }
        createdTickets.Add(epicTicket);

        // Check if feature stories already exist for this epic
        var existingStories = _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
        if (existingStories.Count > 0)
        {
            createdTickets.AddRange(existingStories);
            return createdTickets;
        }

        // 2. TPM generates primary User Stories / Feature Tickets under this Epic
        var story1Id = $"STORY-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var story1 = new TicketItem(
            Id: story1Id,
            ParentEpicId: epicId,
            Title: $"{epicTitle}: Core Engine & Protocols",
            Description: $"TPM Requirement: Implement foundational capabilities, domain models, interfaces, and state lifecycle for {epicTitle}.",
            Type: TicketType.Feature,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: priority,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: [
                "Domain models and value objects are immutable records.",
                "Public API contracts are strongly typed and documented.",
                "Handles asynchronous cancellation tokens and thread-safe execution."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["StoryNumber"] = "1" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(story1);
        createdTickets.Add(story1);

        return createdTickets;
    }

    public IReadOnlyList<TicketItem> DeconstructEpic(
        string epicTitle,
        string epicDescription,
        TicketPriority priority = TicketPriority.High)
    {
        var storyTickets = DeconstructEpicIntoUserStories(epicTitle, epicDescription, null, priority);
        var allTickets = new List<TicketItem>(storyTickets);

        foreach (var story in storyTickets.Where(t => t.Type == TicketType.Feature))
        {
            var subtasks = RefineStoryIntoTechnicalSubtasks(story);
            allTickets.AddRange(subtasks);
        }

        return allTickets;
    }

    public IReadOnlyList<TicketItem> DeconstructStoryIntoTechnicalSubtasks(TicketItem userStory) =>
        RefineStoryIntoTechnicalSubtasks(userStory);

    public IReadOnlyList<TicketItem> RefineStoryIntoTechnicalSubtasks(
        TicketItem userStory,
        IReadOnlyList<ArtifactItem>? upstreamContext = null)
    {
        var subtasks = new List<TicketItem>();
        var parentEpicId = userStory.ParentEpicId ?? userStory.Id;

        // Subtask 1: Architecture Design & Clean Architecture Scaffolding
        var adrSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var adrSubtask = new TicketItem(
            Id: adrSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Arch] Design ADR & Scaffold Clean Architecture for {userStory.Title}",
            Description: $"Lead Architect produces Nygard Architectural Decision Record, defining domain boundaries, zero-allocation protocols, and scaffolds the Clean Architecture solution (Domain, Contracts, DI extensions) for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: userStory.DependsOnTicketIds,
            AcceptanceCriteria: [
                "ADR documents context, decision, alternatives, positive and negative trade-offs.",
                "Scaffolds Clean Architecture solution structure: Domain immutable records, Application contracts/interfaces, and DI extensions.",
                "Approved and registered in ADR Document Hub."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
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
            Title: $"[Dev] Implement Domain Models, Service & Tests for {userStory.Title}",
            Description: $"Senior Developer writes C# 13 / .NET 10 multi-file implementation bundle (Models, Interfaces, Services, DI extensions, and Unit Tests) matching Lead Architect's ADR for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [adrSubtaskId],
            AcceptanceCriteria: [
                "Code strictly implements the exact types and interfaces specified in the Lead Architect ADR.",
                "Implements required business logic with zero heap allocations on hot paths.",
                "Accompanied by complete xUnit unit test suite verifying acceptance criteria."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
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
            Title: $"[Security] STRIDE Threat Model & Code Audit for {userStory.Title}",
            Description: $"Security Engineer audits delivered C# source code for secret exposure, permission boundaries, buffer slices, input sanitization, and STRIDE threats for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SecurityEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [devSubtaskId],
            AcceptanceCriteria: [
                "STRIDE threat model completed against actual delivered code.",
                "No secret leakage or unchecked input vectors in service methods.",
                "Security signoff or remediation reject packet issued."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
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
            Description: $"Optimization Engineer benchmarks delivered service methods, auditing heap allocations, memory spans, ValueTask state machines, and asymptotic time complexity for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.OptimizationEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [devSubtaskId],
            AcceptanceCriteria: [
                "Memory allocations audited on actual service methods and hot-path allocations minimized.",
                "Zero-allocation Span/Memory patterns verified on hot paths.",
                "BenchmarkDotNet report and performance signoff recorded."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
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
            Description: $"Principal QA Analyst validates all acceptance criteria against delivered code and unit tests, confirms test execution pass rate, and certifies release readiness for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.PrincipalQAAnalyst,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [secSubtaskId, optSubtaskId],
            AcceptanceCriteria: [
                "100% acceptance criteria mapped and validated against unit tests and code.",
                "Automated test verification confirms passing test suite.",
                "Final quality scorecard approved."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "QA" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(qaSubtask);
        subtasks.Add(qaSubtask);

        // Subtask 6: Solution Packaging & Repository Integration
        var intSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var intSubtask = new TicketItem(
            Id: intSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Integration] Solution Packaging & Repository Integration for {userStory.Title}",
            Description: $"Integration & Release Engineer packages multi-file deliverables into Clean Architecture project folders, updates .csproj and .slnx solution files, wires DI into Program.cs, and publishes Release Manifest for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.IntegrationEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [qaSubtaskId],
            AcceptanceCriteria: [
                "Multi-file artifacts mapped and placed into Clean Architecture folder structure.",
                "Project and solution files (.slnx, .csproj, CPM) properly wired and compilable.",
                "Release manifest and integrated solution package generated."
            ],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "Integration" },
            CreatedAt: DateTimeOffset.UtcNow
        );
        _ticketStore.CreateTicket(intSubtask);
        subtasks.Add(intSubtask);

        return subtasks;
    }
}
