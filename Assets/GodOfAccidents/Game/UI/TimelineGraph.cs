using System;
using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    /// <summary>
    /// The web of history: every notable event as a glowing node in time, every cause as a luminous thread.
    /// Threads descending from a player intervention take that intervention's colour. Selecting a node lights up
    /// everything that led to it and everything it led to. Scroll to zoom, drag to pan.
    /// </summary>
    public sealed class TimelineGraph : VisualElement
    {
        World _w;
        readonly List<SimEvent> _shown = new List<SimEvent>();
        readonly Dictionary<int, Vector2> _pos = new Dictionary<int, Vector2>();
        readonly HashSet<int> _ancestry = new HashSet<int>();
        readonly HashSet<int> _descend = new HashSet<int>();
        readonly Dictionary<int, int> _rootHue = new Dictionary<int, int>();
        public int Selected = -1;
        public event Action<SimEvent> OnSelect;
        float _zoom = 1, _panX;
        Vector2 _dragStart; float _panStart; bool _dragging; bool _moved;
        readonly VisualElement _labels;
        long _t0, _t1;

        static readonly SimEventType[] Lanes =
        {
            SimEventType.Intervention,
        };

        static int LaneOf(SimEvent e)
        {
            switch (e.type)
            {
                case SimEventType.Intervention: return 0;
                case SimEventType.Trip: case SimEventType.ItemDropped: case SimEventType.ItemFound: case SimEventType.CartRoll:
                case SimEventType.LanternFall: case SimEventType.Injury: case SimEventType.WellBroken: case SimEventType.LightningStrike: return 1;
                case SimEventType.Meeting: case SimEventType.Courtship: case SimEventType.Marriage: case SimEventType.Birth: case SimEventType.Rivalry: case SimEventType.Death: case SimEventType.Emigration: return 2;
                case SimEventType.Spark: case SimEventType.Apprenticeship: case SimEventType.ComingOfAge: case SimEventType.Discovery: case SimEventType.Heroism: case SimEventType.Rescue: case SimEventType.Donation: return 3;
                case SimEventType.FireStarted: case SimEventType.FireSpread: case SimEventType.FireExtinguished: case SimEventType.BuildingDestroyed: case SimEventType.FoodLoss: case SimEventType.Famine: return 4;
                case SimEventType.Scarcity: case SimEventType.MerchantFortune: case SimEventType.Prosperity: case SimEventType.Construction: case SimEventType.PopulationMilestone: return 5;
                default: return 6;
            }
        }

        public static readonly string[] LaneNames = { "Your hand", "Accidents", "Lives & loves", "Minds & deeds", "Fire & loss", "Fortune", "Turning points" };

        public TimelineGraph()
        {
            style.flexGrow = 1;
            generateVisualContent += Draw;
            _labels = new VisualElement { pickingMode = PickingMode.Ignore };
            _labels.style.position = Position.Absolute;
            _labels.style.left = 0; _labels.style.top = 0; _labels.style.right = 0; _labels.style.bottom = 0;
            Add(_labels);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        public void SetWorld(World w)
        {
            _w = w;
            _shown.Clear(); _rootHue.Clear();
            int hue = 0;
            foreach (var e in w.Ledger.Events)
            {
                if (e.IsIntervention) { _rootHue[e.id] = hue++ % Palette.RootHues.Length; _shown.Add(e); continue; }
                if (e.magnitude >= 2 || (e.roots.Length > 0 && e.magnitude >= 1)) _shown.Add(e);
            }
            _t0 = World.StartTime;
            _t1 = Math.Max(w.Time, _t0 + SimTime.Year);
            Layout();
        }

        public void Select(int id)
        {
            Selected = id;
            _ancestry.Clear(); _descend.Clear();
            if (_w != null && id >= 0)
            {
                foreach (var a in _w.Ledger.Ancestry(id)) _ancestry.Add(a.id);
                foreach (var d in _w.Ledger.Descendants(id)) _descend.Add(d.id);
            }
            Layout();
        }

        float XOf(long t, float width)
        {
            double u = (double)(t - _t0) / Math.Max(1, _t1 - _t0);
            return (float)(40 + u * (width - 80) * _zoom + _panX);
        }

        void Layout()
        {
            _pos.Clear();
            var r = contentRect;
            if (_w == null || r.width < 10) { MarkDirtyRepaint(); return; }
            float laneH = (r.height - 40) / LaneNames.Length;
            var laneCount = new Dictionary<long, int>();
            foreach (var e in _shown)
            {
                int lane = LaneOf(e);
                float x = XOf(e.time, r.width);
                long bucket = lane * 100000 + (long)(x / 9);
                laneCount.TryGetValue(bucket, out int stack);
                laneCount[bucket] = stack + 1;
                float y = 30 + lane * laneH + laneH * 0.5f + ((stack % 5) - 2) * Mathf.Min(10, laneH * 0.16f);
                _pos[e.id] = new Vector2(x, y);
            }
            _labels.Clear();
            for (int i = 0; i < LaneNames.Length; i++)
            {
                var l = new Label(LaneNames[i].ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("tiny");
                l.style.position = Position.Absolute; l.style.left = 6; l.style.top = 30 + i * laneH + 2;
                _labels.Add(l);
            }
            // year ticks
            int years = (int)((_t1 - _t0) / SimTime.Year) + 1;
            int stepY = Math.Max(1, (int)(years / (8 * _zoom)) + 1);
            for (int y = 0; y <= years; y += stepY)
            {
                float x = XOf(y * SimTime.Year, r.width);
                if (x < 20 || x > r.width - 10) continue;
                var l = new Label("Y" + (y + 1)) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("tiny");
                l.style.position = Position.Absolute; l.style.left = x - 8; l.style.top = 4;
                _labels.Add(l);
            }
            // name the big moments
            int named = 0;
            foreach (var e in _shown)
            {
                bool hot = e.id == Selected || (e.magnitude >= 4 && named < 14) || (e.IsIntervention && named < 20);
                if (!hot || !_pos.TryGetValue(e.id, out var p)) continue;
                if (p.x < 0 || p.x > r.width) continue;
                named++;
                var l = new Label(Shorten(e.title, e.id == Selected ? 60 : 26)) { pickingMode = PickingMode.Ignore };
                l.AddToClassList(e.id == Selected ? "gold" : "small");
                l.style.position = Position.Absolute; l.style.left = p.x + 9; l.style.top = p.y - 9;
                l.style.fontSize = e.id == Selected ? 13 : 11;
                _labels.Add(l);
            }
            MarkDirtyRepaint();
        }

        static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "...";

        Color ColorOf(SimEvent e)
        {
            if (e.IsIntervention && _rootHue.TryGetValue(e.id, out var h)) return Palette.RootHues[h];
            if (e.roots.Length > 0 && _rootHue.TryGetValue(e.roots[e.roots.Length - 1], out var h2)) return Palette.RootHues[h2];
            return new Color(0.62f, 0.66f, 0.78f);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            if (_w == null) return;
            float laneH = (r.height - 40) / LaneNames.Length;
            // lane separators
            p.lineWidth = 1;
            p.strokeColor = new Color(1, 1, 1, 0.05f);
            for (int i = 0; i <= LaneNames.Length; i++)
            {
                p.BeginPath(); p.MoveTo(new Vector2(0, 30 + i * laneH)); p.LineTo(new Vector2(r.width, 30 + i * laneH)); p.Stroke();
            }
            // "now" line
            float nowX = XOf(_w.Time, r.width);
            p.strokeColor = new Color(1f, 0.8f, 0.45f, 0.35f);
            p.lineWidth = 2;
            p.BeginPath(); p.MoveTo(new Vector2(nowX, 20)); p.LineTo(new Vector2(nowX, r.height)); p.Stroke();

            bool focus = Selected >= 0;
            // threads
            foreach (var e in _shown)
            {
                if (!_pos.TryGetValue(e.id, out var b)) continue;
                foreach (var c in e.causes)
                {
                    if (!_pos.TryGetValue(c, out var a)) continue;
                    bool lit = focus && (e.id == Selected && _ancestry.Contains(c) || _ancestry.Contains(e.id) && _ancestry.Contains(c)
                                          || c == Selected && _descend.Contains(e.id) || _descend.Contains(c) && _descend.Contains(e.id));
                    var col = ColorOf(e);
                    float alpha = focus ? (lit ? 0.95f : 0.06f) : (e.roots.Length > 0 ? 0.55f : 0.18f);
                    p.strokeColor = new Color(Mathf.Min(1, col.r * 0.6f), Mathf.Min(1, col.g * 0.6f), Mathf.Min(1, col.b * 0.6f), alpha);
                    p.lineWidth = lit ? 2.6f : 1.4f;
                    p.BeginPath();
                    p.MoveTo(a);
                    float mx = (a.x + b.x) * 0.5f;
                    p.BezierCurveTo(new Vector2(mx, a.y), new Vector2(mx, b.y), b);
                    p.Stroke();
                }
            }
            // nodes
            foreach (var e in _shown)
            {
                if (!_pos.TryGetValue(e.id, out var c)) continue;
                if (c.x < -10 || c.x > r.width + 10) continue;
                var col = ColorOf(e);
                bool inFocus = !focus || e.id == Selected || _ancestry.Contains(e.id) || _descend.Contains(e.id);
                float rad = e.IsIntervention ? 6.5f : 2.5f + e.magnitude * 1.1f;
                if (e.id == Selected) rad += 3;
                float a = inFocus ? 1 : 0.15f;
                var glow = new Color(Mathf.Min(1, col.r * 0.5f), Mathf.Min(1, col.g * 0.5f), Mathf.Min(1, col.b * 0.5f), 0.25f * a);
                p.fillColor = glow;
                p.BeginPath(); p.Arc(c, rad * 2.2f, 0, 360); p.Fill();
                p.fillColor = new Color(Mathf.Min(1, col.r * 0.62f), Mathf.Min(1, col.g * 0.62f), Mathf.Min(1, col.b * 0.62f), a);
                p.BeginPath(); p.Arc(c, rad, 0, 360); p.Fill();
                if (e.turningPoint || e.IsIntervention)
                {
                    p.strokeColor = new Color(1, 0.92f, 0.75f, 0.8f * a);
                    p.lineWidth = 1.5f;
                    p.BeginPath(); p.Arc(c, rad + 3, 0, 360); p.Stroke();
                }
            }
        }

        void OnWheel(WheelEvent evt)
        {
            float old = _zoom;
            _zoom = Mathf.Clamp(_zoom * (evt.delta.y > 0 ? 0.88f : 1.14f), 1f, 60f);
            float mx = evt.localMousePosition.x;
            _panX = mx - (mx - _panX - 40) * (_zoom / old) - 40;
            ClampPan();
            Layout();
            evt.StopPropagation();
        }

        void ClampPan()
        {
            float w = contentRect.width;
            float content = (w - 80) * _zoom;
            _panX = Mathf.Clamp(_panX, -(content - (w - 80)), 0);
        }

        void OnDown(PointerDownEvent evt) { _dragging = true; _moved = false; _dragStart = evt.localPosition; _panStart = _panX; this.CapturePointer(evt.pointerId); }

        void OnMove(PointerMoveEvent evt)
        {
            if (!_dragging) return;
            var d = (Vector2)evt.localPosition - _dragStart;
            if (Mathf.Abs(d.x) > 4) _moved = true;
            if (_moved) { _panX = _panStart + d.x; ClampPan(); Layout(); }
        }

        void OnUp(PointerUpEvent evt)
        {
            this.ReleasePointer(evt.pointerId);
            _dragging = false;
            if (_moved) return;
            var lp = (Vector2)evt.localPosition;
            int best = -1; float bd = 14;
            foreach (var kv in _pos)
            {
                float d = Vector2.Distance(kv.Value, lp);
                if (d < bd) { bd = d; best = kv.Key; }
            }
            if (best >= 0) { Select(best); OnSelect?.Invoke(_w.Ledger.Get(best)); }
        }
    }
}
