using System;
using System.Collections.Generic;
using System.Linq;
using GOA.Services;
using GOA.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    public sealed partial class UIRoot : MonoBehaviour
    {
        UIDocument _doc;
        VisualElement _root, _menu, _hud, _overlay, _modal, _adLayer, _toasts, _bannerLayer, _floating;
        GameSession S;
        PowerController PC;
        CameraRig Rig;
        ServicesHub Svc;
        AudioDirector Audio;

        // hud refs
        Label _era, _clock, _weather, _pop, _food, _prosperity, _energyText, _speedName, _objHead, _replayLabel;
        VisualElement _energyFill, _objSteps, _objective, _inspector, _inspectorBody, _chronicleList, _chronicle, _replayBar;
        readonly List<(Button b, int idx)> _speedButtons = new List<(Button, int)>();
        readonly Dictionary<PowerType, VisualElement> _powerButtons = new Dictionary<PowerType, VisualElement>();
        Label _floatName;
        Button _energyAdBtn, _glimpseBtn;
        float _hudTick, _inspectorTick;
        int _goalsDone = -1;
        readonly HashSet<string> _goalsSeen = new HashSet<string>();
        PickResult _inspecting;
        SimEvent _inspectEvent;
        LineRenderer _trail;
        Material _trailMat;
        bool _timelineOpen;
        TimelineGraph _graph;
        VisualElement _timelineSide;

        // ------------------------------------------------------------------ setup

        public void Init(GameSession s, PowerController pc, CameraRig rig, ServicesHub svc, AudioDirector audio, PanelSettings panelSettings)
        {
            S = s; PC = pc; Rig = rig; Svc = svc; Audio = audio;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = panelSettings;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/GOA");
            if (sheet != null) _root.styleSheets.Add(sheet); else Debug.LogError("[GOA] UI stylesheet missing");
            _root.AddToClassList("root");
            _root.pickingMode = PickingMode.Ignore;
            if (GameSettings.LargeText) _root.AddToClassList("large-text");

            _floating = Layer("floating");
            _hud = Layer("hud");
            _menu = Layer("menu");
            _overlay = Layer("overlay");
            _bannerLayer = Layer("banner");
            _toasts = Layer("toasts"); _toasts.AddToClassList("toasts");
            _modal = Layer("modal");
            _adLayer = Layer("ads");

            // any press that lands on a UI element belongs to the UI, never to the world behind it
            _root.RegisterCallback<PointerDownEvent>(e => { if (e.target != _root && !IsLayer(e.target as VisualElement)) _uiDownFrame = Time.frameCount; }, TrickleDown.TrickleDown);

            BuildHud();
            BuildMenu();
            BuildTrail();
            BuildChainCard();

            S.OnToast += Toast;
            S.RunHeavy = Veil;
            S.OnEvent += OnSimEvent;
            S.OnTurningPoint += Banner;
            S.OnCodex += CodexReveal;
            S.OnChallengeResolved += ShowResults;
            S.OnGlimpse += ShowGlimpse;
            S.OnWorldChanged += WorldChanged;
            S.OnActed += (r, i) => { if (r.ok) { Audio?.Power(i.power); PC.Select(null); RefreshPowers(); } else Audio?.Ui("error"); };
            PC.OnSelect += Inspect;
            PC.OnHint += Toast;
            svc.MockAds.Presenter = PresentMockAd;
            svc.Gate.Critical = () => S.IsCriticalMoment(PC);

            WorldChanged();
            if (!GameSettings.ConsentAsked) ShowConsent();
        }

        VisualElement Layer(string name)
        {
            var v = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            v.AddToClassList("layer");
            _root.Add(v);
            return v;
        }

        int _uiDownFrame = -100;
        bool IsLayer(VisualElement v) => v != null && v.parent == _root && v.ClassListContains("layer");
        /// <summary>True for a few frames after a press began on a UI element (so its release can't select the world).</summary>
        public bool PressBeganOnUI => Time.frameCount - _uiDownFrame <= 2;

        public bool IsOverUI(Vector2 screen)
        {
            if (_root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = _root.panel.Pick(p);
            return picked != null && picked != _root;
        }

        // ------------------------------------------------------------------ element helpers

        static Label L(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(() => { Audio?.Ui("click"); onClick?.Invoke(); }) { text = text };
            b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            b.RegisterCallback<PointerEnterEvent>(_ => Audio?.Ui("hover"));
            return b;
        }

        Button IconBtn(string icon, Action onClick, string tooltip, float size = 18, params string[] classes)
        {
            var b = Btn("", onClick, classes);
            b.tooltip = tooltip;
            b.Add(new UIIcon(icon, size));
            b.style.alignItems = Align.Center; b.style.justifyContent = Justify.Center;
            return b;
        }

        static VisualElement Row(params VisualElement[] kids)
        {
            var r = new VisualElement();
            r.AddToClassList("row");
            foreach (var k in kids) r.Add(k);
            return r;
        }

        static VisualElement Panel(params string[] classes)
        {
            var p = new VisualElement();
            p.AddToClassList("panel");
            foreach (var c in classes) p.AddToClassList(c);
            return p;
        }

        static VisualElement Spacer() { var s = new VisualElement(); s.AddToClassList("spacer"); return s; }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        string Fmt(long t) => SimTime.Format(t);

        // ------------------------------------------------------------------ world change

        void WorldChanged()
        {
            bool menu = S.Mode == SessionMode.Menu;
            Show(_menu, menu);
            Show(_hud, !menu);
            _chronicleList?.Clear();
            if (_chronicle != null) Show(_chronicle, false);
            CloseInspector();
            if (_timelineOpen) CloseTimeline();
            ClearTrail();
            if (!menu && S.Timeline != null)
            {
                var w = S.World;
                foreach (var id in w.Chronicle.Skip(Math.Max(0, w.Chronicle.Count - 7))) AddChronicle(w.Ledger.Get(id));
                Show(_objective, S.Timeline.Scenario.isChallenge || S.Timeline.Scenario.id == "sandbox");
                _goalsDone = -1; _goalsSeen.Clear();
                if (!GameSettings.TutorialDone && S.Mode == SessionMode.Playing) Tutorial();
            }
            Show(_replayBar, S.InReplay);
            if (S.InReplay && S.ReplayEvent != null) _replayLabel.text = "REPLAYING · " + S.ReplayEvent.title;
            RefreshPowers();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (S == null) return;
            TickFades();
            TickChainCard();
            TickShimmer();
            UpdateLayoutMode();
            TickVeil();
            TickSnapLabel();
            if (_adTick != null && _adTick()) _adTick = null;
            _hudTick -= Time.unscaledDeltaTime;
            if (_hudTick <= 0 && S.Mode != SessionMode.Menu) { _hudTick = 0.15f; RefreshHud(); }
            _inspectorTick -= Time.unscaledDeltaTime;
            if (_inspectorTick <= 0 && _inspector.style.display == DisplayStyle.Flex) { _inspectorTick = 0.6f; RefreshInspector(); }
            UpdateFloatingName();
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && S.Mode != SessionMode.Menu && _modal.childCount == 0)
            {
                if (kb.spaceKey.wasPressedThisFrame) S.TogglePause();
                if (kb.tKey.wasPressedThisFrame) { if (_timelineOpen) CloseTimeline(); else OpenTimeline(); }
                if (kb.escapeKey.wasPressedThisFrame && !PC.Active.HasValue)
                {
                    if (_timelineOpen) CloseTimeline();
                    else if (_inspector.style.display == DisplayStyle.Flex) CloseInspector();
                    else ShowPause();
                }
                for (int i = 0; i < 6; i++)
                    if (kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.F1 + i)].wasPressedThisFrame) S.SetSpeed(i);
            }
        }

        void UpdateFloatingName()
        {
            if (_floatName == null) return;
            bool show = _inspecting.kind == PickKind.Citizen && S.World != null && S.View.CitizenVisible(_inspecting.id);
            Show(_floatName, show);
            if (!show) return;
            var c = S.World.GetCitizen(_inspecting.id);
            var wp = S.View.PositionOf(PickKind.Citizen, c.id, S.DisplayTime) + Vector3.up * 2.1f;
            var sp = Rig.Cam.WorldToScreenPoint(wp);
            if (sp.z < 0) { Show(_floatName, false); return; }
            var pp = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(sp.x, Screen.height - sp.y));
            _floatName.text = c.firstName;
            _floatName.style.left = pp.x - 40;
            _floatName.style.top = pp.y - 22;
        }

        // ------------------------------------------------------------------ toasts & banners

        public void Toast(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            var t = L(msg, "toast");
            _toasts.Add(t);
            while (_toasts.childCount > 3) _toasts.RemoveAt(0);
            FadeOutAndRemove(t, 3800, 500);
        }

        VisualElement _bannerEl;

        void Banner(SimEvent e)
        {
            bool yours = e.roots.Length > 0;
            SmallBanner(yours ? "YOUR HAND IN HISTORY" : "A TURNING POINT", e.title, yours ? "It all began with: " + S.World.Ledger.Get(e.roots[0]).title.Replace("You ", "you ") : e.detail, e);
            Audio?.TurningPoint(yours);
        }

        void SmallBanner(string kicker, string title, string detail, SimEvent e = null, string extraClass = null)
        {
            _bannerEl?.RemoveFromHierarchy();
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.AddToClassList("banner");
            if (extraClass != null) b.AddToClassList(extraClass);
            b.Add(L(kicker, "banner-kicker"));
            b.Add(L(title, "banner-title"));
            if (!string.IsNullOrEmpty(detail)) b.Add(L(detail, "banner-detail"));
            if (e != null)
            {
                var trace = Btn("Trace the chain", () => ShowEvent(e), "btn-ghost");
                trace.style.marginTop = 8;
                b.Add(trace);
            }
            foreach (var ch in b.Children()) if (!(ch is Button)) ch.pickingMode = PickingMode.Ignore;
            _bannerLayer.Add(b);
            _bannerEl = b;
            PopIn(b, 0.45f);
            FadeOutAndRemove(b, 5200, 900, () => { if (_bannerEl == b) _bannerEl = null; });
        }

        readonly List<(VisualElement e, float hideAt, float fade, Action done)> _fades = new List<(VisualElement, float, float, Action)>();

        /// <summary>Hold, then fade out and remove - driven by our own clock so it never depends on the UI scheduler.</summary>
        void FadeOutAndRemove(VisualElement e, long holdMs, long fadeMs, Action done = null)
        {
            e.style.opacity = 1;
            _fades.Add((e, Time.unscaledTime + holdMs / 1000f, Mathf.Max(0.05f, fadeMs / 1000f), done));
        }

        void TickFades()
        {
            for (int i = _fades.Count - 1; i >= 0; i--)
            {
                var f = _fades[i];
                float u = (Time.unscaledTime - f.hideAt) / f.fade;
                if (u <= 0) continue;
                if (u >= 1 || f.e.parent == null) { f.e.RemoveFromHierarchy(); f.done?.Invoke(); _fades.RemoveAt(i); continue; }
                f.e.style.opacity = 1 - u;
            }
        }

        // ------------------------------------------------------------------ chronicle

        void OnSimEvent(SimEvent e)
        {
            if (S.Mode == SessionMode.Menu) return;
            if (e.magnitude >= 2 || e.IsIntervention || (e.roots.Length > 0 && e.magnitude >= 1)) AddChronicle(e);
            Audio?.SimEvent(e, e.hasPos ? TerrainBuilder.Ground(e.pos) : (Vector3?)null);
        }

        Color HueFor(SimEvent e)
        {
            var w = S.World;
            int root = e.IsIntervention ? e.id : (e.roots.Length > 0 ? e.roots[e.roots.Length - 1] : -1);
            if (root < 0) return new Color(0.6f, 0.64f, 0.75f);
            int idx = 0;
            foreach (var x in w.Ledger.Events) { if (x.id >= root) break; if (x.IsIntervention) idx++; }
            var c = Palette.RootHues[idx % Palette.RootHues.Length];
            return new Color(Mathf.Min(1, c.r * 0.55f), Mathf.Min(1, c.g * 0.55f), Mathf.Min(1, c.b * 0.55f));
        }

        void AddChronicle(SimEvent e)
        {
            if (e == null || _chronicleList == null) return;
            var row = new VisualElement();
            row.AddToClassList("chron-row");
            var dot = new VisualElement(); dot.AddToClassList("dot"); dot.style.backgroundColor = HueFor(e);
            row.Add(dot);
            var col = new VisualElement { style = { flexShrink = 1, flexGrow = 1 } };
            col.Add(L(e.title, e.magnitude >= 4 ? "gold" : "small"));
            col.Add(L(SimTime.FormatShort(e.time) + (e.roots.Length > 0 ? " · your doing" : ""), "tiny"));
            col.style.marginTop = 0; col.style.marginBottom = 0;
            row.Add(col);
            row.RegisterCallback<ClickEvent>(_ => { Audio?.Ui("click"); ShowEvent(e); });
            _chronicleList.Insert(0, row);
            Show(_chronicle, true);
            int maxRows = _root.ClassListContains("portrait") ? 3 : _root.ClassListContains("compact") ? 4 : 5;
            while (_chronicleList.childCount > maxRows) _chronicleList.RemoveAt(_chronicleList.childCount - 1);
        }

        // ------------------------------------------------------------------ in-world chain trail

        void BuildTrail()
        {
            var go = new GameObject("ChainTrail");
            _trail = go.AddComponent<LineRenderer>();
            _trailMat = Mats.NewGlow(new Color(1.8f, 1.25f, 0.45f), 1.2f, 10);
            _trail.material = _trailMat;
            _trail.widthMultiplier = 0.55f;
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.positionCount = 0;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void ClearTrail() { if (_trail) _trail.positionCount = 0; }

        /// <summary>Draw glowing arcs through the places where a chain of events happened.</summary>
        void ShowTrail(List<SimEvent> chain)
        {
            var pts = new List<Vector3>();
            Vector3? last = null;
            foreach (var e in chain)
            {
                if (!e.hasPos) continue;
                var p = TerrainBuilder.Ground(e.pos) + Vector3.up * 1.2f;
                if (last.HasValue)
                {
                    float d = Vector3.Distance(last.Value, p);
                    if (d < 0.5f) continue;
                    for (int i = 1; i <= 16; i++)
                    {
                        float u = i / 16f;
                        pts.Add(Vector3.Lerp(last.Value, p, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * Mathf.Clamp(d * 0.35f, 2, 18));
                    }
                }
                else pts.Add(p);
                last = p;
            }
            _trail.positionCount = pts.Count;
            _trail.SetPositions(pts.ToArray());
            _trailMat.SetFloat("_PulseCount", Mathf.Max(3, pts.Count / 6f));
        }
    }
}
