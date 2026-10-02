// Layered navigation grid (0.5m cells) over a BoxLevel - port of zenith-umbra src/ai/Nav.ts. Every walkable surface in a
// cell (street, upper floor, roof) is its own node (up to LAYERS per cell) if a hero can stand there; climb / drop
// limits link surfaces between neighbouring cells, wall clearance costs, jump-pad links; A* with a binary heap.
// Heights are float (TS Float32Array) so the grid rounds exactly as the TS one. The Unity maps use a NavMesh INav instead.
using System;
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public sealed class BoxNav : INav
    {
        const double CELL = 0.5;
        const double CLIMB = LevelConst.STEP + 0.05;
        const double DROP = 4.5;
        const int LAYERS = 4;
        const double HEADROOM = 1.9;

        public readonly int nx, nz; public readonly double x0, z0;
        /// <summary>surface height per node (cell * LAYERS + layer), NaN = no surface</summary>
        public readonly float[] h;
        public readonly float[] cost;
        public readonly Dictionary<int, int> padLink = new Dictionary<int, int>();
        readonly BoxLevel level;

        public BoxNav(BoxLevel level)
        {
            this.level = level;
            double X = level.Size[0], Z = level.Size[1];
            x0 = -X - 2; z0 = -Z - 2;
            nx = (int)Math.Ceiling((2 * X + 4) / CELL); nz = (int)Math.Ceiling((2 * Z + 4) / CELL);
            int n = nx * nz;
            h = Enumerable.Repeat(float.NaN, n * LAYERS).ToArray();
            cost = Enumerable.Repeat(1f, n * LAYERS).ToArray();
            var all = level.Floors.Concat(level.Boxes).ToList();
            var keep = new List<double>();
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
                {
                    double x = x0 + (i + 0.5) * CELL, z = z0 + (j + 0.5) * CELL;
                    // candidate surfaces: every floor / box top over this cell, highest first
                    var here = all.Where(b => BoxLevel.Top(b, x, z).HasValue).ToList();
                    var tops = here.Select(b => BoxLevel.Top(b, x, z).Value).OrderByDescending(v => v).ToList();
                    keep.Clear();
                    foreach (var t in tops)
                    {
                        if (keep.Any(k => Math.Abs(k - t) < 0.3)) continue;
                        // buried inside another box (a floor under a wall), or no room to stand under the next slab
                        bool ok = true;
                        foreach (var b in here)
                        {
                            double y0 = b.y ?? 0, bt = BoxLevel.Top(b, x, z).Value;
                            if (y0 < t + 0.05 && bt > t + 0.05) { ok = false; break; }        // a box rising from (or through) this surface
                            if (y0 > t + 0.05 && y0 < t + HEADROOM) { ok = false; break; }
                        }
                        if (!ok) continue;
                        foreach (var s in level.Solids) if (M.Hypot(x - s.x, z - s.z) < s.r + 0.25 && s.y1 > t + LevelConst.STEP && s.y0 < t + HEADROOM) { ok = false; break; }
                        if (!ok) continue;
                        keep.Add(t);
                        if (keep.Count >= LAYERS) break;
                    }
                    keep.Sort();
                    int c = j * nx + i;
                    for (int l = 0; l < keep.Count; l++) h[c * LAYERS + l] = (float)keep[l];
                }
            // wall clearance: nodes next to a big step up (or a drop into the void) cost more
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) for (int l = 0; l < LAYERS; l++)
                    {
                        int k = (j * nx + i) * LAYERS + l; float hk = h[k];
                        if (float.IsNaN(hk)) continue;
                        int near = 0;
                        for (int dj = -2; dj <= 2; dj++) for (int di = -2; di <= 2; di++)
                            {
                                int ii = i + di, jj = j + dj;
                                if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) { near = Math.Max(near, 1); continue; }
                                if (LayerNear(jj * nx + ii, hk) < 0) near = Math.Max(near, Math.Abs(di) <= 1 && Math.Abs(dj) <= 1 ? 2 : 1);
                            }
                        cost[k] = near == 2 ? 6f : near == 1 ? 2.2f : 1f;
                    }
            // jump pads: simulate the launch arc to find where it lands
            foreach (var p in level.Pads)
            {
                int k = NodeAt(p.x, level.GroundAt(p.x, p.z, (p.y ?? 0) + 1), p.z);
                if (k < 0) continue;
                double x = p.x, y = h[k], z = p.z, vy = p.vy;
                for (int s = 0; s < 400; s++)
                {
                    const double dt = 1.0 / 60; x += p.vx * dt; z += p.vz * dt; vy -= World.G * dt; y += vy * dt;
                    if (vy < 0) { double g = level.GroundAt(x, z, y); if (g > double.NegativeInfinity && y <= g) break; }
                    if (y < level.KillY) { x = double.NaN; break; }
                }
                int land = !double.IsNaN(x) && !double.IsInfinity(x) ? NodeAt(x, y, z) : -1;
                if (land >= 0)
                {
                    // the pad footprint launches you, so walking across it isn't a normal edge
                    int c = k / LAYERS, ci = c % nx, cj = c / nx;
                    for (int dj = -2; dj <= 2; dj++) for (int di = -2; di <= 2; di++)
                        {
                            int cc = (cj + dj) * nx + ci + di, nk = LayerNear(cc, h[k]);
                            if (nk >= 0) padLink[nk] = land;
                        }
                }
            }
        }

        /// <summary>the node of cell `c` at a surface within a step of height y (-1: none)</summary>
        int LayerNear(int c, double y, double up = CLIMB, double down = CLIMB)
        {
            if (c < 0 || c >= nx * nz) return -1;
            int best = -1; double bd = double.PositiveInfinity;
            for (int l = 0; l < LAYERS; l++)
            {
                float hv = h[c * LAYERS + l];
                if (float.IsNaN(hv) || hv - y > up || y - hv > down) continue;
                double d = Math.Abs(hv - y);
                if (d < bd) { bd = d; best = c * LAYERS + l; }
            }
            return best;
        }
        /// <summary>the node under a world point (the surface closest to its height, within a storey)</summary>
        public int NodeAt(double x, double y, double z) => LayerNear(CellOf(x, z), y, 1.2, 2.5);

        public int CellOf(double x, double z)
        {
            int i = (int)Math.Floor((x - x0) / CELL), j = (int)Math.Floor((z - z0) / CELL);
            if (i < 0 || j < 0 || i >= nx || j >= nz) return -1;
            return j * nx + i;
        }
        public V3 Center(int k)
        {
            int c = k / LAYERS, i = c % nx, j = c / nx;
            return new V3(x0 + (i + 0.5) * CELL, h[k], z0 + (j + 0.5) * CELL);
        }
        public V3? NearestPoint(V3 p, double maxR) { int k = Nearest(p, maxR); return k >= 0 ? Center(k) : (V3?)null; }
        public bool Walkable(int k) => k >= 0 && k < h.Length && !float.IsNaN(h[k]);

        /// <summary>nearest walkable node to a point, preferring the height the point is at</summary>
        public int Nearest(V3 p, double maxR = 8)
        {
            int k0 = NodeAt(p.x, p.y, p.z);
            if (k0 >= 0 && !padLink.ContainsKey(k0)) return k0;
            int c0 = CellOf(p.x, p.z);
            int i0 = c0 >= 0 ? c0 % nx : Math.Max(0, Math.Min(nx - 1, (int)Math.Floor((p.x - x0) / CELL)));
            int j0 = c0 >= 0 ? c0 / nx : Math.Max(0, Math.Min(nz - 1, (int)Math.Floor((p.z - z0) / CELL)));
            int best = -1; double bd = double.PositiveInfinity;
            int R = (int)Math.Ceiling(maxR / CELL);
            for (int dj = -R; dj <= R; dj++) for (int di = -R; di <= R; di++)
                {
                    int i = i0 + di, j = j0 + dj;
                    if (i < 0 || j < 0 || i >= nx || j >= nz) continue;
                    for (int l = 0; l < LAYERS; l++)
                    {
                        int k = (j * nx + i) * LAYERS + l;
                        if (!Walkable(k) || padLink.ContainsKey(k)) continue;
                        double d = di * di + dj * dj + Math.Abs(h[k] - p.y) * 4;
                        if (d < bd) { bd = d; best = k; }
                    }
                }
            return best;
        }

        /// <summary>the node reached by stepping from node `a` into cell `c` (climb up to a step, drop down to DROP)</summary>
        int StepInto(int a, int c)
        {
            float ha = h[a];
            // prefer staying on the same level; else the highest surface we can drop onto
            int same = LayerNear(c, ha);
            if (same >= 0) return same;
            int best = -1; double bh = double.NegativeInfinity;
            for (int l = 0; l < LAYERS; l++)
            {
                int k = c * LAYERS + l; float hb = h[k];
                if (float.IsNaN(hb) || hb - ha > CLIMB || ha - hb > DROP) continue;
                if (hb > bh) { bh = hb; best = k; }
            }
            return best;
        }

        static readonly (int di, int dj, double dc)[] D = { (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1), (1, 1, 1.414), (1, -1, 1.414), (-1, 1, 1.414), (-1, -1, 1.414) };

        /// <summary>A*; returns world waypoints (node centres), or null</summary>
        public List<V3> Find(V3 from, V3 to, int maxIter = 60000)
        {
            int s = Nearest(from, 4), g = Nearest(to, 8);
            if (s < 0 || g < 0) return null;
            if (s == g) return new List<V3> { Center(g) };
            int n = h.Length;
            var gs = new float[n]; for (int q = 0; q < n; q++) gs[q] = float.PositiveInfinity;
            var came = new int[n]; for (int q = 0; q < n; q++) came[q] = -1;
            var closed = new byte[n];
            var heap = new Heap();
            var gc = Center(g);
            double Hf(int k) { var c = Center(k); return M.Hypot(c.x - gc.x, c.z - gc.z) + Math.Abs(c.y - gc.y) * 0.5; }
            gs[s] = 0; heap.Push(s, Hf(s));
            int it = 0;
            while (heap.Size > 0 && it++ < maxIter)
            {
                int k = heap.Pop();
                if (k == g) break;
                if (closed[k] != 0) continue;
                closed[k] = 1;
                int c = k / LAYERS, i = c % nx, j = c / nx;
                if (padLink.TryGetValue(k, out var pl) && k != s)
                {
                    V3 c0 = Center(k), c1 = Center(pl);
                    float ng = (float)(gs[k] + M.Hypot(c1.x - c0.x, c1.z - c0.z) * CELL * 0.6);
                    if (ng < gs[pl]) { gs[pl] = ng; came[pl] = k; heap.Push(pl, ng + Hf(pl)); }
                    continue;
                }
                foreach (var (di, dj, dc) in D)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) continue;
                    int nk = StepInto(k, jj * nx + ii);
                    if (nk < 0 || closed[nk] != 0) continue;
                    // no corner cutting through a wall
                    if (di != 0 && dj != 0 && (StepInto(k, j * nx + ii) < 0 || StepInto(k, jj * nx + i) < 0)) continue;
                    double drop = Math.Max(0, h[k] - h[nk]);
                    float ng = (float)(gs[k] + dc * CELL * cost[nk] + (drop > CLIMB ? drop * 0.6 : 0));
                    if (ng < gs[nk]) { gs[nk] = ng; came[nk] = k; heap.Push(nk, ng + Hf(nk)); }
                }
            }
            if (came[g] < 0) return null;
            var nodes = new List<int>();
            for (int k = g; k != -1 && k != s; k = came[k]) nodes.Add(k);
            nodes.Reverse();
            // string-pull: drop intermediate nodes while the straight line stays walkable on the same surfaces
            var output = new List<V3>();
            int cur = s, idx = 0;
            while (idx < nodes.Count)
            {
                int far = idx;
                if (!padLink.ContainsKey(cur))
                {
                    for (int m = Math.Min(nodes.Count - 1, idx + 24); m > idx; m--)
                    {
                        if (padLink.ContainsKey(nodes[m - 1]) && m - 1 >= idx) continue;
                        if (Straight(cur, nodes[m])) { far = m; break; }
                    }
                }
                output.Add(Center(nodes[far]));
                cur = nodes[far]; idx = far + 1;
            }
            return output;
        }

        /// <summary>can you walk straight between two nodes without big steps (following the surfaces along the line)?</summary>
        public bool Straight(int a, int b)
        {
            V3 ca = Center(a), cb = Center(b);
            double d = M.Hypot(cb.x - ca.x, cb.z - ca.z); int n = (int)Math.Ceiling(d / (CELL * 0.5));
            int prev = a;
            for (int s = 1; s <= n; s++)
            {
                double x = ca.x + (cb.x - ca.x) * s / n, z = ca.z + (cb.z - ca.z) * s / n;
                int c = CellOf(x, z);
                if (c != prev / LAYERS)
                {
                    int k = c < 0 ? -1 : LayerNear(c, h[prev]);
                    if (k < 0 || padLink.ContainsKey(k) || cost[k] > 5) return false;
                    prev = k;
                }
            }
            return prev == b || Math.Abs(h[prev] - h[b]) < CLIMB;
        }

        /// <summary>binary min-heap (TS Heap: priorities as JS numbers)</summary>
        sealed class Heap
        {
            readonly List<int> k = new List<int>(); readonly List<double> p = new List<double>();
            public int Size => k.Count;
            public void Push(int key, double pri)
            {
                k.Add(key); p.Add(pri);
                int i = k.Count - 1;
                while (i > 0) { int q = (i - 1) >> 1; if (p[q] <= p[i]) break; Sw(i, q); i = q; }
            }
            public int Pop()
            {
                int top = k[0]; int lk = k[k.Count - 1]; double lp = p[p.Count - 1];
                k.RemoveAt(k.Count - 1); p.RemoveAt(p.Count - 1);
                if (k.Count > 0)
                {
                    k[0] = lk; p[0] = lp;
                    int i = 0;
                    for (; ; )
                    {
                        int l = i * 2 + 1, r = l + 1, m = i;
                        if (l < k.Count && p[l] < p[m]) m = l;
                        if (r < k.Count && p[r] < p[m]) m = r;
                        if (m == i) break;
                        Sw(i, m); i = m;
                    }
                }
                return top;
            }
            void Sw(int a, int b) { (k[a], k[b]) = (k[b], k[a]); (p[a], p[b]) = (p[b], p[a]); }
        }
    }
}
