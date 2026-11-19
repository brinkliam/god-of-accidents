using System;
using System.Collections.Generic;
using System.Linq;
using GOA.Services;
using GOA.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    public sealed partial class UIRoot
    {
        static readonly (PowerType p, string icon, string name)[] PowerDefs =
        {
            (PowerType.Nudge, "nudge", "Nudge"), (PowerType.Gust, "gust", "Gust"), (PowerType.Distract, "distract", "Distract"), (PowerType.Drop, "drop", "Drop"),
        };

        static readonly string[] LockedPowers = { "Delay", "Redirect", "Coincidence", "Probability Shift", "Fate Fracture" };

        void BuildHud()
        {
            // ---------- top bar
            var top = new VisualElement(); top.AddToClassList("topbar"); top.pickingMode = PickingMode.Ignore;
            var chips = new VisualElement(); chips.AddToClassList("chips"); chips.pickingMode = PickingMode.Ignore;
            var eraChip = Chip(out _era, "era"); eraChip.style.flexDirection = FlexDirection.Column; eraChip.style.alignItems = Align.FlexStart;
            _clock = L("", "clock"); eraChip.Add(_clock);
            chips.Add(eraChip);
            chips.Add(Chip(out _weather));
            chips.Add(Chip(out _pop));
            chips.Add(Chip(out _food));
            chips.Add(Chip(out _prosperity));
            top.Add(chips);

            var right = new VisualElement(); right.AddToClassList("timebar");
            var tb = Panel("panel-soft"); tb.style.flexDirection = FlexDirection.Row; tb.style.alignItems = Align.Center;
            string[] icons = { "pause", "play1", "play2", "play3", "play4", "ages" };
            for (int i = 0; i < icons.Length; i++)
            {
                int idx = i;
                var b = IconBtn(icons[i], () => S.SetSpeed(idx), GameSession.SpeedNames[i] + $"  (F{i + 1})", 16, "timebtn");
                _speedButtons.Add((b, i));
                tb.Add(b);
            }
            _speedName = L("", "small"); _speedName.style.width = 64; _speedName.style.marginLeft = 4;
            tb.Add(_speedName);
            right.Add(tb);
            right.Add(IconBtn("timeline", OpenTimeline, "Timeline of causes  (T)", 20, "timebtn"));
            right.Add(IconBtn("book", ShowCodex, "Codex of accidents", 20, "timebtn"));
            right.Add(IconBtn("menu", ShowPause, "Menu  (Esc)", 18, "timebtn"));
            top.Add(right);
            _hud.Add(top);

            // ---------- objective
            var leftCol = new VisualElement { pickingMode = PickingMode.Ignore };
            leftCol.AddToClassList("leftcol");
            _hud.Add(leftCol);
            _objective = Panel("objective");
            _objHead = L("", "small");
            _objSteps = new VisualElement();
            var objTitle = L("", "h2"); objTitle.name = "objTitle";
            _objective.Add(objTitle);
            _objective.Add(_objHead);
            _objective.Add(_objSteps);
            leftCol.Add(_objective);

            // ---------- power bar
            var bar = new VisualElement(); bar.AddToClassList("powerbar"); bar.pickingMode = PickingMode.Ignore;
            var energy = Panel("energy");
            var er = Row(new UIIcon("energy", 16), L("Divine energy", "small"));
            energy.Add(er);
            var track = new VisualElement(); track.AddToClassList("energy-track");
            _energyFill = new VisualElement(); _energyFill.AddToClassList("energy-fill");
            track.Add(_energyFill);
            energy.Add(track);
            _energyText = L("", "tiny");
            energy.Add(_energyText);
            _energyAdBtn = Btn("+3 energy  (watch ad)", () => Rewarded(Placements.ExtraEnergy, () => S.GrantEnergy(3)), "btn-ad");
            _energyAdBtn.style.fontSize = 12; _energyAdBtn.style.marginTop = 6;
            energy.Add(_energyAdBtn);
            _glimpseBtn = Btn("Glimpse the future  (ad)", () => Rewarded(Placements.GlimpseOfFuture, () => { S.GlimpseArmed = true; Toast("Your next intervention will be foreseen before it happens."); }), "btn-ad");
            _glimpseBtn.style.fontSize = 12;
            energy.Add(_glimpseBtn);
            bar.Add(energy);

            foreach (var (p, icon, name) in PowerDefs)
            {
                var pb = new VisualElement(); pb.AddToClassList("power");
                pb.Add(new UIIcon(icon, 34));
                pb.Add(L(name, "power-name"));
                var cost = new VisualElement(); cost.AddToClassList("power-cost");
                for (int d = 0; d < (int)World.CostOf(p); d++) { var dot = new VisualElement(); dot.AddToClassList("power-cost-dot"); cost.Add(dot); }
                pb.Add(cost);
                var pp = p;
                pb.RegisterCallback<ClickEvent>(_ => { Audio?.Ui("select"); PC.Select(pp); RefreshPowers(); });
                pb.RegisterCallback<PointerEnterEvent>(_ => Audio?.Ui("hover"));
                pb.tooltip = PowerController.HintFor(p);
                _powerButtons[p] = pb;
                bar.Add(pb);
            }
            var locked = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 14, alignItems = Align.FlexEnd } };
            foreach (var n in LockedPowers)
            {
                var lb = new VisualElement(); lb.AddToClassList("power"); lb.AddToClassList("power-locked");
                lb.Add(new UIIcon("lock", 18));
                lb.tooltip = n + " - not yet awakened (coming in a later age)";
                locked.Add(lb);
            }
            bar.Add(locked);
            _hud.Add(bar);

            // ---------- chronicle
            _chronicle = Panel("chronicle");
            _chronicle.Add(L("CHRONICLE", "h2"));
            _chronicleList = new VisualElement();
            _chronicle.Add(_chronicleList);
            leftCol.Add(_chronicle);

            // ---------- inspector
            _inspector = Panel("inspector");
            var head = Row(Spacer(), IconBtn("close", CloseInspector, "Close", 14, "btn-ghost"));
            _inspector.Add(head);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            _inspectorBody = new VisualElement();
            scroll.Add(_inspectorBody);
            _inspector.Add(scroll);
            Show(_inspector, false);
            _hud.Add(_inspector);

            // ---------- replay bar
            _replayBar = Panel();
            _replayBar.style.position = Position.Absolute; _replayBar.style.top = 70; _replayBar.style.left = new Length(30, LengthUnit.Percent); _replayBar.style.right = new Length(30, LengthUnit.Percent);
            _replayBar.style.flexDirection = FlexDirection.Row; _replayBar.style.alignItems = Align.Center;
            _replayLabel = L("REPLAY", "gold");
            _replayBar.Add(_replayLabel);
            _replayBar.Add(Spacer());
            _replayBar.Add(Btn("Return to the present", () => S.ExitReplay(), "btn-primary"));
            Show(_replayBar, false);
            _hud.Add(_replayBar);

            _floatName = L("", "tag");
            _floatName.style.position = Position.Absolute;
            _floatName.style.width = 80; _floatName.style.unityTextAlign = TextAnchor.MiddleCenter;
            _floatName.pickingMode = PickingMode.Ignore;
            _floating.Add(_floatName);
        }

        VisualElement Chip(out Label label, string cls = null)
        {
            var c = new VisualElement(); c.AddToClassList("chip");
            label = L("", cls ?? "small");
            c.Add(label);
            return c;
        }

        void RefreshHud()
        {
            var w = S.World;
            if (w == null) return;
            long t = (long)S.DisplayTime;
            _era.text = World.EraName(w.Era).ToUpperInvariant();
            int hr = (int)SimTime.HourOfDay(t), mn = (int)((t % SimTime.Hour) / SimTime.Minute);
            _clock.text = $"Year {SimTime.Year1(t)} · {SimTime.SeasonOf(t)} {SimTime.DayOfSeason(t)} · {hr:00}:{mn:00}";
            _weather.text = w.Weather + (w.WindStrength > 0.7f ? " · windy" : "");
            _pop.text = $"{w.Population} souls";
            _food.text = $"Food {w.DaysOfFood:0} days" + (w.FoodPrice > 2 ? " · bread dear" : "");
            _prosperity.text = $"Prosperity {(int)(w.Prosperity * 100)}%";
            foreach (var (b, idx) in _speedButtons) b.EnableInClassList("btn-on", idx == S.SpeedIndex);
            _speedName.text = GameSession.SpeedNames[S.SpeedIndex];
            float max = w.Scenario.maxEnergy;
            _energyFill.style.width = new Length(Mathf.Clamp01(w.Energy / Mathf.Max(1, max)) * 100, LengthUnit.Percent);
            string cap = w.Scenario.maxInterventions > 0 ? $" · interventions {w.InterventionCount}/{w.Scenario.maxInterventions}" : "";
            _energyText.text = $"{w.Energy:0.0} / {max:0}{cap}";
            _energyAdBtn.EnableInClassList("btn-disabled", !Svc.Ads.CanShow(Placements.ExtraEnergy, out _));
            _glimpseBtn.text = S.GlimpseArmed ? "Glimpse ready - act to foresee" : "Glimpse the future  (ad)";
            foreach (var kv in _powerButtons)
                kv.Value.EnableInClassList("btn-disabled", w.Energy + 1e-3f < World.CostOf(kv.Key) || (w.Scenario.maxInterventions > 0 && w.InterventionCount >= w.Scenario.maxInterventions));

            if (S.Timeline != null && S.Timeline.Scenario.isChallenge)
            {
                var c = S.Challenge;
                _objective.Q<Label>("objTitle").text = S.Timeline.Scenario.title.ToUpperInvariant();
                _objHead.text = c.headline;
                _objSteps.Clear();
                foreach (var (text, done) in c.steps)
                {
                    var ic = new UIIcon(done ? "check" : "dotEmpty", 14) { Tint = done ? new Color(0.6f, 0.95f, 0.55f) : new Color(1, 1, 1, 0.5f) };
                    _objSteps.Add(Row(ic, L(" " + text, done ? "small" : "tiny")));
                }
            }
        }

        void RefreshPowers()
        {
            foreach (var kv in _powerButtons) kv.Value.EnableInClassList("power-on", PC.Active == kv.Key);
        }

        // ------------------------------------------------------------------ inspector

        void CloseInspector()
        {
            Show(_inspector, false);
            _inspecting = default;
            _inspectEvent = null;
            if (S?.View != null) S.View.Selected = default;
            Rig.Follow = null;
            ClearTrail();
        }

        void Inspect(PickResult pick)
        {
            if (pick.kind == PickKind.None || pick.kind == PickKind.Ground) { CloseInspector(); return; }
            _inspecting = pick;
            _inspectEvent = null;
            S.View.Selected = pick;
            Audio?.Ui("select");
            Show(_inspector, true);
            ClearTrail();
            if (pick.kind == PickKind.Citizen)
            {
                int id = pick.id;
                Rig.Follow = () => S.View.PositionOf(PickKind.Citizen, id, S.DisplayTime);
                if (Rig.Distance > 60) Rig.Distance = 40;
            }
            RefreshInspector();
        }

        void RefreshInspector()
        {
            if (_inspectEvent != null) return; // event pages are static
            var w = S.World;
            _inspectorBody.Clear();
            switch (_inspecting.kind)
            {
                case PickKind.Citizen: CitizenPage(w, w.GetCitizen(_inspecting.id)); break;
                case PickKind.Building: BuildingPage(w, w.GetBuilding(_inspecting.id)); break;
                case PickKind.Prop: PropPage(w, w.GetProp(_inspecting.id)); break;
            }
        }

        static string TraitList(Trait t)
        {
            var l = new List<string>();
            foreach (Trait x in Enum.GetValues(typeof(Trait))) if (x != Trait.None && (t & x) != 0) l.Add(x.ToString());
            return l.Count == 0 ? "Ordinary" : string.Join(", ", l);
        }

        static string Doing(World w, Citizen c)
        {
            var a = c.activity;
            if (a == null) return "-";
            string place = a.nodeId >= 0 ? w.Nav.Node(a.nodeId).name : "";
            if (place.StartsWith("Door_")) place = "home";
            if (place.StartsWith("J_")) place = "the lanes";
            return a.kind switch
            {
                ActivityKind.Travel => "Walking to " + place,
                ActivityKind.Sleep => "Asleep",
                ActivityKind.Work => "Working at " + place,
                ActivityKind.Play => "Playing on the green",
                ActivityKind.Distracted => "Lost in wonder at a glimmer",
                ActivityKind.FightFire => "Fighting a fire!",
                ActivityKind.OffMap => "Away beyond the pass",
                ActivityKind.Trade => "Trading at the market",
                ActivityKind.Socialize => "Chatting at " + place,
                ActivityKind.Study => "Learning at " + place,
                ActivityKind.Build => "Building",
                ActivityKind.Pray => "Praying at the chapel",
                ActivityKind.Recover => "Recovering at home",
                ActivityKind.Fetch => "Carrying water from the river",
                ActivityKind.Festival => "At the festival",
                ActivityKind.Eat => "Eating" + (place == "Tavern" ? " at the tavern" : ""),
                _ => "Idling at " + place
            };
        }

        void Bar(VisualElement parent, string label, float v, Color c)
        {
            parent.Add(L($"{label}  {(int)(v * 100)}", "tiny"));
            var b = new VisualElement(); b.AddToClassList("bar");
            var f = new VisualElement(); f.AddToClassList("bar-fill"); f.style.width = new Length(Mathf.Clamp01(v) * 100, LengthUnit.Percent); f.style.backgroundColor = c;
            b.Add(f);
            parent.Add(b);
        }

        void CitizenPage(World w, Citizen c)
        {
            if (c == null) return;
            var b = _inspectorBody;
            b.Add(L(c.Name, "h1"));
            int age = c.AgeYears(w.Time);
            b.Add(L($"{World.OccupationName(c.occupation)} · {age} years{(c.alive ? "" : " · died")}{(c.id == w.ElderId ? " · ELDER" : "")}", "gold"));
            b.Add(L(Doing(w, c), "small"));
            if (c.offMapResident && c.cargo != "") b.Add(L($"Carrying today: {c.cargo}", "small"));
            if (!string.IsNullOrEmpty(c.goal)) b.Add(L("Dreams of: " + c.goal, "small"));
            var tags = new VisualElement(); tags.AddToClassList("row"); tags.AddToClassList("wrap");
            foreach (var part in TraitList(c.traits).Split(',')) tags.Add(L(part.Trim(), "tag"));
            foreach (var m in c.marks.Keys) if (m.StartsWith("spark:")) tags.Add(L("Spark: " + m.Substring(6), "tag"));
            b.Add(tags);
            b.Add(L("NEEDS & SKILLS", "h2"));
            Bar(b, "Mood", c.mood, new Color(0.95f, 0.78f, 0.4f));
            Bar(b, "Craft", c.craft / 100f, new Color(0.55f, 0.75f, 0.95f));
            Bar(b, "Lore", c.lore / 100f, new Color(0.75f, 0.6f, 0.95f));
            b.Add(L($"Wealth {c.wealth} coins · Renown {c.fame}", "tiny"));
            b.Add(L("PEOPLE", "h2"));
            foreach (var r in c.relations.Values.OrderByDescending(r => Math.Abs(r.affinity)).Take(6))
            {
                var o = w.GetCitizen(r.otherId);
                if (o == null) continue;
                var row = Row(L($"{o.Name}", "small"), Spacer(), L(r.kind.ToString(), "tiny"));
                row.AddToClassList("chron-row");
                int oid = o.id;
                row.RegisterCallback<ClickEvent>(_ => Inspect(new PickResult { kind = PickKind.Citizen, id = oid }));
                b.Add(row);
            }
            b.Add(L("MEMORIES", "h2"));
            foreach (var m in Enumerable.Reverse(c.memories).Take(8))
            {
                var row = Row(L(SimTime.FormatShort(m.time), "tiny"), L("  " + m.text, "small"));
                row.AddToClassList("chron-row");
                var ev = w.Ledger.Get(m.eventId);
                if (ev != null) row.RegisterCallback<ClickEvent>(_ => ShowEvent(ev));
                b.Add(row);
            }
            if (c.memories.Count == 0) b.Add(L("Nothing remarkable has happened to them. Yet.", "tiny"));
            b.Add(Btn("Follow with the camera", () => { int id = c.id; Rig.Follow = () => S.View.PositionOf(PickKind.Citizen, id, S.DisplayTime); Rig.Distance = 22; }, "btn-ghost"));
        }

        void BuildingPage(World w, Building bl)
        {
            if (bl == null) return;
            var b = _inspectorBody;
            b.Add(L(bl.name, "h1"));
            string state = bl.destroyed ? "Ruined" : bl.buildProgress < 1 ? $"Under construction · {(int)(bl.buildProgress * 100)}%" : "Standing";
            b.Add(L($"{bl.type} · {state}", "gold"));
            string[] tiers = { "Thatch and wattle", "Timber frame", "Dressed stone", "Brick", "Industrial brick" };
            if (bl.type == BuildingType.House) b.Add(L("Architecture: " + tiers[Mathf.Clamp(bl.tier, 0, 4)], "small"));
            var fire = w.FireAt(bl.id);
            if (fire != null) b.Add(L($"ON FIRE · intensity {(int)(fire.intensity * 100)}% · {fire.firefighters} with buckets", "gold"));
            if (bl.flammable && !(bl.type == BuildingType.House && bl.tier >= 2)) b.Add(L("Flammable - embers on the wind could catch here.", "tiny"));
            if (bl.type == BuildingType.House)
            {
                b.Add(L("RESIDENTS", "h2"));
                foreach (var c in w.Citizens.Where(c => c.IsActive && c.homeBuildingId == bl.id))
                {
                    var row = Row(L(c.Name, "small"), Spacer(), L(World.OccupationName(c.occupation), "tiny"));
                    row.AddToClassList("chron-row");
                    int id = c.id;
                    row.RegisterCallback<ClickEvent>(_ => Inspect(new PickResult { kind = PickKind.Citizen, id = id }));
                    b.Add(row);
                }
            }
            if (bl.causeEventId >= 0)
            {
                var ev = w.Ledger.Get(bl.causeEventId);
                if (ev != null) { b.Add(L("WHY IT EXISTS", "h2")); b.Add(Btn(ev.title, () => ShowEvent(ev), "btn-ghost")); }
            }
            if (bl.type == BuildingType.Granary) b.Add(L($"Stores: {w.Food:0} portions ({w.DaysOfFood:0} days)", "small"));
        }

        void PropPage(World w, Prop p)
        {
            if (p == null) return;
            var b = _inspectorBody;
            b.Add(L(p.label.Length > 0 ? char.ToUpper(p.label[0]) + p.label.Substring(1) : p.type.ToString(), "h1"));
            string hint = p.type switch
            {
                PropType.Pebble => "A small stone by the road. Nudge it into someone's path and see who stumbles.",
                PropType.Lantern => p.state == 0 ? "Lit from dusk until after midnight. A nudge would bring it down - burning, if it's lit." : "It has fallen.",
                PropType.Cart => p.state == 0 ? "Parked on the slope above the chapel lane, held by one wooden chock." : "It has already rolled.",
                PropType.GroundItem => "Dropped and waiting to be found. Who finds it - and what they are like - decides what happens next.",
                _ => ""
            };
            b.Add(L(hint, "small"));
            if (p.causeEventId >= 0)
            {
                var ev = w.Ledger.Get(p.causeEventId);
                if (ev != null) { b.Add(L("HOW IT GOT HERE", "h2")); b.Add(Btn(ev.title, () => ShowEvent(ev), "btn-ghost")); }
            }
            if (p.nudgeable) b.Add(Btn("Nudge it", () => { PC.Select(PowerType.Nudge); RefreshPowers(); }, "btn-primary"));
        }

        /// <summary>The causal chain of an event, as an inspector page plus a glowing trail in the world.</summary>
        void ShowEvent(SimEvent e)
        {
            if (e == null) return;
            var w = S.World;
            _inspectEvent = e;
            _inspecting = default;
            S.View.Selected = default;
            Show(_inspector, true);
            var b = _inspectorBody;
            b.Clear();
            b.Add(L(e.title, "h1"));
            b.Add(L(Fmt(e.time), "gold"));
            b.Add(L(e.detail, "small"));
            if (e.roots.Length > 0) b.Add(L($"Traces back to {e.roots.Length} of your intervention{(e.roots.Length == 1 ? "" : "s")} · {e.depth} links deep", "tag"));
            else if (!e.IsIntervention) b.Add(L("This happened without your touch.", "tiny"));

            var chain = w.Ledger.PrimaryChain(e.id);
            b.Add(L("HOW IT CAME TO PASS", "h2"));
            foreach (var c in chain)
            {
                var node = new VisualElement(); node.AddToClassList("chain-node");
                if (c.IsIntervention) node.AddToClassList("chain-root");
                node.Add(L(c.title, c.id == e.id ? "gold" : "small"));
                node.Add(L(SimTime.FormatShort(c.time), "tiny"));
                var cc = c;
                node.RegisterCallback<ClickEvent>(_ => { if (cc.id != e.id) ShowEvent(cc); });
                b.Add(node);
            }
            var desc = w.Ledger.Descendants(e.id).Where(d => d.magnitude >= 2).Take(6).ToList();
            if (desc.Count > 0)
            {
                b.Add(L("WHAT IT LED TO", "h2"));
                foreach (var d in desc)
                {
                    var dd = d;
                    var row = Row(L(d.title, "small"), Spacer(), L(SimTime.FormatShort(d.time), "tiny"));
                    row.AddToClassList("chron-row");
                    row.RegisterCallback<ClickEvent>(_ => ShowEvent(dd));
                    b.Add(row);
                }
            }
            int fullAncestry = w.Ledger.Ancestry(e.id).Count;
            var actions = new VisualElement(); actions.AddToClassList("row"); actions.AddToClassList("wrap");
            if (e.hasPos) actions.Add(Btn("Show me", () => Rig.FocusOn(TerrainBuilder.Ground(e.pos), 28, 1.4f), "btn-ghost"));
            if (S.Mode == SessionMode.Playing) actions.Add(Btn("Replay", () => { S.EnterReplay(e); }, "btn-ghost"));
            if (S.Mode == SessionMode.Playing) actions.Add(Btn("Branch from before", () => S.Branch(Math.Max(World.StartTime, e.time - SimTime.Hour)), "btn-ghost"));
            if (fullAncestry > chain.Count - 1 || e.roots.Length > 0)
                actions.Add(Btn("Historical Insight (ad)", () => Rewarded(Placements.HistoricalInsight, () => Insight(e)), "btn-ad"));
            b.Add(actions);
            ShowTrail(chain);
            S.Timeline?.AddBookmark(e);
        }

        /// <summary>Rewarded: the complete web of causes, and an honest counterfactual check.</summary>
        void Insight(SimEvent e)
        {
            var w = S.World;
            var b = _inspectorBody;
            b.Add(L("HISTORICAL INSIGHT", "h2"));
            var anc = w.Ledger.Ancestry(e.id);
            b.Add(L($"{anc.Count} earlier events fed into this one:", "small"));
            foreach (var a in anc.Take(16)) b.Add(L($"· {SimTime.FormatShort(a.time)}  {a.title}", "tiny"));
            if (e.roots.Length > 0 && S.Timeline != null)
            {
                var root = w.Ledger.Get(e.roots[0]);
                var without = new List<PlayerInput>();
                foreach (var i in S.Timeline.Inputs) if (Math.Abs(i.time - root.time) > 0 || i.kind != InputKind.Power) without.Add(i);
                var alt = Timeline.Replay(S.Timeline.Seed, S.Timeline.Scenario, without, Math.Min(w.Time, e.time + SimTime.Day));
                bool stillHappens = alt.Ledger.Events.Any(x => x.type == e.type && x.title == e.title);
                b.Add(L(stillHappens
                    ? $"Tested against a world where you never did '{root.title}': this would have happened anyway."
                    : $"Tested against a world where you never did '{root.title}': this never happens. It is truly your doing.", "gold"));
            }
        }

        // ------------------------------------------------------------------ rewarded ads

        void Rewarded(string placement, Action grant)
        {
            if (S.Timeline == null && placement != Placements.BonusWorld) return;
            Svc.Ads.ShowRewarded(placement, g => grant(), (res, why) =>
            {
                if (res == AdResult.Unavailable) Toast("No ad is available right now - try again later. (" + why + ")");
                else if (res == AdResult.Blocked) Toast("Not available right now: " + why);
                else if (res == AdResult.Skipped) Toast("The ad was closed early, so no reward this time.");
                else if (res == AdResult.Failed) Toast("The ad failed to play. No reward was lost.");
            });
        }
    }
}
