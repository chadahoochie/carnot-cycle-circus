using System.Collections.Concurrent;
using System.Text.Json;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Storage;

namespace CarnotCycleCircus.Core.Domain.Skills;

public record SkillDefinition(
    string Id,
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<string> RecommendedTools,
    string Category = "General",
    IReadOnlyList<AgentRole>? AssignedRoles = null
)
{
    public SkillDefinition WithAssignedRoles(IReadOnlyList<AgentRole> roles) =>
        this with { AssignedRoles = roles };
}

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
        string? explicitId = null;
        var recommendedTools = new List<string>();
        var assignedRoles = new List<AgentRole>();

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
                    if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = trimmed.IndexOf(':');
                        name = trimmed[(idx + 1)..].Trim().Trim('"', '\'');
                    }
                    else if (trimmed.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("skill_id:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("slug:", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = trimmed.IndexOf(':');
                        explicitId = trimmed[(idx + 1)..].Trim().Trim('"', '\'');
                    }
                    else if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("summary:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("vibe:", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = trimmed.IndexOf(':');
                        description = trimmed[(idx + 1)..].Trim().Trim('"', '\'');
                    }
                    else if (trimmed.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = trimmed.IndexOf(':');
                        category = trimmed[(idx + 1)..].Trim().Trim('"', '\'');
                    }
                    else if (trimmed.StartsWith("tools:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("recommended_tools:", StringComparison.OrdinalIgnoreCase) ||
                             trimmed.StartsWith("recommendedTools:", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = trimmed.IndexOf(':');
                        var toolsStr = trimmed[(idx + 1)..].Trim().Trim('[', ']');
                        foreach (var t in toolsStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var clean = t.Trim().Trim('"', '\'');
                            if (!string.IsNullOrEmpty(clean)) recommendedTools.Add(clean);
                        }
                    }
                }
            }
        }

        if (recommendedTools.Count == 0)
        {
            recommendedTools = ["csharp_syntax_check", "web_search", "memory_lookup"];
        }

        string id;
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            id = EnsureSkillIdPrefix(explicitId);
        }
        else if (!string.IsNullOrWhiteSpace(sourceId) && !IsGenericSlug(sourceId))
        {
            id = EnsureSkillIdPrefix(sourceId);
        }
        else if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Imported Skill", StringComparison.OrdinalIgnoreCase))
        {
            id = $"skill-{Slugify(name)}";
        }
        else if (!string.IsNullOrWhiteSpace(sourceId))
        {
            id = EnsureSkillIdPrefix(sourceId);
        }
        else
        {
            id = $"skill-{Guid.NewGuid().ToString("N")[..6]}";
        }

        return new SkillDefinition(
            Id: id,
            Name: name,
            Description: description,
            Instructions: instructions,
            RecommendedTools: recommendedTools,
            Category: category,
            AssignedRoles: assignedRoles.Count > 0 ? assignedRoles : null
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

        var segments = uri.Segments
            .Select(s => s.Trim('/', ' '))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        string? urlSlug = null;
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            var seg = segments[i];
            var cleanSeg = seg.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? seg[..^3]
                : (seg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? seg[..^5] : seg);

            if (!IsGenericSlug(cleanSeg))
            {
                urlSlug = cleanSeg;
                break;
            }
        }

        return ParseSkillMarkdown(text, urlSlug != null ? $"skill-{urlSlug}" : null);
    }

    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "custom";
        var clean = text.ToLowerInvariant().Trim();
        var chars = clean.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    public static string EnsureSkillIdPrefix(string id)
    {
        var clean = Slugify(id);
        return clean.StartsWith("skill-", StringComparison.OrdinalIgnoreCase)
            ? clean
            : $"skill-{clean}";
    }

    public static bool IsGenericSlug(string slug)
    {
        var s = slug.Trim().ToLowerInvariant();
        if (s.StartsWith("skill-")) s = s[6..];
        return s is "skill" or "skills" or "readme" or "index" or "main" or "master" or "raw" or "blob" or "web-skill" or "";
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
    IReadOnlyList<AgentRole> GetRolesForSkill(string skillId);
    void UpdateSkillRoles(string skillId, IEnumerable<AgentRole> roles);
}

public class SkillRegistry : ISkillRegistry
{
    private readonly ConcurrentDictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<AgentRole>> _roleAssignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPersistentStorageService? _storageService;
    private const string StorageFileName = "skills.json";

    public SkillRegistry(ISkillImporter importer, IPersistentStorageService? storageService = null)
    {
        _storageService = storageService;

        var loaded = LoadFromStorage();
        if (!loaded)
        {
            SeedDefaults();
            SaveToStorage();
        }
    }

    private bool LoadFromStorage()
    {
        if (_storageService == null) return false;
        try
        {
            var saved = _storageService.LoadJsonAsync<List<SkillDefinition>>(StorageFileName).GetAwaiter().GetResult();
            if (saved != null && saved.Count > 0)
            {
                foreach (var s in saved)
                {
                    _skills[s.Id] = s;
                    if (s.AssignedRoles != null && s.AssignedRoles.Count > 0)
                    {
                        var set = _roleAssignments.GetOrAdd(s.Id, _ => new HashSet<AgentRole>());
                        foreach (var r in s.AssignedRoles) set.Add(r);
                    }
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void SaveToStorage()
    {
        if (_storageService == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var list = GetAllSkills();
                await _storageService.SaveJsonAsync(StorageFileName, list);

                // Also save skill markdown documents in skills directory
                foreach (var s in list)
                {
                    var md = $"---\nname: {s.Name}\nid: {s.Id}\ndescription: {s.Description}\ncategory: {s.Category}\ntools: [{string.Join(", ", s.RecommendedTools)}]\n---\n\n{s.Instructions}";
                    await _storageService.SaveTextAsync($"skills/{s.Id}.md", md);
                }
            }
            catch
            {
                // Ignore transient write error
            }
        });
    }

    private void SeedDefaults()
    {
        // Seed default skills
        var s1 = new SkillDefinition(
            Id: "skill-csharp-standards",
            Name: "Modern C# 13 & Zero-Allocation Dogma",
            Description: "Patterns for readonly record structs, pattern matching, Span/Memory, and async/await.",
            Instructions: "Enforce zero-allocation Span/Memory where possible, use records for immutable domain modeling, and ban all setters.",
            RecommendedTools: ["csharp_syntax_check"],
            Category: "Architecture",
            AssignedRoles: [AgentRole.SoftwareDeveloper, AgentRole.LeadArchitect]
        );
        RegisterSkill(s1);

        var s2 = new SkillDefinition(
            Id: "skill-stride-modeling",
            Name: "STRIDE Threat Modeling (Paranoid Mode)",
            Description: "Vulnerability analysis covering Spoofing, Tampering, Repudiation, Info Disclosure, DoS, and Privilege Elevation.",
            Instructions: "Check trust boundaries, sanitize all inputs, reject hardcoded credentials, and assume everyone is a hacker.",
            RecommendedTools: ["web_search", "memory_lookup"],
            Category: "Security",
            AssignedRoles: [AgentRole.SecurityEngineer]
        );
        RegisterSkill(s2);

        var s3 = new SkillDefinition(
            Id: "skill-perf-benchmarks",
            Name: "Nanosecond Obsession & Zero Allocations",
            Description: "Latency profiling, ValueTask pipelines, and GC Gen0 minimization.",
            Instructions: "Verify benchmark metrics, ensure < 5ms P99 latency and 0 byte hot path allocations. If GC triggers, alert Otto.",
            RecommendedTools: ["test_runner"],
            Category: "Optimization",
            AssignedRoles: [AgentRole.OptimizationEngineer]
        );
        RegisterSkill(s3);

        var s4 = new SkillDefinition(
            Id: "skill-buzzword-mastery",
            Name: "Jira Juggling & Buzzword Mastery",
            Description: "Turning simple button clicks into synergistic multi-quarter value-stream epics.",
            Instructions: "Deconstruct simple tasks into 15 subtasks with high-priority agile labels and optimistic estimations.",
            RecommendedTools: ["web_search", "memory_lookup"],
            Category: "Management",
            AssignedRoles: [AgentRole.TechnicalProductManager]
        );
        RegisterSkill(s4);

        var s5 = new SkillDefinition(
            Id: "skill-edge-case-torture",
            Name: "Demonic Edge-Case Crafting",
            Description: "Crafting diabolical inputs (null bytes, emojis, negative infinity) to break developer confidence.",
            Instructions: "Generate maximum entropy payloads to test system resilience and failure port recovery.",
            RecommendedTools: ["test_runner"],
            Category: "Testing",
            AssignedRoles: [AgentRole.PrincipalQAAnalyst]
        );
        RegisterSkill(s5);
    }

    public IReadOnlyList<SkillDefinition> GetAllSkills()
    {
        return _skills.Values
            .Select(s =>
            {
                if (_roleAssignments.TryGetValue(s.Id, out var roles))
                {
                    lock (roles)
                    {
                        return s with { AssignedRoles = roles.OrderBy(r => r).ToList() };
                    }
                }
                return s;
            })
            .OrderBy(s => s.Name)
            .ToList();
    }

    public SkillDefinition? GetSkill(string id)
    {
        if (_skills.TryGetValue(id, out var skill))
        {
            if (_roleAssignments.TryGetValue(id, out var roles))
            {
                lock (roles)
                {
                    return skill with { AssignedRoles = roles.OrderBy(r => r).ToList() };
                }
            }
            return skill;
        }
        return null;
    }

    public SkillDefinition RegisterSkill(SkillDefinition skill)
    {
        if (skill.AssignedRoles != null)
        {
            var set = _roleAssignments.GetOrAdd(skill.Id, _ => new HashSet<AgentRole>());
            lock (set)
            {
                set.Clear();
                foreach (var r in skill.AssignedRoles) set.Add(r);
            }
        }
        else if (_roleAssignments.TryGetValue(skill.Id, out var existingRoles))
        {
            lock (existingRoles)
            {
                skill = skill with { AssignedRoles = existingRoles.OrderBy(r => r).ToList() };
            }
        }

        _skills[skill.Id] = skill;
        SaveToStorage();
        return skill;
    }

    public bool UnregisterSkill(string id)
    {
        _roleAssignments.TryRemove(id, out _);
        var removed = _skills.TryRemove(id, out _);
        if (removed) SaveToStorage();
        return removed;
    }

    public void AssignSkillToRole(string skillId, AgentRole role)
    {
        var set = _roleAssignments.GetOrAdd(skillId, _ => new HashSet<AgentRole>());
        lock (set) { set.Add(role); }
        if (_skills.TryGetValue(skillId, out var skill))
        {
            lock (set)
            {
                _skills[skillId] = skill with { AssignedRoles = set.OrderBy(r => r).ToList() };
            }
        }
        SaveToStorage();
    }

    public void UnassignSkillFromRole(string skillId, AgentRole role)
    {
        if (_roleAssignments.TryGetValue(skillId, out var set))
        {
            lock (set) { set.Remove(role); }
            if (_skills.TryGetValue(skillId, out var skill))
            {
                lock (set)
                {
                    _skills[skillId] = skill with { AssignedRoles = set.OrderBy(r => r).ToList() };
                }
            }
            SaveToStorage();
        }
    }

    public IReadOnlyList<AgentRole> GetRolesForSkill(string skillId)
    {
        if (_roleAssignments.TryGetValue(skillId, out var set))
        {
            lock (set)
            {
                return set.OrderBy(r => r).ToList();
            }
        }
        return Array.Empty<AgentRole>();
    }

    public void UpdateSkillRoles(string skillId, IEnumerable<AgentRole> roles)
    {
        var set = _roleAssignments.GetOrAdd(skillId, _ => new HashSet<AgentRole>());
        lock (set)
        {
            set.Clear();
            foreach (var r in roles) set.Add(r);
        }
        if (_skills.TryGetValue(skillId, out var skill))
        {
            lock (set)
            {
                _skills[skillId] = skill with { AssignedRoles = set.OrderBy(r => r).ToList() };
            }
        }
        SaveToStorage();
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
                    result.Add(skill with { AssignedRoles = kvp.Value.OrderBy(r => r).ToList() });
                }
            }
        }
        return result.OrderBy(s => s.Name).ToList();
    }
}
