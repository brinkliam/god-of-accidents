using System;
using System.Linq;
using GOA.Services;
using GOA.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    public sealed partial class UIRoot
    {
        // ------------------------------------------------------------------ main menu

        void BuildMenu()
        {
            var screen = new VisualElement(); screen.AddToClassList("screen"); screen.AddToClassList("vignette");
            screen.Add(L("GOD OF ACCIDENTS", "title-xl"));
            screen.Add(L("THE SMALLEST ACCIDENT CAN CHANGE THE FATE OF A CIVILISATION", "title-sub"));
            var col = new VisualElement(); col.AddToClassList("menu-col");
            var cont = Btn("CONTINUE", () => Veil("Remembering your world...", () => { if (S.Load()) Audio?.Ui("begin"); }), "btn-menu", "btn-primary");
            cont.name = "continue";
            col.Add(cont);
            col.Add(Btn("NEW WORLD", ShowNewWorld, "btn-menu"));
            col.Add(Btn("CHALLENGES", ShowChallenges, "btn-menu"));
            col.Add(Btn("CODEX", ShowCodex, "btn-menu"));
            col.Add(Btn("SETTINGS", ShowSettings, "btn-menu"));
            screen.Add(col);
            var foot = L($"v{Application.version} · Thistlewick Vale · ads: {Svc.Ads.ProviderName}", "footer");
            screen.Add(foot);
            _menu.Add(screen);
            _menu.RegisterCallback<GeometryChangedEvent>(_ => Show(cont, S.HasSave));
        }

        VisualElement Modal(string title, out VisualElement body, Action onClose = null, float width = 620)
        {
            S.SetModal(true);
            var dim = new VisualElement(); dim.AddToClassList("screen"); dim.AddToClassList("dim");
            var m = Panel("modal");
            m.style.width = width;
            var head = Row(L(title, "h1"), Spacer(), IconBtn("close", () => { CloseModal(dim); onClose?.Invoke(); }, "Close", 14, "btn-ghost"));
            head.AddToClassList("modal-head");
            m.Add(head);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            body = new VisualElement();
            scroll.Add(body);
            scroll.AddToClassList("modal-scroll");
            m.Add(scroll);
            dim.Add(m);
            _modal.Add(dim);
            return dim;
        }

        void CloseModal(VisualElement dim)
        {
            dim.RemoveFromHierarchy();
            if (_modal.childCount == 0) S.SetModal(false);
        }

        void CloseAllModals()
        {
            _modal.Clear();
            S.SetModal(false);
        }

        void ShowNewWorld()
        {
            var dim = Modal("A New World", out var b);
            b.Add(L("Every seed grows a different valley of people: different names, temperaments, families and futures. The founding families of Thistlewick are always there.", "small"));
            var seed = new TextField("Seed") { value = UnityEngine.Random.Range(1000, 99999).ToString() };
            seed.AddToClassList("textfield");
            b.Add(seed);
            b.Add(Btn("Roll a random seed", () => seed.value = UnityEngine.Random.Range(1000, 999999).ToString(), "btn-ghost"));
            b.Add(Btn("Begin", () =>
            {
                ulong.TryParse(seed.value, out ulong s);
                if (s == 0) s = (ulong)Mathf.Abs(seed.value.GetHashCode()) + 1;
                CloseAllModals();
                S.NewGame(ScenarioDef.Sandbox, s);
                Audio?.Ui("begin");
            }, "btn-primary"));
            if (S.BonusUnlocked) // unlocked for this session only, by design
                b.Add(Btn("Play the Bonus World", () => { CloseAllModals(); S.NewGame(ScenarioDef.BonusFestival, 0); }, "btn-ghost"));
            else
                b.Add(Btn("Unlock a Bonus World (ad)", () => Rewarded(Placements.BonusWorld, () => { S.BonusUnlocked = true; CloseModal(dim); ShowNewWorld(); Toast("Bonus World unlocked for this session."); }), "btn-ad"));
        }

        void ShowChallenges()
        {
            Modal("Challenges", out var b);
            foreach (var sc in ScenarioDef.All.Where(s => s.isChallenge))
            {
                var card = new VisualElement(); card.AddToClassList("card");
                int stars = PlayerPrefs.GetInt("goa_stars_" + sc.id, 0);
                var sr = Row();
                for (int i = 0; i < 3; i++) sr.Add(new UIIcon(i < stars ? "star" : "starEmpty", 16) { Tint = new Color(1f, 0.82f, 0.4f) });
                card.Add(Row(L(sc.title, "h2"), Spacer(), sr));
                card.Add(L(sc.subtitle, "gold"));
                card.Add(L(sc.briefing, "small"));
                var s2 = sc;
                card.Add(Btn("Begin", () => { CloseAllModals(); S.NewGame(s2, 0); Audio?.Ui("begin"); ShowBriefing(s2); }, "btn-primary"));
                b.Add(card);
            }
            var soon = new VisualElement(); soon.AddToClassList("card");
            soon.Add(L("Coming in later ages", "h2"));
            soon.Add(L("The Unlikely Hero · The Impossible Peace · The Accidental Empire · The Perfect Disaster · The Thousand-Year Challenge · daily seeded challenges", "tiny"));
            b.Add(soon);
        }

        void ShowBriefing(ScenarioDef sc)
        {
            var dim = Modal(sc.title, out var b);
            b.Add(L(sc.briefing, "small"));
            b.Add(L("Tips", "h2"));
            if (sc.id == "forgotten_genius")
            {
                b.Add(L("· Tap Wren to follow her. She plays on the green most mornings and afternoons.", "tiny"));
                b.Add(L("· Old Ambrose Pell, the tinkerer, walks past the green toward the river around three each afternoon.", "tiny"));
                b.Add(L("· Corvin Hale, the travelling merchant, comes over the east bridge each morning. Tap him to see what he carries.", "tiny"));
                b.Add(L("· Things dropped on the ground are found by whoever notices them first. Curious people notice more.", "tiny"));
            }
            else b.Add(L("· One act only. Think about who will be where, and what they're carrying.", "tiny"));
            b.Add(Btn("Begin", () => CloseModal(dim), "btn-primary"));
        }

        // ------------------------------------------------------------------ settings, consent, pause

        void ShowSettings()
        {
            Modal("Settings", out var b);
            b.Add(L("AUDIO", "h2"));
            b.Add(SliderRow("Music", GameSettings.MusicVolume, v => { GameSettings.MusicVolume = v; Audio?.ApplyVolumes(); }));
            b.Add(SliderRow("Effects", GameSettings.SfxVolume, v => { GameSettings.SfxVolume = v; Audio?.ApplyVolumes(); }));
            b.Add(SliderRow("Ambience", GameSettings.AmbienceVolume, v => { GameSettings.AmbienceVolume = v; Audio?.ApplyVolumes(); }));
            b.Add(L("GRAPHICS", "h2"));
            var q = Row();
            string[] qn = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                int qi = i;
                var btn = Btn(qn[i], () => { GameSettings.Quality = qi; Bootstrap.ApplyQuality(); ShowSettingsRefresh(); }, GameSettings.Quality == i ? "btn-on" : "btn-ghost");
                q.Add(btn);
            }
            b.Add(q);
            b.Add(L("Low disables grass and some effects for older phones. Vegetation density applies on the next world load.", "tiny"));
            b.Add(L("ACCESSIBILITY", "h2"));
            b.Add(ToggleRow("Cinematic camera on turning points", GameSettings.Cinematics, v => GameSettings.Cinematics = v));
            b.Add(ToggleRow("Reduce motion", GameSettings.ReduceMotion, v => GameSettings.ReduceMotion = v));
            b.Add(ToggleRow("Larger text", GameSettings.LargeText, v => { GameSettings.LargeText = v; _root.EnableInClassList("large-text", v); }));
            b.Add(L("PRIVACY", "h2"));
            b.Add(ToggleRow("Share anonymous gameplay analytics", GameSettings.AnalyticsConsent, v => { GameSettings.AnalyticsConsent = v; Svc.Analytics.SetConsent(v); }));
            b.Add(ToggleRow("Personalised ads (on mobile)", GameSettings.PersonalizedAds, v => GameSettings.PersonalizedAds = v));
            b.Add(L("No personal information is collected. Analytics stay on this device in this build.", "tiny"));
            b.Add(L("PURCHASES", "h2"));
            bool adFree = Svc.Entitlements.Has(Products.RemoveAds);
            b.Add(L(adFree ? "Ad-free: interstitial ads are off. Optional rewarded ads remain available." : "Remove interstitial ads with a one-time purchase.", "small"));
            if (!adFree) b.Add(Btn($"Remove Ads - {Svc.Store.PriceLabel(Products.RemoveAds)}", () => Svc.BuyRemoveAds(r => { Toast(r == PurchaseResult.Success ? "Thank you! Interstitial ads are now off." : "Purchase: " + r); ShowSettingsRefresh(); }), "btn-primary"));
            b.Add(Btn("Restore purchases", () => { Svc.RestoreEntitlements(); Toast("Purchases restored."); ShowSettingsRefresh(); }, "btn-ghost"));
            b.Add(L($"Store: {Svc.Store.Name}. Ads: {Svc.Ads.ProviderName}. No real money or real ads are used in this build.", "tiny"));
            b.Add(L("DEVELOPER", "h2"));
            b.Add(Btn("Analytics dashboard", ShowAnalytics, "btn-ghost"));
        }

        void ShowSettingsRefresh() { CloseAllModals(); ShowSettings(); }

        VisualElement SliderRow(string label, float value, Action<float> set)
        {
            var r = new VisualElement(); r.AddToClassList("slider-row");
            r.Add(L(label, "small"));
            var s = new Slider(0, 1) { value = value };
            s.RegisterValueChangedCallback(e => set(e.newValue));
            r.Add(s);
            return r;
        }

        VisualElement ToggleRow(string label, bool value, Action<bool> set)
        {
            var t = new Toggle(label) { value = value };
            t.RegisterValueChangedCallback(e => set(e.newValue));
            t.style.marginTop = 2; t.style.marginBottom = 2;
            t.Q<Label>()?.AddToClassList("small");
            return t;
        }

        void ShowConsent()
        {
            var dim = Modal("Before you begin", out var b, () => GameSettings.ConsentAsked = true, 560);
            b.Add(L("God of Accidents can record anonymous gameplay statistics (which powers are used, how challenges go) to help improve the game. Nothing that identifies you is collected.", "small"));
            b.Add(L("On phones the game is supported by optional ads. You can choose whether those ads may be personalised.", "small"));
            var a = ToggleRow("Share anonymous analytics", false, v => GameSettings.AnalyticsConsent = v);
            var p = ToggleRow("Allow personalised ads (mobile)", false, v => GameSettings.PersonalizedAds = v);
            b.Add(a); b.Add(p);
            b.Add(Btn("Continue", () =>
            {
                GameSettings.ConsentAsked = true;
                Svc.Analytics.SetConsent(GameSettings.AnalyticsConsent);
                CloseModal(dim);
            }, "btn-primary"));
            b.Add(L("You can change these any time in Settings.", "tiny"));
        }

        void ShowPause()
        {
            var dim = Modal("Paused", out var b, null, 420);
            b.Add(Btn("Resume", () => CloseModal(dim), "btn-primary"));
            b.Add(Btn("Save history", () => { S.Save(); Toast("History saved."); }, "btn-ghost"));
            b.Add(Btn("Timelines", () => { CloseModal(dim); OpenTimeline(); }, "btn-ghost"));
            b.Add(Btn("Codex", () => { CloseModal(dim); ShowCodex(); }, "btn-ghost"));
            b.Add(Btn("Settings", () => { CloseModal(dim); ShowSettings(); }, "btn-ghost"));
            b.Add(Btn("Main menu", () => { CloseAllModals(); S.ReturnToMenu(); }, "btn-ghost"));
            b.Add(L("Controls: drag to pan · right-drag to orbit · wheel to zoom · 1-4 powers · Space pause · F1-F6 speed · T timeline", "tiny"));
        }

        void ShowCodex()
        {
            Modal("Codex of Accidents", out var b);
            b.Add(L($"{Codex.FoundCount} of {Codex.All.Length} kinds of consequence discovered. Entries unlock only when a chain truly traces back to you.", "small"));
            foreach (var e in Codex.All)
            {
                bool found = Codex.IsFound(e.id);
                var card = new VisualElement(); card.AddToClassList("card");
                card.Add(L(found ? e.title : "Undiscovered", found ? "h2" : "codex-locked"));
                card.Add(L(found ? e.blurb : "Hint: " + e.hint, found ? "tiny" : "codex-hint"));
                b.Add(card);
            }
        }

        void ShowAnalytics()
        {
            Modal("Analytics (this device)", out var b);
            var a = Svc.Analytics;
            b.Add(L(a.Consent ? $"Session {a.SessionId} · consent given" : "Analytics consent not given - nothing is being recorded.", "small"));
            b.Add(L($"Estimated ad revenue this session (mock eCPM): ${a.EstimatedRevenueUsd:0.0000}", "gold"));
            foreach (var kv in a.Counters.OrderByDescending(k => k.Value)) b.Add(Row(L(kv.Key, "small"), Spacer(), L(kv.Value.ToString(), "small")));
            foreach (var kv in a.RevenueByPlacement) b.Add(Row(L("revenue · " + kv.Key, "tiny"), Spacer(), L($"${kv.Value:0.0000}", "tiny")));
            b.Add(L("Events are written to goa_analytics.jsonl in the app's data folder. See docs/MONETIZATION.md for provider integration.", "tiny"));
        }

        // ------------------------------------------------------------------ results, glimpse

        void ShowResults(ChallengeStatus c)
        {
            bool won = c.state == ChallengeState.Won;
            Audio?.TurningPoint(won);
            var dim = Modal(won ? "History, Rewritten" : "History Holds", out var b, null, 640);
            var stars = Row();
            if (won) for (int i = 0; i < 3; i++) stars.Add(new UIIcon(i < c.stars ? "star" : "starEmpty", 34) { Tint = new Color(1f, 0.82f, 0.4f) });
            b.Add(stars);
            b.Add(L(c.headline, "h1"));
            b.Add(L(c.detail, "small"));
            if (won && c.winningEventId >= 0)
            {
                b.Add(L("THE CHAIN YOU SET IN MOTION", "h2"));
                foreach (var e in S.World.Ledger.PrimaryChain(c.winningEventId))
                {
                    var node = new VisualElement(); node.AddToClassList("chain-node");
                    if (e.IsIntervention) node.AddToClassList("chain-root");
                    node.Add(L(e.title, "small")); node.Add(L(SimTime.FormatShort(e.time), "tiny"));
                    b.Add(node);
                }
            }
            var row = Row();
            row.AddToClassList("wrap");
            if (won) row.Add(Btn("Keep living in this world", () => { CloseModal(dim); S.ContinueAfterResults(); }, "btn-primary"));
            else
            {
                if (S.Timeline.World.InterventionCount > 0)
                    row.Add(Btn("Second Chance (ad)", () => Rewarded(Placements.SecondChance, () => { CloseModal(dim); Veil("Folding time back...", S.SecondChance); }), "btn-ad"));
                row.Add(Btn("Try again", () => { CloseModal(dim); S.Retry(); }, "btn-primary"));
            }
            row.Add(Btn("Open the timeline", () => { CloseModal(dim); S.ContinueAfterResults(); S.SetSpeed(0); OpenTimeline(); }, "btn-ghost"));
            row.Add(Btn("Main menu", () => { CloseAllModals(); S.ReturnToMenu(); }, "btn-ghost"));
            b.Add(row);
        }

        void ShowGlimpse(Counterfactual.Glimpse g, PlayerInput input, Vector3 at)
        {
            var dim = Modal("A Glimpse of What May Be", out var b, () => S.SetModal(false));
            if (!g.valid) { b.Add(L(g.error, "small")); return; }
            b.Add(L($"Looking two years ahead, comparing a world where you act now with one where you don't:", "small"));
            b.Add(L("WOULD HAPPEN", "h2"));
            foreach (var e in g.newEvents.Where(e => e.magnitude >= 1).Take(10)) b.Add(L($"· {SimTime.FormatShort(e.time)}  {e.title}", "small"));
            if (g.newEvents.Count == 0) b.Add(L("Nothing of note. The ripple fades.", "tiny"));
            if (g.preventedEvents.Count > 0)
            {
                b.Add(L("WOULD NO LONGER HAPPEN", "h2"));
                foreach (var e in g.preventedEvents.Where(e => e.magnitude >= 1).Take(6)) b.Add(L($"· {SimTime.FormatShort(e.time)}  {e.title}", "small"));
            }
            b.Add(L("The future is certain only if nothing else changes - and you may change it again.", "tiny"));
            b.Add(Row(
                Btn("Do it", () => { CloseModal(dim); S.ActNow(input); }, "btn-primary"),
                Btn("Hold back", () => CloseModal(dim), "btn-ghost")));
        }

        // ------------------------------------------------------------------ timeline screen

        void OpenTimeline()
        {
            if (S.World == null || S.Mode == SessionMode.Menu) return;
            _timelineOpen = true;
            Show(_hud, false); // nothing of the HUD shows through the Web of History
            S.SetModal(true);
            _overlay.Clear();
            var scr = new VisualElement(); scr.AddToClassList("timeline-screen");
            var head = new VisualElement(); head.AddToClassList("timeline-head");
            head.Add(L("THE WEB OF HISTORY", "h1"));
            head.Add(Spacer());
            for (int i = 0; i < S.Timelines.Count; i++)
            {
                int idx = i;
                head.Add(Btn(S.Timelines[i].Name, () => { S.SwitchTimeline(idx); OpenTimeline(); }, i == S.ActiveIndex ? "btn-on" : "btn-ghost"));
            }
            if (!S.CanBranch) head.Add(Btn("+ timeline (ad)", () => Rewarded(Placements.TimelineExperiment, () => { S.BranchAllowance++; Toast("A new alternate timeline is available."); OpenTimeline(); }), "btn-ad"));
            head.Add(IconBtn("close", CloseTimeline, "Close  (T)", 14, "btn-ghost"));
            scr.Add(head);
            _graph = new TimelineGraph();
            _graph.AddToClassList("timeline-graph");
            _graph.style.position = Position.Absolute;
            _graph.SetWorld(S.World);
            scr.Add(_graph);
            _timelineSide = Panel("timeline-side");
            _timelineSide.style.position = Position.Absolute;
            scr.Add(_timelineSide);
            _overlay.Add(scr);
            _graph.OnSelect += TimelineSelect;
            TimelineCompare();
        }

        void CloseTimeline()
        {
            _timelineOpen = false;
            Show(_hud, true);
            _overlay.Clear();
            S.SetModal(false);
        }

        void TimelineSelect(SimEvent e)
        {
            Audio?.Ui("select");
            _timelineSide.Clear();
            var w = S.World;
            _timelineSide.Add(L(e.title, "h1"));
            _timelineSide.Add(L(Fmt(e.time), "gold"));
            _timelineSide.Add(L(e.detail, "small"));
            _timelineSide.Add(L("CAUSES", "h2"));
            foreach (var c in w.Ledger.PrimaryChain(e.id).Take(12))
            {
                var cc = c;
                var row = Row(L(c.title, c.id == e.id ? "gold" : "small"));
                row.AddToClassList("chron-row");
                row.RegisterCallback<ClickEvent>(_ => { _graph.Select(cc.id); TimelineSelect(cc); });
                _timelineSide.Add(row);
            }
            _timelineSide.Add(Btn("Replay this moment", () => { CloseTimeline(); Veil("Rewinding time...", () => S.EnterReplay(e)); }, "btn-primary"));
            _timelineSide.Add(Btn("Branch: what if...", () => { CloseTimeline(); Veil("Splitting the timeline...", () => S.Branch(Math.Max(World.StartTime, e.time - SimTime.Hour))); }, S.CanBranch ? "btn-ghost" : "btn-disabled"));
            _timelineSide.Add(Btn("Show in the world", () => { CloseTimeline(); ShowEvent(e); if (e.hasPos) Rig.FocusOn(TerrainBuilder.Ground(e.pos), 30, 1.4f); }, "btn-ghost"));
        }

        void TimelineCompare()
        {
            _timelineSide.Clear();
            _timelineSide.Add(L("COMPARE HISTORIES", "h1"));
            _timelineSide.Add(L("Select any glowing node to see what caused it and what it caused. Threads take the colour of the intervention they descend from.", "small"));
            if (S.Timelines.Count > 1)
            {
                var other = S.Timelines[S.ActiveIndex == 0 ? 1 : 0];
                CompareBlock(S.Timeline.World, other.World, S.Timeline.Name, other.Name);
            }
            var btn = Btn("Compare with a world where you never acted", () =>
            {
                var baseline = Timeline.Replay(S.Timeline.Seed, S.Timeline.Scenario, new System.Collections.Generic.List<PlayerInput>(), S.World.Time);
                CompareBlock(S.World, baseline, S.Timeline.Name, "Untouched");
            }, "btn-ghost");
            _timelineSide.Add(btn);
        }

        void CompareBlock(World a, World bw, string an, string bn)
        {
            var cmp = Counterfactual.Compare(a, bw, 3);
            var box = new VisualElement(); box.AddToClassList("card");
            box.Add(Row(L("", "tiny"), Spacer(), L(an, "gold"), L("   vs   ", "tiny"), L(bn, "small")));
            box.Add(Row(L("Souls", "small"), Spacer(), L($"{cmp.popA}   ·   {cmp.popB}", "small")));
            box.Add(Row(L("Age", "small"), Spacer(), L($"{World.EraName(cmp.eraA)}   ·   {World.EraName(cmp.eraB)}", "tiny")));
            box.Add(Row(L("Inventions", "small"), Spacer(), L($"{cmp.techsA}   ·   {cmp.techsB}", "small")));
            box.Add(Row(L("Elder", "small"), Spacer(), L($"{cmp.elderA}   ·   {cmp.elderB}", "tiny")));
            box.Add(Row(L("Prosperity", "small"), Spacer(), L($"{(int)(cmp.prosperityA * 100)}%   ·   {(int)(cmp.prosperityB * 100)}%", "small")));
            box.Add(L($"Only in {an}:", "h2"));
            foreach (var e in cmp.onlyInA.Take(8)) box.Add(L("· " + e.title, "tiny"));
            if (cmp.onlyInA.Count == 0) box.Add(L("(nothing major)", "tiny"));
            box.Add(L($"Only in {bn}:", "h2"));
            foreach (var e in cmp.onlyInB.Take(6)) box.Add(L("· " + e.title, "tiny"));
            if (cmp.onlyInB.Count == 0) box.Add(L("(nothing major)", "tiny"));
            _timelineSide.Add(box);
        }

        // ------------------------------------------------------------------ mock ad presenter

        void PresentMockAd(string placement, AdFormat format, Action<AdResult, bool> done)
        {
            S.SetModal(true);
            var scr = new VisualElement(); scr.AddToClassList("ad-screen");
            scr.Add(L("TEST AD", "ad-test"));
            scr.Add(L("Development build - this is not a real advertisement and earns nothing.", "small"));
            scr.Add(L(format == AdFormat.Rewarded ? "Reward: " + Rewards.Describe(Svc.Ads.Config.Get(placement)?.rewardId) : "Interstitial at a natural break", "gold"));
            var count = L("", "h1");
            scr.Add(count);
            bool finished = false;
            void Finish(AdResult r, bool earned)
            {
                if (finished) return;
                finished = true;
                scr.RemoveFromHierarchy();
                S.SetModal(false);
                done(r, earned);
            }
            var claim = Btn("Claim reward", () => Finish(AdResult.Completed, true), "btn-primary");
            var close = Btn(format == AdFormat.Rewarded ? "Close (no reward)" : "Skip", () => Finish(format == AdFormat.Rewarded ? AdResult.Skipped : AdResult.Completed, false), "btn-ghost");
            Show(claim, false);
            scr.Add(Row(close, claim));
            _adLayer.Add(scr);
            int seconds = format == AdFormat.Rewarded ? 5 : 3;
            float endAt = Time.unscaledTime + seconds;
            _adTick = () =>
            {
                if (finished) return true;
                int left = Mathf.CeilToInt(endAt - Time.unscaledTime);
                count.text = left > 0 ? left.ToString() : "";
                if (left > 0) return false;
                if (format == AdFormat.Rewarded) { Show(claim, true); return true; }
                Finish(AdResult.Completed, false);
                return true;
            };
        }

        Func<bool> _adTick;

        // ------------------------------------------------------------------ tutorial

        int _tutorialStep;

        void Tutorial()
        {
            if (GameSettings.TutorialDone || S.Mode != SessionMode.Playing) return;
            string[] steps =
            {
                "You are an invisible god. You cannot command anyone - but you can arrange tiny accidents.",
                "Watch for flashing rings: they mark a moment where a tiny push would cause something juicy. Gold means Nudge, violet means Distract.",
                "Choose a power at the bottom (or press 1-4), then tap a glowing target. Nudge rolls a pebble into someone's path; Gust bends the wind; Distract stops a villager with a glimmer; Drop sends the crow.",
                "Tap any villager to see their life, traits and memories. Tap the travelling merchant to see what he carries.",
                "Speed time up with the controls at the top right. The goals on the left are optional - every consequence is recorded, and T opens the web of history.",
            };
            if (_tutorialStep >= steps.Length) { GameSettings.TutorialDone = true; return; }
            var p = Panel();
            p.AddToClassList("tutorial");
            p.Add(L(steps[_tutorialStep], "small"));
            p.Add(Row(Spacer(), Btn(_tutorialStep == steps.Length - 1 ? "Begin" : "Next", () =>
            {
                p.RemoveFromHierarchy();
                _tutorialStep++;
                Tutorial();
            }, "btn-primary"), Btn("Skip", () => { p.RemoveFromHierarchy(); GameSettings.TutorialDone = true; }, "btn-ghost")));
            _hud.Add(p);
        }
    }
}
