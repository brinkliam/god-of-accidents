using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    /// <summary>
    /// The "cause -> effect" card: when a chain reaction the player started settles down, show the path from their act
    /// to its latest consequence. Also the pop-in animation shared by banners and cards.
    /// </summary>
    public sealed partial class UIRoot
    {
        VisualElement _chainLayer;
        SimEvent _chainLatest;
        float _chainSettleAt = -1;
        int _chainLinks;
        readonly List<(VisualElement e, float start, float dur)> _pops = new List<(VisualElement, float, float)>();

        void BuildChainCard()
        {
            _chainLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _chainLayer.AddToClassList("chain-layer");
            _bannerLayer.Add(_chainLayer);
            S.OnChainLink += e =>
            {
                // keep the deepest link; show the card once the chain has been quiet for a moment
                if (_chainLatest == null || e.depth >= _chainLatest.depth || e.magnitude > _chainLatest.magnitude) _chainLatest = e;
                _chainLinks++;
                _chainSettleAt = Time.unscaledTime + 2.2f;
            };
        }

        /// <summary>Called every frame from the main UI update.</summary>
        void TickChainCard()
        {
            float now = Time.unscaledTime;
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var (e, start, dur) = _pops[i];
                float u = Mathf.Clamp01((now - start) / dur);
                float k = 1 - Mathf.Pow(1 - u, 3);
                float overshoot = Mathf.Sin(u * Mathf.PI) * 0.04f;
                e.style.scale = new Scale(Vector3.one * (0.86f + 0.14f * k + overshoot));
                e.style.translate = new Translate(0, (1 - k) * -18, 0);
                if (u >= 1) _pops.RemoveAt(i);
            }
            if (_chainSettleAt < 0 || now < _chainSettleAt || _chainLatest == null) return;
            var last = _chainLatest;
            int links = _chainLinks;
            _chainSettleAt = -1; _chainLatest = null; _chainLinks = 0;
            if (S.Mode != SessionMode.Playing || links < 1) return;
            ShowChainCard(last);
        }

        /// <summary>Slide and scale an element in, driven by our own clock.</summary>
        void PopIn(VisualElement e, float seconds = 0.38f)
        {
            if (GameSettings.ReduceMotion) return;
            e.style.scale = new Scale(Vector3.one * 0.86f);
            _pops.Add((e, Time.unscaledTime, seconds));
        }

        List<SimEvent> ChainTo(World w, SimEvent e)
        {
            var path = new List<SimEvent> { e };
            var cur = e;
            int guard = 0;
            while (cur.causes.Length > 0 && guard++ < 40)
            {
                SimEvent next = null;
                foreach (var c in cur.causes)
                {
                    if (c < 0) continue;
                    var ce = w.Ledger.Get(c);
                    if (ce == null) continue;
                    // follow the branch that leads back to the player
                    if (ce.IsIntervention || ce.roots.Length > 0) { if (next == null || ce.depth > next.depth) next = ce; }
                }
                if (next == null) break;
                path.Add(next);
                cur = next;
                if (cur.IsIntervention) break;
            }
            path.Reverse();
            return path;
        }

        void ShowChainCard(SimEvent last)
        {
            var w = S.World;
            if (w == null) return;
            var path = ChainTo(w, last);
            if (path.Count < 2) return;
            _chainLayer.Clear();
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("chain-card");
            card.Add(L(path.Count >= 4 ? $"CHAIN REACTION · {path.Count - 1} LINKS" : "CAUSE AND EFFECT", "banner-kicker"));
            // show the start, the latest, and up to three links between
            var shown = new List<SimEvent>();
            if (path.Count <= 5) shown.AddRange(path);
            else { shown.Add(path[0]); shown.Add(path[1]); shown.Add(null); shown.Add(path[path.Count - 2]); shown.Add(path[path.Count - 1]); }
            for (int i = 0; i < shown.Count; i++)
            {
                var ev = shown[i];
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("chain-row");
                if (i > 0) row.Add(L("»", "chain-arrow"));
                var lbl = L(ev == null ? "..." : ev.title, i == shown.Count - 1 ? "chain-last" : i == 0 ? "chain-first" : "chain-mid");
                lbl.pickingMode = PickingMode.Ignore;
                row.Add(lbl);
                card.Add(row);
            }
            var trace = Btn("Trace the chain", () => ShowEvent(last), "btn-ghost");
            trace.style.marginTop = 6;
            card.Add(trace);
            _chainLayer.Add(card);
            PopIn(card);
            Audio?.TurningPoint(true);
            FadeOutAndRemove(card, 6500, 900);
        }
    }
}
