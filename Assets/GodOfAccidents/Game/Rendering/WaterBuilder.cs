using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    public static class WaterBuilder
    {
        public static void Build(Transform parent)
        {
            // the river: a ribbon that follows the channel down the waterfall and into the lake
            var kit = new MeshKit();
            float zStart = -118f, zEnd = 222f, step = 1.25f;
            int n = Mathf.CeilToInt((zEnd - zStart) / step);
            var verts = new Vector3[(n + 1) * 3];
            var cols = new Color[(n + 1) * 3];
            var uvs = new Vector2[(n + 1) * 3];
            for (int i = 0; i <= n; i++)
            {
                float z = zStart + i * step;
                float cx = VillageLayout.RiverX(z);
                float w = VillageLayout.RiverHalfWidth + 1.2f;
                float y = TerrainBuilder.RiverSurface(z);
                float slope = Mathf.Abs(TerrainBuilder.RiverSurface(z + 1) - TerrainBuilder.RiverSurface(z - 1)) * 0.5f;
                float foam = Mathf.Clamp01(slope * 1.6f);
                if (z > TerrainBuilder.WaterfallZ + 3) foam = Mathf.Max(foam, 0.6f);
                for (int s = 0; s < 3; s++)
                {
                    float k = s - 1;
                    verts[i * 3 + s] = new Vector3(cx + k * w, y, z);
                    cols[i * 3 + s] = new Color(0.5f + foam * 0.5f, 0, 0, 1);
                    uvs[i * 3 + s] = new Vector2(s * 0.5f, z * 0.05f);
                }
            }
            var tris = new int[n * 2 * 6];
            int t = 0;
            for (int i = 0; i < n; i++)
                for (int s = 0; s < 2; s++)
                {
                    int a = i * 3 + s, b = a + 1, c = a + 3, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                }
            var river = new Mesh { name = "River" };
            river.vertices = verts; river.colors = cols; river.uv = uvs; river.triangles = tris;
            river.RecalculateNormals(); river.RecalculateBounds();
            var rmat = Mats.Water("river");
            rmat.SetVector("_Flow", new Vector4(0, -1, 0, 0.9f));
            rmat.SetFloat("_DepthRange", 1.6f);
            Spawn(parent, "River", river, rmat);

            // the lake
            int rings = 6, segs = 72;
            float R = VillageLayout.LakeRadius + 9;
            var lv = new Vector3[1 + rings * segs];
            var lc = new Color[lv.Length];
            var lu = new Vector2[lv.Length];
            var center = new Vector3(VillageLayout.LakeCenter.x, TerrainBuilder.WaterLevel - 0.02f, VillageLayout.LakeCenter.z);
            lv[0] = center; lc[0] = new Color(0.5f, 0, 0, 1);
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    float a = s * Mathf.PI * 2 / segs;
                    float rad = R * (r + 1) / rings;
                    int k = 1 + r * segs + s;
                    lv[k] = center + new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad);
                    lc[k] = new Color(0.5f, 0, 0, 1);
                }
            var lt = new System.Collections.Generic.List<int>();
            for (int s = 0; s < segs; s++) { lt.Add(0); lt.Add(1 + (s + 1) % segs); lt.Add(1 + s); }
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = 1 + r * segs + s, b = 1 + r * segs + (s + 1) % segs, c = a + segs, d = b + segs;
                    lt.Add(a); lt.Add(b); lt.Add(d); lt.Add(a); lt.Add(d); lt.Add(c);
                }
            var lake = new Mesh { name = "Lake", vertices = lv, colors = lc, uv = lu };
            lake.SetTriangles(lt, 0);
            lake.RecalculateNormals(); lake.RecalculateBounds();
            var lmat = Mats.Water("lake");
            lmat.SetVector("_Flow", new Vector4(0.3f, 0.2f, 0, 0.15f));
            lmat.SetFloat("_DepthRange", 4f);
            lmat.SetFloat("_WaveHeight", 0.09f);
            Spawn(parent, "Lake", lake, lmat);
        }

        static void Spawn(Transform parent, string name, Mesh m, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
