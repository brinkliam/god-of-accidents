using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    /// <summary>Adapts the interface to the window: compact on narrow screens, regrouped in portrait.</summary>
    public sealed partial class UIRoot
    {
        int _layoutW = -1, _layoutH = -1;

        void UpdateLayoutMode()
        {
            if (_root == null || (Screen.width == _layoutW && Screen.height == _layoutH)) return;
            _layoutW = Screen.width; _layoutH = Screen.height;
            bool portrait = Screen.height > Screen.width * 1.05f;
            // keep text a readable physical size: on phones (small screens in CSS pixels) design against a smaller canvas
            float dpr = Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 96f) : 1f;
            float shortCss = Mathf.Min(Screen.width, Screen.height) / dpr;
            Vector2Int reference = portrait ? new Vector2Int(440, 820) : shortCss < 520 ? new Vector2Int(960, 540) : new Vector2Int(1280, 720);
            if (_doc != null && _doc.panelSettings != null)
            {
                _doc.panelSettings.referenceResolution = reference;
                _doc.panelSettings.match = 0.5f;
            }
            // width of the panel in UI units
            float scale = Mathf.Sqrt((Screen.width / (float)reference.x) * (Screen.height / (float)reference.y));
            float uiWidth = Screen.width / Mathf.Max(0.01f, scale);
            bool compact = portrait || uiWidth < 1100;
            _root.EnableInClassList("portrait", portrait);
            _root.EnableInClassList("compact", compact);
            if (!compact) _hud?.RemoveFromClassList("chips-open");
        }

        // ------------------------------------------------------------------ "folding time" veil for heavy work

        VisualElement _veil;
        Action _veilWork;
        int _veilFrames;

        /// <summary>
        /// Show a short "folding time" overlay, let it render, then run work that re-simulates history (load, branch,
        /// replay, glimpse). On the web that work can take a moment; the player sees why instead of a frozen frame.
        /// </summary>
        public void Veil(string message, Action work)
        {
            if (_veilWork != null) { work(); return; }
            _veil?.RemoveFromHierarchy();
            _veil = new VisualElement { pickingMode = PickingMode.Position };
            _veil.AddToClassList("screen"); _veil.AddToClassList("veil");
            var l = L(message, "veil-text");
            _veil.Add(l);
            _adLayer.Add(_veil);
            _veilWork = work;
            _veilFrames = 2;
        }

        void TickVeil()
        {
            if (_veilWork == null || --_veilFrames > 0) return;
            var w = _veilWork;
            _veilWork = null;
            try { w(); }
            finally { _veil?.RemoveFromHierarchy(); _veil = null; }
        }

        // ------------------------------------------------------------------ snapped-target label

        Label _snapLabel;

        /// <summary>Names the villager or spot a tap would hit before the player confirms it.</summary>
        void TickSnapLabel()
        {
            if (_snapLabel == null)
            {
                _snapLabel = L("", "snap-label");
                _snapLabel.pickingMode = PickingMode.Ignore;
                _floating.Add(_snapLabel);
            }
            bool show = PC != null && PC.HasSnap && S.Mode == SessionMode.Playing && Rig != null && Rig.Cam != null;
            Show(_snapLabel, show);
            if (!show) return;
            var sp = Rig.Cam.WorldToScreenPoint(PC.SnapWorld + Vector3.up * 3.2f);
            if (sp.z < 0) { Show(_snapLabel, false); return; }
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(sp.x, Screen.height - sp.y));
            _snapLabel.text = PC.SnapLabel;
            _snapLabel.style.left = p.x - 110;
            _snapLabel.style.top = p.y - 28;
        }

        void RestoreHud() => Show(_hud, S != null && S.Mode != SessionMode.Menu && !_timelineOpen && _modal.childCount == 0);

        // ------------------------------------------------------------------ small-screen drawer for the side panels

        Button _tabGoals, _tabChron;

        /// <summary>On compact screens Goals and Chronicle share one drawer with tabs; tapping the open tab tucks it away.</summary>
        VisualElement BuildDrawerTabs()
        {
            var tabs = new VisualElement();
            tabs.AddToClassList("drawer-tabs");
            _tabGoals = Btn("Goals", () => SetDrawer("drawer-goals"), "drawer-tab");
            _tabChron = Btn("Chronicle", () => SetDrawer("drawer-chron"), "drawer-tab");
            tabs.Add(_tabGoals); tabs.Add(_tabChron);
            SetDrawer("drawer-goals");
            return tabs;
        }

        void SetDrawer(string mode)
        {
            bool closing = _hud.ClassListContains(mode);
            _hud.RemoveFromClassList("drawer-goals"); _hud.RemoveFromClassList("drawer-chron"); _hud.RemoveFromClassList("drawer-closed");
            _hud.AddToClassList(closing ? "drawer-closed" : mode);
            _tabGoals?.EnableInClassList("btn-on", !closing && mode == "drawer-goals");
            _tabChron?.EnableInClassList("btn-on", !closing && mode == "drawer-chron");
        }

        /// <summary>Forgotten Genius opening: fly to Wren, open her card, and say what to watch for.</summary>
        void GuideToWren()
        {
            var w = S.World;
            var wren = w?.FindCitizen("Wren Ashdown");
            if (wren == null) return;
            var at = S.View.PositionOf(PickKind.Citizen, wren.id, S.DisplayTime);
            Rig.FocusOn(at, 32, GameSettings.ReduceMotion ? 0.01f : 1.8f);
            Rig.Follow = () => S.View.PositionOf(PickKind.Citizen, wren.id, S.DisplayTime);
            Inspect(new PickResult { kind = PickKind.Citizen, id = wren.id, point = at });
            Toast("This is Wren. Watch her day - when old Ambrose walks by in the afternoon, a violet marker will show you the moment.");
        }
    }
}
