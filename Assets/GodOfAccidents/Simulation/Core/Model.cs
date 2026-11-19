using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public enum Occupation
    {
        Child, Farmer, Smith, Shopkeeper, Merchant, Innkeeper, Priest, Woodcutter, Shepherd,
        Fisher, Tinkerer, Scholar, Builder, Elder, Miller, Toddler
    }

    [Flags]
    public enum Trait
    {
        None = 0,
        Curious = 1 << 0,
        Kind = 1 << 1,
        Greedy = 1 << 2,
        Brave = 1 << 3,
        Timid = 1 << 4,
        Ambitious = 1 << 5,
        Romantic = 1 << 6,
        Pious = 1 << 7,
        Clumsy = 1 << 8,
        Charismatic = 1 << 9,
        Reckless = 1 << 10,
        Diligent = 1 << 11,
    }

    public enum ActivityKind
    {
        Sleep, Travel, Work, Eat, Socialize, Play, Pray, Trade, Idle, Distracted, FightFire, Flee,
        Build, Study, OffMap, Recover, Festival, Fetch
    }

    public enum PowerType { Nudge, Gust, Distract, Drop, Delay, Redirect }

    public enum BuildingType
    {
        House, Well, Market, Tavern, Chapel, Forge, Granary, Barn, Workshop, ElderHall,
        Haystack, Mill, ClockTower, Library, Windmill, Wall
    }

    public enum PropType { Pebble, Lantern, Cart, GroundItem, CrowNest }

    public enum ItemType { None, Gear, Lens, Book, Coin, Key, Feather }

    public enum Weather { Clear, Cloudy, Rain, Storm, Snow }

    public enum Era { Hamlet, Village, Town, Renaissance, Industrial }

    public enum Policy { Tradition, Trade, Faith, Watch, Commons, Learning }

    public enum RelationKind { Acquaintance, Friend, Rival, Courting, Spouse, Family, Mentor, Apprentice }

    public enum SimEventType
    {
        Intervention,
        // micro
        Trip, ItemDropped, ItemFound, ItemSold, Spark, Meeting, Apprenticeship, Courtship, Marriage, Birth,
        Death, ComingOfAge, LanternFall, FireStarted, FireSpread, FireExtinguished, BuildingDestroyed,
        Heroism, CartRoll, WellBroken, Injury, Rescue, Donation, Gamble, Brawl, Rivalry, Startled,
        // macro
        FoodLoss, Scarcity, Prosperity, MerchantFortune, Election, PolicyChange, Invention, Construction,
        EraChange, Emigration, Famine, Storm, LightningStrike, Festival, Discovery, PopulationMilestone,
        ChallengeComplete, Glimpse
    }

    [Serializable]
    public class Relation
    {
        public int otherId;
        public float affinity; // -100..100
        public RelationKind kind;
        public int causeEventId = -1;
    }

    [Serializable]
    public class Memory
    {
        public long time;
        public string text;
        public int eventId;
    }

    public class Activity
    {
        public ActivityKind kind;
        public long start;
        public long end;
        public int nodeId = -1;          // logical place
        public Vec2 position;            // standing position for non-travel activities
        public List<Vec2> path;          // travel polyline
        public float pathLength;
        public ActivityKind followUp;    // what they intend to do on arrival
        public long followUpUntil;
        public int followUpNode = -1;
        public int serial;
        public int causeEventId = -1;
    }

    public class Citizen
    {
        public int id;
        public string firstName;
        public string familyName;
        public bool female;
        public long birthTime;
        public bool alive = true;
        public bool emigrated;
        public long deathTime = -1;
        public int motherId = -1, fatherId = -1, spouseId = -1;
        public List<int> children = new List<int>();
        public Occupation occupation;
        public Trait traits;
        public float craft, lore, charm, strength;
        public float hunger, mood = 0.7f, energy = 1f;
        public int wealth;
        public int fame;
        public int homeBuildingId = -1;
        public int workNodeId = -1;
        public bool offMapResident;      // the travelling merchant lives beyond the pass
        public Dictionary<int, Relation> relations = new Dictionary<int, Relation>();
        public List<Memory> memories = new List<Memory>();
        /// <summary>Named marks on a person ("spark:engineering"), each tied to the event that caused it.</summary>
        public Dictionary<string, int> marks = new Dictionary<string, int>();
        public int injuredUntilDay = -1;
        public int hungryDays;
        public string goal = "";
        public Activity activity;
        public int activitySerial;
        public ItemType carrying;        // item in hand
        public int carryingCause = -1;
        public string cargo = "";        // merchant's goods for the day
        public ItemType cargoItem;
        public int occupationCause = -1;
        public int birthCause = -1;
        public List<int> renown = new List<int>();   // events that earned this person standing
        public int skinTone, hairColor, outfitSeed;

        public string Name => firstName + " " + familyName;
        public bool Has(Trait t) => (traits & t) != 0;
        public int AgeYears(long now) => (int)((now - birthTime) / SimTime.Year);
        public bool IsChild(long now) => AgeYears(now) < 14;
        public bool IsActive => alive && !emigrated;
        public bool HasMark(string m) => marks.ContainsKey(m);

        public Relation Rel(int other)
        {
            relations.TryGetValue(other, out var r);
            return r;
        }
    }

    public class Building
    {
        public int id;
        public BuildingType type;
        public Vec2 pos;
        public float rotation; // radians, facing
        public Vec2 size;
        public int nodeId = -1;          // door node
        public int lotId = -1;
        public bool destroyed;
        public bool flammable = true;
        public float buildProgress = 1f; // <1 while under construction
        public float workRequired;
        public int tier;                 // visual architecture tier
        public int ownerFamilyId = -1;
        public int causeEventId = -1;    // event that created it (construction)
        public long builtTime;
        public string name = "";
        public int variant;
        public bool Complete => buildProgress >= 1f && !destroyed;
    }

    public class Prop
    {
        public int id;
        public PropType type;
        public Vec2 pos;
        public Vec2 originalPos;
        public bool active = true;       // false once consumed/broken/picked up
        public ItemType item;
        public int causeEventId = -1;    // most recent event that put it where it is
        public float radius = 0.8f;
        public bool nudgeable;
        public int state;                // lantern: 0 hung,1 fallen ; cart: 0 parked,1 rolling,2 crashed
        public List<ItemType> hoard;     // crow nest contents
        public string label = "";
        public long placedTime;
    }

    public class Fire
    {
        public int id;
        public int buildingId;
        public Vec2 pos;
        public float intensity;
        public bool active = true;
        public int causeEventId;
        public long startTime;
        public int firefighters;
        public long burnedAtMaxSince = -1;
    }

    public class CartRoll
    {
        public Vec2 from, to;
        public long start, end;
        public int causeEventId;
        public bool done;
    }

    public class WindGust
    {
        public Vec2 origin;
        public Vec2 dir;
        public float strength;
        public long start, end;
        public int causeEventId;
    }

    public class ConstructionProject
    {
        public int buildingId;
        public int causeEventId;
    }
}
