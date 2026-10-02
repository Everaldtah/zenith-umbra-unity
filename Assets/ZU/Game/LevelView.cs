// Greybox view of a map straight from its data (the boxes the simulation collides with, ramps, floors, render-only
// decor, prop stand-ins, the objective) plus its sun, ambient light and fog. The prototype view: the detailed Unity maps
// (terrain, modular buildings, skyline) replace it map by map.
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game
{
    public class LevelView : MonoBehaviour
    {
        static readonly Dictionary<string, Color> MAT = new Dictionary<string, Color>
        {
            ["wall"] = new Color(0.78f, 0.74f, 0.68f), ["trim"] = new Color(0.36f, 0.30f, 0.27f), ["ground"] = new Color(0.47f, 0.47f, 0.45f),
            ["glass"] = new Color(0.55f, 0.75f, 0.85f, 0.6f), ["accent"] = new Color(0.75f, 0.22f, 0.20f), ["roof"] = new Color(0.30f, 0.33f, 0.40f),
            ["window"] = new Color(1f, 0.85f, 0.55f), ["wood"] = new Color(0.52f, 0.36f, 0.24f), ["rock"] = new Color(0.50f, 0.47f, 0.43f), ["paint"] = new Color(0.86f, 0.80f, 0.62f),
        };
        static Shader lit;
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        Material Mat(string kind, MapDef map)
        {
            kind ??= "wall";
            if (mats.TryGetValue(kind, out var m)) return m;
            lit ??= Shader.Find("Universal Render Pipeline/Lit");
            var c = MAT.TryGetValue(kind, out var col) ? col : MAT["wall"];
            if (kind == "accent") c = Conv.Hex(map.tint, c);
            m = new Material(lit) { name = "zu_" + kind };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", kind == "glass" ? 0.9f : kind == "ground" ? 0.15f : 0.3f);
            if (kind == "window") { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.6f); }
            mats[kind] = m;
            return m;
        }

        public static LevelView Build(MapDef map, Transform parent)
        {
            var go = new GameObject("Level " + map.id);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<LevelView>();
            v.Make(map);
            return v;
        }

        void Make(MapDef map)
        {
            var solid = new GameObject("Solid").transform; solid.SetParent(transform, false);
            var deco = new GameObject("Decor").transform; deco.SetParent(transform, false);
            foreach (var b in map.floors ?? new List<Box>()) Block(b, solid, map, "ground");
            foreach (var b in map.boxes ?? new List<Box>()) Block(b, solid, map, null);
            foreach (var b in map.decor ?? new List<Box>()) Block(b, deco, map, null, collider: false);
            var props = new GameObject("Props").transform; props.SetParent(transform, false);
            foreach (var p in map.props ?? new List<Prop>()) PropStandIn(p, props, map);
            // the objective: the capture point ring (Control) - the payload route is drawn by the payload view
            if (map.objective != "push" && map.point != null && map.point.Length == 3)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(ring.GetComponent<Collider>());
                ring.name = "Point"; ring.transform.SetParent(transform, false);
                ring.transform.localPosition = Conv.U(map.point[0], map.point[1] + 0.02, map.point[2]);
                ring.transform.localScale = new Vector3(12, 0.02f, 12);
                var m = new Material(Mat("accent", map)); m.SetColor("_BaseColor", Conv.Hex(map.tint, Color.cyan) * 0.8f);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Conv.Hex(map.tint, Color.cyan) * 0.6f);
                ring.GetComponent<Renderer>().sharedMaterial = m;
            }
            Lighting(map);
        }

        void Block(Box b, Transform parent, MapDef map, string forceMat, bool collider = true)
        {
            double y0 = b.y ?? 0;
            GameObject go;
            if (b.ramp != null) go = Ramp(b);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localPosition = Conv.U(b.x, y0 + b.h / 2, b.z);
                go.transform.localScale = new Vector3((float)b.w, (float)b.h, (float)b.d);
            }
            go.name = (forceMat ?? b.mat ?? "wall") + (b.ramp != null ? " ramp" : "");
            go.transform.SetParent(parent, false);
            if (!collider) { var c = go.GetComponent<Collider>(); if (c) Destroy(c); }
            go.GetComponent<Renderer>().sharedMaterial = Mat(forceMat ?? b.mat, map);
            go.isStatic = true;
        }

        /// <summary>a wedge: the box's footprint, its top rising from y0 to y0 + h along the ramp direction</summary>
        static GameObject Ramp(Box b)
        {
            var go = new GameObject();
            double y0 = b.y ?? 0, hx = b.w / 2, hz = b.d / 2;
            // corners in sim space, then converted (mirrored X)
            V3[] c = {
                new V3(b.x - hx, y0, b.z - hz), new V3(b.x + hx, y0, b.z - hz), new V3(b.x + hx, y0, b.z + hz), new V3(b.x - hx, y0, b.z + hz),
            };
            double Top(V3 p) => BoxLevel.Top(b, p.x, p.z) ?? y0 + b.h;
            var bottom = new Vector3[4]; var top = new Vector3[4];
            for (int i = 0; i < 4; i++) { bottom[i] = Conv.U(c[i]); top[i] = Conv.U(c[i].x, Top(c[i]), c[i].z); }
            var verts = new List<Vector3>(); var tris = new List<int>();
            var mid = Vector3.zero; for (int i = 0; i < 4; i++) mid += (bottom[i] + top[i]) / 8;
            // one face per quad, wound to face away from the wedge's centre (the X mirror flips handedness, so the winding
            // is decided by geometry, not by the corner order). Faces of their own vertices: hard edges, true normals.
            void Quad(Vector3 a, Vector3 bb, Vector3 cc, Vector3 d)
            {
                var n = Vector3.Cross(bb - a, cc - a) + Vector3.Cross(cc - a, d - a);
                if (n.sqrMagnitude < 1e-10f) return;                         // the zero-height end of a ramp
                bool outward = Vector3.Dot(n, (a + bb + cc + d) / 4 - mid) > 0;
                var q = new[] { a, bb, cc, d };
                void Tri(int i, int j, int l)
                {
                    if (Vector3.Cross(q[j] - q[i], q[l] - q[i]).sqrMagnitude < 1e-10f) return;   // a ramp's side is a triangle
                    int k = verts.Count; verts.Add(q[i]);
                    if (outward) { verts.Add(q[j]); verts.Add(q[l]); } else { verts.Add(q[l]); verts.Add(q[j]); }
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                }
                Tri(0, 1, 2); Tri(0, 2, 3);
            }
            Quad(top[0], top[1], top[2], top[3]);
            Quad(bottom[0], bottom[3], bottom[2], bottom[1]);
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Quad(bottom[i], bottom[j], top[j], top[i]); }
            var mesh = new Mesh { name = "ramp" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        void PropStandIn(Prop p, Transform parent, MapDef map)
        {
            double s = p.s ?? 2, r = p.solid ?? s * 0.25;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = p.id; go.transform.SetParent(parent, false);
            double y = p.y ?? 0;
            go.transform.localPosition = Conv.U(p.x, y + s * 0.45, p.z);
            go.transform.localRotation = Conv.Yaw(p.rot ?? 0);
            go.transform.localScale = new Vector3((float)(r * 2), (float)(s * 0.45), (float)(r * 2));
            go.GetComponent<Renderer>().sharedMaterial = Mat("wood", map);
        }

        static void Lighting(MapDef map)
        {
            // (no `??` on Unity objects: a missing component is a "fake null" that ?? treats as present)
            var sunGo = GameObject.Find("ZU Sun"); if (sunGo == null) sunGo = new GameObject("ZU Sun");
            var sun = sunGo.GetComponent<Light>(); if (sun == null) sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft;
            if (map.sun != null)
            {
                sun.color = Conv.Hex(map.sun.color, Color.white);
                sun.intensity = (float)map.sun.intensity;
                if (map.sun.dir != null && map.sun.dir.Length == 3)
                {
                    // the sim gives the direction toward the sun: light travels the other way
                    var d = Conv.U(map.sun.dir[0], map.sun.dir[1], map.sun.dir[2]).normalized;
                    sunGo.transform.rotation = Quaternion.LookRotation(-d);
                }
            }
            RenderSettings.sun = sun;
            if (map.ambient != null && map.ambient.Length == 3)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                float k = System.Convert.ToSingle(map.ambient[2]);
                RenderSettings.ambientSkyColor = Conv.Hex(map.ambient[0] as string) * k;
                RenderSettings.ambientEquatorColor = Color.Lerp(Conv.Hex(map.ambient[0] as string), Conv.Hex(map.ambient[1] as string), 0.5f) * k;
                RenderSettings.ambientGroundColor = Conv.Hex(map.ambient[1] as string) * k;
            }
            if (map.fog != null && map.fog.Length == 3)
            {
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Conv.Hex(map.fog[0] as string, Color.gray);
                RenderSettings.fogStartDistance = System.Convert.ToSingle(map.fog[1]);
                RenderSettings.fogEndDistance = System.Convert.ToSingle(map.fog[2]);
            }
        }
    }
}
