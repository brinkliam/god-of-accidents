using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>
    /// The valley's shape: rolling meadows, a flattened village basin, a river carved down from a mountain
    /// waterfall into a southern lake, and a ring of peaks. Heights are a pure function so every system agrees.
    /// </summary>
    public static class TerrainBuilder
    {
        public const float WaterLevel = -0.55f;
        public const float WaterfallZ = 136f;

        static float Hash(int x, int z)
        {
            unchecked
            {
                int h = x * 374761393 + z * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float Noise(float x, float z)
        {
            int xi = Mathf.FloorToInt(x), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fz = z - zi;
            float u = fx * fx * (3 - 2 * fx), w = fz * fz * (3 - 2 * fz);
            float a = Hash(xi, zi), b = Hash(xi + 1, zi), c = Hash(xi, zi + 1), d = Hash(xi + 1, zi + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w) * 2 - 1;
        }

        public static float Fbm(float x, float z, int oct = 4)
        {
            float s = 0, amp = 0.5f, f = 1;
            for (int i = 0; i < oct; i++) { s += Noise(x * f, z * f) * amp; f *= 2.03f; amp *= 0.5f; }
            return s;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }

        public static float RiverBedHeight(float z)
        {
            return -1.7f + Smooth(WaterfallZ - 3, WaterfallZ + 3, z) * 15f + Mathf.Max(0, z - WaterfallZ - 3) * 0.32f;
        }

        public static float RiverSurface(float z)
        {
            if (z < WaterfallZ - 3) return WaterLevel;
            return Mathf.Max(WaterLevel, RiverBedHeight(z) + 0.9f);
        }

        /// <summary>Terrain height at a world position (x, z). Pure and deterministic.</summary>
        public static float Height(float x, float z)
        {
            float h = Fbm(x * 0.011f, z * 0.011f) * 7f + Fbm(x * 0.045f + 11, z * 0.045f) * 1.1f;

            // mountain rim, highest to the north, with a low pass where the east road leaves
            float r = Mathf.Sqrt(x * x + z * z * 0.92f);
            float rimNoise = Fbm(x * 0.017f + 5, z * 0.017f - 3, 5);
            float rim = Smooth(118, 205, r) * (42 + rimNoise * 34);
            rim += Smooth(100, 190, z) * (30 + rimNoise * 10);
            float pass = Mathf.Exp(-Mathf.Pow((z - 5) / 22f, 2)) * Smooth(95, 150, x);
            rim *= 1 - pass * 0.82f;
            rim += Mathf.Max(0, r - 205) * (0.6f + rimNoise * 0.3f);
            h += Mathf.Max(0, rim);

            // the village basin and its fields are gentle
            float dv = Mathf.Sqrt(x * x / (100f * 100f) + (z + 25) * (z + 25) / (88f * 88f));
            float basin = 1 - Smooth(0.55f, 1.25f, dv);
            h = Mathf.Lerp(h, 0.35f + Fbm(x * 0.02f, z * 0.02f) * 0.7f, basin * 0.9f);

            // a gentle rise to the north of the chapel where the cart sits, and the windmill hill
            h += Mathf.Exp(-((x + 2) * (x + 2) + (z - 50) * (z - 50)) / 600f) * 3.2f;
            h += Mathf.Exp(-((x + 10) * (x + 10) + (z + 78) * (z + 78)) / 260f) * 3.0f;

            // river channel
            float rx = VillageLayout.RiverX(z);
            float dr = Mathf.Abs(x - rx);
            float bedH = RiverBedHeight(z);
            float hw = VillageLayout.RiverHalfWidth;
            float channel = Smooth(hw + 5.5f, hw - 1.5f, dr);
            float bank = Smooth(hw + 12f, hw + 2f, dr);
            if (z > WaterfallZ + 3) h = Mathf.Max(h * (1 - bank * 0.2f), bedH + 4f * (1 - channel));
            h = Mathf.Lerp(h, Mathf.Min(h, bedH + 0.8f), bank * 0.7f);
            h = Mathf.Lerp(h, bedH, channel);

            // lake basin
            float dl = Vector2.Distance(new Vector2(x, z), new Vector2(VillageLayout.LakeCenter.x, VillageLayout.LakeCenter.z));
            float lake = Smooth(VillageLayout.LakeRadius + 10, VillageLayout.LakeRadius - 6, dl + Fbm(x * 0.05f, z * 0.05f) * 6);
            h = Mathf.Lerp(h, -2.8f, lake);
            return h;
        }

        public static Vector3 Ground(float x, float z) => new Vector3(x, Height(x, z), z);
        public static Vector3 Ground(Vec2 p) => Ground(p.x, p.z);

        public static Vector3 Normal(float x, float z)
        {
            const float e = 0.6f;
            float hx = Height(x + e, z) - Height(x - e, z);
            float hz = Height(x, z + e) - Height(x, z - e);
            return new Vector3(-hx, 2 * e, -hz).normalized;
        }

        // ------------------------------------------------------------------ surface painting

        public struct Paint
        {
            public List<(Vec2 a, Vec2 b)> roads;
            public List<FieldRect> fields;
        }

        public static Paint BuildPaint(World w)
        {
            var p = new Paint { roads = new List<(Vec2, Vec2)>(), fields = new List<FieldRect>(VillageLayout.Fields) };
            foreach (var n in w.Nav.nodes)
                foreach (var l in n.links)
                    if (l > n.id) p.roads.Add((n.pos, w.Nav.Node(l).pos));
            p.fields.Add(VillageLayout.Pasture);
            return p;
        }

        public static float RoadDistance(in Paint paint, float x, float z)
        {
            var q = new Vec2(x, z);
            float best = 999;
            foreach (var (a, b) in paint.roads)
            {
                // quick reject
                float minx = Mathf.Min(a.x, b.x) - 4, maxx = Mathf.Max(a.x, b.x) + 4, minz = Mathf.Min(a.z, b.z) - 4, maxz = Mathf.Max(a.z, b.z) + 4;
                if (x < minx || x > maxx || z < minz || z > maxz) continue;
                float d = Vec2.DistanceToSegment(q, a, b, out _);
                if (d < best) best = d;
            }
            return best;
        }

        public static bool InField(in Paint paint, float x, float z, out FieldRect f, out float u, out float v, float pad = 0)
        {
            foreach (var fr in paint.fields)
            {
                float cs = Mathf.Cos(-fr.rotation), sn = Mathf.Sin(-fr.rotation);
                float dx = x - fr.center.x, dz = z - fr.center.z;
                float lx = dx * cs - dz * sn, lz = dx * sn + dz * cs;
                if (Mathf.Abs(lx) <= fr.size.x * 0.5f + pad && Mathf.Abs(lz) <= fr.size.z * 0.5f + pad)
                {
                    f = fr; u = lx / fr.size.x + 0.5f; v = lz / fr.size.z + 0.5f;
                    return true;
                }
            }
            f = default; u = v = 0;
            return false;
        }

        /// <summary>Colour of the ground and how much of it is living grass (for season tint).</summary>
        public static Color SurfaceColor(in Paint paint, float x, float z, float h, Vector3 n, out float grass)
        {
            float slope = 1 - n.y;
            float nz = Fbm(x * 0.08f, z * 0.08f);
            float nz2 = Fbm(x * 0.021f + 40, z * 0.021f);
            Color c = Color.Lerp(Palette.GrassB, Palette.GrassA, 0.5f + nz * 0.8f);
            c = Color.Lerp(c, Palette.Meadow, Mathf.Clamp01(nz2 * 1.5f));
            c = Color.Lerp(c, Palette.GrassDry, Mathf.Clamp01((h - 22) / 30f) * 0.45f);
            c = Color.Lerp(c, Palette.Pine * 1.25f, Mathf.Clamp01((h - 6) / 20f) * 0.35f);
            grass = 1;

            // rock on steep or high ground, snow caps on the peaks
            float rock = Mathf.Clamp01((slope - 0.42f) * 3.5f) + Mathf.Clamp01((h - 44) / 18f);
            if (rock > 0) { c = Color.Lerp(c, Color.Lerp(Palette.Rock, Palette.RockDark, 0.5f + nz), Mathf.Clamp01(rock)); grass *= 1 - Mathf.Clamp01(rock); }
            float snow = Mathf.Clamp01((h - 66 + nz * 8) / 8f) * Mathf.Clamp01(n.y * 1.6f);
            if (snow > 0) { c = Color.Lerp(c, Palette.Snow, snow); grass *= 1 - snow; }

            // river banks and lake shore
            float dr = Mathf.Abs(x - VillageLayout.RiverX(z));
            float shore = Mathf.Max(Smooth(VillageLayout.RiverHalfWidth + 4, VillageLayout.RiverHalfWidth, dr), Smooth(TerrainBuilder.WaterLevel + 0.9f, TerrainBuilder.WaterLevel + 0.1f, h));
            if (shore > 0) { c = Color.Lerp(c, h < WaterLevel + 0.2f ? Palette.Sand : Palette.WetBank, shore * 0.85f); grass *= 1 - shore * 0.8f; }
            if (h < WaterLevel - 0.1f) { c = Color.Lerp(Palette.Sand, Palette.DirtDark, Mathf.Clamp01((WaterLevel - h) / 1.5f)); grass = 0; }

            // fields: furrows
            if (InField(paint, x, z, out var f, out float u, out float v))
            {
                bool pasture = f.center.x == VillageLayout.Pasture.center.x && f.center.z == VillageLayout.Pasture.center.z;
                if (!pasture)
                {
                    float rows = Mathf.Sin(v * f.size.z * 2.2f) * 0.5f + 0.5f;
                    float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v)) * Mathf.Min(f.size.x, f.size.z);
                    c = Color.Lerp(Palette.Soil, Palette.SoilLight, rows * 0.7f + nz * 0.2f);
                    grass = 0.25f;
                    if (edge < 0.8f) { c = Color.Lerp(c, Palette.GrassB, 0.5f); grass = 0.8f; }
                }
                else c = Color.Lerp(c, Palette.Meadow, 0.4f);
            }

            // roads and the cobbled well square
            float rd = RoadDistance(paint, x, z);
            float roadW = 0.85f + nz * 0.3f;
            float road = Smooth(roadW + 0.9f, roadW, rd);
            if (road > 0)
            {
                c = Color.Lerp(c, Color.Lerp(Palette.Dirt, Palette.DirtDark, 0.25f + nz * 0.4f), road);
                grass *= 1 - road;
            }
            float sq = Smooth(7.5f, 5.5f, Mathf.Sqrt(x * x + z * z));
            if (sq > 0) { float cob = 0.94f + 0.06f * Mathf.Abs(Mathf.Sin(x * 2.1f) * Mathf.Sin(z * 2.1f)); c = Color.Lerp(c, Palette.Cobble * cob * 1.05f, sq); grass *= 1 - sq; }
            return c;
        }

        // ------------------------------------------------------------------ mesh

        public static List<GameObject> Build(World w, Transform parent, Material mat, float detail = 1f)
        {
            var paint = BuildPaint(w);
            var result = new List<GameObject>();
            float half = 224f; // aligned with the 32 m backdrop tiles
            float step = 1.5f / detail;
            int chunks = 8;
            float chunkSize = half * 2 / chunks;
            int per = Mathf.CeilToInt(chunkSize / step);
            for (int cz = 0; cz < chunks; cz++)
                for (int cx = 0; cx < chunks; cx++)
                {
                    float x0 = -half + cx * chunkSize, z0 = -half + cz * chunkSize;
                    var go = BuildGrid(paint, x0, z0, chunkSize, per, mat, parent, $"Terrain_{cx}_{cz}");
                    result.Add(go);
                }
            // distant backdrop ring of mountains so the world never ends at a hard edge
            float outer = 640f;
            int ringRes = 40;
            float ringStep = outer * 2 / ringRes;
            for (int rz = 0; rz < ringRes; rz++)
                for (int rx = 0; rx < ringRes; rx++)
                {
                    float x0 = -outer + rx * ringStep, z0 = -outer + rz * ringStep;
                    if (x0 >= -half - 0.1f && x0 + ringStep <= half + 0.1f && z0 >= -half - 0.1f && z0 + ringStep <= half + 0.1f) continue;
                    // only tiles fully outside the detailed square
                    if (x0 + ringStep > -half && x0 < half && z0 + ringStep > -half && z0 < half) continue;
                    result.Add(BuildGrid(paint, x0, z0, ringStep, 3, mat, parent, $"Backdrop_{rx}_{rz}", true));
                }

            return result;
        }

        static GameObject BuildGrid(in Paint paint, float x0, float z0, float size, int per, Material mat, Transform parent, string name, bool far = false)
        {
            int vn = per + 1;
            var verts = new Vector3[vn * vn];
            var norms = new Vector3[vn * vn];
            var cols = new Color[vn * vn];
            var uvs = new Vector2[vn * vn];
            float step = size / per;
            for (int j = 0; j < vn; j++)
                for (int i = 0; i < vn; i++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    float h = Height(x, z);
                    var nrm = Normal(x, z);
                    int k = j * vn + i;
                    verts[k] = new Vector3(x, h, z);
                    norms[k] = nrm;
                    cols[k] = SurfaceColor(paint, x, z, h, nrm, out float grass);
                    cols[k].a = 0;
                    uvs[k] = new Vector2(grass, 0);
                }
            var tris = new int[per * per * 6];
            int t = 0;
            for (int j = 0; j < per; j++)
                for (int i = 0; i < per; i++)
                {
                    int a = j * vn + i, b = a + 1, c = a + vn, d = c + 1;
                    // alternate diagonals for a more natural facet pattern
                    if (((i + j) & 1) == 0) { tris[t++] = a; tris[t++] = c; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = b; }
                    else { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
                }
            var mesh = new Mesh { name = name };
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts; mesh.normals = norms; mesh.colors = cols; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = far ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }

        /// <summary>March a ray against the height field. Returns false if it never hits the ground.</summary>
        public static bool Raycast(Ray ray, out Vector3 hit, float maxDist = 1500)
        {
            hit = default;
            float t = 0, stepLen = 1.5f;
            Vector3 prev = ray.origin;
            float prevDiff = prev.y - Height(prev.x, prev.z);
            if (prevDiff < 0) return false;
            while (t < maxDist)
            {
                t += stepLen;
                var p = ray.GetPoint(t);
                float diff = p.y - Height(p.x, p.z);
                if (diff <= 0)
                {
                    // refine
                    float lo = t - stepLen, hi = t;
                    for (int i = 0; i < 10; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        var q = ray.GetPoint(mid);
                        if (q.y - Height(q.x, q.z) > 0) lo = mid; else hi = mid;
                    }
                    hit = ray.GetPoint(hi);
                    return true;
                }
                stepLen = Mathf.Clamp(diff * 0.5f, 0.4f, 8f);
            }
            return false;
        }
    }
}
