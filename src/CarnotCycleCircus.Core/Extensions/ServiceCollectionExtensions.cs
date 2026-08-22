using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Skills;
using CarnotCycleCircus.Core.Domain.Standards;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace CarnotCycleCircus.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCarnotCycleCircusCore(this IServiceCollection services)
    {
        // Event Stream & Message Bus
        services.AddSingleton<IAgentEventStream, AgentEventStream>();

        // Ticket Management & Work Decomposition Engine
        services.AddSingleton<ITicketStore, TicketStore>();
        services.AddSingleton<IWorkDecompositionEngine, WorkDecompositionEngine>();
        services.AddSingleton<IHandoffRouter, HandoffRouter>();

        // Memory Layer
        services.AddSingleton<IPersistentMemoryStore, EmbeddedVectorMemoryStore>();
        services.AddSingleton<IExternalMemoryConnector, ExternalMemoryConnector>();
        services.AddSingleton<IMemoryConsolidationEngine, MemoryConsolidationEngine>();
        services.AddSingleton<IContextAwareMemoryInjector, ContextAwareMemoryInjector>();

        // Inference & Key Vault
        services.AddSingleton<IApiKeyVaultService, ApiKeyVaultService>();
        services.AddSingleton<IOpenRouterClient, OpenRouterClient>();
        services.AddSingleton<IAgentInferenceResolver, AgentInferenceResolver>();
        services.AddSingleton<ISimulatedScenarioEngine, SimulatedScenarioEngine>();

        // Tools Sandbox
        services.AddSingleton<IToolDefinition, WebSearchTool>();
        services.AddSingleton<IToolDefinition, CSharpSyntaxCheckTool>();
        services.AddSingleton<IToolDefinition, TestRunnerTool>();
        services.AddSingleton<IToolDefinition, AdrWriterTool>();
        services.AddSingleton<IToolDefinition, MemoryLookupTool>();

        // ADR & Documentation Hub
        services.AddSingleton<IAdrDocumentManager, AdrDocumentManager>();

        // Standards & Quality Gates
        services.AddSingleton<IStandardsValidator, StandardsValidator>();

        // AI Knowledge Maps
        services.AddSingleton<IKnowledgeMapService, KnowledgeMapService>();

        // Teams & Archetypes
        services.AddSingleton<ITeamDefinitionManager, TeamDefinitionManager>();

        // Skills & Importer
        services.AddSingleton<ISkillImporter, SkillImporter>();
        services.AddSingleton<ISkillRegistry, SkillRegistry>();

        // Graph Orchestrator & Workflow Executor
        services.AddSingleton<IGraphWorkflowExecutor, GraphWorkflowExecutor>();

        return services;
    }
}
