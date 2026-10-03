// Uniform-grid broadphase over the level's static geometry (port of zenith-umbra src/engine/Broadphase.ts, engine core).
// Every Level query used to walk every box of the map - groundAt and collide per hero per 120 Hz step, a ray per bot
// sight check, camera pull-in, hitscan, ragdoll bone - so their cost grew with map detail. Items are binned by their XZ
// footprint into square cells; a query visits only the cells it touches. Results come back as item indices in
// ascending order, so callers can run their original loop over the candidates in the original order and get
// bit-identical answers (ties, sequential pushes).
using System;
using System.Collections.Generic;

namespace ZU.Sim
{
    public sealed class GridIndex
    {
        static readonly int[] EMPTY = new int[0];

        double x0, z0, inv = 1; int nx, nz;
        readonly int[][] cells = new int[0][];
        readonly uint[] stamp;
        uint q;
        readonly double cell;
        public readonly int count;
        // the segment's clipped t range (TS module-level _span; per grid here, so two levels never share it)
        readonly double[] _span = new double[2];

        /// <param name="bounds">item i's XZ footprint [minX, minZ, maxX, maxZ]</param>
        public GridIndex(int count, Func<int, double[]> bounds, double cell = 6)
        {
            this.count = count;
            this.cell = cell;
            stamp = new uint[count];
            if (count == 0) return;
            double ax = double.PositiveInfinity, az = double.PositiveInfinity, bx = double.NegativeInfinity, bz = double.NegativeInfinity;
            var bb = new double[count][];
            for (int i = 0; i < count; i++)
            {
                // a hair of padding: a point exactly on a footprint's edge (or a ray through a cell corner) still finds it
                var f = bounds(i); const double e = 1e-3;
                bb[i] = new[] { f[0] - e, f[1] - e, f[2] + e, f[3] + e };
                ax = Math.Min(ax, f[0] - e); az = Math.Min(az, f[1] - e); bx = Math.Max(bx, f[2] + e); bz = Math.Max(bz, f[3] + e);
            }
            inv = 1 / cell;
            x0 = ax; z0 = az;
            nx = Math.Max(1, (int)Math.Ceiling((bx - ax) * inv));
            nz = Math.Max(1, (int)Math.Ceiling((bz - az) * inv));
            var lists = new List<int>[nx * nz];
            for (int k = 0; k < lists.Length; k++) lists[k] = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var b = bb[i];
                int cx0 = Cx(b[0]), cx1 = Cx(b[2]), cz0 = Cz(b[1]), cz1 = Cz(b[3]);
                for (int z = cz0; z <= cz1; z++) for (int x = cx0; x <= cx1; x++) lists[z * nx + x].Add(i);   // ascending i
            }
            cells = new int[lists.Length][];
            for (int k = 0; k < lists.Length; k++) cells[k] = lists[k].Count > 0 ? lists[k].ToArray() : EMPTY;
        }

        // clamped as doubles before the cast (as the TS clamps Math.floor's double): an out-of-range value can't wrap
        // (a NaN coordinate - a broken sim state - lands in cell 0 instead of throwing; the TS would throw there)
        int Cx(double x) { double c = Math.Min(nx - 1, Math.Max(0, Math.Floor((x - x0) * inv))); return double.IsNaN(c) ? 0 : (int)c; }
        int Cz(double z) { double c = Math.Min(nz - 1, Math.Max(0, Math.Floor((z - z0) * inv))); return double.IsNaN(c) ? 0 : (int)c; }
        bool Inside(double x, double z) => x >= x0 && z >= z0 && x <= x0 + nx * cell && z <= z0 + nz * cell;

        /// <summary>items whose cell holds the point (ascending; empty outside the grid)</summary>
        public int[] At(double x, double z)
        {
            if (count == 0 || !Inside(x, z)) return EMPTY;
            return cells[Cz(z) * nx + Cx(x)];
        }

        /// <summary>items in the cells a rectangle overlaps (deduplicated, ascending)</summary>
        public List<int> Rect(double rx0, double rz0, double rx1, double rz1, List<int> output)
        {
            output.Clear();
            if (count == 0 || rx1 < x0 || rz1 < z0 || rx0 > x0 + nx * cell || rz0 > z0 + nz * cell) return output;
            var s = Next();
            int cx0 = Cx(rx0), cx1 = Cx(rx1), cz0 = Cz(rz0), cz1 = Cz(rz1);
            for (int z = cz0; z <= cz1; z++) for (int x = cx0; x <= cx1; x++) Take(cells[z * nx + x], s, output);
            if (cx1 > cx0 || cz1 > cz0) output.Sort();
            return output;
        }

        /// <summary>items in the cells the segment o + d*t, t in [0, max], passes over in XZ (deduplicated, ascending)</summary>
        public List<int> Segment(double ox, double oz, double dx, double dz, double max, List<int> output)
        {
            output.Clear();
            if (count == 0) return output;
            var s = Next();
            double W = nx * cell, H = nz * cell;
            // clip the segment to the grid rectangle
            _span[0] = 0; _span[1] = max;
            if (!Clip(ox, dx, x0, x0 + W) || !Clip(oz, dz, z0, z0 + H)) return output;
            double t0 = _span[0], t1 = _span[1];
            // Amanatides & Woo grid walk from the entry point
            double px = ox + dx * t0, pz = oz + dz * t0;
            int x = Cx(px), z = Cz(pz);
            int sx = dx > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            double ddx = Math.Abs(dx) < 1e-12 ? double.PositiveInfinity : cell / Math.Abs(dx);
            double ddz = Math.Abs(dz) < 1e-12 ? double.PositiveInfinity : cell / Math.Abs(dz);
            double bxn = x0 + (x + (sx > 0 ? 1 : 0)) * cell, bzn = z0 + (z + (sz > 0 ? 1 : 0)) * cell;
            double tx = double.IsPositiveInfinity(ddx) ? double.PositiveInfinity : t0 + (bxn - px) / dx;
            double tz = double.IsPositiveInfinity(ddz) ? double.PositiveInfinity : t0 + (bzn - pz) / dz;
            for (int guard = 0; guard < nx + nz + 2; guard++)
            {
                Take(cells[z * nx + x], s, output);
                if (tx <= tz) { if (tx > t1) break; x += sx; tx += ddx; } else { if (tz > t1) break; z += sz; tz += ddz; }
                if (x < 0 || z < 0 || x >= nx || z >= nz) break;
            }
            output.Sort();
            return output;
        }

        uint Next() { if (++q == 0xffffffff) { Array.Clear(stamp, 0, stamp.Length); q = 1; } return q; }
        void Take(int[] list, uint s, List<int> output)
        {
            for (int k = 0; k < list.Length; k++) { int i = list[k]; if (stamp[i] != s) { stamp[i] = s; output.Add(i); } }
        }

        /// <summary>narrow _span (t range) to where o + d*t lies within [lo, hi]; false when it empties</summary>
        bool Clip(double o, double d, double lo, double hi)
        {
            if (Math.Abs(d) < 1e-12) return o >= lo && o <= hi;
            double a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { var t = a; a = b; b = t; }
            if (a > _span[0]) _span[0] = a;
            if (b < _span[1]) _span[1] = b;
            return _span[0] <= _span[1];
        }
    }
}
