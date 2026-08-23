using System.Net;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Skills;
using FluentAssertions;
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

        skill.Name.Should().Be("Custom Security Auditor");
        skill.Description.Should().Be("Audits code for SQL injection and XSS vulnerabilities.");
        skill.Category.Should().Be("Security");
        skill.Id.Should().Be("skill-custom-security-auditor");
        skill.RecommendedTools.Should().Contain(["csharp_syntax_check", "web_search"]);
        skill.Instructions.Should().Contain("Analyze raw input streams");
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
        skill.Id.Should().Be("skill-evidence-qa-specialist");
        skill.Name.Should().Be("Evidence Collector QA");
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
        qaSkills.Should().Contain(s => s.Id == "skill-custom-qa");

        var roles = registry.GetRolesForSkill("skill-custom-qa");
        roles.Should().Contain(AgentRole.PrincipalQAAnalyst);
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
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Updated Name");
        retrieved.Description.Should().Be("Updated Description");
        retrieved.Instructions.Should().Be("Updated Instructions");
        retrieved.RecommendedTools.Should().Contain("tool2");
        retrieved.AssignedRoles.Should().BeEquivalentTo([AgentRole.LeadArchitect, AgentRole.PrincipalQAAnalyst]);

        registry.GetSkillsForRole(AgentRole.SoftwareDeveloper).Should().NotContain(s => s.Id == "skill-test-edit");
        registry.GetSkillsForRole(AgentRole.LeadArchitect).Should().Contain(s => s.Id == "skill-test-edit");
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
        registry.GetSkill("skill-to-delete").Should().NotBeNull();

        var unregistered = registry.UnregisterSkill("skill-to-delete");
        unregistered.Should().BeTrue();

        registry.GetSkill("skill-to-delete").Should().BeNull();
        registry.GetRolesForSkill("skill-to-delete").Should().BeEmpty();
        registry.GetSkillsForRole(AgentRole.SecurityEngineer).Should().NotContain(s => s.Id == "skill-to-delete");
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

        skill1.Id.Should().NotBe(skill2.Id);
        skill1.Id.Should().Be("skill-engineering-backend-architect");
        skill2.Id.Should().Be("skill-testing-evidence-collector");

        registry.RegisterSkill(skill1);
        registry.RegisterSkill(skill2);

        registry.GetAllSkills().Should().Contain(s => s.Id == "skill-engineering-backend-architect");
        registry.GetAllSkills().Should().Contain(s => s.Id == "skill-testing-evidence-collector");
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
