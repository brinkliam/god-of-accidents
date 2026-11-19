using System;
using GOA.Sim;

namespace GOA.Tests
{
    static class SimTestUtil
    {
        public static long DayStart(int day) => day * SimTime.Day;
        public static long At(int day, double hour) => day * SimTime.Day + (long)(hour * SimTime.Hour);

        public static Prop Pebble(World w, int index)
        {
            int k = 0;
            foreach (var p in w.Props) if (p.type == PropType.Pebble) { if (k == index) return p; k++; }
            return null;
        }

        public static Prop Find(World w, PropType t)
        {
            foreach (var p in w.Props) if (p.type == t) return p;
            return null;
        }

        /// <summary>Scan forward minute by minute until a predicate holds (advancing the world). Returns false if never.</summary>
        public static bool AdvanceUntil(World w, long until, Func<World, bool> pred, long step = 60)
        {
            while (w.Time < until)
            {
                if (pred(w)) return true;
                w.AdvanceTo(Math.Min(until, w.Time + step));
            }
            return pred(w);
        }

        public static int CountType(World w, SimEventType t)
        {
            int n = 0;
            foreach (var e in w.Ledger.Events) if (e.type == t) n++;
            return n;
        }

        public static SimEvent FirstOfType(World w, SimEventType t, int fromId = 0)
        {
            for (int i = fromId; i < w.Ledger.Count; i++) if (w.Ledger.Events[i].type == t) return w.Ledger.Events[i];
            return null;
        }

        /// <summary>The first day the travelling merchant carries the given cargo.</summary>
        public static int DayWithCargo(ulong seed, ItemType item, int fromDay = 1, int maxDays = 200)
        {
            var merchantId = -1;
            var probe = new World(seed, ScenarioDef.Sandbox);
            foreach (var c in probe.Citizens) if (c.offMapResident) merchantId = c.id;
            for (int d = fromDay; d < maxDays; d++)
            {
                if (SimTime.IsHolyDay(d * SimTime.Day)) continue;
                double r = Rand.U01(seed, d, merchantId, 0, (int)Salt.Cargo);
                ItemType it = r < 0.3 ? ItemType.Gear : r < 0.5 ? ItemType.Lens : r < 0.7 ? ItemType.Book : ItemType.None;
                if (it == item) return d;
            }
            return -1;
        }
    }
}
