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
using Shouldly;
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
        raftSkill.Id.ShouldBe("skill-raft-consensus");
        raftSkill.Category.ShouldBe("DistributedSystems");
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
        zeroCopySkill.Id.ShouldBe("skill-zero-copy-memory");
        skillRegistry.RegisterSkill(zeroCopySkill);

        // Verify skills are searchable and registered
        var allSkills = skillRegistry.GetAllSkills();
        allSkills.ShouldContain(s => s.Id == "skill-raft-consensus");
        allSkills.ShouldContain(s => s.Id == "skill-zero-copy-memory");

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
        new[] { "skill-zero-copy-memory", "skill-csharp-standards" }.ShouldBeSubsetOf(devSkills.Select(s => s.Id));

        var raftRoles = skillRegistry.GetRolesForSkill("skill-raft-consensus");
        raftRoles.ShouldContain(AgentRole.LeadArchitect);

        // =========================================================================
        // PILLAR 1: AGENT DEFINITION (Dynamic personas, prompt generation, ADR-0005 contract)
        // =========================================================================
        var nameGenerator = new AgentNameGenerator();

        var archSuggestedName = nameGenerator.GenerateSuggestedName(AgentRole.LeadArchitect, skillRegistry.GetSkillsForRole(AgentRole.LeadArchitect), seed: 42);
        var archPrompt = nameGenerator.GenerateSystemPrompt(AgentRole.LeadArchitect, archSuggestedName, skillRegistry.GetSkillsForRole(AgentRole.LeadArchitect));

        archPrompt.ShouldContain("DELIVERABLE ISOLATION CONTRACT");
        archPrompt.ShouldContain("Distributed Raft Consensus");
        archPrompt.ShouldContain("zero-allocation log entry buffers");

        var devSuggestedName = nameGenerator.GenerateSuggestedName(AgentRole.SoftwareDeveloper, skillRegistry.GetSkillsForRole(AgentRole.SoftwareDeveloper), seed: 42);
        var devPrompt = nameGenerator.GenerateSystemPrompt(AgentRole.SoftwareDeveloper, devSuggestedName, skillRegistry.GetSkillsForRole(AgentRole.SoftwareDeveloper));

        devPrompt.ShouldContain("DELIVERABLE ISOLATION CONTRACT");
        devPrompt.ShouldContain("Zero-Copy Memory Pipelines");

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
        // PILLAR 2: TEAM PROCESS DEFINITION (Archetypes, custom team, JSON export/import, CLAW graph)
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
            Members: customMembers,
            Graph: WorkflowGraph.CreateDefaultEngineeringCircus(),
            DefaultFallbackModel: "anthropic/claude-3.7-sonnet",
            CreatedAt: DateTimeOffset.UtcNow
        );

        teamManager.SaveTeam(customTeamDef);
        teamManager.SetCurrentTeam(customTeamDef);

        var activeTeam = teamManager.GetCurrentTeam();
        activeTeam.Id.ShouldBe("team-raft-circus-e2e");
        activeTeam.Members.Count().ShouldBe(customTeamDef.Members.Count);

        // Validate JSON export and import roundtrip
        var exportedJson = teamManager.ExportToJson(customTeamDef.Id);
        exportedJson.ShouldContain("skill-raft-consensus");
        exportedJson.ShouldContain("skill-zero-copy-memory");

        var reimportedTeam = teamManager.ImportFromJson(exportedJson);
        reimportedTeam.ShouldNotBeNull();
        reimportedTeam.Members.Count().ShouldBe(customTeamDef.Members.Count);

        // Define CLAW Workflow Graph with failure loopback ports
        var workflowGraph = WorkflowGraph.CreateDefaultEngineeringCircus();
        workflowGraph.Nodes.Count().ShouldBe(8);
        workflowGraph.Connections.ShouldContain(c => c.SourcePort == PortType.Failure && c.TargetNodeId == "node-dev");

        // =========================================================================
        // PILLAR 3: TICKET PERSISTENCE, TRANSITIONS & CHANGING ASSIGNMENTS (Work decomposition, handoffs, failure loopback)
        // =========================================================================
        var ticketStore = new TicketStore(_storageService);
        var eventStream = new AgentEventStream();
        var memoryStore = new EmbeddedVectorMemoryStore(_storageService);
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore);
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(ticketStore);
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var knowledgeMap = new KnowledgeMapService(_storageService);
        var selfImprovement = new SelfImprovementEngine(memoryStore, ticketStore, knowledgeMap, eventStream, _storageService);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompositionEngine,
            handoffRouter,
            executionEngine,
            eventStream,
            consolidationEngine,
            selfImprovement
        );

        executor.SetGraph(workflowGraph);

        // Pre-flight failure remediation test: Security rejects an initial draft to Developer
        var testTicket = new TicketItem(
            Id: "TCK-RAFT-DEV-001",
            ParentEpicId: null,
            Title: "Raft State Machine Implementation",
            Description: "Initial draft with unvalidated input buffers",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Wrap input in ReadOnlySpan<char>"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(testTicket);

        handoffRouter.RouteFailureRemediation(
            testTicket.Id,
            AgentRole.SecurityEngineer,
            AgentRole.SoftwareDeveloper,
            "Unsanitized input vector in state machine transition.",
            "Wrap input in ReadOnlySpan<char> and sanitize with allow-list regex immediately."
        );

        // Verify ticket transitioned to Remediating
        ticketStore.GetTicketById(testTicket.Id)!.Status.ShouldBe(TicketStatus.Remediating);

        // Execute remediation
        await executor.ExecuteTicketAsync(testTicket.Id);
        ticketStore.GetTicketById(testTicket.Id)!.Status.ShouldBe(TicketStatus.Done);

        // Execute full workflow
        var workflowSuccess = await executor.ExecuteWorkflowAsync(
            epicTitle: "Zero-Allocation Distributed Raft Consensus Engine",
            epicDescription: "Build a high-performance Raft cluster engine in C# 13 with zero Gen0 allocations, connectable failure recovery, and strict validation."
        );

        workflowSuccess.ShouldBeTrue();

        // Validate all tickets were decomposed, transitioned, and completed
        var allTickets = ticketStore.GetAllTickets();
        allTickets.ShouldNotBeEmpty();
        allTickets.ShouldContain(t => t.Type == TicketType.Epic && t.Status == TicketStatus.Done);
        allTickets.Where(t => t.Type == TicketType.Subtask).ShouldAllBe(t => t.Status == TicketStatus.Done);

        // Validate inter-agent handoff packets were created, dispatched, and recorded
        var allHandoffs = ticketStore.GetAllHandoffs();
        allHandoffs.ShouldNotBeEmpty();

        // Verify failure remediation handoff occurred and was properly addressed
        var failureHandoff = allHandoffs.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.RemediationNotes));
        failureHandoff.ShouldNotBeNull();
        failureHandoff!.FromAgentRole.ShouldBe(AgentRole.SecurityEngineer);
        failureHandoff.ToAgentRole.ShouldBe(AgentRole.SoftwareDeveloper);
        failureHandoff.RemediationNotes.ShouldNotBeNull();
        failureHandoff.RemediationNotes.ShouldContain("ReadOnlySpan<char>");

        // Verify deliverables were attached to tickets
        var totalDeliverables = allTickets.Sum(t => t.Deliverables.Count);
        totalDeliverables.ShouldBeGreaterThan(0);

        // =========================================================================
        // PILLAR 6: LEARNINGS PERSISTENCE (Self-improvement cycles, Knowledge Map, Procedural & Semantic Memory)
        // =========================================================================
        var report = await selfImprovement.RunSelfImprovementCycleAsync();

        report.TotalCyclesRun.ShouldBeGreaterThan(0);
        report.InsightsDistilledCount.ShouldBeGreaterThan(0);
        report.ProceduralRecipesGenerated.ShouldBeGreaterThan(0);
        report.SemanticRulesReinforced.ShouldBeGreaterThan(0);

        // Verify knowledge map node was distilled from the failure remediation handoff
        var distilledNode = knowledgeMap.GetNode($"KN-LI-REM-{failureHandoff.TicketId}");
        distilledNode.ShouldNotBeNull();
        distilledNode!.Category.ShouldBe("LearnedInsight");
        distilledNode.Summary.ShouldContain("Remediation Lesson:");
        distilledNode.Attributes.ShouldContainKey("SourceRole");

        // Verify procedural and semantic memories exist in vector store
        var proceduralMemories = await memoryStore.GetByTypeAsync(MemoryType.Procedural);
        proceduralMemories.ShouldNotBeEmpty();
        proceduralMemories.First().Content.ShouldContain("Standard Execution Recipe");

        var semanticMemories = await memoryStore.GetByTypeAsync(MemoryType.Semantic);
        semanticMemories.ShouldNotBeEmpty();
        semanticMemories.ShouldContain(m => m.Content.Contains("Reinforced Rule"));

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
        healthReport.IsHealthy.ShouldBeTrue();
        healthReport.TotalFilesCount.ShouldBeGreaterThan(0);

        var artifactsExist = await restartedStorage.FileExistsAsync("artifacts/LEARNED_INSIGHTS.md");
        artifactsExist.ShouldBeTrue();
        var learnedInsightsMd = await restartedStorage.LoadTextAsync("artifacts/LEARNED_INSIGHTS.md");
        learnedInsightsMd.ShouldNotBeNull();
        learnedInsightsMd.ShouldContain("Autonomous Self-Improvement & Continuous Learning Report");
        learnedInsightsMd.ShouldContain("KN-LI-REM-");

        // 2. Verify Skills Hub reloads from storage
        var restartedSkillRegistry = new SkillRegistry(skillImporter, restartedStorage);
        var loadedRaftSkill = restartedSkillRegistry.GetSkill("skill-raft-consensus");
        loadedRaftSkill.ShouldNotBeNull();
        loadedRaftSkill!.Name.ShouldBe("Distributed Raft Consensus");
        loadedRaftSkill.AssignedRoles.ShouldNotBeNull();
        loadedRaftSkill.AssignedRoles.ShouldContain(AgentRole.LeadArchitect);

        // 3. Verify Team Definition reloads from storage
        var restartedTeamManager = new TeamDefinitionManager(restartedStorage);
        var loadedTeams = restartedTeamManager.GetAllTeams();
        loadedTeams.ShouldContain(t => t.Id == "team-raft-circus-e2e");
        var activeLoadedTeam = restartedTeamManager.GetCurrentTeam();
        activeLoadedTeam.ShouldNotBeNull();

        // 4. Verify Tickets and Handoffs reload from storage
        var restartedTicketStore = new TicketStore(restartedStorage);
        var reloadedTickets = restartedTicketStore.GetAllTickets();
        reloadedTickets.Count().ShouldBe(allTickets.Count);
        reloadedTickets.Where(t => t.Type == TicketType.Subtask).ShouldAllBe(t => t.Status == TicketStatus.Done);

        var reloadedHandoffs = restartedTicketStore.GetAllHandoffs();
        reloadedHandoffs.Count().ShouldBe(allHandoffs.Count);
        reloadedHandoffs.ShouldContain(h => h.FromAgentRole == AgentRole.SecurityEngineer && !string.IsNullOrWhiteSpace(h.RemediationNotes));

        // 5. Verify Knowledge Map reloads from storage
        var restartedKnowledgeMap = new KnowledgeMapService(restartedStorage);
        var reloadedDistilledNode = restartedKnowledgeMap.GetNode($"KN-LI-REM-{failureHandoff.TicketId}");
        reloadedDistilledNode.ShouldNotBeNull();
        reloadedDistilledNode!.Summary.ShouldContain("Remediation Lesson:");

        // 6. Verify Memory Store reloads and supports semantic search
        var restartedMemoryStore = new EmbeddedVectorMemoryStore(restartedStorage);
        var searchResults = await restartedMemoryStore.SearchAsync("Raft execution recipe zero-allocation span", topK: 3);
        searchResults.ShouldNotBeEmpty();
        searchResults[0].SimilarityScore.ShouldBeGreaterThan(0.2f);

        // 7. Verify Self-Improvement Engine reloads report state
        var restartedSelfImprovement = new SelfImprovementEngine(
            restartedMemoryStore,
            restartedTicketStore,
            restartedKnowledgeMap,
            eventStream,
            restartedStorage
        );
        var latestReport = restartedSelfImprovement.GetLatestReport();
        latestReport.TotalCyclesRun.ShouldBe(report.TotalCyclesRun);
        latestReport.InsightsDistilledCount.ShouldBe(report.InsightsDistilledCount);
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
            name.ShouldNotBeNullOrWhiteSpace();

            var prompt = generator.GenerateSystemPrompt(role, name, skills);
            prompt.ShouldContain(name);
            prompt.ShouldContain("DELIVERABLE ISOLATION CONTRACT");
            prompt.ShouldContain("Strict rule instruction");
        }
    }

    [Fact]
    public async Task TeamProcessDefinition_And_WorkflowGraph_ShouldConfigureAndSerializeCorrectly()
    {
        var teamManager = new TeamDefinitionManager(_storageService);
        var team = teamManager.GetTeam("team-security-hardened") ?? teamManager.GetAllTeams().First();

        team.ShouldNotBeNull();
        team.Members.Count().ShouldBe(Enum.GetValues<AgentRole>().Length);

        var json = teamManager.ExportToJson(team.Id);
        json.ShouldContain(team.Id);
        json.ShouldContain("Zero-Trust");

        var imported = teamManager.ImportFromJson(json);
        imported.Id.ShouldStartWith("team-import-");
        imported.Members.Count().ShouldBe(Enum.GetValues<AgentRole>().Length);

        var graph = WorkflowGraph.CreateDefaultEngineeringCircus();
        graph.Nodes.Count().ShouldBe(8);

        // Modify graph
        var customConnection = new PortConnection("node-sec", PortType.Output, "node-qa", PortType.Input);
        graph = graph with { Connections = [.. graph.Connections, customConnection] };
        graph.Connections.ShouldContain(customConnection);
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
        new[] { AgentRole.LeadArchitect, AgentRole.SoftwareDeveloper, AgentRole.OptimizationEngineer }.ShouldBeSubsetOf(roles);

        await registry.FlushAsync();

        // Cold-start reload
        var reloadedRegistry = new SkillRegistry(importer, _storageService);
        var loaded = reloadedRegistry.GetSkill("skill-e2e-matrix-test");
        loaded.ShouldNotBeNull();
        loaded!.AssignedRoles.ShouldNotBeNull();
        new[] { AgentRole.LeadArchitect, AgentRole.SoftwareDeveloper, AgentRole.OptimizationEngineer }.ShouldBeSubsetOf(loaded.AssignedRoles);
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

        handoff.ShouldNotBeNull();
        ticketStore.GetHandoffsForTicket(underReview.Id).Count().ShouldBe(1);

        // Security rejects and routes failure remediation back to Dev
        var remediationHandoff = router.RouteFailureRemediation(
            underReview.Id,
            AgentRole.SecurityEngineer,
            AgentRole.SoftwareDeveloper,
            "Memory boundary violation on wrap-around index",
            "Mask index with buffer capacity - 1 using bitwise AND"
        );

        var remediatingTicket = ticketStore.GetTicketById(underReview.Id);
        remediatingTicket!.Status.ShouldBe(TicketStatus.Remediating);
        remediatingTicket.AssigneeRole.ShouldBe(AgentRole.SoftwareDeveloper);

        // Software Developer fixes and Completes ticket
        var fixedDeliverable = deliverable with { Content = "public sealed class RingBuffer<T> { /* bitwise masked */ }" };
        var completedTicket = remediatingTicket.WithDeliverable(fixedDeliverable);
        ticketStore.UpdateTicket(completedTicket);

        router.AdvanceWorkflowOnTicketCompletion(completedTicket.Id);

        var finalTicket = ticketStore.GetTicketById(completedTicket.Id);
        finalTicket!.Status.ShouldBe(TicketStatus.Done);
        finalTicket.Deliverables.Count().ShouldBe(2);

        await ticketStore.FlushAsync();

        // Cold reload validation
        var reloadedStore = new TicketStore(_storageService);
        var loadedTicket = reloadedStore.GetTicketById(ticket.Id);
        loadedTicket.ShouldNotBeNull();
        loadedTicket!.Status.ShouldBe(TicketStatus.Done);
        loadedTicket.Deliverables.Count().ShouldBe(2);

        var loadedHandoffs = reloadedStore.GetHandoffsForTicket(ticket.Id);
        loadedHandoffs.Count().ShouldBe(2);
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

        report.DistilledInsights.ShouldNotBeEmpty();
        var node = knowledgeMap.GetNode("KN-LI-REM-TCK-LEARN-01");
        node.ShouldNotBeNull();
        node!.Summary.ShouldContain("GeneratedRegexAttribute");

        await knowledgeMap.FlushAsync();

        // Verify across reload
        var reloadedKnowledgeMap = new KnowledgeMapService(_storageService);
        var reloadedNode = reloadedKnowledgeMap.GetNode("KN-LI-REM-TCK-LEARN-01");
        reloadedNode.ShouldNotBeNull();
        reloadedNode!.Category.ShouldBe("LearnedInsight");
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
