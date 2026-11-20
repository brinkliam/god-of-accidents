using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public class Glimmer
    {
        public Vec2 pos; public long until; public int citizenId; public int causeEventId;
    }

    public class BirdFlight
    {
        public Vec2 from, to; public long start, end; public ItemType item; public int causeEventId; public bool landed;
    }

    public enum Salt
    {
        Weather = 1, Wind, Cargo, Trip, Notice, Pick, Romance, Birth, Death, Occupation, Traits, Evening,
        Spot, SubMove, Ignite, Spread, Lightning, Invention, Pray, Merchant, Emigrate, Gamble, Rescue, Fight,
        NaturalSpark, Meet, LightningHour, Lunch, Festival, Build, Courtship, WindIgnite, Hit,
        Ambient, AmbientPick, AmbientFollow
    }

    /// <summary>
    /// The living world of Thistlewick Vale: a discrete-event simulation. Everything that happens is a scheduled
    /// task processed in strict (time, sequence) order, so a world is fully determined by its seed and the
    /// ordered list of player inputs.
    /// </summary>
    public sealed partial class World
    {
        public readonly ulong Seed;
        public readonly ScenarioDef Scenario;
        public long Time { get; private set; }
        public readonly CausalLedger Ledger = new CausalLedger();
        readonly EventQueue _queue = new EventQueue();
        public readonly NavGraph Nav = new NavGraph();

        public readonly List<Citizen> Citizens = new List<Citizen>();
        public readonly List<Building> Buildings = new List<Building>();
        public readonly List<Prop> Props = new List<Prop>();
        public readonly List<Fire> Fires = new List<Fire>();
        public readonly List<Glimmer> Glimmers = new List<Glimmer>();
        public readonly List<BirdFlight> Birds = new List<BirdFlight>();
        public readonly List<ConstructionProject> Projects = new List<ConstructionProject>();
        public readonly List<LotDef> FreeLots = new List<LotDef>();
        public readonly HashSet<string> Techs = new HashSet<string>();
        public readonly Dictionary<string, int> TechEvents = new Dictionary<string, int>();
        public readonly List<int> Chronicle = new List<int>();

        public float Food;
        public float Wood;
        public float FoodPrice = 1f;
        public Era Era = Era.Hamlet;
        public int EraEventId = -1;
        public Policy Policy = Policy.Tradition;
        public int PolicyEventId = -1;
        public int ElderId = -1;
        public Weather Weather;
        public Vec2 WindDir = new Vec2(1, 0);
        public float WindStrength = 0.2f;
        public WindGust Gust;
        public CartRoll Cart;
        public float Energy;
        public int InterventionCount;
        public int ScarcityEventId = -1;
        public int FamineEventId = -1;
        public int LastFoodLossEventId = -1;
        public int ProsperityEventId = -1;
        public bool WellBroken;
        public int WellBrokenEventId = -1;
        public int HighestPopulationMilestone;
        public float Prosperity = 0.5f;
        public readonly Dictionary<int, int> PropVersion = new Dictionary<int, int>();

        public event Action<SimEvent> OnEvent;

        public World(ulong seed, ScenarioDef scenario = null)
        {
            Seed = seed;
            Scenario = scenario ?? ScenarioDef.Sandbox;
            Ledger.OnRecorded += e =>
            {
                if (e.magnitude >= 2 || e.IsIntervention) Chronicle.Add(e.id);
                OnEvent?.Invoke(e);
            };
            Generate();
        }

        // ------------------------------------------------------------------ queries

        public int Population
        {
            get { int n = 0; foreach (var c in Citizens) if (c.IsActive && !c.offMapResident) n++; return n; }
        }

        public Citizen GetCitizen(int id) => id >= 0 && id < Citizens.Count ? Citizens[id] : null;
        public Building GetBuilding(int id) => id >= 0 && id < Buildings.Count ? Buildings[id] : null;
        public Prop GetProp(int id) => id >= 0 && id < Props.Count ? Props[id] : null;

        public Citizen FindCitizen(string fullName)
        {
            foreach (var c in Citizens) if (c.Name == fullName) return c;
            return null;
        }

        public Building FirstBuilding(BuildingType t, bool requireComplete = false)
        {
            foreach (var b in Buildings) if (b.type == t && (!requireComplete || b.Complete)) return b;
            return null;
        }

        public bool HasComplete(BuildingType t) => FirstBuilding(t, true) != null;

        public float SpeedOf(Citizen c)
        {
            int age = c.AgeYears(Time);
            float s = SimTime.WalkSpeedPerSecond;
            if (age < 14) s *= 1.15f;
            else if (age > 60) s *= 0.8f;
            return s;
        }

        public Vec2 PositionAt(Citizen c, double t)
        {
            var a = c.activity;
            if (a == null) return HomePos(c);
            if (a.kind == ActivityKind.Travel && a.path != null)
            {
                float d = (float)((t - a.start) * SpeedOf(c));
                if (d > a.pathLength) d = a.pathLength;
                return NavGraph.Sample(a.path, d);
            }
            return a.position;
        }

        public Vec2 PositionNow(Citizen c) => PositionAt(c, Time);

        public bool IsIndoors(Citizen c)
        {
            var a = c.activity;
            if (a == null || a.kind == ActivityKind.Travel) return false;
            if (a.kind == ActivityKind.OffMap) return true;
            if (a.nodeId < 0) return false;
            var n = Nav.Node(a.nodeId);
            if (n.kind == NodeKind.Door) return a.kind == ActivityKind.Sleep || a.kind == ActivityKind.Eat || a.kind == ActivityKind.Idle || a.kind == ActivityKind.Recover;
            return VillageLayout.IndoorNodes.Contains(n.name) && a.kind != ActivityKind.Distracted && a.kind != ActivityKind.FightFire;
        }

        public bool IsAwakeOutside(Citizen c) => c.IsActive && c.activity != null && c.activity.kind != ActivityKind.Sleep && !IsIndoors(c);

        Vec2 HomePos(Citizen c)
        {
            var h = GetBuilding(c.homeBuildingId);
            if (h != null && h.nodeId >= 0) return Nav.Node(h.nodeId).pos;
            return Nav.Node(Nav["EastGate"]).pos;
        }

        int HomeNode(Citizen c)
        {
            var h = GetBuilding(c.homeBuildingId);
            if (h != null && h.nodeId >= 0 && !h.destroyed) return h.nodeId;
            if (c.offMapResident) return Nav["EastGate"];
            return Nav["Tavern"]; // the homeless lodge at the tavern
        }

        // ------------------------------------------------------------------ main loop

        /// <summary>Process every scheduled task up to and including time t.</summary>
        public void AdvanceTo(long t)
        {
            if (t < Time) return;
            int guard = 0;
            while (_queue.Count > 0 && _queue.Peek().time <= t)
            {
                var task = _queue.Pop();
                Time = task.time;
                Process(task);
                if (++guard > 5_000_000) throw new InvalidOperationException("Simulation runaway");
            }
            Time = t;
        }

        public void Schedule(long time, TaskKind kind, int a = 0, int b = 0, int c = 0)
        {
            if (time < Time) time = Time;
            _queue.Push(time, kind, a, b, c);
        }

        void Process(in ScheduledTask task)
        {
            switch (task.kind)
            {
                case TaskKind.Decide:
                {
                    var c = Citizens[task.a];
                    if (c.activitySerial != task.b || !c.IsActive) return;
                    OnActivityEnd(c);
                    Decide(c);
                    break;
                }
                case TaskKind.Encounter: HandleEncounter(task.a, task.b, task.c); break;
                case TaskKind.DailyTick: DailyTick(); break;
                case TaskKind.FireUpdate: FireUpdate(task.a); break;
                case TaskKind.NoticeScan: NoticeScan(); break;
                case TaskKind.CartStep: CartStep(); break;
                case TaskKind.BirdArrive: BirdArrive(task.a); break;
                case TaskKind.LanternIgnite: LanternIgnite(task.a, task.b); break;
                case TaskKind.GustTick: GustTick(); break;
                case TaskKind.WindCheck: NaturalWindCheck(); break;
                case TaskKind.Lightning: LightningStrike(); break;
                case TaskKind.Ambient: AmbientAccident(task.a); break;
                case TaskKind.AmbientFollow: AmbientFollow(task.a, task.b, task.c); break;
            }
        }

        // ------------------------------------------------------------------ helpers for events

        public SimEvent Record(SimEventType type, string title, string detail, int magnitude, int[] causes = null, int[] actors = null, Vec2? pos = null, bool turning = false)
        {
            return Ledger.Record(Time, type, title, detail, magnitude, causes, actors, pos, turning);
        }

        /// <summary>Record a minor event only when it belongs to a causal chain (has a cause). Returns -1 if skipped.</summary>
        int RecordIfChained(SimEventType type, string title, string detail, int magnitude, int[] causes, int[] actors = null, Vec2? pos = null)
        {
            bool any = false;
            if (causes != null) foreach (var c in causes) if (c >= 0) { any = true; break; }
            if (!any) return -1;
            return Record(type, title, detail, magnitude, causes, actors, pos).id;
        }

        public void Remember(Citizen c, string text, int eventId)
        {
            c.memories.Add(new Memory { time = Time, text = text, eventId = eventId });
            if (c.memories.Count > 40) c.memories.RemoveAt(0);
        }

        public static int[] C(params int[] ids) => ids;

        Relation EnsureRelation(Citizen a, Citizen b)
        {
            if (!a.relations.TryGetValue(b.id, out var r))
            {
                r = new Relation { otherId = b.id, kind = RelationKind.Acquaintance };
                a.relations[b.id] = r;
            }
            return r;
        }

        void AdjustAffinity(Citizen a, Citizen b, float delta, int cause = -1)
        {
            var r1 = EnsureRelation(a, b); r1.affinity = Math.Max(-100, Math.Min(100, r1.affinity + delta));
            var r2 = EnsureRelation(b, a); r2.affinity = Math.Max(-100, Math.Min(100, r2.affinity + delta));
            if (r1.kind == RelationKind.Acquaintance && r1.affinity >= 40)
            {
                r1.kind = RelationKind.Friend; r2.kind = RelationKind.Friend;
                if (!AreFamily(a, b) && !a.offMapResident && !b.offMapResident)
                {
                    // a relationship milestone worth a line in the Chronicle
                    var e = Record(SimEventType.Friendship, $"{a.Name} and {b.Name} become friends",
                        $"{a.firstName} and {b.firstName} now look for each other on the green.", 2, cause >= 0 ? C(cause) : null, C(a.id, b.id), PositionNow(a));
                    r1.causeEventId = r2.causeEventId = e.id;
                    Remember(a, $"Became friends with {b.Name}", e.id);
                    Remember(b, $"Became friends with {a.Name}", e.id);
                }
            }
        }

        public bool AreFamily(Citizen a, Citizen b)
        {
            if (a.motherId == b.id || a.fatherId == b.id || b.motherId == a.id || b.fatherId == a.id) return true;
            if (a.motherId >= 0 && a.motherId == b.motherId) return true;
            if (a.fatherId >= 0 && a.fatherId == b.fatherId) return true;
            if (a.familyName == b.familyName && a.homeBuildingId == b.homeBuildingId) return true;
            return false;
        }

        public IEnumerable<Citizen> ActiveCitizens()
        {
            foreach (var c in Citizens) if (c.IsActive) yield return c;
        }

        /// <summary>Stable hash of the whole world state, used by determinism tests.</summary>
        public ulong StateHash()
        {
            ulong h = Rand.Mix(Seed);
            void Add(long v) { h = Rand.Mix(h ^ (ulong)v); }
            void AddF(float f) { Add((long)Math.Round(f * 1000)); }
            Add(Time);
            Add(Ledger.Count);
            foreach (var c in Citizens)
            {
                Add(c.id); Add(c.alive ? 1 : 0); Add(c.emigrated ? 1 : 0); Add((int)c.occupation); Add(c.wealth); Add(c.fame);
                AddF(c.craft); AddF(c.lore); Add(c.spouseId); Add(c.relations.Count); Add(c.marks.Count);
                var p = PositionNow(c); AddF(p.x); AddF(p.z);
                if (c.activity != null) { Add((int)c.activity.kind); Add(c.activity.end); }
            }
            foreach (var b in Buildings) { Add(b.id); Add(b.destroyed ? 1 : 0); AddF(b.buildProgress); Add(b.tier); }
            foreach (var p in Props) { Add(p.id); Add(p.active ? 1 : 0); AddF(p.pos.x); AddF(p.pos.z); Add(p.state); }
            AddF(Food); AddF(Wood); Add((int)Era); Add((int)Policy); Add(ElderId); AddF(Energy);
            foreach (var e in Ledger.Events) { Add((int)e.type); Add(e.time); Add(e.causes.Length); }
            return h;
        }
    }
}
