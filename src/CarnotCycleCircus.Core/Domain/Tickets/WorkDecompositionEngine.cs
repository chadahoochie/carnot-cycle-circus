using System.Text.Json;
using System.Text.RegularExpressions;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;

namespace CarnotCycleCircus.Core.Domain.Tickets;

public record ParsedUserStoryDto(
    string Title,
    string Description,
    IReadOnlyList<string> AcceptanceCriteria
);

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
    /// Phase 1B (Product Manager): Dynamically extracts modular User Stories from the authored PRD (or rich Research Brief),
    /// synchronizing them into the ticket store so the Lead Architect can refine each discrete feature.
    /// </summary>
    IReadOnlyList<TicketItem> SyncUserStoriesFromPrd(
        string epicId,
        string prdContent,
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
                "Requirements research and technical feasibility verified.",
                "PRD authored with clear domain entities, functional acceptance criteria, and NFRs.",
                "Architectural Decision Record (ADR) and Clean Architecture scaffold approved.",
                "All user stories and subtasks implemented, audited, and verified.",
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

        // 2. Requirements Research Spike Ticket (Starts the whole engineering process!)
        var existingResearchTicket = _ticketStore.GetTicketsByEpic(epicId).FirstOrDefault(t => t.Type == TicketType.ResearchSpike);
        var resTicketId = existingResearchTicket?.Id ?? $"RES-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var resTicket = existingResearchTicket ?? new TicketItem(
            Id: resTicketId,
            ParentEpicId: epicId,
            Title: $"Requirements Research & Feasibility: {epicTitle}",
            Description: $"Requirements Researcher investigates domain concepts, specifications (RFCs/standards), modern .NET 10 / C# 13 ecosystem libraries, codebase boundaries, failure modes, and technical feasibility for {epicTitle}.",
            Type: TicketType.ResearchSpike,
            Status: researchBrief != null ? TicketStatus.Done : TicketStatus.Ready,
            AssigneeRole: AgentRole.RequirementsResearcher,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: priority,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: [
                "Identify domain concepts, specifications, and RFC standards.",
                "Map codebase dependencies and target architecture boundaries.",
                "Identify edge cases, security hazards, and non-functional constraints.",
                "Provide structured feasibility recommendations for TPM."
            ],
            Deliverables: researchBrief != null ? [researchBrief] : Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Stage"] = "Research" },
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: researchBrief != null ? DateTimeOffset.UtcNow : null
        );

        if (existingResearchTicket == null)
        {
            _ticketStore.CreateTicket(resTicket);
        }
        else if (researchBrief != null && !resTicket.Deliverables.Any(d => d.Name == researchBrief.Name))
        {
            resTicket = resTicket.WithDeliverable(researchBrief).WithStatus(TicketStatus.Done);
            _ticketStore.UpdateTicket(resTicket);
        }
        else if (researchBrief == null && resTicket.Status != TicketStatus.Ready)
        {
            // When starting fresh discovery without a research brief, ensure research ticket is Ready
            resTicket = resTicket.WithStatus(TicketStatus.Ready);
            _ticketStore.UpdateTicket(resTicket);
        }
        createdTickets.Add(resTicket);

        // Check if feature stories already exist for this epic
        var existingStories = _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
        if (existingStories.Count > 0)
        {
            if (researchBrief == null)
            {
                // When starting fresh discovery without a research brief, ensure feature stories are in Backlog
                // waiting on the research spike, and assigned to TechnicalProductManager.
                var resetStories = new List<TicketItem>();
                foreach (var s in existingStories)
                {
                    var updated = s.WithStatus(TicketStatus.Backlog);
                    if (!updated.DependsOnTicketIds.Contains(resTicketId))
                    {
                        updated = updated with { DependsOnTicketIds = [resTicketId] };
                    }
                    if (updated.AssigneeRole != AgentRole.TechnicalProductManager)
                    {
                        updated = updated with { AssigneeRole = AgentRole.TechnicalProductManager };
                    }
                    _ticketStore.UpdateTicket(updated);
                    resetStories.Add(updated);
                }
                createdTickets.AddRange(resetStories);
                return createdTickets;
            }

            createdTickets.AddRange(existingStories);
            return createdTickets;
        }

        // 3. TPM synthesizes Research and generates primary User Stories / Feature Tickets under this Epic
        var story1Id = $"STORY-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var story1 = new TicketItem(
            Id: story1Id,
            ParentEpicId: epicId,
            Title: $"{epicTitle}: Core Engine & Protocols",
            Description: $"TPM Requirement: Synthesize Research Brief into formal PRD and establish foundational capabilities, domain models, interfaces, and state lifecycle for {epicTitle}.",
            Type: TicketType.Feature,
            Status: researchBrief != null ? TicketStatus.Ready : TicketStatus.Backlog,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: priority,
            DependsOnTicketIds: [resTicketId],
            AcceptanceCriteria: [
                "Executive summary and user stories synthesized from Research Brief.",
                "Domain models and value objects specified as immutable C# records.",
                "Public API contracts and service boundaries defined.",
                "Non-functional requirements (latency SLA, zero allocations, STRIDE security baseline) documented."
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
        var parentEpicId = userStory.ParentEpicId ?? userStory.Id;

        // Reuse existing subtasks for this story if already present, preventing ticket duplication
        var existingStorySubtasks = _ticketStore.GetTicketsByEpic(parentEpicId)
            .Where(t => t.Type == TicketType.Subtask && (t.Title.Contains(userStory.Title) || t.DependsOnTicketIds.Contains(userStory.Id)))
            .ToList();

        if (existingStorySubtasks.Count > 0)
        {
            var synchronized = new List<TicketItem>();
            foreach (var st in existingStorySubtasks)
            {
                var cur = st;
                if (cur.AssigneeRole == AgentRole.LeadArchitect && userStory.Status == TicketStatus.Done && cur.Status == TicketStatus.Backlog)
                {
                    cur = cur.WithStatus(TicketStatus.Ready);
                    _ticketStore.UpdateTicket(cur);
                }
                else if (userStory.Status != TicketStatus.Done && cur.Status != TicketStatus.Backlog)
                {
                    cur = cur.WithStatus(TicketStatus.Backlog);
                    _ticketStore.UpdateTicket(cur);
                }
                synchronized.Add(cur);
            }
            return synchronized;
        }

        var subtasks = new List<TicketItem>();

        // Subtask 1: Architecture Design & Clean Architecture Scaffolding
        var adrSubtaskId = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var adrSubtask = new TicketItem(
            Id: adrSubtaskId,
            ParentEpicId: parentEpicId,
            Title: $"[Arch] Design ADR & Scaffold Clean Architecture for {userStory.Title}",
            Description: $"Lead Architect produces Nygard Architectural Decision Record, defining domain boundaries, zero-allocation protocols, and scaffolds the Clean Architecture solution (Domain, Contracts, DI extensions) for {userStory.Title}.",
            Type: TicketType.Subtask,
            Status: userStory.Status == TicketStatus.Done ? TicketStatus.Ready : TicketStatus.Backlog,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: userStory.Priority,
            DependsOnTicketIds: [userStory.Id],
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

    public IReadOnlyList<TicketItem> SyncUserStoriesFromPrd(
        string epicId,
        string prdContent,
        TicketPriority priority = TicketPriority.High)
    {
        if (string.IsNullOrWhiteSpace(prdContent))
        {
            return _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
        }

        var parsedStories = ExtractStoriesFromContent(prdContent);
        if (parsedStories.Count == 0)
        {
            return _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
        }

        var existingEpic = _ticketStore.GetTicketById(epicId);
        var existingFeatures = _ticketStore.GetTicketsByEpic(epicId)
            .Where(t => t.Type == TicketType.Feature)
            .OrderBy(t => t.CreatedAt)
            .ToList();

        var resTicket = _ticketStore.GetTicketsByEpic(epicId).FirstOrDefault(t => t.Type == TicketType.ResearchSpike);
        var dependsOn = resTicket != null ? new[] { resTicket.Id } : Array.Empty<string>();

        var result = new List<TicketItem>();

        for (int i = 0; i < parsedStories.Count; i++)
        {
            var storyDto = parsedStories[i];
            var storyTitle = storyDto.Title.Trim();
            var epicPrefix = existingEpic?.Title != null ? $"{existingEpic.Title}: " : "";
            if (!storyTitle.StartsWith(existingEpic?.Title ?? "", StringComparison.OrdinalIgnoreCase))
            {
                storyTitle = $"{epicPrefix}{storyTitle}";
            }

            if (i < existingFeatures.Count)
            {
                var existing = existingFeatures[i];
                var updated = existing with
                {
                    Title = storyTitle,
                    Description = !string.IsNullOrWhiteSpace(storyDto.Description) ? storyDto.Description : existing.Description,
                    AcceptanceCriteria = storyDto.AcceptanceCriteria.Count > 0 ? storyDto.AcceptanceCriteria : existing.AcceptanceCriteria,
                    Status = TicketStatus.Done,
                    Metadata = new Dictionary<string, string>(existing.Metadata ?? new Dictionary<string, string>())
                    {
                        ["StoryNumber"] = $"{i + 1}",
                        ["DecompositionSource"] = "PRD_Extraction"
                    }
                };
                _ticketStore.UpdateTicket(updated);
                result.Add(updated);
            }
            else
            {
                var newStoryId = $"STORY-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                var newStory = new TicketItem(
                    Id: newStoryId,
                    ParentEpicId: epicId,
                    Title: storyTitle,
                    Description: storyDto.Description,
                    Type: TicketType.Feature,
                    Status: TicketStatus.Done,
                    AssigneeRole: AgentRole.TechnicalProductManager,
                    CreatedByRole: AgentRole.TechnicalProductManager,
                    Priority: priority,
                    DependsOnTicketIds: dependsOn,
                    AcceptanceCriteria: storyDto.AcceptanceCriteria,
                    Deliverables: Array.Empty<ArtifactItem>(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["StoryNumber"] = $"{i + 1}",
                        ["DecompositionSource"] = "PRD_Extraction"
                    },
                    CreatedAt: DateTimeOffset.UtcNow
                );
                _ticketStore.CreateTicket(newStory);
                result.Add(newStory);
            }
        }

        return result;
    }

    public static IReadOnlyList<ParsedUserStoryDto> ExtractStoriesFromContent(string content)
    {
        var stories = new List<ParsedUserStoryDto>();
        if (string.IsNullOrWhiteSpace(content)) return stories;

        // 1. Try to parse machine-readable ```json:user_stories or ```json [...] block
        var jsonMatch = Regex.Match(content, @"```(?:json:user_stories|json)?\s*(\[\s*\{[\s\S]*?\}\s*\])\s*```", RegexOptions.IgnoreCase);
        if (jsonMatch.Success)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsedJson = JsonSerializer.Deserialize<List<ParsedUserStoryDto>>(jsonMatch.Groups[1].Value, options);
                if (parsedJson != null && parsedJson.Count > 0)
                {
                    var valid = parsedJson.Where(s => !string.IsNullOrWhiteSpace(s.Title)).ToList();
                    if (valid.Count > 0) return valid;
                }
            }
            catch { }
        }

        // 2. Parse Markdown headings (### User Story N: [Title] or ### Feature N: [Title])
        var headingMatches = Regex.Matches(content, @"(?m)^###\s*(?:User Story\s*\d*|Story\s*\d*|Feature\s*\d*)?[:\s-]+([^\r\n]+)");
        if (headingMatches.Count > 0)
        {
            for (int i = 0; i < headingMatches.Count; i++)
            {
                var match = headingMatches[i];
                var rawTitle = match.Groups[1].Value.Trim().TrimEnd(':');
                if (string.IsNullOrWhiteSpace(rawTitle) ||
                    rawTitle.StartsWith("Non-Functional", StringComparison.OrdinalIgnoreCase) ||
                    rawTitle.StartsWith("Functional Acceptance", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int startPos = match.Index + match.Length;
                int endPos = (i + 1 < headingMatches.Count) ? headingMatches[i + 1].Index : content.Length;
                var sectionText = content.Substring(startPos, endPos - startPos);

                var descMatch = Regex.Match(sectionText, @"(?:-\s*Description:|\*\*Description:\*\*|Description:)\s*([^\r\n]+)");
                var desc = descMatch.Success ? descMatch.Groups[1].Value.Trim() : $"Modular capability implementation for {rawTitle}.";

                var criteria = new List<string>();
                var criteriaMatches = Regex.Matches(sectionText, @"^[ \t]*[-*]\s*\[[ xX]?\]\s*(.+)$", RegexOptions.Multiline);
                foreach (Match cm in criteriaMatches)
                {
                    var crit = cm.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(crit)) criteria.Add(crit);
                }

                if (criteria.Count == 0)
                {
                    criteria.Add($"Functional requirements and acceptance tests verified for {rawTitle}.");
                }

                stories.Add(new ParsedUserStoryDto(rawTitle, desc, criteria));
            }
        }

        return stories;
    }
}
