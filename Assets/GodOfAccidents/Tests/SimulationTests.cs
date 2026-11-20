using System;
using System.Collections.Generic;
using System.Linq;
using GOA.Sim;
using NUnit.Framework;
using static GOA.Tests.SimTestUtil;

namespace GOA.Tests
{
    public class DeterminismTests
    {
        [TestCase(1UL)]
        [TestCase(1207UL)]
        [TestCase(98765UL)]
        public void SameSeedSameHistory(ulong seed)
        {
            var a = new World(seed);
            var b = new World(seed);
            a.AdvanceTo(2 * SimTime.Year);
            b.AdvanceTo(2 * SimTime.Year);
            Assert.AreEqual(a.Ledger.Count, b.Ledger.Count);
            Assert.AreEqual(a.StateHash(), b.StateHash());
        }

        [Test]
        public void ChunkedAdvanceMatchesSingleAdvance()
        {
            var a = new World(55);
            var b = new World(55);
            a.AdvanceTo(SimTime.Year);
            for (long t = 0; t <= SimTime.Year; t += 997) b.AdvanceTo(t);
            b.AdvanceTo(SimTime.Year);
            Assert.AreEqual(a.StateHash(), b.StateHash());
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            var a = new World(11); var b = new World(12);
            a.AdvanceTo(SimTime.Year / 2); b.AdvanceTo(SimTime.Year / 2);
            Assert.AreNotEqual(a.StateHash(), b.StateHash());
        }

        [Test]
        public void LivePlayEqualsReplay()
        {
            var t = new Timeline(1207, ScenarioDef.Sandbox);
            t.AdvanceTo(At(2, 7.5));
            var peb = Pebble(t.World, 0);
            Assert.IsTrue(t.Act(PlayerInput.Nudge(peb.id, new Vec2(35, 0.2f))).ok);
            t.AdvanceTo(At(3, 14));
            var r = t.Act(PlayerInput.GustAt(new Vec2(-14, -12), new Vec2(-0.5f, -0.8f)));
            Assert.IsTrue(r.ok, r.message);
            t.AdvanceTo(At(40, 10));
            var replay = Timeline.Replay(t.Seed, t.Scenario, t.Inputs, t.World.Time);
            Assert.AreEqual(t.World.StateHash(), replay.StateHash());
        }

        [Test]
        public void SaveLoadRestoresIdenticalState()
        {
            var t = new Timeline(31337, ScenarioDef.Sandbox);
            t.AdvanceTo(At(1, 16));
            var someone = t.World.Citizens.First(c => t.World.IsAwakeOutside(c));
            Assert.IsTrue(t.Act(PlayerInput.DistractCitizen(someone.id)).ok);
            t.AdvanceTo(At(9, 3));
            var save = t.ToSave();
            var loaded = Timeline.FromSave(save);
            Assert.IsTrue(loaded.VerifyHash(save));
            Assert.AreEqual(t.World.Ledger.Count, loaded.World.Ledger.Count);
            loaded.AdvanceTo(At(20, 0)); t.AdvanceTo(At(20, 0));
            Assert.AreEqual(t.World.StateHash(), loaded.World.StateHash(), "worlds must keep evolving identically after load");
        }

        [Test]
        public void BranchKeepsSharedPastAndCanDiverge()
        {
            var t = new Timeline(2024, ScenarioDef.Sandbox);
            t.AdvanceTo(At(4, 8));
            var peb = Pebble(t.World, 1);
            t.Act(PlayerInput.Nudge(peb.id, new Vec2(-8, -0.1f)));
            t.AdvanceTo(At(10, 8));
            var branch = t.Branch(At(4, 8), "What if");
            Assert.AreEqual(0, branch.Inputs.Count);
            var baseline = new World(2024); baseline.AdvanceTo(At(4, 8));
            Assert.AreEqual(baseline.StateHash(), branch.World.StateHash());
        }
    }

    public class CausalityTests
    {
        [TestCase(1207UL)]
        [TestCase(5UL)]
        public void CausesAlwaysPrecedeEffectsAndRootsAreInterventions(ulong seed)
        {
            var t = new Timeline(seed, ScenarioDef.Sandbox);
            t.AdvanceTo(At(1, 7));
            t.Act(PlayerInput.Nudge(Pebble(t.World, 0).id, new Vec2(35, 0.1f)));
            t.AdvanceTo(At(2, 9));
            t.Act(PlayerInput.Nudge(Find(t.World, PropType.Cart).id, default));
            t.AdvanceTo(At(3, 19));
            t.Act(PlayerInput.Nudge(Find(t.World, PropType.Lantern).id, default));
            t.AdvanceTo(SimTime.Year * 2);
            foreach (var e in t.World.Ledger.Events)
            {
                foreach (var c in e.causes)
                {
                    Assert.Less(c, e.id);
                    Assert.LessOrEqual(t.World.Ledger.Get(c).time, e.time);
                }
                foreach (var r in e.roots) Assert.IsTrue(t.World.Ledger.Get(r).IsIntervention);
                if (e.IsIntervention) Assert.AreEqual(0, e.causes.Length);
            }
        }

        [Test]
        public void WithoutInterventionsNothingClaimsPlayerCause()
        {
            var w = new World(77);
            w.AdvanceTo(SimTime.Year * 3);
            Assert.IsTrue(w.Ledger.Events.All(e => e.roots.Length == 0));
        }

        [Test]
        public void PebbleTripsTheMerchantAndSpillsCargo()
        {
            ulong seed = 1207;
            int successes = 0;
            for (int attempt = 0, day = 1; attempt < 6; attempt++)
            {
                day = DayWithCargo(seed, ItemType.Gear, day);
                Assert.Greater(day, 0);
                var t = new Timeline(seed, ScenarioDef.Sandbox);
                t.AdvanceTo(At(day, 5));
                var nudge = t.Act(PlayerInput.Nudge(Pebble(t.World, 0).id, new Vec2(35, 0.15f)));
                Assert.IsTrue(nudge.ok, nudge.message);
                t.AdvanceTo(At(day, 12));
                var trip = t.World.Ledger.Events.FirstOrDefault(e => e.type == SimEventType.Trip && e.roots.Contains(nudge.eventId));
                if (trip != null)
                {
                    successes++;
                    var drop = t.World.Ledger.Events.FirstOrDefault(e => e.type == SimEventType.ItemDropped && e.causes.Contains(trip.id));
                    Assert.IsNotNull(drop, "a carrying merchant who trips must drop the cargo");
                    Assert.IsTrue(t.World.Props.Any(p => p.type == PropType.GroundItem && p.item == ItemType.Gear));

                    // counterfactual: the same day without the nudge has no such trip
                    var baseline = new World(seed);
                    baseline.AdvanceTo(At(day, 12));
                    Assert.IsFalse(baseline.Ledger.Events.Any(e => e.type == SimEventType.Trip), "the merchant must not trip without the pebble");
                    break;
                }
                day++;
            }
            Assert.Greater(successes, 0, "the merchant should trip on a pebble placed on his road");
        }

        [Test]
        public void EventsBeforeAnInterventionAreIdenticalAcrossTimelines()
        {
            var a = new Timeline(999, ScenarioDef.Sandbox);
            a.AdvanceTo(At(6, 10));
            int before = a.World.Ledger.Count;
            a.Act(PlayerInput.Nudge(Find(a.World, PropType.Cart).id, default));
            a.AdvanceTo(At(30, 0));
            var b = new World(999); b.AdvanceTo(At(30, 0));
            for (int i = 0; i < before; i++)
            {
                Assert.AreEqual(b.Ledger.Events[i].title, a.World.Ledger.Events[i].title);
                Assert.AreEqual(b.Ledger.Events[i].time, a.World.Ledger.Events[i].time);
            }
        }

        [Test]
        public void RunawayCartBreaksTheWellAndIsRepaired()
        {
            var t = new Timeline(3, ScenarioDef.Sandbox);
            t.AdvanceTo(At(2, 10));
            var r = t.Act(PlayerInput.Nudge(Find(t.World, PropType.Cart).id, default));
            Assert.IsTrue(r.ok);
            t.AdvanceTo(At(2, 11));
            var broke = FirstOfType(t.World, SimEventType.WellBroken);
            Assert.IsNotNull(broke);
            Assert.IsTrue(broke.roots.Contains(r.eventId));
            Assert.IsTrue(t.World.WellBroken);
            t.AdvanceTo(At(40, 0));
            Assert.IsFalse(t.World.WellBroken, "builders should repair the well");
            var rebuilt = t.World.Ledger.Events.FirstOrDefault(e => e.type == SimEventType.Construction && e.causes.Contains(broke.id));
            Assert.IsNotNull(rebuilt);
        }

        [Test]
        public void GustCarriesForgeEmbersIntoFire()
        {
            // the forge is lit while the smith works; a gust toward the haystack should eventually catch
            bool caught = false;
            for (int day = 1; day < 25 && !caught; day++)
            {
                if (SimTime.IsHolyDay(day * SimTime.Day)) continue;
                var t = new Timeline(42, ScenarioDef.Sandbox);
                t.AdvanceTo(At(day, 10));
                var forge = t.World.FirstBuilding(BuildingType.Forge);
                var hay = t.World.Buildings.First(b => b.type == BuildingType.Haystack);
                var dir = (hay.pos - forge.pos).Normalized;
                var r = t.Act(PlayerInput.GustAt(forge.pos, dir));
                Assert.IsTrue(r.ok, r.message);
                t.AdvanceTo(At(day, 13));
                var fire = t.World.Ledger.Events.FirstOrDefault(e => (e.type == SimEventType.FireStarted) && e.causes.Contains(r.eventId));
                if (fire != null) caught = true;
            }
            Assert.IsTrue(caught, "a gust aimed from a working forge at a haystack should start a fire on some day");
        }

        [Test]
        public void DistractingTheTinkererNearPlayingWrenCanStartAnApprenticeship()
        {
            var found = FindApprenticeshipMoment(ScenarioDef.ForgottenGenius, out var t, out int evId);
            Assert.IsTrue(found, "there should be an afternoon where Ambrose passes near Wren at play");
            var appr = t.World.Ledger.Events.First(e => e.type == SimEventType.Apprenticeship);
            Assert.IsTrue(appr.roots.Contains(evId));
            var wren = t.World.FindCitizen("Wren Ashdown");
            Assert.IsTrue(wren.HasMark("spark:engineering"));
        }

        public static bool FindApprenticeshipMoment(ScenarioDef sc, out Timeline t, out int eventId)
        {
            t = null; eventId = -1;
            for (int day = 0; day < 40; day++)
            {
                if (SimTime.IsHolyDay(day * SimTime.Day)) continue;
                var probe = new World(sc.fixedSeed, sc);
                var amb = probe.FindCitizen("Ambrose Pell");
                var wren = probe.FindCitizen("Wren Ashdown");
                for (double h = 14.9; h < 16.0; h += 1 / 60.0)
                {
                    probe.AdvanceTo(At(day, h));
                    if (!probe.IsAwakeOutside(amb) || !probe.IsAwakeOutside(wren)) continue;
                    if (wren.activity.kind == ActivityKind.Travel || wren.activity.kind != ActivityKind.Play) continue;
                    if (Vec2.Distance(probe.PositionNow(amb), probe.PositionNow(wren)) > 9.5f) continue;
                    var tl = new Timeline(sc.fixedSeed, sc);
                    tl.AdvanceTo(probe.Time);
                    var r = tl.Act(PlayerInput.DistractCitizen(amb.id));
                    if (!r.ok) continue;
                    if (tl.World.Ledger.Events.Any(e => e.type == SimEventType.Apprenticeship)) { t = tl; eventId = r.eventId; return true; }
                }
            }
            return false;
        }
    }

    public class ChallengeTests
    {
        [Test]
        public void ForgottenGeniusBaselineFails()
        {
            var sc = ScenarioDef.ForgottenGenius;
            var w = new World(sc.fixedSeed, sc);
            w.AdvanceTo((sc.deadlineYear - 1) * SimTime.Year + SimTime.Day);
            var s = Challenges.Evaluate(w);
            Assert.AreEqual(ChallengeState.Failed, s.state, "left alone, Wren should never invent anything: " + s.headline);
        }

        [Test]
        public void ForgottenGeniusIsWinnableWithOneIntervention()
        {
            Assert.IsTrue(CausalityTests.FindApprenticeshipMoment(ScenarioDef.ForgottenGenius, out var t, out _));
            var sc = ScenarioDef.ForgottenGenius;
            ChallengeStatus s = null;
            for (int y = 1; y < sc.deadlineYear; y++)
            {
                t.AdvanceTo(y * SimTime.Year);
                s = Challenges.Evaluate(t.World);
                if (s.state != ChallengeState.InProgress) break;
            }
            Assert.AreEqual(ChallengeState.Won, s.state, s.headline);
            Assert.AreEqual(3, s.stars);
            var win = t.World.Ledger.Get(s.winningEventId);
            Assert.Greater(win.roots.Length, 0, "the invention must trace back to the player's act");
        }

        [Test]
        public void InterventionCapIsEnforced()
        {
            var sc = ScenarioDef.OnePebble;
            var t = new Timeline(sc.fixedSeed, sc);
            t.AdvanceTo(At(1, 8));
            Assert.IsTrue(t.Act(PlayerInput.Nudge(Pebble(t.World, 0).id, new Vec2(35, 0.2f))).ok);
            var second = t.Act(PlayerInput.Nudge(Pebble(t.World, 1).id, new Vec2(-8, -0.2f)));
            Assert.IsFalse(second.ok);
            Assert.AreEqual(1, t.Inputs.Count);
        }

        [Test]
        public void SecondChanceRewindsLastIntervention()
        {
            var t = new Timeline(8, ScenarioDef.Sandbox);
            t.AdvanceTo(At(1, 8));
            t.Act(PlayerInput.Nudge(Pebble(t.World, 0).id, new Vec2(35, 0.2f)));
            t.AdvanceTo(At(2, 8));
            t.Act(PlayerInput.Nudge(Find(t.World, PropType.Cart).id, default));
            var rewound = t.RewindLastIntervention();
            Assert.AreEqual(1, rewound.Inputs.Count);
            Assert.AreEqual(1, rewound.World.InterventionCount);
        }
    }

    public class LivingWorldTests
    {
        [Test]
        public void VillagersFollowDailyRoutines()
        {
            var w = new World(10);
            w.AdvanceTo(At(1, 3));
            int asleep = w.Citizens.Count(c => c.IsActive && !c.offMapResident && c.activity?.kind == ActivityKind.Sleep);
            Assert.Greater(asleep, w.Population * 0.8, "most people sleep at 3am");
            w.AdvanceTo(At(1, 10.5));
            int working = w.Citizens.Count(c => c.IsActive && c.activity != null && (c.activity.kind == ActivityKind.Work || c.activity.kind == ActivityKind.Play || c.activity.kind == ActivityKind.Trade || c.activity.kind == ActivityKind.Travel));
            Assert.Greater(working, w.Population * 0.5, "most people are out and about at mid-morning");
        }

        [Test]
        public void PopulationAndEconomyStayPlausibleOverDecades()
        {
            var w = new World(2468);
            w.AdvanceTo(30 * SimTime.Year);
            Assert.Greater(w.Population, 10, "the vale should not collapse on its own");
            Assert.Less(w.Population, 400);
            Assert.GreaterOrEqual(w.Food, 0);
            Assert.Greater(CountType(w, SimEventType.Birth), 3);
            Assert.Greater(CountType(w, SimEventType.Death), 1);
            Assert.Greater(CountType(w, SimEventType.Election), 0);
        }

        [Test]
        public void PebblesStartOffTheRoads()
        {
            var w = new World(1);
            foreach (var p in w.Props.Where(p => p.type == PropType.Pebble))
                foreach (var n in w.Nav.nodes)
                    foreach (var l in n.links)
                        Assert.Greater(Vec2.DistanceToSegment(p.pos, n.pos, w.Nav.Node(l).pos, out _), 1.3f, $"pebble at {p.pos} sits on a road");
        }

        [Test]
        public void ChildrenComeOfAgeIntoTrades()
        {
            var w = new World(13579);
            w.AdvanceTo(8 * SimTime.Year);
            var wren = w.FindCitizen("Wren Ashdown");
            Assert.AreNotEqual(Occupation.Child, wren.occupation);
        }
    }
}

namespace GOA.Tests
{
    public class PacingTests
    {
        [TestCase(1UL)]
        [TestCase(2026UL)]
        [TestCase(98765UL)]
        public void FirstChainReactionWithinTheFirstMinuteOfPlay(ulong seed)
        {
            // one real minute at "Living" speed (120x) is two in-game hours
            var w = new World(seed);
            w.AdvanceTo(World.StartTime + 2 * SimTime.Hour);
            var chained = w.Ledger.Events.FirstOrDefault(e => e.causes.Length > 0 && e.magnitude >= 2);
            Assert.IsNotNull(chained, "no chain reaction in the first two in-game hours");
            Assert.IsTrue(w.Ledger.Events.All(e => e.roots.Length == 0), "ambient life must never claim the player as a cause");
        }

        [Test]
        public void TheWorldLivesWithoutThePlayer()
        {
            var w = new World(4321);
            w.AdvanceTo(World.StartTime + SimTime.Day);
            int mishaps = w.Ledger.Events.Count(e => e.type == SimEventType.Mishap || (e.type == SimEventType.ItemFound && e.causes.Length == 0));
            Assert.GreaterOrEqual(mishaps, 4, "a day should hold several natural mishaps");
            Assert.GreaterOrEqual(w.Chronicle.Count, 6, "the Chronicle should fill up without any input");
        }

        [Test]
        public void WorldsStartWithFullEnergyAndRewardsNeverOverfill()
        {
            var w = new World(5, ScenarioDef.Sandbox);
            Assert.AreEqual(ScenarioDef.Sandbox.maxEnergy, w.Energy, 1e-4);
            w.Apply(PlayerInput.Grant(5));
            Assert.LessOrEqual(w.Energy, ScenarioDef.Sandbox.maxEnergy + 1e-4f);
        }

        [Test]
        public void OpportunityQueryDoesNotChangeTheWorld()
        {
            var a = new World(77); var b = new World(77);
            for (int h = 1; h <= 30; h++)
            {
                a.AdvanceTo(World.StartTime + h * SimTime.Hour);
                a.FindOpportunities(8);
            }
            b.AdvanceTo(World.StartTime + 30 * SimTime.Hour);
            Assert.AreEqual(b.StateHash(), a.StateHash());
        }

        [Test]
        public void TapNudgeTargetIsReachableAndPure()
        {
            var a = new World(31); var b = new World(31);
            a.AdvanceTo(World.StartTime + 3 * SimTime.Hour); b.AdvanceTo(World.StartTime + 3 * SimTime.Hour);
            foreach (var p in a.Props)
            {
                if (!p.active || (p.type != PropType.Pebble && p.type != PropType.GroundItem)) continue;
                var to = a.SuggestNudgeTarget(p.id);
                Assert.LessOrEqual(Vec2.Distance(to, p.pos), World.NudgeRange + 0.01f);
                Assert.IsTrue(a.Validate(PlayerInput.Nudge(p.id, to)).ok, "a tap-nudge must always be a legal nudge");
            }
            Assert.AreEqual(b.StateHash(), a.StateHash());
        }

        [Test]
        public void SandboxGoalsGuideButNeverEndTheGame()
        {
            var w = new World(8, ScenarioDef.Sandbox);
            w.AdvanceTo(World.StartTime + 2 * SimTime.Day);
            var s = Challenges.Evaluate(w);
            Assert.AreEqual(ChallengeState.None, s.state);
            Assert.GreaterOrEqual(s.steps.Count, 4);
            Assert.IsFalse(s.steps[0].done, "no power used yet");
        }

        [TestCase(1UL)]
        [TestCase(2026UL)]
        [TestCase(12251UL)]
        public void OpportunitiesShowUpInTheOpeningHours(ulong seed)
        {
            var w = new World(seed);
            int withHints = 0, samples = 0;
            for (long t = World.StartTime + 20 * SimTime.Minute; t <= World.StartTime + 4 * SimTime.Hour; t += 10 * SimTime.Minute)
            {
                w.AdvanceTo(t);
                samples++;
                if (w.FindOpportunities(4).Count > 0) withHints++;
            }
            Assert.GreaterOrEqual(withHints, samples / 3, "markers should be on screen for much of the first minutes of play");
        }

        [TestCase(1UL)]
        [TestCase(77UL)]
        [TestCase(4321UL)]
        public void LivingVillagersNeverShareAFirstName(ulong seed)
        {
            var w = new World(seed);
            for (int y = 0; y <= 12; y += 3)
            {
                w.AdvanceTo(World.StartTime + y * SimTime.Year);
                var names = w.Citizens.Where(c => c.IsActive).Select(c => c.firstName).ToList();
                Assert.AreEqual(names.Count, names.Distinct().Count(), $"duplicate first names in year {y}: " + string.Join(", ", names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
            }
        }

        [TestCase(1UL)]
        [TestCase(2026UL)]
        [TestCase(12251UL)]
        [TestCase(98765UL)]
        public void NoNaturalFiresInTheFirstDays(ulong seed)
        {
            var w = new World(seed);
            w.AdvanceTo(World.StartTime + 3 * SimTime.Day);
            Assert.IsFalse(w.Ledger.Events.Any(e => e.type == SimEventType.FireStarted), "a fresh world should not catch fire on its own");
        }

        [Test]
        public void ForgottenGeniusShowsAnEarlyFirstStep()
        {
            var w = new World(ScenarioDef.ForgottenGenius.fixedSeed, ScenarioDef.ForgottenGenius);
            var s = Challenges.Evaluate(w);
            Assert.AreEqual("Catch Wren's eye with something new", s.steps[0].text);
            StringAssert.Contains("Ambrose", s.headline);
        }
    }
}
