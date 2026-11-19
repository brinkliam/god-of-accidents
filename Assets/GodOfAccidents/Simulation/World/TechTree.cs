namespace GOA.Sim
{
    public class TechDef
    {
        public string id;
        public string title;
        public string description;
        public string spark;          // required mark on the inventor ("spark:engineering")
        public Occupation[] occupations;
        public float minCraft, minLore;
        public string[] requires;
        public BuildingType? unlocks;
        public double dailyChance;
    }

    /// <summary>Data-driven inventions. Each needs a specific kind of person, not just elapsed time.</summary>
    public static class TechTree
    {
        public static readonly TechDef[] All =
        {
            new TechDef { id = "waterwheel", title = "The Waterwheel", description = "A wheel the river turns. Grain is ground without aching backs.",
                spark = "spark:engineering", occupations = new[] { Occupation.Tinkerer }, minCraft = 42, minLore = 10,
                requires = new string[0], unlocks = BuildingType.Mill, dailyChance = 0.04 },
            new TechDef { id = "starcharts", title = "Star Charts", description = "The heavens mapped; seasons predicted to the day. Better sowing.",
                spark = "spark:optics", occupations = new[] { Occupation.Scholar }, minCraft = 0, minLore = 40,
                requires = new string[0], unlocks = null, dailyChance = 0.04 },
            new TechDef { id = "letters", title = "The Common Alphabet", description = "Everyone may learn to read. Knowledge stops dying with the old.",
                spark = "spark:letters", occupations = new[] { Occupation.Scholar }, minCraft = 0, minLore = 38,
                requires = new string[0], unlocks = BuildingType.Library, dailyChance = 0.035 },
            new TechDef { id = "clockwork", title = "Clockwork", description = "Time itself, measured in brass. The village keeps hours.",
                spark = "spark:engineering", occupations = new[] { Occupation.Tinkerer }, minCraft = 62, minLore = 25,
                requires = new[] { "waterwheel" }, unlocks = BuildingType.ClockTower, dailyChance = 0.025 },
            new TechDef { id = "windmill", title = "The Windmill", description = "If water can turn a wheel, so can the wind on the hill.",
                spark = "spark:engineering", occupations = new[] { Occupation.Tinkerer, Occupation.Builder }, minCraft = 50, minLore = 15,
                requires = new[] { "waterwheel" }, unlocks = BuildingType.Windmill, dailyChance = 0.02 },
            new TechDef { id = "printing", title = "The Printing Press", description = "Pages by the hundred. Ideas spread faster than gossip.",
                spark = null, occupations = new[] { Occupation.Scholar, Occupation.Tinkerer }, minCraft = 45, minLore = 60,
                requires = new[] { "letters" }, unlocks = null, dailyChance = 0.02 },
            new TechDef { id = "steam", title = "The Steam Engine", description = "Fire made to work. Nothing in the vale will ever be quiet again.",
                spark = "spark:engineering", occupations = new[] { Occupation.Tinkerer }, minCraft = 80, minLore = 55,
                requires = new[] { "clockwork", "printing" }, unlocks = null, dailyChance = 0.015 },
        };

        public static TechDef Get(string id)
        {
            foreach (var t in All) if (t.id == id) return t;
            return null;
        }
    }
}
