using System.Collections.Generic;

namespace GOA.Sim
{
    public enum TaskKind
    {
        Decide, Encounter, DailyTick, FireUpdate, Intervention, NoticeScan, CartStep, BirdArrive,
        LanternIgnite, GustTick, EnergyGrant, WindCheck, Lightning, Ambient, AmbientFollow
    }

    public struct ScheduledTask
    {
        public long time;
        public long seq;
        public TaskKind kind;
        public int a, b, c;
    }

    /// <summary>Binary min-heap ordered by (time, insertion sequence) - fully deterministic.</summary>
    public sealed class EventQueue
    {
        readonly List<ScheduledTask> _heap = new List<ScheduledTask>(1024);
        long _seq;
        public int Count => _heap.Count;

        static bool Less(in ScheduledTask x, in ScheduledTask y) => x.time < y.time || (x.time == y.time && x.seq < y.seq);

        public void Push(long time, TaskKind kind, int a = 0, int b = 0, int c = 0)
        {
            var t = new ScheduledTask { time = time, seq = _seq++, kind = kind, a = a, b = b, c = c };
            _heap.Add(t);
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(_heap[i], _heap[p])) break;
                (_heap[i], _heap[p]) = (_heap[p], _heap[i]);
                i = p;
            }
        }

        public ScheduledTask Peek() => _heap[0];

        public ScheduledTask Pop()
        {
            var top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            int i = 0;
            int n = _heap.Count;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < n && Less(_heap[l], _heap[m])) m = l;
                if (r < n && Less(_heap[r], _heap[m])) m = r;
                if (m == i) break;
                (_heap[i], _heap[m]) = (_heap[m], _heap[i]);
                i = m;
            }
            return top;
        }
    }
}
