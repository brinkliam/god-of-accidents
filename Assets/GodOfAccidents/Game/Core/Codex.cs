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
        public struct Entry { public string id, title, blurb, hint; public SimEventType type; }

        public static readonly Entry[] All =
        {
            new Entry { id = "stumble", hint = "Nudge a pebble into the path of someone walking by.", type = SimEventType.Trip, title = "The Stumble", blurb = "A pebble in the right place is a kind of fate." },
            new Entry { id = "spill", hint = "Trip someone who is carrying something.", type = SimEventType.ItemDropped, title = "Fallen Treasures", blurb = "Something lost is something someone else will find." },
            new Entry { id = "spark", hint = "Put a gear, lens or book where a curious child will find it.", type = SimEventType.Spark, title = "A Spark of Wonder", blurb = "You woke a mind that would have slept forever." },
            new Entry { id = "mentor", hint = "Stop a curious child near the tinkerer or the scholar.", type = SimEventType.Apprenticeship, title = "The Master and the Apprentice", blurb = "A chance meeting became a lifetime of learning." },
            new Entry { id = "meeting", hint = "Distract someone outdoors while others are nearby.", type = SimEventType.Meeting, title = "Strangers No More", blurb = "Two lives crossed because one paused." },
            new Entry { id = "friends", hint = "Help two villagers meet, then let them keep meeting.", type = SimEventType.Friendship, title = "Friends for Life", blurb = "They would have passed each other by. Now they save each other a seat." },
            new Entry { id = "love", hint = "Bring two unattached adults together with a glimmer.", type = SimEventType.Courtship, title = "Love at First Glimmer", blurb = "A glance, a laugh, a courtship." },
            new Entry { id = "wedding", hint = "A courtship you started may end at the chapel - give it time.", type = SimEventType.Marriage, title = "Bells Over the Vale", blurb = "A wedding no one would have attended - but for you." },
            new Entry { id = "newlife", hint = "A marriage you caused, and a few seasons.", type = SimEventType.Birth, title = "A Life Unwritten", blurb = "Someone now exists who never would have." },
            new Entry { id = "ember", hint = "A gust near the forge, or the tavern lantern after dark.", type = SimEventType.FireStarted, title = "The Ember's Path", blurb = "Wind, spark, thatch. You know how this goes." },
            new Entry { id = "ash", hint = "Let a fire you started run its course.", type = SimEventType.BuildingDestroyed, title = "Ashes", blurb = "Some things cannot be unburned." },
            new Entry { id = "hero", hint = "Start a fire somewhere brave villagers can reach it.", type = SimEventType.Heroism, title = "The Unlikely Hero", blurb = "Courage needs an occasion. You provided one." },
            new Entry { id = "rescue", hint = "Loose the hay cart while someone is on the chapel lane.", type = SimEventType.Rescue, title = "Out of Harm's Way", blurb = "A runaway cart, a brave shove." },
            new Entry { id = "well", hint = "The hay cart rolls toward the green... and the well.", type = SimEventType.WellBroken, title = "The Day the Well Broke", blurb = "Every household walked to the river. Paths crossed." },
            new Entry { id = "hunger", hint = "Spill enough food and the market notices.", type = SimEventType.Scarcity, title = "Dear Bread", blurb = "Scarcity makes some people poor and others powerful." },
            new Entry { id = "fortune", hint = "Make sure a merchant's goods end up in the right hands.", type = SimEventType.MerchantFortune, title = "A Fortune Made", blurb = "One family's loss, another's ledger." },
            new Entry { id = "politics", hint = "Change who is liked and respected before an election.", type = SimEventType.Election, title = "The Accidental Elder", blurb = "The vale chose a leader you made possible." },
            new Entry { id = "law", hint = "An Elder you helped choose may change the rules.", type = SimEventType.PolicyChange, title = "A New Law", blurb = "Small causes, written into law." },
            new Entry { id = "invention", hint = "A sparked mind, a mentor, and years of work.", type = SimEventType.Invention, title = "The Invention", blurb = "History bends around a single idea." },
            new Entry { id = "era", hint = "Inventions and growth together move the vale into a new age.", type = SimEventType.EraChange, title = "A New Age", blurb = "The whole vale looks different now." },
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
