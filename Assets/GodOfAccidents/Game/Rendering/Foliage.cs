using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace GOA.Game
{
    /// <summary>GPU-instanced vegetation and scatter: forests, meadow grass, wildflowers, reeds, rocks and crops.</summary>
    public sealed class Foliage
    {
        sealed class Set
        {
            public Mesh mesh; public Material mat; public bool shadows; public float maxDistance;
            public readonly List<Cell> cells = new List<Cell>();
            public readonly Dictionary<long, Cell> byKey = new Dictionary<long, Cell>();
        }

        sealed class Cell
        {
            public readonly List<Matrix4x4> m = new List<Matrix4x4>();
            public Bounds bounds;
            public bool hasBounds;
            public Matrix4x4[] arr;
        }

        readonly List<Set> _sets = new List<Set>();
        public float Density = 1f;
        public bool DrawGrass = true;
        const float CellSize = 48f;
        readonly Plane[] _planes = new Plane[6];
        public int DrawnInstances { get; private set; }
        readonly List<(Vector3 pos, float h)> _treeSpots = new List<(Vector3, float)>();
        public IReadOnlyList<(Vector3 pos, float h)> TreeSpots => _treeSpots;

        Set NewSet(Mesh mesh, Material mat, bool shadows, float maxDist)
        {
            var s = new Set { mesh = mesh, mat = mat, shadows = shadows, maxDistance = maxDist };
            _sets.Add(s);
            return s;
        }

        static void Add(Set s, Matrix4x4 m)
        {
            Vector3 p = m.GetColumn(3);
            long key = ((long)Mathf.FloorToInt(p.x / CellSize) << 32) ^ (uint)Mathf.FloorToInt(p.z / CellSize);
            if (!s.byKey.TryGetValue(key, out var c)) { c = new Cell(); s.byKey[key] = c; s.cells.Add(c); }
            c.m.Add(m);
            var b = new Bounds(p + Vector3.up * 4, new Vector3(10, 18, 10));
            if (!c.hasBounds) { c.bounds = b; c.hasBounds = true; } else c.bounds.Encapsulate(b);
        }

        // ------------------------------------------------------------------ meshes

        static Mesh PineMesh(int seed, float scale)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            k.Cylinder(Vector3.zero, 0.35f * scale, 2.2f * scale, 6, Palette.Bark);
            k.sway = 1f;
            int tiers = 3 + rng.Next(2);
            float y = 1.4f * scale;
            for (int i = 0; i < tiers; i++)
            {
                float r = (2.6f - i * 0.55f) * scale * (0.9f + (float)rng.NextDouble() * 0.2f);
                float h = (2.6f - i * 0.25f) * scale;
                var col = Color.Lerp(Palette.PineDark, Palette.Pine, i / (float)tiers + (float)rng.NextDouble() * 0.2f);
                k.Cone(new Vector3(0, y, 0), r, h, 7, col, (float)rng.NextDouble() * 60, Mathf.Clamp01(y / (8 * scale)) * 0.6f);
                y += h * 0.55f;
            }
            return k.ToMesh("Pine");
        }

        static Mesh BroadleafMesh(int seed, float scale, Color leaf, Color bark)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            k.Cylinder(Vector3.zero, 0.32f * scale, 3.2f * scale, 6, bark, null, 0.22f * scale);
            int blobs = 3 + rng.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2;
                float r = i == 0 ? 0 : 1.2f * scale;
                var c = new Vector3(Mathf.Cos(a) * r, (3.6f + (float)rng.NextDouble() * 1.4f) * scale, Mathf.Sin(a) * r);
                float s = (i == 0 ? 2.3f : 1.6f + (float)rng.NextDouble() * 0.6f) * scale;
                var col = Color.Lerp(leaf, Palette.LeafLight, (float)rng.NextDouble() * 0.5f);
                k.Blob(c, new Vector3(s, s * 0.85f, s), col, seed * 7 + i, 0.22f, 1, 0.35f, 1f);
            }
            return k.ToMesh("Broadleaf");
        }

        static Mesh GrassMesh(int seed)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            for (int i = 0; i < 6; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2;
                float r = (float)rng.NextDouble() * 0.35f;
                var b = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float h = 0.35f + (float)rng.NextDouble() * 0.35f;
                var lean = new Vector3(Mathf.Cos(a + 1) * 0.12f, 0, Mathf.Sin(a + 1) * 0.12f);
                var side = new Vector3(Mathf.Cos(a + 1.57f), 0, Mathf.Sin(a + 1.57f)) * 0.06f;
                var col = Color.Lerp(Palette.GrassB, Palette.Meadow, (float)rng.NextDouble());
                k.Tri(b - side, b + Vector3.up * h + lean, b + side, col, 0, 1, 0);
                k.Tri(b + side, b + Vector3.up * h + lean, b - side, col * 0.9f, 0, 1, 0);
            }
            return k.ToMesh("Grass");
        }

        static Mesh FlowerMesh(int seed)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            Color[] petals = { new Color(0.95f, 0.85f, 0.35f), new Color(0.92f, 0.45f, 0.55f), new Color(0.6f, 0.55f, 0.95f), new Color(0.98f, 0.97f, 0.92f), new Color(0.95f, 0.55f, 0.25f) };
            var pc = petals[seed % petals.Length];
            for (int i = 0; i < 5; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, r = (float)rng.NextDouble() * 0.4f;
                var b = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float h = 0.3f + (float)rng.NextDouble() * 0.25f;
                k.Tri(b + new Vector3(-0.02f, 0, 0), b + Vector3.up * h, b + new Vector3(0.02f, 0, 0), Palette.GrassB, 0, 1, 0);
                k.sway = 1;
                k.Blob(b + Vector3.up * h, new Vector3(0.09f, 0.05f, 0.09f), pc, seed + i, 0.1f, 0, 1, 1);
                k.sway = 0;
            }
            return k.ToMesh("Flower");
        }

        static Mesh RockMesh(int seed)
        {
            var k = new MeshKit();
            k.Blob(new Vector3(0, 0.2f, 0), new Vector3(1f, 0.6f, 0.8f), Color.Lerp(Palette.Rock, Palette.RockDark, (seed % 5) / 5f), seed, 0.3f, 1);
            return k.ToMesh("Rock");
        }

        static Mesh BushMesh(int seed)
        {
            var k = new MeshKit();
            k.Blob(new Vector3(0, 0.55f, 0), new Vector3(1.1f, 0.75f, 1.0f), Palette.Bush, seed, 0.25f, 1, 0.1f, 0.6f);
            k.Blob(new Vector3(0.6f, 0.4f, 0.3f), new Vector3(0.6f, 0.5f, 0.6f), Palette.Leaf, seed + 3, 0.25f, 1, 0.1f, 0.6f);
            return k.ToMesh("Bush");
        }

        static Mesh ReedMesh(int seed)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            for (int i = 0; i < 7; i++)
            {
                var b = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.8f;
                float h = 1.0f + (float)rng.NextDouble() * 0.8f;
                var col = Color.Lerp(Palette.WheatGreen, Palette.Wheat, (float)rng.NextDouble() * 0.5f);
                k.Tri(b + new Vector3(-0.04f, 0, 0), b + Vector3.up * h, b + new Vector3(0.04f, 0, 0), col, 0, 1, 0);
                k.Tri(b + new Vector3(0, 0, -0.04f), b + Vector3.up * h, b + new Vector3(0, 0, 0.04f), col * 0.9f, 0, 1, 0);
                k.Box(b + Vector3.up * (h * 0.85f), new Vector3(0.07f, 0.25f, 0.07f), new Color(0.45f, 0.3f, 0.18f));
            }
            return k.ToMesh("Reeds");
        }

        public static Mesh CropMesh(int seed, Color col, float height)
        {
            var k = new MeshKit();
            var rng = new System.Random(seed);
            for (int i = 0; i < 5; i++)
            {
                var b = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, 0, ((float)rng.NextDouble() - 0.5f) * 0.4f);
                float h = height * (0.8f + (float)rng.NextDouble() * 0.4f);
                k.Tri(b + new Vector3(-0.05f, 0, 0), b + Vector3.up * h, b + new Vector3(0.05f, 0, 0), col, 0, 1, 0);
                k.Tri(b + new Vector3(0, 0, -0.05f), b + Vector3.up * h, b + new Vector3(0, 0, 0.05f), col * 0.88f, 0, 1, 0);
            }
            return k.ToMesh("Crop");
        }

        // ------------------------------------------------------------------ placement

        static float H01(int x, int z, int salt)
        {
            unchecked
            {
                int h = x * 73856093 ^ z * 19349663 ^ salt * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        bool Blocked(World w, in TerrainBuilder.Paint paint, float x, float z, float clearance)
        {
            if (TerrainBuilder.RoadDistance(paint, x, z) < clearance) return true;
            if (TerrainBuilder.InField(paint, x, z, out _, out _, out _, clearance * 0.6f)) return true;
            if (VillageLayout.DistanceToRiver(new Vec2(x, z)) < VillageLayout.RiverHalfWidth + clearance * 0.5f) return true;
            var p = new Vec2(x, z);
            foreach (var b in w.Buildings)
                if (Vec2.Distance(b.pos, p) < Mathf.Max(b.size.x, b.size.z) * 0.6f + clearance) return true;
            foreach (var lot in w.FreeLots)
                if (Vec2.Distance(lot.pos, p) < 5 + clearance) return true;
            if (Vec2.Distance(p, VillageLayout.LakeCenter) < VillageLayout.LakeRadius + 3) return true;
            return false;
        }

        public void Build(World w, float quality)
        {
            Density = quality;
            var paint = TerrainBuilder.BuildPaint(w);
            var foliageMat = Mats.Stylized("foliage", wind: 1f, foliage: 1f, rim: 0.35f, wrap: 0.6f);
            var trunkyMat = Mats.Stylized("foliage_pine", wind: 0.6f, foliage: 0.6f, rim: 0.3f, wrap: 0.55f);
            var grassMat = Mats.Stylized("grass", wind: 1.6f, foliage: 1f, rim: 0.2f, wrap: 0.8f, snow: 0.6f);
            var rockMat = Mats.Stylized("rock", rim: 0.15f, wrap: 0.35f);

            var pines = new[] { NewSet(PineMesh(1, 1f), trunkyMat, true, 900), NewSet(PineMesh(2, 1.25f), trunkyMat, true, 900), NewSet(PineMesh(3, 0.8f), trunkyMat, true, 900) };
            var broad = new[] { NewSet(BroadleafMesh(4, 1f, Palette.Leaf, Palette.Bark), foliageMat, true, 900), NewSet(BroadleafMesh(5, 1.2f, Palette.Leaf, Palette.Bark), foliageMat, true, 900),
                                NewSet(BroadleafMesh(6, 0.9f, Palette.Birch, Palette.BarkLight), foliageMat, true, 900) };
            var bushes = new[] { NewSet(BushMesh(7), foliageMat, true, 260), NewSet(BushMesh(8), foliageMat, true, 260) };
            var rocks = new[] { NewSet(RockMesh(9), rockMat, true, 500), NewSet(RockMesh(10), rockMat, true, 500), NewSet(RockMesh(11), rockMat, true, 500) };
            var grass = new[] { NewSet(GrassMesh(12), grassMat, false, 130), NewSet(GrassMesh(13), grassMat, false, 130) };
            var flowers = new[] { NewSet(FlowerMesh(14), grassMat, false, 110), NewSet(FlowerMesh(15), grassMat, false, 110), NewSet(FlowerMesh(16), grassMat, false, 110), NewSet(FlowerMesh(17), grassMat, false, 110) };
            var reeds = NewSet(ReedMesh(18), grassMat, false, 160);

            // the old oak - the crow's home
            var oak = NewSet(BroadleafMesh(99, 1.9f, Palette.Leaf * 0.9f, Palette.Bark), foliageMat, true, 1200);
            var oakPos = TerrainBuilder.Ground(VillageLayout.OakPos) - Vector3.up * 0.2f;
            Add(oak, Matrix4x4.TRS(oakPos, Quaternion.Euler(0, 37, 0), Vector3.one));
            _treeSpots.Add((oakPos, 14f));

            // forests
            float step = 4.6f / Mathf.Sqrt(Mathf.Max(0.3f, quality));
            int ext = Mathf.CeilToInt(380 / step);
            for (int gz = -ext; gz <= ext; gz++)
                for (int gx = -ext; gx <= ext; gx++)
                {
                    float x = gx * step + (H01(gx, gz, 1) - 0.5f) * step * 0.9f;
                    float z = gz * step + (H01(gx, gz, 2) - 0.5f) * step * 0.9f;
                    float r = Mathf.Sqrt(x * x + z * z);
                    bool far = Mathf.Abs(x) > 224 || Mathf.Abs(z) > 224;
                    if (far && H01(gx, gz, 9) > 0.45f) continue;
                    float forest = TerrainBuilder.Fbm(x * 0.012f + 100, z * 0.012f, 3) + Mathf.Clamp01((r - 60) / 90f) * 0.55f - 0.18f;
                    if (Mathf.Abs(x + 72) < 30 && Mathf.Abs(z - 30) < 25) forest += 0.4f; // woodcutters' forest
                    if (r < 48) forest -= 0.6f;
                    if (forest < 0.12f) continue;
                    float h = TerrainBuilder.Height(x, z);
                    if (h > 58 + TerrainBuilder.Fbm(x * 0.05f, z * 0.05f) * 10) continue;
                    var nrm = TerrainBuilder.Normal(x, z);
                    if (nrm.y < 0.62f) continue;
                    if (h < TerrainBuilder.WaterLevel + 0.4f) continue;
                    if (!far && Blocked(w, paint, x, z, 3.2f)) continue;
                    float pick = H01(gx, gz, 3);
                    float scale = 0.75f + H01(gx, gz, 4) * 0.6f;
                    var m = Matrix4x4.TRS(new Vector3(x, h - 0.15f, z), Quaternion.Euler(0, H01(gx, gz, 5) * 360, 0), Vector3.one * scale);
                    bool high = h > 22 || r > 150;
                    Set set;
                    if (high || pick < 0.35f) set = pines[(int)(H01(gx, gz, 6) * 3) % 3];
                    else set = broad[(int)(H01(gx, gz, 6) * 3) % 3];
                    Add(set, m);
                    if (!far) _treeSpots.Add((new Vector3(x, h, z), 8 * scale));
                    if (!far && H01(gx, gz, 7) < 0.25f)
                    {
                        float bx = x + 2.5f, bz = z - 1.5f;
                        if (!Blocked(w, paint, bx, bz, 2.2f)) Add(bushes[gx & 1], Matrix4x4.TRS(TerrainBuilder.Ground(bx, bz), Quaternion.Euler(0, H01(gx, gz, 8) * 360, 0), Vector3.one * (0.7f + H01(gx, gz, 10) * 0.6f)));
                    }
                }

            // hedgerow bushes and orchard trees around the village
            for (int i = 0; i < 160; i++)
            {
                float a = H01(i, 0, 21) * Mathf.PI * 2, rr = 35 + H01(i, 1, 21) * 70;
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr * 0.85f - 15;
                if (Blocked(w, paint, x, z, 2.5f)) continue;
                Add(bushes[i & 1], Matrix4x4.TRS(TerrainBuilder.Ground(x, z), Quaternion.Euler(0, a * 57, 0), Vector3.one * (0.6f + H01(i, 2, 21) * 0.7f)));
            }
            for (int i = 0; i < 26; i++)
            {
                float x = -40 + (i % 6 - 2.5f) * 4.2f + H01(i, 3, 22) * 1.5f, z = 22 + (i / 6 - 2f) * 4.2f;
                if (Blocked(w, paint, x, z, 1.6f)) continue;
                Add(broad[i % 2], Matrix4x4.TRS(TerrainBuilder.Ground(x, z), Quaternion.Euler(0, i * 47, 0), Vector3.one * 0.55f));
            }

            // rocks
            for (int i = 0; i < 900; i++)
            {
                float x = (H01(i, 4, 23) - 0.5f) * 440, z = (H01(i, 5, 23) - 0.5f) * 440;
                float h = TerrainBuilder.Height(x, z);
                var nrm = TerrainBuilder.Normal(x, z);
                bool river = VillageLayout.DistanceToRiver(new Vec2(x, z)) < VillageLayout.RiverHalfWidth + 4;
                if (!(nrm.y < 0.86f || river || H01(i, 6, 23) < 0.15f)) continue;
                if (Blocked(w, paint, x, z, 1.5f) && !river) continue;
                float s = 0.4f + H01(i, 7, 23) * (nrm.y < 0.8f ? 2.4f : 1f);
                Add(rocks[i % 3], Matrix4x4.TRS(new Vector3(x, h - 0.15f, z), Quaternion.Euler(H01(i, 8, 23) * 20, H01(i, 9, 23) * 360, 0), new Vector3(s, s * (0.6f + H01(i, 10, 23) * 0.6f), s)));
            }

            // meadow grass and wildflowers near the village
            float gstep = 1.45f / Mathf.Sqrt(Mathf.Max(0.25f, quality));
            int gext = Mathf.CeilToInt(130 / gstep);
            for (int gz = -gext; gz <= gext; gz++)
                for (int gx = -gext; gx <= gext; gx++)
                {
                    float x = gx * gstep + (H01(gx, gz, 31) - 0.5f) * gstep;
                    float z = gz * gstep - 15 + (H01(gx, gz, 32) - 0.5f) * gstep;
                    if (x * x / (130f * 130f) + (z + 15) * (z + 15) / (125f * 125f) > 1) continue;
                    if (TerrainBuilder.RoadDistance(paint, x, z) < 2.3f) continue;
                    if (TerrainBuilder.InField(paint, x, z, out var fr, out _, out _, 0.3f) && fr.center.x != VillageLayout.Pasture.center.x) continue;
                    float h = TerrainBuilder.Height(x, z);
                    if (h < TerrainBuilder.WaterLevel + 0.25f) continue;
                    var nrm = TerrainBuilder.Normal(x, z);
                    if (nrm.y < 0.75f) continue;
                    var p = new Vec2(x, z);
                    bool nearBuilding = false;
                    foreach (var b in w.Buildings) if (Vec2.Distance(b.pos, p) < Mathf.Max(b.size.x, b.size.z) * 0.6f + 0.6f) { nearBuilding = true; break; }
                    if (nearBuilding || (x * x + z * z) < 60) continue;
                    var rot = Quaternion.Euler(0, H01(gx, gz, 33) * 360, 0);
                    float s = 0.8f + H01(gx, gz, 34) * 0.7f;
                    float riverD = VillageLayout.DistanceToRiver(p);
                    if (riverD < VillageLayout.RiverHalfWidth + 2.2f && riverD > VillageLayout.RiverHalfWidth - 0.8f)
                    {
                        if (H01(gx, gz, 35) < 0.55f) Add(reeds, Matrix4x4.TRS(new Vector3(x, h - 0.1f, z), rot, Vector3.one * s));
                        continue;
                    }
                    if (H01(gx, gz, 36) < 0.07f) Add(flowers[(int)(H01(gx, gz, 37) * 4) % 4], Matrix4x4.TRS(new Vector3(x, h, z), rot, Vector3.one * s));
                    else if (H01(gx, gz, 38) < 0.8f) Add(grass[(gx ^ gz) & 1], Matrix4x4.TRS(new Vector3(x, h - 0.03f, z), rot, Vector3.one * s));
                }

            foreach (var s in _sets)
                foreach (var c in s.cells) c.arr = c.m.ToArray();
        }

        public void Draw(Camera cam)
        {
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            var camPos = cam.transform.position;
            int drawn = 0;
            foreach (var s in _sets)
            {
                if (!DrawGrass && s.maxDistance < 200) continue;
                var rp = new RenderParams(s.mat)
                {
                    shadowCastingMode = s.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                };
                foreach (var c in s.cells)
                {
                    if (!c.hasBounds || c.arr.Length == 0) continue;
                    float d = Mathf.Sqrt(c.bounds.SqrDistance(camPos));
                    if (d > s.maxDistance) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, c.bounds) && !(s.shadows && d < 120)) continue;
                    rp.worldBounds = c.bounds;
                    int total = c.arr.Length;
                    for (int start = 0; start < total; start += 1023)
                    {
                        int count = Mathf.Min(1023, total - start);
                        Graphics.RenderMeshInstanced(rp, s.mesh, 0, c.arr, count, start);
                        drawn += count;
                    }
                }
            }
            DrawnInstances = drawn;
        }
    }
}
