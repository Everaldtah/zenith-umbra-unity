// Hand-held weapons (port of HeldProps.ts + the guns CharacterView.ts / TomoeProps.ts / Hammer.ts place for the other heroes):
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
        /// <summary>the TS gun group's own scale over the fitted size (Gantetsu's chainguns are concept-sized: x1.25, "half as
        /// long as he is tall") - the barrels, the muzzle flash and the grip offset all scale with it</summary>
        public float group = 1;
        public Color color, glow;
        public HeldItem(string id, HeldKind kind, float size, string color, string glow, float pitch = 0, string alt = null, bool flip = false)
        { this.id = id; this.kind = kind; this.size = size; this.color = Conv.Hex(color); this.glow = Conv.Hex(glow); this.pitch = pitch; this.alt = alt; this.flip = flip; }
    }

    /// <summary>chains: a blade on a chain in each fist (Enra); bracer: a vambrace prop worn on each forearm;
    /// prop: a two-handed weapon on the hammer frame (haft up +Y from the pommel) - Tenkai-Oh's hammer, Tomoe's axe</summary>
    public class HeldSpec { public HeldItem L, R, prop; public bool chains, backProp, skates; public string bracer; }

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
            // Gantetsu's rotary chainguns (Hinoko left, Hanabi right): the second generation when the build has it, fitted 0.46 H
            // long in a group scaled 1.25 (TS attachGuns), the grip a quarter of the way along from the back (baked into the
            // held_ prefab); the barrel cluster spins on its own axis (Looks/Editor BarrelBake: split_held_<id>)
            { "gantetsu", new HeldSpec { L = new HeldItem("prop_gantetsu_hinoko_v2", HeldKind.Gun, 0.46f, "#1a1a1a", "#4fe3c1", 0, "prop_gantetsu_hinoko") { group = 1.25f },
                R = new HeldItem("prop_gantetsu_hanabi_v2", HeldKind.Gun, 0.46f, "#1a1a1a", "#ffb347", 0, "prop_gantetsu_hanabi") { group = 1.25f } } },
            // Haruto's Sunspark sidearm (Hammer.ts buildBlaster, procedural): barrel along the forearm, 6% of the height past the
            // wrist, a little below it, at 1.5x
            { "haruto", new HeldSpec { R = new HeldItem("blaster", HeldKind.Gun, 1.5f, "#eef1f6", "#ffd76a") { at = new Vector3(0, -0.017f, 0.06f) } } },
            // Hibiki: the Subwoofer Blaster on the right forearm (Hammer.ts buildSonicAmp, procedural), mag-skates on both feet
            { "hibiki", new HeldSpec { R = new HeldItem("sonicamp", HeldKind.Gun, 1, "#f2f4f7", "#39d6ff"), skates = true } },
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
            /// <summary>a gun's spinning barrel cluster (Gantetsu) and its muzzle flash (Gantetsu's star, Tomoe's crown disc):
            /// ChaingunProp.spin / .flash; the flash isn't among `rends` (it shows on its own rounds, not with the gun)</summary>
            public Transform spin, flash; public MeshRenderer flashR; public float spinA, flashSize; public Color flashCol;
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
                if (spec.prop.kind == HeldKind.Hammer) h.BuildFlame(H);
            }
            if (spec.skates)
            {
                h.skates = new ProcProps.Skate[2];
                for (int i = 0; i < 2; i++) { h.skates[i] = ProcProps.MagSkate(H); h.skates[i].root.SetParent(rig.root, false); }
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

        /// <summary>the held gun cut in two (Looks/Editor BarrelBake, TS upgradeChaingun): the receiver, and the barrel cluster in a
        /// "spin" node on its own axis, plus a "muzzle" marker - for whichever of id / alt Load would use; null when not baked</summary>
        public static GameObject LoadSplit(string id, string alt)
        {
            string use = Resources.Load<GameObject>("ZUProps/held_" + id) != null ? id : alt;
            var pf = use != null ? Resources.Load<GameObject>("ZULooks/split_held_" + use) : null;
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
            GameObject m = it.id == "blaster" ? ProcProps.Blaster(H, it) : it.id == "sonicamp" ? ProcProps.SonicAmp(H, out amp) : it.kind == HeldKind.Arrow ? ProcProps.Arrow(len, H, it)
                : (heroId == "gantetsu" ? LoadSplit(it.id, it.alt) : null) ?? Load(it.id, it.alt);
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
                    if (!proc) { fit.localScale = Vector3.one * len * it.group; fit.localPosition = it.at * H * it.group; }
                    s.spin = fit.Find("spin");
                    break;
                case HeldKind.Hammer: case HeldKind.Axe:
                    // the haft up +Y from the pommel at the origin, the head's striking face on +X
                    fit.localScale = Vector3.one * len; fit.localPosition = it.at * H;
                    break;
            }
            s.rends = s.body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in s.rends) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (it.kind == HeldKind.Gun) MakeFlash(s, it, H, fit);
            return s;
        }

        /// <summary>the muzzle flashes CharacterView.updateGuns flickers (after `rends`: a flash shows on its own rounds). Gantetsu
        /// (Hammer.ts buildChaingun): a crisp four-petal star, its points 0.11 H out and its waist 0.03 H, warm gold, on the
        /// barrel cluster's axis 0.03 H past the muzzle - all in the 1.25 group. Tomoe (TomoeProps.buildScattergun): a pale
        /// turquoise disc of radius 0.07 H at the flared crown, (0, 0.047 H, 0.36 H) x 0.72.</summary>
        void MakeFlash(Slot s, HeldItem it, float H, Transform fit)
        {
            Mesh mesh; Vector3 at;
            if (heroId == "gantetsu")
            {
                var mz = fit.Find("muzzle");
                if (mz != null) at = s.node.InverseTransformPoint(mz.position);
                else
                {
                    // not baked: the fitted gun's front face, on its middle
                    var b = new Bounds(); bool any = false;
                    foreach (var mf in fit.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null) continue;
                        var mb = mf.sharedMesh.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var c = s.node.InverseTransformPoint(mf.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                            if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                        }
                    }
                    at = any ? new Vector3(b.center.x, b.center.y, b.max.z) : new Vector3(0, 0, it.size * H * it.group);
                }
                at += Vector3.forward * (0.03f * H * it.group);
                mesh = ProcProps.StarMesh; s.flashSize = 0.11f * H * it.group;
                var c0 = Conv.Hex("#ffd27a"); c0.a = 0.95f; s.flashCol = c0;
            }
            else if (heroId == "tomoe")
            {
                at = new Vector3(0, (0.035f + 0.012f) * H, 0.36f * H) * 0.72f;
                mesh = ProcProps.DiscMesh; s.flashSize = 0.07f * H;
                var c0 = Conv.Hex("#dffffb"); c0.a = 0.9f; s.flashCol = c0;
            }
            else return;
            var add = Fx.MatchFx.Current?.Additive ?? Resources.Load<Material>("ZUFx/additive");
            if (add == null) return;
            var g = new GameObject("muzzle flash"); s.flash = g.transform; s.flash.SetParent(s.node, false);
            s.flash.localPosition = at;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            s.flashR = g.AddComponent<MeshRenderer>(); s.flashR.sharedMaterial = add;
            s.flashR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; s.flashR.receiveShadows = false;
            g.SetActive(false);
        }

        /// <summary>every prop renderer (the owner hides them with the hero, or moves them to the viewmodel layer)</summary>
        public IEnumerable<Renderer> Renderers()
        {
            foreach (var s in slots) if (s != null) { if (s.rends != null) foreach (var r in s.rends) yield return r; if (s.swapRends != null) foreach (var r in s.swapRends) yield return r; if (s.flashR != null) yield return s.flashR; }
            if (propRends != null) foreach (var r in propRends) yield return r;
            if (flameR != null) yield return flameR;          // (made after propRends; UpdateDetails shows it)
            if (backRends != null) foreach (var r in backRends) yield return r;
            foreach (var b in bracers) if (b != null) foreach (var r in b.GetComponentsInChildren<Renderer>(true)) yield return r;
            if (skates != null) foreach (var s in skates) foreach (var r in s.root.GetComponentsInChildren<Renderer>(true)) yield return r;
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
            Guns(a, t);
        }
        static void Show(Renderer[] rs, bool on) { if (rs == null) return; foreach (var r in rs) if (r != null) r.enabled = on; }

        double gunT = -1;
        /// <summary>CharacterView.updateGuns for the gunners (here so the first-person viewmodel gets it too, after the visibility
        /// above): Gantetsu's barrels spin with each gun's spin-up and each muzzle flashes on its own rounds; Tomoe's crown
        /// muzzle flashes on each primary blast. (TS: the cores glow hotter while firing - on the procedural guns' ember vents
        /// and shroud face, which upgradeChaingun removes once the Tripo guns load, so nothing glows on them in the TS either.)</summary>
        void Guns(Actor a, double t)
        {
            float dt = gunT < 0 ? 0 : Mathf.Clamp((float)(t - gunT), 0, 0.1f); gunT = t;
            if (heroId == "gantetsu")
                for (int i = 0; i < 2; i++)
                {
                    var s = slots[i]; if (s?.item == null) continue;
                    float spin = (float)a.Sv(i == 0 ? "spin1" : "spin2", 0); double age = t - (i == 0 ? a.anim.fireL : a.anim.fireR);
                    s.spinA += dt * spin * 38 * (i == 0 ? 1 : -1);
                    if (s.spin != null) s.spin.localRotation = Quaternion.AngleAxis(-s.spinA * Mathf.Rad2Deg, Vector3.forward);    // (TS rotation.z; mirrored)
                    Flash(s, age < 0.045 && a.alive, 0.7f + Random.value * 0.6f);
                }
            else if (heroId == "tomoe" && slots[1] != null)
                Flash(slots[1], t - a.anim.attackAt < 0.06 && a.anim.attackKind == "primary" && a.alive, 0.8f + Random.value * 0.5f);
        }

        /// <summary>a muzzle flash for this frame: on with its gun, a fresh size and a random turn about the barrel</summary>
        void Flash(Slot s, bool on, float k)
        {
            if (s.flash == null) return;
            on &= !s.hidden && shown;
            if (s.flash.gameObject.activeSelf != on) s.flash.gameObject.SetActive(on);
            if (!on) return;
            s.flash.localRotation = Quaternion.AngleAxis(-Random.value * 180, Vector3.forward);
            s.flash.localScale = Vector3.one * (s.flashSize * k);
            mpb ??= new MaterialPropertyBlock();
            s.flashR.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", s.flashCol); s.flashR.SetPropertyBlock(mpb);
        }

        // ---------------------------------------------------------------------------------------------- weapon details
        Transform flame; MeshRenderer flameR; float flameLen, flameR0; Vector3 nozzle;
        /// <summary>Hibiki's amp (its woofer, burst ring and glowing parts) and his skates</summary>
        ProcProps.Amp amp; ProcProps.Skate[] skates;
        /// <summary>the lower body's yaw off the facing (the animator's hipYaw, model space, TS frame): the skates point with the legs</summary>
        public float feetYaw;
        MaterialPropertyBlock mpb;          // (made on first use: a MonoBehaviour may not create Unity objects in a field initializer)
        bool bladeGlowOn;
        static Mesh cone;

        /// <summary>Tenkai-Oh's rocket hammer: the thruster flame at the nozzle, the -X end of the head 12% below its top (TS
        /// Hammer.ts upgradeHammer), an additive cone 0.045 x 0.16 of the model height trailing away along -X</summary>
        void BuildFlame(float H)
        {
            if (prop == null) return;
            var b = new Bounds(); bool any = false;
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = prop.worldToLocalMatrix * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            if (!any) return;
            var add = Fx.MatchFx.Current?.Additive ?? Resources.Load<Material>("ZUFx/additive");
            if (add == null) return;
            if (cone == null)
            {
                // an open cone along +Z, apex at the origin, radius 1 at z = 1 (the TS ConeGeometry, open-ended)
                var v = new System.Collections.Generic.List<Vector3>(); var c = new System.Collections.Generic.List<Color>(); var tri = new System.Collections.Generic.List<int>();
                for (int i = 0; i <= 14; i++) { float a = i * Mathf.PI * 2 / 14; v.Add(Vector3.zero); v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1)); c.Add(Color.white); c.Add(Color.white); if (i < 14) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); } }
                cone = new Mesh { name = "flame cone" }; cone.SetVertices(v); cone.SetColors(c); cone.SetTriangles(tri, 0); cone.RecalculateBounds();
            }
            var g = new GameObject("thruster flame"); flame = g.transform; flame.SetParent(prop, false);
            g.AddComponent<MeshFilter>().sharedMesh = cone;
            flameR = g.AddComponent<MeshRenderer>(); flameR.sharedMaterial = add; flameR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flameLen = 0.16f * H; flameR0 = 0.045f * H;
            // the base at the nozzle, the tip trailing back along -X: the cone's apex sits flameLen behind the nozzle, +Z toward it
            nozzle = new Vector3(b.min.x - 0.012f * H, b.max.y - b.size.y * 0.12f, 0);
            flame.localRotation = Quaternion.LookRotation(Vector3.right);      // apex behind, the wide end (+Z) at the nozzle
            g.SetActive(false);
        }

        float lastT = -1;
        /// <summary>CharacterView.updateGuns for Hibiki: the woofer pumps on each round, the burst ring flashes, the equaliser and
        /// the wheels glow the colour of the track (brighter amped, brighter still grinding and deep in the Groove); placeFeet:
        /// the skates clamped under the ankles, pointing where the legs face, the wheels rolling with the ground speed</summary>
        void Hibiki(Actor a, double t)
        {
            float dt = lastT < 0 ? 0 : Mathf.Clamp((float)(t - lastT), 0, 0.1f); lastT = (float)t;
            var col = Conv.Hex(a.Sv("track", 0) != 0 ? "#ffd23f" : "#39d6ff"); float ampK = a.Has("amp", t) ? 1.6f : 1;
            if (amp != null)
            {
                double age = t - a.anim.attackAt; float L = rig.height;
                amp.spin.localPosition = amp.spin0 + Vector3.forward * (age < 0.06 ? 0.01f * L * (float)(1 - age / 0.06) : 0);
                bool fl = age < 0.05 && a.alive && a.anim.attackKind != "punch" && shown;
                amp.flash.gameObject.SetActive(fl);
                if (fl)
                {
                    amp.flash.localScale = Vector3.one * (0.07f * L * (0.8f + (float)age * 12));
                    amp.flashR.GetPropertyBlock(mpb); var c = Conv.Hex("#bffcff"); c.a = 0.85f; mpb.SetColor("_BaseColor", c); amp.flashR.SetPropertyBlock(mpb);
                }
                amp.core.SetColor("_EmissionColor", col * (2.2f * ampK));
            }
            if (skates != null)
            {
                float H = rig.height, roll = Mathf.Sqrt((float)(a.vel.x * a.vel.x + a.vel.z * a.vel.z)) * dt / (0.016f * H);
                float groove = 1 + Mathf.Min(2.5f, (float)((a.Sv("rhythm", 1) - 1) * 0.15));
                float footY = (rig.rest["foot_L"].p.y + rig.rest["foot_R"].p.y) / 2;
                for (int i = 0; i < 2; i++)
                {
                    var s = skates[i]; var fb = rig.B(i == 0 ? "foot_L" : "foot_R");
                    s.root.gameObject.SetActive(fb != null && shown);
                    if (fb == null) continue;
                    // wheels on the floor under the foot (the ankle's height above its rest says how far the foot is lifted)
                    var p = rig.root.InverseTransformPoint(fb.position);
                    s.root.localPosition = new Vector3(p.x, Mathf.Max(0, p.y - footY) - 0.004f * H, p.z + 0.015f * H);
                    s.root.localRotation = Quaternion.Euler(0, -feetYaw * Mathf.Rad2Deg, 0);      // (TS rotation.y; mirrored)
                    foreach (var w in s.wheels) w.Rotate(Vector3.right, roll * Mathf.Rad2Deg, Space.Self);
                    s.glow.SetColor("_EmissionColor", col * ((a.Has("grinding", t) ? 3.2f : 2) * ampK * groove));
                }
            }
        }

        /// <summary>the per-frame details CharacterView.updateGuns draws: Tenkai-Oh's thruster roaring through the strike (not the
        /// wind-up), Hayate's drawn nodachi burning with the koi-dragon's violet through the Dragon Gate</summary>
        public void UpdateDetails(Actor a, double t)
        {
            mpb ??= new MaterialPropertyBlock();
            if (flame != null)
            {
                double age = t - a.anim.attackAt;
                bool on = a.alive && propShown && a.anim.attackKind == "primary" && age > 0.12 && age < 0.45;
                flame.gameObject.SetActive(on);
                if (on)
                {
                    float k = 0.5f + 0.9f * Mathf.Sin(Mathf.Min(1, (float)(age - 0.12) / 0.33f) * Mathf.PI) + Random.value * 0.15f;
                    // the cone runs apex -> base along +Z: the apex trails k x the length behind the nozzle, the base on it
                    flame.localScale = new Vector3(flameR0, flameR0, flameLen * k);
                    flame.localPosition = nozzle + Vector3.left * (flameLen * k);
                    flameR.GetPropertyBlock(mpb); var c = Conv.Hex("#ff8a2a"); c.a = 0.7f; mpb.SetColor("_BaseColor", c); flameR.SetPropertyBlock(mpb);
                }
            }
            if (amp != null || skates != null) Hibiki(a, t);
            if (heroId == "hayate" && slots[1]?.rends != null)
            {
                bool glow = a.Has("dragonblade", t);
                if (glow || bladeGlowOn)
                {
                    float k = glow ? 0.9f + 0.35f * Mathf.Sin((float)t * 9) : 0;
                    foreach (var r in slots[1].rends)
                    {
                        if (r == null) continue;
                        if (glow && !bladeGlowOn) { var ms = new System.Collections.Generic.List<Material>(r.sharedMaterials); var rim = Resources.Load<Material>("ZUFx/rim"); if (rim != null && !ms.Contains(rim)) { ms.Add(rim); r.sharedMaterials = ms.ToArray(); } }
                        r.GetPropertyBlock(mpb); mpb.SetColor("_RimColor", Conv.Hex("#b36bff")); mpb.SetFloat("_Rim", 1.2f * k); mpb.SetFloat("_Fill", 0.45f * k); r.SetPropertyBlock(mpb);
                    }
                    bladeGlowOn = glow;
                }
            }
        }
        /// <summary>a ragdoll death (TS CharacterView.deathRagdoll): the two-handed weapon leaves the body, the slung one hides</summary>
        public void HideTwoHanded() { Show(propRends, false); Show(backRends, false); }

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
        /// <summary>the two-handed prop with its pommel exactly at `pommel` (model space; the animator's held-hammer pose, the
        /// 10% grip offset already taken), the haft along `dir`, the striking face toward `side`</summary>
        public void PlacePropFrame(Vector3 pommel, Vector3 dir, Vector3 side)
        {
            if (prop == null) return;
            var H = dir.normalized; var T = (side - H * Vector3.Dot(side, H)).normalized;
            prop.localPosition = pommel;
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

        static Mesh star, disc;
        /// <summary>Gantetsu's muzzle flash (Hammer.ts buildChaingun's star Shape): eight points round the barrel axis in the XY
        /// plane, alternately at radius 1 (on the axes) and 0.03 / 0.11 (between them), filled; white vertex colours (FX shaders
        /// multiply by them)</summary>
        public static Mesh StarMesh => star ??= Fan("muzzle star", 8, k => k % 2 == 1 ? 0.03f / 0.11f : 1);
        /// <summary>Tomoe's crown flash (TomoeProps: CircleGeometry(0.07 L, 12)): a unit disc in the XY plane</summary>
        public static Mesh DiscMesh => disc ??= Fan("muzzle disc", 12, k => 1);

        static Mesh Fan(string name, int n, System.Func<int, float> radius)
        {
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { Color.white }; var tri = new List<int>();
            for (int k = 0; k < n; k++) { float a = k * Mathf.PI * 2 / n, r = radius(k); v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0)); c.Add(Color.white); }
            for (int k = 0; k < n; k++) tri.AddRange(new[] { 0, 1 + k, 1 + (k + 1) % n });
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

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

        public sealed class Amp { public Transform spin, flash; public Vector3 spin0; public MeshRenderer flashR; public Material core; }
        public sealed class Skate { public Transform root; public Transform[] wheels; public Material glow; }

        /// <summary>Hibiki's Subwoofer Blaster (TS buildSonicAmp): a housing along the forearm with a gold band and an equaliser
        /// strip, a round baffle with the woofer cone (pumps on each shot) and a glowing dust cap, a burst ring in front</summary>
        public static GameObject SonicAmp(float L, out Amp amp)
        {
            var g = new GameObject("proc_sonicamp");
            var shell = Lit(Conv.Hex("#f2f4f7"), 0.25f, 0.65f); var gold = Lit(Conv.Hex("#d9a441"), 0.85f, 0.7f); var dark = Lit(Conv.Hex("#15181f"), 0.4f, 0.5f);
            var core = Lit(Conv.Hex("#bffcff"), 0, 0.6f, Conv.Hex("#39d6ff") * 2.4f);
            float cy = -0.028f * L;
            Prim(PrimitiveType.Cube, g.transform, shell, new Vector3(0, cy, 0.06f * L), new Vector3(0.075f * L, 0.07f * L, 0.2f * L));
            Prim(PrimitiveType.Cube, g.transform, gold, new Vector3(0, cy + 0.036f * L, 0.06f * L), new Vector3(0.079f * L, 0.012f * L, 0.2f * L));
            for (int k = 0; k < 6; k++) Prim(PrimitiveType.Cube, g.transform, core, new Vector3(-0.025f * L + k * 0.01f * L, cy + 0.045f * L, 0.03f * L), new Vector3(0.008f * L, (0.01f + (k % 3) * 0.006f) * L, 0.012f * L));
            float bz = 0.17f * L;
            Prim(PrimitiveType.Cylinder, g.transform, shell, new Vector3(0, cy, bz), new Vector3(0.11f * L, 0.015f * L, 0.11f * L), AlongZ);     // the baffle
            Prim(PrimitiveType.Cylinder, g.transform, gold, new Vector3(0, cy, bz + 0.016f * L), new Vector3(0.112f * L, 0.003f * L, 0.112f * L), AlongZ);
            var spin = new GameObject("woofer").transform; spin.SetParent(g.transform, false); spin.localPosition = new Vector3(0, cy, bz + 0.018f * L);
            Prim(PrimitiveType.Cylinder, spin, dark, new Vector3(0, 0, -0.004f * L), new Vector3(0.09f * L, 0.01f * L, 0.09f * L), AlongZ);       // the cone
            Prim(PrimitiveType.Cylinder, spin, core, new Vector3(0, 0, 0.004f * L), new Vector3(0.032f * L, 0.002f * L, 0.032f * L), AlongZ);     // the dust cap
            // the burst ring in front of the cone (the muzzle flash): the FX annulus stood across the barrel
            var add = Fx.MatchFx.Current?.Additive ?? Resources.Load<Material>("ZUFx/additive");
            var fg = new GameObject("burst"); fg.transform.SetParent(g.transform, false);
            fg.transform.localPosition = new Vector3(0, cy, bz + 0.045f * L); fg.transform.localRotation = Quaternion.Euler(90, 0, 0);
            fg.AddComponent<MeshFilter>().sharedMesh = Fx.FxKit.RingMesh;
            var fr = fg.AddComponent<MeshRenderer>(); fr.sharedMaterial = add; fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fg.transform.localScale = Vector3.one * 0.07f * L;
            fg.SetActive(false);
            Prim(PrimitiveType.Cube, g.transform, dark, new Vector3(0, -0.005f * L, 0), new Vector3(0.03f * L, 0.045f * L, 0.05f * L));
            amp = new Amp { spin = spin, spin0 = spin.localPosition, flash = fg.transform, flashR = fr, core = core };
            return g;
        }

        /// <summary>a mag-skate (TS buildMagSkate): a gold-trimmed white chassis clamped under the shoe, four glowing sky-blue wheels</summary>
        public static Skate MagSkate(float L)
        {
            var g = new GameObject("proc_magskate");
            var white = Lit(Conv.Hex("#f4f6fa"), 0.3f, 0.65f); var gold = Lit(Conv.Hex("#d9a441"), 0.85f, 0.7f);
            var glow = Lit(Conv.Hex("#c9fbff"), 0, 0.7f, Conv.Hex("#39d6ff") * 2.2f);
            Prim(PrimitiveType.Cube, g.transform, white, new Vector3(0, 0.034f * L, 0.02f * L), new Vector3(0.078f * L, 0.022f * L, 0.2f * L));
            Prim(PrimitiveType.Cube, g.transform, gold, new Vector3(0, 0.047f * L, 0.02f * L), new Vector3(0.082f * L, 0.006f * L, 0.2f * L));
            foreach (var sx in new[] { 1, -1 }) Prim(PrimitiveType.Cube, g.transform, glow, new Vector3(sx * 0.041f * L, 0.034f * L, 0.02f * L), new Vector3(0.004f * L, 0.01f * L, 0.18f * L));
            var wheels = new Transform[4];
            for (int k = 0; k < 4; k++)
            {
                var hub = new GameObject("wheel").transform; hub.SetParent(g.transform, false); hub.localPosition = new Vector3(0, 0.022f * L, -0.068f * L + k * 0.058f * L);
                Prim(PrimitiveType.Cylinder, hub, glow, Vector3.zero, new Vector3(0.048f * L, 0.011f * L, 0.048f * L), Quaternion.Euler(0, 0, 90));
                wheels[k] = hub;
            }
            return new Skate { root = g.transform, wheels = wheels, glow = glow };
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
