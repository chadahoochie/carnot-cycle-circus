using System.Net;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Skills;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class SkillImporterTests
{
    private readonly SkillImporter _importer = new();

    [Fact]
    public void ParseSkillMarkdown_ShouldExtractYamlFrontmatterAndInstructions()
    {
        var markdown = """
        ---
        name: Custom Security Auditor
        description: Audits code for SQL injection and XSS vulnerabilities.
        category: Security
        tools: csharp_syntax_check, web_search
        ---
        # Instructions
        Analyze raw input streams and ensure parameterized queries are used.
        """;

        var skill = _importer.ParseSkillMarkdown(markdown);

        skill.Name.ShouldBe("Custom Security Auditor");
        skill.Description.ShouldBe("Audits code for SQL injection and XSS vulnerabilities.");
        skill.Category.ShouldBe("Security");
        skill.Id.ShouldBe("skill-custom-security-auditor");
        new[] { "csharp_syntax_check", "web_search" }.ShouldBeSubsetOf(skill.RecommendedTools);
        skill.Instructions.ShouldContain("Analyze raw input streams");
    }

    [Fact]
    public void ParseSkillMarkdown_WithExplicitId_ShouldPreserveExplicitId()
    {
        var markdown = """
        ---
        id: evidence-qa-specialist
        name: Evidence Collector QA
        description: Requires visual proof
        category: Testing
        ---
        # Instructions
        Capture screenshots.
        """;

        var skill = _importer.ParseSkillMarkdown(markdown);
        skill.Id.ShouldBe("skill-evidence-qa-specialist");
        skill.Name.ShouldBe("Evidence Collector QA");
    }

    [Fact]
    public void SkillRegistry_ShouldAssignAndRetrieveSkillsByRole()
    {
        var registry = new SkillRegistry(_importer);
        var skill = new SkillDefinition(
            Id: "skill-custom-qa",
            Name: "Custom QA Skill",
            Description: "QA skills",
            Instructions: "Run tests",
            RecommendedTools: ["test_runner"],
            Category: "Testing"
        );

        registry.RegisterSkill(skill);
        registry.AssignSkillToRole("skill-custom-qa", AgentRole.PrincipalQAAnalyst);

        var qaSkills = registry.GetSkillsForRole(AgentRole.PrincipalQAAnalyst);
        qaSkills.ShouldContain(s => s.Id == "skill-custom-qa");

        var roles = registry.GetRolesForSkill("skill-custom-qa");
        roles.ShouldContain(AgentRole.PrincipalQAAnalyst);
    }

    [Fact]
    public void SkillRegistry_ShouldUpdateSkillAndRoleAssignments()
    {
        var registry = new SkillRegistry(_importer);
        var original = new SkillDefinition(
            Id: "skill-test-edit",
            Name: "Original Name",
            Description: "Original Description",
            Instructions: "Original Instructions",
            RecommendedTools: ["tool1"],
            Category: "Testing",
            AssignedRoles: [AgentRole.SoftwareDeveloper]
        );

        registry.RegisterSkill(original);

        var updated = original with
        {
            Name = "Updated Name",
            Description = "Updated Description",
            Instructions = "Updated Instructions",
            RecommendedTools = ["tool1", "tool2"],
            AssignedRoles = [AgentRole.LeadArchitect, AgentRole.PrincipalQAAnalyst]
        };

        registry.RegisterSkill(updated);

        var retrieved = registry.GetSkill("skill-test-edit");
        retrieved.ShouldNotBeNull();
        retrieved!.Name.ShouldBe("Updated Name");
        retrieved.Description.ShouldBe("Updated Description");
        retrieved.Instructions.ShouldBe("Updated Instructions");
        retrieved.RecommendedTools.ShouldContain("tool2");
        retrieved.AssignedRoles.ShouldBe([AgentRole.LeadArchitect, AgentRole.PrincipalQAAnalyst], ignoreOrder: true);

        registry.GetSkillsForRole(AgentRole.SoftwareDeveloper).ShouldNotContain(s => s.Id == "skill-test-edit");
        registry.GetSkillsForRole(AgentRole.LeadArchitect).ShouldContain(s => s.Id == "skill-test-edit");
    }

    [Fact]
    public void SkillRegistry_ShouldUnregisterSkillAndCleanupRoles()
    {
        var registry = new SkillRegistry(_importer);
        var skill = new SkillDefinition(
            Id: "skill-to-delete",
            Name: "Delete Me",
            Description: "To be removed",
            Instructions: "None",
            RecommendedTools: ["test_runner"],
            Category: "General",
            AssignedRoles: [AgentRole.SecurityEngineer]
        );

        registry.RegisterSkill(skill);
        registry.GetSkill("skill-to-delete").ShouldNotBeNull();

        var unregistered = registry.UnregisterSkill("skill-to-delete");
        unregistered.ShouldBeTrue();

        registry.GetSkill("skill-to-delete").ShouldBeNull();
        registry.GetRolesForSkill("skill-to-delete").ShouldBeEmpty();
        registry.GetSkillsForRole(AgentRole.SecurityEngineer).ShouldNotContain(s => s.Id == "skill-to-delete");
    }

    [Fact]
    public async Task ImportFromUrlAsync_WithDifferentSkillMdUrls_ShouldProduceDistinctIds()
    {
        var handler = new MockHttpMessageHandler();
        handler.RegisterResponse(
            "https://raw.githubusercontent.com/org/repo/main/skills/engineering-backend-architect/SKILL.md",
            """
            ---
            name: Backend Architect
            description: Senior backend architect
            category: Architecture
            ---
            # Architecture Instructions
            """
        );
        handler.RegisterResponse(
            "https://raw.githubusercontent.com/org/repo/main/skills/testing-evidence-collector/SKILL.md",
            """
            ---
            name: Evidence Collector
            description: Screenshot-obsessed QA
            category: Testing
            ---
            # QA Instructions
            """
        );

        var client = new HttpClient(handler);
        var importer = new SkillImporter(client);
        var registry = new SkillRegistry(importer);

        var skill1 = await importer.ImportFromUrlAsync("https://raw.githubusercontent.com/org/repo/main/skills/engineering-backend-architect/SKILL.md");
        var skill2 = await importer.ImportFromUrlAsync("https://raw.githubusercontent.com/org/repo/main/skills/testing-evidence-collector/SKILL.md");

        skill1.Id.ShouldNotBe(skill2.Id);
        skill1.Id.ShouldBe("skill-engineering-backend-architect");
        skill2.Id.ShouldBe("skill-testing-evidence-collector");

        registry.RegisterSkill(skill1);
        registry.RegisterSkill(skill2);

        registry.GetAllSkills().ShouldContain(s => s.Id == "skill-engineering-backend-architect");
        registry.GetAllSkills().ShouldContain(s => s.Id == "skill-testing-evidence-collector");
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterResponse(string url, string content) => _responses[url] = content;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (_responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
