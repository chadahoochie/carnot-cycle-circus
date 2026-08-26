using System.Security.Cryptography;
using System.Text;
using CarnotCycleCircus.Core.Domain.Skills;

namespace CarnotCycleCircus.Core.Domain.Agents;

public interface IAgentNameGenerator
{
    string GenerateSuggestedName(AgentRole role, IEnumerable<SkillDefinition>? skills = null, int? seed = null);
    IReadOnlyList<string> GenerateNameSuggestions(AgentRole role, IEnumerable<SkillDefinition>? skills = null, int count = 4, int? seed = null);
    string GenerateSystemPrompt(AgentRole role, string agentName, IEnumerable<SkillDefinition>? skills = null);
}

public class AgentNameGenerator : IAgentNameGenerator
{
    private static readonly string[] Honorifics =
    [
        "Archduke", "Baroness", "Count", "Countess", "Professor", "Maestro", "Madame",
        "Captain", "Lord", "Lady", "Doctor", "Duchess", "Brother", "Grand Inquisitor",
        "Dame", "Major", "High Priest", "General", "Sir", "Prince", "Baron",
        "Archbishop of Anti-Patterns", "Lord of the Bounded Channels", "Knight Who Says Ni",
        "Lt. Detective", "Chosen One", "Grand Moff", "Cousin", "Sheriff"
    ];

    private static readonly string[] FirstNames =
    [
        "Barnum", "Archibald", "Devon", "Sari", "Otto", "Quinn",
        "Genevieve", "Percival", "Ignatius", "Seraphina", "Valerian", "Cassandra",
        "Mortimer", "Bartholomew", "Clementine", "Leopold", "Wilhelmina", "Lysander",
        "Ambrose", "Octavia", "Cornelius", "Beatrice", "Aloysius", "Maximilian",
        "Hildegard", "Zephaniah", "Barnaby", "Thaddeus", "Wolfgang", "Felix",
        "Delilah", "Tobias", "Lucian", "Cosmo", "Silas", "Theodore", "Evangeline",
        "Dark-Helmet", "Blinkin", "Fronkensteen", "Navin", "Bobby", "Frank-the-Tank",
        "Thorny", "Wimp-Lo", "Rumack", "Drebin", "Griswold", "Spackler", "Ace"
    ];

    private static readonly Dictionary<AgentRole, (string RoleSuffix, string DefaultAct, string[] DefaultNicknames, string[] DefaultSurnames)> RoleDefaults = new()
    {
        [AgentRole.TechnicalProductManager] = (
            "TPM",
            "Grand Ringmaster of Agility",
            ["Buzzword", "Velocity", "Ludicrous-Speed", "Plaid", "Synergy", "Scope-Creep", "Sprint-Master", "Chance-Haver"],
            ["Buzzword", "Jira-Juggler", "Story-Spinner", "Standup-Barker", "Gantt-Gladiator", "Roadmap-Rodeo", "Phonebook-Finder"]
        ),
        [AgentRole.LeadArchitect] = (
            "Lead Architect",
            "High Trapeze Artist of Pure Abstractions",
            ["Abstraction-o", "Cathedral", "Monad", "Indirection", "Clean-Arch", "Fronkensteen", "Interface-Purist", "Holy-Grail"],
            ["Abstraction-o", "Cathedral-Builder", "Monad-Maker", "Layer-Stacker", "Decoupler-General", "Pattern-Puppeteer", "Abby-Normal"]
        ),
        [AgentRole.SoftwareDeveloper] = (
            "Senior Developer",
            "Fire-Breathing Gen0 Destroyer",
            ["Coldbrew", "Zero-Alloc", "Span-Swallower", "Crashdump", "High-Quality-H2O", "Like-A-Glove", "Holy-Schnikes", "Segfault-Surfer"],
            ["Crashdump", "Byte-Breather", "Span-Swallower", "Segfault", "Heap-Banisher", "Deadlock-Defier", "Little-Coat"]
        ),
        [AgentRole.SecurityEngineer] = (
            "Security Engineer",
            "Lion Tamer of Unsanitized Input",
            ["Tinfoil", "Zero-Trust", "Its-A-Trap", "Spanish-Inquisition", "STRIDE-Tamer", "Airgap-Acrobat", "Raspberry-Jam", "Biohazard-Buster"],
            ["Sandbox", "Firewall-Flinger", "Tinfoil-Lion", "Threat-Tamer", "Cipher-Clown", "Airgap-Sentinel", "Ackbar-Sentinel"]
        ),
        [AgentRole.OptimizationEngineer] = (
            "Optimization Engineer",
            "Sub-Nanosecond Tightrope Walker",
            ["Overclock", "Sub-Nanosecond", "Enhance-Enhance", "P99-Slasher", "Flamegraph-Feeder", "Zero-Byte", "Goin-For-Me", "Cache-Line"],
            ["Overclock", "Nanosecond-Tightroper", "Flamegrapher", "Cycle-Cruncher", "Microbenchmark-Mage", "Latency-Juggler", "Cinderella-Story"]
        ),
        [AgentRole.PrincipalQAAnalyst] = (
            "Principal QA Analyst",
            "Chaos Clown of Software Torture",
            ["Build-Executioner", "Tis-But-A-Scratch", "Lots-Of-Nuts", "Chaos-Clown", "Demonic-Payload", "Negative-Infinity", "Shitter-Full", "Fuzz-Master"],
            ["Build-Executioner", "Chaos-Clown", "Assertion-Assassin", "NullPointer-Puppeteer", "Fuzz-Thrower", "Edge-Executioner", "Gnodab-Crusher"]
        )
    };

    public string GenerateSuggestedName(AgentRole role, IEnumerable<SkillDefinition>? skills = null, int? seed = null)
    {
        var suggestions = GenerateNameSuggestions(role, skills, count: 1, seed: seed);
        return suggestions[0];
    }

    public IReadOnlyList<string> GenerateNameSuggestions(AgentRole role, IEnumerable<SkillDefinition>? skills = null, int count = 4, int? seed = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var roleInfo = RoleDefaults.GetValueOrDefault(role, ("Agent", "Circus Performer", ["Circus"], ["Performer"]));
        var skillList = skills?.ToList() ?? [];

        var extractedThemes = ExtractSkillThemes(skillList, role);
        var results = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < count * 3 && results.Count < count; i++)
        {
            var honorific = Honorifics[rng.Next(Honorifics.Length)];
            var firstName = FirstNames[rng.Next(FirstNames.Length)];
            var theme = extractedThemes[rng.Next(extractedThemes.Count)];

            var pattern = (i % 5);
            string name = pattern switch
            {
                0 => $"{honorific} {firstName} \"{theme.Nickname}\" {theme.Surname} ({roleInfo.RoleSuffix})",
                1 => $"{firstName} \"{theme.Nickname}\" {theme.Surname} ({roleInfo.RoleSuffix})",
                2 => $"{honorific} {firstName} the {theme.Act} ({roleInfo.RoleSuffix})",
                3 => $"{firstName} \"{theme.Nickname}\" {roleInfo.DefaultSurnames[rng.Next(roleInfo.DefaultSurnames.Length)]} ({roleInfo.RoleSuffix})",
                _ => $"{honorific} {firstName} \"{theme.Nickname}\" ({roleInfo.RoleSuffix})"
            };

            if (usedNames.Add(name))
            {
                results.Add(name);
            }
        }

        while (results.Count < count)
        {
            var h = Honorifics[rng.Next(Honorifics.Length)];
            var f = FirstNames[rng.Next(FirstNames.Length)];
            var fallback = $"{h} {f} \"{roleInfo.DefaultNicknames[rng.Next(roleInfo.DefaultNicknames.Length)]}\" {roleInfo.DefaultSurnames[rng.Next(roleInfo.DefaultSurnames.Length)]} ({roleInfo.RoleSuffix})";
            if (usedNames.Add(fallback))
            {
                results.Add(fallback);
            }
        }

        return results;
    }

    public string GenerateSystemPrompt(AgentRole role, string agentName, IEnumerable<SkillDefinition>? skills = null)
    {
        var roleInfo = RoleDefaults.GetValueOrDefault(role, ("Agent", "Circus Performer", ["Circus"], ["Performer"]));
        var skillList = skills?.ToList() ?? [];

        var sb = new StringBuilder();
        sb.Append($"You are {agentName}, the {roleInfo.DefaultAct} and {role.ToDisplayName()}. ");
        sb.Append("In conversational chatter, banter, and thought logs, you exhibit an eccentric, theatrical circus persona with deep technical mastery. ");

        if (skillList.Count > 0)
        {
            sb.Append("You possess specialized skills and cognitive directives in: ");
            sb.Append(string.Join(", ", skillList.Select(s => s.Name)));
            sb.Append(". ");

            sb.Append("\n\nCOGNITIVE SKILL DIRECTIVES:\n");
            foreach (var s in skillList)
            {
                sb.AppendLine($"- [{s.Name}]: {s.Instructions}");
            }
            sb.AppendLine();
        }

        sb.Append("DELIVERABLE ISOLATION CONTRACT: All technical deliverables (source code, ADRs, test suites, acceptance criteria, STRIDE threat models, benchmark reports, and ticket definitions) MUST remain strictly professional, unambiguous, rigorous, production-grade, and completely free of joke text or sarcastic phrasing.");

        return sb.ToString();
    }

    private static List<SkillTheme> ExtractSkillThemes(IReadOnlyList<SkillDefinition> skills, AgentRole role)
    {
        var list = new List<SkillTheme>();

        if (skills.Count == 0)
        {
            var r = RoleDefaults.GetValueOrDefault(role, ("Agent", "Circus Performer", ["Circus"], ["Performer"]));
            for (int i = 0; i < r.DefaultNicknames.Length; i++)
            {
                var nick = r.DefaultNicknames[i];
                var sur = r.DefaultSurnames[i % r.DefaultSurnames.Length];
                list.Add(new SkillTheme(nick, sur, r.DefaultAct));
            }
            return list;
        }

        foreach (var skill in skills)
        {
            var text = $"{skill.Id} {skill.Name} {skill.Description} {skill.Category}".ToLowerInvariant();

            if (text.Contains("csharp") || text.Contains("zero-allocation") || text.Contains("struct") || text.Contains("span") || text.Contains("memory") || text.Contains("allocation"))
            {
                list.Add(new SkillTheme("Zero-Alloc", "Byte-Trapeze", "Span-Swallowing Acrobat"));
                list.Add(new SkillTheme("Span-Swallower", "Struct-Smith", "Zero-Allocation Trapeze Artist"));
                list.Add(new SkillTheme("Memory-Pinning", "Heap-Banisher", "Heap-Banishing Fire-Breather"));
                list.Add(new SkillTheme("Struct-Only", "Pointer-Tamer", "Low-Level Pointer Tamer"));
            }
            else if (text.Contains("stride") || text.Contains("threat") || text.Contains("security") || text.Contains("paranoid") || text.Contains("zero-trust") || text.Contains("auth") || text.Contains("tinfoil"))
            {
                list.Add(new SkillTheme("STRIDE-Tamer", "Threat-Shield", "Lion Tamer of Unsanitized Input"));
                list.Add(new SkillTheme("Zero-Trust", "Airgap-Sentinel", "Zero-Trust High-Wire Walker"));
                list.Add(new SkillTheme("Tinfoil-Lion", "Sandbox-Sentinel", "Paranoid Firewall Acrobat"));
                list.Add(new SkillTheme("Airgap-Acrobat", "Cipher-Clown", "Airgap Escapologist"));
            }
            else if (text.Contains("nanosecond") || text.Contains("benchmark") || text.Contains("perf") || text.Contains("optimization") || text.Contains("latency") || text.Contains("p99"))
            {
                list.Add(new SkillTheme("Sub-Nanosecond", "Tightrope-Overclocker", "Sub-Nanosecond Tightrope Walker"));
                list.Add(new SkillTheme("P99-Slasher", "Flamegrapher", "Flamegraph Fire-Eater"));
                list.Add(new SkillTheme("Zero-Byte", "Cycle-Cruncher", "Microbenchmark Magician"));
                list.Add(new SkillTheme("Cache-Line", "Latency-Juggler", "Latency-Defying Juggler"));
            }
            else if (text.Contains("jira") || text.Contains("buzzword") || text.Contains("agile") || text.Contains("scrum") || text.Contains("velocity") || text.Contains("epic"))
            {
                list.Add(new SkillTheme("Buzzword-Baron", "Buzzword-Ringmaster", "Grand Ringmaster of Agility"));
                list.Add(new SkillTheme("Velocity-Juggler", "Story-Spinner", "Velocity Juggler"));
                list.Add(new SkillTheme("Scope-Creep", "Gantt-Gladiator", "Scope-Creep Contortionist"));
                list.Add(new SkillTheme("Epic-Synergizer", "Roadmap-Rodeo", "Synergy Trapeze Master"));
            }
            else if (text.Contains("demonic") || text.Contains("edge-case") || text.Contains("qa") || text.Contains("test") || text.Contains("torture") || text.Contains("chaos") || text.Contains("fuzz"))
            {
                list.Add(new SkillTheme("Demonic-Payload", "Build-Executioner", "Chaos Clown of Software Torture"));
                list.Add(new SkillTheme("Chaos-Monkey", "Chaos-Clown", "Demonic Edge-Case Cannonball"));
                list.Add(new SkillTheme("Negative-Infinity", "NullPointer-Puppeteer", "Negative-Infinity Juggler"));
                list.Add(new SkillTheme("Fuzz-Master", "Assertion-Assassin", "Fuzz-Throwing Fire-Breather"));
            }
            else if (text.Contains("data") || text.Contains("marten") || text.Contains("cosmos") || text.Contains("sql") || text.Contains("redis") || text.Contains("storage") || text.Contains("postgres"))
            {
                list.Add(new SkillTheme("Shard-Juggler", "Data-Trapeze", "Shard-Juggling Contortionist"));
                list.Add(new SkillTheme("ACID-Alchemist", "Query-Caster", "ACID-Transaction Escapologist"));
                list.Add(new SkillTheme("Query-Tamer", "Store-Keeper", "Index-Optimizing Acrobat"));
            }
            else if (text.Contains("graphql"))
            {
                list.Add(new SkillTheme("GraphQL-Contortionist", "Schema-Weaver", "GraphQL Schema Illusionist"));
                list.Add(new SkillTheme("Schema-Weaver", "GraphQL-Trapeze", "Federated GraphQL Juggler"));
                list.Add(new SkillTheme("Query-Plan-Acrobat", "GraphQL-Barker", "High-Trapeze GraphQL Planner"));
            }
            else if (text.Contains("api") || text.Contains("rest") || text.Contains("wire") || text.Contains("protocol"))
            {
                list.Add(new SkillTheme("Wire-Walker", "Schema-Weaver", "High-Wire Wire Protocol Walker"));
                list.Add(new SkillTheme("Schema-Weaver", "Contract-Cracker", "Schema-Weaving Illusionist"));
                list.Add(new SkillTheme("Payload-Pilot", "Socket-Spinner", "API Wire Juggler"));
            }
            else if (text.Contains("evidence") || text.Contains("wcag") || text.Contains("accessibility") || text.Contains("audit") || text.Contains("compliance"))
            {
                list.Add(new SkillTheme("Evidence-Hoarder", "Proof-Finder", "Screenshot-Obsessed Ring Barker"));
                list.Add(new SkillTheme("WCAG-Warlock", "Pixel-Peeker", "WCAG High-Wire Inspector"));
                list.Add(new SkillTheme("Audit-Acrobat", "Rule-Enforcer", "Compliance High-Wire Acrobat"));
            }
            else
            {
                // Fallback for custom / domain skills: Extract primary significant keyword from skill name
                var words = skill.Name.Split([' ', '-', '_', '&', '/'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim())
                    .Where(w => w.Length > 2 && !NoiseWords.Contains(w, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var kw = words.Count > 0 ? words[0] : skill.Category;
                if (string.IsNullOrWhiteSpace(kw)) kw = "Specialist";

                list.Add(new SkillTheme($"{kw}-Tamer", $"{kw}-Trapeze", $"{kw} Tightrope Performer"));
                list.Add(new SkillTheme($"{kw}-Juggler", $"{kw}-Spinner", $"{kw} High-Wire Acrobat"));
                list.Add(new SkillTheme($"{kw}-Fireeater", $"{kw}-Clown", $"{kw} Illusionist"));
            }
        }

        if (list.Count == 0)
        {
            var r = RoleDefaults.GetValueOrDefault(role, ("Agent", "Circus Performer", ["Circus"], ["Performer"]));
            list.Add(new SkillTheme(r.DefaultNicknames[0], r.DefaultSurnames[0], r.DefaultAct));
        }

        return list;
    }

    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "of", "in", "to", "a", "an", "developer", "engineer",
        "specialist", "auditor", "manager", "architect", "expert", "master", "mode", "edition"
    };

    private sealed record SkillTheme(string Nickname, string Surname, string Act);
}
