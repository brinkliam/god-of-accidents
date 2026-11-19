using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>
    /// A journal of the kinds of chain reaction the player has personally caused. Entries unlock only from events
    /// that genuinely trace back to a player intervention.
    /// </summary>
    public static class Codex
    {
        public struct Entry { public string id, title, blurb; public SimEventType type; }

        public static readonly Entry[] All =
        {
            new Entry { id = "stumble", type = SimEventType.Trip, title = "The Stumble", blurb = "A pebble in the right place is a kind of fate." },
            new Entry { id = "spill", type = SimEventType.ItemDropped, title = "Fallen Treasures", blurb = "Something lost is something someone else will find." },
            new Entry { id = "spark", type = SimEventType.Spark, title = "A Spark of Wonder", blurb = "You woke a mind that would have slept forever." },
            new Entry { id = "mentor", type = SimEventType.Apprenticeship, title = "The Master and the Apprentice", blurb = "A chance meeting became a lifetime of learning." },
            new Entry { id = "meeting", type = SimEventType.Meeting, title = "Strangers No More", blurb = "Two lives crossed because one paused." },
            new Entry { id = "love", type = SimEventType.Courtship, title = "Love at First Glimmer", blurb = "A glance, a laugh, a courtship." },
            new Entry { id = "wedding", type = SimEventType.Marriage, title = "Bells Over the Vale", blurb = "A wedding no one would have attended - but for you." },
            new Entry { id = "newlife", type = SimEventType.Birth, title = "A Life Unwritten", blurb = "Someone now exists who never would have." },
            new Entry { id = "ember", type = SimEventType.FireStarted, title = "The Ember's Path", blurb = "Wind, spark, thatch. You know how this goes." },
            new Entry { id = "ash", type = SimEventType.BuildingDestroyed, title = "Ashes", blurb = "Some things cannot be unburned." },
            new Entry { id = "hero", type = SimEventType.Heroism, title = "The Unlikely Hero", blurb = "Courage needs an occasion. You provided one." },
            new Entry { id = "rescue", type = SimEventType.Rescue, title = "Out of Harm's Way", blurb = "A runaway cart, a brave shove." },
            new Entry { id = "well", type = SimEventType.WellBroken, title = "The Day the Well Broke", blurb = "Every household walked to the river. Paths crossed." },
            new Entry { id = "hunger", type = SimEventType.Scarcity, title = "Dear Bread", blurb = "Scarcity makes some people poor and others powerful." },
            new Entry { id = "fortune", type = SimEventType.MerchantFortune, title = "A Fortune Made", blurb = "One family's loss, another's ledger." },
            new Entry { id = "politics", type = SimEventType.Election, title = "The Accidental Elder", blurb = "The vale chose a leader you made possible." },
            new Entry { id = "law", type = SimEventType.PolicyChange, title = "A New Law", blurb = "Small causes, written into law." },
            new Entry { id = "invention", type = SimEventType.Invention, title = "The Invention", blurb = "History bends around a single idea." },
            new Entry { id = "era", type = SimEventType.EraChange, title = "A New Age", blurb = "The whole vale looks different now." },
        };

        static HashSet<string> _found;

        static HashSet<string> Found
        {
            get
            {
                if (_found != null) return _found;
                _found = new HashSet<string>(PlayerPrefs.GetString("goa_codex", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
                return _found;
            }
        }

        public static bool IsFound(string id) => Found.Contains(id);
        public static int FoundCount => Found.Count;

        /// <summary>Returns the newly unlocked entry, if any.</summary>
        public static Entry? Consider(SimEvent e)
        {
            if (e.roots.Length == 0) return null;
            foreach (var en in All)
            {
                if (en.type != e.type || Found.Contains(en.id)) continue;
                Found.Add(en.id);
                PlayerPrefs.SetString("goa_codex", string.Join(",", Found));
                return en;
            }
            return null;
        }
    }
}
