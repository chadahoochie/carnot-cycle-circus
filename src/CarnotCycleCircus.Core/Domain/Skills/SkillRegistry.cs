using System.Collections.Concurrent;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Skills;

public record SkillDefinition(
    string Id,
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<string> RecommendedTools,
    string Category = "General",
    IReadOnlyList<AgentRole>? AssignedRoles = null
);

public interface ISkillImporter
{
    SkillDefinition ParseSkillMarkdown(string content, string? sourceId = null);
    SkillDefinition ParseSkillJson(string json);
    Task<SkillDefinition> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default);
}

public class SkillImporter : ISkillImporter
{
    private readonly HttpClient _httpClient;

    public SkillImporter(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public SkillDefinition ParseSkillMarkdown(string content, string? sourceId = null)
    {
        var name = "Imported Skill";
        var description = "Custom imported capability";
        var instructions = content;
        var category = "Engineering";

        if (content.StartsWith("---"))
        {
            var parts = content.Split("---", 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var yaml = parts[0];
                instructions = parts[1].Trim();

                foreach (var line in yaml.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    {
                        name = trimmed[5..].Trim();
                    }
                    else if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    {
                        description = trimmed[12..].Trim();
                    }
                    else if (trimmed.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
                    {
                        category = trimmed[9..].Trim();
                    }
                }
            }
        }

        var id = sourceId ?? $"skill-{Guid.NewGuid().ToString("N")[..6]}";
        return new SkillDefinition(
            Id: id,
            Name: name,
            Description: description,
            Instructions: instructions,
            RecommendedTools: ["csharp_syntax_check", "web_search", "memory_lookup"],
            Category: category
        );
    }

    public SkillDefinition ParseSkillJson(string json)
    {
        return JsonSerializer.Deserialize<SkillDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize skill JSON.");
    }

    public async Task<SkillDefinition> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var text = await _httpClient.GetStringAsync(url, cts.Token);
        var uri = new Uri(url);
        var slug = uri.Segments.LastOrDefault()?.Replace(".md", "") ?? "web-skill";

        return ParseSkillMarkdown(text, $"skill-{slug}");
    }
}

public interface ISkillRegistry
{
    IReadOnlyList<SkillDefinition> GetAllSkills();
    SkillDefinition? GetSkill(string id);
    SkillDefinition RegisterSkill(SkillDefinition skill);
    bool UnregisterSkill(string id);
    void AssignSkillToRole(string skillId, AgentRole role);
    void UnassignSkillFromRole(string skillId, AgentRole role);
    IReadOnlyList<SkillDefinition> GetSkillsForRole(AgentRole role);
}

public class SkillRegistry : ISkillRegistry
{
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<AgentRole>> _roleAssignments = new(StringComparer.OrdinalIgnoreCase);

    public SkillRegistry(ISkillImporter importer)
    {
        // Seed default skills
        var s1 = new SkillDefinition(
            Id: "skill-csharp-standards",
            Name: "Modern C# 13 Coding Standards",
            Description: "Patterns for readonly record structs, pattern matching, Span/Memory, and async/await.",
            Instructions: "Enforce zero-allocation Span/Memory where possible, use records for immutable domain modeling.",
            RecommendedTools: ["csharp_syntax_check"],
            Category: "Architecture",
            AssignedRoles: [AgentRole.SoftwareDeveloper, AgentRole.LeadArchitect]
        );
        RegisterSkill(s1);
        AssignSkillToRole(s1.Id, AgentRole.SoftwareDeveloper);
        AssignSkillToRole(s1.Id, AgentRole.LeadArchitect);

        var s2 = new SkillDefinition(
            Id: "skill-stride-modeling",
            Name: "STRIDE Threat Modeling",
            Description: "Vulnerability analysis covering Spoofing, Tampering, Repudiation, Info Disclosure, DoS, and Privilege Elevation.",
            Instructions: "Check trust boundaries, sanitize all inputs, and enforce permission scopes.",
            RecommendedTools: ["web_search", "memory_lookup"],
            Category: "Security",
            AssignedRoles: [AgentRole.SecurityEngineer]
        );
        RegisterSkill(s2);
        AssignSkillToRole(s2.Id, AgentRole.SecurityEngineer);

        var s3 = new SkillDefinition(
            Id: "skill-perf-benchmarks",
            Name: "Zero-Allocation Performance Optimization",
            Description: "Latency profiling, ValueTask pipelines, and GC Gen0 minimization.",
            Instructions: "Verify benchmark metrics, ensure < 5ms P99 latency and 0 byte hot path allocations.",
            RecommendedTools: ["test_runner"],
            Category: "Optimization",
            AssignedRoles: [AgentRole.OptimizationEngineer]
        );
        RegisterSkill(s3);
        AssignSkillToRole(s3.Id, AgentRole.OptimizationEngineer);
    }

    public IReadOnlyList<SkillDefinition> GetAllSkills() =>
        _skills.Values.OrderBy(s => s.Name).ToList();

    public SkillDefinition? GetSkill(string id) =>
        _skills.TryGetValue(id, out var skill) ? skill : null;

    public SkillDefinition RegisterSkill(SkillDefinition skill)
    {
        _skills[skill.Id] = skill;
        if (skill.AssignedRoles != null)
        {
            var set = _roleAssignments.GetOrAdd(skill.Id, _ => new HashSet<AgentRole>());
            foreach (var r in skill.AssignedRoles) set.Add(r);
        }
        return skill;
    }

    public bool UnregisterSkill(string id) => _skills.TryRemove(id, out _);

    public void AssignSkillToRole(string skillId, AgentRole role)
    {
        var set = _roleAssignments.GetOrAdd(skillId, _ => new HashSet<AgentRole>());
        lock (set) { set.Add(role); }
    }

    public void UnassignSkillFromRole(string skillId, AgentRole role)
    {
        if (_roleAssignments.TryGetValue(skillId, out var set))
        {
            lock (set) { set.Remove(role); }
        }
    }

    public IReadOnlyList<SkillDefinition> GetSkillsForRole(AgentRole role)
    {
        var result = new List<SkillDefinition>();
        foreach (var kvp in _roleAssignments)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.Contains(role) && _skills.TryGetValue(kvp.Key, out var skill))
                {
                    result.Add(skill);
                }
            }
        }
        return result;
    }
}
