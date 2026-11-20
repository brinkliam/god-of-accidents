using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    /// <summary>A spot where a divine nudge would cause something juicy right now. Read-only hint for the view.</summary>
    public struct Opportunity
    {
        public Vec2 pos;
        public PowerType power;
        public int targetId;      // prop id (Nudge) or citizen id (Distract)
        public string hint;
        public float heat;        // 0..1, how promising
    }

    /// <summary>
    /// Ambient life: small natural mishaps that happen whether or not the player acts (a dropped basket, a stray goat,
    /// a tipped barrow, an argument, a hearth spark, a lucky find). Each one is an ordinary causal root, so anything
    /// it leads to (meetings, friendships, rivalries, sparks, fires) is chained to it in the ledger, never to the player.
    /// </summary>
    public sealed partial class World
    {
        enum AmbientKind { Basket = 0, Stray = 1, Spill = 2, Argument = 3, Hearth = 4, LuckyFind = 5, PrologueHelp = 6, StrayHelp = 7, ArgumentSide = 8 }
        const int PrologueSlot = -1;

        static readonly string[] ArgumentTopics =
        {
            "whose turn it is at the well", "a borrowed rake never returned", "the price of eggs", "who saw the goat first",
            "the proper way to stack firewood", "a song sung badly at the tavern", "a fence a foot too far east",
        };

        void ScheduleAmbientDay(long now, bool firstDay)
        {
            if (Scenario.ambientPerDay <= 0) return;
            long dayStart = SimTime.DayIndex(now) * SimTime.Day;
            long day = SimTime.DayIndex(now);
            int n = Scenario.ambientPerDay;
            for (int i = 0; i < n; i++)
            {
                // stratified across waking hours so the day never goes quiet for long
                double h = 7.5 + 12.0 * (i + Rand.U01(Seed, day, i, 0, (int)Salt.Ambient)) / n;
                long t = SimTime.At(dayStart, h);
                if (t > Time) Schedule(t, TaskKind.Ambient, i);
            }
            // the first chain reaction of a new world: within the first minute of real play
            if (firstDay) Schedule(Time + 30 * SimTime.Minute, TaskKind.Ambient, PrologueSlot);
        }

        List<Citizen> OutsideNow(int minAge)
        {
            var list = new List<Citizen>();
            foreach (var c in Citizens)
                if (IsAwakeOutside(c) && !c.offMapResident && c.AgeYears(Time) >= minAge
                    && c.activity.kind != ActivityKind.FightFire && c.activity.kind != ActivityKind.Flee) list.Add(c);
            return list;
        }

        Citizen Nearest(Citizen to, List<Citizen> pool, float maxDist, Func<Citizen, bool> ok = null)
        {
            Citizen best = null; float bd = maxDist;
            var p = PositionNow(to);
            foreach (var o in pool)
            {
                if (o.id == to.id || (ok != null && !ok(o))) continue;
                float d = Vec2.Distance(PositionNow(o), p);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        void AmbientAccident(int slot)
        {
            long day = SimTime.DayIndex(Time);
            // in a challenge the mishaps stay among adults and never scatter spark-worthy things, so the puzzle remains the player's to solve
            var outside = OutsideNow(Scenario.isChallenge ? 16 : 6);
            if (outside.Count == 0)
            {
                // the prologue waits a little for someone to step outside
                if (slot == PrologueSlot && Time < StartTime + 3 * SimTime.Hour) Schedule(Time + 10 * SimTime.Minute, TaskKind.Ambient, PrologueSlot);
                return;
            }
            if (slot == PrologueSlot) { Prologue(outside); return; }

            bool dry = Weather == Weather.Clear || Weather == Weather.Cloudy;
            int[] weights = { 3, 2, 2, 2, dry ? 1 : 0, 2 };
            int total = 0; foreach (var w in weights) total += w;
            int roll = Rand.Range(0, total, Seed, day, slot, 1, (int)Salt.Ambient);
            var kind = AmbientKind.Basket;
            for (int i = 0; i < weights.Length; i++) { if (roll < weights[i]) { kind = (AmbientKind)i; break; } roll -= weights[i]; }

            var a = outside[Rand.Range(0, outside.Count, Seed, day, slot, 2, (int)Salt.AmbientPick)];
            switch (kind)
            {
                case AmbientKind.Basket: DropBasket(a, false); break;
                case AmbientKind.Stray: StrayAnimal(a, outside, day, slot); break;
                case AmbientKind.Spill: SpillBarrow(a, day, slot); break;
                case AmbientKind.Argument: Argument(a, outside, day, slot); break;
                case AmbientKind.Hearth: HearthSpark(day, slot); break;
                default: LuckyFind(a); break;
            }
        }

        /// <summary>A guaranteed small chain to open every world: apples spill, a neighbour stops to help, two people meet.</summary>
        void Prologue(List<Citizen> outside)
        {
            // prefer someone walking near the heart of the village, with a neighbour close enough to help
            Citizen a = null, b = null; float best = float.MaxValue;
            foreach (var c in outside)
            {
                if (c.AgeYears(Time) < 14) continue;
                var helper = Nearest(c, outside, 30f, o => !AreFamily(o, c));
                if (helper == null) continue;
                float score = Vec2.Distance(PositionNow(c), VillageLayout.CartEnd) + Vec2.Distance(PositionNow(helper), PositionNow(c)) * 0.5f;
                if (score < best) { best = score; a = c; b = helper; }
            }
            if (a == null) { DropBasket(outside[0], true); return; }
            var drop = DropBasket(a, true);
            Schedule(Time + 5 * SimTime.Minute, TaskKind.AmbientFollow, (int)AmbientKind.PrologueHelp, (a.id << 16) | b.id, drop);
        }

        /// <returns>The mishap event id.</returns>
        int DropBasket(Citizen a, bool prologue)
        {
            var here = PositionNow(a);
            var e = Record(SimEventType.Mishap, $"{a.Name} drops a basket of apples",
                prologue ? $"The handle gives way. Apples bounce and roll across the lane in every direction." : $"Apples roll across the lane. {a.firstName} doesn't notice one basket is missing.",
                2, null, C(a.id), here);
            var p = SpawnGroundItem(ItemType.Basket, here + new Vec2(0.8f, 0.6f), e.id);
            p.ownerId = a.id;
            if (prologue) Interrupt(a, ActivityKind.Idle, 12 * SimTime.Minute, e.id); // stoops to gather them
            Remember(a, "Dropped a basket of apples", e.id);
            return e.id;
        }

        void StrayAnimal(Citizen a, List<Citizen> outside, long day, int slot)
        {
            string beast = Rand.Chance(0.5, Seed, day, slot, 3, (int)Salt.Ambient) ? "goat" : "pig";
            var here = PositionNow(a);
            var e = Record(SimEventType.Mishap, $"A runaway {beast} bowls past {a.Name}", $"{a.firstName} gives chase, shouting, as the {beast} makes for the vegetable rows.", 2, null, C(a.id), here);
            Interrupt(a, ActivityKind.Fetch, 18 * SimTime.Minute, e.id);
            Remember(a, $"Chased a runaway {beast}", e.id);
            var helper = Nearest(a, outside, 22f);
            if (helper != null) Schedule(Time + 6 * SimTime.Minute, TaskKind.AmbientFollow, (int)AmbientKind.StrayHelp, (a.id << 16) | helper.id, e.id);
            if (Rand.Chance(0.3, Seed, day, slot, 4, (int)Salt.Ambient))
            {
                Food = Math.Max(0, Food - 3);
                var loss = Record(SimEventType.FoodLoss, $"The {beast} gets into the cabbages", "Half a row is trampled before anyone catches it.", 1, C(e.id), null, here);
                LastFoodLossEventId = loss.id;
            }
        }

        void SpillBarrow(Citizen a, long day, int slot)
        {
            var here = PositionNow(a);
            var e = Record(SimEventType.Mishap, $"{a.Name}'s barrow tips into the ditch", "A wheel finds a rut. Baskets, sacks and odds and ends scatter across the verge.", 2, null, C(a.id), here);
            Food = Math.Max(0, Food - 2);
            LastFoodLossEventId = e.id;
            var basket = SpawnGroundItem(ItemType.Basket, here + new Vec2(-0.9f, 0.7f), e.id);
            basket.ownerId = a.id;
            double r = Rand.U01(Seed, day, slot, 5, (int)Salt.Ambient);
            var extra = Scenario.isChallenge || r < 0.45 ? ItemType.Coin : r < 0.75 ? ItemType.Book : ItemType.Gear;
            SpawnGroundItem(extra, here + new Vec2(1.1f, -0.8f), e.id);
            Interrupt(a, ActivityKind.Idle, 15 * SimTime.Minute, e.id);
            Remember(a, "Tipped the barrow into the ditch", e.id);
        }

        void Argument(Citizen a, List<Citizen> outside, long day, int slot)
        {
            if (a.AgeYears(Time) < 14) return;
            var b = Nearest(a, outside, 18f, o => o.AgeYears(Time) >= 14);
            if (b == null) { LuckyFind(a); return; }
            string topic = ArgumentTopics[Rand.Range(0, ArgumentTopics.Length, Seed, day, slot, 6, (int)Salt.Ambient)];
            var here = PositionNow(a);
            var e = Record(SimEventType.Mishap, $"{a.Name} and {b.Name} argue", $"Voices rise over {topic}.", 2, null, C(a.id, b.id), here);
            AdjustAffinity(a, b, -28, e.id);
            Interrupt(a, ActivityKind.Idle, 10 * SimTime.Minute, e.id);
            Interrupt(b, ActivityKind.Idle, 10 * SimTime.Minute, e.id);
            var r = a.Rel(b.id);
            if (r != null && r.affinity <= -30 && r.kind != RelationKind.Rival && r.kind != RelationKind.Spouse && !AreFamily(a, b))
            {
                var rv = Record(SimEventType.Rivalry, $"{a.Name} and {b.Name} become rivals", "Neither will be the first to apologise.", 2, C(e.id), C(a.id, b.id), here);
                EnsureRelation(a, b).kind = RelationKind.Rival; EnsureRelation(b, a).kind = RelationKind.Rival;
                Remember(a, $"Became rivals with {b.Name}", rv.id);
                Remember(b, $"Became rivals with {a.Name}", rv.id);
            }
            var onlooker = Nearest(a, outside, 20f, o => o.id != b.id && o.AgeYears(Time) >= 10);
            if (onlooker != null) Schedule(Time + 8 * SimTime.Minute, TaskKind.AmbientFollow, (int)AmbientKind.ArgumentSide, (onlooker.id << 16) | a.id, e.id);
        }

        void HearthSpark(long day, int slot)
        {
            var houses = new List<Building>();
            foreach (var b in Buildings) if (b.type == BuildingType.House && IsFlammable(b)) houses.Add(b);
            if (houses.Count == 0) return;
            var h = houses[Rand.Range(0, houses.Count, Seed, day, slot, 7, (int)Salt.Ambient)];
            var e = Record(SimEventType.Mishap, $"Sparks fly from the chimney of {h.name}", "A log shifts in the hearth and a gout of sparks rides the smoke up and out.", 2, null, null, h.pos);
            // most sparks die on the thatch; a dry, windy day gives them a chance
            double p = 0.25 + WindStrength * 0.35;
            if (Rand.Chance(p, Seed, day, slot, 8, (int)Salt.Ambient)) StartFire(h, C(e.id), "A hearth spark has caught the thatch.");
        }

        void LuckyFind(Citizen a)
        {
            var here = PositionNow(a);
            var e = Record(SimEventType.ItemFound, $"{a.Name} spots a silver coin in the grass", $"Something winks in the verge. {a.firstName} bends to pick it up.", 2, null, C(a.id), here);
            Remember(a, "Found a silver coin in the grass", e.id);
            FoundCoin(a, e.id);
        }

        void AmbientFollow(int kind, int pair, int cause)
        {
            var a = GetCitizen(pair >> 16);
            var b = GetCitizen(pair & 0xFFFF);
            if (a == null || b == null || !a.IsActive || !b.IsActive) return;
            if (!IsAwakeOutside(a) || !IsAwakeOutside(b)) return;
            switch ((AmbientKind)kind)
            {
                case AmbientKind.PrologueHelp:
                {
                    Interrupt(b, ActivityKind.Idle, 10 * SimTime.Minute, cause);
                    Meet(b, a, cause, "hurries over and helps gather the spilled apples with", false);
                    // the basket is gathered up again
                    foreach (var p in Props)
                        if (p.active && p.type == PropType.GroundItem && p.item == ItemType.Basket && p.causeEventId == cause) { p.active = false; PropVersion[p.id]++; }
                    break;
                }
                case AmbientKind.StrayHelp:
                    Interrupt(b, ActivityKind.Fetch, 12 * SimTime.Minute, cause);
                    Meet(b, a, cause, "helps corner the runaway with", false);
                    break;
                case AmbientKind.ArgumentSide:
                    Meet(a, b, cause, "takes the side of", false);
                    break;
            }
        }

        // ------------------------------------------------------------------ opportunities (read-only hints)

        /// <summary>Spots where a nudge would cause something juicy right now. Pure query: never changes the world.</summary>
        public List<Opportunity> FindOpportunities(int max = 4)
        {
            var list = new List<Opportunity>();
            double hour = SimTime.HourOfDay(Time);
            foreach (var p in Props)
            {
                if (!p.active) continue;
                if (p.type == PropType.Lantern && p.state == 0 && (hour >= 18 || hour < 1) && HasComplete(BuildingType.Tavern))
                    list.Add(new Opportunity { pos = p.pos, power = PowerType.Nudge, targetId = p.id, hint = "A lit lantern hangs by a worn hook", heat = 0.75f });
                else if (p.type == PropType.Cart && p.state == 0)
                {
                    foreach (var c in Citizens)
                        if (IsAwakeOutside(c) && Vec2.Distance(PositionNow(c), VillageLayout.CartEnd) < 18f)
                        { list.Add(new Opportunity { pos = p.pos, power = PowerType.Nudge, targetId = p.id, hint = $"{c.Name} is near the bottom of the lane", heat = 0.7f }); break; }
                }
                else if (p.type == PropType.Pebble || p.type == PropType.GroundItem)
                {
                    foreach (var c in Citizens)
                    {
                        var a = c.activity;
                        if (!c.IsActive || a == null || a.kind != ActivityKind.Travel || a.path == null) continue;
                        float done = (float)((Time - a.start) * SpeedOf(c));
                        float d = NavGraph.FirstApproach(a.path, p.pos, NudgeRange, done);
                        if (d < 0) continue;
                        double eta = (d - done) / SpeedOf(c);
                        if (eta < 20 || eta > 25 * 60) continue;
                        bool juicy = p.type == PropType.Pebble ? (c.AgeYears(Time) >= 14 && !c.offMapResident || c.cargoItem != ItemType.None)
                                                              : (c.Has(Trait.Curious) && c.AgeYears(Time) <= 20);
                        if (!juicy) continue;
                        string hint = p.type == PropType.Pebble
                            ? (c.cargoItem != ItemType.None ? $"{c.Name} is coming this way with a {c.cargo}" : $"{c.Name} is walking this way")
                            : $"Curious {c.firstName} will pass this {ItemName(p.item)}";
                        list.Add(new Opportunity { pos = p.pos, power = PowerType.Nudge, targetId = p.id, hint = hint, heat = c.cargoItem != ItemType.None || p.type == PropType.GroundItem ? 0.95f : c.AgeYears(Time) > 60 ? 0.65f : 0.5f });
                        break;
                    }
                }
            }
            foreach (var c in Citizens)
            {
                if (!IsAwakeOutside(c) || c.offMapResident || c.activity.kind == ActivityKind.Distracted) continue;
                int age = c.AgeYears(Time);
                var here = PositionNow(c);
                foreach (var o in Citizens)
                {
                    if (o.id <= c.id && !(age >= 7 && age <= 16)) continue;
                    if (o.id == c.id || !IsAwakeOutside(o) || o.offMapResident) continue;
                    float d = Vec2.Distance(here, PositionNow(o));
                    if (d > 14f) continue;
                    bool mentor = age >= 7 && age <= 16 && c.Has(Trait.Curious) && (o.occupation == Occupation.Tinkerer || o.occupation == Occupation.Scholar);
                    if (mentor)
                    {
                        list.Add(new Opportunity { pos = here, power = PowerType.Distract, targetId = c.id, hint = $"Curious {c.firstName} is a few steps from the {o.occupation.ToString().ToLowerInvariant()}", heat = 1f });
                        break;
                    }
                    var r = c.Rel(o.id);
                    if ((r == null || r.affinity < 20) && !AreFamily(c, o) && d < 11f && age >= 10 && o.AgeYears(Time) >= 10)
                    {
                        bool sparks = CanCourt(c, o);
                        list.Add(new Opportunity { pos = here, power = PowerType.Distract, targetId = c.id, hint = $"{c.firstName} and {o.firstName} have never really talked", heat = sparks ? 0.6f : 0.45f });
                        break;
                    }
                }
            }
            list.Sort((x, y) => y.heat.CompareTo(x.heat));
            var seen = new HashSet<long>();
            var result = new List<Opportunity>();
            foreach (var o in list)
            {
                if (!seen.Add(((long)o.power << 32) | (uint)o.targetId)) continue;
                result.Add(o);
                if (result.Count >= max) break;
            }
            return result;
        }

        /// <summary>
        /// Where a one-tap Nudge should roll this pebble or trinket: onto the path of the soonest traveller who will
        /// pass within reach, else onto the nearest road, else a few paces toward the nearest villager. Pure query.
        /// </summary>
        public Vec2 SuggestNudgeTarget(int propId)
        {
            var p = GetProp(propId);
            if (p == null) return default;
            Vec2 best = p.pos; double bestEta = double.MaxValue; bool found = false;
            foreach (var c in Citizens)
            {
                var a = c.activity;
                if (!c.IsActive || a == null || a.kind != ActivityKind.Travel || a.path == null || a.path.Count < 2) continue;
                float done = (float)((Time - a.start) * SpeedOf(c));
                float walked = 0;
                for (int i = 0; i + 1 < a.path.Count; i++)
                {
                    var s0 = a.path[i]; var s1 = a.path[i + 1];
                    float len = Vec2.Distance(s0, s1);
                    if (walked + len < done + 2f) { walked += len; continue; }
                    float d = Vec2.DistanceToSegment(p.pos, s0, s1, out float u);
                    if (d <= NudgeRange - 0.2f)
                    {
                        var at = Vec2.Lerp(s0, s1, u);
                        double eta = (walked + u * len - done) / SpeedOf(c);
                        if (eta > 5 && eta < bestEta && VillageLayout.DistanceToRiver(at) >= VillageLayout.RiverHalfWidth) { bestEta = eta; best = at; found = true; }
                        break;
                    }
                    walked += len;
                }
            }
            if (found) return best;
            float bd = NudgeRange - 0.2f;
            foreach (var n in Nav.nodes)
                foreach (var l in n.links)
                {
                    if (l < n.id) continue;
                    float d = Vec2.DistanceToSegment(p.pos, n.pos, Nav.Node(l).pos, out float u);
                    var at = Vec2.Lerp(n.pos, Nav.Node(l).pos, u);
                    if (d < bd && VillageLayout.DistanceToRiver(at) >= VillageLayout.RiverHalfWidth) { bd = d; best = at; found = true; }
                }
            if (found) return best;
            Citizen near = null; float nd = float.MaxValue;
            foreach (var c in Citizens) if (IsAwakeOutside(c)) { float d = Vec2.Distance(PositionNow(c), p.pos); if (d < nd) { nd = d; near = c; } }
            var dir = near != null ? (PositionNow(near) - p.pos).Normalized : new Vec2(1, 0);
            return p.pos + dir * Math.Min(3f, NudgeRange - 0.5f);
        }
    }
}
