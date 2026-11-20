using System;

namespace GOA.Sim
{
    public enum InputKind { Power = 0, EnergyGrant = 1 }

    /// <summary>A single player input. A world is exactly (seed, scenario, ordered inputs).</summary>
    [Serializable]
    public class PlayerInput
    {
        public long time;
        public InputKind kind;
        public PowerType power;
        public int targetId = -1;
        public float x, z, dx, dz;
        public float amount;

        public PlayerInput Clone() => (PlayerInput)MemberwiseClone();

        public static PlayerInput Nudge(int propId, Vec2 to) => new PlayerInput { power = PowerType.Nudge, targetId = propId, x = to.x, z = to.z };
        public static PlayerInput GustAt(Vec2 origin, Vec2 dir) => new PlayerInput { power = PowerType.Gust, x = origin.x, z = origin.z, dx = dir.x, dz = dir.z };
        public static PlayerInput DistractCitizen(int citizenId) => new PlayerInput { power = PowerType.Distract, targetId = citizenId };
        public static PlayerInput DropAt(Vec2 pos) => new PlayerInput { power = PowerType.Drop, x = pos.x, z = pos.z };
        public static PlayerInput Grant(float amount) => new PlayerInput { kind = InputKind.EnergyGrant, amount = amount };
    }

    public struct PowerResult
    {
        public bool ok;
        public string message;
        public int eventId;
        public static PowerResult Fail(string m) => new PowerResult { ok = false, message = m, eventId = -1 };
    }

    public sealed partial class World
    {
        public const float NudgeRange = 5f;

        public static float CostOf(PowerType p) => p switch
        {
            PowerType.Nudge => 1, PowerType.Distract => 1, PowerType.Gust => 2, PowerType.Drop => 2, _ => 3
        };

        public static bool IsUnlocked(PowerType p) => p == PowerType.Nudge || p == PowerType.Gust || p == PowerType.Distract || p == PowerType.Drop;

        public PowerResult Validate(PlayerInput input)
        {
            if (input.kind == InputKind.EnergyGrant) return new PowerResult { ok = true };
            if (!IsUnlocked(input.power)) return PowerResult.Fail("That power has not awakened yet.");
            if (Scenario.maxInterventions > 0 && InterventionCount >= Scenario.maxInterventions)
                return PowerResult.Fail($"This challenge allows only {Scenario.maxInterventions} intervention{(Scenario.maxInterventions == 1 ? "" : "s")}.");
            if (Energy + 1e-4f < CostOf(input.power)) return PowerResult.Fail("Not enough divine energy.");
            switch (input.power)
            {
                case PowerType.Nudge:
                {
                    var p = GetProp(input.targetId);
                    if (p == null || !p.active || !p.nudgeable) return PowerResult.Fail("Nothing there can be nudged.");
                    if (p.type == PropType.Lantern && p.state != 0) return PowerResult.Fail("The lantern has already fallen.");
                    if (p.type == PropType.Cart && p.state != 0) return PowerResult.Fail("The cart has already rolled.");
                    if (p.type == PropType.Pebble || p.type == PropType.GroundItem)
                    {
                        var to = new Vec2(input.x, input.z);
                        if (Vec2.Distance(to, p.pos) > NudgeRange + 0.01f) return PowerResult.Fail("A nudge can only move it a few paces.");
                        if (VillageLayout.DistanceToRiver(to) < VillageLayout.RiverHalfWidth - 0.5f) return PowerResult.Fail("It would sink in the river.");
                    }
                    return new PowerResult { ok = true };
                }
                case PowerType.Distract:
                {
                    var c = GetCitizen(input.targetId);
                    if (c == null || !c.IsActive) return PowerResult.Fail("No one is there.");
                    if (!IsAwakeOutside(c)) return PowerResult.Fail("They can't see a glimmer from where they are.");
                    if (c.activity.kind == ActivityKind.Distracted) return PowerResult.Fail("They are already lost in wonder.");
                    return new PowerResult { ok = true };
                }
                case PowerType.Gust:
                {
                    var d = new Vec2(input.dx, input.dz);
                    if (d.Length < 0.1f) return PowerResult.Fail("Choose a direction for the wind.");
                    return new PowerResult { ok = true };
                }
                case PowerType.Drop:
                {
                    var to = new Vec2(input.x, input.z);
                    if (Math.Abs(to.x) > VillageLayout.WorldHalfSize - 5 || Math.Abs(to.z) > VillageLayout.WorldHalfSize - 5) return PowerResult.Fail("The crow won't fly so far.");
                    if (Vec2.Distance(to, VillageLayout.OakPos) > 140) return PowerResult.Fail("Too far from the crow's oak.");
                    foreach (var b in Birds) if (!b.landed) return PowerResult.Fail("The crow is already on the wing.");
                    return new PowerResult { ok = true };
                }
            }
            return PowerResult.Fail("Unknown power.");
        }

        /// <summary>Apply a player input at the current simulation time.</summary>
        public PowerResult Apply(PlayerInput input)
        {
            var v = Validate(input);
            if (!v.ok) return v;
            if (input.kind == InputKind.EnergyGrant)
            {
                Energy = Math.Min(Scenario.maxEnergy, Energy + input.amount); // a reward never overfills the well
                return new PowerResult { ok = true, eventId = -1, message = $"+{input.amount:0} divine energy" };
            }
            Energy -= CostOf(input.power);
            InterventionCount++;
            SimEvent e;
            switch (input.power)
            {
                case PowerType.Nudge: e = DoNudge(input); break;
                case PowerType.Gust: e = DoGust(input); break;
                case PowerType.Distract: e = DoDistract(input); break;
                default: e = DoDrop(input); break;
            }
            return new PowerResult { ok = true, eventId = e.id, message = e.title };
        }

        SimEvent Intervention(PowerType p, string title, string detail, Vec2 pos)
        {
            var e = Record(SimEventType.Intervention, title, detail, 1, null, null, pos);
            e.power = p;
            return e;
        }

        SimEvent DoNudge(PlayerInput input)
        {
            var p = Props[input.targetId];
            if (p.type == PropType.Lantern)
            {
                double h = SimTime.HourOfDay(Time);
                bool lit = h >= 18 || h < 1;
                var e = Intervention(PowerType.Nudge, "You nudged the tavern lantern", lit ? "The lit lantern swings, slips its hook, and falls." : "The cold lantern drops from its hook.", p.pos);
                p.state = 1;
                PropVersion[p.id]++;
                var fall = Record(SimEventType.LanternFall, lit ? "A burning lantern falls" : "A lantern clatters to the porch",
                    lit ? "Oil spills across the boards of the Crooked Kettle's porch, burning." : "It dents. Nobody minds much.", lit ? 2 : 0, C(e.id), null, p.pos);
                if (lit) Schedule(Time + 4 * SimTime.Minute, TaskKind.LanternIgnite, p.id, fall.id);
                else p.state = 2;
                return e;
            }
            if (p.type == PropType.Cart)
            {
                var e = Intervention(PowerType.Nudge, "You nudged the wheel chock", "The chock slides out from under the hay cart's wheel.", p.pos);
                p.state = 1;
                PropVersion[p.id]++;
                var roll = Record(SimEventType.CartRoll, "The hay cart rolls loose!", "It gathers speed down the chapel lane toward the green.", 2, C(e.id), null, p.pos);
                Cart = new CartRoll { from = p.pos, to = VillageLayout.CartEnd, start = Time, end = Time + 3 * SimTime.Minute, causeEventId = roll.id };
                _cartHits.Clear();
                Schedule(Time + 15, TaskKind.CartStep);
                return e;
            }
            var to = new Vec2(input.x, input.z);
            string what = p.type == PropType.Pebble ? "a pebble" : $"the {ItemName(p.item)}";
            var ev = Intervention(PowerType.Nudge, $"You nudged {what}", $"{Capital(what)} rolls {Vec2.Distance(p.pos, to):0.0} paces {Compass(to - p.pos)}.", to);
            p.pos = to;
            p.causeEventId = ev.id;
            PropVersion[p.id]++;
            RecheckTravellers();
            return ev;
        }

        SimEvent DoGust(PlayerInput input)
        {
            var origin = new Vec2(input.x, input.z);
            var dir = new Vec2(input.dx, input.dz).Normalized;
            var e = Intervention(PowerType.Gust, $"You stirred a gust toward the {Compass(dir)}", "The air over the vale shivers and leans.", origin);
            Gust = new WindGust { origin = origin, dir = dir, strength = 1f, start = Time, end = Time + 90 * SimTime.Minute, causeEventId = e.id };

            // light things on the ground are carried downwind
            bool moved = false;
            foreach (var p in Props)
            {
                if (!p.active || p.type != PropType.GroundItem) continue;
                if (Vec2.Distance(p.pos, origin) > 25f) continue;
                var to = p.pos + dir * 4f;
                if (VillageLayout.DistanceToRiver(to) < VillageLayout.RiverHalfWidth) continue;
                var mv = Record(SimEventType.ItemDropped, $"The wind carries the {ItemName(p.item)}", $"It skitters four paces {Compass(dir)}.", 1, C(e.id, p.causeEventId), null, to);
                p.pos = to; p.causeEventId = mv.id; PropVersion[p.id]++; moved = true;
            }
            if (moved) RecheckTravellers();

            BlowEmbers(dir, 1f, e.id, origin, 0.3);
            Schedule(Time + 10 * SimTime.Minute, TaskKind.GustTick);
            return e;
        }

        SimEvent DoDistract(PlayerInput input)
        {
            var c = Citizens[input.targetId];
            var pos = PositionNow(c);
            var e = Intervention(PowerType.Distract, $"You set a glimmer before {c.Name}'s eyes", $"{c.Name} stops dead, enchanted by a drifting mote of light.", pos);
            Interrupt(c, ActivityKind.Distracted, 25 * SimTime.Minute, e.id);
            Remember(c, "Saw a strange glimmer in the air", e.id);
            Glimmers.Add(new Glimmer { pos = pos, until = Time + 25 * SimTime.Minute, citizenId = c.id, causeEventId = e.id });

            // anyone idling nearby notices it too
            foreach (var o in Citizens)
            {
                if (o.id == c.id || !IsAwakeOutside(o) || o.activity.kind == ActivityKind.Travel || o.activity.kind == ActivityKind.FightFire) continue;
                if (Vec2.Distance(PositionNow(o), pos) > 10f) continue;
                double join = o.Has(Trait.Curious) ? 1.0 : 0.5;
                if (!Rand.Chance(join, Seed, o.id, Time, c.id, (int)Salt.Meet)) continue;
                Interrupt(o, ActivityKind.Distracted, 20 * SimTime.Minute, e.id);
                Meet(o, c, e.id, "wanders over to stand beside", true);
            }
            RecheckTravellers();
            return e;
        }

        SimEvent DoDrop(PlayerInput input)
        {
            var to = new Vec2(input.x, input.z);
            var nest = NestProp();
            ItemType item = ItemType.Feather;
            if (nest != null && nest.hoard != null && nest.hoard.Count > 0) { item = nest.hoard[0]; nest.hoard.RemoveAt(0); }
            var e = Intervention(PowerType.Drop, "You sent the crow aloft", $"The crow leaves the old oak with something glinting in its beak.", to);
            Birds.Add(new BirdFlight { from = VillageLayout.OakPos, to = to, start = Time, end = Time + 4 * SimTime.Minute, item = item, causeEventId = e.id });
            Schedule(Time + 4 * SimTime.Minute, TaskKind.BirdArrive, Birds.Count - 1);
            return e;
        }

        public static string Compass(Vec2 d)
        {
            double a = Math.Atan2(d.z, d.x) * 180 / Math.PI; // 0 = east, 90 = north
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            int idx = (int)Math.Round(((a % 360) + 360) % 360 / 45.0) % 8;
            return names[idx];
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
