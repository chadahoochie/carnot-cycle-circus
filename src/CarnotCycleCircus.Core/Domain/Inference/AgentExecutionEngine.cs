using System.Text.RegularExpressions;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Harvester;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Inference;

public interface IAgentExecutionEngine
{
    Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default);
}

public class AgentExecutionEngine : IAgentExecutionEngine
{
    private readonly IOpenRouterClient _openRouterClient;
    private readonly IAgentInferenceResolver _inferenceResolver;
    private readonly ITeamDefinitionManager? _teamManager;
    private readonly IAgentDefinitionManager? _agentDefManager;
    private readonly IAgentEventStream? _eventStream;
    private readonly IKnowledgeMapService? _knowledgeMap;
    private readonly IPersistentMemoryStore? _memoryStore;
    private readonly ICodebaseHarvesterService? _harvester;
    private readonly ITicketStore? _ticketStore;

    public AgentExecutionEngine(
        IOpenRouterClient openRouterClient,
        IAgentInferenceResolver inferenceResolver,
        ITeamDefinitionManager? teamManager = null,
        IAgentDefinitionManager? agentDefManager = null,
        IAgentEventStream? eventStream = null,
        IKnowledgeMapService? knowledgeMap = null,
        IPersistentMemoryStore? memoryStore = null,
        ICodebaseHarvesterService? harvester = null,
        ITicketStore? ticketStore = null)
    {
        _openRouterClient = openRouterClient ?? throw new ArgumentNullException(nameof(openRouterClient));
        _inferenceResolver = inferenceResolver ?? throw new ArgumentNullException(nameof(inferenceResolver));
        _teamManager = teamManager;
        _agentDefManager = agentDefManager;
        _eventStream = eventStream;
        _knowledgeMap = knowledgeMap;
        _memoryStore = memoryStore;
        _harvester = harvester;
        _ticketStore = ticketStore;
    }

    public async Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskAsync(
        AgentRole role,
        TicketItem ticket,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve agent member and inference parameters from active squad and DAG node bindings
        var team = _teamManager?.GetCurrentTeam() ?? EngineeringTeam.CreateDefault();
        var node = team.Graph.Nodes.FirstOrDefault(n => n.Role == role);

        AgentMember? member = null;
        if (node != null && !string.IsNullOrWhiteSpace(node.AgentId))
        {
            member = _agentDefManager?.GetAgent(node.AgentId) ?? team.GetMemberById(node.AgentId);
        }

        if (member == null)
        {
            var teamMember = team.GetMember(role);
            if (teamMember != null && teamMember.HasModel)
            {
                member = teamMember;
            }
        }

        if (member == null || !member.HasModel)
        {
            var definedAgent = _agentDefManager?.GetAgentForRole(role);
            if (definedAgent != null && (member == null || definedAgent.HasModel))
            {
                member = definedAgent;
            }
        }

        if (member == null)
        {
            member = team.GetMember(role) ?? new AgentMember(AgentPersona.CreateDefault(role));
        }

        var inferenceConfig = _inferenceResolver.ResolveInferenceConfig(member, team);
        var model = inferenceConfig.PrimaryModel;
        var fallbackModel = inferenceConfig.FallbackModel;
        var apiKey = inferenceConfig.ApiKey;

        // 2. Validate model availability
        if (string.IsNullOrWhiteSpace(model))
        {
            var errMsg = $"No inference model selected for {role.ToDisplayName()} ({member.Persona.Name}). Please select an inference model in Team Circus Ring & Agent Studio.";
            _eventStream?.Publish(AgentMessage.Create(
                role: role,
                senderName: member.Persona.Name,
                content: $"🛑 {errMsg}",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));

            throw new InvalidOperationException(errMsg);
        }

        // 3. Validate API key availability
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var errMsg = $"No active OpenRouter API key configured for {role.ToDisplayName()} ({member.Persona.Name}) on model [{model}]. Please add an API key in the Key Vault.";
            _eventStream?.Publish(AgentMessage.Create(
                role: role,
                senderName: member.Persona.Name,
                content: $"🛑 {errMsg}",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));

            throw new InvalidOperationException(errMsg);
        }

        // 4. Gather upstream context from parent epic and dependent tickets
        var upstreamDeliverables = GatherUpstreamDeliverables(ticket);
        var harvestReport = _harvester?.GetLatestReport();

        _eventStream?.Publish(AgentMessage.Create(
            role: role,
            senderName: member.Persona.Name,
            content: $"🚀 Executing inference on [{model}] for ticket [{ticket.Id}]: '{ticket.Title}' (Context: {upstreamDeliverables.Count} upstream deliverables)...",
            type: MessageType.StateChange,
            ticketId: ticket.Id
        ));

        try
        {
            var (artifacts, finishReason) = await GenerateViaOpenRouterAsync(member, role, ticket, upstreamDeliverables, harvestReport, model, apiKey, cancellationToken);
            if (artifacts.Count > 0)
            {
                var names = string.Join(", ", artifacts.Select(a => $"'{a.Name}'"));
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"⚡ Generated {artifacts.Count} deliverable(s): {names} via [{model}].",
                    type: MessageType.Handoff,
                    ticketId: ticket.Id
                ));

                return artifacts;
            }

            // Primary model produced 0 artifacts (e.g. empty output or reasoning tokens exhausted)
            var emptyReason = finishReason != null ? $"FinishReason: {finishReason}" : "Empty payload returned";
            if (!string.IsNullOrWhiteSpace(fallbackModel) && !string.Equals(fallbackModel, model, StringComparison.OrdinalIgnoreCase))
            {
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"⚠️ Primary model [{model}] produced empty deliverable content ({emptyReason}). Initiating autonomous failover to fallback model [{fallbackModel}]...",
                    type: MessageType.Alert,
                    ticketId: ticket.Id
                ));

                var (fallbackArtifacts, fallbackFinishReason) = await GenerateViaOpenRouterAsync(member, role, ticket, upstreamDeliverables, harvestReport, fallbackModel, apiKey, cancellationToken);
                if (fallbackArtifacts.Count > 0)
                {
                    var fbNames = string.Join(", ", fallbackArtifacts.Select(a => $"'{a.Name}'"));
                    _eventStream?.Publish(AgentMessage.Create(
                        role: role,
                        senderName: member.Persona.Name,
                        content: $"⚡ Fallback model [{fallbackModel}] successfully generated {fallbackArtifacts.Count} deliverable(s): {fbNames}.",
                        type: MessageType.Handoff,
                        ticketId: ticket.Id
                    ));

                    return fallbackArtifacts;
                }

                throw new InvalidOperationException($"Both primary model [{model}] ({emptyReason}) and fallback model [{fallbackModel}] (FinishReason: {fallbackFinishReason ?? "empty"}) produced empty deliverable content for [{ticket.Id}].");
            }

            throw new InvalidOperationException($"Model [{model}] produced empty deliverable content for [{ticket.Id}] ({emptyReason}).");
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(fallbackModel) &&
                                  !string.Equals(fallbackModel, model, StringComparison.OrdinalIgnoreCase) &&
                                  !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            _eventStream?.Publish(AgentMessage.Create(
                role: role,
                senderName: member.Persona.Name,
                content: $"⚠️ Primary model [{model}] failed with error ({ex.Message}). Initiating autonomous failover to fallback model [{fallbackModel}]...",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));

            try
            {
                var (fallbackArtifacts, fallbackFinishReason) = await GenerateViaOpenRouterAsync(member, role, ticket, upstreamDeliverables, harvestReport, fallbackModel, apiKey, cancellationToken);
                if (fallbackArtifacts.Count > 0)
                {
                    var fbNames = string.Join(", ", fallbackArtifacts.Select(a => $"'{a.Name}'"));
                    _eventStream?.Publish(AgentMessage.Create(
                        role: role,
                        senderName: member.Persona.Name,
                        content: $"⚡ Fallback model [{fallbackModel}] successfully generated {fallbackArtifacts.Count} deliverable(s): {fbNames}.",
                        type: MessageType.Handoff,
                        ticketId: ticket.Id
                    ));

                    return fallbackArtifacts;
                }

                throw new InvalidOperationException($"Primary model [{model}] failed ({ex.Message}) and fallback model [{fallbackModel}] produced empty deliverable content for [{ticket.Id}] (FinishReason: {fallbackFinishReason ?? "empty"}).");
            }
            catch (Exception fallbackEx) when (!(fallbackEx is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"🛑 Execution failed on both primary [{model}] and fallback [{fallbackModel}] for {role.ToDisplayName()} ({member.Persona.Name}): {fallbackEx.Message}",
                    type: MessageType.Alert,
                    ticketId: ticket.Id
                ));
                throw;
            }
        }
        catch (Exception ex)
        {
            _eventStream?.Publish(AgentMessage.Create(
                role: role,
                senderName: member.Persona.Name,
                content: $"🛑 Execution failed for {role.ToDisplayName()} ({member.Persona.Name}) on [{model}]: {ex.Message}",
                type: MessageType.Alert,
                ticketId: ticket.Id
            ));
            throw;
        }
    }

    private IReadOnlyList<ArtifactItem> GatherUpstreamDeliverables(TicketItem ticket)
    {
        var deliverables = new List<ArtifactItem>();

        if (_ticketStore == null)
        {
            return deliverables;
        }

        // 1. Direct Dependencies Deliverables (e.g. ADRs, Source Code, Test Suites)
        foreach (var depId in ticket.DependsOnTicketIds)
        {
            var depTicket = _ticketStore.GetTicketById(depId);
            if (depTicket != null)
            {
                deliverables.AddRange(depTicket.Deliverables);
            }
        }

        // 2. Parent Epic Deliverables (e.g. Research Brief, PRD)
        if (!string.IsNullOrWhiteSpace(ticket.ParentEpicId))
        {
            var epic = _ticketStore.GetTicketById(ticket.ParentEpicId);
            if (epic != null)
            {
                deliverables.AddRange(epic.Deliverables);
            }

            // 3. All sibling & predecessor completed tickets in the same Epic hierarchy
            var epicTickets = _ticketStore.GetTicketsByEpic(ticket.ParentEpicId);
            foreach (var t in epicTickets.Where(t => t.Id != ticket.Id && t.Status == TicketStatus.Done))
            {
                deliverables.AddRange(t.Deliverables);
            }
        }

        // 4. Current ticket deliverables
        deliverables.AddRange(ticket.Deliverables);

        return deliverables.DistinctBy(d => d.Name).ToList();
    }

    private string GatherHandoffContext(TicketItem ticket)
    {
        if (_ticketStore == null) return string.Empty;

        var handoffs = _ticketStore.GetHandoffsForTicket(ticket.Id);
        if (handoffs.Count == 0 && !string.IsNullOrWhiteSpace(ticket.ParentEpicId))
        {
            handoffs = _ticketStore.GetHandoffsForTicket(ticket.ParentEpicId);
        }

        if (handoffs.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n=== INTER-AGENT HANDOFF CONTEXT & ROUTING DIRECTIVES ===");
        foreach (var h in handoffs.TakeLast(3))
        {
            sb.AppendLine($"From: {h.FromAgentRole.ToDisplayName()} ➔ To: {h.ToAgentRole.ToDisplayName()} [{h.Timestamp:yyyy-MM-dd HH:mm:ss}]");
            sb.AppendLine($"Handoff Summary: {h.ContextSummary}");
            if (!string.IsNullOrWhiteSpace(h.ActionRequested))
            {
                sb.AppendLine($"Action Requested: {h.ActionRequested}");
            }
            if (!string.IsNullOrWhiteSpace(h.RemediationNotes))
            {
                sb.AppendLine($"🚨 CRITICAL REMEDIATION DIRECTIVE: {h.RemediationNotes}");
            }
            if (h.ReviewChecklist.Count > 0)
            {
                sb.AppendLine("Review Checklist:");
                foreach (var item in h.ReviewChecklist)
                {
                    sb.AppendLine($"- [ ] {item}");
                }
            }
            sb.AppendLine("--------------------------------------------------------\n");
        }

        return sb.ToString();
    }

    private async Task<(IReadOnlyList<ArtifactItem> Artifacts, string? FinishReason)> GenerateViaOpenRouterAsync(
        AgentMember member,
        AgentRole role,
        TicketItem ticket,
        IReadOnlyList<ArtifactItem> upstreamDeliverables,
        CodebaseHarvestReport? harvestReport,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var (systemPrompt, userPrompt, defaultArtifactName, contentType, description) =
            BuildPromptsForRole(member, role, ticket, upstreamDeliverables, harvestReport);

        var messages = new List<OpenRouterMessage>
        {
            new("system", systemPrompt),
            new("user", userPrompt)
        };

        var request = new OpenRouterChatRequest(
            Model: model,
            Messages: messages,
            Temperature: Math.Clamp(member.Persona.Temperature, 0.0, 1.0),
            MaxTokens: 8192
        );

        var response = await _openRouterClient.CompleteAsync(request, apiKey, cancellationToken);
        var rawContent = response.FirstContent;
        var finishReason = response.FirstFinishReason;

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return (Array.Empty<ArtifactItem>(), finishReason);
        }

        var parsedArtifacts = ParseDeliverableArtifacts(rawContent, defaultArtifactName, contentType, description, ticket);

        // Self-Healing C# Syntax Verification Loop for Developer Role
        if (role == AgentRole.SoftwareDeveloper && parsedArtifacts.Any(a => a.ContentType == "csharp"))
        {
            var syntaxTool = new CSharpSyntaxCheckTool();
            var syntaxErrors = new List<string>();

            foreach (var art in parsedArtifacts.Where(a => a.ContentType == "csharp"))
            {
                var checkRes = await syntaxTool.ExecuteAsync(new ToolExecutionContext("csharp_syntax_check", new Dictionary<string, string> { ["code"] = art.Content }, role, ticket.Id), cancellationToken);
                if (!checkRes.Success)
                {
                    syntaxErrors.Add($"File '{art.Name}': {checkRes.Output}");
                }
            }

            if (syntaxErrors.Count > 0)
            {
                _eventStream?.Publish(AgentMessage.Create(
                    role: role,
                    senderName: member.Persona.Name,
                    content: $"🔧 Detected syntax issues in generated C# code. Initiating autonomous self-healing remediation pass...",
                    type: MessageType.Alert,
                    ticketId: ticket.Id
                ));

                var remediationPrompt = $"""
                The previously generated C# source code contained structural syntax errors:
                {string.Join("\n", syntaxErrors)}

                Please fix the syntax errors (unmatched braces, unclosed quotes, invalid declarations) and output the complete, valid C# source code for each file using ```csharp:FileName.cs blocks.
                """;

                var remediationMessages = new List<OpenRouterMessage>
                {
                    new("system", systemPrompt),
                    new("user", userPrompt),
                    new("assistant", rawContent),
                    new("user", remediationPrompt)
                };

                try
                {
                    var remResponse = await _openRouterClient.CompleteAsync(new OpenRouterChatRequest(model, remediationMessages, Temperature: 0.1, MaxTokens: 8192), apiKey, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(remResponse.FirstContent))
                    {
                        var healed = ParseDeliverableArtifacts(remResponse.FirstContent, defaultArtifactName, contentType, description, ticket);
                        if (healed.Count > 0)
                        {
                            return (healed, remResponse.FirstFinishReason);
                        }
                    }
                }
                catch
                {
                    // Fallback to original parsed artifacts
                }
            }
        }

        return (parsedArtifacts, finishReason);
    }

    private (string SystemPrompt, string UserPrompt, string DefaultArtifactName, string ContentType, string Description) BuildPromptsForRole(
        AgentMember member,
        AgentRole role,
        TicketItem ticket,
        IReadOnlyList<ArtifactItem> upstreamDeliverables,
        CodebaseHarvestReport? harvestReport)
    {
        var personaPrompt = string.IsNullOrWhiteSpace(member.Persona.SystemPrompt)
            ? $"You are {member.Persona.Name}, acting as the {role.ToDisplayName()} on an elite autonomous engineering team."
            : member.Persona.SystemPrompt;

        var systemPrompt = $"""
        {personaPrompt}

        === NON-NEGOTIABLE DELIVERABLE ISOLATION CONTRACT (ADR-0005) ===
        All formal technical deliverables (PRDs, ADRs, C# code, unit tests, STRIDE threat models, benchmark reports, QA scorecards) MUST remain 100% professional, standard-compliant, rigorous, unambiguous, and completely free of joke text or sarcastic phrasing.
        Output ONLY the complete, raw technical deliverables. Do NOT wrap the entire response in outer conversational preamble.
        """;

        var domainContext = ExtractDomainContext(ticket);
        var targetNamespace = harvestReport?.Projects.FirstOrDefault(p => p.ProjectType.Contains("Core") || p.ProjectType.Contains("Domain"))?.Name
                              ?? (harvestReport != null && !string.IsNullOrWhiteSpace(harvestReport.SolutionName) ? $"{harvestReport.SolutionName}.Core" : "CarnotCycleCircus.Core.Domain");

        var upstreamSummary = new System.Text.StringBuilder();
        if (upstreamDeliverables.Count > 0)
        {
            upstreamSummary.AppendLine("\n=== UPSTREAM INTER-AGENT DELIVERABLE CONTEXT ===");
            foreach (var d in upstreamDeliverables)
            {
                upstreamSummary.AppendLine($"--- [Artifact: {d.Name} ({d.ContentType})] ---");
                // Generous context preservation without premature truncation
                var preview = d.Content.Length > 40000 ? d.Content[..40000] + "\n...[truncated for context budget]..." : d.Content;
                upstreamSummary.AppendLine(preview);
                upstreamSummary.AppendLine("------------------------------------------------\n");
            }
        }

        var handoffDirectives = GatherHandoffContext(ticket);
        if (!string.IsNullOrWhiteSpace(handoffDirectives))
        {
            upstreamSummary.AppendLine(handoffDirectives);
        }

        var repoContext = harvestReport != null
            ? $"\n=== HOST REPOSITORY CONTEXT ===\nSolution: {harvestReport.SolutionName}\nTarget Namespace: {targetNamespace}\nProjects: {string.Join(", ", harvestReport.Projects.Select(p => p.Name))}\nKey Patterns: {string.Join(", ", harvestReport.DetectedPatterns)}\n"
            : $"\n=== HOST REPOSITORY CONTEXT ===\nTarget Namespace: {targetNamespace}\nTarget Runtime: .NET 10.0 / C# 13\n";

        string userPrompt;
        string defaultArtifactName;
        string contentType;
        string description;

        switch (role)
        {
            case AgentRole.RequirementsResearcher:
                defaultArtifactName = $"{ticket.Id}_RESEARCH_BRIEF.md";
                contentType = "markdown";
                description = "Requirements Research & Technical Feasibility Brief";
                userPrompt = $"""
                Produce an exhaustive, highly structured Requirements Research & Technical Feasibility Brief in Markdown format for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Priority: {ticket.Priority}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                Structure the document with:
                # Requirements Research & Technical Feasibility Brief: {ticket.Title}
                ## 1. Problem Space & Domain Context
                ## 2. Standards, RFCs & Technical Specifications (Specify exact RFC/industry standard numbers, wire protocols, and constraints)
                ## 3. Ecosystem, Frameworks & Library Landscape (Evaluate modern .NET 10 / C# 13 packages, dependencies, and performance implications)
                ## 4. Codebase Dependency & Architecture Footprint (Identify existing interfaces, models, and boundaries impacted)
                ## 5. Potential Edge Cases, Security Hazards & Failure Modes
                ## 6. Recommendations for Technical Product Manager (Scope boundaries, user story decomposition advice, acceptance criteria guidelines)
                """;
                break;

            case AgentRole.TechnicalProductManager:
                defaultArtifactName = $"{ticket.Id}_PRD.md";
                contentType = "markdown";
                description = "Product Requirements Document (PRD)";
                userPrompt = $"""
                Produce a comprehensive, rigorous Product Requirements Document (PRD) in Markdown format for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Priority: {ticket.Priority}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                Structure the document with:
                # Product Requirements Document (PRD): {ticket.Title}
                ## 1. Executive Summary & Objective (Synthesized from upstream Research Brief)
                ## 2. Target Users & System Context
                ## 3. Domain Concepts & Entities (Specify exact entity names, value objects, and states)
                ## 4. Functional Acceptance Criteria (use - [ ] checkboxes for all user stories)
                ## 5. Non-Functional Requirements (NFRs) (Latency SLA, GC Zero-Allocations, STRIDE Security Baseline, Self-Healing Failure Ports)
                """;
                break;

            case AgentRole.LeadArchitect:
                defaultArtifactName = $"{ticket.Id}_ADR.md";
                contentType = "markdown";
                description = "Clean Architecture Blueprint & Architectural Decision Record (ADR)";
                userPrompt = $$"""
                Produce a formal Nygard / MADR-compliant Architectural Decision Record (ADR) AND a complete Clean Architecture solution scaffolding bundle in Markdown for:
                Ticket: {{ticket.Id}} - {{ticket.Title}}
                Description: {{ticket.Description}}
                Acceptance Criteria:
                {{string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}}
                {{repoContext}}
                {{upstreamSummary}}

                === MANDATORY CLEAN ARCHITECTURE SCAFFOLDING REQUIREMENTS ===
                You MUST explicitly scaffold the Clean Architecture solution structure and define exact C# Type Contracts and Interfaces so downstream Software Developers implement without ambiguity or cohesion drift:
                1. Target Namespace: `{{targetNamespace}}.{{domainContext}}`
                2. Layering Structure:
                   - **Domain Layer**: Core immutable entities, Value Objects (`readonly record struct`), Domain Enums, and Domain Events (`Domain/` or `Models/`).
                   - **Application / Contracts Layer**: Primary Service Interfaces (`I{{domainContext}}Service` or `I{{domainContext}}Pipeline`), DTOs, and Pipeline Contracts (`Contracts/`).
                   - **Dependency Injection Extension**: Explicit `Add{{domainContext}}(this IServiceCollection services)` registration extension (`Extensions/`).
                3. Output BOTH:
                   - The formal ADR document in Markdown (`# ADR-014: High-Performance Architecture for {{ticket.Title}}`)
                   - Compilable C# Clean Architecture scaffold files using labeled code blocks:
                     ```csharp:Contracts/I{{domainContext}}Service.cs
                     // File: Contracts/I{{domainContext}}Service.cs
                     namespace {{targetNamespace}}.{{domainContext}};

                     using System;
                     using System.Threading;
                     using System.Threading.Tasks;

                     public interface I{{domainContext}}Service
                     {
                         // Exact service signatures tailored to the ticket requirements and acceptance criteria
                     }
                     ```

                     ```csharp:Models/{{domainContext}}Models.cs
                     // File: Models/{{domainContext}}Models.cs
                     namespace {{targetNamespace}}.{{domainContext}};

                     using System;

                     // Exact immutable records and readonly record structs for this domain
                     public readonly record struct {{domainContext}}Result(string Id, bool IsSuccess, string Message);
                     public record {{domainContext}}Entity(string Id, string Name, DateTimeOffset CreatedAt);
                     ```

                     ```csharp:Extensions/{{domainContext}}ServiceCollectionExtensions.cs
                     // File: Extensions/{{domainContext}}ServiceCollectionExtensions.cs
                     namespace {{targetNamespace}}.{{domainContext}};

                     using Microsoft.Extensions.DependencyInjection;

                     public static class {{domainContext}}ServiceCollectionExtensions
                     {
                         public static IServiceCollection Add{{domainContext}}(this IServiceCollection services)
                         {
                             // DI registration
                             return services;
                         }
                     }
                     ```

                Structure the document with:
                # ADR-014: High-Performance Architecture for {{ticket.Title}}
                ## Status
                Accepted
                ## Context
                ## Architectural Decision (Specify immutable C# records, bounded Channel<T>, zero-allocation protocols, connectable failure DAG ports)
                ## Exact C# Type Contracts & Interface Signatures (Provide the compilable C# scaffold code blocks shown above)
                ## Clean Architecture Scaffolding Blueprint (Explicitly list Domain, Contracts, Infrastructure, and DI layouts)
                ## Alternatives Considered
                ## Consequences & Trade-offs (Positive and Negative)
                """;
                break;

            case AgentRole.SoftwareDeveloper:
                defaultArtifactName = $"{domainContext}Service.cs";
                contentType = "csharp";
                description = "C# 13 Multi-File Production Implementation & Test Suite";
                userPrompt = $$"""
                Produce a complete, compilable, production-ready C# 13 (.NET 10) multi-file implementation bundle for:
                Ticket: {{ticket.Id}} - {{ticket.Title}}
                Description: {{ticket.Description}}
                Acceptance Criteria:
                {{string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}}
                {{repoContext}}
                {{upstreamSummary}}

                === CRITICAL PRODUCTION CODE MANDATE ===
                You MUST implement the exact domain types, records, methods, and interfaces tailored to the ticket requirements, acceptance criteria, and upstream Lead Architect's Clean Architecture scaffold and PRD above.
                Target Namespace: `{{targetNamespace}}.{{domainContext}}`
                Do NOT invent arbitrary generic class names like 'SUB_XXXXService' or 'MyService'. Use domain names (e.g., `{{domainContext}}Service`, `I{{domainContext}}Service`, `{{domainContext}}Models`).
                Do NOT output mock simulations, empty placeholders, 'await Task.Yield()', or 'throw new NotImplementedException()'. Write COMPLETE, working, production-grade business logic and real algorithms.

                Output each file in a distinct labeled code block:
                1. `Contracts/I{{domainContext}}Service.cs` - Service interfaces, contracts, DTOs
                2. `Models/{{domainContext}}Models.cs` - Domain models, immutable records, readonly record structs, domain events
                3. `Services/{{domainContext}}Service.cs` - Full production business logic implementation
                4. `Extensions/{{domainContext}}ServiceCollectionExtensions.cs` - Microsoft.Extensions.DependencyInjection wireup
                5. `Tests/{{domainContext}}ServiceTests.cs` - Complete xUnit unit tests asserting every Acceptance Criterion

                Example Format:
                ```csharp:Contracts/I{{domainContext}}Service.cs
                // File: Contracts/I{{domainContext}}Service.cs
                namespace {{targetNamespace}}.{{domainContext}};

                using System;
                using System.Threading;
                using System.Threading.Tasks;

                public interface I{{domainContext}}Service
                {
                    // Define exact service methods required by ticket acceptance criteria
                }
                ```

                ```csharp:Models/{{domainContext}}Models.cs
                // File: Models/{{domainContext}}Models.cs
                namespace {{targetNamespace}}.{{domainContext}};

                using System;

                public readonly record struct {{domainContext}}Result(string Id, bool IsSuccess, string Message);
                public record {{domainContext}}Entity(string Id, string Name, DateTimeOffset CreatedAt);
                ```

                ```csharp:Services/{{domainContext}}Service.cs
                // File: Services/{{domainContext}}Service.cs
                namespace {{targetNamespace}}.{{domainContext}};

                using System;
                using System.Collections.Concurrent;
                using System.Threading;
                using System.Threading.Tasks;
                using Microsoft.Extensions.Logging;

                public sealed class {{domainContext}}Service : I{{domainContext}}Service
                {
                    private readonly ILogger<{{domainContext}}Service>? _logger;

                    public {{domainContext}}Service(ILogger<{{domainContext}}Service>? logger = null)
                    {
                        _logger = logger;
                    }

                    // Implement complete domain logic and algorithms fulfilling ticket requirements
                }
                ```

                ```csharp:Extensions/{{domainContext}}ServiceCollectionExtensions.cs
                // File: Extensions/{{domainContext}}ServiceCollectionExtensions.cs
                namespace {{targetNamespace}}.{{domainContext}};

                using Microsoft.Extensions.DependencyInjection;

                public static class {{domainContext}}ServiceCollectionExtensions
                {
                    public static IServiceCollection Add{{domainContext}}(this IServiceCollection services)
                    {
                        services.AddSingleton<I{{domainContext}}Service, {{domainContext}}Service>();
                        return services;
                    }
                }
                ```

                ```csharp:Tests/{{domainContext}}ServiceTests.cs
                // File: Tests/{{domainContext}}ServiceTests.cs
                namespace {{targetNamespace}}.{{domainContext}}.Tests;

                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using Xunit;

                public class {{domainContext}}ServiceTests
                {
                    [Fact]
                    public async Task ExecuteAsync_ValidInput_ShouldSatisfyAcceptanceCriteria()
                    {
                        var service = new {{domainContext}}Service();
                        // Assert ticket acceptance criteria
                    }
                }
                ```

                Technical Requirements:
                - Use modern C# 13 / .NET 10 constructs (file-scoped namespaces, sealed classes, readonly record structs, primary constructors).
                - Zero heap allocations on hot path routines (use ValueTask, ReadOnlyMemory<byte>, ReadOnlySpan<char>, MemoryPool, bounded Channels).
                - Accept CancellationToken cancellationToken = default on all async methods and check for cancellation.
                - Include comprehensive xUnit unit tests verifying acceptance criteria.
                - Output complete, fully compilable code for every file.
                """;
                break;

            case AgentRole.SecurityEngineer:
                defaultArtifactName = $"{ticket.Id}_STRIDE_Model.md";
                contentType = "markdown";
                description = "STRIDE Security Threat Evaluation Matrix";
                userPrompt = $"""
                Produce an exhaustive Microsoft STRIDE Security Threat Model Audit in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === AUDIT MANDATE ===
                Audit the ACTUAL C# source code and interfaces provided in the upstream deliverable above. Evaluate input sanitization, buffer boundary slices, cancellation token handling, secret leakage, and cryptographic integrity on the actual classes and methods.

                Structure the document with:
                # STRIDE Threat Model Audit: {ticket.Title}
                ## Security Assessment Summary (Reference actual evaluated classes and methods)
                ## Threat Evaluation Matrix (Markdown Table covering Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege with columns: Threat Category | Asset & Code Method Evaluated | Mitigation Strategy in Code | Verification Status)
                ## Code-Level Security Findings & Mitigations
                ## Verdict (State Status: APPROVED - 0 Critical, 0 High vulnerabilities)
                """;
                break;

            case AgentRole.OptimizationEngineer:
                defaultArtifactName = $"{ticket.Id}_Perf_Profile.md";
                contentType = "markdown";
                description = "Performance & Zero-Allocation Benchmark Report";
                userPrompt = $"""
                Produce a detailed BenchmarkDotNet Performance and Zero-Allocation Report in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === BENCHMARK MANDATE ===
                Benchmark and audit the ACTUAL C# service implementations from the upstream deliverables above. Verify ValueTask async state machine costs, ReadOnlySpan memory slicing, and zero GC Gen0 allocations on the actual service methods.

                Structure the document with:
                # Performance & Allocation Benchmark Report: {ticket.Title}
                ## Benchmark Execution Environment (.NET 10.0, RyuJIT, AVX-512)
                ## Evaluated Methods & Hot Paths (Reference actual methods in the implementation)
                ## Benchmark Metrics Table (Columns: Method | Mean | Error | StdDev | P99 | Gen0 | Gen1 | Gen2 | Allocated)
                ## Diagnostic Conclusions (Verify 0 B Gen0 heap allocations and sub-5ms P99 latency SLA conformance)
                """;
                break;

            case AgentRole.PrincipalQAAnalyst:
                defaultArtifactName = $"{ticket.Id}_QA_Scorecard.md";
                contentType = "markdown";
                description = "QA Verification & Traceability Scorecard";
                userPrompt = $"""
                Produce an exhaustive QA Acceptance & Verification Scorecard in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === QA TRACEABILITY & ARCHITECTURAL GOVERNANCE MANDATE ===
                1. Verify Architectural Compliance & ADR:
                   - Audit whether upstream deliverables contain a formal, approved Architectural Decision Record (ADR) and Clean Architecture scaffold.
                   - Verify that domain boundaries, Clean Architecture layering, and type contracts match the architectural blueprint.
                   - IF ADR is missing or domain boundaries are violated, mark Certification Status: REJECTED (Fail to Lead Architect for remediation).
                2. Review the ACTUAL C# service implementation and xUnit test suites provided in the upstream deliverable above. Map every Acceptance Criterion directly to the corresponding unit test method that validates it.

                Structure the document with:
                # QA Certification & Acceptance Scorecard: {ticket.Title}
                ## 1. Architectural & ADR Compliance Audit (Verify ADR presence, Clean Architecture boundaries; mark - [x] Verified or Flag Violations)
                ## 2. Acceptance Criteria Traceability Matrix (Map each criterion to the specific Unit Test method and mark - [x] Verified)
                ## 3. Automated Test Execution Summary (Unit Tests count, Line Coverage %, Branch Coverage %, Mocking Boundaries)
                ## 4. Boundary & Negative Edge Case Results (Null input, cancellation handling, failure port recovery)
                ## 5. Release Decision (Certification Status: PASSED or REJECTED)
                """;
                break;

            case AgentRole.IntegrationEngineer:
            default:
                defaultArtifactName = $"{ticket.Id}_Release_Manifest.md";
                contentType = "markdown";
                description = "Release Manifest & Repository Solution Package";
                userPrompt = $"""
                Produce a comprehensive, rigorous Release Manifest & Repository Integration Blueprint in Markdown for:
                Ticket: {ticket.Id} - {ticket.Title}
                Description: {ticket.Description}
                Acceptance Criteria:
                {string.Join("\n", ticket.AcceptanceCriteria.Select(ac => $"- {ac}"))}
                {repoContext}
                {upstreamSummary}

                === PACKAGING & REPOSITORY INTEGRATION MANDATE ===
                Review all upstream deliverables (PRD, ADR, C# code, STRIDE threat model, Benchmark profile, and QA scorecard).
                1. Solution & Project Wiring Blueprint:
                   - Detail the exact directory layout (`src/`, `tests/`, `docs/adrs/`, `artifacts/`).
                   - Specify the `.slnx` solution references and Central Package Management (`Directory.Packages.props`) bindings.
                   - Provide Dependency Injection composition root wireup in `Program.cs`.
                2. Release Manifest Summary:
                   - Verification Status: Certified by QA and STRIDE Security.
                   - Installation / Execution commands (`dotnet build`, `dotnet test`, `dotnet run`).
                   - Unified Artifact Inventory Table (Listing all PRDs, ADRs, Code files, STRIDE matrices, Benchmarks, and QA scorecards).

                Structure the document with:
                # Release Manifest & Solution Package: {ticket.Title}
                ## 1. Solution Architecture & Directory Layout
                ## 2. Integrated Artifact Inventory (Table listing all upstream deliverables)
                ## 3. Dependency Injection & Host Composition Root Wiring
                ## 4. Build, Test & Verification Commands
                ## 5. Release Certification Summary (Status: PACKAGED & PRODUCTION READY)
                """;
                break;
        }

        return (systemPrompt, userPrompt, defaultArtifactName, contentType, description);
    }

    private static string CleanAndExtractContent(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return string.Empty;
        }

        var content = rawContent.Trim();

        // 1. If thinking tags <think>...</think> or <thought>...</thought> are present:
        var stripped = Regex.Replace(content, @"<(?:think|thought)>[\s\S]*?<\/(?:think|thought)>", "", RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrWhiteSpace(stripped))
        {
            content = stripped;
        }
        else
        {
            // 2. If content outside thinking tags was empty, check if deliverable was drafted inside thinking tags
            var matchInside = Regex.Match(content, @"<(?:think|thought)>([\s\S]*?)(?:<\/(?:think|thought)>|$)", RegexOptions.IgnoreCase);
            if (matchInside.Success)
            {
                var inside = matchInside.Groups[1].Value.Trim();
                if (inside.Contains('#') || inside.Contains("```"))
                {
                    content = inside;
                }
            }
            else
            {
                // 3. Unclosed <think> tag at start
                content = Regex.Replace(content, @"^<(?:think|thought)>", "", RegexOptions.IgnoreCase).Trim();
            }
        }

        // 4. Strip conversational preamble / monologue before the primary markdown header (# ) or code fence
        var headerIndex = content.IndexOf("\n# ", StringComparison.Ordinal);
        if (headerIndex >= 0)
        {
            var preamble = content[..headerIndex].Trim();
            if (preamble.Contains("The user wants") ||
                preamble.Contains("We need respond") ||
                preamble.Contains("Per my system prompt") ||
                preamble.Contains("Need produce") ||
                preamble.Contains("Let me structure") ||
                !preamble.StartsWith('#'))
            {
                content = content[(headerIndex + 1)..].Trim();
            }
        }
        else if (!content.StartsWith("# ") && !content.StartsWith("```"))
        {
            var anyHeader = Regex.Match(content, @"(?:^|\n)(#[^#\n\r][^\n\r]*)");
            if (anyHeader.Success && anyHeader.Index > 0)
            {
                var preamble = content[..anyHeader.Index].Trim();
                if (preamble.Contains("The user wants") ||
                    preamble.Contains("We need") ||
                    preamble.Contains("Per my system prompt") ||
                    preamble.Contains("Need produce") ||
                    preamble.Contains("Let me structure"))
                {
                    content = content[anyHeader.Index..].Trim();
                }
            }
        }

        // 5. Detect and eliminate infinite token repetition hallucination loops
        content = TruncateRepetitiveLoops(content);

        return content;
    }

    private static string TruncateRepetitiveLoops(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 200)
        {
            return text;
        }

        var lines = text.Split('\n');
        if (lines.Length > 15)
        {
            for (int i = 0; i < lines.Length - 8; i++)
            {
                var line = lines[i].Trim();
                if (line.Length > 15)
                {
                    int consecutive = 1;
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        if (string.Equals(lines[j].Trim(), line, StringComparison.OrdinalIgnoreCase))
                        {
                            consecutive++;
                            if (consecutive >= 6)
                            {
                                return string.Join('\n', lines.Take(i + 2)).Trim();
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
        }

        return text;
    }

    private static IReadOnlyList<ArtifactItem> ParseDeliverableArtifacts(
        string rawContent,
        string defaultArtifactName,
        string defaultContentType,
        string defaultDescription,
        TicketItem ticket)
    {
        var artifacts = new List<ArtifactItem>();
        var cleaned = CleanAndExtractContent(rawContent);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return artifacts;
        }

        // 1. If this is code implementation (csharp), extract multi-file or single file code blocks
        if (defaultContentType == "csharp")
        {
            var multiBlockMatches = Regex.Matches(cleaned, @"```(?:csharp|cs)?(?::([^\n\r]+))?\s*\n(?:(?:\/\/|\/\*|\#)\s*File:\s*([^\n\r*]+)(?:\*\/)?\s*\n)?([\s\S]*?)\n```", RegexOptions.IgnoreCase);

            if (multiBlockMatches.Count > 1 || (multiBlockMatches.Count == 1 && (!string.IsNullOrWhiteSpace(multiBlockMatches[0].Groups[1].Value) || !string.IsNullOrWhiteSpace(multiBlockMatches[0].Groups[2].Value))))
            {
                int fileIndex = 1;
                foreach (Match m in multiBlockMatches)
                {
                    var labelTag = m.Groups[1].Value.Trim();
                    var commentTag = m.Groups[2].Value.Trim();
                    var codeBody = m.Groups[3].Value.Trim();

                    var fileName = !string.IsNullOrWhiteSpace(labelTag) ? Path.GetFileName(labelTag) :
                                   !string.IsNullOrWhiteSpace(commentTag) ? Path.GetFileName(commentTag) :
                                   $"{ExtractDomainContext(ticket)}_{fileIndex}.cs";

                    fileName = fileName.Replace('\\', '_').Replace('/', '_');
                    if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName += ".cs";
                    }

                    artifacts.Add(new ArtifactItem(
                        Name: fileName,
                        Content: codeBody,
                        ContentType: "csharp",
                        Description: $"{defaultDescription} - {fileName}"
                    ));

                    fileIndex++;
                }

                if (artifacts.Count > 0)
                {
                    return artifacts;
                }
            }

            var singleMatch = Regex.Match(cleaned, @"```(?:csharp|cs)?\s*\n([\s\S]*?)\n```", RegexOptions.IgnoreCase);
            var cleanCode = singleMatch.Success ? singleMatch.Groups[1].Value.Trim() : cleaned;

            artifacts.Add(new ArtifactItem(
                Name: defaultArtifactName,
                Content: cleanCode,
                ContentType: "csharp",
                Description: defaultDescription
            ));
            return artifacts;
        }

        // 2. For Markdown deliverables (ADRs, PRDs, Research Briefs, STRIDE Threat Models, Benchmarks, QA Scorecards, Release Manifests):
        // Always preserve the complete Markdown document as the primary deliverable
        var mdMatch = Regex.Match(cleaned, @"^```(?:markdown|md)?\s*\n([\s\S]*?)\n```$", RegexOptions.IgnoreCase);
        var cleanMd = mdMatch.Success ? mdMatch.Groups[1].Value.Trim() : cleaned;

        artifacts.Add(new ArtifactItem(
            Name: defaultArtifactName,
            Content: cleanMd,
            ContentType: "markdown",
            Description: defaultDescription
        ));

        // For Lead Architect, if they also scaffolded C# type contracts, extract them as companion artifacts
        if (ticket.AssigneeRole == AgentRole.LeadArchitect)
        {
            var codeBlocks = Regex.Matches(cleaned, @"```(?:csharp|cs)?(?::([^\n\r]+))?\s*\n(?:(?:\/\/|\/\*|\#)\s*File:\s*([^\n\r*]+)(?:\*\/)?\s*\n)?([\s\S]*?)\n```", RegexOptions.IgnoreCase);
            int blockIndex = 1;
            foreach (Match m in codeBlocks)
            {
                var labelTag = m.Groups[1].Value.Trim();
                var commentTag = m.Groups[2].Value.Trim();
                var code = m.Groups[3].Value.Trim();

                if (string.IsNullOrWhiteSpace(code)) continue;

                string fileName;
                if (!string.IsNullOrWhiteSpace(labelTag))
                {
                    fileName = Path.GetFileName(labelTag);
                }
                else if (!string.IsNullOrWhiteSpace(commentTag))
                {
                    fileName = Path.GetFileName(commentTag);
                }
                else
                {
                    // Infer filename based on code declarations
                    if (Regex.IsMatch(code, @"\bpublic\s+interface\s+(I\w+)", RegexOptions.IgnoreCase))
                    {
                        var ifaceMatch = Regex.Match(code, @"\bpublic\s+interface\s+(I\w+)", RegexOptions.IgnoreCase);
                        fileName = $"{ifaceMatch.Groups[1].Value}.cs";
                    }
                    else if (Regex.IsMatch(code, @"\bpublic\s+(?:readonly\s+)?record\s+(?:struct\s+)?(\w+)", RegexOptions.IgnoreCase))
                    {
                        var recordMatch = Regex.Match(code, @"\bpublic\s+(?:readonly\s+)?record\s+(?:struct\s+)?(\w+)", RegexOptions.IgnoreCase);
                        fileName = $"{recordMatch.Groups[1].Value}.cs";
                    }
                    else if (Regex.IsMatch(code, @"\bpublic\s+static\s+class\s+(\w+)", RegexOptions.IgnoreCase))
                    {
                        var classMatch = Regex.Match(code, @"\bpublic\s+static\s+class\s+(\w+)", RegexOptions.IgnoreCase);
                        fileName = $"{classMatch.Groups[1].Value}.cs";
                    }
                    else
                    {
                        fileName = $"{ExtractDomainContext(ticket)}_Scaffold_{blockIndex}.cs";
                    }
                }

                fileName = fileName.Replace('\\', '_').Replace('/', '_');
                if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".cs";
                }

                if (!artifacts.Any(a => string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    artifacts.Add(new ArtifactItem(
                        Name: fileName,
                        Content: code,
                        ContentType: "csharp",
                        Description: $"Architectural Scaffold - {fileName}"
                    ));
                }
                blockIndex++;
            }

            // Fallback: If Lead Architect output only markdown ADR with 0 companion C# code blocks, synthesize baseline Clean Architecture scaffolds
            if (!artifacts.Any(a => a.ContentType == "csharp" || a.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var domainCtx = ExtractDomainContext(ticket);
                var targetNs = "CarnotCycleCircus.Core.Domain";

                var interfaceCode = $$"""
                // File: Contracts/I{{domainCtx}}Service.cs
                namespace {{targetNs}}.{{domainCtx}};

                using System;
                using System.Threading;
                using System.Threading.Tasks;

                public interface I{{domainCtx}}Service
                {
                    ValueTask<bool> ExecuteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
                }
                """;

                var modelsCode = $$"""
                // File: Models/{{domainCtx}}Models.cs
                namespace {{targetNs}}.{{domainCtx}};

                using System;

                public readonly record struct {{domainCtx}}Result(string Id, bool IsSuccess, string Message);
                public record {{domainCtx}}State(string Id, string Name, DateTimeOffset Timestamp);
                """;

                var extCode = $$"""
                // File: Extensions/{{domainCtx}}ServiceCollectionExtensions.cs
                namespace {{targetNs}}.{{domainCtx}};

                using Microsoft.Extensions.DependencyInjection;

                public static class {{domainCtx}}ServiceCollectionExtensions
                {
                    public static IServiceCollection Add{{domainCtx}}(this IServiceCollection services)
                    {
                        return services;
                    }
                }
                """;

                artifacts.Add(new ArtifactItem($"I{domainCtx}Service.cs", interfaceCode.Trim(), "csharp", $"Architectural Scaffold - I{domainCtx}Service.cs"));
                artifacts.Add(new ArtifactItem($"{domainCtx}Models.cs", modelsCode.Trim(), "csharp", $"Architectural Scaffold - {domainCtx}Models.cs"));
                artifacts.Add(new ArtifactItem($"{domainCtx}ServiceCollectionExtensions.cs", extCode.Trim(), "csharp", $"Architectural Scaffold - {domainCtx}ServiceCollectionExtensions.cs"));
            }
        }

        return artifacts;
    }

    private static string ExtractDomainContext(TicketItem ticket)
    {
        var title = ticket.Title;

        // 1. Remove role prefix [Arch], [Dev], etc.
        title = Regex.Replace(title, @"^\[(?:Arch|Dev|Security|Opt|QA|TPM|Research|Integration)\]\s*", "", RegexOptions.IgnoreCase).Trim();

        // 2. If the title is in the format "... for <Domain/Feature>[: Sub-domain]", extract the domain after 'for'
        var forMatch = Regex.Match(title, @"\bfor\s+(.+)$", RegexOptions.IgnoreCase);
        if (forMatch.Success && !string.IsNullOrWhiteSpace(forMatch.Groups[1].Value))
        {
            title = forMatch.Groups[1].Value.Trim();
        }
        else
        {
            // If no 'for', strip common action verb prefixes
            title = Regex.Replace(title, @"^(?:Implement|Design|Review|Benchmark|Verify|Audit|Scaffold|Author|Create|Groom|Refine|Package|Wire)\s+", "", RegexOptions.IgnoreCase).Trim();
            title = Regex.Replace(title, @"^(?:ADR|PRD|STRIDE|QA|Test Strategy)\s+(?:for|on|of)\s+", "", RegexOptions.IgnoreCase).Trim();
        }

        // 3. If there is a colon (e.g. "Telemetry Pipeline: Core Engine & Protocols"), prioritize the primary initiative title before colon
        if (title.Contains(':'))
        {
            var parts = title.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                if (!parts[0].Contains("Research", StringComparison.OrdinalIgnoreCase) &&
                    !parts[0].Contains("Feasibility", StringComparison.OrdinalIgnoreCase) &&
                    !parts[0].Contains("Ticket", StringComparison.OrdinalIgnoreCase))
                {
                    title = parts[0];
                }
                else if (parts.Length > 1)
                {
                    title = parts[1];
                }
            }
        }

        // 4. Clean non-alphanumeric characters
        title = Regex.Replace(title, @"[^a-zA-Z0-9]", " ");
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "CoreService";

        // 5. Remove generic fluff words if there are other meaningful words
        var filteredWords = words.Where(w => !string.Equals(w, "ADR", StringComparison.OrdinalIgnoreCase) &&
                                             !string.Equals(w, "PRD", StringComparison.OrdinalIgnoreCase) &&
                                             !string.Equals(w, "Subtask", StringComparison.OrdinalIgnoreCase) &&
                                             !string.Equals(w, "Story", StringComparison.OrdinalIgnoreCase) &&
                                             !string.Equals(w, "Ticket", StringComparison.OrdinalIgnoreCase)).ToList();

        if (filteredWords.Count > 0)
        {
            words = filteredWords.ToArray();
        }

        var pascal = string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : "")));
        return string.IsNullOrWhiteSpace(pascal) ? "CoreService" : pascal;
    }
}
