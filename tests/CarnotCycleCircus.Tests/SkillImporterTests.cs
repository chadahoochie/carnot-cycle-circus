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
        ---
        # Instructions
        Analyze raw input streams and ensure parameterized queries are used.
        """;

        var skill = _importer.ParseSkillMarkdown(markdown);

        skill.Name.Should().Be("Custom Security Auditor");
        skill.Description.Should().Be("Audits code for SQL injection and XSS vulnerabilities.");
        skill.Category.Should().Be("Security");
        skill.Instructions.Should().Contain("Analyze raw input streams");
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
    }
}
