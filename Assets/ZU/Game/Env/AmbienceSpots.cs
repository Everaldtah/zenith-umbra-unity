// Where the map's ambience comes from (evera-eb's AmbientEmitter, ZU.Game.Audio: MatchAudio plays the nearest few of each id,
// fading out over the outer third of the radius, on the ambience bus): waves every ~15 m along Hanabi's water edge, the wind
// off Cloudstep's cloud sea round its islands, the festival lanterns' hum on the lantern props, Iron Gulch's windpumps and
// lamps. Each is a child GameObject of the level, so it goes when the level goes; an id with no sound yet stays silent.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.Audio;
using ZU.Sim.Data;

namespace ZU.Game.Env
{
    public static class AmbienceSpots
    {
        public static void Build(MapDef map, Transform parent)
        {
            var root = new GameObject("Ambience").transform; root.SetParent(parent, false);
            var th = OuterWorld.For(map);
            var floors = map.floors ?? new List<Box>();
            // the harbour: the floor edges that face open water (the sea plane at the map's water level)
            if (th.sea && map.water.HasValue)
                foreach (var p in Edges(floors, 15, 8)) Spot(root, "amb_waves", Conv.U(p.x, map.water.Value, p.y), 25);
            // the cloud sea: the island edges facing the drop, a few metres under the rim
            if (th.clouds)
                foreach (var p in Edges(floors, 30, 16)) Spot(root, "amb_cloudwind", Conv.U(p.x, p.z - 6, p.y), 40);
            foreach (var pr in map.props ?? new List<Prop>())
            {
                string id = pr.id ?? "";
                var at = Conv.U(pr.x, (pr.y ?? 0) + (pr.s ?? 1) * 0.6, pr.z);
                if (id.Contains("lantern") || id == "prop_kagura_lamp" || id == "prop_hanabi_stall") Spot(root, "amb_lanterns", at, 14);
                else if (id.Contains("windpump")) Spot(root, "amb_windpump_creak", at, 12);
                else if (id == "prop_gulch_lamp") Spot(root, "amb_lamp_hum", at, 6);
            }
        }

        static void Spot(Transform root, string id, Vector3 at, float radius)
        {
            var go = new GameObject(id); go.transform.SetParent(root, false); go.transform.localPosition = at;
            AmbientEmitter.Add(go, id, radius);
        }

        /// <summary>points every `step` m along the floor edges whose outside (2 m out) no floor covers - the water / the drop -
        /// none closer than `minGap` to another; x, z (sim) and the floor's top in z of the returned Vector3</summary>
        static List<Vector3> Edges(List<Box> floors, double step, double minGap)
        {
            var pts = new List<Vector3>();
            foreach (var f in floors)
            {
                double top = (f.y ?? 0) + 0.01, x0 = f.x - f.w / 2, x1 = f.x + f.w / 2, z0 = f.z - f.d / 2, z1 = f.z + f.d / 2;
                // the four edges: from, to, outward normal
                var edges = new (double ax, double az, double bx, double bz, double nx, double nz)[]
                    { (x0, z0, x1, z0, 0, -1), (x0, z1, x1, z1, 0, 1), (x0, z0, x0, z1, -1, 0), (x1, z0, x1, z1, 1, 0) };
                foreach (var e in edges)
                {
                    double len = System.Math.Abs(e.bx - e.ax) + System.Math.Abs(e.bz - e.az);
                    int n = System.Math.Max(1, (int)System.Math.Round(len / step));
                    for (int i = 0; i < n; i++)
                    {
                        double t = (i + 0.5) / n, x = e.ax + (e.bx - e.ax) * t, z = e.az + (e.bz - e.az) * t;
                        if (Covered(floors, x + e.nx * 2, z + e.nz * 2)) continue;
                        bool near = false;
                        foreach (var q in pts) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < minGap * minGap) { near = true; break; }
                        if (!near) pts.Add(new Vector3((float)x, (float)z, (float)top));
                    }
                }
            }
            return pts;
        }

        static bool Covered(List<Box> floors, double x, double z)
        {
            foreach (var f in floors) if (System.Math.Abs(x - f.x) < f.w / 2 && System.Math.Abs(z - f.z) < f.d / 2) return true;
            return false;
        }
    }
}
