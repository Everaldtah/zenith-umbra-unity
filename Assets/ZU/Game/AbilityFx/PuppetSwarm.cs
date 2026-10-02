// Hex's puppet army, drawn as one swarm (TS render/PuppetSwarm.ts, ported): a single instanced mesh for every puppet on
// the field (fifty skinned characters would cost fifty rigs), moved the way marionettes move - hanging from their strings
// a hand above the floor, swaying, leaning into the run, lunging on a claw swipe, rising out of the floor in a ripple,
// dropping in a heap when the strings go slack. Two violet strings run up from each of them; a ring of light marks where
// one is rising.
//
// The model is the Tripo puppet (Resources/ZUProps/prop_hex_puppet) when it is there, a stand-in built here otherwise.
// The maths runs in sim space as in the TS (see Sp); each instance crosses into Unity in its matrix.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class PuppetSwarm
    {
        public const string PUPPET_MODEL = "prop_hex_puppet";
        const int MAX = 128;
        static readonly float H = (float)Puppets.PUPPET_DEF.height;
        /// <summary>the model's own facing, turned to the game's (+z forward)</summary>
        const float MODEL_YAW = 0;
        const float HOVER = 0.14f, FALL_SECS = 0.7f, GONE_AFTER = 3.2f;

        readonly List<InstancedBatch> body = new List<InstancedBatch>();
        readonly InstancedBatch strings, rings;
        readonly List<Material> mats = new List<Material>();
        public bool real;

        /// <summary>the stand-in: a masked figure in a tailcoat, from a few solids (vertex colours, faceted like the TS's)</summary>
        static Mesh StandIn()
        {
            var b = new AbilityKit.MeshBuilder();
            void Add(AbilityKit.MeshBuilder g, string col, float x, float y, float z, float rx = 0, float rz = 0, Vector3? scale = null)
            {
                var xf = Matrix4x4.Translate(new Vector3(x, y, z)) * Matrix4x4.Rotate(Quaternion.AngleAxis(rz * Mathf.Rad2Deg, Vector3.forward))
                       * Matrix4x4.Rotate(Quaternion.AngleAxis(rx * Mathf.Rad2Deg, Vector3.right)) * Matrix4x4.Scale(scale ?? Vector3.one);
                b.Append(g, xf, Sp.Lin(Sp.Hex(col)));
            }
            Add(AbilityKit.Sphere(0.17f, 12, 10), "#f4f1ea", 0, 1.56f, 0.02f, scale: new Vector3(0.9f, 1.15f, 0.95f));      // the mask
            Add(AbilityKit.Cone(0.24f, 0.22f, 10, true), "#7a3fd0", 0, 1.36f, 0, Mathf.PI);                                  // the collar
            Add(AbilityKit.Cylinder(0.2f, 0.17f, 0.62f, 10), "#15131a", 0, 1.06f, 0);                                         // the coat
            Add(AbilityKit.Box(0.16f, 0.5f, 0.05f), "#f4f1ea", 0, 1.08f, 0.16f);                                              // the waistcoat
            Add(AbilityKit.Cone(0.26f, 0.7f, 10, true), "#15131a", 0, 0.62f, -0.05f, Mathf.PI);                               // the tails
            foreach (int s in new[] { -1, 1 })
            {
                Add(AbilityKit.Cylinder(0.055f, 0.045f, 0.42f, 8), "#15131a", s * 0.3f, 1.22f, 0.12f, -1.0f, s * 0.5f);     // arm, raised
                Add(AbilityKit.Sphere(0.09f, 8, 6), "#7a3fd0", s * 0.4f, 1.4f, 0.3f);                                         // the glove
                Add(AbilityKit.Cylinder(0.07f, 0.05f, 0.75f, 8), "#15131a", s * 0.1f, 0.4f, 0);                               // the leg
                Add(AbilityKit.Sphere(0.06f, 8, 6), "#d8c3a5", s * 0.1f, 0.42f, 0.02f);                                       // the knee joint
            }
            // three: toNonIndexed + computeVertexNormals = one normal a face
            var f = new AbilityKit.MeshBuilder();
            for (int i = 0; i < b.t.Count; i += 3)
            {
                int a = b.t[i], c = b.t[i + 1], d = b.t[i + 2];
                var nrm = Vector3.Cross(b.v[d] - b.v[c], b.v[a] - b.v[c]).normalized;
                int k = f.Count;
                f.Add(b.v[a], nrm, Vector2.zero, b.c[a]); f.Add(b.v[c], nrm, Vector2.zero, b.c[c]); f.Add(b.v[d], nrm, Vector2.zero, b.c[d]);
                f.Tri(k, k + 1, k + 2);
            }
            return f.Build("hex puppet (stand-in)");
        }

        public PuppetSwarm()
        {
            // two strings a puppet, fading upward
            var sg = AbilityKit.Cylinder(0.012f, 0.012f, 1, 4, true);
            for (int i = 0; i < sg.Count; i++) { var p = sg.v[i]; p.y += 0.5f; sg.v[i] = p; float k = 1 - p.y; sg.c[i] = new Color(k * k, k * k, k * k, 1); }
            var sm = AbilityKit.Additive(); sm.SetFloat("_VertexColors", 1); sm.SetColor("_BaseColor", new Color(1, 1, 1, 0.85f));
            var rg = AbilityKit.Ring(0.35f, 0.5f, 24);
            var rm = AbilityKit.Additive(); rm.SetColor("_BaseColor", new Color(1, 1, 1, 0.9f));
            mats.Add(sm); mats.Add(rm);
            strings = new InstancedBatch(sg.Build("puppet string"), sm, MAX * 2);
            rings = new InstancedBatch(rg.Build("puppet ring"), rm, MAX) { pre = Matrix4x4.Rotate(Quaternion.AngleAxis(-90, Vector3.right)) };
            if (!Adopt())
            {
                var m = AbilityKit.Lit(); m.SetFloat("_VertexColors", 1); m.SetFloat("_Roughness", 0.7f); m.SetFloat("_Metalness", 0.05f);
                mats.Add(m);
                body.Add(new InstancedBatch(StandIn(), m, MAX) { shadows = ShadowCastingMode.On, receiveShadows = true });
            }
        }

        /// <summary>the published puppet: its meshes, scaled to a puppet's height, feet on the floor</summary>
        bool Adopt()
        {
            var p = PropParts.Load(PUPPET_MODEL);
            if (p == null) return false;
            var yaw = Matrix4x4.Rotate(Quaternion.AngleAxis(MODEL_YAW * Mathf.Rad2Deg, Vector3.up));
            p.Normalise(yaw);
            // (bounds after the yaw: MODEL_YAW is 0, so the prefab's own)
            var b = p.bounds; float k = H / (b.size.y > 0 ? b.size.y : 1);
            p.Normalise(Matrix4x4.Scale(Vector3.one * k) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z)));
            foreach (var part in p.parts)
            {
                // uniform surface values: generated metal / roughness maps are noise (as on the heroes)
                var m = AbilityKit.Lit();
                var map = AbilityKit.MapOf(part.source) ?? p.map;
                if (map != null) m.SetTexture("_BaseMap", map);
                m.SetFloat("_Roughness", 0.75f); m.SetFloat("_Metalness", 0.05f);
                mats.Add(m);
                body.Add(new InstancedBatch(part.mesh, m, MAX) { pre = part.pre, shadows = ShadowCastingMode.On, receiveShadows = true });
            }
            return real = true;
        }

        static readonly Color WHITE = Sp.Lin(Sp.Hex("#ffffff")), PINK = Sp.Lin(Sp.Hex("#ffd0d0")), ALLY = Sp.Lin(Sp.Hex("#b98cff")), FOE = Sp.Lin(Sp.Hex("#ff6a8a"));

        /// <summary>sees: whether the viewer can see this puppet (stealth)</summary>
        public void Update(World w, float time, string viewerTeam, Func<Actor, bool> sees)
        {
            foreach (var b in body) b.Clear();
            strings.Clear(); rings.Clear();
            int n = 0;
            foreach (var a in w.actors)
            {
                if (!a.IsSummon || a.def.id != "puppet" || n >= MAX) continue;
                float fell = a.alive ? -1 : time - (float)a.Sv("fellAt", a.deathAt);
                if (!a.alive && (fell > GONE_AFTER || a.deathAt < -50)) continue;
                if (a.alive && !sees(a)) continue;
                float id = a.id * 1.37f;
                float riseK = a.alive && a.sv.ContainsKey("riseUntil") && time < a.Sv("riseUntil")
                    ? Mathf.Max(0, (time - (float)a.Sv("riseAt")) / Mathf.Max(0.01f, (float)(a.Sv("riseUntil") - a.Sv("riseAt")))) : 1;
                float speed = Mathf.Sqrt((float)(a.vel.x * a.vel.x + a.vel.z * a.vel.z));
                float atk = time - (float)a.anim.attackAt, lunge = atk >= 0 && atk < 0.32f ? Mathf.Sin(atk / 0.32f * Mathf.PI) : 0;
                float hit = time - (float)a.anim.hitAt, flinch = hit >= 0 && hit < 0.2f ? 1 - hit / 0.2f : 0;
                float y = (float)a.pos.y, pitch = 0, roll = 0, yaw = (float)a.yaw, sy = 1;
                if (a.alive)
                {
                    float ease = 1 - (1 - riseK) * (1 - riseK);
                    y += -H * (1 - ease) + HOVER * ease + Mathf.Sin(time * 3.1f + id) * 0.05f * ease;
                    pitch = Mathf.Min(0.32f, speed * 0.05f) + lunge * 0.55f - flinch * 0.35f;
                    roll = Mathf.Sin(time * 2.3f + id) * 0.07f + Mathf.Sin(time * 9 + id) * 0.02f * Mathf.Min(1, speed / 3);
                    yaw += Mathf.Sin(time * 1.7f + id) * 0.08f;
                }
                else
                {
                    // the strings go slack: knees first, then the whole figure folds forward into a heap and sinks away
                    float k = Mathf.Min(1, fell / FALL_SECS), kk = k * k;
                    pitch = kk * 1.45f; roll = Mathf.Sin(id) * 0.5f * kk; sy = 1 - 0.25f * kk;
                    y += HOVER * (1 - kk) + 0.12f * kk - Mathf.Max(0, fell - (GONE_AFTER - 0.8f)) * 0.6f;
                }
                float fx = Mathf.Sin((float)a.yaw), fz = Mathf.Cos((float)a.yaw), sc = (float)a.scale;
                var p = new Vector3((float)a.pos.x + fx * lunge * 0.35f, y, (float)a.pos.z + fz * lunge * 0.35f);
                var m = Sp.TRS(p, Sp.EulerYXZ(pitch, yaw, roll), new Vector3(sc, sc * sy, sc));
                // enemies' puppets read a shade redder
                foreach (var b in body) b.Add(m, a.team == viewerTeam ? WHITE : PINK);
                n++;
                if (a.alive)
                {
                    var col = a.team == viewerTeam ? ALLY : FOE; float len = 5 + Mathf.Sin(id) * 1.2f;
                    foreach (float sx in new[] { -0.22f, 0.22f })
                    {
                        // from the shoulders up, leaning a little with the run
                        var sp = new Vector3((float)a.pos.x - fz * sx * sc, y + H * 0.82f * sc, (float)a.pos.z + fx * sx * sc);
                        strings.Add(Sp.TRS(sp, Sp.EulerYXZ(-pitch * 0.4f, yaw, roll * 0.5f + sx * 0.06f), new Vector3(1, len * riseK, 1)), col);
                    }
                    if (riseK < 1)
                    {
                        float r = 0.6f + riseK * 1.2f;
                        rings.Add(Sp.TRS(new Vector3((float)a.pos.x, (float)a.pos.y + 0.04f, (float)a.pos.z), Quaternion.identity, new Vector3(r, 1, r)), col * (1 - riseK));
                    }
                }
            }
            foreach (var b in body) b.Draw();
            strings.Draw(); rings.Draw();
        }

        public void Dispose() { foreach (var m in mats) UnityEngine.Object.Destroy(m); }
    }
}
