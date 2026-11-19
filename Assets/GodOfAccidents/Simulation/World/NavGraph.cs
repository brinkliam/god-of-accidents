using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public enum NodeKind { Junction, Place, Door, Field, Edge }

    public class NavNode
    {
        public int id;
        public string name;
        public Vec2 pos;
        public NodeKind kind;
        public float areaRadius;   // activities at this node spread within this radius
        public List<int> links = new List<int>();
    }

    /// <summary>Small road graph with all-pairs shortest paths (Floyd-Warshall). Rebuilt when roads change.</summary>
    public sealed class NavGraph
    {
        public readonly List<NavNode> nodes = new List<NavNode>();
        readonly Dictionary<string, int> _byName = new Dictionary<string, int>();
        int[,] _next;
        float[,] _dist;
        bool _dirty = true;

        public int Add(string name, float x, float z, NodeKind kind = NodeKind.Junction, float area = 2f)
        {
            var n = new NavNode { id = nodes.Count, name = name, pos = new Vec2(x, z), kind = kind, areaRadius = area };
            nodes.Add(n);
            _byName[name] = n.id;
            _dirty = true;
            return n.id;
        }

        public int this[string name] => _byName.TryGetValue(name, out var id) ? id : -1;
        public NavNode Node(int id) => nodes[id];

        public void Link(int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return;
            if (!nodes[a].links.Contains(b)) nodes[a].links.Add(b);
            if (!nodes[b].links.Contains(a)) nodes[b].links.Add(a);
            _dirty = true;
        }

        public void Link(string a, string b) => Link(this[a], this[b]);

        public int Nearest(Vec2 p, Func<NavNode, bool> filter = null)
        {
            int best = -1; float bd = float.MaxValue;
            foreach (var n in nodes)
            {
                if (filter != null && !filter(n)) continue;
                float d = (n.pos - p).SqrLength;
                if (d < bd) { bd = d; best = n.id; }
            }
            return best;
        }

        void Rebuild()
        {
            int n = nodes.Count;
            _dist = new float[n, n];
            _next = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    _dist[i, j] = i == j ? 0 : float.PositiveInfinity;
                    _next[i, j] = -1;
                }
            foreach (var a in nodes)
                foreach (var b in a.links)
                {
                    _dist[a.id, b] = Vec2.Distance(a.pos, nodes[b].pos);
                    _next[a.id, b] = b;
                }
            for (int i = 0; i < n; i++) _next[i, i] = i;
            for (int k = 0; k < n; k++)
                for (int i = 0; i < n; i++)
                {
                    float dik = _dist[i, k];
                    if (float.IsPositiveInfinity(dik)) continue;
                    for (int j = 0; j < n; j++)
                    {
                        float nd = dik + _dist[k, j];
                        if (nd < _dist[i, j]) { _dist[i, j] = nd; _next[i, j] = _next[i, k]; }
                    }
                }
            _dirty = false;
        }

        public float Distance(int a, int b)
        {
            if (_dirty) Rebuild();
            return _dist[a, b];
        }

        public List<int> PathNodes(int a, int b)
        {
            if (_dirty) Rebuild();
            var list = new List<int>();
            if (a < 0 || b < 0 || _next[a, b] < 0) return list;
            int cur = a;
            list.Add(cur);
            int guard = 0;
            while (cur != b && guard++ < 512)
            {
                cur = _next[cur, b];
                list.Add(cur);
            }
            return list;
        }

        /// <summary>Polyline from a free position to a node, via the nearest road node.</summary>
        public List<Vec2> Route(Vec2 from, int fromNode, int toNode, Vec2 toPos)
        {
            var pts = new List<Vec2> { from };
            foreach (var id in PathNodes(fromNode, toNode))
            {
                var p = nodes[id].pos;
                if (Vec2.Distance(pts[pts.Count - 1], p) > 0.05f) pts.Add(p);
            }
            if (Vec2.Distance(pts[pts.Count - 1], toPos) > 0.05f) pts.Add(toPos);
            return pts;
        }

        public static float Length(List<Vec2> pts)
        {
            float L = 0;
            for (int i = 1; i < pts.Count; i++) L += Vec2.Distance(pts[i - 1], pts[i]);
            return L;
        }

        public static Vec2 Sample(List<Vec2> pts, float distance)
        {
            if (pts == null || pts.Count == 0) return default;
            if (distance <= 0) return pts[0];
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vec2.Distance(pts[i - 1], pts[i]);
                if (distance <= seg) return Vec2.Lerp(pts[i - 1], pts[i], seg > 0 ? distance / seg : 1);
                distance -= seg;
            }
            return pts[pts.Count - 1];
        }

        /// <summary>Where along a polyline (distance from start) the path comes within radius of p; -1 if never.</summary>
        public static float FirstApproach(List<Vec2> pts, Vec2 p, float radius, float startDistance = 0)
        {
            float acc = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                float seg = Vec2.Distance(a, b);
                if (acc + seg >= startDistance)
                {
                    float d = Vec2.DistanceToSegment(p, a, b, out float t);
                    if (d <= radius)
                    {
                        // step back from the closest point to where the circle is first entered
                        float along = t * seg;
                        float back = (float)Math.Sqrt(Math.Max(0, radius * radius - d * d));
                        float hit = acc + Math.Max(0, along - back);
                        if (hit >= startDistance - 0.01f) return hit;
                        if (acc + along >= startDistance) return startDistance;
                    }
                }
                acc += seg;
            }
            return -1;
        }
    }
}
