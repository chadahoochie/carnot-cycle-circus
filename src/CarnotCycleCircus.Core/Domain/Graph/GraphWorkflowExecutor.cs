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

    public async Task<bool> ExecuteWorkflowAsync(
        string epicTitle,
        string epicDescription,
        bool triggerFailureSimulation = false,
        CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        ResetGraph();

        _eventStream.Publish(AgentMessage.Create(
            role: null,
            senderName: "🎪 Circus Ringmaster",
            content: $"🎪 Ladies & Gentlemen! The Carnot Circus is officially running for Epic: '{epicTitle}'! (Panic/Failure Sim: {(triggerFailureSimulation ? "🚨 ARMED ('I've got a bad feeling about this')" : "😌 Disarmed ('Ludicrous speed, GO!')")})",
            type: MessageType.Alert
        ));

        // 1. TPM Phase - Work Decomposition
        var tpmNode = GetNodeByRole(AgentRole.TechnicalProductManager);
        if (tpmNode != null)
        {
            UpdateNodeState(tpmNode.Id, NodeExecutionState.Running);
            await Task.Delay(200, cancellationToken);

            var createdTickets = _decompositionEngine.DeconstructEpic(epicTitle, epicDescription);
            var epicTicket = createdTickets.First(t => t.Type == TicketType.Epic);

            _eventStream.Publish(AgentMessage.Create(
                role: AgentRole.TechnicalProductManager,
                senderName: "Barnum B. Buzzword (TPM)",
                content: $"🎯 TPM Barnum B. Buzzword: 'The new Jira backlog is here! The new Jira backlog is here! I'm somebody now!' Deconstructed into {createdTickets.Count - 1} stories & subtasks at Ludicrous Speed. 'So you're telling me there's a chance!'",
                type: MessageType.Chat,
                ticketId: epicTicket.Id
            ));

            UpdateNodeState(tpmNode.Id, NodeExecutionState.Completed, $"Decomposed into {createdTickets.Count} work items.", epicTicket.Id);
        }

        // 2. Lead Architect Phase - Architecture & ADR
        var archNode = GetNodeByRole(AgentRole.LeadArchitect);
        var readyTickets = _ticketStore.GetReadyTickets();
        var archTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect);

        if (archNode != null && archTicket != null)
        {
            UpdateNodeState(archNode.Id, NodeExecutionState.Running, ticketId: archTicket.Id);
            await Task.Delay(250, cancellationToken);

            var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.LeadArchitect, archTicket, cancellationToken);
            foreach (var a in artifacts)
            {
                archTicket = archTicket.WithDeliverable(a);
            }
            _ticketStore.UpdateTicket(archTicket);

            _handoffRouter.RouteSuccessHandoff(
                archTicket.Id,
                AgentRole.LeadArchitect,
                AgentRole.SoftwareDeveloper,
                "ADR Architecture & Topology finalized (Contains 24 layers of pure abstraction). 'Listen, strange developers lyin' in Slack distributin' interfaces is no basis for a system!'",
                "Devon: 'Surely you can't be serious?' 'I am serious, and don't call me Shirley.' Implement feature with zero heap allocations.",
                artifacts
            );

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(archTicket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(archTicket, _eventStream.GetHistory(), cancellationToken);

            UpdateNodeState(archNode.Id, NodeExecutionState.Completed, "ADR & Topology designed ('It's pronounced Fronkensteen!').", archTicket.Id);
        }

        // 3. Software Developer Phase - Implementation
        var devNode = GetNodeByRole(AgentRole.SoftwareDeveloper);
        readyTickets = _ticketStore.GetReadyTickets();
        var devTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper);

        if (devNode != null && devTicket != null)
        {
            UpdateNodeState(devNode.Id, NodeExecutionState.Running, ticketId: devTicket.Id);
            await Task.Delay(250, cancellationToken);

            var devArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, devTicket, cancellationToken);
            foreach (var a in devArtifacts)
            {
                devTicket = devTicket.WithDeliverable(a);
            }
            _ticketStore.UpdateTicket(devTicket);

            _handoffRouter.RouteSuccessHandoff(
                devTicket.Id,
                AgentRole.SoftwareDeveloper,
                AgentRole.SecurityEngineer,
                "Feature implemented! 'Now that's what I call high quality H2O / Span<T>!' Zero heap allocations, coldbrew-fueled unit tests.",
                "Sari & Otto: 'Like a glove!' Audit this before my cold brew gets warm.",
                devArtifacts
            );

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(devTicket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(devTicket, _eventStream.GetHistory(), cancellationToken);

            UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Implementation delivered ('Holy schnikes!').", devTicket.Id);
        }

        // 4. Parallel Security & Optimization Phase
        var secNode = GetNodeByRole(AgentRole.SecurityEngineer);
        var optNode = GetNodeByRole(AgentRole.OptimizationEngineer);
        readyTickets = _ticketStore.GetReadyTickets();
        var secTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SecurityEngineer && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SecurityEngineer);
        var optTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.OptimizationEngineer && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.OptimizationEngineer);

        if (secNode != null && secTicket != null)
        {
            UpdateNodeState(secNode.Id, NodeExecutionState.Running, ticketId: secTicket.Id);
            await Task.Delay(200, cancellationToken);

            if (triggerFailureSimulation && secNode.RetryCount == 0)
            {
                // Simulate security rejection & remediation loopback!
                UpdateNodeState(secNode.Id, NodeExecutionState.Failed, "Rejected: 'It's a trap!' Unsanitized input detected.", secTicket.Id);
                
                _handoffRouter.RouteFailureRemediation(
                    secTicket.Id,
                    AgentRole.SecurityEngineer,
                    AgentRole.SoftwareDeveloper,
                    "'Nobody expects the Spanish Inquisition!' Unsanitized input vector in service layer. 'He hates these cans/open ports!'",
                    "Wrap input in ReadOnlySpan<char> and sanitize with allow-list regex immediately."
                );

                if (devNode != null)
                {
                    UpdateNodeState(devNode.Id, NodeExecutionState.Remediating, "Fixing security vulnerability... ('Tis but a scratch!')");
                    await Task.Delay(250, cancellationToken);
                    UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Vulnerability remediated ('Like a glove! Just a flesh wound').");
                }

                UpdateNodeState(secNode.Id, NodeExecutionState.Running, ticketId: secTicket.Id);
                await Task.Delay(150, cancellationToken);
            }

            var secArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.SecurityEngineer, secTicket, cancellationToken);
            foreach (var a in secArtifacts) secTicket = secTicket.WithDeliverable(a);
            _ticketStore.UpdateTicket(secTicket);
            _handoffRouter.AdvanceWorkflowOnTicketCompletion(secTicket.Id);
            UpdateNodeState(secNode.Id, NodeExecutionState.Completed, "STRIDE Threat Model Approved ('Count to 3, no more, no less').", secTicket.Id);
        }

        if (optNode != null && optTicket != null)
        {
            UpdateNodeState(optNode.Id, NodeExecutionState.Running, ticketId: optTicket.Id);
            await Task.Delay(200, cancellationToken);

            var optArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.OptimizationEngineer, optTicket, cancellationToken);
            foreach (var a in optArtifacts) optTicket = optTicket.WithDeliverable(a);
            _ticketStore.UpdateTicket(optTicket);
            _handoffRouter.AdvanceWorkflowOnTicketCompletion(optTicket.Id);
            UpdateNodeState(optNode.Id, NodeExecutionState.Completed, "Zero-Allocations Verified ('Enhance... enhance... So I got that goin' for me, which is nice').", optTicket.Id);
        }

        // 5. Principal QA Phase - Validation & Certification
        var qaNode = GetNodeByRole(AgentRole.PrincipalQAAnalyst);
        readyTickets = _ticketStore.GetReadyTickets();
        var qaTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst);

        if (qaNode != null && qaTicket != null)
        {
            UpdateNodeState(qaNode.Id, NodeExecutionState.Running, ticketId: qaTicket.Id);
            await Task.Delay(250, cancellationToken);

            var qaArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.PrincipalQAAnalyst, qaTicket, cancellationToken);
            foreach (var a in qaArtifacts) qaTicket = qaTicket.WithDeliverable(a);
            _ticketStore.UpdateTicket(qaTicket);

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(qaTicket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(qaTicket, _eventStream.GetHistory(), cancellationToken);
            UpdateNodeState(qaNode.Id, NodeExecutionState.Completed, "QA Certification 100% Passed ('That's a lot of nuts!').", qaTicket.Id);

            _eventStream.Publish(AgentMessage.Create(
                role: AgentRole.PrincipalQAAnalyst,
                senderName: "Quinn the Build-Executioner (Principal QA)",
                content: "🧪 Quinn the Build-Executioner (QA): 'That's a lot of nuts!' Tortured the build with 50,000 demonic edge cases and null payloads. 'Shitter was full, but 'tis but a scratch!' Miraculously, everything passed! Production release certified: 'Alllllrighty then!'",
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

        // Mark remaining parent stories/epics as complete
        foreach (var remaining in _ticketStore.GetAllTickets().Where(t => t.Status != TicketStatus.Done))
        {
            _ticketStore.UpdateTicket(remaining.WithStatus(TicketStatus.Done));
        }

        _eventStream.Publish(AgentMessage.Create(
            role: null,
            senderName: "Workflow Orchestrator",
            content: $"🏆 Workflow Completed Successfully! All 6 engineering phases passed.",
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

        _isRunning = false;
        return true;
    }

    public async Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default)
    {
        var readyTickets = _ticketStore.GetReadyTickets();
        if (readyTickets.Count == 0) return false;

        var nextTicket = readyTickets.First();
        var node = GetNodeByRole(nextTicket.AssigneeRole);
        if (node == null) return false;

        UpdateNodeState(node.Id, NodeExecutionState.Running, ticketId: nextTicket.Id);
        await Task.Delay(150, cancellationToken);

        var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(nextTicket.AssigneeRole, nextTicket, cancellationToken);
        foreach (var a in artifacts) nextTicket = nextTicket.WithDeliverable(a);
        _ticketStore.UpdateTicket(nextTicket);

        _handoffRouter.AdvanceWorkflowOnTicketCompletion(nextTicket.Id);
        await _memoryConsolidation.ConsolidateTaskCompletionAsync(nextTicket, _eventStream.GetHistory(), cancellationToken);

        UpdateNodeState(node.Id, NodeExecutionState.Completed, $"Completed {nextTicket.Title}", nextTicket.Id);
        return true;
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
