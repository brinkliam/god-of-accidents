using UnityEngine;

namespace GOA.Game
{
    /// <summary>The single colour language of Thistlewick Vale. Everything visual draws from here.</summary>
    public static class Palette
    {
        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        // land
        public static readonly Color GrassA = H("#6f9e3e");
        public static readonly Color GrassB = H("#4f8436");
        public static readonly Color GrassDry = H("#b5b45f");
        public static readonly Color Meadow = H("#8db84a");
        public static readonly Color Dirt = H("#b08a5e");
        public static readonly Color DirtDark = H("#7a5a3c");
        public static readonly Color Soil = H("#6e4a33");
        public static readonly Color SoilLight = H("#8b6343");
        public static readonly Color Sand = H("#d8c595");
        public static readonly Color WetBank = H("#6f7a4b");
        public static readonly Color Rock = H("#8e877e");
        public static readonly Color RockDark = H("#645f5a");
        public static readonly Color Snow = H("#eef2f8");
        public static readonly Color Cobble = H("#a59a8a");

        // foliage
        public static readonly Color Pine = H("#2f6b45");
        public static readonly Color PineDark = H("#24573a");
        public static readonly Color Leaf = H("#5c9a3c");
        public static readonly Color LeafLight = H("#86b84a");
        public static readonly Color Birch = H("#a6c45a");
        public static readonly Color Bark = H("#6b4a32");
        public static readonly Color BarkLight = H("#e8e2d2");
        public static readonly Color Bush = H("#4f8a3a");
        public static readonly Color Wheat = H("#d9b65a");
        public static readonly Color WheatGreen = H("#8fb34e");

        // architecture
        public static readonly Color Plaster = H("#ecdcb8");
        public static readonly Color PlasterWarm = H("#e9d1a8");
        public static readonly Color Timber = H("#5b3d2a");
        public static readonly Color Thatch = H("#c6923f");
        public static readonly Color ThatchDark = H("#a57d3e");
        public static readonly Color Shingle = H("#7d5a44");
        public static readonly Color TileRed = H("#b5543c");
        public static readonly Color TileSlate = H("#4f5866");
        public static readonly Color Stone = H("#b3aa9a");
        public static readonly Color StoneDark = H("#8a8174");
        public static readonly Color Brick = H("#a5523e");
        public static readonly Color Door = H("#6e3f2a");
        public static readonly Color Window = H("#2c3440");
        public static readonly Color WindowGlow = H("#ffc46b");
        public static readonly Color Charred = H("#2b2622");
        public static readonly Color Cloth1 = H("#c4533f");
        public static readonly Color Cloth2 = H("#3f7bb0");
        public static readonly Color Cloth3 = H("#e6c14e");
        public static readonly Color Iron = H("#3d3f44");
        public static readonly Color Brass = H("#d4a64a");

        // people
        public static readonly Color[] Skin = { H("#f3d2b5"), H("#e8b892"), H("#d39b72"), H("#b47a52"), H("#8d5a3b"), H("#6a4029") };
        public static readonly Color[] Hair = { H("#2b1e16"), H("#5a3a22"), H("#8a5a2e"), H("#c8923f"), H("#e0c27a"), H("#b9b4ad"), H("#7a2f1f") };
        public static readonly Color[] Outfit =
        {
            H("#7c4f8f"), H("#3f6f9a"), H("#a8483a"), H("#4f7f4a"), H("#c08a3e"), H("#5a5f74"), H("#9a6a4a"), H("#2f5f68"), H("#b56a7a"), H("#6d7b3a")
        };

        // ui / magic
        public static readonly Color Gold = H("#f2c46d");
        public static readonly Color Fate = new Color(1.8f, 1.25f, 0.45f, 1);
        public static readonly Color FateCool = new Color(0.6f, 1.2f, 1.9f, 1);
        public static readonly Color Ember = new Color(2.4f, 0.9f, 0.25f, 1);

        public static readonly Color[] RootHues =
        {
            new Color(1.9f, 1.3f, 0.45f), new Color(0.55f, 1.4f, 2.0f), new Color(1.9f, 0.6f, 1.3f), new Color(0.6f, 1.9f, 0.9f),
            new Color(2.0f, 0.75f, 0.5f), new Color(1.3f, 0.8f, 2.0f)
        };

        public static Color Vary(Color c, float amount, int seed)
        {
            var r = new System.Random(seed);
            float k = 1 + ((float)r.NextDouble() * 2 - 1) * amount;
            float hs = ((float)r.NextDouble() * 2 - 1) * amount * 0.25f;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var o = Color.HSVToRGB(Mathf.Repeat(h + hs * 0.1f, 1), Mathf.Clamp01(s * (1 + hs)), Mathf.Clamp01(v * k));
            o.a = c.a;
            return o;
        }
    }
}
