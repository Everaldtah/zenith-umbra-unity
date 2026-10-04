// Tripo-generated environment pieces (evera-fb drives Tripo; manifest copied to Resources/ZUData/tripo_env.json by the
// import): `vista` set pieces for the world past the walls (mesas and canyon walls, a mountain castle, a pagoda hill, a
// factory skyline, an observatory peak, floating temple islands), `rock` formations that dress the arena's rock boxes,
// `building` / `wall` models (the skyline's buildings; the arena's box-built structures as shells, tripo_arena.json) and
// `dressing` (grass / rock beds, bushes, rubble: GroundDressing). Each id is a prop prefab (Resources/ZUProps/<id>, 1 m
// tall, on y = 0, centred - the props pipeline). Nothing here until the manifests and the prefabs are in.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ZU.Game.Env
{
    public static class TripoEnv
    {
        public sealed class Piece
        {
            public string id, style, kind;          // kind: vista | rock | building | wall | dressing
            public string archetype, biome;         // building / wall: townhouse, storefront, hall, hall1f, pavilion, pagoda, tower,
                                                    // watertower, chimney, tank, block, wall; dressing: temperate | desert | urban | gothic | alpine
            public float footprint_m, height_m, w_m, d_m;
            public bool tall;                       // dressing over ~0.35 m: wall bases and map edges only (nothing collides with it)
            public string maps;                     // vista: the maps it stands round ("gulch", "cloudstep, amatsu, campaign");
                                                    // empty = every map of its style
            public float yaw;                       // degrees that turn the model's front to +Z (Tripo puts fronts anywhere)
            [JsonIgnore] public GameObject prefab;
            [JsonIgnore] Vector3? unit;
            /// <summary>the prefab's own bounds size at scale 1 (1 m tall, footprint as Tripo made it), measured once</summary>
            [JsonIgnore] public Vector3 Unit => unit ??= Measure(prefab);
        }

        /// <summary>a box-built arena structure wearing a Tripo shell (Resources/ZUData/tripo_arena.json, written by the cut
        /// stage from maps.json). A solid cluster's boxes stop being drawn (they stay the simulation's and the camera's
        /// colliders) under a shell fitted to them; a walk-in one keeps its boxes drawn - they are its interior and door jambs -
        /// inside a door-cut shell fitted by its walls, and only its outer decor (windows, trims) goes.</summary>
        public sealed class Shell
        {
            public string id, @base;                // the cut variant's prop id, the plain model it was cut from
            public double[] c, size;                // TS coordinates: the cluster's foot centre x, y, z; its extents w, h, d
            public int turns;                       // quarter turns about Y when fitted to c / size
            public double grow = 1;                 // fitted: the cluster's bounds times this
            public double[] pos; public double s;   // exact placement from the cut stage (Unity map coords, uniform scale =
                                                    // the cut mesh's real height): wins over the fit when given
            public bool walkin;
            public int[] boxes, decor;              // indices into map.boxes / map.decor no longer drawn
            [JsonIgnore] public GameObject prefab;
        }

        static List<Piece> all;
        /// <summary>the manifests are read again on every play (the statics outlive a play session when the Editor skips the
        /// domain reload)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCaches() { all = null; shells = null; }
        public static List<Piece> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Piece>();
                var t = Resources.Load<TextAsset>("ZUData/tripo_env");
                if (t == null) return all;
                try
                {
                    // the manifest is a list, or { "pieces": [...] }
                    var tok = Newtonsoft.Json.Linq.JToken.Parse(t.text);
                    var arr = tok is Newtonsoft.Json.Linq.JArray a ? a : tok["pieces"] as Newtonsoft.Json.Linq.JArray;
                    // entry by entry: one bad entry is skipped with a warning, not the whole manifest (a "maps" written as a
                    // list instead of a string once emptied every Tripo piece, 2026-10-04 - a list is accepted now)
                    if (arr != null)
                        foreach (var e in arr)
                            try
                            {
                                if (e is Newtonsoft.Json.Linq.JObject o && o["maps"] is Newtonsoft.Json.Linq.JArray ml) o["maps"] = string.Join(", ", ml.Select(x => (string)x));
                                var p = e.ToObject<Piece>();
                                if (p != null) all.Add(p);
                            }
                            catch (System.Exception ex) { Debug.LogWarning($"[ZU] tripo_env entry {e["id"]} skipped: {ex.Message}"); }
                }
                catch (System.Exception e) { Debug.LogWarning("[ZU] tripo_env.json unreadable: " + e.Message); }
                foreach (var p in all) p.prefab = Resources.Load<GameObject>("ZUProps/" + p.id);
                all = all.Where(p => p.prefab != null && p.height_m > 0).ToList();
                return all;
            }
        }

        /// <summary>the vistas for an outer-world style ("west", "japan", "industry", "observatory", "sky", "academy")</summary>
        public static List<Piece> Vistas(string style, string mapId) => All.Where(p => p.kind == "vista" && ForMap(p, style, mapId)).ToList();

        /// <summary>a vista's maps list names this map ("campaign" = every c1_ .. c5_ level), or it has none and the style matches</summary>
        static bool ForMap(Piece p, string style, string mapId)
        {
            if (string.IsNullOrWhiteSpace(p.maps)) return string.Equals(p.style, style, System.StringComparison.OrdinalIgnoreCase);
            bool campaign = mapId.Length > 2 && mapId[0] == 'c' && char.IsDigit(mapId[1]) && mapId[2] == '_';
            foreach (var t in p.maps.Split(','))
            {
                var m = t.Trim();
                if (m == mapId || (campaign && m == "campaign")) return true;
            }
            return false;
        }
        public static List<Piece> Rocks => All.Where(p => p.kind == "rock").ToList();
        /// <summary>the ground dressing of a biome (GroundDressing scatters it)</summary>
        public static List<Piece> Dressing(string biome) => All.Where(p => p.kind == "dressing" && string.Equals(p.biome, biome, System.StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>a building of a style + archetype for the skyline, the one whose real height is nearest `wantH` (a seeded
        /// pick among near-equals); null when the style has none, so the procedural building stays</summary>
        public static Piece Building(string style, string archetype, float wantH, System.Random rng)
        {
            Piece best = null; float bd = float.MaxValue;
            foreach (var p in All)
            {
                if (p.kind != "building" || p.archetype != archetype || !string.Equals(p.style, style, System.StringComparison.OrdinalIgnoreCase)) continue;
                float d = Mathf.Abs(p.height_m - wantH) * (0.8f + (float)rng.NextDouble() * 0.4f);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        static Dictionary<string, List<Shell>> shells;
        /// <summary>the Tripo shells of a map's box-built structures whose prefabs are in (a walk-in cluster needs its cut
        /// variant; a solid one may fall back to the plain model)</summary>
        public static List<Shell> Shells(string mapId)
        {
            if (shells == null)
            {
                shells = new Dictionary<string, List<Shell>>();
                var t = Resources.Load<TextAsset>("ZUData/tripo_arena");
                if (t != null)
                    try { shells = JsonConvert.DeserializeObject<Dictionary<string, List<Shell>>>(t.text) ?? shells; }
                    catch (System.Exception e) { Debug.LogWarning("[ZU] tripo_arena.json unreadable: " + e.Message); }
                foreach (var list in shells.Values)
                    foreach (var s in list)
                    {
                        s.prefab = Resources.Load<GameObject>("ZUProps/" + s.id);
                        // a solid block may wear the plain model; a walk-in one never does (it would wall up the doors)
                        if (s.prefab == null && !s.walkin && !string.IsNullOrEmpty(s.@base)) { s.prefab = Resources.Load<GameObject>("ZUProps/" + s.@base); s.pos = null; }
                    }
            }
            return shells.TryGetValue(mapId, out var l)
                ? l.Where(s => s.prefab != null && (s.pos?.Length == 3 && s.s > 0 || s.c?.Length == 3 && s.size?.Length == 3)).ToList()
                : new List<Shell>();
        }

        /// <summary>the bounds size of a prefab's renderers at scale 1, from its meshes (no instance needed)</summary>
        static Vector3 Measure(GameObject prefab)
        {
            if (prefab == null) return Vector3.one;
            var root = prefab.transform.worldToLocalMatrix;
            bool any = false; var b = new Bounds();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = root * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? new Vector3(Mathf.Max(0.01f, b.size.x), Mathf.Max(0.01f, b.size.y), Mathf.Max(0.01f, b.size.z)) : Vector3.one;
        }

        /// <summary>a piece fitted inside a footprint (uniform scale: as large as fits w x d and hMax, never stretched), standing
        /// at `foot` turned by yaw - the skyline's buildings, each in the plot the procedural one would have had</summary>
        public static GameObject FitInside(Piece p, Transform parent, Vector3 foot, float yawDeg, float w, float d, float hMax)
        {
            var u = p.Unit;
            if (Mathf.Abs(Mathf.DeltaAngle(p.yaw, 90)) < 45 || Mathf.Abs(Mathf.DeltaAngle(p.yaw, -90)) < 45) u = new Vector3(u.z, u.y, u.x);   // turned a quarter: its footprint swaps
            float s = Mathf.Min(w / u.x, d / u.z, hMax / u.y);
            var go = Object.Instantiate(p.prefab, parent, false);
            go.name = p.id;
            go.transform.localPosition = foot;
            go.transform.localRotation = Quaternion.Euler(0, yawDeg + p.yaw, 0);
            go.transform.localScale = Vector3.one * s;
            return go;
        }

        /// <summary>a piece placed: the prefab at height h (uniform scale), turned by yaw (degrees), at pos</summary>
        public static GameObject Place(Piece p, Transform parent, Vector3 pos, float yawDeg, float height)
        {
            var go = Object.Instantiate(p.prefab, parent, false);
            go.name = p.id;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yawDeg + p.yaw, 0);
            go.transform.localScale = Vector3.one * height;
            return go;
        }

        /// <summary>a rock formation fitted around a box (centre, size in Unity metres): its bounds scaled to the box times
        /// `grow`, so the box's edges hide inside it; yaw in 90-degree steps</summary>
        public static GameObject FitRock(Piece p, Transform parent, Vector3 center, Vector3 size, int quarterTurns, float grow = 1.06f)
            => Fit(p.prefab, p.Unit, p.id + " (rock)", parent, center, size, quarterTurns, grow);

        /// <summary>a prefab stretched to a box (centre, size in Unity metres) times `grow`, standing on the box's foot; its own
        /// scale-1 extents `unit` (1 m tall, footprint as made); yaw in 90-degree steps</summary>
        public static GameObject Fit(GameObject prefab, Vector3 unit, string name, Transform parent, Vector3 center, Vector3 size, int quarterTurns, float grow = 1f)
        {
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            go.transform.localRotation = Quaternion.Euler(0, quarterTurns * 90, 0);
            bool turned = (quarterTurns & 1) != 0;
            float sx = (turned ? size.z : size.x) * grow / Mathf.Max(0.01f, unit.x);
            float sz = (turned ? size.x : size.z) * grow / Mathf.Max(0.01f, unit.z);
            float sy = size.y * grow / Mathf.Max(0.01f, unit.y);
            go.transform.localScale = new Vector3(sx, sy, sz);
            go.transform.localPosition = new Vector3(center.x, center.y - size.y / 2 - size.y * (grow - 1) * 0.5f, center.z);
            return go;
        }

        /// <summary>a shell's scale-1 extents (its prefab measured like a piece's)</summary>
        public static Vector3 Unit(Shell s) => Measure(s.prefab);
    }
}
