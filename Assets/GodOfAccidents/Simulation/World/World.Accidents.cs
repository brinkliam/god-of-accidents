using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public sealed partial class World
    {
        // ------------------------------------------------------------------ fire

        public Fire FireAt(int buildingId)
        {
            foreach (var f in Fires) if (f.active && f.buildingId == buildingId) return f;
            return null;
        }

        bool IsFlammable(Building b) =>
            b != null && b.Complete && b.flammable && !(b.type == BuildingType.House && b.tier >= 2) && FireAt(b.id) == null;

        public Fire StartFire(Building b, int[] causes, string how, SimEventType type = SimEventType.FireStarted)
        {
            if (!IsFlammable(b)) return null;
            var e = Record(type, $"Fire at {b.name}!", how, 3, causes, null, b.pos, true);
            var fire = new Fire { id = Fires.Count, buildingId = b.id, pos = b.pos, intensity = 0.18f, causeEventId = e.id, startTime = Time };
            Fires.Add(fire);
            Schedule(Time + 10 * SimTime.Minute, TaskKind.FireUpdate, fire.id);
            AlertFirefighters(fire, b);
            return fire;
        }

        void AlertFirefighters(Fire fire, Building b)
        {
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.activity == null || c.activity.kind == ActivityKind.OffMap) continue;
                int age = c.AgeYears(Time);
                if (age < 12 || age > 70 || c.injuredUntilDay >= SimTime.DayIndex(Time)) continue;
                if (c.Has(Trait.Timid) && !c.Has(Trait.Brave)) continue;
                float d = Vec2.Distance(PositionNow(c), fire.pos);
                bool asleep = c.activity.kind == ActivityKind.Sleep;
                if (d > (asleep ? 18f : 55f)) continue;
                float ang = Rand.RangeF(0, 6.283f, Seed, c.id, fire.id, 0, (int)Salt.Fight);
                var spot = fire.pos + Vec2.FromAngle(ang) * (Math.Max(b.size.x, b.size.z) * 0.5f + 1.6f);
                SetDirective(c, ActivityKind.FightFire, b.nodeId, spot, Time + 8 * SimTime.Hour, fire.causeEventId, fire.id);
            }
        }

        void FireUpdate(int fid)
        {
            var fire = Fires[fid];
            if (!fire.active) return;
            var b = Buildings[fire.buildingId];
            float reach = Math.Max(b.size.x, b.size.z) * 0.5f + 3.5f;
            int ff = 0;
            Citizen firstArrived = null;
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.activity == null || c.activity.kind != ActivityKind.FightFire) continue;
                var d = DirectiveOf(c);
                if (d == null || d.tag != fid) continue;
                if (Vec2.Distance(PositionNow(c), fire.pos) > reach) continue;
                ff++;
                if (firstArrived == null || c.activity.start < firstArrived.activity.start) firstArrived = c;
            }
            fire.firefighters = ff;
            float wind = WindStrength + (Gust != null && Gust.end > Time ? Gust.strength : 0);
            float growth = 0.1f + 0.08f * wind;
            if (Weather == Weather.Rain || Weather == Weather.Storm || Weather == Weather.Snow) growth -= 0.2f;
            float fight = 0.075f * ff * (Policy == Policy.Watch ? 1.4f : 1f);
            fire.intensity = Math.Min(1f, fire.intensity + growth - fight);

            if (fire.intensity > 0.55f)
            {
                foreach (var o in Buildings)
                {
                    if (o.id == b.id || !IsFlammable(o)) continue;
                    float dist = Vec2.Distance(o.pos, b.pos);
                    if (dist > 13f) continue;
                    var dir = (o.pos - b.pos).Normalized;
                    float downwind = Math.Max(0, Vec2.Dot(dir, WindDir));
                    double p = 0.07 * fire.intensity * (0.5 + downwind) * (1 - dist / 14f);
                    if (Rand.Chance(p, Seed, fire.id, Time, o.id, (int)Salt.Spread))
                        StartFire(o, C(fire.causeEventId), $"Flames leap from {b.name} to {o.name}.", SimEventType.FireSpread);
                }
            }

            if (fire.intensity <= 0f)
            {
                fire.active = false;
                var e = Record(SimEventType.FireExtinguished, $"The fire at {b.name} is out",
                    ff > 0 ? $"{ff} villagers with buckets beat back the flames." : "The flames gutter and die.", 2, C(fire.causeEventId), null, b.pos);
                if (firstArrived != null)
                {
                    firstArrived.fame += 3;
                    bool unlikely = firstArrived.Has(Trait.Timid) || firstArrived.AgeYears(Time) < 16 || firstArrived.Has(Trait.Clumsy);
                    var h = Record(SimEventType.Heroism, $"{firstArrived.Name} leads the bucket line",
                        $"{firstArrived.Name} was first to the flames at {b.name}{(unlikely ? " - nobody expected it of them" : "")}.", 2,
                        C(e.id), C(firstArrived.id), b.pos);
                    Remember(firstArrived, $"Fought the fire at {b.name}", h.id);
                }
                ReleaseFirefighters(fid);
                return;
            }

            if (fire.intensity >= 1f)
            {
                if (fire.burnedAtMaxSince < 0) fire.burnedAtMaxSince = Time;
                if (Time - fire.burnedAtMaxSince >= 50 * SimTime.Minute)
                {
                    fire.active = false;
                    DestroyBuilding(b, fire.causeEventId, "burned to the ground");
                    ReleaseFirefighters(fid);
                    return;
                }
            }
            else fire.burnedAtMaxSince = -1;

            Schedule(Time + 10 * SimTime.Minute, TaskKind.FireUpdate, fid);
        }

        void ReleaseFirefighters(int fid)
        {
            foreach (var c in Citizens)
            {
                var d = DirectiveOf(c);
                if (d != null && d.kind == ActivityKind.FightFire && d.tag == fid) ClearDirective(c);
            }
        }

        void DestroyBuilding(Building b, int cause, string how)
        {
            b.destroyed = true;
            var e = Record(SimEventType.BuildingDestroyed, $"{b.name} is lost", $"{b.name} {how}.", b.type == BuildingType.Haystack ? 1 : 3, C(cause), null, b.pos, b.type != BuildingType.Haystack);
            switch (b.type)
            {
                case BuildingType.Granary:
                {
                    float lost = Food * 0.65f;
                    Food -= lost;
                    var f = Record(SimEventType.FoodLoss, "The winter stores are ash", $"{lost:0} days of grain burned with the granary.", 3, C(e.id), null, b.pos, true);
                    LastFoodLossEventId = f.id;
                    break;
                }
                case BuildingType.Barn:
                {
                    float lost = Food * 0.25f;
                    Food -= lost;
                    var f = Record(SimEventType.FoodLoss, "The barn's harvest is lost", $"{lost:0} portions of food gone.", 2, C(e.id), null, b.pos);
                    LastFoodLossEventId = f.id;
                    break;
                }
                case BuildingType.Haystack:
                    Food = Math.Max(0, Food - 4);
                    break;
                case BuildingType.Market:
                    foreach (var c in Citizens) if (c.IsActive && c.occupation == Occupation.Shopkeeper) c.wealth = (int)(c.wealth * 0.6f);
                    break;
                case BuildingType.House:
                    foreach (var c in Citizens) if (c.IsActive && c.homeBuildingId == b.id) { c.mood = Math.Max(0, c.mood - 0.3f); Remember(c, "Lost our home to fire", e.id); }
                    break;
            }
            if (b.type != BuildingType.Haystack) QueueConstruction(b, e.id, true);
        }

        public void QueueConstruction(Building b, int cause, bool rebuild)
        {
            b.destroyed = false;
            b.buildProgress = 0f;
            b.workRequired = b.type switch
            {
                BuildingType.House => 36f, BuildingType.Mill => 90f, BuildingType.ClockTower => 80f, BuildingType.Library => 80f,
                BuildingType.Windmill => 70f, BuildingType.Well => 16f, BuildingType.Granary => 40f, BuildingType.Wall => 120f,
                _ => 50f
            };
            b.causeEventId = cause;
            Projects.Add(new ConstructionProject { buildingId = b.id, causeEventId = cause });
            if (rebuild) b.tier = HouseTierForEra();
        }

        void AddBuildProgress(Citizen c, float hours)
        {
            var site = ActiveProjectSite();
            if (site == null || hours <= 0) return;
            float woodNeed = hours * 0.5f;
            float rate = Wood >= woodNeed ? 1f : 0.35f;
            Wood = Math.Max(0, Wood - woodNeed);
            site.buildProgress = Math.Min(1f, site.buildProgress + hours * rate / Math.Max(1f, site.workRequired));
            if (site.buildProgress >= 1f) CompleteConstruction(site);
        }

        void CompleteConstruction(Building b)
        {
            ConstructionProject proj = null;
            foreach (var p in Projects) if (p.buildingId == b.id) { proj = p; break; }
            if (proj != null) Projects.Remove(proj);
            b.builtTime = Time;
            bool major = b.type == BuildingType.Mill || b.type == BuildingType.ClockTower || b.type == BuildingType.Library || b.type == BuildingType.Windmill;
            var e = Record(SimEventType.Construction, $"{b.name} {(major ? "rises" : "is finished")}", $"The village completes {b.name}.", major ? 3 : 1,
                proj != null ? C(proj.causeEventId) : null, null, b.pos, major);
            if (b.type == BuildingType.Well)
            {
                WellBroken = false;
                foreach (var p in Props) if (p.type == PropType.Cart) { p.pos = p.originalPos; p.state = 0; PropVersion[p.id]++; }
            }
            if (b.type == BuildingType.Mill) foreach (var c in Citizens) if (c.IsActive && c.occupation == Occupation.Miller) AssignWorkNode(c);
        }

        // ------------------------------------------------------------------ embers & wind

        struct EmberSource { public Vec2 pos; public int cause; public string name; public int buildingId; }

        List<EmberSource> EmberSources()
        {
            var list = new List<EmberSource>();
            double h = SimTime.HourOfDay(Time);
            var forge = FirstBuilding(BuildingType.Forge, true);
            if (forge != null)
                foreach (var c in Citizens)
                    if (c.IsActive && c.occupation == Occupation.Smith && c.activity != null && c.activity.kind == ActivityKind.Work && c.activity.nodeId == Nav["Forge"])
                    { list.Add(new EmberSource { pos = forge.pos, cause = -1, name = "the forge", buildingId = forge.id }); break; }
            foreach (var f in Fires)
                if (f.active) list.Add(new EmberSource { pos = f.pos, cause = f.causeEventId, name = "the blaze", buildingId = f.buildingId });
            if (SimTime.IsHolyDay(Time) && h >= 14 && h < 18)
                list.Add(new EmberSource { pos = Nav.Node(Nav["Green"]).pos, cause = -1, name = "the festival bonfire", buildingId = -1 });
            if (h >= 17 && h < 22)
                foreach (var b in Buildings)
                    if (b.type == BuildingType.House && b.Complete && b.tier < 3)
                        list.Add(new EmberSource { pos = b.pos, cause = -1, name = $"the hearth of {b.name}", buildingId = b.id });
            return list;
        }

        void GustTick()
        {
            bool gusting = Gust != null && Gust.end > Time;
            if (!gusting) return;
            Schedule(Time + 10 * SimTime.Minute, TaskKind.GustTick);
            BlowEmbers(Gust.dir, Gust.strength, Gust.causeEventId, Gust.origin, 0.3);
        }

        void BlowEmbers(Vec2 dir, float strength, int gustCause, Vec2 origin, double baseChance)
        {
            foreach (var s in EmberSources())
            {
                if (gustCause >= 0 && Vec2.Distance(s.pos, origin) > 45f) continue; // a gust is local
                foreach (var b in Buildings)
                {
                    if (b.id == s.buildingId || !IsFlammable(b)) continue;
                    var to = b.pos - s.pos;
                    float d = to.Length;
                    if (d < 4f || d > 34f) continue;
                    float cos = Vec2.Dot(to.Normalized, dir);
                    if (cos < 0.86f) continue;
                    double p = baseChance * strength * (1.0 - d / 40.0) * (cos - 0.8) * 5;
                    if (!Rand.Chance(p, Seed, b.id, Time, (long)(s.pos.x * 7 + s.pos.z * 13), (int)Salt.Ignite)) continue;
                    var causes = new List<int>();
                    if (gustCause >= 0) causes.Add(gustCause);
                    if (s.cause >= 0) causes.Add(s.cause);
                    StartFire(b, causes.ToArray(), $"Embers from {s.name} ride the wind onto {b.name}.");
                }
            }
        }

        /// <summary>Strong natural winds occasionally carry embers too - the world has accidents of its own.</summary>
        void NaturalWindCheck()
        {
            if (WindStrength < 0.75f) return;
            BlowEmbers(WindDir, WindStrength, -1, default, 0.035);
        }

        // ------------------------------------------------------------------ the tavern lantern

        void LanternIgnite(int propId, int fallEvent)
        {
            var lantern = Props[propId];
            if (lantern.state != 1) return;
            var tavern = FirstBuilding(BuildingType.Tavern);
            Citizen stamper = null; float best = float.MaxValue;
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.activity == null || c.activity.kind == ActivityKind.Sleep || c.activity.kind == ActivityKind.OffMap) continue;
                bool inTavern = c.activity.nodeId == Nav["Tavern"] && c.activity.kind != ActivityKind.Travel;
                float d = Vec2.Distance(PositionNow(c), lantern.pos);
                if (!inTavern && (d > 9f || IsIndoors(c))) continue;
                double react = c.Has(Trait.Brave) ? 0.9 : c.Has(Trait.Timid) ? 0.25 : 0.55;
                if (!Rand.Chance(react, Seed, c.id, Time, propId, (int)Salt.Rescue)) continue;
                float score = d - (c.Has(Trait.Brave) ? 5 : 0);
                if (score < best) { best = score; stamper = c; }
            }
            if (stamper != null)
            {
                stamper.fame += 2;
                var h = Record(SimEventType.Heroism, $"{stamper.Name} stamps out the lantern fire",
                    $"{stamper.Name} smothers the burning oil before it reaches the thatch of the Crooked Kettle.", 2, C(fallEvent), C(stamper.id), lantern.pos);
                Remember(stamper, "Stamped out a fire at the tavern", h.id);
                lantern.state = 2; // smashed, out
                return;
            }
            lantern.state = 2;
            StartFire(tavern, C(fallEvent), "Burning oil from the fallen lantern creeps into the tavern's thatch.");
        }

        // ------------------------------------------------------------------ the runaway cart

        readonly HashSet<int> _cartHits = new HashSet<int>();

        public Vec2 CartPosition(double t)
        {
            var cartProp = CartProp();
            if (Cart == null || cartProp == null) return cartProp != null ? cartProp.pos : default;
            double u = (t - Cart.start) / Math.Max(1, Cart.end - Cart.start);
            if (u <= 0) return Cart.from;
            if (u >= 1) return Cart.to;
            float e = (float)Math.Pow(u, 1.6);
            return Vec2.Lerp(Cart.from, Cart.to, e);
        }

        public Prop CartProp()
        {
            foreach (var p in Props) if (p.type == PropType.Cart) return p;
            return null;
        }

        void CartStep()
        {
            if (Cart == null || Cart.done) return;
            var pos = CartPosition(Time);
            var cartProp = CartProp();
            cartProp.pos = pos;
            foreach (var c in Citizens)
            {
                if (!IsAwakeOutside(c) || _cartHits.Contains(c.id)) continue;
                if (Vec2.Distance(PositionNow(c), pos) > 2.4f) continue;
                _cartHits.Add(c.id);
                Citizen rescuer = null;
                foreach (var o in Citizens)
                {
                    if (o.id == c.id || !IsAwakeOutside(o) || o.AgeYears(Time) < 12) continue;
                    if (Vec2.Distance(PositionNow(o), pos) > 9f) continue;
                    double react = o.Has(Trait.Brave) ? 0.85 : o.Has(Trait.Timid) ? 0.1 : 0.35;
                    if (Rand.Chance(react, Seed, o.id, Time, c.id, (int)Salt.Rescue)) { rescuer = o; break; }
                }
                if (rescuer != null)
                {
                    rescuer.fame += 3;
                    var e = Record(SimEventType.Rescue, $"{rescuer.Name} pulls {c.Name} clear",
                        $"{rescuer.Name} dives and drags {c.Name} out of the runaway cart's path.", 2, C(Cart.causeEventId), C(rescuer.id, c.id), pos);
                    AdjustAffinity(rescuer, c, 30);
                    Remember(rescuer, $"Saved {c.Name} from the runaway cart", e.id);
                    Remember(c, $"Was saved by {rescuer.Name}", e.id);
                    Interrupt(c, ActivityKind.Idle, 10 * SimTime.Minute, e.id);
                    Interrupt(rescuer, ActivityKind.Idle, 10 * SimTime.Minute, e.id);
                }
                else
                {
                    c.injuredUntilDay = (int)SimTime.DayIndex(Time) + 5;
                    var e = Record(SimEventType.Injury, $"{c.Name} is struck by the cart", $"{c.Name} is knocked down and badly bruised.", 2, C(Cart.causeEventId), C(c.id), pos);
                    Remember(c, "Was struck by a runaway cart", e.id);
                    Interrupt(c, ActivityKind.Recover, 30 * SimTime.Minute, e.id);
                }
            }

            if (Time >= Cart.end)
            {
                Cart.done = true;
                cartProp.state = 2;
                cartProp.pos = Cart.to;
                PropVersion[cartProp.id]++;
                var well = FirstBuilding(BuildingType.Well);
                if (well != null && well.Complete)
                {
                    var e = Record(SimEventType.WellBroken, "The cart smashes into the Old Well",
                        "The windlass splinters and stones tumble into the shaft. The village must carry water from the river.", 2,
                        C(Cart.causeEventId), null, well.pos, false);
                    WellBroken = true;
                    WellBrokenEventId = e.id;
                    QueueConstruction(well, e.id, true);
                }
                return;
            }
            Schedule(Time + 15, TaskKind.CartStep);
        }

        // ------------------------------------------------------------------ the crow

        void BirdArrive(int idx)
        {
            var bird = Birds[idx];
            if (bird.landed) return;
            bird.landed = true;
            var e = Record(SimEventType.ItemDropped, $"The crow drops a {ItemName(bird.item)}",
                $"A {ItemName(bird.item)} falls from the crow's beak and lands in the grass.", 1, C(bird.causeEventId), null, bird.to);
            if (bird.item != ItemType.Feather) SpawnGroundItem(bird.item, bird.to, e.id);
        }
    }
}
