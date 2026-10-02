// Hand-held weapons (port of HeldProps.ts + the guns CharacterView / TomoeProps / Hammer place for the other heroes):
// Raijin's katana, Enra's chain blades and bracers, Hayate's nodachi (with the shuriken in hand while it's sheathed),
// the archers' bows and nocked arrows, Kaien's talisman, Tomoe's scattergun / Fang / great axe, Tenkai-Oh's hammer,
// Gantetsu's twin chainguns, Haruto's sidearm. The Tripo props are fitted at import (HeldImport: Resources/ZUProps/
// held_<id>.prefab, unit length in the grip frame), so here they are only scaled by size x model height and attached.
//
// Frame (the TS animator's gun frame): origin in the fist (the hand bone, dropped 1.8% of the height), +Z along the
// forearm toward the aim, +Y up across it. A blade leaves the fist along +Z pitched up by `pitch`; a bow stands across
// it on Y, grip in the fist; the hammer / axe hang from the pommel with the haft up +Y and the striking face on +X.
// HeldRig sits on a hero root (third person: HeroView; first person: the viewmodel) and places the props from the real
// bones each frame, after the Animator.
using System.Collections.Generic;
using UnityEngine;
using ZU.Game.FirstPerson;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game
{
    public enum HeldKind { Blade, Bow, Arrow, Card, Gun, Hammer, Axe }

    public class HeldItem
    {
        public string id, alt; public HeldKind kind; public float size; public float pitch; public bool flip;
        /// <summary>the fitted prop's centre, relative to the fist (x model height)</summary>
        public Vector3 at;
        public Color color, glow;
        public HeldItem(string id, HeldKind kind, float size, string color, string glow, float pitch = 0, string alt = null, bool flip = false)
        { this.id = id; this.kind = kind; this.size = size; this.color = Conv.Hex(color); this.glow = Conv.Hex(glow); this.pitch = pitch; this.alt = alt; this.flip = flip; }
    }

    /// <summary>chains: a blade on a chain in each fist (Enra); bracer: a vambrace prop worn on each forearm;
    /// prop: a two-handed weapon on the hammer frame (haft up +Y from the pommel) - Tenkai-Oh's hammer, Tomoe's axe</summary>
    public class HeldSpec { public HeldItem L, R, prop; public bool chains, backProp; public string bracer; }

    public static class Held
    {
        public static readonly Dictionary<string, HeldSpec> TABLE = new Dictionary<string, HeldSpec>
        {
            { "raijin", new HeldSpec { R = new HeldItem("prop_raijin_katana", HeldKind.Blade, 0.56f, "#cfd6e2", "#7fc8ff", -0.55f) } },
            // Enra's Hellfire Chains: a broad forward-curved hooked cleaver in EACH fist, 0.75 m on a 2.05 m oni, its pommel a
            // ring the chain runs from; chain-wrapped bracers on the forearms. flip: the fit's bulk heuristic puts the grip forward
            { "enra", new HeldSpec { chains = true, bracer = "prop_enra_bracer",
                L = new HeldItem("prop_enra_chainblade", HeldKind.Blade, 0.37f, "#3a2a2c", "#ff5a1f", -0.2f, "prop_enra_blade", true),
                R = new HeldItem("prop_enra_chainblade", HeldKind.Blade, 0.37f, "#3a2a2c", "#ff5a1f", -0.2f, "prop_enra_blade", true) } },
            { "hayate", new HeldSpec { R = new HeldItem("prop_hayate_nodachi", HeldKind.Blade, 0.62f, "#e8efe9", "#4fe3c1", -0.5f) } },
            // the archers also hold the next arrow in the string hand (nocked, drawn to the jaw, loosed, then a new one from the quiver)
            { "yuzu", new HeldSpec { L = new HeldItem("prop_yuzu_bow", HeldKind.Bow, 0.72f, "#f2c14e", "#ffd76a"), R = new HeldItem("prop_yuzu_arrow", HeldKind.Arrow, 0.4f, "#f7e2a8", "#ffb347") } },
            { "seiran", new HeldSpec { L = new HeldItem("prop_seiran_bow", HeldKind.Bow, 0.86f, "#1d2433", "#6fa8ff"), R = new HeldItem("prop_seiran_arrow", HeldKind.Arrow, 0.42f, "#dfe6f0", "#6fa8ff") } },
            // Kaien's ofuda: a paper talisman pinched between the index and middle fingers of the throwing hand
            { "kaien", new HeldSpec { R = new HeldItem("prop_kaien_talisman", HeldKind.Card, 0.1f, "#f2e6c4", "#ffcf5a") } },
            // Tomoe: the Crownfire Scattergun on the right forearm, the Crescent Fang in the left fist, the great axe slung on her
            // back (TomoeProps: the gun fitted 0.4 H long centred 2.5% up / 5.8% forward of the fist; the knife 0.26 H, its grip
            // the back third so the fist closes on the middle of it)
            { "tomoe", new HeldSpec { L = new HeldItem("prop_tomoe_blade", HeldKind.Blade, 0.26f, "#f3f1ec", "#5ff2e0") { at = new Vector3(0, 0, 0.0875f) },
                R = new HeldItem("prop_tomoe_shotgun", HeldKind.Gun, 0.4f, "#f3f1ec", "#5ff2e0") { at = new Vector3(0, 0.0252f, 0.0576f) },
                prop = new HeldItem("prop_tomoe_axe", HeldKind.Axe, 0.6324f, "#f3f1ec", "#5ff2e0") { at = new Vector3(0, -0.02f, 0) }, backProp = true } },
            // Tenkai-Oh's Dawnbreaker: a touch longer than the TS procedural haft (Reinhardt-sized): 0.62 H x 1.08
            { "tenkai", new HeldSpec { prop = new HeldItem("prop_tenkai_hammer", HeldKind.Hammer, 0.6696f, "#eef1f6", "#ffd76a") } },
            // Gantetsu's rotary chainguns (Hinoko left, Hanabi right): the second generation when the build has it, 0.46 H long,
            // the grip a quarter of the way along from the back (baked into the held_ prefab)
            { "gantetsu", new HeldSpec { L = new HeldItem("prop_gantetsu_hinoko_v2", HeldKind.Gun, 0.46f, "#1a1a1a", "#4fe3c1", 0, "prop_gantetsu_hinoko"),
                R = new HeldItem("prop_gantetsu_hanabi_v2", HeldKind.Gun, 0.46f, "#1a1a1a", "#ffb347", 0, "prop_gantetsu_hanabi") } },
            // Haruto's Sunspark sidearm (Hammer.ts buildBlaster, procedural): barrel along the forearm, 6% of the height past the
            // wrist, a little below it, at 1.5x
            { "haruto", new HeldSpec { R = new HeldItem("blaster", HeldKind.Gun, 1.5f, "#eef1f6", "#ffd76a") { at = new Vector3(0, -0.017f, 0.06f) } } },
        };

        /// <summary>after a shot the string hand is empty until it brings the next arrow from the quiver (seconds after the shot)</summary>
        public static readonly Vector2 ARROW_GONE = new Vector2(0.03f, 0.42f);
        /// <summary>after a throw the talisman hand is empty until the next card is drawn from the sleeve</summary>
        public static readonly Vector2 CARD_GONE = new Vector2(0.04f, 0.24f);
        /// <summary>Hayate's koi-scale shuriken: its radius as a fraction of his height</summary>
        public const float SHURIKEN_R = 0.085f;

        /// <summary>Hayate keeps the nodachi sheathed on his back except while he deflects, dashes, cuts or runs the Dragon Gate</summary>
        public static bool Visible(string heroId, int side, Actor a, double t)
        {
            if (heroId != "hayate" || side != 1) return true;
            var c = a.anim; double cast = t - c.castAt, atk = t - c.attackAt;
            return a.Has("dragonblade", t) || a.Has("parry", t) || a.Has("deflect", t) || a.Has("phased", t) && c.castId == "dragongate"
                || c.castId == "currentdash" && cast < 0.45 || c.castId == "dragongate" && cast < 1.4
                || (c.attackKind == "punch" || c.attackKind == "secondary" && a.Has("phased", t)) && atk < 0.5;
        }

        public static HeldSpec For(string heroId) => TABLE.TryGetValue(heroId, out var s) ? s : null;

        /// <summary>Tomoe's great axe is out of its sling while she swings it (Crescent Reaping, Tide of Blades)</summary>
        public const double REAP_SECS = 0.75, REAP_STOP = 0.05;
        public static bool AxeOut(Actor a, double t) => a.def.id == "tomoe" && ((a.forced != null && a.forced.kind == "tide") || (a.anim.castId == "reaping" && t - a.anim.castAt < REAP_SECS + REAP_STOP));
    }

    public class HeldRig : MonoBehaviour
    {
        public class Slot
        {
            public HeldItem item; public Transform node, body, swap; public Renderer[] rends, swapRends;
            public bool upright, arrow, card, hidden;
        }
        public readonly Slot[] slots = new Slot[2];       // [left hand, right hand]
        /// <summary>the two-handed prop on the hammer frame (pommel at the origin, haft +Y) and its haft length (m)</summary>
        public Transform prop; public float hammerLen; Renderer[] propRends; bool propShown = true;
        /// <summary>the same weapon slung across the back (Tomoe's axe)</summary>
        Transform back; Renderer[] backRends;
        readonly Transform[] bracers = new Transform[2];
        public RigPose rig; public HeldSpec spec; string heroId;
        /// <summary>the viewmodel's liberties: props scaled, barrels converged on a point ahead, bows canted / tilted</summary>
        public float gunScale = 1; public Vector3? gunAim; public float bowCant, bowTilt;
        /// <summary>a prop taken out of the hand (Enra's blade on its chain): model-space position, +Z, +Y and blend weight</summary>
        public (Vector3 p, Vector3 z, Vector3 y, float w)?[] orbit = new (Vector3, Vector3, Vector3, float)?[2];
        public bool[] gunHide = new bool[2];
        bool shown = true;

        public static HeldRig Attach(GameObject root, RigPose rig, HeroDef def)
        {
            var spec = Held.For(def.id);
            if (spec == null || !rig.ok) return null;
            var h = root.AddComponent<HeldRig>();
            h.rig = rig; h.spec = spec; h.heroId = def.id;
            float H = rig.height;
            h.slots[0] = h.Make(spec.L, H, "held_L"); h.slots[1] = h.Make(spec.R, H, "held_R");
            if (spec.prop != null)
            {
                var ps = h.Make(spec.prop, H, "held_prop");
                h.prop = ps.node; h.propRends = ps.rends; h.hammerLen = 0.62f * H;
                if (spec.backProp) { var bs = h.Make(spec.prop, H, "held_back"); h.back = bs.node; h.backRends = bs.rends; h.back.localScale = Vector3.one * 0.85f; }
            }
            if (spec.bracer != null)
                for (int i = 0; i < 2; i++)
                {
                    var g = new GameObject("bracer_" + (i == 0 ? "L" : "R")).transform; g.SetParent(rig.root, false);
                    var m = Load(spec.bracer, null); if (m != null) { m.transform.SetParent(g, false); m.transform.localScale = Vector3.one * 0.17f * H; }
                    h.bracers[i] = g;
                }
            return h;
        }

        /// <summary>a fitted Tripo prop (HeldImport's Resources/ZUProps/held_&lt;id&gt;), or its alt, or null when neither is in the build</summary>
        public static GameObject Load(string id, string alt)
        {
            var pf = Resources.Load<GameObject>("ZUProps/held_" + id);
            if (pf == null && alt != null) pf = Resources.Load<GameObject>("ZUProps/held_" + alt);
            return pf != null ? Instantiate(pf) : null;
        }

        /// <summary>a slot's hierarchy: node (the gun frame) -> body (the blade pitch) -> the fitted prop scaled to size x H</summary>
        Slot Make(HeldItem it, float H, string name)
        {
            var s = new Slot { item = it };
            s.node = new GameObject(name).transform; s.node.SetParent(rig.root, false);
            if (it == null) return s;
            s.body = new GameObject("body").transform; s.body.SetParent(s.node, false);
            float len = it.size * H;
            GameObject m = it.id == "blaster" ? ProcProps.Blaster(H, it) : it.kind == HeldKind.Arrow ? ProcProps.Arrow(len, H, it) : Load(it.id, it.alt);
            if (m == null) m = it.kind == HeldKind.Card ? ProcProps.Card(len, H, it) : ProcProps.Blade(len, H, it);   // (no prop published yet: a procedural stand-in)
            var fit = m.transform; fit.SetParent(s.body, false);
            bool proc = fit.name.StartsWith("proc_");
            switch (it.kind)
            {
                case HeldKind.Blade:
                    // the fit lays the long axis on +Z and centres it; the blade slides forward so the grip sits in the fist
                    if (!proc) { fit.localScale = Vector3.one * len; fit.localRotation = it.flip ? Quaternion.Euler(0, 180, 0) : Quaternion.identity; fit.localPosition = new Vector3(0, 0, 0.38f * len) + it.at * H; }
                    s.body.localRotation = Quaternion.AngleAxis(it.pitch * Mathf.Rad2Deg, Vector3.right);
                    if (it.id == "prop_hayate_nodachi")
                    {
                        // Hayate's shuriken in his throwing hand whenever the nodachi is on his back: held edge-on between the
                        // fingers, the flat facing across the forearm, its centre just past the fingertips
                        var sh = Load("prop_hayate_shuriken_v2", "prop_hayate_shuriken") ?? ProcProps.Shuriken(H, it);
                        s.swap = new GameObject("swap").transform; s.swap.SetParent(s.node, false);
                        sh.transform.SetParent(s.swap, false); sh.transform.localScale = Vector3.one * 2 * Held.SHURIKEN_R * H;
                        s.swap.localRotation = Quaternion.Euler(0, 0, 90); s.swap.localPosition = new Vector3(0, 0.012f * H, 0.035f * H + Held.SHURIKEN_R * H * 0.9f);
                        s.swapRends = sh.GetComponentsInChildren<Renderer>(true);
                    }
                    break;
                case HeldKind.Card:
                    // fitted like a blade, rolled flat so its face is up (the TS's +90 about Z, mirrored), out past the fingertips
                    if (!proc) { fit.localScale = Vector3.one * len; fit.localPosition = new Vector3(0, 0, 0.5f * len); s.body.localRotation = Quaternion.Euler(0, 0, -90); s.body.localPosition = new Vector3(0, 0.02f * H, 0); }
                    s.card = true; break;
                case HeldKind.Bow:
                    // stood up across the forearm (HeldImport's bow fit: limbs on Y, belly forward) with its middle in the fist
                    if (!proc) fit.localScale = Vector3.one * len;
                    s.upright = true; break;
                case HeldKind.Arrow: s.arrow = true; break;
                case HeldKind.Gun:
                    if (!proc) { fit.localScale = Vector3.one * len; fit.localPosition = it.at * H; }
                    break;
                case HeldKind.Hammer: case HeldKind.Axe:
                    // the haft up +Y from the pommel at the origin, the head's striking face on +X
                    fit.localScale = Vector3.one * len; fit.localPosition = it.at * H;
                    break;
            }
            s.rends = s.body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in s.rends) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return s;
        }

        /// <summary>every prop renderer (the owner hides them with the hero, or moves them to the viewmodel layer)</summary>
        public IEnumerable<Renderer> Renderers()
        {
            foreach (var s in slots) if (s != null) { if (s.rends != null) foreach (var r in s.rends) yield return r; if (s.swapRends != null) foreach (var r in s.swapRends) yield return r; }
            if (propRends != null) foreach (var r in propRends) yield return r;
            if (backRends != null) foreach (var r in backRends) yield return r;
            foreach (var b in bracers) if (b != null) foreach (var r in b.GetComponentsInChildren<Renderer>(true)) yield return r;
        }

        /// <summary>gameplay -> which props show this frame (arrow / card gone after a shot, the nodachi sheathed, the axe out)</summary>
        public void UpdateState(Actor a, double t, bool show)
        {
            shown = show;
            var c = a.anim; double since = t - c.attackAt; bool shot = c.attackKind == "primary" || c.attackKind == "secondary";
            bool axe = Held.AxeOut(a, t);
            for (int i = 0; i < 2; i++)
            {
                var s = slots[i]; if (s?.item == null) continue;
                bool hide = gunHide[i];
                if (s.arrow) hide |= shot && since > Held.ARROW_GONE.x && since < Held.ARROW_GONE.y;
                else if (s.card) hide |= shot && since > Held.CARD_GONE.x && since < Held.CARD_GONE.y;
                else
                {
                    bool vis = Held.Visible(heroId, i, a, t);
                    if (s.swap != null) { Show(s.rends, show && vis); Show(s.swapRends, show && !vis); s.hidden = !show; continue; }
                    hide |= !vis;
                }
                if (heroId == "tomoe") hide |= axe || (i == 0 && a.Sv("fang", 0) > 0);
                s.hidden = hide || !show;
                Show(s.rends, !s.hidden);
            }
            if (prop != null) { propShown = show && (heroId != "tomoe" || axe); Show(propRends, propShown && propPlaced); }
            if (back != null) Show(backRends, show && !axe);
            foreach (var b in bracers) if (b != null) foreach (var r in b.GetComponentsInChildren<Renderer>(true)) r.enabled = show;
        }
        static void Show(Renderer[] rs, bool on) { if (rs == null) return; foreach (var r in rs) if (r != null) r.enabled = on; }

        bool propPlaced;
        /// <summary>the two-handed prop along a grip (model space): the pommel 10% of the haft behind `pos`, the haft along `dir`,
        /// the striking face toward `side`</summary>
        public void PlaceProp(Vector3 pos, Vector3 dir, Vector3 side)
        {
            if (prop == null) return;
            var H = dir.normalized; var T = (side - H * Vector3.Dot(side, H)).normalized;
            prop.localPosition = pos - H * 0.1f * hammerLen;
            prop.localRotation = Quaternion.LookRotation(Vector3.Cross(T, H), H);
            propPlaced = true; Show(propRends, propShown);
        }
        /// <summary>third person without a swing path: the hammer rides in the right fist, haft up and a little forward</summary>
        public void PlacePropAtRest()
        {
            if (prop == null || !rig.Has("hand_R")) return;
            var pR = rig.Pos("hand_R");
            PlaceProp(pR, new Vector3(0, 1, 0.3f).normalized, Vector3.right);
        }

        /// <summary>the held props from the real bones this frame (Animator.placeGunsFromBones), in the rig root's space</summary>
        public void Place()
        {
            var root = rig.root;
            for (int i = 0; i < 2; i++)
            {
                var s = slots[i]; if (s?.item == null) continue;
                string S = i == 0 ? "L" : "R";
                var fa = rig.B("forearm_" + S); var hn = rig.B("hand_" + S) ?? fa;
                if (fa == null) { s.node.gameObject.SetActive(false); continue; }
                var at = root.InverseTransformPoint(hn.position); var from = root.InverseTransformPoint(fa.position);
                var Zv = gunAim.HasValue ? gunAim.Value - at : at - from;
                if (s.arrow)
                {
                    // the nocked arrow: laid from the string hand through the bow hand
                    var ob = rig.B("hand_" + (i == 0 ? "R" : "L"));
                    if (ob != null) Zv = root.InverseTransformPoint(ob.position) - at;
                }
                if (Zv.sqrMagnitude < 1e-10f) Zv = Vector3.forward;
                Zv.Normalize();
                // the bow faces where the forearm points across the ground, limbs up
                if (s.upright) { Zv = new Vector3(Zv.x, 0, Zv.z); if (Zv.sqrMagnitude < 0.09f) Zv = Vector3.forward; Zv.Normalize(); }
                var Yv = Vector3.up - Zv * Zv.y;
                if (Yv.sqrMagnitude < 1e-4f) Yv = Vector3.forward;
                Yv.Normalize();
                // an archer's cant in first person: the bow's top limb tipped in toward the reticle
                if (s.upright && bowCant != 0) Yv = Quaternion.AngleAxis(-bowCant * Mathf.Rad2Deg, Zv) * Yv;
                if (s.upright && bowTilt != 0) { var q = Quaternion.AngleAxis(bowTilt * Mathf.Rad2Deg, Vector3.Cross(Yv, Zv).normalized); Yv = q * Yv; Zv = q * Zv; }
                var pos = at - Yv * 0.018f * rig.height;
                var rot = Quaternion.LookRotation(Zv, Yv);
                var ob2 = orbit[i];
                if (ob2.HasValue && ob2.Value.w > 0.001f)
                {
                    // out of the hand: blended toward its own place and facing
                    var o = ob2.Value;
                    pos = Vector3.Lerp(pos, o.p, o.w); rot = Quaternion.Slerp(rot, Quaternion.LookRotation(o.z, o.y), o.w);
                }
                s.node.localPosition = pos; s.node.localRotation = rot; s.node.localScale = Vector3.one * gunScale;
            }
            for (int i = 0; i < 2; i++)
            {
                var b = bracers[i]; if (b == null) continue;
                string S = i == 0 ? "L" : "R";
                var fa = rig.B("forearm_" + S); var hn = rig.B("hand_" + S);
                if (fa == null || hn == null) continue;
                var pf = root.InverseTransformPoint(fa.position); var ph = root.InverseTransformPoint(hn.position);
                var Zv = ph - pf; if (Zv.sqrMagnitude < 1e-10f) Zv = Vector3.forward; Zv.Normalize();
                var Yv = Vector3.up - Zv * Zv.y; if (Yv.sqrMagnitude < 1e-4f) Yv = Vector3.forward; Yv.Normalize();
                b.localPosition = Vector3.Lerp(pf, ph, 0.62f); b.localRotation = Quaternion.LookRotation(Zv, Yv); b.localScale = Vector3.one * gunScale;
            }
            if (back != null && rig.Has("chest"))
            {
                // the slung weapon rides the chest: pommel behind the right hip, haft up across the spine, the head over the left shoulder
                var H = rig.height; var chest = rig.B("chest");
                var Q = Quaternion.Inverse(root.rotation) * chest.rotation * Quaternion.Inverse(rig.rest["chest"].q);
                back.localPosition = rig.Pos("chest") + Q * new Vector3(0.08f * H, -0.27f * H, -0.11f * H);
                back.localRotation = Q * Quaternion.AngleAxis(0.42f * Mathf.Rad2Deg, Vector3.forward);
            }
        }
    }

    /// <summary>procedural stand-ins and the props the TS only ever built in code (the arrows, Haruto's blaster)</summary>
    public static class ProcProps
    {
        static Material Lit(Color c, float metal, float smooth, Color? glow = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (glow.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", glow.Value); }
            return m;
        }
        static GameObject Prim(PrimitiveType t, Transform parent, Material m, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var g = GameObject.CreatePrimitive(t); var col = g.GetComponent<Collider>(); if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localRotation = rot ?? Quaternion.identity;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }
        static readonly Quaternion AlongZ = Quaternion.Euler(90, 0, 0);     // a cylinder (Y axis) laid along +Z

        /// <summary>an arrow along +Z from its nock at the origin: a lacquered shaft, a steel head with a glowing edge, three vanes</summary>
        public static GameObject Arrow(float len, float L, HeldItem it)
        {
            var g = new GameObject("proc_arrow");
            var shaft = Lit(Conv.Hex("#2a2230"), 0.3f, 0.5f); var steel = Lit(it.color, 0.9f, 0.75f, it.glow * 0.35f); var vane = Lit(it.color, 0, 0.4f);
            Prim(PrimitiveType.Cylinder, g.transform, shaft, new Vector3(0, 0, len / 2), new Vector3(0.009f * L, len / 2, 0.009f * L), AlongZ);
            Prim(PrimitiveType.Cylinder, g.transform, steel, new Vector3(0, 0, len + 0.022f * L), new Vector3(0.016f * L, 0.025f * L, 0.004f * L), AlongZ);    // the head: a flat steel point
            Prim(PrimitiveType.Cylinder, g.transform, steel, new Vector3(0, 0, 0.004f * L), new Vector3(0.012f * L, 0.006f * L, 0.012f * L), AlongZ);       // nock
            for (int k = 0; k < 3; k++)
            {
                var v = Prim(PrimitiveType.Cube, g.transform, vane, Vector3.zero, new Vector3(0.001f * L, 0.018f * L, 0.065f * L));
                v.transform.localRotation = Quaternion.AngleAxis(k * 120, Vector3.forward);
                v.transform.localPosition = v.transform.localRotation * new Vector3(0, 0.012f * L, 0.05f * L);
            }
            return g;
        }

        /// <summary>a paper talisman along +Z past the fingertips, its face up (+Y): cream paper, a red seal</summary>
        public static GameObject Card(float len, float L, HeldItem it)
        {
            var g = new GameObject("proc_card"); float w = len * 0.36f;
            Prim(PrimitiveType.Cube, g.transform, Lit(it.color, 0, 0.15f, it.glow * 0.12f), new Vector3(0, 0.02f * L, len * 0.5f), new Vector3(w, 0.0015f * L, len));
            Prim(PrimitiveType.Cube, g.transform, Lit(Conv.Hex("#b3202a"), 0, 0.4f), new Vector3(0, 0.02f * L, len * 0.55f), new Vector3(w * 0.55f, 0.0018f * L, len * 0.55f));
            return g;
        }

        /// <summary>a plain blade along +Z from the grip (the stand-in while a prop is missing)</summary>
        public static GameObject Blade(float len, float L, HeldItem it)
        {
            var g = new GameObject("proc_blade"); float grip = len * 0.24f, blade = len - grip;
            Prim(PrimitiveType.Cylinder, g.transform, Lit(Conv.Hex("#1b2233"), 0.3f, 0.4f), new Vector3(0, 0, grip * 0.2f), new Vector3(0.022f * L, grip / 2, 0.022f * L), AlongZ);
            Prim(PrimitiveType.Cylinder, g.transform, Lit(Conv.Hex("#d9a441"), 0.85f, 0.7f), new Vector3(0, 0, grip * 0.7f), new Vector3(0.06f * L, 0.003f * L, 0.06f * L), AlongZ);
            Prim(PrimitiveType.Cube, g.transform, Lit(it.color, 0.9f, 0.78f), new Vector3(0, 0, grip * 0.7f + blade / 2), new Vector3(0.004f * L, 0.02f * L, blade));
            Prim(PrimitiveType.Cube, g.transform, Lit(it.glow, 0, 0.7f, it.glow * 1.6f), new Vector3(0, 0.011f * L, grip * 0.7f + blade * 0.47f), new Vector3(0.0045f * L, 0.003f * L, blade * 0.92f));
            return g;
        }

        /// <summary>Hayate's koi-scale shuriken lying flat (plane XZ, normal +Y), 2 x SHURIKEN_R x H across - a flat star</summary>
        public static GameObject Shuriken(float L, HeldItem it)
        {
            var g = new GameObject("proc_shuriken"); float r = Held.SHURIKEN_R * L;
            var steel = Lit(Conv.Hex("#dfe7e3"), 0.85f, 0.75f, Conv.Hex("#4fe3c1") * 0.25f);
            for (int k = 0; k < 4; k++) Prim(PrimitiveType.Cube, g.transform, steel, Vector3.zero, new Vector3(r * 2, 0.005f * L, r * 0.22f), Quaternion.AngleAxis(k * 45, Vector3.up));
            return g;
        }

        /// <summary>Haruto's "Sunspark" sidearm: barrel along +Z from the grip (origin)</summary>
        public static GameObject Blaster(float L, HeldItem it)
        {
            var g = new GameObject("proc_blaster");
            var white = Lit(Conv.Hex("#eef1f6"), 0.5f, 0.65f); var dark = Lit(Conv.Hex("#1c2433"), 0.55f, 0.5f); var gold = Lit(Conv.Hex("#b98224"), 0.8f, 0.66f);
            var core = Lit(Conv.Hex("#fff1c2"), 0, 0.6f, Conv.Hex("#ffd76a") * 2.2f);
            Prim(PrimitiveType.Cube, g.transform, white, new Vector3(0, 0.03f * L, 0.05f * L), new Vector3(0.035f * L, 0.05f * L, 0.13f * L));
            Prim(PrimitiveType.Cylinder, g.transform, dark, new Vector3(0, 0.038f * L, 0.15f * L), new Vector3(0.024f * L, 0.045f * L, 0.024f * L), AlongZ);
            Prim(PrimitiveType.Cube, g.transform, dark, new Vector3(0, -0.012f * L, 0), new Vector3(0.028f * L, 0.06f * L, 0.03f * L), Quaternion.AngleAxis(-0.25f * Mathf.Rad2Deg, Vector3.right));
            Prim(PrimitiveType.Cube, g.transform, gold, new Vector3(0, 0.058f * L, 0.05f * L), new Vector3(0.038f * L, 0.012f * L, 0.1f * L));
            Prim(PrimitiveType.Sphere, g.transform, core, new Vector3(0.018f * L, 0.03f * L, 0.06f * L), Vector3.one * 0.024f * L);
            g.transform.localScale = Vector3.one * it.size;
            return g;
        }
    }
}
