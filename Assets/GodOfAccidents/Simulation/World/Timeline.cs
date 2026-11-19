using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    [Serializable]
    public class Bookmark
    {
        public long time;
        public int eventId;
        public string title;
    }

    [Serializable]
    public class TimelineSave
    {
        public int version = 1;
        public string seed;            // ulong as string (JsonUtility-safe)
        public string scenario;
        public long time;
        public string name;
        public string stateHash;
        public List<PlayerInput> inputs = new List<PlayerInput>();
        public List<Bookmark> bookmarks = new List<Bookmark>();
    }

    [Serializable]
    public class SaveFile
    {
        public int version = 1;
        public int active;
        public List<TimelineSave> timelines = new List<TimelineSave>();
        public string savedAtUtc;
    }

    /// <summary>
    /// One history. Its world is reconstructed purely from (seed, scenario, inputs), which makes saving,
    /// replay, branching and counterfactual comparison exact rather than approximate.
    /// </summary>
    public sealed class Timeline
    {
        public readonly ulong Seed;
        public readonly ScenarioDef Scenario;
        public readonly List<PlayerInput> Inputs = new List<PlayerInput>();
        public readonly List<Bookmark> Bookmarks = new List<Bookmark>();
        public World World { get; private set; }
        public string Name = "Prime Timeline";

        public Timeline(ulong seed, ScenarioDef scenario)
        {
            Seed = seed;
            Scenario = scenario;
            World = new World(seed, scenario);
        }

        Timeline(ulong seed, ScenarioDef scenario, World w, IEnumerable<PlayerInput> inputs)
        {
            Seed = seed; Scenario = scenario; World = w;
            foreach (var i in inputs) Inputs.Add(i.Clone());
        }

        public PowerResult Act(PlayerInput input)
        {
            var i = input.Clone();
            i.time = World.Time;
            var r = World.Apply(i);
            if (r.ok) Inputs.Add(i);
            return r;
        }

        public void AdvanceTo(long t) => World.AdvanceTo(t);

        public static World Replay(ulong seed, ScenarioDef scenario, IList<PlayerInput> inputs, long until)
        {
            var w = new World(seed, scenario);
            foreach (var i in inputs)
            {
                if (i.time > until) break;
                w.AdvanceTo(i.time);
                w.Apply(i);
            }
            w.AdvanceTo(until);
            return w;
        }

        /// <summary>Rebuild an identical copy of this world at its current time.</summary>
        public World CloneWorld() => Replay(Seed, Scenario, Inputs, World.Time);

        /// <summary>A new timeline that shares this history up to (but not including inputs at) the given time.</summary>
        public Timeline Branch(long at, string name)
        {
            var kept = new List<PlayerInput>();
            foreach (var i in Inputs) if (i.time < at) kept.Add(i);
            var w = Replay(Seed, Scenario, kept, at);
            var t = new Timeline(Seed, Scenario, w, kept) { Name = name };
            foreach (var b in Bookmarks) if (b.time < at) t.Bookmarks.Add(b);
            return t;
        }

        /// <summary>Undo the most recent power (used by the "Second Chance" reward).</summary>
        public Timeline RewindLastIntervention()
        {
            for (int k = Inputs.Count - 1; k >= 0; k--)
                if (Inputs[k].kind == InputKind.Power) return Branch(Inputs[k].time, Name);
            return Branch(World.StartTime, Name);
        }

        public void AddBookmark(SimEvent e)
        {
            foreach (var b in Bookmarks) if (b.eventId == e.id) return;
            Bookmarks.Add(new Bookmark { time = e.time, eventId = e.id, title = e.title });
        }

        public TimelineSave ToSave()
        {
            var s = new TimelineSave
            {
                seed = Seed.ToString(), scenario = Scenario.id, time = World.Time, name = Name,
                stateHash = World.StateHash().ToString()
            };
            foreach (var i in Inputs) s.inputs.Add(i.Clone());
            s.bookmarks.AddRange(Bookmarks);
            return s;
        }

        public static Timeline FromSave(TimelineSave s)
        {
            ulong seed = ulong.Parse(s.seed);
            var sc = ScenarioDef.Get(s.scenario);
            var w = Replay(seed, sc, s.inputs, s.time);
            var t = new Timeline(seed, sc, w, s.inputs) { Name = s.name ?? "Restored Timeline" };
            if (s.bookmarks != null) t.Bookmarks.AddRange(s.bookmarks);
            return t;
        }

        public bool VerifyHash(TimelineSave s) => s.stateHash == World.StateHash().ToString();
    }

    public class TimelineComparison
    {
        public int popA, popB;
        public Era eraA, eraB;
        public int techsA, techsB;
        public string elderA = "", elderB = "";
        public float prosperityA, prosperityB;
        public readonly List<SimEvent> onlyInA = new List<SimEvent>();
        public readonly List<SimEvent> onlyInB = new List<SimEvent>();
        public int firstDivergenceEvent = -1;
    }

    public static class Counterfactual
    {
        static string Sig(SimEvent e) => ((int)e.type) + "|" + e.title + "|" + SimTime.DayIndex(e.time);

        /// <summary>Compare two worlds at the same moment. Because randomness is context-keyed, events that were not
        /// influenced by the difference between them line up exactly; whatever is left over genuinely diverged.</summary>
        public static TimelineComparison Compare(World a, World b, int minMagnitude = 2)
        {
            var r = new TimelineComparison
            {
                popA = a.Population, popB = b.Population, eraA = a.Era, eraB = b.Era, techsA = a.Techs.Count, techsB = b.Techs.Count,
                prosperityA = a.Prosperity, prosperityB = b.Prosperity,
                elderA = a.GetCitizen(a.ElderId)?.Name ?? "-", elderB = b.GetCitizen(b.ElderId)?.Name ?? "-",
            };
            var sa = new HashSet<string>(); var sb = new HashSet<string>();
            foreach (var e in a.Ledger.Events) if (e.type != SimEventType.Intervention) sa.Add(Sig(e));
            foreach (var e in b.Ledger.Events) if (e.type != SimEventType.Intervention) sb.Add(Sig(e));
            foreach (var e in a.Ledger.Events)
                if (e.type != SimEventType.Intervention && e.magnitude >= minMagnitude && !sb.Contains(Sig(e))) r.onlyInA.Add(e);
            foreach (var e in b.Ledger.Events)
                if (e.type != SimEventType.Intervention && e.magnitude >= minMagnitude && !sa.Contains(Sig(e))) r.onlyInB.Add(e);
            return r;
        }

        public class Glimpse
        {
            public long horizon;
            public readonly List<SimEvent> newEvents = new List<SimEvent>();
            public readonly List<SimEvent> preventedEvents = new List<SimEvent>();
            public bool valid;
            public string error = "";
        }

        /// <summary>Look ahead: what happens over the horizon if this input is made now, versus if it isn't?</summary>
        public static Glimpse Foresee(Timeline t, PlayerInput hypothetical, long horizon)
        {
            var g = new Glimpse { horizon = horizon };
            var with = t.CloneWorld();
            var i = hypothetical.Clone();
            i.time = with.Time;
            var res = with.Apply(i);
            if (!res.ok) { g.error = res.message; return g; }
            var without = t.CloneWorld();
            long until = with.Time + horizon;
            with.AdvanceTo(until);
            without.AdvanceTo(until);
            var cmp = Compare(with, without, 1);
            g.newEvents.AddRange(cmp.onlyInA);
            g.preventedEvents.AddRange(cmp.onlyInB);
            g.valid = true;
            return g;
        }
    }
}
