// Enra's Hellfire Chains (TS render/ChainBlades.ts, ported; after Kratos' Blades of Chaos,
// docs/research/kratos_blades_study.md): the blade itself is a held prop in each fist (HeldRig, prop_enra_chainblade);
// this file is everything that makes it a CHAIN blade:
//
//  - the chain: black iron links (every fourth an ember) from the bracer on the forearm to the ring at the pommel. One
//    instanced draw per chain, the links laid along a curve each frame - a slack loop hanging beside the hand at rest,
//    paid out behind the blade when it flies: on a light swing the blade leaves the fist and whips round him at the
//    chain's full length (the sim's 5 m sweep), on the Chain Throw it shoots straight out to 7.5 m, spins once, snaps
//    taut and is yanked back (the timelines below are the sim's numbers, shared with the animator and first person)
//  - the yoke: Kratos' chains are fused to his forearms; Enra's run on past the bracers, up the arms and across his
//    shoulders, so the two blades are one chain end to end (the user's ask: the swords connected)
//  - the fire: the blades burn on every attack - an additive flame sheet along each edge that licks and flickers, the
//    blade's own glow driven up, and a ribbon trail of ember-orange the blade's tip leaves behind (the path of the cut)
//
// Frame: everything works in the hero rig root's space (the TS's gun root = model space), where HeldRig lays the blades.
// The maths is geometric (lengths, curves, frames from up = +Y and back = -Z), the same in both handednesses, so it runs
// in Unity's model space directly. Drawing: Chain.Draw puts the links through the root's matrix.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZU.Game.Fx
{
    public static class ChainBlades
    {
        // ---------------------------------------------------------------- timelines (the frame counts of the study, sections 2 and 4)
        /// <summary>a light swing: 0.15 s wind-up, 0.25 s arc, recover to 0.62 s (1.6 swings a second); the throw 0.25 s out, a beat
        /// taut, 0.3 s back</summary>
        public const float CB_WIND = 0.15f, CB_ARC = 0.25f, CB_SWING = 0.62f, CB_THROW = 0.6f;
        /// <summary>metres the blade reaches from the shoulder on a full swing / the throw (the sim's primary and secondary ranges)</summary>
        public const float CB_REACH = 5, CB_THROW_REACH = 7.5f;
        /// <summary>the throw's phases: the arm winds back, the blade flies out, holds taut, comes back</summary>
        public const float CB_THROW_OUT = 0.08f, CB_THROW_HIT = 0.25f, CB_THROW_BACK = 0.34f;

        static float Ez(float u) { u = Mathf.Min(1, Mathf.Max(0, u)); return u * u * (3 - 2 * u); }

        /// <summary>how far out on its chain the blade is during a light swing, 0 (in the fist) .. 1 (the chain's full length):
        /// it leaves the hand as the arc starts, is at full stretch for the middle of the arc, and is hauled back in the recover</summary>
        public static float SwingExt(float t)
        {
            if (t < 0) return 0;
            if (t < CB_WIND) return 0.12f * Ez(t / CB_WIND);
            if (t < CB_WIND + CB_ARC) { float u = (t - CB_WIND) / CB_ARC; return 0.12f + 0.88f * Ez(u / 0.45f); }
            if (t < CB_SWING) return 1 - Ez((t - CB_WIND - CB_ARC) / (CB_SWING - CB_WIND - CB_ARC));
            return 0;
        }

        /// <summary>the arc's progress 0..1 (0 = wound back on its own side, 1 = across the body), eased; stays at 1 through the recover</summary>
        public static float SwingArc(float t)
        {
            if (t < CB_WIND) return 0;
            if (t < CB_WIND + CB_ARC) return Ez((t - CB_WIND) / CB_ARC);
            return 1;
        }

        /// <summary>the blade's bearing round the swinger (radians about the vertical, 0 = straight ahead, + = the character's left):
        /// it leads the hand by a little on the way across (a blade on a chain runs ahead of the wrist that whips it)</summary>
        public static float SwingPhi(float t, float side)
        {
            float phi0 = side * 1.9f, phi1 = -side * 1.25f, a = SwingArc(t);
            float lead = t < CB_WIND + CB_ARC ? -side * 0.3f * Mathf.Sin(a * Mathf.PI) : 0;
            if (t >= CB_WIND + CB_ARC) { float u = Ez((t - CB_WIND - CB_ARC) / (CB_SWING - CB_WIND - CB_ARC)); return phi1 + (-side * 0.5f - phi1) * u; }
            return phi0 + (phi1 - phi0) * a + lead;
        }

        /// <summary>the Chain Throw: how far out the blade is on its chain, 0..1</summary>
        public static float ThrowExt(float t)
        {
            if (t < CB_THROW_OUT) return 0;
            if (t < CB_THROW_HIT) return Ez((t - CB_THROW_OUT) / (CB_THROW_HIT - CB_THROW_OUT));
            if (t < CB_THROW_BACK) return 1;
            if (t < CB_THROW) return 1 - Ez((t - CB_THROW_BACK) / (CB_THROW - CB_THROW_BACK));
            return 0;
        }

        /// <summary>turns of the blade about the chain's side axis: one spin on the way out, point-first into the target, one back</summary>
        public static float ThrowSpin(float t)
        {
            if (t < CB_THROW_OUT) return 0;
            if (t < CB_THROW_HIT) return (t - CB_THROW_OUT) / (CB_THROW_HIT - CB_THROW_OUT);
            if (t < CB_THROW_BACK) return 1;
            if (t < CB_THROW) return 1 + (t - CB_THROW_BACK) / (CB_THROW - CB_THROW_BACK);
            return 0;
        }

        /// <summary>how much the blade burns, 0..1: flares on as the attack starts, holds while the blade is moving, dies in the recover</summary>
        public static float FireK(string kind, float age)
        {
            if (age < 0) return 0;
            if (kind == "primary" && age < CB_SWING) return age < 0.06f ? age / 0.06f : age < CB_WIND + CB_ARC + 0.08f ? 1 : Mathf.Max(0, 1 - (age - CB_WIND - CB_ARC - 0.08f) / 0.16f);
            if (kind == "secondary" && age < CB_THROW) return age < 0.06f ? age / 0.06f : age < CB_THROW - 0.12f ? 1 : Mathf.Max(0, (CB_THROW - age) / 0.12f);
            return 0;
        }

        /// <summary>the third-person chain extension of each hand (the TS Animator's chainExt): the swinging hand alternates with
        /// the sweep side (attackSide > 0: the right) on a light swing; the throw is the right blade's</summary>
        public static void Ext(ZU.Sim.Actor a, double t, float[] ext)
        {
            ext[0] = ext[1] = 0;
            float age = (float)(t - a.anim.attackAt);
            if (a.anim.attackKind == "primary" && age < CB_SWING) ext[a.anim.attackSide > 0 ? 1 : 0] = SwingExt(age);
            else if (a.anim.attackKind == "secondary" && age < CB_THROW) ext[1] = ThrowExt(age);
        }

        // ---------------------------------------------------------------- the chain
        /// <summary>iron: the black links; ember: every fourth link, glowing (its own batch: its emission is driven with the fire)</summary>
        public sealed class Chain
        {
            public InstancedBatch iron, ember; public int n; public float pitch; public Vector3? prev; public Vector3 sag; public bool visible = true;
            public Material emberMat;
            /// <summary>the links as laid (LayChain already put them through the rig root's matrix)</summary>
            public void Draw()
            {
                if (!visible) return;
                iron.Draw(); ember.Draw();
            }
        }

        static Material ironMat;
        static readonly Quaternion QY = Quaternion.AngleAxis(90, Vector3.up), QX = Quaternion.AngleAxis(90, Vector3.right);
        static readonly Color WHITE = Color.white;

        /// <summary>a chain of up to `n` links sized for a rig `L` model units tall (Enra's links about 7 cm long on a 2.05 m oni):
        /// one instanced draw, the links rolled alternately so each ring threads the next; every fourth link glows an ember</summary>
        public static Chain BuildChain(float L, int n = 160)
        {
            // the ring's plane holds the chain: a torus lies in XY (hole on Z) - turned so its long axis runs along local Z
            var b = new AbilityKit.MeshBuilder();
            b.Append(AbilityKit.Torus(0.0165f * L, 0.0048f * L, 6, 12), Matrix4x4.Rotate(QY));
            var geo = b.Build("chain link");
            if (ironMat == null)
            {
                ironMat = AbilityKit.Lit(); ironMat.name = "chain iron";
                ironMat.SetColor("_BaseColor", Sp.Hex("#2a2529")); ironMat.SetFloat("_Metalness", 0.88f); ironMat.SetFloat("_Roughness", 0.4f);
            }
            var em = AbilityKit.Lit(); em.name = "chain ember";
            em.SetColor("_BaseColor", Sp.Hex("#3a1a10")); em.SetFloat("_Metalness", 0.6f); em.SetFloat("_Roughness", 0.5f);
            var c = new Chain
            {
                iron = new InstancedBatch(geo, ironMat, n) { shadows = ShadowCastingMode.On, receiveShadows = true },
                ember = new InstancedBatch(geo, em, (n + 3) / 4) { shadows = ShadowCastingMode.On, receiveShadows = true },
                n = n, pitch = 0.0235f * L, emberMat = em,
            };
            SetChainHeat(c, 0);
            return c;
        }

        /// <summary>the chain's own glow (the ember links) driven up with the fire</summary>
        public static void SetChainHeat(Chain c, float heat) => c.emberMat.SetVector("_EmissionColor", Sp.Lin(Sp.Hex("#ff5a1f")) * (1.6f + 3 * heat));

        static readonly List<float> segLen = new List<float>();
        /// <summary>
        /// Lay the links along a polyline of sample points (the root's frame), one link every `pitch` of arc length; links the
        /// curve is too short for are not drawn. Returns how many were laid.
        /// </summary>
        public static int LayChain(Chain c, List<Vector3> pts, Transform root)
        {
            c.iron.Clear(); c.ember.Clear();
            int n = c.n, laid = 0, seg = 0; float into = 0;
            segLen.Clear();
            for (int i = 1; i < pts.Count; i++) segLen.Add(Vector3.Distance(pts[i], pts[i - 1]));
            float total = 0; foreach (var s in segLen) total += s;
            int count = Mathf.Min(n, Mathf.Max(1, Mathf.FloorToInt(total / c.pitch)));
            var R = root.localToWorldMatrix;
            // centre the run on the curve so neither end leaves a gap
            float d = (total - (count - 1) * c.pitch) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                while (seg < segLen.Count - 1 && into + segLen[seg] < d) { d -= segLen[seg]; seg++; }
                float u = segLen[seg] > 1e-9f ? Mathf.Min(1, Mathf.Max(0, d / segLen[seg])) : 0;
                var p = Vector3.LerpUnclamped(pts[seg], pts[seg + 1], u);
                var t = pts[seg + 1] - pts[seg];
                if (t.sqrMagnitude < 1e-10f) t = Vector3.forward;
                t.Normalize();
                var q = Quaternion.FromToRotation(Vector3.forward, t);
                if (i % 2 == 1) q *= QX;
                var m = R * Matrix4x4.TRS(p, q, Vector3.one);
                if (i % 4 == 3) c.ember.Add(m, WHITE); else c.iron.Add(m, WHITE);
                laid++;
                d += c.pitch;
            }
            return laid;
        }

        /// <summary>
        /// A chain from the bracer (`from`) to the pommel (`to`) with `restLen` of chain to spend (model units): slack hangs in a
        /// loop below the chord and trails the pommel's motion by a beat (flung out behind a swing); a chord longer than the
        /// chain is a taut line with the barest bow against the motion. Fills and returns the sampled curve.
        /// </summary>
        public static List<Vector3> SlackCurve(Chain c, Vector3 from, Vector3 to, float restLen, float dt, List<Vector3> output)
        {
            float dist = Vector3.Distance(from, to);
            // the sag: how much of a hanging loop the slack makes (a parabola of arc length s on a chord d dips ~ sqrt(s^2 - d^2) / 2.3)
            float slack = Mathf.Max(0, restLen - dist), drop = Mathf.Sqrt(Mathf.Max(0, restLen * restLen - dist * dist)) / 2.3f;
            // trailing: the loop swept back against the pommel's velocity, more when there's slack to swing
            if (c.prev.HasValue && dt > 0)
            {
                var want = (to - c.prev.Value) / dt * (-0.05f * (0.25f + 0.75f * Mathf.Min(1, slack / Mathf.Max(1e-6f, restLen))));
                float maxLen = Mathf.Max(0.03f * restLen, drop * 1.4f);
                if (want.magnitude > maxLen) want = want.normalized * maxLen;
                c.sag = Vector3.Lerp(c.sag, want, Mathf.Min(1, dt * 16));
            }
            else c.sag = Vector3.zero;
            c.prev = to;
            var mid = (from + to) * 0.5f + c.sag;
            mid.y -= drop;
            // a quadratic bezier from the bracer through the sag to the pommel
            const int N = 32;
            output.Clear();
            for (int i = 0; i <= N; i++)
            {
                float u = (float)i / N, v = 1 - u;
                output.Add(from * (v * v) + mid * (2 * u * v) + to * (u * u));
            }
            return output;
        }

        /// <summary>the yoke across the shoulders: a smooth run through the given waypoints (bracer, elbow, shoulder, nape, ...) -
        /// three's CatmullRomCurve3(way, false, 'centripetal').getPoint, sampled at 49 points</summary>
        public static List<Vector3> YokeCurve(List<Vector3> way, List<Vector3> output)
        {
            const int N = 48;
            output.Clear();
            for (int i = 0; i <= N; i++) output.Add(CatmullRom(way, (float)i / N));
            return output;
        }

        /// <summary>three.js CatmullRomCurve3.getPoint for an open centripetal curve (its uniform-in-segments parameter)</summary>
        static Vector3 CatmullRom(List<Vector3> pts, float t)
        {
            int l = pts.Count;
            float p = (l - 1) * t;
            int ip = Mathf.FloorToInt(p); float w = p - ip;
            if (w == 0 && ip == l - 1) { ip = l - 2; w = 1; }
            var p0 = ip > 0 ? pts[ip - 1] : pts[0] - pts[1] + pts[0];
            var p1 = pts[ip % l]; var p2 = pts[(ip + 1) % l];
            var p3 = ip + 2 < l ? pts[(ip + 2) % l] : pts[l - 1] - pts[l - 2] + pts[l - 1];
            float dt0 = Mathf.Pow((p1 - p0).sqrMagnitude, 0.25f), dt1 = Mathf.Pow((p2 - p1).sqrMagnitude, 0.25f), dt2 = Mathf.Pow((p3 - p2).sqrMagnitude, 0.25f);
            if (dt1 < 1e-4f) dt1 = 1;
            if (dt0 < 1e-4f) dt0 = dt1;
            if (dt2 < 1e-4f) dt2 = dt1;
            return new Vector3(Poly(p0.x, p1.x, p2.x, p3.x, dt0, dt1, dt2, w), Poly(p0.y, p1.y, p2.y, p3.y, dt0, dt1, dt2, w), Poly(p0.z, p1.z, p2.z, p3.z, dt0, dt1, dt2, w));
        }
        /// <summary>three's CubicPoly.initNonuniformCatmullRom + calc</summary>
        static float Poly(float x0, float x1, float x2, float x3, float dt0, float dt1, float dt2, float t)
        {
            float t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
            float t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;
            t1 *= dt1; t2 *= dt1;
            float c0 = x1, c1 = t1, c2 = -3 * x1 + 3 * x2 - 2 * t1 - t2, c3 = 2 * x1 - 2 * x2 + t1 + t2;
            float tt = t * t;
            return c0 + c1 * t + c2 * tt + c3 * tt * t;
        }

        // ---------------------------------------------------------------- the fire
        public sealed class Flame { public Transform group; public Material[] mats; public Transform[] sheets; public float k; }

        static Texture2D flameTex;
        static Texture2D FireTexture()
        {
            if (flameTex != null) return flameTex;
            const int w = 64, h = 256;
            flameTex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "chain fire", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            // tongues of flame: bright at the root, ragged and fading toward the tip; a few sine-stacked licks so it reads as fire
            // and not a glow bar
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w, v = (float)y / h;
                    float lick = 0.55f + 0.45f * Mathf.Sin(u * 23 + v * 9) * Mathf.Sin(u * 11 - v * 17) * Mathf.Sin(u * 41 + 2);
                    float core = Mathf.Max(0, 1 - v * 1.1f), edge = Mathf.Pow(Mathf.Max(0, 1 - Mathf.Abs(u - 0.5f) * 2), 0.6f);
                    float a = Mathf.Min(1, core * core * 1.4f * lick * edge + Mathf.Max(0, 0.5f - v) * 0.4f);
                    // TS-PARITY: the TS uploads this DataTexture with flipY = true, and WebGL flips array uploads too, so its data
                    // row 0 lands at the TOP of the texture (v = 1, the sheet's outer edge) - not where its comment puts it.
                    // Rows are flipped the same way here; check against a side-by-side capture.
                    int row = h - 1 - y;
                    px[row * w + x] = new Color32(255, (byte)Mathf.RoundToInt(120 + 135 * core * core), (byte)Mathf.RoundToInt(30 + 90 * core * core * core), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255));
                }
            flameTex.SetPixels32(px); flameTex.Apply(true);
            return flameTex;
        }

        static readonly float[] SHEET_ALPHA = { 0.9f, 0.7f, 0.7f, 0.55f };
        /// <summary>flames along a blade `len` long (its +Z): two crossed sheets off the edge plus a hotter core sheet, additive</summary>
        public static Flame BuildFlame(float L, float len, Transform parent)
        {
            var tex = FireTexture();
            var group = new GameObject("chain blade fire").transform; group.SetParent(parent, false);
            var mats = new List<Material>(); var sheets = new List<Transform>();
            void Mk(float h, float rot, float alpha, float z0)
            {
                var b = new AbilityKit.MeshBuilder();
                b.Append(AbilityKit.Plane(len * 0.72f, h, 8, 1), Matrix4x4.Translate(new Vector3(0, h * 0.4f, z0 + len * 0.36f)) * Matrix4x4.Rotate(Quaternion.AngleAxis(-90, Vector3.up)));
                var mat = AbilityKit.Additive(); mat.SetTexture("_BaseMap", tex);
                var c = Sp.Hex("#ffb070"); c.a = alpha; mat.SetColor("_BaseColor", c);
                var go = new GameObject("sheet"); go.transform.SetParent(group, false);
                go.transform.localRotation = Quaternion.AngleAxis(rot * Mathf.Rad2Deg, Vector3.forward);
                go.AddComponent<MeshFilter>().sharedMesh = b.Build("fire sheet");
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mats.Add(mat); sheets.Add(go.transform);
            }
            Mk(0.16f * L, 0, 0.9f, 0.02f * L); Mk(0.13f * L, Mathf.PI / 2, 0.7f, 0.03f * L); Mk(0.11f * L, -Mathf.PI / 2, 0.7f, 0.01f * L); Mk(0.09f * L, Mathf.PI, 0.55f, 0.02f * L);
            group.gameObject.SetActive(false);
            return new Flame { group = group, mats = mats.ToArray(), sheets = sheets.ToArray(), k = 0 };
        }

        /// <summary>burn at strength `k` (0 = out): the sheets scroll, flicker and swell with it</summary>
        public static void UpdateFlame(Flame f, float k, float dt, float time)
        {
            f.k += (k - f.k) * Mathf.Min(1, dt * 18);
            bool on = f.k > 0.02f;
            if (f.group.gameObject.activeSelf != on) f.group.gameObject.SetActive(on);
            if (!on) return;
            for (int i = 0; i < f.mats.Length; i++)
            {
                var m = f.mats[i]; float fl = 0.78f + 0.22f * Mathf.Sin(time * 37 + i * 2.1f) * Mathf.Sin(time * 23 + i);
                m.SetTextureOffset("_BaseMap", new Vector2((time * 1.7f) % 1, 0));
                var c = m.GetColor("_BaseColor"); c.a = f.k * fl * SHEET_ALPHA[i]; m.SetColor("_BaseColor", c);
                f.sheets[i].localScale = new Vector3(1, 0.55f + 0.6f * f.k * fl, 1);
            }
        }

        // ---------------------------------------------------------------- the trail
        public sealed class Trail
        {
            public GameObject go; public Mesh mesh; public int max; public float life;
            public readonly List<(Vector3 a, Vector3 b, float t)> samples = new List<(Vector3, Vector3, float)>();
            public Vector3[] pos; public Color[] col; public readonly List<int> idx = new List<int>();
        }

        /// <summary>a ribbon the blade's tip leaves (the swing's path, Kratos' ember arc): the last `max` pommel-tip pairs, brightest
        /// at the blade and black (gone, additive) `life` seconds back - in the rig root's frame (it travels with him)</summary>
        public static Trail BuildTrail(Transform root, int max = 28, float life = 0.28f)
        {
            var tr = new Trail { max = max, life = life, pos = new Vector3[max * 2], col = new Color[max * 2] };
            tr.mesh = new Mesh { name = "chain blade trail" }; tr.mesh.MarkDynamic();
            tr.mesh.vertices = tr.pos; tr.mesh.colors = tr.col;
            tr.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100);
            tr.go = new GameObject("chain blade trail"); tr.go.transform.SetParent(root, false);
            tr.go.AddComponent<MeshFilter>().sharedMesh = tr.mesh;
            var mr = tr.go.AddComponent<MeshRenderer>();
            var mat = AbilityKit.Additive(); mat.SetFloat("_VertexColors", 1); mat.SetColor("_BaseColor", new Color(1, 1, 1, 0.6f));
            mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            tr.go.SetActive(false);
            return tr;
        }

        static readonly Color EMBER_HOT = Sp.Lin(Sp.Hex("#ffd27a")), EMBER_COOL = Sp.Lin(Sp.Hex("#ff3a0a"));
        /// <summary>push this frame's blade (pommel `a` -> tip `b`) when `on`, age the ribbon, rebuild it</summary>
        public static void UpdateTrail(Trail tr, Vector3? a, Vector3? b, bool on, float now)
        {
            if (on && a.HasValue && b.HasValue)
            {
                bool fresh = tr.samples.Count == 0 || (tr.samples[tr.samples.Count - 1].b - b.Value).sqrMagnitude > 1e-6f;
                if (fresh) { tr.samples.Add((a.Value, b.Value, now)); if (tr.samples.Count > tr.max) tr.samples.RemoveAt(0); }
            }
            while (tr.samples.Count > 0 && now - tr.samples[0].t > tr.life) tr.samples.RemoveAt(0);
            int n = tr.samples.Count;
            if (tr.go.activeSelf != n >= 2) tr.go.SetActive(n >= 2);
            if (n < 2) return;
            for (int i = 0; i < tr.max; i++)
            {
                // bright at the blade, dark at the tail: by age, and by place along the ribbon (a whip crosses metres in a few
                // frames, so by age alone the whole sweep would read as one slab)
                int j = Mathf.Min(i, n - 1); var s = tr.samples[j];
                float k = Mathf.Max(0, 1 - (now - s.t) / tr.life) * Mathf.Min(1, (float)(j + 1) / n);
                tr.pos[i * 2] = s.a; tr.pos[i * 2 + 1] = s.b;
                var c = Color.Lerp(EMBER_COOL, EMBER_HOT, k * k) * (k * k);
                tr.col[i * 2] = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 1); tr.col[i * 2 + 1] = new Color(c.r, c.g, c.b, 1);
            }
            tr.idx.Clear();
            for (int i = 0; i < n - 1; i++) { int q = i * 2; tr.idx.Add(q); tr.idx.Add(q + 1); tr.idx.Add(q + 2); tr.idx.Add(q + 1); tr.idx.Add(q + 3); tr.idx.Add(q + 2); }
            tr.mesh.vertices = tr.pos; tr.mesh.colors = tr.col;
            tr.mesh.SetTriangles(tr.idx, 0, false);
        }

        // ---------------------------------------------------------------- the blade's own glow
        /// <summary>a renderer's material driven hot: its own emission remembered so it can be restored</summary>
        public sealed class Heat { public Material m; public Color e; public bool keyword; }

        /// <summary>every material on the blade's renderers, instanced onto this blade (the two blades share one prop's materials,
        /// and one blade burning must not light the other), each with its own glow remembered</summary>
        public static List<Heat> HeatMaterials(Renderer[] rends)
        {
            var output = new List<Heat>();
            if (rends == null) return output;
            foreach (var r in rends)
            {
                if (r == null) continue;
                foreach (var m in r.materials)          // (.materials: this renderer's own instances)
                    if (m != null && m.HasProperty("_EmissionColor"))
                        output.Add(new Heat { m = m, e = m.GetColor("_EmissionColor"), keyword = m.IsKeywordEnabled("_EMISSION") });
            }
            return output;
        }

        static readonly Color HEAT = Sp.Hex("#ff4a12");
        /// <summary>the TS emissive.lerp(HEAT, 0.8 k), intensity + 3.5 k</summary>
        public static void SetHeat(List<Heat> mats, float k)
        {
            foreach (var x in mats)
            {
                if (k <= 0) { x.m.SetColor("_EmissionColor", x.e); if (!x.keyword) x.m.DisableKeyword("_EMISSION"); continue; }
                x.m.EnableKeyword("_EMISSION");
                x.m.SetColor("_EmissionColor", Color.Lerp(x.e, HEAT, Mathf.Min(1, k * 0.8f)) * (1 + 3.5f * k));
            }
        }
    }
}
