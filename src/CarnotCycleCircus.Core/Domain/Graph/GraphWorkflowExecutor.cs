using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Artifacts;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Graph;

public interface IGraphWorkflowExecutor
{
    WorkflowGraph CurrentGraph { get; }
    bool IsRunning { get; }
    void SetGraph(WorkflowGraph graph);
    void LoadTeam(TeamDefinition team);
    void LoadTeam(EngineeringTeam team);
    void UpdateNodePosition(string nodeId, int x, int y);
    void AddNode(GraphNode node);
    void RemoveNode(string nodeId);
    void AddConnection(PortConnection connection);
    void RemoveConnection(string sourceNodeId, PortType sourcePort, string targetNodeId, PortType targetPort);
    void UpdatePolicy(FailurePolicy policy);
    void LoadPreset(string presetId);
    bool ValidateConnection(PortConnection connection, out string? errorMessage);
    void ResetGraph();

    Task<bool> ExecuteWorkflowAsync(
        string epicTitle,
        string epicDescription,
        CancellationToken cancellationToken = default);

    Task<bool> ExecuteTicketAsync(string ticketId, CancellationToken cancellationToken = default);
    Task<bool> ExecuteReadyTicketsAsync(CancellationToken cancellationToken = default);
    Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default);

    event Action<WorkflowGraph>? OnGraphUpdated;
    event Action<string, NodeExecutionState>? OnNodeStateChanged;
}

public class GraphWorkflowExecutor : IGraphWorkflowExecutor
{
    private WorkflowGraph _graph;
    private readonly ITicketStore _ticketStore;
    private readonly IWorkDecompositionEngine _decompositionEngine;
    private readonly IHandoffRouter _handoffRouter;
    private readonly IAgentExecutionEngine _executionEngine;
    private readonly IAgentEventStream _eventStream;
    private readonly IMemoryConsolidationEngine _memoryConsolidation;
    private readonly Learning.ISelfImprovementEngine? _selfImprovement;
    private readonly ITeamDefinitionManager? _teamManager;
    private readonly IArtifactManager? _artifactManager;
    private bool _isRunning;

    public WorkflowGraph CurrentGraph => _graph;
    public bool IsRunning => _isRunning;

    public event Action<WorkflowGraph>? OnGraphUpdated;
    public event Action<string, NodeExecutionState>? OnNodeStateChanged;

    public GraphWorkflowExecutor(
        ITicketStore ticketStore,
        IWorkDecompositionEngine decompositionEngine,
        IHandoffRouter handoffRouter,
        IAgentExecutionEngine executionEngine,
        IAgentEventStream eventStream,
        IMemoryConsolidationEngine memoryConsolidation,
        Learning.ISelfImprovementEngine? selfImprovement = null,
        ITeamDefinitionManager? teamManager = null,
        IArtifactManager? artifactManager = null)
    {
        _ticketStore = ticketStore;
        _decompositionEngine = decompositionEngine;
        _handoffRouter = handoffRouter;
        _executionEngine = executionEngine;
        _eventStream = eventStream;
        _memoryConsolidation = memoryConsolidation;
        _selfImprovement = selfImprovement;
        _teamManager = teamManager;
        _artifactManager = artifactManager;

        _graph = _teamManager?.GetCurrentTeam().Graph ?? WorkflowGraph.CreateDefaultEngineeringCircus();

        if (_teamManager != null)
        {
            _teamManager.OnCurrentTeamChanged += team =>
            {
                _graph = team.Graph;
                OnGraphUpdated?.Invoke(_graph);
            };
        }
    }

    public void SetGraph(WorkflowGraph graph)
    {
        _graph = graph;
        OnGraphUpdated?.Invoke(_graph);
    }

    public void UpdateNodePosition(string nodeId, int x, int y)
    {
        var nodes = _graph.Nodes.Select(n => n.Id == nodeId ? n with { X = x, Y = y } : n).ToList();
        _graph = _graph with { Nodes = nodes };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void AddNode(GraphNode node)
    {
        if (_graph.Nodes.Any(n => n.Id == node.Id))
        {
            return;
        }

        var nodes = _graph.Nodes.Append(node).ToList();
        _graph = _graph with { Nodes = nodes };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void RemoveNode(string nodeId)
    {
        var nodes = _graph.Nodes.Where(n => n.Id != nodeId).ToList();
        var connections = _graph.Connections.Where(c => c.SourceNodeId != nodeId && c.TargetNodeId != nodeId).ToList();
        _graph = _graph with { Nodes = nodes, Connections = connections };
        OnGraphUpdated?.Invoke(_graph);
    }

    public bool ValidateConnection(PortConnection connection, out string? errorMessage)
    {
        if (connection.SourceNodeId == connection.TargetNodeId)
        {
            errorMessage = "A node cannot connect to itself.";
            return false;
        }

        if (connection.SourcePort == PortType.Input)
        {
            errorMessage = "Source port must be an Output or Failure port.";
            return false;
        }

        if (connection.TargetPort != PortType.Input)
        {
            errorMessage = "Target port must be an Input port.";
            return false;
        }

        var sourceNode = _graph.Nodes.FirstOrDefault(n => n.Id == connection.SourceNodeId);
        var targetNode = _graph.Nodes.FirstOrDefault(n => n.Id == connection.TargetNodeId);

        if (sourceNode == null || targetNode == null)
        {
            errorMessage = "Both source and target nodes must exist in the workflow graph.";
            return false;
        }

        if (_graph.Connections.Any(c =>
            c.SourceNodeId == connection.SourceNodeId &&
            c.SourcePort == connection.SourcePort &&
            c.TargetNodeId == connection.TargetNodeId &&
            c.TargetPort == connection.TargetPort))
        {
            errorMessage = "This connection cable already exists.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public void AddConnection(PortConnection connection)
    {
        if (!ValidateConnection(connection, out _))
        {
            return;
        }

        var list = _graph.Connections.Append(connection).Distinct().ToList();
        _graph = _graph with { Connections = list };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void RemoveConnection(string sourceNodeId, PortType sourcePort, string targetNodeId, PortType targetPort)
    {
        var list = _graph.Connections.Where(c => !(c.SourceNodeId == sourceNodeId && c.SourcePort == sourcePort && c.TargetNodeId == targetNodeId && c.TargetPort == targetPort)).ToList();
        _graph = _graph with { Connections = list };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void UpdatePolicy(FailurePolicy policy)
    {
        _graph = _graph with { Policy = policy };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void LoadTeam(TeamDefinition team)
    {
        _graph = team.Graph;
        _isRunning = false;
        OnGraphUpdated?.Invoke(_graph);
    }

    public void LoadTeam(EngineeringTeam team)
    {
        _graph = team.Graph;
        _isRunning = false;
        OnGraphUpdated?.Invoke(_graph);
    }

    public void LoadPreset(string presetId)
    {
        _graph = presetId.ToLowerInvariant() switch
        {
            "rapid" or "preset-rapid" => WorkflowGraph.CreateRapidPrototype(),
            "zero-trust" or "preset-zero-trust" => WorkflowGraph.CreateZeroTrustSecurityCircus(),
            "performance" or "preset-performance" => WorkflowGraph.CreateHighPerformanceCircus(),
            _ => WorkflowGraph.CreateDefaultEngineeringCircus()
        };
        _isRunning = false;
        OnGraphUpdated?.Invoke(_graph);
    }

    public void ResetGraph()
    {
        var resetNodes = _graph.Nodes.Select(n => n with { State = NodeExecutionState.Idle, RetryCount = 0, LastOutputSummary = null, CurrentTicketId = null }).ToList();
        _graph = _graph with { Nodes = resetNodes };
        _isRunning = false;
        OnGraphUpdated?.Invoke(_graph);
    }

    public async Task<bool> ExecuteTicketAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = _ticketStore.GetTicketById(ticketId);
        if (ticket == null || ticket.Status == TicketStatus.Done)
        {
            return false;
        }

        if (!_ticketStore.AreDependenciesSatisfied(ticket.Id))
        {
            _eventStream.Publish(AgentMessage.Create(
                role: ticket.AssigneeRole,
                senderName: "Ticket Engine",
                content: $"⏳ Cannot execute [{ticket.Id}] {ticket.Title}: Dependencies ({string.Join(", ", ticket.DependsOnTicketIds)}) not yet completed.",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));
            return false;
        }

        var node = GetNodeByRole(ticket.AssigneeRole);
        if (node != null)
        {
            UpdateNodeState(node.Id, NodeExecutionState.Running, ticketId: ticket.Id);
        }

        _ticketStore.UpdateTicket(ticket.WithStatus(TicketStatus.InProgress));

        _eventStream.Publish(AgentMessage.Create(
            role: ticket.AssigneeRole,
            senderName: ticket.AssigneeRole.ToDisplayName(),
            content: $"👷 Agent picked up [{ticket.Id}]: '{ticket.Title}'. Executing task deliverable...",
            type: MessageType.StateChange,
            ticketId: ticket.Id
        ));

        await Task.Delay(100, cancellationToken);

        try
        {
            var artifacts = await _executionEngine.ExecuteRoleTaskAsync(ticket.AssigneeRole, ticket, cancellationToken);
            foreach (var a in artifacts)
            {
                ticket = ticket.WithDeliverable(a);
                if (_artifactManager != null)
                {
                    await _artifactManager.SaveDeliverableArtifactAsync(ticket, a, cancellationToken);
                }
            }
            _ticketStore.UpdateTicket(ticket);

            // Record handoff to all downstream roles
            var downstreamRoles = GetDownstreamRolesFor(ticket.AssigneeRole);
            foreach (var nextRole in downstreamRoles)
            {
                _handoffRouter.RouteSuccessHandoff(
                    ticket.Id,
                    ticket.AssigneeRole,
                    nextRole,
                    $"Delivered [{ticket.Id}] {ticket.Title}. Attached {artifacts.Count} artifacts.",
                    $"Proceed with downstream task for {ticket.Title}.",
                    artifacts
                );
            }

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(ticket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(ticket, _eventStream.GetHistory(), cancellationToken);

            if (node != null)
            {
                UpdateNodeState(node.Id, NodeExecutionState.Completed, $"Delivered {ticket.Title}", ticket.Id);
            }

            return true;
        }
        catch (Exception ex)
        {
            if (node != null)
            {
                UpdateNodeState(node.Id, NodeExecutionState.Failed, ex.Message, ticket.Id);
            }
            _eventStream.Publish(AgentMessage.Create(
                role: ticket.AssigneeRole,
                senderName: ticket.AssigneeRole.ToDisplayName(),
                content: $"🛑 Task execution failed for [{ticket.Id}] ({ticket.AssigneeRole.ToDisplayName()}): {ex.Message}",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));
            _ticketStore.UpdateTicket(ticket.WithStatus(TicketStatus.Ready));
            return false;
        }
    }

    public async Task<bool> ExecuteReadyTicketsAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        try
        {
            int maxIterations = 50;
            int count = 0;

            while (count < maxIterations)
            {
                var readyTickets = _ticketStore.GetReadyTickets()
                    .Where(t => t.Type != TicketType.Epic && t.Status != TicketStatus.Done)
                    .ToList();

                if (readyTickets.Count == 0)
                {
                    break;
                }

                var nextTicket = readyTickets.First();
                var executed = await ExecuteTicketAsync(nextTicket.Id, cancellationToken);
                if (!executed)
                {
                    _eventStream.Publish(AgentMessage.Create(
                        role: nextTicket.AssigneeRole,
                        senderName: "Workflow Engine",
                        content: $"🛑 Queue execution halted on [{nextTicket.Id}] ({nextTicket.AssigneeRole.ToDisplayName()}).",
                        type: MessageType.Alert,
                        ticketId: nextTicket.Id
                    ));
                    break;
                }
                count++;
                await Task.Delay(150, cancellationToken);
            }

            // Mark parent stories/epics Done if all subtasks are Done
            var allTickets = _ticketStore.GetAllTickets();
            var epics = allTickets.Where(t => t.Type == TicketType.Epic).ToList();
            foreach (var epic in epics)
            {
                var epicTickets = _ticketStore.GetTicketsByEpic(epic.Id);
                var subtasks = epicTickets.Where(t => t.Type == TicketType.Subtask).ToList();
                if (subtasks.Count > 0 && subtasks.All(t => t.Status == TicketStatus.Done))
                {
                    foreach (var parentItem in epicTickets.Where(t => t.Type != TicketType.Subtask && t.Status != TicketStatus.Done))
                    {
                        _ticketStore.UpdateTicket(parentItem.WithStatus(TicketStatus.Done));
                    }
                }
            }

            // Post-execution self-improvement
            if (_selfImprovement != null)
            {
                try
                {
                    await _selfImprovement.RunSelfImprovementCycleAsync(cancellationToken);
                }
                catch { }
            }

            return count > 0;
        }
        finally
        {
            _isRunning = false;
            OnGraphUpdated?.Invoke(_graph);
        }
    }

    public async Task<bool> ExecuteWorkflowAsync(
        string epicTitle,
        string epicDescription,
        CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        ResetGraph();

        try
        {
            _eventStream.Publish(AgentMessage.Create(
                role: null,
                senderName: "🎪 Circus Ringmaster",
                content: $"🎪 Ladies & Gentlemen! The Carnot Circus is officially running for Epic: '{epicTitle}' at Ludicrous Speed!",
                type: MessageType.Alert
            ));

            // Check if tickets already exist for this epic or if we need to decompose
            var existingEpic = _ticketStore.GetAllTickets().FirstOrDefault(t => t.Type == TicketType.Epic && string.Equals(t.Title, epicTitle, StringComparison.OrdinalIgnoreCase));
            string epicId;

            // 1. Collaborative Discovery Stage: Requirements Researcher & Technical Product Manager
            var resNode = GetNodeByRole(AgentRole.RequirementsResearcher);
            ArtifactItem? researchBrief = existingEpic?.Deliverables.FirstOrDefault(d => d.Name.EndsWith("_RESEARCH_BRIEF.md", StringComparison.OrdinalIgnoreCase));

            if (researchBrief == null)
            {
                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.TechnicalProductManager,
                    senderName: "Barnum B. Buzzword (TPM)",
                    content: $"🤝 Collaborative Discovery Initiated: TPM & Research Analyst partnering to frame and investigate specifications for new project '{epicTitle}'.",
                    type: MessageType.Chat
                ));

                if (resNode != null)
                {
                    UpdateNodeState(resNode.Id, NodeExecutionState.Running);
                    await Task.Delay(150, cancellationToken);
                }

                var existingResearchTicket = _ticketStore.GetAllTickets().FirstOrDefault(t => t.Type == TicketType.ResearchSpike && t.Title.Contains(epicTitle, StringComparison.OrdinalIgnoreCase));
                var tempResearchTicket = existingResearchTicket ?? new TicketItem(
                    Id: $"RES-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ParentEpicId: existingEpic?.Id,
                    Title: $"Requirements Research & Feasibility: {epicTitle}",
                    Description: epicDescription,
                    Type: TicketType.ResearchSpike,
                    Status: TicketStatus.InProgress,
                    AssigneeRole: AgentRole.RequirementsResearcher,
                    CreatedByRole: AgentRole.TechnicalProductManager,
                    Priority: TicketPriority.High,
                    DependsOnTicketIds: Array.Empty<string>(),
                    AcceptanceCriteria: [
                        "Identify domain concepts, specifications, and RFC standards.",
                        "Map codebase dependencies and target architecture boundaries.",
                        "Identify edge cases, security hazards, and non-functional constraints.",
                        "Provide structured feasibility recommendations for TPM."
                    ],
                    Deliverables: Array.Empty<ArtifactItem>(),
                    Metadata: new Dictionary<string, string> { ["Stage"] = "Research" },
                    CreatedAt: DateTimeOffset.UtcNow
                );

                if (existingResearchTicket == null)
                {
                    _ticketStore.CreateTicket(tempResearchTicket);
                }

                var researchArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.RequirementsResearcher, tempResearchTicket, cancellationToken);
                researchBrief = researchArtifacts.FirstOrDefault();

                foreach (var a in researchArtifacts)
                {
                    tempResearchTicket = tempResearchTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(tempResearchTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(tempResearchTicket.WithStatus(TicketStatus.Done));

                _handoffRouter.RouteSuccessHandoff(
                    tempResearchTicket.Id,
                    AgentRole.RequirementsResearcher,
                    AgentRole.TechnicalProductManager,
                    "Requirements researched & Feasibility Brief produced for collaborative synthesis.",
                    "Synthesize research findings into PRD and deconstruct Epic into User Stories.",
                    researchArtifacts
                );

                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.RequirementsResearcher,
                    senderName: "Rachel 'DeepDive' Reference (Requirements Researcher)",
                    content: $"🔬 Requirements Researcher Rachel Reference: 'When you have eliminated the impossible, whatever remains must be the requirements!' Researched '{epicTitle}' specifications and produced Feasibility Brief for TPM.",
                    type: MessageType.Chat,
                    ticketId: tempResearchTicket.Id
                ));

                if (resNode != null)
                {
                    UpdateNodeState(resNode.Id, NodeExecutionState.Completed, "Requirements researched & Feasibility Brief produced.", tempResearchTicket.Id);
                }
            }
            else if (resNode != null)
            {
                UpdateNodeState(resNode.Id, NodeExecutionState.Completed, "Requirements researched & Feasibility Brief available.");
            }

            // 2. TPM Phase - Collaborative PRD Synthesis & User Story Deconstruction
            var tpmNode = GetNodeByRole(AgentRole.TechnicalProductManager);
            var hasPrd = existingEpic != null && existingEpic.Deliverables.Any(d => d.Name.EndsWith("_PRD.md", StringComparison.OrdinalIgnoreCase));

            if (!hasPrd)
            {
                if (tpmNode != null)
                {
                    UpdateNodeState(tpmNode.Id, NodeExecutionState.Running);
                    await Task.Delay(150, cancellationToken);
                }

                var createdStoryTickets = _decompositionEngine.DeconstructEpicIntoUserStories(epicTitle, epicDescription, researchBrief);
                var epicTicket = createdStoryTickets.First(t => t.Type == TicketType.Epic);
                if (researchBrief != null && !epicTicket.Deliverables.Any(d => d.Name == researchBrief.Name))
                {
                    epicTicket = epicTicket.WithDeliverable(researchBrief);
                }
                epicId = epicTicket.Id;

                var prdArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.TechnicalProductManager, epicTicket, cancellationToken);
                foreach (var a in prdArtifacts)
                {
                    epicTicket = epicTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(epicTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(epicTicket);

                // Mark the TPM feature stories as Done with the PRD deliverables so Lead Architect can refine and scaffold
                var stories = _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
                foreach (var s in stories)
                {
                    var updatedStory = s.WithDeliverables(prdArtifacts).WithStatus(TicketStatus.Done);
                    _ticketStore.UpdateTicket(updatedStory);
                }

                var storiesCount = stories.Count;
                _handoffRouter.RouteSuccessHandoff(
                    epicTicket.Id,
                    AgentRole.TechnicalProductManager,
                    AgentRole.LeadArchitect,
                    $"Synthesized Research Brief and authored PRD for '{epicTitle}' with {storiesCount} foundational User Stories.",
                    "Refine User Stories into technical subtasks, then scaffold Clean Architecture and produce Architectural Decision Record (ADR).",
                    prdArtifacts
                );

                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.TechnicalProductManager,
                    senderName: "Barnum B. Buzzword (TPM)",
                    content: $"🎯 TPM Barnum B. Buzzword: 'The new Jira backlog is here! I'm somebody now!' Ingested Research Brief, authored PRD, and established {storiesCount} foundational User Stories for Lead Architect refinement.",
                    type: MessageType.Chat,
                    ticketId: epicTicket.Id
                ));

                if (tpmNode != null)
                {
                    UpdateNodeState(tpmNode.Id, NodeExecutionState.Completed, $"PRD authored & {storiesCount} stories established for refinement.", epicTicket.Id);
                }
            }
            else
            {
                epicId = existingEpic!.Id;
                var stories = _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
                foreach (var s in stories.Where(s => s.Status != TicketStatus.Done))
                {
                    var updatedStory = s.WithStatus(TicketStatus.Done);
                    _ticketStore.UpdateTicket(updatedStory);
                }

                if (tpmNode != null)
                {
                    UpdateNodeState(tpmNode.Id, NodeExecutionState.Completed, "PRD authored & stories established.", epicId);
                }
            }

            // 3. Lead Architect Phase: Backlog Refinement followed by Architecture & ADR Scaffolding
            var archNode = GetNodeByRole(AgentRole.LeadArchitect);
            
            // 3A. Architect Backlog Refinement Pass: Deconstruct Feature Stories into Technical Subtasks
            var unrefinedStories = _ticketStore.GetTicketsByEpic(epicId)
                .Where(t => t.Type == TicketType.Feature)
                .ToList();

            var existingSubtasks = _ticketStore.GetTicketsByEpic(epicId)
                .Where(t => t.Type == TicketType.Subtask)
                .ToList();

            if (existingSubtasks.Count == 0 && unrefinedStories.Count > 0)
            {
                foreach (var story in unrefinedStories)
                {
                    var refinedSubtasks = _decompositionEngine.RefineStoryIntoTechnicalSubtasks(story);
                    _eventStream.Publish(AgentMessage.Create(
                        role: AgentRole.LeadArchitect,
                        senderName: "Archduke Archibald Abstraction-o (Lead Architect)",
                        content: $"📐 Lead Architect Backlog Refinement: Groomed story '[{story.Id}] {story.Title}' into {refinedSubtasks.Count} technical execution subtasks with strict DAG dependencies.",
                        type: MessageType.StateChange,
                        ticketId: story.Id
                    ));
                }
            }

            // 3B. Architect ADR & Clean Architecture Scaffolding
            var readyTickets = _ticketStore.GetReadyTickets();
            var archTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect && t.Type == TicketType.Subtask && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect && t.Type == TicketType.Subtask)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect);

            if (archNode != null && archTicket != null)
            {
                UpdateNodeState(archNode.Id, NodeExecutionState.Running, ticketId: archTicket.Id);
                await Task.Delay(150, cancellationToken);

                var artifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, archTicket, cancellationToken);
                foreach (var a in artifacts)
                {
                    archTicket = archTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(archTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(archTicket);

                _handoffRouter.RouteSuccessHandoff(
                    archTicket.Id,
                    AgentRole.LeadArchitect,
                    AgentRole.SoftwareDeveloper,
                    "ADR Architecture & Topology finalized after backlog refinement. 'Listen, strange developers lyin' in Slack distributin' interfaces is no basis for a system!'",
                    "Implement feature with zero heap allocations matching refined subtasks and ADR contracts.",
                    artifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(archTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(archTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(archNode.Id, NodeExecutionState.Completed, "Backlog refined & ADR/Topology designed.", archTicket.Id);
            }

            // 4. Software Developer Phase
            var devNode = GetNodeByRole(AgentRole.SoftwareDeveloper);
            readyTickets = _ticketStore.GetReadyTickets();
            var devTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper);

            if (devNode != null && devTicket != null)
            {
                UpdateNodeState(devNode.Id, NodeExecutionState.Running, ticketId: devTicket.Id);
                await Task.Delay(150, cancellationToken);

                var devArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, devTicket, cancellationToken);
                foreach (var a in devArtifacts)
                {
                    devTicket = devTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(devTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(devTicket);

                // Handoff to both downstream review roles: Security and Optimization
                _handoffRouter.RouteSuccessHandoff(
                    devTicket.Id,
                    AgentRole.SoftwareDeveloper,
                    AgentRole.SecurityEngineer,
                    "Feature implemented! Zero heap allocations.",
                    "Perform STRIDE threat audit on implementation.",
                    devArtifacts
                );

                _handoffRouter.RouteSuccessHandoff(
                    devTicket.Id,
                    AgentRole.SoftwareDeveloper,
                    AgentRole.OptimizationEngineer,
                    "Feature implemented! Zero heap allocations.",
                    "Perform allocation and latency benchmark profiling.",
                    devArtifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(devTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(devTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Implementation delivered.", devTicket.Id);
            }

            // 5. Parallel Security & Optimization Phase
            var secNode = GetNodeByRole(AgentRole.SecurityEngineer);
            var optNode = GetNodeByRole(AgentRole.OptimizationEngineer);
            readyTickets = _ticketStore.GetReadyTickets();
            var secTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SecurityEngineer && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SecurityEngineer);
            var optTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.OptimizationEngineer && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.OptimizationEngineer);

            if (secNode != null && secTicket != null)
            {
                UpdateNodeState(secNode.Id, NodeExecutionState.Running, ticketId: secTicket.Id);
                await Task.Delay(150, cancellationToken);

                var secArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.SecurityEngineer, secTicket, cancellationToken);
                foreach (var a in secArtifacts)
                {
                    secTicket = secTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(secTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(secTicket);

                _handoffRouter.RouteSuccessHandoff(
                    secTicket.Id,
                    AgentRole.SecurityEngineer,
                    AgentRole.PrincipalQAAnalyst,
                    "STRIDE Threat Model audit approved.",
                    "Verify security findings and trace against QA test plan.",
                    secArtifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(secTicket.Id);
                UpdateNodeState(secNode.Id, NodeExecutionState.Completed, "STRIDE Threat Model Approved.", secTicket.Id);
            }

            if (optNode != null && optTicket != null)
            {
                UpdateNodeState(optNode.Id, NodeExecutionState.Running, ticketId: optTicket.Id);
                await Task.Delay(150, cancellationToken);

                var optArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.OptimizationEngineer, optTicket, cancellationToken);
                foreach (var a in optArtifacts)
                {
                    optTicket = optTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(optTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(optTicket);

                _handoffRouter.RouteSuccessHandoff(
                    optTicket.Id,
                    AgentRole.OptimizationEngineer,
                    AgentRole.PrincipalQAAnalyst,
                    "Zero-Allocations & latency benchmarks verified.",
                    "Verify performance SLA conformance in QA scorecard.",
                    optArtifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(optTicket.Id);
                UpdateNodeState(optNode.Id, NodeExecutionState.Completed, "Zero-Allocations Verified.", optTicket.Id);
            }

            // 6. Principal QA Phase
            var qaNode = GetNodeByRole(AgentRole.PrincipalQAAnalyst);
            readyTickets = _ticketStore.GetReadyTickets();
            var qaTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst);

            if (qaNode != null && qaTicket != null)
            {
                UpdateNodeState(qaNode.Id, NodeExecutionState.Running, ticketId: qaTicket.Id);
                await Task.Delay(150, cancellationToken);

                var qaArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.PrincipalQAAnalyst, qaTicket, cancellationToken);
                foreach (var a in qaArtifacts)
                {
                    qaTicket = qaTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(qaTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(qaTicket);

                _handoffRouter.RouteSuccessHandoff(
                    qaTicket.Id,
                    AgentRole.PrincipalQAAnalyst,
                    AgentRole.IntegrationEngineer,
                    "QA Acceptance criteria 100% verified and certified.",
                    "Package Clean Architecture solution and publish Release Manifest.",
                    qaArtifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(qaTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(qaTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(qaNode.Id, NodeExecutionState.Completed, "QA Certification 100% Passed.", qaTicket.Id);

                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.PrincipalQAAnalyst,
                    senderName: "Quinn the Build-Executioner (Principal QA)",
                    content: "🧪 Quinn the Build-Executioner (QA): 'That's a lot of nuts!' Tortured the build with edge cases. Production release certified: 'Alllllrighty then!'",
                    type: MessageType.StateChange,
                    ticketId: qaTicket.Id
                ));
            }

            // 7. Integration & Solution Packaging Phase
            var intNode = GetNodeByRole(AgentRole.IntegrationEngineer);
            readyTickets = _ticketStore.GetReadyTickets();
            var intTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.IntegrationEngineer && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.IntegrationEngineer);

            if (intNode != null && intTicket != null)
            {
                UpdateNodeState(intNode.Id, NodeExecutionState.Running, ticketId: intTicket.Id);
                await Task.Delay(150, cancellationToken);

                var intArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.IntegrationEngineer, intTicket, cancellationToken);
                foreach (var a in intArtifacts)
                {
                    intTicket = intTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(intTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(intTicket);

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(intTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(intTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(intNode.Id, NodeExecutionState.Completed, "Solution Packaged & Wired.", intTicket.Id);

                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.IntegrationEngineer,
                    senderName: "Ingrid the Package-Master (Release Integrator)",
                    content: "📦 Ingrid 'The Tarball' Tarjan (Release Integrator): 'Clean build, clean clone, zero merge conflicts!' Packaged Clean Architecture solution and published Release Manifest.",
                    type: MessageType.StateChange,
                    ticketId: intTicket.Id
                ));
            }

            _eventStream.Publish(AgentMessage.Create(
                role: null,
                senderName: "🎪 Circus Ringmaster",
                content: $"🏆 Carnot Cycle Epic '{epicTitle}' completed at Ludicrous Speed with maximum thermodynamic efficiency! 'You're my boy, Blue!'",
                type: MessageType.StateChange
            ));

            // If all subtasks of this epic are completed, mark features and epic as complete
            var allEpicTickets = _ticketStore.GetTicketsByEpic(epicId);
            var subtasks = allEpicTickets.Where(t => t.Type == TicketType.Subtask).ToList();
            if (subtasks.Count == 0 || subtasks.All(t => t.Status == TicketStatus.Done))
            {
                foreach (var parentItem in allEpicTickets.Where(t => t.Type != TicketType.Subtask))
                {
                    _ticketStore.UpdateTicket(parentItem.WithStatus(TicketStatus.Done));
                }
                var epicObj = _ticketStore.GetTicketById(epicId);
                if (epicObj != null && epicObj.Status != TicketStatus.Done)
                {
                    _ticketStore.UpdateTicket(epicObj.WithStatus(TicketStatus.Done));
                }
            }

            _eventStream.Publish(AgentMessage.Create(
                role: null,
                senderName: "Workflow Orchestrator",
                content: $"🏆 Workflow for '{epicTitle}' Completed Successfully!",
                type: MessageType.Alert
            ));

            // Trigger autonomous post-workflow self-improvement cycle
            if (_selfImprovement != null)
            {
                try
                {
                    await _selfImprovement.RunSelfImprovementCycleAsync(cancellationToken);
                }
                catch
                {
                    // Non-fatal learning cycle error
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _eventStream.Publish(AgentMessage.Create(
                role: null,
                senderName: "Workflow Orchestrator",
                content: $"🛑 Workflow stopped: {ex.Message}",
                type: MessageType.Alert
            ));

            var runningNodes = _graph.Nodes.Where(n => n.State == NodeExecutionState.Running).ToList();
            foreach (var n in runningNodes)
            {
                UpdateNodeState(n.Id, NodeExecutionState.Failed, ex.Message, n.CurrentTicketId);
            }

            return false;
        }
        finally
        {
            _isRunning = false;
            OnGraphUpdated?.Invoke(_graph);
        }
    }

    public async Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var readyTickets = _ticketStore.GetReadyTickets()
                .Where(t => t.Type != TicketType.Epic && t.Status != TicketStatus.Done)
                .ToList();

            if (readyTickets.Count == 0) return false;

            var nextTicket = readyTickets.First();
            return await ExecuteTicketAsync(nextTicket.Id, cancellationToken);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private IReadOnlyList<AgentRole> GetDownstreamRolesFor(AgentRole currentRole)
    {
        var node = GetNodeByRole(currentRole);
        if (node != null)
        {
            var outboundConns = _graph.Connections
                .Where(c => c.SourceNodeId == node.Id && c.SourcePort == PortType.Output)
                .ToList();

            if (outboundConns.Count > 0)
            {
                var targetRoles = outboundConns
                    .Select(c => _graph.Nodes.FirstOrDefault(n => n.Id == c.TargetNodeId)?.Role)
                    .Where(r => r.HasValue)
                    .Select(r => r!.Value)
                    .Distinct()
                    .ToList();

                if (targetRoles.Count > 0)
                {
                    return targetRoles;
                }
            }
        }

        return currentRole switch
        {
            AgentRole.RequirementsResearcher => [AgentRole.TechnicalProductManager],
            AgentRole.TechnicalProductManager => [AgentRole.LeadArchitect],
            AgentRole.LeadArchitect => [AgentRole.SoftwareDeveloper],
            AgentRole.SoftwareDeveloper => [AgentRole.SecurityEngineer, AgentRole.OptimizationEngineer],
            AgentRole.SecurityEngineer => [AgentRole.PrincipalQAAnalyst],
            AgentRole.OptimizationEngineer => [AgentRole.PrincipalQAAnalyst],
            AgentRole.PrincipalQAAnalyst => [AgentRole.IntegrationEngineer],
            _ => Array.Empty<AgentRole>()
        };
    }

    private GraphNode? GetNodeByRole(AgentRole role) =>
        _graph.Nodes.FirstOrDefault(n => n.Role == role);

    private void UpdateNodeState(string nodeId, NodeExecutionState state, string? outputSummary = null, string? ticketId = null)
    {
        var nodes = _graph.Nodes.Select(n => n.Id == nodeId ? n.WithState(state, outputSummary, ticketId) : n).ToList();
        _graph = _graph with { Nodes = nodes };
        OnNodeStateChanged?.Invoke(nodeId, state);
        OnGraphUpdated?.Invoke(_graph);
    }
}
