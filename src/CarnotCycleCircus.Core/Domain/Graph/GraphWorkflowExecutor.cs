using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Approvals;
using CarnotCycleCircus.Core.Domain.Artifacts;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Projects;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Graph;

public record ActiveExecutionStatus(
    AgentRole Role,
    string RoleName,
    string TicketId,
    string TicketTitle,
    string Model,
    DateTimeOffset StartedAt,
    string CurrentPhase,
    int ChunksReceived = 0,
    string LastSnippet = ""
);

public interface IGraphWorkflowExecutor
{
    WorkflowGraph CurrentGraph { get; }
    bool IsRunning { get; }
    ActiveExecutionStatus? CurrentActiveExecution { get; }
    IAgentExecutionTracker? Tracker { get; }
    IWorkflowApprovalService ApprovalService { get; }
    event Action<ActiveExecutionStatus?>? OnActiveExecutionChanged;

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
    private readonly IWorkflowApprovalService _approvalService;
    private readonly IActiveProjectContext? _activeProjectContext;
    private readonly IProjectManager? _projectManager;
    private bool _isRunning;
    private ActiveExecutionStatus? _currentActiveExecution;

    public WorkflowGraph CurrentGraph => _graph;
    public bool IsRunning => _isRunning;
    public ActiveExecutionStatus? CurrentActiveExecution => _currentActiveExecution;
    public IAgentExecutionTracker? Tracker => _executionEngine.Tracker;
    public IWorkflowApprovalService ApprovalService => _approvalService;

    public event Action<WorkflowGraph>? OnGraphUpdated;
    public event Action<string, NodeExecutionState>? OnNodeStateChanged;
    public event Action<ActiveExecutionStatus?>? OnActiveExecutionChanged;

    public GraphWorkflowExecutor(
        ITicketStore ticketStore,
        IWorkDecompositionEngine decompositionEngine,
        IHandoffRouter handoffRouter,
        IAgentExecutionEngine executionEngine,
        IAgentEventStream eventStream,
        IMemoryConsolidationEngine memoryConsolidation,
        Learning.ISelfImprovementEngine? selfImprovement = null,
        ITeamDefinitionManager? teamManager = null,
        IArtifactManager? artifactManager = null,
        IWorkflowApprovalService? approvalService = null,
        IActiveProjectContext? activeProjectContext = null,
        IProjectManager? projectManager = null)
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
        _approvalService = approvalService ?? new WorkflowApprovalService(requireUserApproval: false);
        _activeProjectContext = activeProjectContext;
        _projectManager = projectManager;

        _executionEngine.OnStreamingChunk += OnChunkReceived;

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

    private void OnChunkReceived(StreamingChunkEvent evt)
    {
        if (_currentActiveExecution != null && _currentActiveExecution.TicketId == evt.TicketId)
        {
            var combined = _currentActiveExecution.LastSnippet + evt.Chunk;
            var snippet = combined.Length <= 400 ? combined : combined[^400..];
            _currentActiveExecution = _currentActiveExecution with
            {
                ChunksReceived = _currentActiveExecution.ChunksReceived + 1,
                LastSnippet = snippet,
                CurrentPhase = $"Streaming deliverable ({_currentActiveExecution.ChunksReceived + 1} chunks)"
            };
            OnActiveExecutionChanged?.Invoke(_currentActiveExecution);
        }
    }

    private void SetActiveExecution(ActiveExecutionStatus? status)
    {
        _currentActiveExecution = status;
        OnActiveExecutionChanged?.Invoke(_currentActiveExecution);
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

        SetActiveExecution(new ActiveExecutionStatus(
            Role: ticket.AssigneeRole,
            RoleName: ticket.AssigneeRole.ToDisplayName(),
            TicketId: ticket.Id,
            TicketTitle: ticket.Title,
            Model: "Auto-Resolved",
            StartedAt: DateTimeOffset.UtcNow,
            CurrentPhase: "Initiating LLM inference..."
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

            SetActiveExecution(null);

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
            SetActiveExecution(null);
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
            _ticketStore.UpdateTicket(ticket.WithStatus(TicketStatus.Blocked));
            return false;
        }
    }

    public async Task<bool> ExecuteReadyTicketsAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        try
        {
            int maxIterations = 100;
            int count = 0;
            var failedTicketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (count < maxIterations && !cancellationToken.IsCancellationRequested)
            {
                var readyTickets = _ticketStore.GetReadyTickets()
                    .Where(t => t.Type != TicketType.Epic && t.Status != TicketStatus.Done && !failedTicketIds.Contains(t.Id))
                    .ToList();

                if (readyTickets.Count == 0)
                {
                    break;
                }

                var nextTicket = readyTickets.First();

                if (nextTicket.AssigneeRole == AgentRole.SoftwareDeveloper && !string.IsNullOrEmpty(nextTicket.ParentEpicId))
                {
                    var parentEpic = _ticketStore.GetTicketById(nextTicket.ParentEpicId);
                    var devApproved = await EnsureArchitectToCoderApprovalAsync(
                        nextTicket.ParentEpicId,
                        parentEpic?.Title ?? nextTicket.Title,
                        nextTicket,
                        cancellationToken);
                    if (!devApproved)
                    {
                        break;
                    }
                }

                var executed = await ExecuteTicketAsync(nextTicket.Id, cancellationToken);
                if (!executed)
                {
                    failedTicketIds.Add(nextTicket.Id);
                    var otherReady = readyTickets.Where(t => t.Id != nextTicket.Id && !failedTicketIds.Contains(t.Id)).ToList();
                    if (otherReady.Count == 0)
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

                    _eventStream.Publish(AgentMessage.Create(
                        role: nextTicket.AssigneeRole,
                        senderName: "Workflow Engine",
                        content: $"⚠️ Execution failed on [{nextTicket.Id}]. Continuing with remaining independent ready ticket(s)...",
                        type: MessageType.Alert,
                        ticketId: nextTicket.Id
                    ));
                    continue;
                }
                count++;
                await Task.Delay(100, cancellationToken);
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
            SetActiveExecution(null);
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

        if (_activeProjectContext?.CurrentProject != null)
        {
            var touched = _activeProjectContext.CurrentProject.Touch();
            _activeProjectContext.SetActiveProject(touched);
            if (_projectManager != null)
            {
                _ = _projectManager.UpdateAsync(touched, cancellationToken);
            }
        }

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

            if (existingEpic != null && existingEpic.Status == TicketStatus.Done)
            {
                // Re-running an existing epic: reactivate it for a clean execution run
                existingEpic = existingEpic.WithStatus(TicketStatus.InProgress);
                _ticketStore.UpdateTicket(existingEpic);
            }

            // 1. Collaborative Discovery Stage: Requirements Researcher & Technical Product Manager
            var resNode = GetNodeByRole(AgentRole.RequirementsResearcher);
            ArtifactItem? researchBrief = existingEpic?.Deliverables.FirstOrDefault(d => d.Name.EndsWith("_RESEARCH_BRIEF.md", StringComparison.OrdinalIgnoreCase));

            // Also check research ticket deliverables if not directly on the epic
            if (researchBrief == null && existingEpic != null)
            {
                var existingResearchTicket = _ticketStore.GetTicketsByEpic(existingEpic.Id)
                    .FirstOrDefault(t => t.Type == TicketType.ResearchSpike && t.Status == TicketStatus.Done);
                researchBrief = existingResearchTicket?.Deliverables.FirstOrDefault(d => d.Name.EndsWith("_RESEARCH_BRIEF.md", StringComparison.OrdinalIgnoreCase));
            }

            IReadOnlyList<ArtifactItem>? newlyGeneratedResearchArtifacts = null;

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

                SetActiveExecution(new ActiveExecutionStatus(
                    Role: AgentRole.RequirementsResearcher,
                    RoleName: AgentRole.RequirementsResearcher.ToDisplayName(),
                    TicketId: tempResearchTicket.Id,
                    TicketTitle: tempResearchTicket.Title,
                    Model: "Auto-Resolved",
                    StartedAt: DateTimeOffset.UtcNow,
                    CurrentPhase: "Synthesizing deep feasibility research brief..."
                ));

                var researchArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.RequirementsResearcher, tempResearchTicket, cancellationToken);
                newlyGeneratedResearchArtifacts = researchArtifacts;
                researchBrief = researchArtifacts.FirstOrDefault();
                SetActiveExecution(null);

                foreach (var a in researchArtifacts)
                {
                    tempResearchTicket = tempResearchTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(tempResearchTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(tempResearchTicket.WithStatus(TicketStatus.Done));

                if (existingEpic != null && researchBrief != null && !existingEpic.Deliverables.Any(d => d.Name == researchBrief.Name))
                {
                    existingEpic = existingEpic.WithDeliverable(researchBrief);
                    _ticketStore.UpdateTicket(existingEpic);
                }

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

            if (!hasPrd || newlyGeneratedResearchArtifacts != null)
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

                SetActiveExecution(new ActiveExecutionStatus(
                    Role: AgentRole.TechnicalProductManager,
                    RoleName: AgentRole.TechnicalProductManager.ToDisplayName(),
                    TicketId: epicTicket.Id,
                    TicketTitle: $"PRD & Story Decomposition: {epicTitle}",
                    Model: "Auto-Resolved",
                    StartedAt: DateTimeOffset.UtcNow,
                    CurrentPhase: "Authoring PRD & extracting modular user stories..."
                ));

                var prdArtifacts = await _executionEngine.ExecuteRoleTaskAsync(AgentRole.TechnicalProductManager, epicTicket, cancellationToken);
                SetActiveExecution(null);
                foreach (var a in prdArtifacts)
                {
                    epicTicket = epicTicket.WithDeliverable(a);
                    if (_artifactManager != null)
                    {
                        await _artifactManager.SaveDeliverableArtifactAsync(epicTicket, a, cancellationToken);
                    }
                }
                _ticketStore.UpdateTicket(epicTicket);

                // Dynamically sync and extract modular User Stories from the authored PRD
                var primaryPrd = prdArtifacts.FirstOrDefault(a => a.Name.EndsWith("_PRD.md", StringComparison.OrdinalIgnoreCase)) ?? prdArtifacts.FirstOrDefault();
                IReadOnlyList<TicketItem> stories;
                if (primaryPrd != null && !string.IsNullOrWhiteSpace(primaryPrd.Content))
                {
                    stories = _decompositionEngine.SyncUserStoriesFromPrd(epicId, primaryPrd.Content, epicTicket.Priority);
                }
                else
                {
                    stories = _ticketStore.GetTicketsByEpic(epicId).Where(t => t.Type == TicketType.Feature).ToList();
                }

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

            // 2B. User Interaction Required Step: TPM ➔ Lead Architect Approval Gate
            var tpmApproved = await EnsureTpmToArchitectApprovalAsync(epicId, epicTitle, cancellationToken);
            if (!tpmApproved)
            {
                return false;
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

            // 4. Autonomous DAG Execution Loop: Drains all ready subtasks across all stories for this Epic
            int maxIterations = 100;
            int count = 0;

            while (count < maxIterations && !cancellationToken.IsCancellationRequested)
            {
                var epicTickets = _ticketStore.GetTicketsByEpic(epicId);
                var pendingSubtasks = epicTickets.Where(t => t.Type == TicketType.Subtask && t.Status != TicketStatus.Done).ToList();
                if (pendingSubtasks.Count == 0)
                {
                    break;
                }

                var readySubtasks = _ticketStore.GetReadyTickets()
                    .Where(t => t.Type == TicketType.Subtask && (t.ParentEpicId == epicId || string.IsNullOrWhiteSpace(t.ParentEpicId)) && t.Status != TicketStatus.Done)
                    .ToList();

                if (readySubtasks.Count == 0)
                {
                    // Check if any subtask is in remediating state
                    var remediating = pendingSubtasks.Where(t => t.Status == TicketStatus.Remediating).ToList();
                    if (remediating.Count > 0)
                    {
                        await ExecuteTicketAsync(remediating[0].Id, cancellationToken);
                        count++;
                        continue;
                    }

                    // Subtasks remain but none are currently ready
                    break;
                }

                var nextTicket = readySubtasks.First();

                // 4B. User Interaction Required Step: Lead Architect ➔ Coder (SoftwareDeveloper) Approval Gate
                if (nextTicket.AssigneeRole == AgentRole.SoftwareDeveloper)
                {
                    var devApproved = await EnsureArchitectToCoderApprovalAsync(epicId, epicTitle, nextTicket, cancellationToken);
                    if (!devApproved)
                    {
                        return false;
                    }
                }

                var executed = await ExecuteTicketAsync(nextTicket.Id, cancellationToken);
                if (!executed)
                {
                    var alternativeReady = readySubtasks.Where(t => t.Id != nextTicket.Id).ToList();
                    if (alternativeReady.Count > 0)
                    {
                        _eventStream.Publish(AgentMessage.Create(
                            role: nextTicket.AssigneeRole,
                            senderName: "Workflow Engine",
                            content: $"⚠️ Subtask [{nextTicket.Id}] failed. Proceeding with independent ready subtask [{alternativeReady[0].Id}]...",
                            type: MessageType.Alert,
                            ticketId: nextTicket.Id
                        ));
                        await ExecuteTicketAsync(alternativeReady[0].Id, cancellationToken);
                    }
                    else
                    {
                        _eventStream.Publish(AgentMessage.Create(
                            role: nextTicket.AssigneeRole,
                            senderName: "Workflow Engine",
                            content: $"🛑 Swarm execution halted on [{nextTicket.Id}] ({nextTicket.AssigneeRole.ToDisplayName()}).",
                            type: MessageType.Alert,
                            ticketId: nextTicket.Id
                        ));
                        break;
                    }
                }

                count++;
                await Task.Delay(100, cancellationToken);
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
            SetActiveExecution(null);
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

    private async Task<bool> EnsureTpmToArchitectApprovalAsync(
        string epicId,
        string epicTitle,
        CancellationToken cancellationToken)
    {
        if (_approvalService.IsGateApproved(epicId, ApprovalGateStage.TpmToArchitect))
        {
            return true;
        }

        var epicObj = _ticketStore.GetTicketById(epicId);
        var prdArtifact = epicObj?.Deliverables.FirstOrDefault(d => d.Name.EndsWith("_PRD.md", StringComparison.OrdinalIgnoreCase))
            ?? epicObj?.Deliverables.FirstOrDefault();

        var stories = _ticketStore.GetTicketsByEpic(epicId)
            .Where(t => t.Type == TicketType.Feature)
            .ToList();

        var items = new List<ApprovalItemSummary>();

        if (prdArtifact != null)
        {
            var preview = prdArtifact.Content.Length > 800 ? prdArtifact.Content[..800] + "..." : prdArtifact.Content;
            items.Add(new ApprovalItemSummary(
                Category: "Product Requirements Document (PRD)",
                Title: prdArtifact.Name,
                Details: preview,
                KeyPoints: [
                    $"Initiative: {epicTitle}",
                    $"Authored By: {AgentRole.TechnicalProductManager.ToDisplayName()}",
                    $"Specification Size: {prdArtifact.Content.Length} characters"
                ]
            ));
        }

        foreach (var story in stories)
        {
            items.Add(new ApprovalItemSummary(
                Category: "User Story (Feature)",
                Title: $"[{story.Id}] {story.Title}",
                Details: story.Description,
                KeyPoints: story.AcceptanceCriteria
            ));
        }

        var gate1ProjId = epicObj?.ProjectId ?? _activeProjectContext?.CurrentProjectId;
        var gate1Request = new WorkflowApprovalRequest(
            Id: $"APPR-TPM-ARCH-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            EpicId: epicId,
            ProjectId: gate1ProjId,
            Stage: ApprovalGateStage.TpmToArchitect,
            GateTitle: "User Approval Required: PRD & User Story Scope",
            GateDescription: $"The Technical Product Manager has authored the formal PRD and synthesized {stories.Count} foundational User Stories for initiative '{epicTitle}'. Your review and approval is required before the Lead Architect begins technical decomposition and architecture design.",
            NextStepDescription: "Authorize Lead Architect to refine user stories into technical execution subtasks, author Architectural Decision Record (ADR), and scaffold Clean Architecture contracts.",
            PrecedingRole: AgentRole.TechnicalProductManager,
            ProceedingRole: AgentRole.LeadArchitect,
            ItemsToApprove: items,
            Deliverables: prdArtifact != null ? [prdArtifact] : Array.Empty<ArtifactItem>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var tpmNode = GetNodeByRole(AgentRole.TechnicalProductManager);
        if (tpmNode != null)
        {
            UpdateNodeState(tpmNode.Id, NodeExecutionState.WaitingForApproval, "Awaiting User Approval of PRD & User Stories", epicId);
        }

        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.TechnicalProductManager,
            senderName: "Approval Gatekeeper",
            content: $"✋ USER INTERACTION REQUIRED: Approval requested for PRD and {stories.Count} user stories for '{epicTitle}'.",
            type: MessageType.Alert,
            ticketId: epicId,
            projectId: gate1ProjId
        ));

        SetActiveExecution(new ActiveExecutionStatus(
            Role: AgentRole.TechnicalProductManager,
            RoleName: AgentRole.TechnicalProductManager.ToDisplayName(),
            TicketId: epicId,
            TicketTitle: $"Awaiting Approval: {epicTitle}",
            Model: "Human Gate",
            StartedAt: DateTimeOffset.UtcNow,
            CurrentPhase: "Awaiting User Approval (TPM ➔ Lead Architect)..."
        ));

        var resolution = await _approvalService.RequestApprovalAsync(gate1Request, cancellationToken);
        SetActiveExecution(null);

        if (tpmNode != null)
        {
            UpdateNodeState(tpmNode.Id, NodeExecutionState.Completed, $"PRD authored & {stories.Count} stories approved for refinement.", epicId);
        }

        if (resolution.Status == ApprovalStatus.Rejected)
        {
            _eventStream.Publish(AgentMessage.Create(
                role: AgentRole.TechnicalProductManager,
                senderName: "Approval Gatekeeper",
                content: $"🛑 Workflow halted: User REJECTED approval for PRD & User Stories: {resolution.UserFeedback ?? "No reason provided."}",
                type: MessageType.Alert,
                ticketId: epicId,
                projectId: gate1ProjId
            ));
            return false;
        }

        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.LeadArchitect,
            senderName: "Approval Gatekeeper",
            content: $"✅ User APPROVED PRD & User Stories! Proceeding to Lead Architect refinement. Notes: {resolution.UserFeedback ?? "Approved without notes"}",
            type: MessageType.StateChange,
            ticketId: epicId,
            projectId: gate1ProjId
        ));

        return true;
    }

    private async Task<bool> EnsureArchitectToCoderApprovalAsync(
        string epicId,
        string epicTitle,
        TicketItem devTicket,
        CancellationToken cancellationToken)
    {
        if (_approvalService.IsGateApproved(epicId, ApprovalGateStage.ArchitectToCoder))
        {
            return true;
        }

        var allSubtasks = _ticketStore.GetTicketsByEpic(epicId)
            .Where(t => t.Type == TicketType.Subtask)
            .ToList();

        var adrDeliverables = allSubtasks
            .Where(t => t.AssigneeRole == AgentRole.LeadArchitect)
            .SelectMany(t => t.Deliverables)
            .ToList();

        if (adrDeliverables.Count == 0)
        {
            var epicTicketObj = _ticketStore.GetTicketById(epicId);
            if (epicTicketObj != null)
            {
                adrDeliverables = epicTicketObj.Deliverables
                    .Where(d => d.Name.EndsWith("_ADR.md", StringComparison.OrdinalIgnoreCase) || d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        var primaryAdr = adrDeliverables.FirstOrDefault(d => d.Name.EndsWith("_ADR.md", StringComparison.OrdinalIgnoreCase)) ?? adrDeliverables.FirstOrDefault();

        var items = new List<ApprovalItemSummary>();
        if (primaryAdr != null)
        {
            var adrPreview = primaryAdr.Content.Length > 800 ? primaryAdr.Content[..800] + "..." : primaryAdr.Content;
            items.Add(new ApprovalItemSummary(
                Category: "Architectural Decision Record (ADR)",
                Title: primaryAdr.Name,
                Details: adrPreview,
                KeyPoints: [
                    $"Initiative: {epicTitle}",
                    $"Authored By: {AgentRole.LeadArchitect.ToDisplayName()}",
                    $"Architecture: Clean Architecture & Zero-Allocation Hot Paths"
                ]
            ));
        }

        var companionCode = adrDeliverables.Where(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        if (companionCode.Count > 0)
        {
            items.Add(new ApprovalItemSummary(
                Category: "Clean Architecture Scaffolding",
                Title: $"{companionCode.Count} Companion Scaffold File(s)",
                Details: string.Join(", ", companionCode.Select(c => c.Name)),
                KeyPoints: companionCode.Select(c => $"File: {c.Name} ({c.Content.Length} chars)").ToList()
            ));
        }

        foreach (var st in allSubtasks.OrderBy(t => t.CreatedAt))
        {
            items.Add(new ApprovalItemSummary(
                Category: $"Technical Subtask ({st.AssigneeRole.ToDisplayName()})",
                Title: $"[{st.Id}] {st.Title}",
                Details: st.Description,
                KeyPoints: st.AcceptanceCriteria
            ));
        }

        var gate2ProjId = _ticketStore.GetTicketById(epicId)?.ProjectId ?? devTicket.ProjectId ?? _activeProjectContext?.CurrentProjectId;
        var gate2Request = new WorkflowApprovalRequest(
            Id: $"APPR-ARCH-DEV-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            EpicId: epicId,
            ProjectId: gate2ProjId,
            Stage: ApprovalGateStage.ArchitectToCoder,
            GateTitle: "User Approval Required: Architecture Design & Implementation Plan",
            GateDescription: $"The Lead Architect has finalized the Architectural Decision Record (ADR), scaffolded Clean Architecture contracts, and mapped {allSubtasks.Count} technical execution subtasks. Your review and approval is required before the Coder begins writing implementation code.",
            NextStepDescription: "Authorize Coder (Software Developer) to implement domain models, service methods, business logic, and xUnit unit tests strictly adhering to the approved ADR contracts.",
            PrecedingRole: AgentRole.LeadArchitect,
            ProceedingRole: AgentRole.SoftwareDeveloper,
            ItemsToApprove: items,
            Deliverables: adrDeliverables,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var devNode = GetNodeByRole(AgentRole.SoftwareDeveloper);
        if (devNode != null)
        {
            UpdateNodeState(devNode.Id, NodeExecutionState.WaitingForApproval, "Awaiting User Approval of ADR & Technical Plan", devTicket.Id);
        }

        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.LeadArchitect,
            senderName: "Approval Gatekeeper",
            content: $"✋ USER INTERACTION REQUIRED: Approval requested for ADR and {allSubtasks.Count} technical subtasks before Coder begins implementation.",
            type: MessageType.Alert,
            ticketId: devTicket.Id,
            projectId: gate2ProjId
        ));

        SetActiveExecution(new ActiveExecutionStatus(
            Role: AgentRole.LeadArchitect,
            RoleName: AgentRole.LeadArchitect.ToDisplayName(),
            TicketId: devTicket.Id,
            TicketTitle: $"Awaiting Approval: {devTicket.Title}",
            Model: "Human Gate",
            StartedAt: DateTimeOffset.UtcNow,
            CurrentPhase: "Awaiting User Approval (Lead Architect ➔ Coder)..."
        ));

        var resolution = await _approvalService.RequestApprovalAsync(gate2Request, cancellationToken);
        SetActiveExecution(null);

        if (devNode != null && devNode.State == NodeExecutionState.WaitingForApproval)
        {
            UpdateNodeState(devNode.Id, NodeExecutionState.Idle, null, devTicket.Id);
        }

        if (resolution.Status == ApprovalStatus.Rejected)
        {
            _eventStream.Publish(AgentMessage.Create(
                role: AgentRole.LeadArchitect,
                senderName: "Approval Gatekeeper",
                content: $"🛑 Workflow halted: User REJECTED approval for Architecture & Technical Plan: {resolution.UserFeedback ?? "No reason provided."}",
                type: MessageType.Alert,
                ticketId: devTicket.Id,
                projectId: gate2ProjId
            ));
            return false;
        }

        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.SoftwareDeveloper,
            senderName: "Approval Gatekeeper",
            content: $"✅ User APPROVED Architecture & Implementation Plan! Unleashing Coder (Software Developer). Notes: {resolution.UserFeedback ?? "Approved without notes"}",
            type: MessageType.StateChange,
            ticketId: devTicket.Id,
            projectId: gate2ProjId
        ));

        return true;
    }
}
