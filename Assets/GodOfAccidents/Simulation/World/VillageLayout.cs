using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public struct FieldRect
    {
        public Vec2 center; public Vec2 size; public float rotation;
        public FieldRect(float x, float z, float w, float d, float rot = 0) { center = new Vec2(x, z); size = new Vec2(w, d); rotation = rot; }
    }

    public struct LotDef
    {
        public Vec2 pos; public string linkNode;
        public LotDef(float x, float z, string link) { pos = new Vec2(x, z); linkNode = link; }
    }

    /// <summary>
    /// Shared geography of Thistlewick Vale. The renderer reads the same definitions so the visible world and the
    /// simulated world never disagree about where the river, roads, fields and lots are.
    /// </summary>
    public static class VillageLayout
    {
        public const float WorldHalfSize = 210f;
        public const float RiverHalfWidth = 6f;
        public static readonly Vec2 OakPos = new Vec2(-48, 10);
        public static readonly Vec2 CartStart = new Vec2(-3, 44);
        public static readonly Vec2 CartEnd = new Vec2(-0.6f, 2.6f);
        public static readonly Vec2 LanternPos = new Vec2(-8f, 13.8f);
        public static readonly Vec2 LakeCenter = new Vec2(70, -150);
        public const float LakeRadius = 46f;

        public static float RiverX(float z) => 58f + 6f * (float)Math.Sin(z / 35f) + (z > 60 ? (z - 60) * 0.12f : 0f);

        public static float DistanceToRiver(Vec2 p) => Math.Abs(p.x - RiverX(p.z));

        public static readonly FieldRect[] Fields =
        {
            new FieldRect(-30, -52, 30, 22, 0.05f),
            new FieldRect(4, -56, 28, 22, -0.04f),
            new FieldRect(-22, -82, 34, 20, 0.08f),
            new FieldRect(16, -84, 26, 20, -0.06f),
        };

        public static readonly FieldRect Pasture = new FieldRect(36, -36, 20, 16, 0.1f);

        public static readonly Vec2[] PebbleSpots =
        {
            new Vec2(35f, 2.3f), new Vec2(-8f, -2.6f), new Vec2(2.6f, 8.5f), new Vec2(-17f, -25.5f),
            new Vec2(84f, 4.6f), new Vec2(18f, -1.4f), new Vec2(-22f, 14.5f), new Vec2(6f, -20f),
        };

        public static readonly LotDef[] InitialLots =
        {
            new LotDef(-6, -8, "Well"), new LotDef(8, -14, "J_S"), new LotDef(-17, 17.5f, "Tavern"), new LotDef(-28, 13, "J_NW"),
            new LotDef(-7, 32, "J_N"), new LotDef(12, 31, "J_N"), new LotDef(-34, -10, "J_W"), new LotDef(-13, -31, "J_SW"),
            new LotDef(10, -28, "J_SE"), new LotDef(37, 12, "Green"),
        };

        public static readonly LotDef[] ExpansionLots =
        {
            new LotDef(-40, 4, "Granary"), new LotDef(-42, -18, "J_W"), new LotDef(-6, -32, "J_S"), new LotDef(26, -11, "J_SE"),
            new LotDef(38, -14, "J_SE"), new LotDef(-22, 30, "J_NW"), new LotDef(14, 42, "J_N"), new LotDef(-14, 42, "J_N"),
            new LotDef(0, 50, "J_N"), new LotDef(26, 36, "J_NE"), new LotDef(-52, -18, "J_W"), new LotDef(44, -6, "J_E1"),
            new LotDef(-30, 40, "J_NW"), new LotDef(10, 54, "J_N"), new LotDef(-20, 54, "J_N"), new LotDef(32, 46, "J_NE"),
            new LotDef(-44, 34, "J_NW"), new LotDef(-56, 0, "Granary"), new LotDef(40, 24, "J_NE"), new LotDef(-34, 52, "J_NW"),
            new LotDef(22, 58, "J_N"), new LotDef(-4, 62, "J_N"), new LotDef(-48, -34, "J_SW"), new LotDef(28, -52, "J_SE"),
        };

        public static readonly HashSet<string> IndoorNodes = new HashSet<string> { "Tavern", "Chapel", "ElderHall", "Workshop", "Library" };

        /// <summary>Builds the road graph. Houses are linked later as their door nodes are created.</summary>
        public static void BuildNav(NavGraph nav)
        {
            nav.Add("Well", 0, 0, NodeKind.Place, 4.5f);
            nav.Add("Market", 12, -6, NodeKind.Place, 5f);
            nav.Add("Green", 24, 9, NodeKind.Place, 9f);
            nav.Add("Tavern", -10.5f, 13.5f, NodeKind.Place, 1.5f);
            nav.Add("Chapel", 4, 18.5f, NodeKind.Place, 1.5f);
            nav.Add("Forge", -10.5f, -9.5f, NodeKind.Place, 2.5f);
            nav.Add("Granary", -26, 4, NodeKind.Place, 2f);
            nav.Add("Barn", -23.5f, -27.5f, NodeKind.Place, 3f);
            nav.Add("Workshop", 16, 16.5f, NodeKind.Place, 1.5f);
            nav.Add("ElderHall", -2, -15.5f, NodeKind.Place, 1.5f);
            nav.Add("FieldW", -30, -52, NodeKind.Field, 12f);
            nav.Add("FieldE", 4, -56, NodeKind.Field, 11f);
            nav.Add("Pasture", 36, -36, NodeKind.Field, 8f);
            nav.Add("Riverbank", 44, -22, NodeKind.Place, 5f);
            nav.Add("ForestCamp", -72, 30, NodeKind.Place, 9f);
            nav.Add("MillSite", 49, 34, NodeKind.Place, 3f);
            nav.Add("Orchard", -40, 22, NodeKind.Place, 7f);
            nav.Add("BridgeW", 47, 0);
            nav.Add("BridgeE", 69, 0);
            nav.Add("J_E2", 92, 2);
            nav.Add("EastGate", 182, 6, NodeKind.Edge, 1f);
            nav.Add("J_N", 0, 13);
            nav.Add("J_S", 0, -12);
            nav.Add("J_W", -19, 0);
            nav.Add("J_E1", 30, 0);
            nav.Add("J_SW", -22, -32);
            nav.Add("J_SE", 20, -26);
            nav.Add("J_NE", 28, 17);
            nav.Add("J_NW", -26, 20);
            nav.Add("FieldS", -4, -82, NodeKind.Field, 14f);
            nav.Add("Hill", -10, -75, NodeKind.Place, 3f);
            nav.Add("ClockSite", 6, 4, NodeKind.Place, 2f);
            nav.Add("Library", -18, 26, NodeKind.Place, 1.5f);

            nav.Link("Well", "J_N"); nav.Link("Well", "J_S"); nav.Link("Well", "J_W"); nav.Link("Well", "Market");
            nav.Link("Well", "ClockSite");
            nav.Link("Market", "J_E1"); nav.Link("Market", "J_SE");
            nav.Link("J_E1", "Green"); nav.Link("J_E1", "BridgeW"); nav.Link("BridgeW", "BridgeE");
            nav.Link("BridgeE", "J_E2"); nav.Link("J_E2", "EastGate");
            nav.Link("J_N", "Chapel"); nav.Link("J_N", "Tavern"); nav.Link("J_N", "J_NE"); nav.Link("J_N", "Workshop");
            nav.Link("J_NE", "Workshop"); nav.Link("J_NE", "Green"); nav.Link("J_NE", "MillSite");
            nav.Link("J_W", "Granary"); nav.Link("J_W", "Tavern"); nav.Link("J_W", "Forge"); nav.Link("J_W", "J_NW");
            nav.Link("J_NW", "Orchard"); nav.Link("J_NW", "ForestCamp"); nav.Link("J_NW", "Library");
            nav.Link("J_S", "ElderHall"); nav.Link("J_S", "Forge"); nav.Link("J_S", "J_SW"); nav.Link("J_S", "J_SE");
            nav.Link("J_SW", "Barn"); nav.Link("J_SW", "FieldW");
            nav.Link("J_SE", "FieldE"); nav.Link("J_SE", "Pasture"); nav.Link("J_SE", "Riverbank");
            nav.Link("FieldW", "FieldS"); nav.Link("FieldE", "FieldS"); nav.Link("FieldS", "Hill");

            foreach (var n in nav.nodes)
                if (IndoorNodes.Contains(n.name)) n.kind = NodeKind.Place;
        }

        public struct CivicDef
        {
            public BuildingType type; public Vec2 pos; public Vec2 size; public string node; public string name; public bool flammable; public bool initial;
            public CivicDef(BuildingType t, float x, float z, float w, float d, string node, string name, bool flammable = true, bool initial = true)
            { type = t; pos = new Vec2(x, z); size = new Vec2(w, d); this.node = node; this.name = name; this.flammable = flammable; this.initial = initial; }
        }

        public static readonly CivicDef[] Civic =
        {
            new CivicDef(BuildingType.Well, 0, 0, 2.6f, 2.6f, "Well", "The Old Well", false),
            new CivicDef(BuildingType.Market, 12, -6, 8, 5, "Market", "Market Stalls"),
            new CivicDef(BuildingType.Tavern, -12, 9, 9, 7, "Tavern", "The Crooked Kettle"),
            new CivicDef(BuildingType.Chapel, 4, 24, 7, 10, "Chapel", "Chapel of the Quiet Bell", false),
            new CivicDef(BuildingType.Forge, -14, -12, 7, 6, "Forge", "Brack's Forge", false),
            new CivicDef(BuildingType.Granary, -26, 0, 6, 6, "Granary", "The Granary"),
            new CivicDef(BuildingType.Barn, -30, -24, 10, 8, "Barn", "Long Barn"),
            new CivicDef(BuildingType.Workshop, 16, 21, 6, 6, "Workshop", "Pell's Workshop"),
            new CivicDef(BuildingType.ElderHall, -2, -21, 9, 8, "ElderHall", "Moot Hall"),
            new CivicDef(BuildingType.Haystack, -20, -22, 3, 3, "Barn", "Haystack"),
            new CivicDef(BuildingType.Haystack, -38, -32, 3, 3, "Barn", "Haystack"),
            new CivicDef(BuildingType.Haystack, -21, -40, 3, 3, "FieldW", "Haystack"),
            new CivicDef(BuildingType.Mill, 51, 36, 8, 7, "MillSite", "Water Mill", true, false),
            new CivicDef(BuildingType.ClockTower, 6, 5, 4, 4, "ClockSite", "Clock Tower", false, false),
            new CivicDef(BuildingType.Library, -18, 31, 9, 7, "Library", "House of Letters", false, false),
            new CivicDef(BuildingType.Windmill, -10, -78, 6, 6, "Hill", "Windmill", true, false),
        };
    }
}
