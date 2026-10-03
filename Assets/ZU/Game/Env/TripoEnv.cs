// Tripo-generated landscape pieces (system32-91 drives Tripo; manifest Assets/ZU/Env/Tripo/tripo_env.json, copied to
// Resources/ZUData/tripo_env.json by the import): `vista` set pieces for the world past the walls (mesas and canyon
// walls, a mountain castle, a pagoda hill, a factory skyline, an observatory peak, floating temple islands) and `rock`
// formations that dress the arena's rock boxes. Each id is a prop prefab (Resources/ZUProps/<id>, 1 m tall, on y = 0,
// centred - the props pipeline). Nothing here until the manifest and the prefabs are in.
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
            public string id, style, kind;          // kind: vista | rock
            public float footprint_m, height_m;
            [JsonIgnore] public GameObject prefab;
        }

        static List<Piece> all;
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
                    if (arr != null) all = arr.ToObject<List<Piece>>();
                }
                catch (System.Exception e) { Debug.LogWarning("[ZU] tripo_env.json unreadable: " + e.Message); }
                foreach (var p in all) p.prefab = Resources.Load<GameObject>("ZUProps/" + p.id);
                all = all.Where(p => p.prefab != null && p.height_m > 0).ToList();
                return all;
            }
        }

        /// <summary>the vistas for an outer-world style ("west", "japan", "industry", "observatory", "sky", "academy")</summary>
        public static List<Piece> Vistas(string style) => All.Where(p => p.kind == "vista" && string.Equals(p.style, style, System.StringComparison.OrdinalIgnoreCase)).ToList();
        public static List<Piece> Rocks => All.Where(p => p.kind == "rock").ToList();

        /// <summary>a piece placed: the prefab at height h (uniform scale), turned by yaw (degrees), at pos</summary>
        public static GameObject Place(Piece p, Transform parent, Vector3 pos, float yawDeg, float height)
        {
            var go = Object.Instantiate(p.prefab, parent, false);
            go.name = p.id;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yawDeg, 0);
            go.transform.localScale = Vector3.one * height;
            return go;
        }

        /// <summary>a rock formation fitted around a box (centre, size in Unity metres): its bounds scaled to the box times
        /// `grow`, so the box's edges hide inside it; yaw in 90-degree steps</summary>
        public static GameObject FitRock(Piece p, Transform parent, Vector3 center, Vector3 size, int quarterTurns, float grow = 1.06f)
        {
            var go = Object.Instantiate(p.prefab, parent, false);
            go.name = p.id + " (rock)";
            go.transform.localRotation = Quaternion.Euler(0, quarterTurns * 90, 0);
            go.transform.localScale = Vector3.one;
            go.transform.localPosition = Vector3.zero;
            // its own extents at scale 1 (the prefab is 1 m tall, its footprint whatever Tripo made)
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return go;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var lb = new Bounds(go.transform.InverseTransformPoint(b.center), go.transform.InverseTransformVector(b.size));
            Vector3 ext = new Vector3(Mathf.Abs(lb.size.x), Mathf.Abs(lb.size.y), Mathf.Abs(lb.size.z));
            bool turned = quarterTurns % 2 != 0;
            float sx = (turned ? size.z : size.x) * grow / Mathf.Max(0.01f, ext.x);
            float sz = (turned ? size.x : size.z) * grow / Mathf.Max(0.01f, ext.z);
            float sy = size.y * grow / Mathf.Max(0.01f, ext.y);
            go.transform.localScale = new Vector3(sx, sy, sz);
            // standing on the box's foot, centred on it
            go.transform.localPosition = new Vector3(center.x, center.y - size.y / 2 - size.y * (grow - 1) * 0.5f, center.z);
            return go;
        }
    }
}
