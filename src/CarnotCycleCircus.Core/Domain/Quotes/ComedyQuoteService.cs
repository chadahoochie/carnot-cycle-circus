namespace CarnotCycleCircus.Core.Domain.Quotes;

public record ComedyQuote(
    string Quote,
    string Movie,
    string Character,
    string Category
);

public interface IComedyQuoteService
{
    IReadOnlyList<ComedyQuote> GetAllQuotes();
    ComedyQuote GetRandomQuote();
    IReadOnlyList<ComedyQuote> GetQuotesByMovie(string movie);
    IReadOnlyList<ComedyQuote> GetQuotesByCategory(string category);
}

public class ComedyQuoteService : IComedyQuoteService
{
    private static readonly IReadOnlyList<ComedyQuote> Quotes =
    [
        // Spaceballs
        new("Ludicrous speed, GO!", "Spaceballs", "Dark Helmet", "Velocity & Performance"),
        new("They've gone to plaid!", "Spaceballs", "Barf", "Velocity & Performance"),
        new("Comb the desert! We ain't found shit!", "Spaceballs", "Dark Helmet & Trooper", "Testing & Search"),
        new("I'm surrounded by Assholes!", "Spaceballs", "Dark Helmet", "Team Collaboration"),
        new("Raspberry jam! There's only one man who would dare give me the raspberry... Lone Starr!", "Spaceballs", "Dark Helmet", "Security & Sabotage"),
        new("What's the matter, Colonel Sandurz? Chicken?", "Spaceballs", "Dark Helmet", "Deployment"),

        // Monty Python and the Holy Grail & Skits
        new("'Tis but a scratch! Just a flesh wound!", "Monty Python and the Holy Grail", "The Black Knight", "Debugging & Regressions"),
        new("Your mother was a hamster, and your father smelt of elderberries! Now go away or I shall taunt you a second time!", "Monty Python and the Holy Grail", "French Taunter", "Code Review"),
        new("Listen, strange women lyin' in ponds distributin' swords is no basis for a system of government!", "Monty Python and the Holy Grail", "Dennis", "Architecture & Governance"),
        new("Nobody expects the Spanish Inquisition!", "Monty Python's Flying Circus", "Cardinal Ximénez", "Security & Audits"),
        new("He's not pinin'! 'E's passed on! This parrot is no more! He has ceased to be! This is an EX-PARROT!", "Monty Python's Flying Circus", "John Cleese", "Dead Code & Deprecation"),
        new("Bring out your dead! ... I'm not dead yet! I feel happy!", "Monty Python and the Holy Grail", "Plague Villagers", "Legacy Refactoring"),
        new("Run away! Run away!", "Monty Python and the Holy Grail", "King Arthur", "Incident Response"),
        new("We are the Knights Who Say... NI!", "Monty Python and the Holy Grail", "Head Knight of Ni", "Acceptance Criteria"),

        // Ace Ventura: Pet Detective & When Nature Calls
        new("Alllllrighty then!", "Ace Ventura: Pet Detective", "Ace Ventura", "Acceptance & Success"),
        new("Like a glove!", "Ace Ventura: When Nature Calls", "Ace Ventura", "Integration & Deployment"),
        new("Laces out, Dan!", "Ace Ventura: Pet Detective", "Ace Ventura", "Root Cause Analysis"),
        new("If I'm not back in five minutes... just wait longer!", "Ace Ventura: Pet Detective", "Ace Ventura", "Async Timeouts"),

        // Star Wars
        new("It's a trap!", "Star Wars: Return of the Jedi", "Admiral Ackbar", "Security & Threat Models"),
        new("Never tell me the odds!", "Star Wars: The Empire Strikes Back", "Han Solo", "Estimates & Planning"),
        new("These aren't the droids you're looking for.", "Star Wars: A New Hope", "Obi-Wan Kenobi", "Permissions & Auth"),
        new("I've got a bad feeling about this.", "Star Wars", "Han Solo", "Production Deployments"),
        new("Do. Or do not. There is no try.", "Star Wars: The Empire Strikes Back", "Yoda", "Unit Testing"),

        // Airplane!
        new("Surely you can't be serious? I am serious, and don't call me Shirley.", "Airplane!", "Dr. Rumack", "Architecture & Logic"),
        new("Looks like I picked the wrong week to quit sniffing glue.", "Airplane!", "Steve McCroskey", "On-Call Incidents"),
        new("I just want to tell you both: good luck. We're all counting on you.", "Airplane!", "Dr. Rumack", "Production Releases"),
        new("The tower! The tower! Rapunzel! Rapunzel!", "Airplane!", "Johnny", "Monitoring & Alerts"),

        // The Naked Gun
        new("Nice beaver! Thanks, I just had it stuffed.", "The Naked Gun", "Lt. Frank Drebin & Jane", "Code Review"),
        new("Nothing to see here, please disperse! Nothing to see here!", "The Naked Gun", "Lt. Frank Drebin", "Production Meltdowns"),
        new("Like a blind man at an orgy, I was gonna have to feel my way through.", "The Naked Gun", "Lt. Frank Drebin", "Legacy Code Exploration"),

        // Kung Pow: Enter the Fist
        new("That's a lot of nuts!", "Kung Pow: Enter the Fist", "Master Tang's Crowd", "Big Data & Telemetry"),
        new("Killing is badong. From this moment, I will stand for the opposite of badong: Gnodab.", "Kung Pow: Enter the Fist", "The Chosen One", "Standards & Principles"),
        new("Chosen One! WEE-OOO-WEE-OOO-WEE!", "Kung Pow: Enter the Fist", "Master Doe", "Alerts & Alarms"),
        new("My nipples look like Milk Duds!", "Kung Pow: Enter the Fist", "Wimp Lo", "Extreme Edge Cases"),
        new("I am bleeding, making me the victor!", "Kung Pow: Enter the Fist", "Wimp Lo", "Failing Unit Tests"),

        // Blazing Saddles
        new("Badges? We don't need no stinkin' badges!", "Blazing Saddles", "Gold Hat", "CI/CD & Permissions"),
        new("Somebody's gotta go back and get a shitload of dimes!", "Blazing Saddles", "Taggart", "Cloud Costs & Budgets"),
        new("Excuse me while I whip this out.", "Blazing Saddles", "Sheriff Bart", "Deliverables & PRs"),

        // National Lampoon's Christmas Vacation
        new("Shitter was full!", "Christmas Vacation", "Cousin Eddie", "Memory Leaks & Buffer Overflow"),
        new("Hallelujah! Holy shit! Where's the Tylenol?", "Christmas Vacation", "Clark Griswold", "Merging Conflicts"),
        new("We're gonna have the hap-hap-happiest sprint since Bing Crosby tap-danced with Danny Kaye!", "Christmas Vacation", "Clark Griswold", "Sprint Planning"),

        // Caddyshack
        new("So I got that goin' for me, which is nice.", "Caddyshack", "Carl Spackler", "Partial Success"),
        new("Cinderella story. Outta nowhere. A former greenskeeper, now about to become the Masters champion.", "Caddyshack", "Carl Spackler", "Hero Debugging"),
        new("It's in the hole!", "Caddyshack", "Carl Spackler", "Production Merge"),

        // Dumb and Dumber
        new("So you're telling me there's a chance!", "Dumb and Dumber", "Lloyd Christmas", "Flaky Tests"),
        new("Our pets' heads are falling off!", "Dumb and Dumber", "Tommy / Lloyd", "System Meltdowns"),
        new("We got no food, we got no jobs, our pets' heads are falling off!", "Dumb and Dumber", "Lloyd Christmas", "Backlog Triage"),

        // Robin Hood: Men in Tights
        new("Unlike other Robin Hoods, I can speak with an English accent.", "Robin Hood: Men in Tights", "Robin Hood", "Differentiation"),
        new("Blinkin, what are you doing up there? Guessing. I guess no one's coming.", "Robin Hood: Men in Tights", "Robin & Blinkin", "Static Code Analysis"),
        new("He split Robin's arrow in twain!", "Robin Hood: Men in Tights", "Blinkin", "Microbenchmarking"),

        // Young Frankenstein
        new("What knockers! Why, thank you, doctor.", "Young Frankenstein", "Inga & Dr. Fronkensteen", "UI Design"),
        new("Abby Normal. I'm almost sure that was the name.", "Young Frankenstein", "Igor", "Edge Cases"),
        new("It's pronounced Fronkensteen!", "Young Frankenstein", "Dr. Frankenstein", "Domain Modeling"),
        new("Walk this way... no, this way!", "Young Frankenstein", "Igor", "Workflow Transitions"),

        // The Jerk
        new("The new phone book's here! The new phone book's here! I'm somebody now!", "The Jerk", "Navin R. Johnson", "Release Deployment"),
        new("He hates these cans! Stay away from the cans!", "The Jerk", "Navin R. Johnson", "Bug Hunting"),

        // Hot Shots! & Hot Shots: Part Deux
        new("I've got your father's eyes... GOGGLES!", "Hot Shots!", "Topper Harley", "Debugging Visuals"),
        new("I loved you in Wall Street!", "Hot Shots: Part Deux", "Topper & Tug", "Easter Eggs"),

        // Tommy Boy
        new("Fat guy in a little coat... fat guy in a little coat...", "Tommy Boy", "Tommy", "Memory Profiling"),
        new("Holy schnikes!", "Tommy Boy", "Tommy", "Live Incident Alerts"),
        new("Did you eat paint chips as a child? ... Why, you think they're good?", "Tommy Boy", "Richard & Tommy", "Requirements Clarification"),

        // The Waterboy
        new("Now that's what I call high quality H2O!", "The Waterboy", "Bobby Boucher", "Zero-Allocation Dogma"),
        new("Mama says alligators are ornery 'cause they got all them teeth and no toothbrush.", "The Waterboy", "Bobby Boucher", "Root Cause Analysis"),
        new("Remember when Bobby Boucher showed up at halftime and the Mud Dogs won the Bourbon Bowl?", "The Waterboy", "Fans", "Hero Patching"),

        // Old School
        new("You're my boy, Blue!", "Old School", "Frank the Tank", "Legacy Test Suites"),
        new("We're going streaking! We're going up the quad and into the gymnasium!", "Old School", "Frank the Tank", "Friday Deploys"),

        // Top Secret!
        new("I know a little German. He's sitting over there.", "Top Secret!", "Nick Rivers", "Localization & i18n"),
        new("Latrine! How is your daughter?", "Top Secret!", "General", "Naming Conventions"),

        // Super Troopers
        new("Meow what is so funny?", "Super Troopers", "Officer Foster", "Team Standups"),
        new("Enhance... enhance... enhance...", "Super Troopers", "Thorny & Rabbit", "Profiling Hot Paths"),
        new("Littering and... littering and... smokin' the reefer.", "Super Troopers", "Officer Mac", "Security Violations"),
        new("The snozzberries taste like snozzberries!", "Super Troopers", "College Boy", "Fuzz Testing"),

        // The Fifth Element
        new("Negative, I am a meat popsicle.", "The Fifth Element", "Korben Dallas", "Human Gate & Code Review"),
        new("Anybody else want to negotiate?", "The Fifth Element", "Korben Dallas", "Architecture & Governance"),
        new("Leeloo Dallas mul-ti-pass!", "The Fifth Element", "Leeloo", "Identity & Auth"),
        new("Super green!", "The Fifth Element", "Ruby Rhod", "CI/CD & Monitoring")
    ];

    private readonly Random _random = new();

    public IReadOnlyList<ComedyQuote> GetAllQuotes() => Quotes;

    public ComedyQuote GetRandomQuote() => Quotes[_random.Next(Quotes.Count)];

    public IReadOnlyList<ComedyQuote> GetQuotesByMovie(string movie) =>
        Quotes.Where(q => q.Movie.Contains(movie, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<ComedyQuote> GetQuotesByCategory(string category) =>
        Quotes.Where(q => q.Category.Contains(category, StringComparison.OrdinalIgnoreCase)).ToList();
}
