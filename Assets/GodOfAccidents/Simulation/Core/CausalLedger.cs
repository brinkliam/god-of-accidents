using System;
using System.Collections.Generic;
using System.Text;

namespace GOA.Sim
{
    /// <summary>
    /// One recorded happening. Causes are explicit references to earlier events whose resulting state
    /// was actually read when this event was produced. Roots are the player interventions upstream.
    /// </summary>
    public class SimEvent
    {
        public int id;
        public long time;
        public SimEventType type;
        public string title;
        public string detail;
        public Vec2 pos;
        public bool hasPos;
        public int[] causes = Array.Empty<int>();
        public int[] actors = Array.Empty<int>();
        public int[] roots = Array.Empty<int>();   // intervention event ids upstream (sorted)
        public int depth;                          // length of longest causal path from a root/origin
        public int magnitude;                      // 0 trivial .. 5 world-changing
        public bool turningPoint;
        public bool IsIntervention => type == SimEventType.Intervention;
        public PowerType power;                    // valid when intervention
    }

    public sealed class CausalLedger
    {
        readonly List<SimEvent> _events = new List<SimEvent>();
        public IReadOnlyList<SimEvent> Events => _events;
        public int Count => _events.Count;
        public event Action<SimEvent> OnRecorded;

        public SimEvent Get(int id) => id >= 0 && id < _events.Count ? _events[id] : null;

        public SimEvent Record(long time, SimEventType type, string title, string detail, int magnitude,
            int[] causes = null, int[] actors = null, Vec2? pos = null, bool turningPoint = false)
        {
            var e = new SimEvent
            {
                id = _events.Count,
                time = time,
                type = type,
                title = title,
                detail = detail ?? "",
                magnitude = magnitude,
                turningPoint = turningPoint || magnitude >= 4,
                actors = actors ?? Array.Empty<int>(),
            };
            if (pos.HasValue) { e.pos = pos.Value; e.hasPos = true; }

            // keep only valid, earlier causes, de-duplicated and ordered
            if (causes != null && causes.Length > 0)
            {
                var set = new SortedSet<int>();
                foreach (var c in causes) if (c >= 0 && c < e.id) set.Add(c);
                e.causes = new int[set.Count];
                set.CopyTo(e.causes);
            }

            var roots = new SortedSet<int>();
            int depth = 0;
            foreach (var c in e.causes)
            {
                var ce = _events[c];
                if (ce.IsIntervention) roots.Add(ce.id);
                foreach (var r in ce.roots) roots.Add(r);
                depth = Math.Max(depth, ce.depth + 1);
            }
            if (type == SimEventType.Intervention) depth = 0;
            e.depth = depth;
            e.roots = new int[roots.Count];
            roots.CopyTo(e.roots);
            _events.Add(e);
            OnRecorded?.Invoke(e);
            return e;
        }

        /// <summary>All events that this event transitively depends on (excluding itself), oldest first.</summary>
        public List<SimEvent> Ancestry(int id)
        {
            var result = new List<SimEvent>();
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            var start = Get(id);
            if (start == null) return result;
            foreach (var c in start.causes) stack.Push(c);
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                if (!seen.Add(c)) continue;
                var ce = _events[c];
                result.Add(ce);
                foreach (var cc in ce.causes) stack.Push(cc);
            }
            result.Sort((a, b) => a.id.CompareTo(b.id));
            return result;
        }

        /// <summary>All events that transitively descend from this event, oldest first.</summary>
        public List<SimEvent> Descendants(int id)
        {
            var result = new List<SimEvent>();
            var reach = new HashSet<int> { id };
            for (int i = id + 1; i < _events.Count; i++)
            {
                var e = _events[i];
                foreach (var c in e.causes)
                {
                    if (reach.Contains(c)) { reach.Add(e.id); result.Add(e); break; }
                }
            }
            return result;
        }

        /// <summary>The single most significant chain ending at id: follows the deepest cause each step.</summary>
        public List<SimEvent> PrimaryChain(int id)
        {
            var chain = new List<SimEvent>();
            var e = Get(id);
            while (e != null)
            {
                chain.Add(e);
                if (e.causes.Length == 0) break;
                SimEvent best = null;
                foreach (var c in e.causes)
                {
                    var ce = _events[c];
                    if (best == null || ce.depth > best.depth || (ce.depth == best.depth && ce.roots.Length > best.roots.Length)) best = ce;
                }
                e = best;
            }
            chain.Reverse();
            return chain;
        }

        public bool DescendsFrom(int id, int ancestorId)
        {
            if (id == ancestorId) return true;
            foreach (var a in Ancestry(id)) if (a.id == ancestorId) return true;
            return false;
        }

        public string DescribeChain(int id)
        {
            var sb = new StringBuilder();
            foreach (var e in PrimaryChain(id))
            {
                sb.Append(SimTime.FormatShort(e.time)).Append(" - ").Append(e.title).Append('\n');
            }
            return sb.ToString();
        }
    }
}
