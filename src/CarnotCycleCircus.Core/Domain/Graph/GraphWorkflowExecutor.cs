using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Graph;

public interface IGraphWorkflowExecutor
{
    WorkflowGraph CurrentGraph { get; }
    bool IsRunning { get; }
    void SetGraph(WorkflowGraph graph);
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
        bool triggerFailureSimulation = false,
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
    private readonly ISimulatedScenarioEngine _scenarioEngine;
    private readonly IAgentEventStream _eventStream;
    private readonly IMemoryConsolidationEngine _memoryConsolidation;
    private readonly Learning.ISelfImprovementEngine? _selfImprovement;
    private bool _isRunning;

    public WorkflowGraph CurrentGraph => _graph;
    public bool IsRunning => _isRunning;

    public event Action<WorkflowGraph>? OnGraphUpdated;
    public event Action<string, NodeExecutionState>? OnNodeStateChanged;

    public GraphWorkflowExecutor(
        ITicketStore ticketStore,
        IWorkDecompositionEngine decompositionEngine,
        IHandoffRouter handoffRouter,
        ISimulatedScenarioEngine scenarioEngine,
        IAgentEventStream eventStream,
        IMemoryConsolidationEngine memoryConsolidation,
        Learning.ISelfImprovementEngine? selfImprovement = null)
    {
        _graph = WorkflowGraph.CreateDefaultEngineeringCircus();
        _ticketStore = ticketStore;
        _decompositionEngine = decompositionEngine;
        _handoffRouter = handoffRouter;
        _scenarioEngine = scenarioEngine;
        _eventStream = eventStream;
        _memoryConsolidation = memoryConsolidation;
        _selfImprovement = selfImprovement;
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

        var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(ticket.AssigneeRole, ticket, cancellationToken);
        foreach (var a in artifacts)
        {
            ticket = ticket.WithDeliverable(a);
        }
        _ticketStore.UpdateTicket(ticket);

        // Record handoff to downstream role
        var nextRole = GetDownstreamRoleFor(ticket.AssigneeRole);
        if (nextRole.HasValue)
        {
            _handoffRouter.RouteSuccessHandoff(
                ticket.Id,
                ticket.AssigneeRole,
                nextRole.Value,
                $"Delivered [{ticket.Id}] {ticket.Title}. Attached {artifacts.Count} artifacts.",
                $"Proceed with downstream verification or implementation for {ticket.Title}.",
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
                await ExecuteTicketAsync(nextTicket.Id, cancellationToken);
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
        bool triggerFailureSimulation = false,
        CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        ResetGraph();

        try
        {
            _eventStream.Publish(AgentMessage.Create(
                role: null,
                senderName: "🎪 Circus Ringmaster",
                content: $"🎪 Ladies & Gentlemen! The Carnot Circus is officially running for Epic: '{epicTitle}'! (Panic/Failure Sim: {(triggerFailureSimulation ? "🚨 ARMED ('I've got a bad feeling about this')" : "😌 Disarmed ('Ludicrous speed, GO!')")})",
                type: MessageType.Alert
            ));

            // Check if tickets already exist for this epic or if we need to decompose
            var existingEpic = _ticketStore.GetAllTickets().FirstOrDefault(t => t.Type == TicketType.Epic && string.Equals(t.Title, epicTitle, StringComparison.OrdinalIgnoreCase));
            string epicId;

            if (existingEpic != null)
            {
                epicId = existingEpic.Id;
            }
            else
            {
                // 1. TPM Phase - Work Decomposition
                var tpmNode = GetNodeByRole(AgentRole.TechnicalProductManager);
                if (tpmNode != null)
                {
                    UpdateNodeState(tpmNode.Id, NodeExecutionState.Running);
                    await Task.Delay(150, cancellationToken);
                }

                var createdTickets = _decompositionEngine.DeconstructEpic(epicTitle, epicDescription);
                var epicTicket = createdTickets.First(t => t.Type == TicketType.Epic);
                epicId = epicTicket.Id;

                var prdArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.TechnicalProductManager, epicTicket, cancellationToken);
                foreach (var a in prdArtifacts)
                {
                    epicTicket = epicTicket.WithDeliverable(a);
                }
                _ticketStore.UpdateTicket(epicTicket);

                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.TechnicalProductManager,
                    senderName: "Barnum B. Buzzword (TPM)",
                    content: $"🎯 TPM Barnum B. Buzzword: 'The new Jira backlog is here! I'm somebody now!' Deconstructed '{epicTitle}' into {createdTickets.Count - 1} subtasks and produced Product Requirements Document (PRD).",
                    type: MessageType.Chat,
                    ticketId: epicTicket.Id
                ));

                if (tpmNode != null)
                {
                    UpdateNodeState(tpmNode.Id, NodeExecutionState.Completed, $"Decomposed into {createdTickets.Count} work items with PRD.", epicTicket.Id);
                }
            }

            // 2. Lead Architect Phase
            var archNode = GetNodeByRole(AgentRole.LeadArchitect);
            var readyTickets = _ticketStore.GetReadyTickets();
            var archTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect);

            if (archNode != null && archTicket != null)
            {
                UpdateNodeState(archNode.Id, NodeExecutionState.Running, ticketId: archTicket.Id);
                await Task.Delay(150, cancellationToken);

                var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.LeadArchitect, archTicket, cancellationToken);
                foreach (var a in artifacts) archTicket = archTicket.WithDeliverable(a);
                _ticketStore.UpdateTicket(archTicket);

                _handoffRouter.RouteSuccessHandoff(
                    archTicket.Id,
                    AgentRole.LeadArchitect,
                    AgentRole.SoftwareDeveloper,
                    "ADR Architecture & Topology finalized. 'Listen, strange developers lyin' in Slack distributin' interfaces is no basis for a system!'",
                    "Implement feature with zero heap allocations.",
                    artifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(archTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(archTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(archNode.Id, NodeExecutionState.Completed, "ADR & Topology designed.", archTicket.Id);
            }

            // 3. Software Developer Phase
            var devNode = GetNodeByRole(AgentRole.SoftwareDeveloper);
            readyTickets = _ticketStore.GetReadyTickets();
            var devTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper);

            if (devNode != null && devTicket != null)
            {
                UpdateNodeState(devNode.Id, NodeExecutionState.Running, ticketId: devTicket.Id);
                await Task.Delay(150, cancellationToken);

                var devArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, devTicket, cancellationToken);
                foreach (var a in devArtifacts) devTicket = devTicket.WithDeliverable(a);
                _ticketStore.UpdateTicket(devTicket);

                _handoffRouter.RouteSuccessHandoff(
                    devTicket.Id,
                    AgentRole.SoftwareDeveloper,
                    AgentRole.SecurityEngineer,
                    "Feature implemented! 'Now that's what I call high quality H2O / Span<T>!' Zero heap allocations.",
                    "Audit this before my cold brew gets warm.",
                    devArtifacts
                );

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(devTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(devTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Implementation delivered.", devTicket.Id);
            }

            // 4. Parallel Security & Optimization Phase
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

                if (triggerFailureSimulation && secNode.RetryCount == 0)
                {
                    UpdateNodeState(secNode.Id, NodeExecutionState.Failed, "Rejected: 'It's a trap!' Unsanitized input detected.", secTicket.Id);
                    
                    _handoffRouter.RouteFailureRemediation(
                        secTicket.Id,
                        AgentRole.SecurityEngineer,
                        AgentRole.SoftwareDeveloper,
                        "'Nobody expects the Spanish Inquisition!' Unsanitized input vector in service layer.",
                        "Wrap input in ReadOnlySpan<char> and sanitize with allow-list regex immediately."
                    );

                    if (devNode != null)
                    {
                        UpdateNodeState(devNode.Id, NodeExecutionState.Remediating, "Fixing security vulnerability... ('Tis but a scratch!')");
                        await Task.Delay(150, cancellationToken);
                        UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Vulnerability remediated ('Like a glove!').");
                    }

                    UpdateNodeState(secNode.Id, NodeExecutionState.Running, ticketId: secTicket.Id);
                    await Task.Delay(100, cancellationToken);
                }

                var secArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.SecurityEngineer, secTicket, cancellationToken);
                foreach (var a in secArtifacts) secTicket = secTicket.WithDeliverable(a);
                _ticketStore.UpdateTicket(secTicket);
                _handoffRouter.AdvanceWorkflowOnTicketCompletion(secTicket.Id);
                UpdateNodeState(secNode.Id, NodeExecutionState.Completed, "STRIDE Threat Model Approved.", secTicket.Id);
            }

            if (optNode != null && optTicket != null)
            {
                UpdateNodeState(optNode.Id, NodeExecutionState.Running, ticketId: optTicket.Id);
                await Task.Delay(150, cancellationToken);

                var optArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.OptimizationEngineer, optTicket, cancellationToken);
                foreach (var a in optArtifacts) optTicket = optTicket.WithDeliverable(a);
                _ticketStore.UpdateTicket(optTicket);
                _handoffRouter.AdvanceWorkflowOnTicketCompletion(optTicket.Id);
                UpdateNodeState(optNode.Id, NodeExecutionState.Completed, "Zero-Allocations Verified.", optTicket.Id);
            }

            // 5. Principal QA Phase
            var qaNode = GetNodeByRole(AgentRole.PrincipalQAAnalyst);
            readyTickets = _ticketStore.GetReadyTickets();
            var qaTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst && t.ParentEpicId == epicId)
                ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst);

            if (qaNode != null && qaTicket != null)
            {
                UpdateNodeState(qaNode.Id, NodeExecutionState.Running, ticketId: qaTicket.Id);
                await Task.Delay(150, cancellationToken);

                var qaArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.PrincipalQAAnalyst, qaTicket, cancellationToken);
                foreach (var a in qaArtifacts) qaTicket = qaTicket.WithDeliverable(a);
                _ticketStore.UpdateTicket(qaTicket);

                _handoffRouter.AdvanceWorkflowOnTicketCompletion(qaTicket.Id);
                await _memoryConsolidation.ConsolidateTaskCompletionAsync(qaTicket, _eventStream.GetHistory(), cancellationToken);
                UpdateNodeState(qaNode.Id, NodeExecutionState.Completed, "QA Certification 100% Passed.", qaTicket.Id);

                _eventStream.Publish(AgentMessage.Create(
                    role: AgentRole.PrincipalQAAnalyst,
                    senderName: "Quinn the Build-Executioner (Principal QA)",
                    content: "🧪 Quinn the Build-Executioner (QA): 'That's a lot of nuts!' Tortured the build with 50,000 edge cases. Production release certified: 'Alllllrighty then!'",
                    type: MessageType.StateChange,
                    ticketId: qaTicket.Id
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
        finally
        {
            _isRunning = false;
            OnGraphUpdated?.Invoke(_graph);
        }
    }

    public async Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default)
    {
        var readyTickets = _ticketStore.GetReadyTickets()
            .Where(t => t.Type != TicketType.Epic && t.Status != TicketStatus.Done)
            .ToList();

        if (readyTickets.Count == 0) return false;

        var nextTicket = readyTickets.First();
        return await ExecuteTicketAsync(nextTicket.Id, cancellationToken);
    }

    private AgentRole? GetDownstreamRoleFor(AgentRole currentRole)
    {
        var node = GetNodeByRole(currentRole);
        if (node != null)
        {
            var outboundConn = _graph.Connections.FirstOrDefault(c => c.SourceNodeId == node.Id && c.SourcePort == PortType.Output);
            if (outboundConn != null)
            {
                var targetNode = _graph.Nodes.FirstOrDefault(n => n.Id == outboundConn.TargetNodeId);
                if (targetNode != null)
                {
                    return targetNode.Role;
                }
            }
        }

        return currentRole switch
        {
            AgentRole.TechnicalProductManager => AgentRole.LeadArchitect,
            AgentRole.LeadArchitect => AgentRole.SoftwareDeveloper,
            AgentRole.SoftwareDeveloper => AgentRole.SecurityEngineer,
            AgentRole.SecurityEngineer => AgentRole.PrincipalQAAnalyst,
            AgentRole.OptimizationEngineer => AgentRole.PrincipalQAAnalyst,
            _ => null
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
