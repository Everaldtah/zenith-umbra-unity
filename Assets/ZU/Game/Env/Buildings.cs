// Procedural buildings for the world outside a map's play space, in the styles of its setting: Japanese townhouses,
// pagodas and castle keeps; western false-fronts and water towers; factory halls, chimneys and tanks; observatory
// domes; floating islands. They write into MeshBins by surface kind (wall, roof, wood, trim, plaster, stone, metal,
// rust, brick, window, ...), so a whole skyline is a handful of draw calls in the map's own materials.
// Positions are Unity world space; `y` is the ground under the building.
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.Env
{
    public static class Buildings
    {
        // ---------------------------------------------------------------------------------------------- roofs
        /// <summary>hip roof over the rectangle centre c / half extents (hx, hz) at height y0: eaves `o` beyond the walls,
        /// ridge `h` above the eaves along the longer side; an underside so it reads from below</summary>
        public static void HipRoof(MeshBins b, Vector3 c, float hx, float hz, float y0, float h, float o, string kind = "roof", string under = "wood")
        {
            float ex = hx + o, ez = hz + o, ye = y0 - o * 0.35f;   // eaves droop a little past the wall
            var e00 = new Vector3(c.x - ex, ye, c.z - ez); var e10 = new Vector3(c.x + ex, ye, c.z - ez);
            var e11 = new Vector3(c.x + ex, ye, c.z + ez); var e01 = new Vector3(c.x - ex, ye, c.z + ez);
            float top = y0 + h;
            var mid = new Vector3(c.x, y0, c.z);
            if (ex >= ez)
            {
                var r0 = new Vector3(c.x - (ex - ez), top, c.z); var r1 = new Vector3(c.x + (ex - ez), top, c.z);
                Face(b, kind, mid, e00, e10, r1, r0); Face(b, kind, mid, e11, e01, r0, r1);
                Face(b, kind, mid, e01, e00, r0); Face(b, kind, mid, e10, e11, r1);
            }
            else
            {
                var r0 = new Vector3(c.x, top, c.z - (ez - ex)); var r1 = new Vector3(c.x, top, c.z + (ez - ex));
                Face(b, kind, mid, e10, e11, r1, r0); Face(b, kind, mid, e01, e00, r0, r1);
                Face(b, kind, mid, e00, e10, r0); Face(b, kind, mid, e11, e01, r1);
            }
            b[under].Poly(new[] { e00, e10, e11, e01 }, Vector3.down);
        }

        /// <summary>gable roof (two slopes along the longer side, triangular gable ends in `gable`)</summary>
        public static void GableRoof(MeshBins b, Vector3 c, float hx, float hz, float y0, float h, float o, string kind = "roof", string gable = "wall")
        {
            float ex = hx + o, ez = hz + o, ye = y0 - o * 0.3f;
            var mid = new Vector3(c.x, y0, c.z);
            if (ex >= ez)
            {
                var a = new Vector3(c.x - ex, ye, c.z - ez); var bb = new Vector3(c.x + ex, ye, c.z - ez);
                var cc = new Vector3(c.x + ex, ye, c.z + ez); var d = new Vector3(c.x - ex, ye, c.z + ez);
                var r0 = new Vector3(c.x - ex, y0 + h, c.z); var r1 = new Vector3(c.x + ex, y0 + h, c.z);
                Face(b, kind, mid, a, bb, r1, r0); Face(b, kind, mid, cc, d, r0, r1);
                Face(b, gable, mid, new Vector3(c.x - hx, y0, c.z - hz), new Vector3(c.x - hx, y0, c.z + hz), new Vector3(c.x - hx, y0 + h, c.z));
                Face(b, gable, mid, new Vector3(c.x + hx, y0, c.z + hz), new Vector3(c.x + hx, y0, c.z - hz), new Vector3(c.x + hx, y0 + h, c.z));
                b["wood"].Poly(new[] { a, bb, cc, d }, Vector3.down);
            }
            else
            {
                var a = new Vector3(c.x - ex, ye, c.z - ez); var bb = new Vector3(c.x + ex, ye, c.z - ez);
                var cc = new Vector3(c.x + ex, ye, c.z + ez); var d = new Vector3(c.x - ex, ye, c.z + ez);
                var r0 = new Vector3(c.x, y0 + h, c.z - ez); var r1 = new Vector3(c.x, y0 + h, c.z + ez);
                Face(b, kind, mid, bb, cc, r1, r0); Face(b, kind, mid, d, a, r0, r1);
                Face(b, gable, mid, new Vector3(c.x + hx, y0, c.z - hz), new Vector3(c.x - hx, y0, c.z - hz), new Vector3(c.x, y0 + h, c.z - hz));
                Face(b, gable, mid, new Vector3(c.x - hx, y0, c.z + hz), new Vector3(c.x + hx, y0, c.z + hz), new Vector3(c.x, y0 + h, c.z + hz));
                b["wood"].Poly(new[] { a, bb, cc, d }, Vector3.down);
            }
        }

        static void Face(MeshBins b, string kind, Vector3 inside, params Vector3[] pts)
        {
            var centroid = Vector3.zero; foreach (var p in pts) centroid += p; centroid /= pts.Length;
            var outward = centroid - inside; outward.y = Mathf.Max(outward.y, 0.01f);
            b[kind].Poly(pts, outward);
        }

        // ---------------------------------------------------------------------------------------------- Japanese
        /// <summary>a machiya-style townhouse: stone plinth, 1-3 storeys of the map's wall, wooden floor bands, a pent roof
        /// over the ground floor, lit paper windows, a tiled hip roof</summary>
        public static void Townhouse(MeshBins b, System.Random r, Vector3 p, float w, float d, int floors, bool lit, string wall = "wall", string roof = "roof")
        {
            float fh = 3.1f, plinth = 0.45f;
            // the stone plinth runs 2.5 m into the ground: on a hillside the downhill side stands on a terrace wall
            b["stone"].Box(p + Vector3.up * (plinth - 2.95f) / 2, new Vector3(w / 2 + 0.15f, (plinth + 2.5f) / 2, d / 2 + 0.15f), grime: 1);
            float y = p.y + plinth;
            for (int f = 0; f < floors; f++)
            {
                float inset = f == 0 ? 0 : 0.35f;
                var c = new Vector3(p.x, y + fh / 2, p.z);
                b[wall].Box(c, new Vector3(w / 2 - inset, fh / 2, d / 2 - inset), grime: f == 0 ? 1 : 0);
                // windows: a lit (or dark) pane on the long facades, set a hair proud of the wall
                Windows(b, c, w / 2 - inset, fh, d / 2 - inset, lit, r);
                y += fh;
                b["wood"].Box(new Vector3(p.x, y - 0.12f, p.z), new Vector3(w / 2 - inset + 0.12f, 0.12f, d / 2 - inset + 0.12f));
                if (f == 0 && floors > 1)
                {
                    // pent roof (hisashi) around the ground floor
                    HipRing(b, new Vector3(p.x, y, p.z), w / 2, d / 2, 0.9f, 0.55f, roof);
                }
            }
            HipRoof(b, new Vector3(p.x, 0, p.z), w / 2 - (floors > 1 ? 0.35f : 0), d / 2 - (floors > 1 ? 0.35f : 0), y, Mathf.Min(w, d) * 0.32f, 0.85f, roof);
        }

        /// <summary>a lean-to roof ring around a storey: four sloped strips from the wall out to `o`</summary>
        static void HipRing(MeshBins b, Vector3 c, float hx, float hz, float o, float drop, string roof = "roof")
        {
            float y = c.y, ye = y - drop;
            var i00 = new Vector3(c.x - hx, y, c.z - hz); var i10 = new Vector3(c.x + hx, y, c.z - hz);
            var i11 = new Vector3(c.x + hx, y, c.z + hz); var i01 = new Vector3(c.x - hx, y, c.z + hz);
            var o00 = new Vector3(c.x - hx - o, ye, c.z - hz - o); var o10 = new Vector3(c.x + hx + o, ye, c.z - hz - o);
            var o11 = new Vector3(c.x + hx + o, ye, c.z + hz + o); var o01 = new Vector3(c.x - hx - o, ye, c.z + hz + o);
            var mid = new Vector3(c.x, y - 1, c.z);
            Face(b, roof, mid, o00, o10, i10, i00); Face(b, roof, mid, o10, o11, i11, i10);
            Face(b, roof, mid, o11, o01, i01, i11); Face(b, roof, mid, o01, o00, i00, i01);
        }

        static void Windows(MeshBins b, Vector3 c, float hx, float fh, float hz, bool lit, System.Random r)
        {
            string kind = lit ? "window" : "pane";
            float wy = c.y + 0.15f, wh = fh * 0.32f;
            foreach (int side in new[] { -1, 1 })
            {
                int n = Mathf.Max(1, Mathf.FloorToInt(hx * 2 / 2.6f));
                for (int i = 0; i < n; i++)
                {
                    if (r.NextDouble() < 0.25) continue;
                    float x = c.x - hx + (i + 0.5f) * (hx * 2 / n);
                    b[kind].Box(new Vector3(x, wy, c.z + side * (hz + 0.03f)), new Vector3(0.6f, wh / 2, 0.03f), bevel: false);
                }
                int m = Mathf.Max(1, Mathf.FloorToInt(hz * 2 / 2.6f));
                for (int i = 0; i < m; i++)
                {
                    if (r.NextDouble() < 0.35) continue;
                    float z = c.z - hz + (i + 0.5f) * (hz * 2 / m);
                    b[kind].Box(new Vector3(c.x + side * (hx + 0.03f), wy, z), new Vector3(0.03f, wh / 2, 0.6f), bevel: false);
                }
            }
        }

        /// <summary>a pagoda: `tiers` storeys stepping in, each under a wide flared hip roof, a bronze finial on top</summary>
        public static void Pagoda(MeshBins b, Vector3 p, float w, int tiers)
        {
            b["stone"].Box(p + Vector3.up * 0.6f, new Vector3(w / 2 + 1.2f, 0.6f, w / 2 + 1.2f), grime: 1);
            float y = p.y + 1.2f, s = w;
            for (int t = 0; t < tiers; t++)
            {
                float h = t == 0 ? 4.2f : 3.0f;
                b["plaster"].Box(new Vector3(p.x, y + h / 2, p.z), new Vector3(s / 2, h / 2, s / 2), grime: t == 0 ? 1 : 0);
                b["wood"].Box(new Vector3(p.x, y + h - 0.3f, p.z), new Vector3(s / 2 + 0.25f, 0.3f, s / 2 + 0.25f));
                y += h;
                HipRoof(b, new Vector3(p.x, 0, p.z), s / 2, s / 2, y, s * 0.18f, s * 0.22f + 0.8f);
                y += s * 0.12f;
                s *= 0.8f;
            }
            b["metal"].Cylinder(new Vector3(p.x, y, p.z), 0.35f, 0.12f, w * 0.75f, 8);
            for (int i = 0; i < 5; i++) b["metal"].Cylinder(new Vector3(p.x, y + 1 + i * w * 0.11f, p.z), 0.55f, 0.55f, 0.18f, 10);
        }

        /// <summary>a castle keep: a battered stone base, then white-walled storeys under tiled roofs with gables</summary>
        public static void Castle(MeshBins b, Vector3 p, float w)
        {
            float baseH = w * 0.7f;
            // battered (sloping) stone base: a frustum
            float bw = w / 2 + baseH * 0.22f, tw = w / 2;
            var c = new Vector3(p.x, p.y, p.z);
            var b00 = new Vector3(c.x - bw, c.y, c.z - bw); var b10 = new Vector3(c.x + bw, c.y, c.z - bw);
            var b11 = new Vector3(c.x + bw, c.y, c.z + bw); var b01 = new Vector3(c.x - bw, c.y, c.z + bw);
            float ty = c.y + baseH;
            var t00 = new Vector3(c.x - tw, ty, c.z - tw); var t10 = new Vector3(c.x + tw, ty, c.z - tw);
            var t11 = new Vector3(c.x + tw, ty, c.z + tw); var t01 = new Vector3(c.x - tw, ty, c.z + tw);
            var mid = new Vector3(c.x, c.y + baseH / 2, c.z);
            Face(b, "stone", mid, b00, b10, t10, t00); Face(b, "stone", mid, b10, b11, t11, t10);
            Face(b, "stone", mid, b11, b01, t01, t11); Face(b, "stone", mid, b01, b00, t00, t01);
            float y = ty, s = w;
            for (int t = 0; t < 4; t++)
            {
                float h = t == 0 ? 5f : 3.6f;
                b["plaster"].Box(new Vector3(p.x, y + h / 2, p.z), new Vector3(s / 2, h / 2, s * 0.42f));
                b["window"].Box(new Vector3(p.x, y + h * 0.55f, p.z - s * 0.42f - 0.03f), new Vector3(s * 0.3f, 0.35f, 0.03f), bevel: false);
                b["window"].Box(new Vector3(p.x, y + h * 0.55f, p.z + s * 0.42f + 0.03f), new Vector3(s * 0.3f, 0.35f, 0.03f), bevel: false);
                y += h;
                HipRoof(b, new Vector3(p.x, 0, p.z), s / 2, s * 0.42f, y, s * 0.2f, 1.6f + s * 0.06f);
                if (t % 2 == 0) GableRoof(b, new Vector3(p.x, 0, p.z - s * 0.42f), s * 0.18f, 0.6f, y - 0.6f, s * 0.16f, 0.5f, "roof", "plaster");
                y += s * 0.1f;
                s *= 0.78f;
            }
            b["metal"].Box(new Vector3(p.x - s * 0.5f, y + 0.6f, p.z), new Vector3(0.25f, 0.6f, 0.4f));   // shachihoko ridge ornaments
            b["metal"].Box(new Vector3(p.x + s * 0.5f, y + 0.6f, p.z), new Vector3(0.25f, 0.6f, 0.4f));
        }

        // ---------------------------------------------------------------------------------------------- western
        /// <summary>a frontier storefront: plank body, a taller false front, a porch roof on posts, a sign board</summary>
        public static void Storefront(MeshBins b, System.Random r, Vector3 p, float w, float d, float facing)
        {
            float h = 4.2f + (float)r.NextDouble() * 2.5f;
            b["wood"].Box(p + Vector3.up * h / 2, new Vector3(w / 2, h / 2, d / 2), grime: 1);
            GableRoof(b, new Vector3(p.x, 0, p.z), w / 2, d / 2, p.y + h, 1.6f, 0.3f, "rust", "wood");
            // false front on the side facing the street (+z or -z by `facing`)
            float fz = p.z + facing * (d / 2 + 0.1f);
            b["planks"].Box(new Vector3(p.x, p.y + (h + 2.4f) / 2, fz), new Vector3(w / 2 + 0.2f, (h + 2.4f) / 2, 0.12f));
            b["trim"].Box(new Vector3(p.x, p.y + h + 1.2f, fz + facing * 0.15f), new Vector3(w * 0.35f, 0.55f, 0.05f), bevel: false);
            // porch
            float pz = fz + facing * 1.4f;
            b["wood"].Box(new Vector3(p.x, p.y + 3.0f, pz), new Vector3(w / 2 + 0.2f, 0.08f, 1.45f));
            foreach (int sx in new[] { -1, 1 }) b["wood"].Box(new Vector3(p.x + sx * (w / 2), p.y + 1.5f, fz + facing * 2.7f), new Vector3(0.1f, 1.5f, 0.1f));
            b["window"].Box(new Vector3(p.x, p.y + 1.5f, fz + facing * 0.15f), new Vector3(w * 0.25f, 0.6f, 0.03f), bevel: false);
        }

        /// <summary>a water tower: a tank with a conical roof on a timber trestle</summary>
        public static void WaterTower(MeshBins b, Vector3 p, float r)
        {
            float legs = 7f;
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.PI / 4 + i * Mathf.PI / 2;
                b["wood"].Box(p + new Vector3(Mathf.Cos(a) * r * 0.8f, legs / 2, Mathf.Sin(a) * r * 0.8f), new Vector3(0.18f, legs / 2, 0.18f));
            }
            b["wood"].Box(p + Vector3.up * (legs * 0.5f), new Vector3(r * 0.8f, 0.08f, 0.08f));
            b["wood"].Box(p + Vector3.up * (legs * 0.5f), new Vector3(0.08f, 0.08f, r * 0.8f));
            b["planks"].Cylinder(p + Vector3.up * legs, r, r, r * 1.5f, 14, top: false, bottom: true);
            b["rust"].Cylinder(p + Vector3.up * (legs + r * 1.5f), r * 1.08f, 0.05f, r * 0.7f, 14, top: false);
        }

        // ---------------------------------------------------------------------------------------------- industrial
        /// <summary>a factory hall: brick or sheet walls, a gable roof, roof vents, a band of high windows</summary>
        public static void Hall(MeshBins b, System.Random r, Vector3 p, float w, float d, float h)
        {
            string wall = r.NextDouble() < 0.5 ? "brick" : "rust";
            b[wall].Box(p + Vector3.up * h / 2, new Vector3(w / 2, h / 2, d / 2), grime: 1);
            b["concrete"].Box(p + Vector3.up * 0.6f, new Vector3(w / 2 + 0.2f, 0.6f, d / 2 + 0.2f), grime: 1);
            GableRoof(b, new Vector3(p.x, 0, p.z), w / 2, d / 2, p.y + h, Mathf.Min(w, d) * 0.18f, 0.4f, "rust", wall);
            float len = Mathf.Max(w, d);
            int n = Mathf.Max(2, Mathf.FloorToInt(len / 6));
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n - 0.5f;
                var c = w >= d ? new Vector3(p.x + t * w, p.y + h * 0.72f, p.z) : new Vector3(p.x, p.y + h * 0.72f, p.z + t * d);
                var half = w >= d ? new Vector3(len / n * 0.35f, h * 0.12f, d / 2 + 0.03f) : new Vector3(w / 2 + 0.03f, h * 0.12f, len / n * 0.35f);
                b["window"].Box(c, half, bevel: false);
            }
        }

        /// <summary>a brick chimney stack, banded, darkened at the top</summary>
        public static void Chimney(MeshBins b, Vector3 p, float h, float r)
        {
            b["brick"].Cylinder(p, r * 1.25f, r, h, 14);
            b["metal"].Cylinder(p + Vector3.up * (h - 1.6f), r * 1.08f, r * 1.08f, 1.6f, 14);
            for (int i = 1; i < 4; i++) b["concrete"].Cylinder(p + Vector3.up * (h * i / 4), r * 1.27f - i * 0.05f, r * 1.27f - i * 0.05f, 0.5f, 14);
        }

        /// <summary>a storage tank with a shallow domed roof and a ring walkway</summary>
        public static void Tank(MeshBins b, Vector3 p, float r, float h)
        {
            b["metal"].Cylinder(p, r, r, h, 20, top: false);
            b["metal"].Dome(p + Vector3.up * h, r, r * 0.25f, 20, 4);
            b["rust"].Cylinder(p + Vector3.up * (h * 0.66f), r + 0.6f, r + 0.6f, 0.15f, 20);
        }

        // ---------------------------------------------------------------------------------------------- others
        /// <summary>an observatory: a drum with a slit dome</summary>
        public static void Observatory(MeshBins b, Vector3 p, float r)
        {
            b["stone"].Cylinder(p, r * 1.15f, r * 1.1f, 1.2f, 20);
            b["plaster"].Cylinder(p + Vector3.up * 1.2f, r, r, r * 1.1f, 20, top: false);
            b["metal"].Dome(p + Vector3.up * (1.2f + r * 1.1f), r * 1.02f, r * 0.95f, 24, 8);
            b["trim"].Box(p + new Vector3(0, 1.2f + r * 1.6f, -r * 0.82f), new Vector3(r * 0.14f, r * 0.42f, 0.1f), bevel: false);
        }

        /// <summary>a floating island: an inverted rock cone, a turf top and something built on it</summary>
        public static void FloatingIsland(MeshBins b, System.Random r, Vector3 top, float radius)
        {
            float depth = radius * (1.4f + (float)r.NextDouble());
            b["rock"].Cylinder(top - Vector3.up * depth, radius * 0.18f, radius, depth, 9, top: false, phase: (float)r.NextDouble() * 6);
            b["rock"].Cylinder(top - Vector3.up * (depth * 1.25f), 0.2f, radius * 0.25f, depth * 0.25f, 7, top: false);
            b["ground"].Cylinder(top - Vector3.up * 0.4f, radius * 1.02f, radius * 0.97f, 0.4f, 12);
            if (radius > 9) Pagoda(b, top, Mathf.Min(7, radius * 0.45f), 3);
            else if (radius > 5) Townhouse(b, r, top, radius * 0.9f, radius * 0.7f, 1, false);
        }

        /// <summary>a modern block: concrete frame with glass bands (the academy)</summary>
        public static void Block(MeshBins b, System.Random r, Vector3 p, float w, float d, int floors)
        {
            float fh = 3.6f;
            for (int f = 0; f < floors; f++)
            {
                float y = p.y + f * fh;
                b["concrete"].Box(new Vector3(p.x, y + 0.5f, p.z), new Vector3(w / 2, 0.5f, d / 2), grime: f == 0 ? 1 : 0);
                b["glass"].Box(new Vector3(p.x, y + 1 + (fh - 1) / 2, p.z), new Vector3(w / 2 - 0.25f, (fh - 1) / 2, d / 2 - 0.25f), bevel: false);
            }
            b["concrete"].Box(new Vector3(p.x, p.y + floors * fh + 0.4f, p.z), new Vector3(w / 2 + 0.2f, 0.4f, d / 2 + 0.2f));
        }
    }
}
