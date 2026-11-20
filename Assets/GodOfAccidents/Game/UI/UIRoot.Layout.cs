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
    }
}
