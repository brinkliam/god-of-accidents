using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>
    /// Procedural architecture. Every building type has its own silhouette, and houses evolve through five
    /// architectural tiers (thatch → timber → stone → brick → industrial) as the civilisation advances.
    /// Submesh 0 = body, 1 = windows (glow at night), 2 = always-hot (forge coals, clock face).
    /// </summary>
    public static class BuildingFactory
    {
        public struct Result
        {
            public Mesh mesh;
            public Mesh spinner;        // waterwheel / windmill sails
            public Vector3 spinnerPos;  // local
            public Vector3 spinnerAxis; // local
            public Vector3 chimney;     // local, for smoke
            public float height;
        }

        static readonly Color[] RoofThatch = { Palette.Thatch, Palette.ThatchDark, new Color(0.74f, 0.6f, 0.36f) };
        static readonly Color[] RoofTier1 = { Palette.Shingle, new Color(0.45f, 0.33f, 0.26f), new Color(0.55f, 0.42f, 0.3f) };
        static readonly Color[] RoofTier2 = { Palette.TileRed, new Color(0.62f, 0.3f, 0.22f), Palette.TileSlate };
        static readonly Color[] Walls = { Palette.Plaster, Palette.PlasterWarm, new Color(0.93f, 0.88f, 0.78f), new Color(0.86f, 0.9f, 0.82f) };

        public static Result Build(Building b)
        {
            var k = new MeshKit();
            var r = new Result();
            switch (b.type)
            {
                case BuildingType.House: House(k, b, ref r); break;
                case BuildingType.Well: Well(k, ref r); break;
                case BuildingType.Market: Market(k, b, ref r); break;
                case BuildingType.Tavern: Tavern(k, b, ref r); break;
                case BuildingType.Chapel: Chapel(k, b, ref r); break;
                case BuildingType.Forge: Forge(k, b, ref r); break;
                case BuildingType.Granary: Granary(k, b, ref r); break;
                case BuildingType.Barn: Barn(k, b, ref r); break;
                case BuildingType.Workshop: Workshop(k, b, ref r); break;
                case BuildingType.ElderHall: Hall(k, b, ref r); break;
                case BuildingType.Haystack: Haystack(k, b, ref r); break;
                case BuildingType.Mill: Mill(k, b, ref r); break;
                case BuildingType.ClockTower: ClockTower(k, b, ref r); break;
                case BuildingType.Library: Library(k, b, ref r); break;
                case BuildingType.Windmill: Windmill(k, b, ref r); break;
                default: k.Box(new Vector3(0, 1.5f, 0), new Vector3(b.size.x, 3, b.size.z), Palette.Stone); break;
            }
            k.Sub(2); // always three submeshes so each of the three materials draws exactly one
            r.mesh = k.ToMesh(b.type.ToString());
            return r;
        }

        // ------------------------------------------------------------------ shared pieces

        static void Foundation(MeshKit k, float w, float d, Color c) => k.Box(new Vector3(0, 0.15f, 0), new Vector3(w + 0.3f, 0.6f, d + 0.3f), c);

        static void Door(MeshKit k, float zFace, float x, float h = 1.9f, float w = 1f)
        {
            k.Sub(0);
            k.Box(new Vector3(x, h * 0.5f + 0.4f, zFace + 0.04f), new Vector3(w, h, 0.12f), Palette.Door);
            k.Box(new Vector3(x, h + 0.48f, zFace + 0.06f), new Vector3(w + 0.25f, 0.16f, 0.14f), Palette.Timber);
        }

        static void WindowZ(MeshKit k, float x, float y, float z, float dir, float w = 0.8f, float h = 0.8f)
        {
            k.Sub(1);
            k.Box(new Vector3(x, y, z + 0.03f * dir), new Vector3(w, h, 0.08f), Palette.Window);
            k.Sub(0);
            k.Box(new Vector3(x, y - h * 0.5f - 0.06f, z + 0.08f * dir), new Vector3(w + 0.2f, 0.1f, 0.18f), Palette.Timber);
            k.Box(new Vector3(x, y, z + 0.05f * dir), new Vector3(0.07f, h, 0.1f), Palette.Timber);
        }

        static void WindowX(MeshKit k, float x, float y, float z, float dir, float w = 0.8f, float h = 0.8f)
        {
            k.Sub(1);
            k.Box(new Vector3(x + 0.03f * dir, y, z), new Vector3(0.08f, h, w), Palette.Window);
            k.Sub(0);
            k.Box(new Vector3(x + 0.08f * dir, y - h * 0.5f - 0.06f, z), new Vector3(0.18f, 0.1f, w + 0.2f), Palette.Timber);
        }

        static void TimberFrame(MeshKit k, float w, float d, float y0, float h)
        {
            float hw = w * 0.5f + 0.02f, hd = d * 0.5f + 0.02f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    k.Box(new Vector3(sx * hw, y0 + h * 0.5f, sz * hd), new Vector3(0.22f, h, 0.22f), Palette.Timber);
            k.Box(new Vector3(0, y0 + h, hd), new Vector3(w + 0.2f, 0.18f, 0.12f), Palette.Timber);
            k.Box(new Vector3(0, y0 + h, -hd), new Vector3(w + 0.2f, 0.18f, 0.12f), Palette.Timber);
            k.Box(new Vector3(0, y0 + h * 0.5f, hd), new Vector3(w, 0.14f, 0.1f), Palette.Timber);
            k.Box(new Vector3(0, y0 + h * 0.5f, -hd), new Vector3(w, 0.14f, 0.1f), Palette.Timber);
            k.Box(new Vector3(hw, y0 + h * 0.5f, 0), new Vector3(0.1f, 0.14f, d), Palette.Timber);
            k.Box(new Vector3(-hw, y0 + h * 0.5f, 0), new Vector3(0.1f, 0.14f, d), Palette.Timber);
        }

        static Color Pick(Color[] arr, int variant, int salt) => arr[Mathf.Abs(variant * 31 + salt) % arr.Length];

        // ------------------------------------------------------------------ types

        static void House(MeshKit k, Building b, ref Result r)
        {
            int v = b.variant;
            int tier = Mathf.Clamp(b.tier, 0, 4);
            float w = 5.4f + (v % 3) * 0.5f, d = 4.4f + (v % 2) * 0.4f;
            bool twoStorey = tier >= 3 || (tier == 2 && v % 3 == 0);
            float wallH = twoStorey ? 5.0f : 2.8f + (tier >= 1 ? 0.3f : 0);
            Color wall = tier switch
            {
                0 => Palette.Vary(Pick(Walls, v, 1), 0.05f, v),
                1 => Palette.Vary(Pick(Walls, v, 2), 0.05f, v),
                2 => Palette.Vary(Palette.Stone, 0.06f, v),
                3 => Palette.Vary(Palette.Brick, 0.06f, v),
                _ => Palette.Vary(Palette.Brick * 0.85f, 0.05f, v)
            };
            Color roof = tier switch
            {
                0 => Pick(RoofThatch, v, 3),
                1 => Pick(RoofTier1, v, 3),
                2 => Pick(RoofTier2, v, 3),
                3 => Pick(RoofTier2, v, 4),
                _ => Palette.TileSlate
            };
            Foundation(k, w, d, Palette.StoneDark);
            k.Box(new Vector3(0, 0.45f + wallH * 0.5f, 0), new Vector3(w, wallH, d), wall);
            if (tier == 1 || (tier == 0 && v % 2 == 0)) TimberFrame(k, w, d, 0.45f, wallH);
            if (tier >= 2)
            {
                // quoins
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f })
                        k.Box(new Vector3(sx * (w * 0.5f), 0.45f + wallH * 0.5f, sz * (d * 0.5f)), new Vector3(0.35f, wallH, 0.35f), Palette.StoneDark);
            }
            float roofH = tier == 0 ? 2.6f : 2.1f;
            float overhang = tier == 0 ? 0.55f : 0.35f;
            float top = 0.45f + wallH;
            k.GableRoof(new Vector3(0, top, 0), w, d, roofH, overhang, roof, wall * 0.95f);
            if (tier == 0) k.Box(new Vector3(0, top + roofH + 0.05f, 0), new Vector3(w + overhang * 2 + 0.1f, 0.28f, 0.4f), Palette.ThatchDark);
            // chimney
            float cx = w * 0.28f * (v % 2 == 0 ? 1 : -1);
            k.Box(new Vector3(cx, top + roofH * 0.6f, -d * 0.15f), new Vector3(0.65f, roofH * 1.2f + 0.6f, 0.65f), tier >= 3 ? Palette.Brick * 0.8f : Palette.StoneDark);
            r.chimney = new Vector3(cx, top + roofH * 1.25f + 0.4f, -d * 0.15f);
            Door(k, d * 0.5f, -w * 0.15f);
            WindowZ(k, w * 0.25f, 1.7f, d * 0.5f, 1);
            WindowZ(k, -w * 0.25f, 1.7f, -d * 0.5f, -1);
            WindowZ(k, w * 0.2f, 1.7f, -d * 0.5f, -1);
            WindowX(k, w * 0.5f, 1.7f, 0, 1);
            WindowX(k, -w * 0.5f, 1.7f, 0, -1);
            if (twoStorey)
            {
                WindowZ(k, -w * 0.25f, 3.9f, d * 0.5f, 1); WindowZ(k, w * 0.25f, 3.9f, d * 0.5f, 1);
                WindowZ(k, -w * 0.25f, 3.9f, -d * 0.5f, -1);
            }
            if (tier >= 4)
            {
                k.Cylinder(new Vector3(-w * 0.3f, top, d * 0.2f), 0.35f, roofH + 3.5f, 8, Palette.Brick * 0.7f);
            }
            // flower box and a barrel for life
            k.Box(new Vector3(w * 0.25f, 1.15f, d * 0.5f + 0.25f), new Vector3(0.95f, 0.22f, 0.3f), Palette.Timber);
            k.Blob(new Vector3(w * 0.25f, 1.32f, d * 0.5f + 0.25f), new Vector3(0.45f, 0.14f, 0.15f), v % 2 == 0 ? new Color(0.9f, 0.35f, 0.4f) : new Color(0.95f, 0.8f, 0.3f), v, 0.3f, 0);
            k.Cylinder(new Vector3(-w * 0.5f - 0.5f, 0.3f, d * 0.3f), 0.35f, 0.8f, 8, Palette.Timber * 1.2f);
            r.height = top + roofH;
        }

        static void Well(MeshKit k, ref Result r)
        {
            k.Cylinder(Vector3.zero, 1.2f, 0.95f, 10, Palette.Stone, Palette.StoneDark);
            k.Cylinder(new Vector3(0, 0.9f, 0), 0.9f, 0.05f, 10, new Color(0.15f, 0.25f, 0.35f));
            k.Box(new Vector3(-1.05f, 1.4f, 0), new Vector3(0.18f, 2.2f, 0.18f), Palette.Timber);
            k.Box(new Vector3(1.05f, 1.4f, 0), new Vector3(0.18f, 2.2f, 0.18f), Palette.Timber);
            k.Box(new Vector3(0, 2.05f, 0), new Vector3(2.2f, 0.12f, 0.12f), Palette.Timber);
            k.GableRoof(new Vector3(0, 2.45f, 0), 2.2f, 1.6f, 0.8f, 0.2f, Palette.Shingle, Palette.Timber);
            k.Cylinder(new Vector3(0.2f, 1.3f, 0), 0.22f, 0.35f, 7, Palette.Timber * 1.2f);
            r.height = 3.3f;
        }

        static void Market(MeshKit k, Building b, ref Result r)
        {
            Color[] cloth = { Palette.Cloth1, Palette.Cloth2, Palette.Cloth3 };
            int stalls = b.tier >= 1 ? 4 : 3;
            for (int i = 0; i < stalls; i++)
            {
                float x = (i - (stalls - 1) * 0.5f) * 2.6f;
                float z = (i % 2 == 0 ? -0.6f : 0.6f);
                k.Box(new Vector3(x, 0.5f, z), new Vector3(2.0f, 1.0f, 1.1f), Palette.Timber * 1.3f);
                foreach (var sx in new[] { -0.95f, 0.95f })
                    foreach (var sz in new[] { -0.5f, 0.5f })
                        k.Box(new Vector3(x + sx, 1.2f, z + sz), new Vector3(0.1f, 2.4f, 0.1f), Palette.Timber);
                var c = cloth[i % 3];
                // striped canopy
                for (int s = 0; s < 4; s++)
                {
                    var sc = s % 2 == 0 ? c : Palette.Plaster;
                    k.Box(new Vector3(x - 0.75f + s * 0.5f, 2.45f, z), new Vector3(0.5f, 0.08f, 1.5f), sc);
                }
                // goods
                k.Blob(new Vector3(x - 0.4f, 1.1f, z), new Vector3(0.3f, 0.15f, 0.3f), new Color(0.85f, 0.4f, 0.2f), i, 0.2f, 0);
                k.Blob(new Vector3(x + 0.4f, 1.1f, z), new Vector3(0.3f, 0.15f, 0.3f), new Color(0.5f, 0.7f, 0.3f), i + 9, 0.2f, 0);
                k.Box(new Vector3(x + 1.2f, 0.3f, z + 0.9f), new Vector3(0.6f, 0.6f, 0.6f), Palette.Timber * 1.4f);
            }
            r.height = 2.6f;
        }

        static void Tavern(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z, wallH = 5.2f;
            Foundation(k, w, d, Palette.StoneDark);
            k.Box(new Vector3(0, 0.45f + 1.4f, 0), new Vector3(w, 2.8f, d), Palette.Stone);
            k.Box(new Vector3(0, 0.45f + 2.8f + 1.2f, 0), new Vector3(w + 0.4f, 2.4f, d + 0.4f), Palette.PlasterWarm);
            TimberFrame(k, w + 0.4f, d + 0.4f, 3.25f, 2.4f);
            k.GableRoof(new Vector3(0, 0.45f + wallH, 0), w + 0.4f, d + 0.4f, 2.8f, 0.5f, b.tier >= 2 ? Palette.TileRed : Palette.Thatch, Palette.PlasterWarm);
            k.Box(new Vector3(w * 0.3f, 0.45f + wallH + 2.0f, 0), new Vector3(0.8f, 2.6f, 0.8f), Palette.StoneDark);
            r.chimney = new Vector3(w * 0.3f, 0.45f + wallH + 3.4f, 0);
            Door(k, d * 0.5f, 0, 2.1f, 1.3f);
            for (int i = -1; i <= 1; i += 2) { WindowZ(k, i * w * 0.3f, 1.8f, d * 0.5f, 1, 1.1f, 0.9f); WindowZ(k, i * w * 0.3f, 4.1f, d * 0.5f + 0.2f, 1, 0.9f, 0.8f); WindowZ(k, i * w * 0.3f, 1.8f, -d * 0.5f, -1, 1.1f, 0.9f); }
            WindowX(k, w * 0.5f, 1.8f, 0, 1); WindowX(k, -w * 0.5f, 1.8f, 0, -1);
            // porch and sign
            k.Box(new Vector3(0, 2.75f, d * 0.5f + 1.0f), new Vector3(w * 0.8f, 0.12f, 2.0f), Palette.Shingle);
            k.Box(new Vector3(-w * 0.38f, 1.6f, d * 0.5f + 1.9f), new Vector3(0.16f, 2.3f, 0.16f), Palette.Timber);
            k.Box(new Vector3(w * 0.38f, 1.6f, d * 0.5f + 1.9f), new Vector3(0.16f, 2.3f, 0.16f), Palette.Timber);
            k.Box(new Vector3(w * 0.5f + 0.7f, 3.4f, d * 0.5f), new Vector3(1.2f, 0.08f, 0.08f), Palette.Iron);
            k.Box(new Vector3(w * 0.5f + 1.0f, 2.85f, d * 0.5f), new Vector3(0.08f, 0.9f, 1.0f), new Color(0.35f, 0.55f, 0.35f));
            k.Cylinder(new Vector3(-w * 0.5f - 0.6f, 0.3f, d * 0.3f), 0.42f, 1.0f, 8, Palette.Timber * 1.2f);
            k.Cylinder(new Vector3(-w * 0.5f - 0.6f, 0.3f, d * 0.3f - 0.95f), 0.42f, 1.0f, 8, Palette.Timber * 1.2f);
            r.height = 0.45f + wallH + 2.8f;
        }

        static void Chapel(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            // nave runs along local z here; build along x then the front faces +z
            Foundation(k, w, d, Palette.StoneDark);
            k.Box(new Vector3(0, 0.45f + 2.3f, -0.5f), new Vector3(w, 4.6f, d - 1), Palette.Stone);
            k.M = Matrix4x4.TRS(new Vector3(0, 5.05f, -0.5f), Quaternion.Euler(0, 90, 0), Vector3.one);
            k.GableRoof(Vector3.zero, d - 1, w, 3.4f, 0.35f, b.tier >= 1 ? Palette.TileSlate : Palette.Shingle, Palette.Stone);
            k.M = Matrix4x4.identity;
            // bell tower at the front
            float tz = d * 0.5f - 1.4f;
            k.Box(new Vector3(0, 0.45f + 4.2f, tz), new Vector3(2.6f, 8.4f, 2.6f), Palette.Stone * 1.03f);
            k.Box(new Vector3(0, 8.4f, tz), new Vector3(2.9f, 0.3f, 2.9f), Palette.StoneDark);
            k.Sub(1);
            k.Box(new Vector3(0, 7.4f, tz + 1.32f), new Vector3(0.9f, 1.3f, 0.06f), new Color(0.15f, 0.12f, 0.1f));
            k.Sub(0);
            k.Pyramid(new Vector3(0, 8.55f, tz), 2.9f, b.tier >= 1 ? 6f : 4.6f, Palette.TileSlate);
            k.Box(new Vector3(0, 8.55f + (b.tier >= 1 ? 6f : 4.6f) + 0.5f, tz), new Vector3(0.08f, 1f, 0.08f), Palette.Brass);
            k.Box(new Vector3(0, 8.55f + (b.tier >= 1 ? 6f : 4.6f) + 0.65f, tz), new Vector3(0.5f, 0.08f, 0.08f), Palette.Brass);
            Door(k, tz + 1.3f, 0, 2.4f, 1.2f);
            for (int i = 0; i < 3; i++)
            {
                float z = -d * 0.4f + i * 2.4f;
                WindowX(k, w * 0.5f, 2.8f, z, 1, 0.7f, 1.6f);
                WindowX(k, -w * 0.5f, 2.8f, z, -1, 0.7f, 1.6f);
            }
            r.height = 15f;
        }

        static void Forge(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            k.Box(new Vector3(0, 0.1f, 0), new Vector3(w, 0.2f, d), Palette.Cobble);
            k.Box(new Vector3(0, 1.6f, -d * 0.5f + 0.25f), new Vector3(w, 3.2f, 0.5f), Palette.Stone);
            k.Box(new Vector3(-w * 0.5f + 0.25f, 1.6f, 0), new Vector3(0.5f, 3.2f, d), Palette.Stone);
            foreach (var sx in new[] { w * 0.45f }) foreach (var sz in new[] { d * 0.45f, -d * 0.2f })
                    k.Box(new Vector3(sx, 1.6f, sz), new Vector3(0.25f, 3.2f, 0.25f), Palette.Timber);
            k.M = Matrix4x4.TRS(new Vector3(0, 3.2f, 0), Quaternion.Euler(0, 0, 0), Vector3.one);
            k.GableRoof(Vector3.zero, w, d, 1.6f, 0.4f, Palette.Shingle, Palette.Stone);
            k.M = Matrix4x4.identity;
            // hearth with glowing coals
            k.Box(new Vector3(-w * 0.2f, 0.6f, -d * 0.25f), new Vector3(1.8f, 1.2f, 1.4f), Palette.StoneDark);
            k.Sub(2);
            k.Box(new Vector3(-w * 0.2f, 1.22f, -d * 0.25f), new Vector3(1.3f, 0.08f, 0.9f), new Color(1, 0.45f, 0.15f));
            k.Sub(0);
            k.Box(new Vector3(-w * 0.2f, 3.6f, -d * 0.3f), new Vector3(0.8f, 3.4f, 0.8f), Palette.StoneDark);
            r.chimney = new Vector3(-w * 0.2f, 5.5f, -d * 0.3f);
            // anvil
            k.Box(new Vector3(w * 0.15f, 0.45f, d * 0.1f), new Vector3(0.4f, 0.7f, 0.4f), Palette.Timber);
            k.Box(new Vector3(w * 0.15f, 0.9f, d * 0.1f), new Vector3(0.8f, 0.25f, 0.35f), Palette.Iron);
            k.Cylinder(new Vector3(w * 0.35f, 0.2f, -d * 0.15f), 0.45f, 0.6f, 8, Palette.Timber * 1.1f);
            r.height = 5f;
        }

        static void Granary(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                {
                    k.Box(new Vector3(sx * w * 0.4f, 0.5f, sz * d * 0.4f), new Vector3(0.3f, 1f, 0.3f), Palette.Stone);
                    k.Cylinder(new Vector3(sx * w * 0.4f, 1f, sz * d * 0.4f), 0.45f, 0.15f, 8, Palette.StoneDark);
                }
            k.Box(new Vector3(0, 2.6f, 0), new Vector3(w, 2.8f, d), Palette.Timber * 1.5f);
            for (int i = -2; i <= 2; i++) k.Box(new Vector3(i * w * 0.2f, 2.6f, d * 0.5f + 0.03f), new Vector3(0.1f, 2.8f, 0.06f), Palette.Timber);
            k.GableRoof(new Vector3(0, 4f, 0), w, d, 2.2f, 0.5f, b.tier >= 2 ? Palette.TileRed : Palette.Thatch, Palette.Timber * 1.4f);
            k.Box(new Vector3(0, 1.4f, d * 0.5f + 0.8f), new Vector3(1f, 0.1f, 1.8f), Palette.Timber);
            r.height = 6.2f;
        }

        static void Barn(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            var red = new Color(0.62f, 0.25f, 0.2f);
            Foundation(k, w, d, Palette.StoneDark);
            k.Box(new Vector3(0, 0.45f + 2f, 0), new Vector3(w, 4f, d), red);
            k.Box(new Vector3(0, 0.45f + 2f, d * 0.5f + 0.02f), new Vector3(0.12f, 4f, 0.06f), Palette.Plaster);
            k.Box(new Vector3(0, 1.6f, d * 0.5f + 0.05f), new Vector3(2.8f, 2.6f, 0.08f), red * 0.8f);
            k.Box(new Vector3(0, 1.6f, d * 0.5f + 0.09f), new Vector3(2.8f, 0.14f, 0.06f), Palette.Plaster);
            k.GableRoof(new Vector3(0, 4.45f, 0), w, d, 3f, 0.5f, Palette.Shingle * 0.9f, red);
            k.Sub(1);
            k.Box(new Vector3(0, 5.6f, d * 0.5f + 0.06f), new Vector3(1f, 0.9f, 0.08f), Palette.Window);
            k.Sub(0);
            r.height = 7.5f;
        }

        static void Workshop(MeshKit k, Building b, ref Result r)
        {
            var hb = new Building { size = b.size, tier = Mathf.Max(1, b.tier), variant = 3 };
            House(k, hb, ref r);
            // a big wooden gear hanging as a sign, and clutter
            k.M = Matrix4x4.TRS(new Vector3(b.size.x * 0.5f + 0.35f, 2.6f, b.size.z * 0.2f), Quaternion.Euler(0, 0, 90), Vector3.one);
            k.Cylinder(new Vector3(0, -0.07f, 0), 0.55f, 0.14f, 10, Palette.Brass);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2 / 10;
                k.Box(new Vector3(Mathf.Cos(a) * 0.62f, 0, Mathf.Sin(a) * 0.62f), new Vector3(0.18f, 0.14f, 0.18f), Palette.Brass);
            }
            k.M = Matrix4x4.identity;
            k.Box(new Vector3(-b.size.x * 0.2f, 0.4f, b.size.z * 0.5f + 0.9f), new Vector3(1.6f, 0.8f, 0.8f), Palette.Timber * 1.3f);
            k.Cylinder(new Vector3(-b.size.x * 0.2f, 1.0f, b.size.z * 0.5f + 0.9f), 0.3f, 0.12f, 8, Palette.Iron);
        }

        static void Hall(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            k.Box(new Vector3(0, 0.3f, 0), new Vector3(w + 1, 0.6f, d + 1), Palette.StoneDark);
            k.Box(new Vector3(0, 0.6f + 2.2f, -0.6f), new Vector3(w, 4.4f, d - 1.2f), Palette.Stone);
            for (int i = 0; i < 4; i++)
                k.Cylinder(new Vector3(-w * 0.375f + i * w * 0.25f, 0.6f, d * 0.5f - 0.4f), 0.28f, 4.4f, 8, Palette.Plaster);
            k.Box(new Vector3(0, 5.15f, d * 0.5f - 0.4f), new Vector3(w, 0.3f, 0.9f), Palette.Stone);
            k.HipRoof(new Vector3(0, 5.0f, 0), w, d, 2.4f, 0.4f, b.tier >= 1 ? Palette.TileRed : Palette.Shingle);
            Door(k, d * 0.5f - 1.2f, 0, 2.4f, 1.4f);
            WindowX(k, w * 0.5f, 2.8f, -1, 1, 0.9f, 1.4f); WindowX(k, -w * 0.5f, 2.8f, -1, -1, 0.9f, 1.4f);
            r.height = 7.6f;
        }

        static void Haystack(MeshKit k, Building b, ref Result r)
        {
            k.Blob(new Vector3(0, 1.0f, 0), new Vector3(1.6f, 1.3f, 1.6f), Palette.Thatch, b.id, 0.12f, 1, 0, 0.3f);
            k.Cone(new Vector3(0, 1.8f, 0), 0.9f, 1.2f, 8, Palette.ThatchDark);
            r.height = 3f;
        }

        static void Mill(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            Foundation(k, w, d, Palette.StoneDark);
            k.Box(new Vector3(0, 0.45f + 2.3f, 0), new Vector3(w, 4.6f, d), Palette.Stone);
            TimberFrame(k, w, d, 2.9f, 2.0f);
            k.GableRoof(new Vector3(0, 5.05f, 0), w, d, 2.6f, 0.45f, Palette.Shingle, Palette.Stone);
            Door(k, d * 0.5f, -1, 2f, 1.1f);
            WindowZ(k, 1.5f, 3.6f, d * 0.5f, 1); WindowX(k, -w * 0.5f, 2f, 0, -1);
            // axle housing towards the river (+x side)
            k.Box(new Vector3(w * 0.5f + 0.4f, 2.2f, 0), new Vector3(0.8f, 0.8f, 0.8f), Palette.Timber);
            var wheel = new MeshKit();
            wheel.M = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one);
            wheel.Cylinder(new Vector3(0, -0.35f, 0), 0.35f, 0.7f, 8, Palette.Iron);
            for (int ring = 0; ring < 2; ring++)
            {
                float y = ring == 0 ? -0.6f : 0.6f;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2 / 16;
                    wheel.Box(new Vector3(Mathf.Cos(a) * 2.6f, y, Mathf.Sin(a) * 2.6f), new Vector3(1.05f, 0.12f, 0.2f), Palette.Timber * 1.2f);
                }
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2 / 8;
                wheel.M = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one) * Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 1.3f, 0, Mathf.Sin(a) * 1.3f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), Vector3.one);
                wheel.Box(Vector3.zero, new Vector3(2.6f, 0.15f, 0.15f), Palette.Timber);
                wheel.M = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one) * Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 2.75f, 0, Mathf.Sin(a) * 2.75f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), Vector3.one);
                wheel.Box(Vector3.zero, new Vector3(0.12f, 1.3f, 0.8f), Palette.Timber * 0.9f);
            }
            r.spinner = wheel.ToMesh("Waterwheel");
            r.spinnerPos = new Vector3(w * 0.5f + 1.4f, 2.2f, 0);
            r.spinnerAxis = Vector3.right;
            r.height = 7.6f;
        }

        static void ClockTower(MeshKit k, Building b, ref Result r)
        {
            k.Box(new Vector3(0, 0.3f, 0), new Vector3(4.4f, 0.6f, 4.4f), Palette.StoneDark);
            k.Box(new Vector3(0, 0.6f + 6f, 0), new Vector3(3.4f, 12f, 3.4f), Palette.Stone);
            k.Box(new Vector3(0, 12.9f, 0), new Vector3(3.9f, 0.4f, 3.9f), Palette.StoneDark);
            k.Sub(2);
            foreach (var dir in new[] { 1f, -1f })
            {
                k.Cylinder(new Vector3(0, 11f, dir * 1.72f) , 0.01f, 0.01f, 3, Color.white);
                k.Box(new Vector3(0, 11f, dir * 1.74f), new Vector3(1.8f, 1.8f, 0.06f), new Color(0.98f, 0.92f, 0.75f));
                k.Box(new Vector3(dir * 1.74f, 11f, 0), new Vector3(0.06f, 1.8f, 1.8f), new Color(0.98f, 0.92f, 0.75f));
            }
            k.Sub(0);
            foreach (var dir in new[] { 1f, -1f })
            {
                k.Box(new Vector3(0, 11.3f, dir * 1.79f), new Vector3(0.08f, 0.75f, 0.04f), Palette.Iron);
                k.Box(new Vector3(0.25f, 11f, dir * 1.79f), new Vector3(0.55f, 0.08f, 0.04f), Palette.Iron);
            }
            k.Pyramid(new Vector3(0, 13.1f, 0), 3.9f, 4.2f, Palette.TileSlate);
            Door(k, 1.7f, 0, 2.2f, 1.1f);
            r.height = 17.3f;
        }

        static void Library(MeshKit k, Building b, ref Result r)
        {
            float w = b.size.x, d = b.size.z;
            k.Box(new Vector3(0, 0.3f, 0), new Vector3(w + 0.8f, 0.6f, d + 0.8f), Palette.StoneDark);
            k.Box(new Vector3(0, 0.6f + 2.8f, 0), new Vector3(w, 5.6f, d), Palette.Plaster * 0.97f);
            for (int i = 0; i < 4; i++)
            {
                float x = -w * 0.36f + i * w * 0.24f;
                WindowZ(k, x, 2.6f, d * 0.5f, 1, 0.9f, 2.0f);
                WindowZ(k, x, 2.6f, -d * 0.5f, -1, 0.9f, 2.0f);
            }
            k.HipRoof(new Vector3(0, 6.2f, 0), w, d, 2.2f, 0.4f, Palette.TileSlate);
            k.Cylinder(new Vector3(0, 8.4f, 0), 1.4f, 1.2f, 10, Palette.Stone);
            k.Blob(new Vector3(0, 9.6f, 0), new Vector3(1.5f, 1.2f, 1.5f), new Color(0.45f, 0.62f, 0.55f), 3, 0.02f, 1);
            Door(k, d * 0.5f, 0, 2.4f, 1.3f);
            r.height = 11f;
        }

        static void Windmill(MeshKit k, Building b, ref Result r)
        {
            k.Cylinder(Vector3.zero, 2.6f, 9f, 10, Palette.Plaster, null, 1.7f);
            k.Cone(new Vector3(0, 9f, 0), 2.1f, 2.4f, 10, Palette.Shingle);
            Door(k, 2.55f, 0, 2f, 1f);
            WindowZ(k, 0, 5.5f, 2.15f, 1, 0.6f, 0.7f);
            var sails = new MeshKit();
            sails.Box(Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f), Palette.Timber);
            for (int i = 0; i < 4; i++)
            {
                sails.M = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, i * 90), Vector3.one);
                sails.Box(new Vector3(0, 3.4f, 0), new Vector3(0.18f, 6.6f, 0.12f), Palette.Timber);
                sails.Box(new Vector3(0.55f, 3.9f, 0.02f), new Vector3(0.95f, 5.0f, 0.04f), Palette.Plaster * 0.95f);
            }
            r.spinner = sails.ToMesh("Sails");
            r.spinnerPos = new Vector3(0, 8.3f, 2.5f);
            r.spinnerAxis = Vector3.forward;
            r.height = 11.4f;
        }

        /// <summary>Charred remains after a fire.</summary>
        public static Mesh Ruin(Building b)
        {
            var k = new MeshKit();
            var rng = new System.Random(b.id);
            float w = Mathf.Max(2, b.size.x), d = Mathf.Max(2, b.size.z);
            k.Box(new Vector3(0, 0.15f, 0), new Vector3(w, 0.3f, d), Palette.Charred);
            for (int i = 0; i < 9; i++)
            {
                float x = ((float)rng.NextDouble() - 0.5f) * w, z = ((float)rng.NextDouble() - 0.5f) * d;
                float h = 0.3f + (float)rng.NextDouble() * 1.6f;
                k.M = Matrix4x4.TRS(new Vector3(x, h * 0.5f, z), Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 50, (float)rng.NextDouble() * 180, 0), Vector3.one);
                k.Box(Vector3.zero, new Vector3(0.25f, h, 0.25f), i % 3 == 0 ? Palette.StoneDark * 0.7f : Palette.Charred * 1.3f);
            }
            k.M = Matrix4x4.identity;
            return k.ToMesh("Ruin");
        }

        /// <summary>Wooden scaffolding wrapped around a construction site.</summary>
        public static Mesh Scaffold(Building b, float height)
        {
            var k = new MeshKit();
            float w = Mathf.Max(2, b.size.x) * 0.5f + 0.6f, d = Mathf.Max(2, b.size.z) * 0.5f + 0.6f;
            foreach (var sx in new[] { -w, 0, w }) foreach (var sz in new[] { -d, d })
                    k.Box(new Vector3(sx, height * 0.5f, sz), new Vector3(0.14f, height, 0.14f), Palette.Timber * 1.4f);
            for (float y = 1.2f; y < height; y += 1.6f)
            {
                k.Box(new Vector3(0, y, d), new Vector3(w * 2, 0.1f, 0.4f), Palette.Timber * 1.5f);
                k.Box(new Vector3(0, y, -d), new Vector3(w * 2, 0.1f, 0.4f), Palette.Timber * 1.5f);
            }
            k.Box(new Vector3(w + 0.8f, 0.3f, d * 0.4f), new Vector3(1.4f, 0.6f, 0.8f), Palette.Timber * 1.2f);
            k.Box(new Vector3(-w - 0.6f, 0.4f, -d * 0.2f), new Vector3(0.9f, 0.8f, 0.9f), Palette.Stone);
            return k.ToMesh("Scaffold");
        }
    }
}
