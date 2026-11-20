using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public class Directive
    {
        public ActivityKind kind; public int nodeId; public Vec2 pos; public long until; public int causeEventId; public int tag;
    }

    public sealed partial class World
    {
        struct Plan
        {
            public ActivityKind kind; public int node; public long until; public bool hasSpot; public Vec2 spot; public int cause;
            public Plan(ActivityKind k, int n, long u) { kind = k; node = n; until = u; hasSpot = false; spot = default; cause = -1; }
        }

        readonly Dictionary<int, Directive> _directives = new Dictionary<int, Directive>();

        public Directive DirectiveOf(Citizen c) => _directives.TryGetValue(c.id, out var d) && d.until > Time ? d : null;

        static bool IsAreaActivity(ActivityKind k) =>
            k == ActivityKind.Work || k == ActivityKind.Play || k == ActivityKind.Festival || k == ActivityKind.Socialize ||
            k == ActivityKind.Trade || k == ActivityKind.Build || k == ActivityKind.Fetch || k == ActivityKind.Idle;

        // ------------------------------------------------------------------ daily routine

        static void WorkHours(Occupation o, Season s, out double start, out double end)
        {
            switch (o)
            {
                case Occupation.Farmer: start = s == Season.Winter ? 9 : 7; end = s == Season.Winter ? 15 : 17; break;
                case Occupation.Smith: start = 8; end = 17; break;
                case Occupation.Shopkeeper: start = 8; end = 15; break;
                case Occupation.Innkeeper: start = 10; end = 21.5; break;
                case Occupation.Priest: start = 6.5; end = 12; break;
                case Occupation.Woodcutter: start = 7; end = 16; break;
                case Occupation.Shepherd: start = 7; end = 18; break;
                case Occupation.Fisher: start = 6; end = 14; break;
                case Occupation.Tinkerer: start = 8; end = 18; break;
                case Occupation.Scholar: start = 8; end = 17; break;
                case Occupation.Builder: start = 8; end = 17; break;
                case Occupation.Elder: start = 9; end = 15; break;
                case Occupation.Miller: start = 7; end = 17; break;
                default: start = 9; end = 16; break;
            }
        }

        Plan PlanFor(Citizen c)
        {
            long now = Time;
            long day0 = SimTime.StartOfDay(now);
            long dayIdx = SimTime.DayIndex(now);
            double h = SimTime.HourOfDay(now);
            long At(double hr) => day0 + (long)(hr * SimTime.Hour);
            int home = HomeNode(c);
            int age = c.AgeYears(now);
            bool holy = SimTime.IsHolyDay(now);
            var season = SimTime.SeasonOf(now);

            var dir = DirectiveOf(c);
            if (dir != null)
            {
                var p = new Plan(dir.kind, dir.nodeId, dir.until) { hasSpot = true, spot = dir.pos, cause = dir.causeEventId };
                return p;
            }

            if (c.offMapResident)
            {
                bool comes = !holy || Policy == Policy.Trade;
                if (comes && h >= 5.5 && h < 15) return new Plan(ActivityKind.Trade, Nav["Market"], At(15));
                long next = h < 5.5 ? At(5.5) : At(5.5) + SimTime.Day;
                return new Plan(ActivityKind.OffMap, Nav["EastGate"], next);
            }

            double bed = (age < 14 || age > 60) ? 21 : 22;
            if (h >= bed || h < 6) return new Plan(ActivityKind.Sleep, home, h < 6 ? At(6) : At(6) + SimTime.Day);
            if (c.injuredUntilDay >= dayIdx) return new Plan(ActivityKind.Recover, home, At(bed));
            if (c.occupation == Occupation.Toddler) return new Plan(ActivityKind.Idle, home, At(bed));
            if (h < 7) return new Plan(ActivityKind.Eat, home, At(7));

            // a broken well sends one adult per household down to the river for water
            if (WellBroken && age >= 14 && h < 7.75 && IsWaterCarrier(c))
                return new Plan(ActivityKind.Fetch, Nav["Riverbank"], At(7.75));

            if (holy)
            {
                if (h < 9) return new Plan(ActivityKind.Idle, home, At(9));
                if (h < 11)
                {
                    bool pray = c.Has(Trait.Pious) || Policy == Policy.Faith || Rand.Chance(0.5, Seed, c.id, dayIdx, 0, (int)Salt.Pray);
                    return pray ? new Plan(ActivityKind.Pray, Nav["Chapel"], At(11)) : new Plan(ActivityKind.Socialize, Nav["Well"], At(11));
                }
                if (h < 13) return new Plan(ActivityKind.Eat, home, At(13));
                if (h < 18) return new Plan(ActivityKind.Festival, Nav["Green"], At(18));
            }
            else if (age < 14)
            {
                bool apprentice = IsApprenticed(c, out int mentorNode);
                if (h < 9) return new Plan(ActivityKind.Idle, home, At(9));
                if (h < 12) return new Plan(ActivityKind.Play, Nav["Green"], At(12));
                if (h < 13) return new Plan(ActivityKind.Eat, home, At(13));
                if (h < 17) return apprentice ? new Plan(ActivityKind.Study, mentorNode, At(17)) : new Plan(ActivityKind.Play, Nav["Green"], At(17));
                return new Plan(ActivityKind.Idle, home, At(bed));
            }
            else
            {
                WorkHours(c.occupation, season, out double ws, out double we);
                bool fieldWorker = c.occupation == Occupation.Farmer || c.occupation == Occupation.Shepherd || c.occupation == Occupation.Fisher || c.occupation == Occupation.Woodcutter;
                bool noLunch = c.occupation == Occupation.Innkeeper || c.occupation == Occupation.Fisher;
                if (h >= ws && h < we)
                {
                    if (!noLunch && h >= 12 && h < 13)
                    {
                        if (fieldWorker) return new Plan(ActivityKind.Eat, WorkNodeFor(c, season), At(13));
                        bool tavern = c.wealth > 3 && (c.Has(Trait.Charismatic) || Rand.Chance(0.3, Seed, c.id, dayIdx, 0, (int)Salt.Lunch));
                        return new Plan(ActivityKind.Eat, tavern ? Nav["Tavern"] : home, At(13));
                    }
                    long blockEnd = (!noLunch && h < 12) ? At(Math.Min(12, we)) : At(we);
                    if (c.occupation == Occupation.Tinkerer && h < 15 && blockEnd > At(15)) blockEnd = At(15);
                    if (c.occupation == Occupation.Tinkerer && h >= 15 && h < 15.75)
                        return new Plan(ActivityKind.Idle, Nav["MillSite"], At(15.75)); // watching the river turn
                    if (c.occupation == Occupation.Builder || (c.occupation == Occupation.Elder && Projects.Count > 0 && h >= 13))
                    {
                        var site = ActiveProjectSite();
                        if (site != null) return new Plan(ActivityKind.Build, site.nodeId, blockEnd) { hasSpot = true, spot = SiteSpot(c, site) };
                        return new Plan(ActivityKind.Work, Nav["FieldE"], blockEnd);
                    }
                    if (c.occupation == Occupation.Priest && h >= 12) return new Plan(ActivityKind.Idle, home, At(we));
                    return new Plan(ActivityKind.Work, WorkNodeFor(c, season), blockEnd);
                }
                if (h < ws) return new Plan(ActivityKind.Idle, home, At(ws));
            }

            // evenings
            if (c.occupation == Occupation.Priest && h >= 16 && h < 20) return new Plan(ActivityKind.Pray, Nav["Chapel"], At(20));
            if (c.Has(Trait.Pious) && h < 19 && Rand.Chance(0.3, Seed, c.id, dayIdx, 1, (int)Salt.Pray))
                return new Plan(ActivityKind.Pray, Nav["Chapel"], At(19));
            double tavP = c.Has(Trait.Charismatic) ? 0.6 : c.Has(Trait.Timid) ? 0.1 : 0.3;
            if (c.occupation == Occupation.Innkeeper) tavP = 0;
            if (h < 21.5 && Rand.Chance(tavP, Seed, c.id, dayIdx, 0, (int)Salt.Evening))
                return new Plan(ActivityKind.Socialize, Nav["Tavern"], At(Math.Min(bed, 21.5)));
            if (h < 19 && Rand.Chance(0.25, Seed, c.id, dayIdx, 1, (int)Salt.Evening))
                return new Plan(ActivityKind.Socialize, Nav["Well"], At(19));
            return new Plan(ActivityKind.Idle, home, At(bed));
        }

        int WorkNodeFor(Citizen c, Season s)
        {
            if (c.occupation == Occupation.Farmer && s == Season.Winter) return Nav["Barn"];
            if (c.occupation == Occupation.Scholar) return HasComplete(BuildingType.Library) ? Nav["Library"] : Nav["Chapel"];
            if (c.occupation == Occupation.Miller) return HasComplete(BuildingType.Mill) ? Nav["MillSite"] : Nav["FieldE"];
            return c.workNodeId >= 0 ? c.workNodeId : Nav["Green"];
        }

        bool IsWaterCarrier(Citizen c)
        {
            if (c.homeBuildingId < 0) return false;
            foreach (var o in Citizens)
            {
                if (!o.IsActive || o.homeBuildingId != c.homeBuildingId || o.AgeYears(Time) < 14) continue;
                return o.id == c.id; // the eldest-registered adult of the household goes
            }
            return false;
        }

        bool IsApprenticed(Citizen c, out int node)
        {
            node = -1;
            foreach (var kv in c.relations)
            {
                if (kv.Value.kind != RelationKind.Mentor) continue;
                var m = GetCitizen(kv.Key);
                if (m != null && m.IsActive) { node = WorkNodeFor(m, SimTime.SeasonOf(Time)); return true; }
            }
            return false;
        }

        Building ActiveProjectSite()
        {
            foreach (var p in Projects)
            {
                var b = GetBuilding(p.buildingId);
                if (b != null && b.buildProgress < 1f) return b;
            }
            return null;
        }

        Vec2 SiteSpot(Citizen c, Building b)
        {
            float ang = Rand.RangeF(0, 6.283f, Seed, c.id, b.id, 0, (int)Salt.Build);
            float r = Math.Max(b.size.x, b.size.z) * 0.5f + 1.2f;
            return b.pos + Vec2.FromAngle(ang) * r;
        }

        // ------------------------------------------------------------------ decisions and movement

        Vec2 SpotIn(Citizen c, NavNode node)
        {
            if (node.areaRadius <= 2.5f) return node.pos;
            long bucket = Time / (10 * SimTime.Minute);
            float ang = Rand.RangeF(0, 6.283f, Seed, c.id, bucket, node.id, (int)Salt.Spot);
            float rr = node.areaRadius * (float)Math.Sqrt(Rand.U01(Seed, c.id, bucket, node.id, (int)Salt.Spot + 100));
            var p = node.pos + Vec2.FromAngle(ang) * rr;
            return p;
        }

        void Decide(Citizen c)
        {
            if (!c.IsActive) return;
            var plan = PlanFor(c);
            if (plan.until <= Time) plan.until = Time + 10 * SimTime.Minute;
            var node = Nav.Node(plan.node);
            Vec2 here = PositionNow(c);
            Vec2 spot = plan.hasSpot ? plan.spot : SpotIn(c, node);
            float reach = plan.hasSpot ? 1.0f : node.areaRadius + 0.6f;
            float dist = plan.hasSpot ? Vec2.Distance(here, plan.spot) : Vec2.Distance(here, node.pos);

            if (plan.kind == ActivityKind.OffMap && Vec2.Distance(here, node.pos) <= 1.5f)
            {
                StartActivity(c, ActivityKind.OffMap, plan.node, node.pos, plan.until, plan.cause);
                return;
            }

            if (dist > reach)
            {
                StartTravel(c, here, plan.node, spot, plan.cause);
                return;
            }

            if (IsAreaActivity(plan.kind) && node.areaRadius > 2.5f && !plan.hasSpot)
            {
                long seg = Rand.Range(18, 50, Seed, c.id, Time, 0, (int)Salt.SubMove) * SimTime.Minute;
                long segEnd = Math.Min(plan.until, Time + seg);
                if (Vec2.Distance(here, spot) > 1.2f && Rand.Chance(0.5, Seed, c.id, Time, 1, (int)Salt.SubMove))
                {
                    StartTravelDirect(c, here, spot, plan.node, plan.cause);
                    return;
                }
                StartActivity(c, plan.kind, plan.node, here, segEnd, plan.cause);
                return;
            }

            StartActivity(c, plan.kind, plan.node, plan.hasSpot ? plan.spot : (node.areaRadius > 2.5f ? here : node.pos), plan.until, plan.cause);
        }

        void StartActivity(Citizen c, ActivityKind kind, int nodeId, Vec2 pos, long until, int cause = -1)
        {
            c.activitySerial++;
            c.activity = new Activity
            {
                kind = kind, start = Time, end = Math.Max(until, Time + 1), nodeId = nodeId, position = pos,
                serial = c.activitySerial, causeEventId = cause
            };
            Schedule(c.activity.end, TaskKind.Decide, c.id, c.activitySerial);
        }

        int BestStartNode(Vec2 from, int target, Citizen c)
        {
            int home = HomeNode(c);
            int best = -1; float bestCost = float.MaxValue;
            foreach (var n in Nav.nodes)
            {
                if (n.kind == NodeKind.Door && n.id != home && n.id != target) continue;
                if (n.kind == NodeKind.Edge && Vec2.Distance(n.pos, from) > 3f && n.id != target) continue;
                float d0 = Vec2.Distance(from, n.pos);
                if (d0 > 60f) continue;
                float cost = d0 + Nav.Distance(n.id, target);
                if (cost < bestCost) { bestCost = cost; best = n.id; }
            }
            return best >= 0 ? best : Nav.Nearest(from);
        }

        void StartTravel(Citizen c, Vec2 from, int targetNode, Vec2 spot, int cause)
        {
            int start = BestStartNode(from, targetNode, c);
            var path = Nav.Route(from, start, targetNode, spot);
            BeginPath(c, path, targetNode, cause);
        }

        void StartTravelDirect(Citizen c, Vec2 from, Vec2 to, int nodeId, int cause)
        {
            BeginPath(c, new List<Vec2> { from, to }, nodeId, cause);
        }

        void BeginPath(Citizen c, List<Vec2> path, int nodeId, int cause)
        {
            float len = NavGraph.Length(path);
            float speed = SpeedOf(c);
            long dur = Math.Max(1, (long)Math.Ceiling(len / speed));
            c.activitySerial++;
            c.activity = new Activity
            {
                kind = ActivityKind.Travel, start = Time, end = Time + dur, nodeId = nodeId, path = path, pathLength = len,
                position = path[path.Count - 1], serial = c.activitySerial, causeEventId = cause
            };
            Schedule(c.activity.end, TaskKind.Decide, c.id, c.activitySerial);
            ScheduleEncounters(c, 0);
        }

        /// <summary>Stop whatever they're doing, right where they stand.</summary>
        public void Interrupt(Citizen c, ActivityKind kind, long duration, int cause)
        {
            var pos = PositionNow(c);
            int node = c.activity != null ? c.activity.nodeId : -1;
            StartActivity(c, kind, node, pos, Time + duration, cause);
        }

        void SetDirective(Citizen c, ActivityKind kind, int nodeId, Vec2 pos, long until, int cause, int tag)
        {
            _directives[c.id] = new Directive { kind = kind, nodeId = nodeId, pos = pos, until = until, causeEventId = cause, tag = tag };
            c.activitySerial++; // invalidate current plan
            Decide(c);
        }

        void ClearDirective(Citizen c)
        {
            if (_directives.Remove(c.id) && c.IsActive) { c.activitySerial++; Decide(c); }
        }

        // ------------------------------------------------------------------ end-of-activity effects

        void OnActivityEnd(Citizen c)
        {
            var a = c.activity;
            if (a == null) return;
            double hours = (Time - a.start) / 3600.0;
            switch (a.kind)
            {
                case ActivityKind.Build:
                    AddBuildProgress(c, (float)hours);
                    break;
                case ActivityKind.Work:
                    if (c.occupation == Occupation.Woodcutter) Wood += (float)hours * 1.6f;
                    break;
                case ActivityKind.Socialize:
                case ActivityKind.Festival:
                case ActivityKind.Play:
                    Socialize(c, a.nodeId, (float)hours);
                    break;
                case ActivityKind.Distracted:
                    c.mood = Math.Min(1f, c.mood + 0.05f);
                    break;
                case ActivityKind.Trade:
                    if (c.offMapResident && SimTime.HourOfDay(Time) >= 14.9) c.cargoItem = ItemType.None; // sold out
                    break;
            }
        }

        void Socialize(Citizen c, int nodeId, float hours)
        {
            if (nodeId < 0 || hours <= 0) return;
            int age = c.AgeYears(Time);
            foreach (var o in Citizens)
            {
                if (o.id == c.id || !o.IsActive || o.activity == null) continue;
                var k = o.activity.kind;
                if (o.activity.nodeId != nodeId) continue;
                if (k != ActivityKind.Socialize && k != ActivityKind.Festival && k != ActivityKind.Play) continue;
                float delta = 1.5f * hours;
                if (c.Has(Trait.Charismatic) || o.Has(Trait.Charismatic)) delta *= 1.6f;
                if ((c.Has(Trait.Greedy) && o.Has(Trait.Greedy)) || (c.Has(Trait.Reckless) && o.Has(Trait.Timid))) delta = -delta;
                AdjustAffinity(c, o, delta);

                // natural courtship between compatible adults
                if (CanCourt(c, o))
                {
                    var rel = c.Rel(o.id);
                    if (rel != null && rel.affinity >= 25 && rel.kind != RelationKind.Courting)
                    {
                        double p = 0.012 * hours * (c.Has(Trait.Romantic) ? 2 : 1) * (o.Has(Trait.Romantic) ? 2 : 1);
                        if (Rand.Chance(p, Seed, Math.Min(c.id, o.id) * 10000L + Math.Max(c.id, o.id), Time, 0, (int)Salt.Courtship))
                            BeginCourtship(c, o, -1);
                    }
                }
            }
            c.mood = Math.Min(1f, c.mood + 0.02f * hours);
        }

        public bool CanCourt(Citizen a, Citizen b)
        {
            if (!a.IsActive || !b.IsActive || a.offMapResident || b.offMapResident) return false;
            if (a.spouseId >= 0 || b.spouseId >= 0) return false;
            int aa = a.AgeYears(Time), ba = b.AgeYears(Time);
            if (aa < 16 || ba < 16 || aa > 55 || ba > 55 || Math.Abs(aa - ba) > 12) return false;
            if (AreFamily(a, b)) return false;
            var r = a.Rel(b.id);
            if (r != null && (r.kind == RelationKind.Rival || r.kind == RelationKind.Courting)) return false;
            return true;
        }

        void BeginCourtship(Citizen a, Citizen b, int cause)
        {
            var e = Record(SimEventType.Courtship, $"{a.Name} and {b.Name} begin courting",
                $"{a.Name} and {b.Name} find excuses to be in the same place at the same time.", 2,
                cause >= 0 ? C(cause) : null, C(a.id, b.id), PositionNow(a));
            var r1 = EnsureRelation(a, b); r1.kind = RelationKind.Courting; r1.causeEventId = e.id; r1.affinity = Math.Max(r1.affinity, 40);
            var r2 = EnsureRelation(b, a); r2.kind = RelationKind.Courting; r2.causeEventId = e.id; r2.affinity = Math.Max(r2.affinity, 40);
            Remember(a, $"Began courting {b.Name}", e.id);
            Remember(b, $"Began courting {a.Name}", e.id);
        }

        // ------------------------------------------------------------------ encounters on the road

        void ScheduleEncounters(Citizen c, float startDistance)
        {
            var a = c.activity;
            if (a == null || a.kind != ActivityKind.Travel || a.path == null) return;
            float speed = SpeedOf(c);
            foreach (var p in Props)
            {
                if (!p.active) continue;
                if (p.type != PropType.Pebble && p.type != PropType.GroundItem) continue;
                float d = NavGraph.FirstApproach(a.path, p.pos, p.radius, startDistance);
                if (d < 0) continue;
                long t = a.start + (long)(d / speed);
                if (t < Time) t = Time;
                Schedule(t, TaskKind.Encounter, c.id, (p.id << 10) | (PropVersion[p.id] & 1023), a.serial);
            }
            for (int i = 0; i < Glimmers.Count; i++)
            {
                var g = Glimmers[i];
                if (g.until <= Time || g.citizenId == c.id) continue;
                float d = NavGraph.FirstApproach(a.path, g.pos, 3f, startDistance);
                if (d < 0) continue;
                long t = a.start + (long)(d / speed);
                if (t < Time) t = Time;
                if (t > g.until) continue;
                Schedule(t, TaskKind.Encounter, c.id, -(i + 1), a.serial);
            }
        }

        /// <summary>A prop moved or appeared: anyone already walking must re-check their remaining route.</summary>
        void RecheckTravellers()
        {
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.activity == null || c.activity.kind != ActivityKind.Travel) continue;
                float done = (float)((Time - c.activity.start) * SpeedOf(c));
                // bump the serial so stale encounters are ignored, but keep the same path and timing
                var a = c.activity;
                c.activitySerial++;
                a.serial = c.activitySerial;
                Schedule(a.end, TaskKind.Decide, c.id, a.serial);
                ScheduleEncounters(c, done + 0.05f);
            }
        }

        void HandleEncounter(int cid, int packedProp, int serial)
        {
            var c = Citizens[cid];
            if (!c.IsActive || c.activitySerial != serial) return;
            if (packedProp < 0)
            {
                var g = Glimmers[-packedProp - 1];
                if (g.until < Time) return;
                var owner = GetCitizen(g.citizenId);
                if (owner == null || !owner.IsActive) return;
                Interrupt(c, ActivityKind.Distracted, 15 * SimTime.Minute, g.causeEventId);
                Meet(c, owner, g.causeEventId, "stopped beside", true);
                return;
            }
            int pid = packedProp >> 10;
            var p = Props[pid];
            if (!p.active || (PropVersion[pid] & 1023) != (packedProp & 1023)) return;
            if (p.type == PropType.Pebble) TripOver(c, p);
            else if (p.type == PropType.GroundItem)
            {
                int age = c.AgeYears(Time);
                double chance = c.Has(Trait.Curious) ? 0.7 : age < 14 ? 0.5 : 0.3;
                if (c.offMapResident) chance *= 0.3;
                if (Rand.Chance(chance, Seed, c.id, Time, p.id, (int)Salt.Notice)) PickUp(c, p);
            }
        }

        void TripOver(Citizen c, Prop pebble)
        {
            int age = c.AgeYears(Time);
            bool carrying = c.cargoItem != ItemType.None;
            double chance = carrying ? (c.Has(Trait.Clumsy) ? 0.95 : 0.7)
                : age < 14 ? 0.15 : age > 60 ? 0.45 : c.Has(Trait.Clumsy) ? 0.6 : 0.3;
            if (!Rand.Chance(chance, Seed, c.id, Time, pebble.id, (int)Salt.Trip)) return;

            var here = PositionNow(c);
            int cause = pebble.causeEventId;
            if (cause < 0 && !carrying)
            {
                Interrupt(c, ActivityKind.Idle, 4 * SimTime.Minute, -1); // a natural stumble nobody will remember
                return;
            }
            var trip = Record(SimEventType.Trip, $"{c.Name} {(carrying ? "trips and spills the cargo" : "trips over a pebble")}",
                $"{c.Name} catches a foot on a pebble{(carrying ? $" and the {c.cargo} goes flying" : "")}.",
                carrying ? 2 : 1, cause >= 0 ? C(cause) : null, C(c.id), here);
            Remember(c, "Tripped on the road", trip.id);

            if (carrying)
            {
                var dir = c.activity.path != null && c.activity.path.Count > 1
                    ? (c.activity.path[c.activity.path.Count - 1] - c.activity.path[0]).Normalized : new Vec2(1, 0);
                float side = Rand.RangeF(-1.5f, 1.5f, Seed, c.id, Time, 0, (int)Salt.Trip + 1);
                var drop = here + dir * 1.6f + new Vec2(-dir.z, dir.x) * side;
                var item = c.cargoItem;
                c.cargoItem = ItemType.None;
                var dropped = Record(SimEventType.ItemDropped, $"A {ItemName(item)} rolls into the grass",
                    $"Nobody notices the {ItemName(item)} that tumbled from {c.Name}'s pack.", 1, C(trip.id), C(c.id), drop);
                SpawnGroundItem(item, drop, dropped.id);
            }

            if (age > 60 && Rand.Chance(0.35, Seed, c.id, Time, 0, (int)Salt.Hit))
            {
                c.injuredUntilDay = (int)SimTime.DayIndex(Time) + 3;
                var inj = Record(SimEventType.Injury, $"{c.Name} twists an ankle", $"{c.Name} will be laid up for a few days.", 1, C(trip.id), C(c.id), here);
                Remember(c, "Twisted an ankle on the road", inj.id);
            }
            Interrupt(c, ActivityKind.Idle, 8 * SimTime.Minute, trip.id);
        }

        public static string ItemName(ItemType t) => t switch
        {
            ItemType.Gear => "brass gear",
            ItemType.Lens => "glass lens",
            ItemType.Book => "worn book",
            ItemType.Coin => "silver coin",
            ItemType.Key => "iron key",
            ItemType.Feather => "black feather",
            _ => "trinket"
        };

        Prop SpawnGroundItem(ItemType item, Vec2 pos, int cause)
        {
            var p = AddProp(PropType.GroundItem, pos, true, ItemName(item));
            p.item = item;
            p.causeEventId = cause;
            p.placedTime = Time;
            RecheckTravellers();
            return p;
        }

        void NoticeScan()
        {
            Schedule(Time + 30 * SimTime.Minute, TaskKind.NoticeScan);
            foreach (var p in Props)
            {
                if (!p.active || p.type != PropType.GroundItem) continue;
                foreach (var c in Citizens)
                {
                    if (!IsAwakeOutside(c) || c.activity.kind == ActivityKind.Travel) continue;
                    int age = c.AgeYears(Time);
                    bool playing = c.activity.kind == ActivityKind.Play || c.activity.kind == ActivityKind.Festival;
                    float radius = playing ? 12f : 6f;
                    float d = Vec2.Distance(PositionNow(c), p.pos);
                    if (d > radius) continue;
                    double chance = 0.16 * (c.Has(Trait.Curious) ? 2.2 : 1) * (age < 14 ? 1.3 : 1) * (1.2 - d / radius);
                    if (Rand.Chance(chance, Seed, c.id, Time, p.id, (int)Salt.Notice + 7))
                    {
                        PickUp(c, p);
                        break;
                    }
                }
            }
        }

        void PickUp(Citizen c, Prop p)
        {
            if (!p.active) return;
            p.active = false;
            PropVersion[p.id]++;
            int age = c.AgeYears(Time);
            var found = Record(SimEventType.ItemFound, $"{c.Name} finds a {ItemName(p.item)}",
                $"{c.Name} picks a {ItemName(p.item)} out of the grass.", 1, C(p.causeEventId), C(c.id), p.pos);
            Remember(c, $"Found a {ItemName(p.item)}", found.id);
            // briefly stop to look at it
            if (c.activity.kind == ActivityKind.Travel) Interrupt(c, ActivityKind.Idle, 6 * SimTime.Minute, found.id);

            switch (p.item)
            {
                case ItemType.Gear:
                    if (c.Has(Trait.Curious) && age <= 20) Spark(c, "engineering", found.id, "cannot stop turning the little gear over, counting its teeth");
                    else Sell(c, p.item, 6, found.id);
                    break;
                case ItemType.Lens:
                    if (c.Has(Trait.Curious) && age <= 25) Spark(c, "optics", found.id, "holds the lens up to the sky and sees the moon's craters");
                    else Sell(c, p.item, 8, found.id);
                    break;
                case ItemType.Book:
                    if (c.Has(Trait.Curious) || c.lore >= 25)
                    {
                        c.lore += 12;
                        if (age <= 20) Spark(c, "letters", found.id, "reads the book by candlelight until it falls apart");
                        else RecordIfChained(SimEventType.Discovery, $"{c.Name} studies a lost book", $"{c.Name} learns from its pages.", 1, C(found.id), C(c.id));
                    }
                    else RecordIfChained(SimEventType.ItemSold, $"{c.Name} uses a book as kindling", "Its secrets go up the chimney.", 0, C(found.id), C(c.id));
                    break;
                case ItemType.Coin:
                    FoundCoin(c, found.id);
                    break;
                case ItemType.Basket:
                {
                    var owner = GetCitizen(p.ownerId);
                    if (owner != null && owner.id != c.id && IsAwakeOutside(owner) && (!Scenario.isChallenge || age >= 16))
                        Meet(c, owner, found.id, "carries the lost basket of apples back to", false);
                    else c.mood = Math.Min(1, c.mood + 0.08f);
                    break;
                }
                case ItemType.Key:
                    if (c.Has(Trait.Curious))
                    {
                        var e = Record(SimEventType.Discovery, $"{c.Name} opens the crypt chest",
                            $"The iron key fits a forgotten chest beneath the chapel. Inside: an old almanac.", 2, C(found.id), C(c.id), p.pos);
                        c.lore += 15;
                        if (age <= 20) Spark(c, "letters", e.id, "pores over the almanac's star tables");
                    }
                    else Sell(c, p.item, 3, found.id);
                    break;
                default:
                    c.mood = Math.Min(1, c.mood + 0.05f);
                    break;
            }
        }

        void Sell(Citizen c, ItemType item, int value, int cause)
        {
            c.wealth += value;
            RecordIfChained(SimEventType.ItemSold, $"{c.Name} sells the {ItemName(item)}", $"{c.Name} trades it for {value} coins.", 0, C(cause), C(c.id));
        }

        void FoundCoin(Citizen c, int cause)
        {
            if (c.Has(Trait.Kind) && !c.Has(Trait.Greedy))
            {
                var e = Record(SimEventType.Donation, $"{c.Name} gives the coin to the chapel", $"{c.Name} drops the silver into the alms box.", 1, C(cause), C(c.id));
                foreach (var o in Citizens) if (o.IsActive && o.occupation == Occupation.Priest) { o.mood = Math.Min(1, o.mood + 0.1f); AdjustAffinity(o, c, 10); }
                c.fame += 1;
                Remember(c, "Gave a found coin to the chapel", e.id);
            }
            else if (c.Has(Trait.Reckless))
            {
                bool win = Rand.Chance(0.5, Seed, c.id, Time, 0, (int)Salt.Gamble);
                var e = Record(SimEventType.Gamble, win ? $"{c.Name} doubles the coin at dice" : $"{c.Name} loses the coin at dice",
                    win ? $"{c.Name}'s luck holds at the Crooked Kettle." : $"{c.Name} accuses the dice of cheating.", 1, C(cause), C(c.id));
                if (win) c.wealth += 20;
                else
                {
                    var inn = FirstWith(Occupation.Innkeeper);
                    if (inn != null && inn.id != c.id)
                    {
                        AdjustAffinity(c, inn, -35);
                        var r = Record(SimEventType.Rivalry, $"{c.Name} quarrels with {inn.Name}", $"Harsh words at the tavern between {c.Name} and {inn.Name}.", 2, C(e.id), C(c.id, inn.id));
                        EnsureRelation(c, inn).kind = RelationKind.Rival; EnsureRelation(inn, c).kind = RelationKind.Rival;
                        Remember(c, $"Fell out with {inn.Name}", r.id);
                    }
                }
            }
            else
            {
                c.wealth += 10;
                RecordIfChained(SimEventType.ItemSold, $"{c.Name} pockets the coin", $"{c.Name} is ten coins richer.", 0, C(cause), C(c.id));
            }
        }

        Citizen FirstWith(Occupation o)
        {
            foreach (var c in Citizens) if (c.IsActive && c.occupation == o) return c;
            return null;
        }

        public void Spark(Citizen c, string field, int cause, string flavour)
        {
            string key = "spark:" + field;
            if (c.marks.ContainsKey(key)) return;
            var e = Record(SimEventType.Spark, $"A spark of wonder in {c.Name}",
                $"{c.Name} {flavour}. Something has woken up.", 3, cause >= 0 ? C(cause) : null, C(c.id), PositionNow(c), true);
            c.marks[key] = e.id;
            c.goal = field switch
            {
                "engineering" => "Make the gears turn on their own",
                "optics" => "Chart every star above the vale",
                _ => "Read every book there is"
            };
            c.mood = Math.Min(1f, c.mood + 0.2f);
            Remember(c, "Felt a spark of wonder (" + field + ")", e.id);
        }

        /// <summary>Two people meet because something stopped one of them in the right place.</summary>
        public int Meet(Citizen a, Citizen b, int cause, string verb, bool glimmer)
        {
            if (a.id == b.id) return -1;
            var existing = a.Rel(b.id);
            bool strangers = existing == null || existing.affinity < 20;
            float gain = (a.Has(Trait.Curious) && b.Has(Trait.Curious)) ? 35 : 22;
            AdjustAffinity(a, b, gain, cause);
            var e = Record(SimEventType.Meeting,
                strangers ? $"{a.Name} and {b.Name} meet" : $"{a.Name} and {b.Name} share a moment",
                glimmer ? $"{a.Name} {verb} {b.Name}; both watch the same strange glimmer dance in the air." : $"{a.Name} {verb} {b.Name}.",
                strangers ? 2 : 1, cause >= 0 ? C(cause) : null, C(a.id, b.id), PositionNow(b));
            Remember(a, $"Met {b.Name}", e.id);
            Remember(b, $"Met {a.Name}", e.id);

            // mentorship: an old craftsman and a curious young mind
            TryApprentice(a, b, e.id);
            TryApprentice(b, a, e.id);

            if (CanCourt(a, b))
            {
                double p = 0.3 + (a.Has(Trait.Romantic) ? 0.25 : 0) + (b.Has(Trait.Romantic) ? 0.25 : 0);
                if (Rand.Chance(p, Seed, a.id * 1000 + b.id, Time, 0, (int)Salt.Romance)) BeginCourtship(a, b, e.id);
            }
            else if ((a.Has(Trait.Greedy) && b.Has(Trait.Ambitious)) || (a.Has(Trait.Ambitious) && b.Has(Trait.Greedy)))
            {
                AdjustAffinity(a, b, -40);
                var r = Record(SimEventType.Rivalry, $"{a.Name} and {b.Name} become rivals", "Two ambitions, one small vale.", 2, C(e.id), C(a.id, b.id));
                EnsureRelation(a, b).kind = RelationKind.Rival; EnsureRelation(b, a).kind = RelationKind.Rival;
                Remember(a, $"Became rivals with {b.Name}", r.id);
            }
            return e.id;
        }

        void TryApprentice(Citizen mentor, Citizen young, int meetEvent)
        {
            int ma = mentor.AgeYears(Time), ya = young.AgeYears(Time);
            bool craftsman = mentor.occupation == Occupation.Tinkerer || mentor.occupation == Occupation.Scholar;
            if (!craftsman || ma < 28 || ya < 7 || ya > 19 || !young.Has(Trait.Curious)) return;
            if (!(mentor.Has(Trait.Kind) || mentor.HasMark("lonely") || mentor.Has(Trait.Curious))) return;
            foreach (var r in young.relations.Values) if (r.kind == RelationKind.Mentor) return;
            var e = Record(SimEventType.Apprenticeship, $"{mentor.Name} takes {young.Name} as apprentice",
                $"{mentor.Name} hears a younger self in {young.Name}'s questions and invites them to the {(mentor.occupation == Occupation.Tinkerer ? "workshop" : "library")}.",
                3, C(meetEvent), C(mentor.id, young.id), PositionNow(young), true);
            var r1 = EnsureRelation(young, mentor); r1.kind = RelationKind.Mentor; r1.causeEventId = e.id; r1.affinity = Math.Max(r1.affinity, 50);
            var r2 = EnsureRelation(mentor, young); r2.kind = RelationKind.Apprentice; r2.causeEventId = e.id; r2.affinity = Math.Max(r2.affinity, 50);
            mentor.marks.Remove("lonely");
            mentor.mood = Math.Min(1, mentor.mood + 0.3f);
            Remember(young, $"Became {mentor.Name}'s apprentice", e.id);
            Remember(mentor, $"Took {young.Name} as an apprentice", e.id);
            Spark(young, mentor.occupation == Occupation.Tinkerer ? "engineering" : "letters", e.id,
                mentor.occupation == Occupation.Tinkerer ? "watches the old man's clockwork tick and asks a hundred questions" : "learns their letters by lamplight");
        }
    }
}
