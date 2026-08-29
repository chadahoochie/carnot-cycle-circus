using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Learning;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Skills;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

/// <summary>
/// Comprehensive end-to-end integration and verification suite testing all 6 foundational pillars:
/// 1) Agent Definition
/// 2) Team Process Definition
/// 3) Ticket Persistence, Transitions & Changing Assignments Between Agents
/// 4) Skills Hub
/// 5) Skill to Agent Matrix
/// 6) Learnings Persistence
/// </summary>
public class EndToEndSystemVerificationTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly CarnotStorageOptions _storageOptions;
    private readonly IPersistentStorageService _storageService;

    public EndToEndSystemVerificationTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"carnot_e2e_full_{Guid.NewGuid():N}");
        _storageOptions = new CarnotStorageOptions
        {
            DataDirectory = _testTempDir,
            EnableAtomicWrites = true,
            SelfImprovementIntervalSeconds = 60,
            AutoRunSelfImprovementOnStartup = false
        };
        _storageService = new FilePersistentStorageService(_storageOptions);
    }

    [Fact]
    public async Task CompleteAutonomousCircus_EndToEndLifecycle_ShouldSucceedAndPersistAllLearnings()
    {
        // =========================================================================
        // PILLAR 4: SKILLS HUB (Import Markdown & JSON skills, register, categorize)
        // =========================================================================
        var skillImporter = new SkillImporter();
        var skillRegistry = new SkillRegistry(skillImporter, _storageService);

        var raftSkillMd = """
            ---
            name: Distributed Raft Consensus
            id: skill-raft-consensus
            description: Leader election, log replication, and cluster membership safety proofs.
            category: DistributedSystems
            tools: [csharp_syntax_check, test_runner]
            ---

            Implement zero-allocation log entry buffers, strict term monotonicity, and heartbeat timers.
            Validate network partitions and quorum math before committing any state machine log entries.
            """;

        var raftSkill = skillImporter.ParseSkillMarkdown(raftSkillMd);
        raftSkill.Id.Should().Be("skill-raft-consensus");
        raftSkill.Category.Should().Be("DistributedSystems");
        skillRegistry.RegisterSkill(raftSkill);

        var zeroCopySkillJson = """
            {
                "id": "skill-zero-copy-memory",
                "name": "Zero-Copy Memory Pipelines",
                "description": "Span, ReadOnlySequence, and Native Memory pooling for 0-heap throughput.",
                "instructions": "Ban byte array allocations on hot paths. Enforce ArrayPool<byte> and MemoryMarshal.",
                "recommendedTools": ["csharp_syntax_check", "test_runner"],
                "category": "Performance"
            }
            """;

        var zeroCopySkill = skillImporter.ParseSkillJson(zeroCopySkillJson);
        zeroCopySkill.Id.Should().Be("skill-zero-copy-memory");
        skillRegistry.RegisterSkill(zeroCopySkill);

        // Verify skills are searchable and registered
        var allSkills = skillRegistry.GetAllSkills();
        allSkills.Should().Contain(s => s.Id == "skill-raft-consensus");
        allSkills.Should().Contain(s => s.Id == "skill-zero-copy-memory");

        // =========================================================================
        // PILLAR 5: SKILL TO AGENT MATRIX (Map skills to roles & inject into prompts)
        // =========================================================================
        skillRegistry.AssignSkillToRole("skill-raft-consensus", AgentRole.LeadArchitect);
        skillRegistry.AssignSkillToRole("skill-zero-copy-memory", AgentRole.SoftwareDeveloper);
        skillRegistry.AssignSkillToRole("skill-zero-copy-memory", AgentRole.OptimizationEngineer);
        skillRegistry.AssignSkillToRole("skill-csharp-standards", AgentRole.SoftwareDeveloper);
        skillRegistry.AssignSkillToRole("skill-stride-modeling", AgentRole.SecurityEngineer);
        skillRegistry.AssignSkillToRole("skill-edge-case-torture", AgentRole.PrincipalQAAnalyst);
        skillRegistry.AssignSkillToRole("skill-buzzword-mastery", AgentRole.TechnicalProductManager);

        // Validate matrix associations
        var devSkills = skillRegistry.GetSkillsForRole(AgentRole.SoftwareDeveloper);
        devSkills.Select(s => s.Id).Should().Contain(["skill-zero-copy-memory", "skill-csharp-standards"]);

        var raftRoles = skillRegistry.GetRolesForSkill("skill-raft-consensus");
        raftRoles.Should().Contain(AgentRole.LeadArchitect);

        // =========================================================================
        // PILLAR 1: AGENT DEFINITION (Dynamic personas, prompt generation, ADR-0005 contract)
        // =========================================================================
        var nameGenerator = new AgentNameGenerator();

        var archSuggestedName = nameGenerator.GenerateSuggestedName(AgentRole.LeadArchitect, skillRegistry.GetSkillsForRole(AgentRole.LeadArchitect), seed: 42);
        var archPrompt = nameGenerator.GenerateSystemPrompt(AgentRole.LeadArchitect, archSuggestedName, skillRegistry.GetSkillsForRole(AgentRole.LeadArchitect));

        archPrompt.Should().Contain("DELIVERABLE ISOLATION CONTRACT");
        archPrompt.Should().Contain("Distributed Raft Consensus");
        archPrompt.Should().Contain("zero-allocation log entry buffers");

        var devSuggestedName = nameGenerator.GenerateSuggestedName(AgentRole.SoftwareDeveloper, skillRegistry.GetSkillsForRole(AgentRole.SoftwareDeveloper), seed: 42);
        var devPrompt = nameGenerator.GenerateSystemPrompt(AgentRole.SoftwareDeveloper, devSuggestedName, skillRegistry.GetSkillsForRole(AgentRole.SoftwareDeveloper));

        devPrompt.Should().Contain("DELIVERABLE ISOLATION CONTRACT");
        devPrompt.Should().Contain("Zero-Copy Memory Pipelines");

        var archPersona = new AgentPersona(
            Role: AgentRole.LeadArchitect,
            Name: archSuggestedName,
            SystemPrompt: archPrompt,
            DefaultModel: "anthropic/claude-3.7-sonnet",
            FallbackModel: "openai/gpt-4o",
            Temperature: 0.1,
            AllowedToolNames: ["csharp_syntax_check", "adr_writer", "memory_lookup"],
            AssignedSkillIds: skillRegistry.GetSkillsForRole(AgentRole.LeadArchitect).Select(s => s.Id).ToList()
        );

        var devPersona = new AgentPersona(
            Role: AgentRole.SoftwareDeveloper,
            Name: devSuggestedName,
            SystemPrompt: devPrompt,
            DefaultModel: "qwen/qwen-2.5-coder-32b-instruct",
            FallbackModel: "anthropic/claude-3.7-sonnet",
            Temperature: 0.1,
            AllowedToolNames: ["csharp_syntax_check", "test_runner", "memory_lookup"],
            AssignedSkillIds: skillRegistry.GetSkillsForRole(AgentRole.SoftwareDeveloper).Select(s => s.Id).ToList()
        );

        // =========================================================================
        // PILLAR 2: TEAM PROCESS DEFINITION (Archetypes, custom team, JSON export/import, DAG graph)
        // =========================================================================
        var teamManager = new TeamDefinitionManager(_storageService);

        var customMembers = Enum.GetValues<AgentRole>().Select(role =>
        {
            var roleSkills = skillRegistry.GetSkillsForRole(role);
            var name = nameGenerator.GenerateSuggestedName(role, roleSkills, seed: 100 + (int)role);
            var prompt = nameGenerator.GenerateSystemPrompt(role, name, roleSkills);
            var persona = role switch
            {
                AgentRole.LeadArchitect => archPersona,
                AgentRole.SoftwareDeveloper => devPersona,
                _ => AgentPersona.CreateDefault(role) with
                {
                    Name = name,
                    SystemPrompt = prompt,
                    AssignedSkillIds = roleSkills.Select(s => s.Id).ToList()
                }
            };
            return new AgentMember(persona);
        }).ToList();

        var customTeamDef = new TeamDefinition(
            Id: "team-raft-circus-e2e",
            Name: "🎪 Raft Consensus Engineering Crew",
            Description: "Specialized team executing zero-allocation distributed consensus protocols.",
            ArchetypeName: "HighPerformance",
            Members: customMembers,
            DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
            CreatedAt: DateTimeOffset.UtcNow
        );

        teamManager.SaveTeam(customTeamDef);
        teamManager.SetCurrentTeam(customTeamDef);

        var activeTeam = teamManager.GetCurrentTeam();
        activeTeam.Id.Should().Be("team-raft-circus-e2e");
        activeTeam.Members.Should().HaveCount(8);

        // Validate JSON export and import roundtrip
        var exportedJson = teamManager.ExportToJson(customTeamDef.Id);
        exportedJson.Should().Contain("skill-raft-consensus");
        exportedJson.Should().Contain("skill-zero-copy-memory");

        var reimportedTeam = teamManager.ImportFromJson(exportedJson);
        reimportedTeam.Should().NotBeNull();
        reimportedTeam.Members.Should().HaveCount(8);

        // Define DAG Workflow Graph with failure loopback ports
        var workflowGraph = WorkflowGraph.CreateDefaultEngineeringCircus();
        workflowGraph.Nodes.Should().HaveCount(8);
        workflowGraph.Connections.Should().Contain(c => c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");

        // =========================================================================
        // PILLAR 3: TICKET PERSISTENCE, TRANSITIONS & CHANGING ASSIGNMENTS (Work decomposition, handoffs, failure loopback)
        // =========================================================================
        var ticketStore = new TicketStore(_storageService);
        var eventStream = new AgentEventStream();
        var memoryStore = new EmbeddedVectorMemoryStore(_storageService);
        var scenarioEngine = new SimulatedScenarioEngine();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(ticketStore);
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var knowledgeMap = new KnowledgeMapService(_storageService);
        var selfImprovement = new SelfImprovementEngine(memoryStore, ticketStore, knowledgeMap, eventStream, _storageService);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompositionEngine,
            handoffRouter,
            scenarioEngine,
            eventStream,
            consolidationEngine,
            selfImprovement
        );

        executor.SetGraph(workflowGraph);

        // Execute workflow with failure simulation triggered on Security node
        var workflowSuccess = await executor.ExecuteWorkflowAsync(
            epicTitle: "Zero-Allocation Distributed Raft Consensus Engine",
            epicDescription: "Build a high-performance Raft cluster engine in C# 13 with zero Gen0 allocations, connectable failure recovery, and strict validation.",
            triggerFailureSimulation: true
        );

        workflowSuccess.Should().BeTrue();

        // Validate all tickets were decomposed, transitioned, and completed
        var allTickets = ticketStore.GetAllTickets();
        allTickets.Should().NotBeEmpty();
        allTickets.Should().Contain(t => t.Type == TicketType.Epic && t.Status == TicketStatus.Done);
        allTickets.Where(t => t.Type == TicketType.Subtask).Should().OnlyContain(t => t.Status == TicketStatus.Done);

        // Validate inter-agent handoff packets were created, dispatched, and recorded
        var allHandoffs = ticketStore.GetAllHandoffs();
        allHandoffs.Should().NotBeEmpty();

        // Verify failure remediation handoff occurred and was properly addressed
        var failureHandoff = allHandoffs.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.RemediationNotes));
        failureHandoff.Should().NotBeNull();
        failureHandoff!.FromAgentRole.Should().Be(AgentRole.SecurityEngineer);
        failureHandoff.ToAgentRole.Should().Be(AgentRole.SoftwareDeveloper);
        failureHandoff.RemediationNotes.Should().Contain("ReadOnlySpan<char>");

        // Verify deliverables were attached to tickets
        var totalDeliverables = allTickets.Sum(t => t.Deliverables.Count);
        totalDeliverables.Should().BeGreaterThan(0);

        // =========================================================================
        // PILLAR 6: LEARNINGS PERSISTENCE (Self-improvement cycles, Knowledge Map, Procedural & Semantic Memory)
        // =========================================================================
        var report = await selfImprovement.RunSelfImprovementCycleAsync();

        report.TotalCyclesRun.Should().BeGreaterThan(0);
        report.InsightsDistilledCount.Should().BeGreaterThan(0);
        report.ProceduralRecipesGenerated.Should().BeGreaterThan(0);
        report.SemanticRulesReinforced.Should().BeGreaterThan(0);

        // Verify knowledge map node was distilled from the failure remediation handoff
        var distilledNode = knowledgeMap.GetNode($"KN-LI-REM-{failureHandoff.TicketId}");
        distilledNode.Should().NotBeNull();
        distilledNode!.Category.Should().Be("LearnedInsight");
        distilledNode.Summary.Should().Contain("Remediation Lesson:");
        distilledNode.Attributes.Should().ContainKey("SourceRole");

        // Verify procedural and semantic memories exist in vector store
        var proceduralMemories = await memoryStore.GetByTypeAsync(MemoryType.Procedural);
        proceduralMemories.Should().NotBeEmpty();
        proceduralMemories.First().Content.Should().Contain("Standard Execution Recipe");

        var semanticMemories = await memoryStore.GetByTypeAsync(MemoryType.Semantic);
        semanticMemories.Should().NotBeEmpty();
        semanticMemories.Should().Contain(m => m.Content.Contains("Reinforced Rule"));

        // Allow explicit storage flush
        await skillRegistry.FlushAsync();
        await teamManager.FlushAsync();
        await ticketStore.FlushAsync();
        await knowledgeMap.FlushAsync();
        await memoryStore.FlushAsync();

        // =========================================================================
        // PILLAR 7: COLD-START RESTART & 100% PERSISTENCE VERIFICATION
        // (Simulate complete application restart and load all data from storage)
        // =========================================================================
        var restartedStorage = new FilePersistentStorageService(_storageOptions);

        // 1. Verify Storage Health & Files exist on disk
        var healthReport = await restartedStorage.GetStorageHealthAsync();
        healthReport.IsHealthy.Should().BeTrue();
        healthReport.TotalFilesCount.Should().BeGreaterThan(0);

        var artifactsExist = await restartedStorage.FileExistsAsync("artifacts/LEARNED_INSIGHTS.md");
        artifactsExist.Should().BeTrue();
        var learnedInsightsMd = await restartedStorage.LoadTextAsync("artifacts/LEARNED_INSIGHTS.md");
        learnedInsightsMd.Should().Contain("Autonomous Self-Improvement & Continuous Learning Report");
        learnedInsightsMd.Should().Contain("KN-LI-REM-");

        // 2. Verify Skills Hub reloads from storage
        var restartedSkillRegistry = new SkillRegistry(skillImporter, restartedStorage);
        var loadedRaftSkill = restartedSkillRegistry.GetSkill("skill-raft-consensus");
        loadedRaftSkill.Should().NotBeNull();
        loadedRaftSkill!.Name.Should().Be("Distributed Raft Consensus");
        loadedRaftSkill.AssignedRoles.Should().Contain(AgentRole.LeadArchitect);

        // 3. Verify Team Definition reloads from storage
        var restartedTeamManager = new TeamDefinitionManager(restartedStorage);
        var loadedTeams = restartedTeamManager.GetAllTeams();
        loadedTeams.Should().Contain(t => t.Id == "team-raft-circus-e2e");
        var activeLoadedTeam = restartedTeamManager.GetCurrentTeam();
        activeLoadedTeam.Should().NotBeNull();

        // 4. Verify Tickets and Handoffs reload from storage
        var restartedTicketStore = new TicketStore(restartedStorage);
        var reloadedTickets = restartedTicketStore.GetAllTickets();
        reloadedTickets.Should().HaveCount(allTickets.Count);
        reloadedTickets.Where(t => t.Type == TicketType.Subtask).Should().OnlyContain(t => t.Status == TicketStatus.Done);

        var reloadedHandoffs = restartedTicketStore.GetAllHandoffs();
        reloadedHandoffs.Should().HaveCount(allHandoffs.Count);
        reloadedHandoffs.Should().Contain(h => h.FromAgentRole == AgentRole.SecurityEngineer && !string.IsNullOrWhiteSpace(h.RemediationNotes));

        // 5. Verify Knowledge Map reloads from storage
        var restartedKnowledgeMap = new KnowledgeMapService(restartedStorage);
        var reloadedDistilledNode = restartedKnowledgeMap.GetNode($"KN-LI-REM-{failureHandoff.TicketId}");
        reloadedDistilledNode.Should().NotBeNull();
        reloadedDistilledNode!.Summary.Should().Contain("Remediation Lesson:");

        // 6. Verify Memory Store reloads and supports semantic search
        var restartedMemoryStore = new EmbeddedVectorMemoryStore(restartedStorage);
        var searchResults = await restartedMemoryStore.SearchAsync("Raft execution recipe zero-allocation span", topK: 3);
        searchResults.Should().NotBeEmpty();
        searchResults[0].SimilarityScore.Should().BeGreaterThan(0.2f);

        // 7. Verify Self-Improvement Engine reloads report state
        var restartedSelfImprovement = new SelfImprovementEngine(
            restartedMemoryStore,
            restartedTicketStore,
            restartedKnowledgeMap,
            eventStream,
            restartedStorage
        );
        var latestReport = restartedSelfImprovement.GetLatestReport();
        latestReport.TotalCyclesRun.Should().Be(report.TotalCyclesRun);
        latestReport.InsightsDistilledCount.Should().Be(report.InsightsDistilledCount);
    }

    [Fact]
    public void AgentDefinition_And_PromptGeneration_ShouldEnforceIsolationContract()
    {
        var generator = new AgentNameGenerator();
        var roles = Enum.GetValues<AgentRole>();

        foreach (var role in roles)
        {
            var skills = new List<SkillDefinition>
            {
                new("skill-test", "Test Capability", "Test Description", "Strict rule instruction", ["test_tool"], "Testing")
            };

            var name = generator.GenerateSuggestedName(role, skills, seed: 42);
            name.Should().NotBeNullOrWhiteSpace();

            var prompt = generator.GenerateSystemPrompt(role, name, skills);
            prompt.Should().Contain(name);
            prompt.Should().Contain("DELIVERABLE ISOLATION CONTRACT");
            prompt.Should().Contain("Strict rule instruction");
        }
    }

    [Fact]
    public async Task TeamProcessDefinition_And_WorkflowGraph_ShouldConfigureAndSerializeCorrectly()
    {
        var teamManager = new TeamDefinitionManager(_storageService);
        var archetype = teamManager.LoadArchetype("SecurityHardened");

        archetype.Should().NotBeNull();
        archetype.Members.Should().HaveCount(8);

        var json = teamManager.ExportToJson(archetype.Id);
        json.Should().Contain("SecurityHardened");

        var imported = teamManager.ImportFromJson(json);
        imported.Id.Should().StartWith("team-import-");
        imported.Members.Should().HaveCount(8);

        var graph = WorkflowGraph.CreateDefaultEngineeringCircus();
        graph.Nodes.Should().HaveCount(8);

        // Modify graph
        var customConnection = new PortConnection("node-sec", PortType.Output, "node-qa", PortType.Input);
        graph = graph with { Connections = [.. graph.Connections, customConnection] };
        graph.Connections.Should().Contain(customConnection);
    }

    [Fact]
    public async Task SkillsHub_And_SkillMatrix_ShouldImportAssignAndPersistAcrossRestarts()
    {
        var importer = new SkillImporter();
        var registry = new SkillRegistry(importer, _storageService);

        var customSkill = new SkillDefinition(
            Id: "skill-e2e-matrix-test",
            Name: "Matrix Test Skill",
            Description: "Validates matrix role mappings",
            Instructions: "Enforce strict typing and immutability.",
            RecommendedTools: ["csharp_syntax_check"],
            Category: "Architecture",
            AssignedRoles: [AgentRole.LeadArchitect, AgentRole.SoftwareDeveloper]
        );

        registry.RegisterSkill(customSkill);
        registry.AssignSkillToRole("skill-e2e-matrix-test", AgentRole.OptimizationEngineer);

        var roles = registry.GetRolesForSkill("skill-e2e-matrix-test");
        roles.Should().Contain([AgentRole.LeadArchitect, AgentRole.SoftwareDeveloper, AgentRole.OptimizationEngineer]);

        await registry.FlushAsync();

        // Cold-start reload
        var reloadedRegistry = new SkillRegistry(importer, _storageService);
        var loaded = reloadedRegistry.GetSkill("skill-e2e-matrix-test");
        loaded.Should().NotBeNull();
        loaded!.AssignedRoles.Should().Contain([AgentRole.LeadArchitect, AgentRole.SoftwareDeveloper, AgentRole.OptimizationEngineer]);
    }

    [Fact]
    public async Task TicketLifecycle_TransitionsAndHandoffs_ShouldPersistStateAndDeliverables()
    {
        var ticketStore = new TicketStore(_storageService);
        var eventStream = new AgentEventStream();
        var router = new HandoffRouter(ticketStore, eventStream);

        var ticket = new TicketItem(
            Id: "TCK-E2E-TRANS-01",
            ParentEpicId: null,
            Title: "Implement In-Memory Ring Buffer",
            Description: "Lock-free single-producer single-consumer ring buffer.",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.Critical,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Zero heap allocation on enqueue/dequeue"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string> { ["Module"] = "Memory" },
            CreatedAt: DateTimeOffset.UtcNow
        );

        ticketStore.CreateTicket(ticket);

        // Status transition: InProgress -> Review with deliverable
        var inProgress = ticket.WithStatus(TicketStatus.InProgress);
        ticketStore.UpdateTicket(inProgress);

        var deliverable = new ArtifactItem(
            Name: "RingBuffer.cs",
            Content: "public sealed class RingBuffer<T> { ... }",
            ContentType: "csharp",
            Description: "Lock-free ring buffer implementation"
        );

        var underReview = inProgress.WithStatus(TicketStatus.Review).WithDeliverable(deliverable);
        ticketStore.UpdateTicket(underReview);

        // Handoff to Security
        var handoff = router.RouteSuccessHandoff(
            underReview.Id,
            AgentRole.SoftwareDeveloper,
            AgentRole.SecurityEngineer,
            "Ring buffer implemented",
            "Security review required",
            [deliverable]
        );

        handoff.Should().NotBeNull();
        ticketStore.GetHandoffsForTicket(underReview.Id).Should().HaveCount(1);

        // Security rejects and routes failure remediation back to Dev
        var remediationHandoff = router.RouteFailureRemediation(
            underReview.Id,
            AgentRole.SecurityEngineer,
            AgentRole.SoftwareDeveloper,
            "Memory boundary violation on wrap-around index",
            "Mask index with buffer capacity - 1 using bitwise AND"
        );

        var remediatingTicket = ticketStore.GetTicketById(underReview.Id);
        remediatingTicket!.Status.Should().Be(TicketStatus.Remediating);
        remediatingTicket.AssigneeRole.Should().Be(AgentRole.SoftwareDeveloper);

        // Software Developer fixes and Completes ticket
        var fixedDeliverable = deliverable with { Content = "public sealed class RingBuffer<T> { /* bitwise masked */ }" };
        var completedTicket = remediatingTicket.WithDeliverable(fixedDeliverable);
        ticketStore.UpdateTicket(completedTicket);

        router.AdvanceWorkflowOnTicketCompletion(completedTicket.Id);

        var finalTicket = ticketStore.GetTicketById(completedTicket.Id);
        finalTicket!.Status.Should().Be(TicketStatus.Done);
        finalTicket.Deliverables.Should().HaveCount(2);

        await ticketStore.FlushAsync();

        // Cold reload validation
        var reloadedStore = new TicketStore(_storageService);
        var loadedTicket = reloadedStore.GetTicketById(ticket.Id);
        loadedTicket.Should().NotBeNull();
        loadedTicket!.Status.Should().Be(TicketStatus.Done);
        loadedTicket.Deliverables.Should().HaveCount(2);

        var loadedHandoffs = reloadedStore.GetHandoffsForTicket(ticket.Id);
        loadedHandoffs.Should().HaveCount(2);
    }

    [Fact]
    public async Task LearningsPersistence_ShouldDistillRemediationsAndSynthesizeKnowledgeGraph()
    {
        var memStore = new EmbeddedVectorMemoryStore(_storageService);
        var ticketStore = new TicketStore(_storageService);
        var knowledgeMap = new KnowledgeMapService(_storageService);
        var eventStream = new AgentEventStream();

        var failureHandoff = HandoffPacket.Create(
            ticketId: "TCK-LEARN-01",
            fromRole: AgentRole.SecurityEngineer,
            toRole: AgentRole.SoftwareDeveloper,
            contextSummary: "ReDoS vulnerability in regex parser",
            actionRequested: "Fix regex",
            remediationNotes: "Replace catastrophic backtracking regex with non-backtracking GeneratedRegexAttribute"
        );
        ticketStore.RecordHandoff(failureHandoff);

        var engine = new SelfImprovementEngine(memStore, ticketStore, knowledgeMap, eventStream, _storageService);
        var report = await engine.RunSelfImprovementCycleAsync();

        report.DistilledInsights.Should().NotBeEmpty();
        var node = knowledgeMap.GetNode("KN-LI-REM-TCK-LEARN-01");
        node.Should().NotBeNull();
        node!.Summary.Should().Contain("GeneratedRegexAttribute");

        await knowledgeMap.FlushAsync();

        // Verify across reload
        var reloadedKnowledgeMap = new KnowledgeMapService(_storageService);
        var reloadedNode = reloadedKnowledgeMap.GetNode("KN-LI-REM-TCK-LEARN-01");
        reloadedNode.Should().NotBeNull();
        reloadedNode!.Category.Should().Be("LearnedInsight");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp dir cleanup exceptions
        }
    }
}
