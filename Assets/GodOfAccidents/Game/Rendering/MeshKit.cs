using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GOA.Game
{
    /// <summary>
    /// Procedural low-poly mesh builder. Faceted (flat-shaded) by default for the hand-carved diorama look.
    /// Vertex colour alpha is the wind-sway weight read by GOA/Stylized.
    /// </summary>
    public sealed class MeshKit
    {
        public readonly List<Vector3> v = new List<Vector3>(1024);
        public readonly List<Vector3> n = new List<Vector3>(1024);
        public readonly List<Color> c = new List<Color>(1024);
        public readonly List<Vector2> uv = new List<Vector2>(1024);
        readonly List<List<int>> _subs = new List<List<int>> { new List<int>(2048) };
        int _sub;
        public Matrix4x4 M = Matrix4x4.identity;
        public float sway; // wind weight multiplier applied by height

        public void Sub(int index)
        {
            while (_subs.Count <= index) _subs.Add(new List<int>());
            _sub = index;
        }

        Vector3 P(Vector3 p) => M.MultiplyPoint3x4(p);
        Vector3 N(Vector3 d) => M.MultiplyVector(d).normalized;

        float SwayAt(Vector3 local, float height) => sway <= 0 ? 0 : Mathf.Clamp01(local.y / Mathf.Max(0.01f, height)) * sway;

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, float swayA = 0, float swayB = 0, float swayD = 0)
        {
            var pa = P(a); var pb = P(b); var pd = P(d);
            var nn = Vector3.Cross(pb - pa, pd - pa).normalized;
            int i = v.Count;
            v.Add(pa); v.Add(pb); v.Add(pd);
            n.Add(nn); n.Add(nn); n.Add(nn);
            col.a = swayA; c.Add(col); col.a = swayB; c.Add(col); col.a = swayD; c.Add(col);
            uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
            var t = _subs[_sub];
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col)
        {
            Tri(a, b, d, col); Tri(a, d, e, col);
        }

        public void Box(Vector3 center, Vector3 size, Color col, Color? top = null)
        {
            var h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z), p100 = center + new Vector3(h.x, -h.y, -h.z),
                    p010 = center + new Vector3(-h.x, h.y, -h.z), p110 = center + new Vector3(h.x, h.y, -h.z),
                    p001 = center + new Vector3(-h.x, -h.y, h.z), p101 = center + new Vector3(h.x, -h.y, h.z),
                    p011 = center + new Vector3(-h.x, h.y, h.z), p111 = center + new Vector3(h.x, h.y, h.z);
            var tc = top ?? col;
            Quad(p000, p010, p110, p100, col);       // -z
            Quad(p101, p111, p011, p001, col);       // +z
            Quad(p001, p011, p010, p000, col * 0.94f); // -x
            Quad(p100, p110, p111, p101, col * 0.94f); // +x
            Quad(p010, p011, p111, p110, tc);        // top
            Quad(p001, p000, p100, p101, col * 0.7f); // bottom
        }

        /// <summary>Gable roof along local X with overhang. Ridge height above base y.</summary>
        public void GableRoof(Vector3 baseCenter, float width, float depth, float height, float overhang, Color col, Color gable)
        {
            float hw = width * 0.5f + overhang, hd = depth * 0.5f + overhang;
            Vector3 a = baseCenter + new Vector3(-hw, 0, -hd), b = baseCenter + new Vector3(hw, 0, -hd);
            Vector3 d = baseCenter + new Vector3(hw, 0, hd), e = baseCenter + new Vector3(-hw, 0, hd);
            Vector3 r0 = baseCenter + new Vector3(-hw, height, 0), r1 = baseCenter + new Vector3(hw, height, 0);
            Quad(a, r0, r1, b, col);
            Quad(d, r1, r0, e, col * 0.92f);
            // gable triangles (inset to the wall plane)
            float gw = width * 0.5f;
            Vector3 g0 = baseCenter + new Vector3(-gw, 0, -depth * 0.5f), g1 = baseCenter + new Vector3(-gw, 0, depth * 0.5f), g2 = baseCenter + new Vector3(-gw, height * (1 - overhang / hd * 0.5f), 0);
            Tri(g0, g1, g2, gable);
            Vector3 k0 = baseCenter + new Vector3(gw, 0, depth * 0.5f), k1 = baseCenter + new Vector3(gw, 0, -depth * 0.5f), k2 = baseCenter + new Vector3(gw, height * (1 - overhang / hd * 0.5f), 0);
            Tri(k0, k1, k2, gable);
            // underside
            Quad(b, r1, r0, a, col * 0.5f);
            Quad(e, r0, r1, d, col * 0.5f);
        }

        public void HipRoof(Vector3 baseCenter, float width, float depth, float height, float overhang, Color col)
        {
            float hw = width * 0.5f + overhang, hd = depth * 0.5f + overhang;
            Vector3 a = baseCenter + new Vector3(-hw, 0, -hd), b = baseCenter + new Vector3(hw, 0, -hd);
            Vector3 d = baseCenter + new Vector3(hw, 0, hd), e = baseCenter + new Vector3(-hw, 0, hd);
            float ridge = Mathf.Max(0, hw - hd);
            Vector3 r0 = baseCenter + new Vector3(-ridge, height, 0), r1 = baseCenter + new Vector3(ridge, height, 0);
            Quad(a, r0, r1, b, col);
            Quad(d, r1, r0, e, col * 0.9f);
            Tri(e, r0, a, col * 0.95f);
            Tri(b, r1, d, col * 0.95f);
        }

        public void Pyramid(Vector3 baseCenter, float width, float height, Color col, int sides = 4, float rot = 45)
        {
            Cone(baseCenter, width * 0.5f / Mathf.Cos(Mathf.PI / sides), height, sides, col, rot, 0);
        }

        public void Cylinder(Vector3 baseCenter, float radius, float height, int sides, Color col, Color? capCol = null, float topRadius = -1)
        {
            if (topRadius < 0) topRadius = radius;
            var cap = capCol ?? col;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 b0 = baseCenter + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                Vector3 b1 = baseCenter + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                Vector3 t0 = baseCenter + new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);
                Vector3 t1 = baseCenter + new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);
                float s0 = SwayAt(b0 - baseCenter, height), s1 = SwayAt(t0 - baseCenter, height);
                Tri(b0, t0, t1, col, s0, s1, s1);
                Tri(b0, t1, b1, col, s0, s1, s0);
                Tri(baseCenter + Vector3.up * height, t1, t0, cap, s1, s1, s1);
            }
        }

        public void Cone(Vector3 baseCenter, float radius, float height, int sides, Color col, float rotDeg = 0, float swayBase = 0)
        {
            float rot = rotDeg * Mathf.Deg2Rad;
            Vector3 tip = baseCenter + Vector3.up * height;
            float tipSway = sway;
            for (int i = 0; i < sides; i++)
            {
                float a0 = rot + i * Mathf.PI * 2 / sides, a1 = rot + (i + 1) * Mathf.PI * 2 / sides;
                Vector3 b0 = baseCenter + new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                Vector3 b1 = baseCenter + new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                Tri(b0, tip, b1, col, swayBase, tipSway, swayBase);
                Tri(b1, baseCenter, b0, col * 0.6f, swayBase, swayBase, swayBase);
            }
        }

        /// <summary>Faceted blob (displaced octahedral sphere) for canopies, bushes, rocks, clouds.</summary>
        public void Blob(Vector3 center, Vector3 radii, Color col, int seed, float noise = 0.18f, int detail = 1, float swayLow = 0, float swayHigh = 0)
        {
            var verts = new List<Vector3>
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };
            var tris = new List<int> { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            for (int s = 0; s < detail; s++)
            {
                var nt = new List<int>();
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    mid[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], d = tris[i + 2];
                    int ab = Mid(a, b), bd = Mid(b, d), da = Mid(d, a);
                    nt.AddRange(new[] { a, ab, da, ab, b, bd, da, bd, d, ab, bd, da });
                }
                tris = nt;
            }
            var rng = new System.Random(seed);
            var disp = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                float k = 1f + ((float)rng.NextDouble() * 2 - 1) * noise;
                disp[i] = center + Vector3.Scale(verts[i] * k, radii);
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = disp[tris[i]], b = disp[tris[i + 1]], d = disp[tris[i + 2]];
                float yA = verts[tris[i]].y, yB = verts[tris[i + 1]].y, yD = verts[tris[i + 2]].y;
                float shade = 0.9f + 0.1f * ((yA + yB + yD) / 3f);
                Tri(a, b, d, col * shade, Mathf.Lerp(swayLow, swayHigh, yA * 0.5f + 0.5f), Mathf.Lerp(swayLow, swayHigh, yB * 0.5f + 0.5f), Mathf.Lerp(swayLow, swayHigh, yD * 0.5f + 0.5f));
            }
        }

        public Mesh ToMesh(string name = "goa_mesh")
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetColors(c);
            m.SetUVs(0, uv);
            m.subMeshCount = _subs.Count;
            for (int i = 0; i < _subs.Count; i++) m.SetTriangles(_subs[i], i, true);
            m.RecalculateBounds();
            return m;
        }

        public void Clear()
        {
            v.Clear(); n.Clear(); c.Clear(); uv.Clear();
            foreach (var s in _subs) s.Clear();
            _sub = 0; M = Matrix4x4.identity; sway = 0;
        }
    }
}
