using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public class ScenarioDef
    {
        public string id = "sandbox";
        public string title = "Thistlewick Vale";
        public string subtitle = "A quiet valley. A thousand tiny accidents waiting to happen.";
        public string briefing = "";
        public ulong fixedSeed;            // 0 = player-chosen seed
        public float startEnergy = 6;
        public float maxEnergy = 8;
        public float energyRegenPerDay = 0.5f;
        public int maxInterventions;       // 0 = unlimited
        public double naturalSparkRate = 0.0006;
        public bool lensFirstInNest;
        public int deadlineYear;           // challenge fails when this year begins (0 = none)
        public bool isChallenge;
        public bool isBonus;

        public static readonly ScenarioDef Sandbox = new ScenarioDef();

        public static readonly ScenarioDef ForgottenGenius = new ScenarioDef
        {
            id = "forgotten_genius",
            title = "The Forgotten Genius",
            subtitle = "Wren Ashdown asks how everything works. Nobody ever answers.",
            briefing = "Wren is a curious orphan of nine. Left alone, she will grow up to plough her aunt's fields and never build a thing. " +
                       "Without touching her, give Wren the chance to invent something that changes the vale - within 20 years, using at most 3 interventions.",
            fixedSeed = 1207,
            startEnergy = 6, maxEnergy = 6, energyRegenPerDay = 0.25f,
            maxInterventions = 3,
            naturalSparkRate = 0.0006,
            deadlineYear = 21,
            isChallenge = true,
        };

        public static readonly ScenarioDef OnePebble = new ScenarioDef
        {
            id = "one_pebble",
            title = "One Pebble",
            subtitle = "One action. Change the fate of the vale.",
            briefing = "You have a single intervention. Make it count: cause a world-changing turning point (an invention, a new Elder, or a new era) " +
                       "that traces back to your one act, within 15 years.",
            fixedSeed = 4242,
            startEnergy = 2, maxEnergy = 2, energyRegenPerDay = 0,
            maxInterventions = 1,
            deadlineYear = 16,
            isChallenge = true,
        };

        public static readonly ScenarioDef BonusFestival = new ScenarioDef
        {
            id = "bonus_festival",
            title = "Bonus: The Restless Year",
            subtitle = "Divine energy overflows. Meddle freely.",
            briefing = "A bonus world with abundant energy. Nothing to win - just see how far one village can be pushed.",
            fixedSeed = 777,
            startEnergy = 12, maxEnergy = 12, energyRegenPerDay = 2,
            isBonus = true,
        };

        public static readonly ScenarioDef[] All = { Sandbox, ForgottenGenius, OnePebble, BonusFestival };

        public static ScenarioDef Get(string id)
        {
            foreach (var s in All) if (s.id == id) return s;
            return Sandbox;
        }
    }

    public enum ChallengeState { None, InProgress, Won, Failed }

    public class ChallengeStatus
    {
        public ChallengeState state = ChallengeState.None;
        public string headline = "";
        public string detail = "";
        public int stars;
        public int winningEventId = -1;
        public readonly List<(string text, bool done)> steps = new List<(string, bool)>();
    }

    public static class Challenges
    {
        public static ChallengeStatus Evaluate(World w)
        {
            var s = new ChallengeStatus();
            switch (w.Scenario.id)
            {
                case "forgotten_genius": ForgottenGenius(w, s); break;
                case "one_pebble": OnePebble(w, s); break;
            }
            return s;
        }

        static int Stars(World w)
        {
            int n = w.InterventionCount;
            return n <= 1 ? 3 : n == 2 ? 2 : 1;
        }

        static void ForgottenGenius(World w, ChallengeStatus s)
        {
            var wren = w.FindCitizen("Wren Ashdown");
            s.state = ChallengeState.InProgress;
            if (wren == null) { s.state = ChallengeState.Failed; s.headline = "Wren is missing from this world."; return; }
            bool spark = wren.marks.ContainsKey("spark:engineering") || wren.marks.ContainsKey("spark:optics") || wren.marks.ContainsKey("spark:letters");
            bool mentor = false;
            foreach (var r in wren.relations.Values) if (r.kind == RelationKind.Mentor) mentor = true;
            bool trade = wren.occupation == Occupation.Tinkerer || wren.occupation == Occupation.Scholar;
            s.steps.Add(("Wren feels a spark of wonder", spark));
            s.steps.Add(("Someone takes her seriously (an apprenticeship, a calling)", mentor || trade));
            s.steps.Add(($"Wren masters her craft (craft {wren.craft:0}, lore {wren.lore:0})", wren.craft >= 42 || wren.lore >= 38));
            SimEvent win = null;
            foreach (var e in w.Ledger.Events)
                if (e.type == SimEventType.Invention && Array.IndexOf(e.actors, wren.id) >= 0) { win = e; break; }
            s.steps.Add(("Wren invents something that changes the vale", win != null));
            if (win != null)
            {
                s.state = ChallengeState.Won;
                s.stars = Stars(w);
                s.winningEventId = win.id;
                s.headline = $"{win.title}!";
                s.detail = $"Using {w.InterventionCount} intervention{(w.InterventionCount == 1 ? "" : "s")}, you gave the forgotten genius her chance.";
                return;
            }
            if (!wren.IsActive) { s.state = ChallengeState.Failed; s.headline = wren.alive ? "Wren has left the vale." : "Wren's story ended too soon."; return; }
            if (SimTime.Year1(w.Time) >= w.Scenario.deadlineYear) { s.state = ChallengeState.Failed; s.headline = "Twenty years pass. Wren never builds a thing."; return; }
            s.headline = $"Year {SimTime.Year1(w.Time)} of 20 - interventions used {w.InterventionCount}/{w.Scenario.maxInterventions}";
        }

        static void OnePebble(World w, ChallengeStatus s)
        {
            s.state = ChallengeState.InProgress;
            SimEvent win = null;
            foreach (var e in w.Ledger.Events)
                if (e.magnitude >= 4 && e.roots.Length > 0 && (e.type == SimEventType.Invention || e.type == SimEventType.Election || e.type == SimEventType.EraChange)) { win = e; break; }
            s.steps.Add(("Make your one intervention", w.InterventionCount > 0));
            s.steps.Add(("A world-changing turning point traces back to it", win != null));
            if (win != null)
            {
                s.state = ChallengeState.Won; s.stars = 3; s.winningEventId = win.id;
                s.headline = win.title;
                s.detail = "One act. One altered history.";
                return;
            }
            if (SimTime.Year1(w.Time) >= w.Scenario.deadlineYear) { s.state = ChallengeState.Failed; s.headline = "Fifteen years pass. History shrugs."; return; }
            s.headline = $"Year {SimTime.Year1(w.Time)} of 15";
        }
    }
}
