// Static level collision, as the simulation sees it (port of zenith-umbra src/engine/Physics.ts).
// ILevel is what World / abilities / bots call. Two implementations:
//   BoxLevel   - the exact port of the TS axis-aligned-box level (parity tests against the TS sim; the classic arenas)
//   PhysXLevel - (ZU.Game) the same contract answered by Unity physics queries against the detailed Unity maps
using System;
using System.Collections.Generic;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public struct RayHit { public double t, nx, ny, nz; public string mat; }

    public interface ILevel
    {
        MapDef Map { get; }
        double KillY { get; }
        /// <summary>half extents X, Z of the playable area</summary>
        double[] Size { get; }
        IReadOnlyList<Pad> Pads { get; }
        /// <summary>highest walkable surface at (x,z) not above fromY + STEP; -Infinity = void (a fatal fall)</summary>
        double GroundAt(double x, double z, double fromY, double radius = 0);
        /// <summary>material of the surface GroundAt would stand on (footsteps)</summary>
        string MatAt(double x, double z, double fromY);
        /// <summary>push a vertical capsule (feet at p.y, radius r, height h) out of walls; true if it touched one</summary>
        bool Collide(ref V3 p, double r, double h);
        /// <summary>bottom of the lowest solid above headY at (x,z); +Infinity = open sky</summary>
        double CeilingAt(double x, double z, double headY);
        /// <summary>ray vs level, d normalised; null = nothing within max</summary>
        RayHit? Ray(V3 o, V3 d, double max);
        bool LineOfSight(V3 a, V3 b);
        Pad PadAt(double x, double z, double y);
    }

    public static class LevelConst
    {
        /// <summary>step height: what a hero walks up without jumping</summary>
        public const double STEP = 0.55;
    }

    public sealed class BoxLevel : ILevel
    {
        public struct Solid { public double x, z, r, y0, y1; }
        readonly List<Box> boxes, floors;
        readonly List<Solid> solids = new List<Solid>();
        public IReadOnlyList<Box> Boxes => boxes;
        public IReadOnlyList<Box> Floors => floors;
        public IReadOnlyList<Solid> Solids => solids;
        readonly List<Pad> pads;
        public MapDef Map { get; }
        public double KillY => Map.killY;
        public double[] Size => Map.size;
        public IReadOnlyList<Pad> Pads => pads;
        const double STEP = LevelConst.STEP;
        /// <summary>Collide(): candidates are gathered this far beyond the capsule; a push sequence that carries it further
        /// re-runs over every box (so the result is always exactly the full loop's)</summary>
        const double PUSH_PAD = 2;
        readonly List<int> _cand = new List<int>();
        static double[] Footprint(Box b) => new[] { b.x - b.w / 2, b.z - b.d / 2, b.x + b.w / 2, b.z + b.d / 2 };
        // broadphase grids over the static geometry (Broadphase.cs), built on first use
        GridIndex gBoxes, gFloors, gSolids;
        GridIndex BoxGrid()
        {
            if (gBoxes == null || gBoxes.count != boxes.Count) gBoxes = new GridIndex(boxes.Count, i => Footprint(boxes[i]));
            return gBoxes;
        }
        GridIndex FloorGrid()
        {
            if (gFloors == null || gFloors.count != floors.Count) gFloors = new GridIndex(floors.Count, i => Footprint(floors[i]));
            return gFloors;
        }
        GridIndex SolidGrid()
        {
            if (gSolids == null || gSolids.count != solids.Count) gSolids = new GridIndex(solids.Count, i => { var s = solids[i]; return new[] { s.x - s.r, s.z - s.r, s.x + s.r, s.z + s.r }; });
            return gSolids;
        }

        public BoxLevel(MapDef map)
        {
            Map = map;
            boxes = map.boxes ?? new List<Box>();
            floors = map.floors ?? new List<Box>();
            pads = map.pads ?? new List<Pad>();
            foreach (var p in map.props ?? new List<Prop>())
                if (p.solid.HasValue && p.solid.Value > 0)
                {
                    var y0 = p.y ?? GroundAt(p.x, p.z, 50);
                    solids.Add(new Solid { x = p.x, z = p.z, r = p.solid.Value, y0 = y0, y1 = y0 + (p.s ?? 2) * 0.9 });
                }
        }

        /// <summary>surface height of a box/ramp at (x,z), or null outside its footprint</summary>
        public static double? Top(Box b, double x, double z)
        {
            double hx = b.w / 2, hz = b.d / 2;
            if (x < b.x - hx || x > b.x + hx || z < b.z - hz || z > b.z + hz) return null;
            double y0 = b.y ?? 0;
            if (b.ramp == null) return y0 + b.h;
            double f;
            if (b.ramp == "x+") f = (x - (b.x - hx)) / b.w;
            else if (b.ramp == "x-") f = ((b.x + hx) - x) / b.w;
            else if (b.ramp == "z+") f = (z - (b.z - hz)) / b.d;
            else f = ((b.z + hz) - z) / b.d;
            return y0 + b.h * Math.Min(1, Math.Max(0, f));
        }

        public string MatAt(double x, double z, double fromY)
        {
            double g = double.NegativeInfinity; string m = null; double lim = fromY + STEP;
            int[] fl = FloorGrid().At(x, z), bl = BoxGrid().At(x, z);
            for (int k = 0; k < fl.Length; k++) { var b = floors[fl[k]]; var t = Top(b, x, z); if (t.HasValue && t.Value <= lim && t.Value > g) { g = t.Value; m = b.mat ?? "ground"; } }
            for (int k = 0; k < bl.Length; k++) { var b = boxes[bl[k]]; var t = Top(b, x, z); if (t.HasValue && t.Value <= lim && t.Value > g) { g = t.Value; m = b.mat ?? "ground"; } }
            return m;
        }

        public double GroundAt(double x, double z, double fromY, double radius = 0)
        {
            double lim = fromY + STEP;
            double g = Probe(x, z, lim, double.NegativeInfinity);
            if (radius > 0 && double.IsNegativeInfinity(g))
            {
                // standing on an edge: count the footprint so heroes don't slip off ledges they visibly stand on
                double r = radius * 0.6;
                g = Probe(x + r, z, lim, g); g = Probe(x - r, z, lim, g); g = Probe(x, z + r, lim, g); g = Probe(x, z - r, lim, g);
            }
            return g;
        }

        /// <summary>highest surface top at (px,pz) that is &lt;= lim and above g</summary>
        double Probe(double px, double pz, double lim, double g)
        {
            int[] fl = FloorGrid().At(px, pz), bl = BoxGrid().At(px, pz);
            for (int k = 0; k < fl.Length; k++) { var t = Top(floors[fl[k]], px, pz); if (t.HasValue && t.Value <= lim && t.Value > g) g = t.Value; }
            for (int k = 0; k < bl.Length; k++) { var t = Top(boxes[bl[k]], px, pz); if (t.HasValue && t.Value <= lim && t.Value > g) g = t.Value; }
            return g;
        }

        public bool Collide(ref V3 p, double r, double h)
        {
            // Only boxes within r + PUSH_PAD of the start can be touched while every push keeps the capsule within PUSH_PAD
            // of it - anything further fails the distance test anyway - so the candidates give the full loop's exact answer.
            // A push that carries it further aborts, and the whole pass re-runs over everything.
            double x0 = p.x, z0 = p.z, R = r + PUSH_PAD;
            bool? hit = CollideBoxes(ref p, r, h, BoxGrid().Rect(x0 - R, z0 - R, x0 + R, z0 + R, _cand), x0, z0);
            if (hit == null) { p.x = x0; p.z = z0; hit = CollideBoxes(ref p, r, h, null, x0, z0); }
            double x1 = p.x, z1 = p.z;
            bool? sh = CollideSolids(ref p, r, h, SolidGrid().Rect(x1 - R, z1 - R, x1 + R, z1 + R, _cand), x1, z1);
            if (sh == null) { p.x = x1; p.z = z1; sh = CollideSolids(ref p, r, h, null, x1, z1); }
            return hit.Value || sh.Value;
        }

        /// <summary>Collide()'s box pass over candidate indices (ascending) - null = moved past PUSH_PAD - or over every box</summary>
        bool? CollideBoxes(ref V3 p, double r, double h, List<int> cand, double x0, double z0)
        {
            bool hit = false;
            int n = cand != null ? cand.Count : boxes.Count;
            for (int k = 0; k < n; k++)
            {
                var b = boxes[cand != null ? cand[k] : k];
                double y0 = b.y ?? 0, hx = b.w / 2, hz = b.d / 2;
                double cx = Math.Max(b.x - hx, Math.Min(p.x, b.x + hx));
                double cz = Math.Max(b.z - hz, Math.Min(p.z, b.z + hz));
                // ramps are walkable up their slope but block like a wall wherever the local surface is above step height
                double top = b.ramp != null ? Top(b, cx, cz).Value : y0 + b.h;
                if (p.y >= top - STEP * 0.98 || p.y + h <= y0) continue;
                double dx = p.x - cx, dz = p.z - cz, d2 = dx * dx + dz * dz;
                if (d2 >= r * r) continue;
                hit = true;
                if (d2 > 1e-8)
                {
                    double d = Math.Sqrt(d2), push = r - d;
                    p.x += dx / d * push; p.z += dz / d * push;
                }
                else
                {
                    // centre inside the box: exit along the shallowest axis (first minimum, as JS indexOf)
                    double[] ex = { b.x + hx - p.x + r, p.x - (b.x - hx) + r, b.z + hz - p.z + r, p.z - (b.z - hz) + r };
                    int i = 0; for (int k = 1; k < 4; k++) if (ex[k] < ex[i]) i = k;
                    double m = ex[i];
                    if (i == 0) p.x += m; else if (i == 1) p.x -= m; else if (i == 2) p.z += m; else p.z -= m;
                }
                if (cand != null && (Math.Abs(p.x - x0) > PUSH_PAD || Math.Abs(p.z - z0) > PUSH_PAD)) return null;
            }
            return hit;
        }

        /// <summary>the same for the cylindrical prop solids (binned by their radius, so the same margin argument holds)</summary>
        bool? CollideSolids(ref V3 p, double r, double h, List<int> cand, double x0, double z0)
        {
            bool hit = false;
            int n = cand != null ? cand.Count : solids.Count;
            for (int k = 0; k < n; k++)
            {
                var s = solids[cand != null ? cand[k] : k];
                if (p.y >= s.y1 || p.y + h <= s.y0) continue;
                double dx = p.x - s.x, dz = p.z - s.z, rr = r + s.r, d2 = dx * dx + dz * dz;
                if (d2 >= rr * rr) continue;
                double d = Math.Sqrt(d2); if (d == 0) d = 1e-4;
                p.x = s.x + dx / d * rr; p.z = s.z + dz / d * rr; hit = true;
                if (cand != null && (Math.Abs(p.x - x0) > PUSH_PAD || Math.Abs(p.z - z0) > PUSH_PAD)) return null;
            }
            return hit;
        }

        public double CeilingAt(double x, double z, double headY)
        {
            double c = double.PositiveInfinity;
            var bl = BoxGrid().At(x, z);
            for (int k = 0; k < bl.Length; k++)
            {
                var b = boxes[bl[k]];
                double y0 = b.y ?? 0;
                if (y0 > headY - 0.05 && y0 < c && !(x < b.x - b.w / 2 || x > b.x + b.w / 2 || z < b.z - b.d / 2 || z > b.z + b.d / 2)) c = y0;
            }
            return c;
        }

        public RayHit? Ray(V3 o, V3 d, double max)
        {
            // candidates = what lies in the grid cells under the ray's path, tested in the original order (boxes, floors,
            // solids; ascending), so ties and the ramp march resolve exactly as a test of every item would
            RayHit? best = null;
            void Test(Box b, double thick)
            {
                double y0 = (b.y ?? 0) - thick, y1 = (b.y ?? 0) + b.h;
                if (!Slab(o, d, b.x - b.w / 2, y0, b.z - b.d / 2, b.x + b.w / 2, y1, b.z + b.d / 2, best?.t ?? max, out var h)) return;
                if (b.ramp != null)
                {
                    // walk the ray through the prism until it drops below the slope
                    const int n = 12; double t0 = h.t, t1 = h.tExit;
                    for (int i = 0; i <= n; i++)
                    {
                        double t = t0 + (Math.Min(t1, best?.t ?? max) - t0) * ((double)i / n);
                        double px = o.x + d.x * t, py = o.y + d.y * t, pz = o.z + d.z * t;
                        var top = Top(b, px, pz);
                        if (top.HasValue && py <= top.Value) { best = new RayHit { t = t, nx = 0, ny = 1, nz = 0, mat = b.mat }; return; }
                    }
                    return;
                }
                best = new RayHit { t = h.t, nx = h.nx, ny = h.ny, nz = h.nz, mat = b.mat };
            }
            var bl = BoxGrid().Segment(o.x, o.z, d.x, d.z, max, _cand);
            for (int k = 0; k < bl.Count; k++) Test(boxes[bl[k]], 0);
            var fl = FloorGrid().Segment(o.x, o.z, d.x, d.z, max, _cand);
            for (int k = 0; k < fl.Count; k++) Test(floors[fl[k]], 1.5);
            var sl = SolidGrid().Segment(o.x, o.z, d.x, d.z, max, _cand);
            for (int k = 0; k < sl.Count; k++)
            {
                var s = solids[sl[k]];
                // vertical cylinder
                double ox = o.x - s.x, oz = o.z - s.z, a = d.x * d.x + d.z * d.z;
                if (a < 1e-9) continue;
                double bq = 2 * (ox * d.x + oz * d.z), c = ox * ox + oz * oz - s.r * s.r, disc = bq * bq - 4 * a * c;
                if (disc < 0) continue;
                double t = (-bq - Math.Sqrt(disc)) / (2 * a);
                if (t < 0 || t > (best?.t ?? max)) continue;
                double y = o.y + d.y * t;
                if (y < s.y0 || y > s.y1) continue;
                double hx = ox + d.x * t, hz = oz + d.z * t, l = M.Hypot(hx, hz); if (l == 0) l = 1;
                best = new RayHit { t = t, nx = hx / l, ny = 0, nz = hz / l };
            }
            return best;
        }

        public bool LineOfSight(V3 a, V3 b)
        {
            double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z, l = M.Hypot(dx, dy, dz);
            if (l < 1e-4) return true;
            return !Ray(a, new V3(dx / l, dy / l, dz / l), l - 0.05).HasValue;
        }

        public Pad PadAt(double x, double z, double y)
        {
            foreach (var p in pads) if (M.Hypot(x - p.x, z - p.z) < 1.6 && Math.Abs(y - (p.y ?? GroundAt(p.x, p.z, y + 1))) < 0.4) return p;
            return null;
        }

        // ray vs axis-aligned box by slabs, x then y then z (the TS order: ties resolve identically)
        struct SlabHit { public double t, tExit, nx, ny, nz; }
        static bool Slab(V3 o, V3 d, double x0, double y0, double z0, double x1, double y1, double z1, double max, out SlabHit h)
        {
            h = new SlabHit { t = 0, tExit = max };
            return Axis(ref h, o.x, d.x, x0, x1, 0) && Axis(ref h, o.y, d.y, y0, y1, 1) && Axis(ref h, o.z, d.z, z0, z1, 2);
        }
        static bool Axis(ref SlabHit h, double oo, double dd, double lo, double hi, int ax)
        {
            if (Math.Abs(dd) < 1e-9) return !(oo < lo || oo > hi);
            double t1 = (lo - oo) / dd, t2 = (hi - oo) / dd, s = -1;
            if (t1 > t2) { var tt = t1; t1 = t2; t2 = tt; s = 1; }
            if (t1 > h.t) { h.t = t1; h.nx = ax == 0 ? s : 0; h.ny = ax == 1 ? s : 0; h.nz = ax == 2 ? s : 0; }
            if (t2 < h.tExit) h.tExit = t2;
            return !(h.t > h.tExit);
        }
    }
}
