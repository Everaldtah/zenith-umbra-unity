// First-person arms (viewmodel; port of FirstPerson.ts): the local player's own hero model, cut down to the arms
// (ArmsMesh), head collapsed, parked at the eye under the main camera and drawn by a URP OVERLAY camera stacked on it
// (its own FOV and a cleared depth, so hands never clip into walls; its own layer, so it casts no shadow into the world).
// Each hero holds and uses their weapon their own way.
//
// Two sources of motion, per action:
//  1. The Blender-authored clips (Assets/ZU/Art/Anim/FP, FpImport): fp_idle (loop), fp_fire / fp_fire2, fp_alt, fp_melee,
//     fp_reload, fp_ability1 / 2, fp_ult, fp_hit, fp_land, fp_beam, fp_draw, fp_equip, fp_inspect - humanoid clips on the
//     hero's own rig through the FP controller, played by state name; gameplay owns the clock (Overwatch-style).
//  2. Procedural personality (FirstPersonView.Proc.cs): hand targets per hero with recoil, slashes, bow draw, kunai throws,
//     flame palms, reloads, casts, quick melee, Tenkai-Oh's keyed hammer - two-bone IK on the rig's arms.
// Movement bob, mouse sway and landing dip ride on top of either source. HeldRig attaches the weapons to the hands.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ZU.Sim;

namespace ZU.Game.FirstPerson
{
    public partial class FirstPersonView : MonoBehaviour
    {
        public const int LAYER = 8;                 // "Viewmodel" (FpImport names it)
        public const float FOV = 58;                // the TS viewmodel camera
        const float INSPECT_AFTER = 7;              // seconds standing idle before the hero shows off

        MatchRunner r;
        Camera main, overlay;
        Transform pivot;                            // under the camera: the rig root sits at -eye in here, bob / sway on top
        GameObject model;
        RigPose rig; Animator anim; HeldRig held; Fingers fingers; FpStyle style;
        public string heroId { get; private set; }
        public bool active { get; private set; }
        /// <summary>the current action source: 'clip:fp_fire' or 'proc:fire' (captures / tests)</summary>
        public string source = "";
        /// <summary>CPU cost of the viewmodel's own work this frame (Think + LateUpdate pose / props / fingers), ms, smoothed</summary>
        public float costMs;
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        public int trisKept { get; private set; }
        Vector3 eye;                                // model-space eye
        readonly Dictionary<string, float> clipLen = new Dictionary<string, float>();
        bool HasClip(string n) => clipLen.ContainsKey(n);
        // motion layers
        float walk, swayX, swayY, dip, dipV, aimK, leapK; bool wasLeap;
        double lastYaw, lastPitch; bool hasLast;
        double pAttack = 9, pCast = 9, pHit = 9, pLand = 9;
        int swings;
        (string name, double until)? oneShot;
        double idleSince; bool equipped; float reloadRate = 1;
        string playing = "";
        // what LateUpdate must do with the pose this frame
        bool procFrame; double procT; bool procNewAttack; bool clipFrame;
        Actor actor; double simT; float frameDt;

        public static FirstPersonView Ensure(MatchRunner r)
        {
            var cam = Camera.main; if (cam == null) return null;
            var v = cam.GetComponentInChildren<FirstPersonView>(true);
            if (v == null)
            {
                var go = new GameObject("Viewmodel");
                go.transform.SetParent(cam.transform, false);
                v = go.AddComponent<FirstPersonView>();
            }
            v.r = r; v.main = cam;
            if (v.overlay == null) v.MakeOverlay();
            return v;
        }

        void MakeOverlay()
        {
            var go = new GameObject("Viewmodel Camera");
            go.transform.SetParent(main.transform, false);
            overlay = go.AddComponent<Camera>();
            overlay.fieldOfView = FOV; overlay.nearClipPlane = 0.02f; overlay.farClipPlane = 30; overlay.cullingMask = 1 << LAYER;
            overlay.clearFlags = CameraClearFlags.Depth; overlay.depth = main.depth + 1; overlay.allowHDR = main.allowHDR; overlay.allowMSAA = main.allowMSAA;
            var od = overlay.GetUniversalAdditionalCameraData();
            od.renderType = CameraRenderType.Overlay; od.renderPostProcessing = false; od.renderShadows = true;   // (clearDepth is the serialized default: true - hands never clip into walls)
            od.requiresColorOption = CameraOverrideOption.Off; od.requiresDepthOption = CameraOverrideOption.Off;
            main.cullingMask &= ~(1 << LAYER);
            var md = main.GetUniversalAdditionalCameraData();
            md.renderType = CameraRenderType.Base;
            pivot = new GameObject("pivot").transform;
            pivot.SetParent(transform, false);
            SetActive(false);
        }

        void SetActive(bool on)
        {
            if (active == on) return;
            active = on;
            var md = main.GetUniversalAdditionalCameraData();
            if (on) { if (!md.cameraStack.Contains(overlay)) md.cameraStack.Add(overlay); }
            else md.cameraStack.Remove(overlay);
            overlay.enabled = on;
            if (model != null) model.SetActive(on);
        }

        /// <summary>once a frame after the camera is placed: who the viewmodel is for, and what it does this frame</summary>
        public void Sync(MatchRunner r)
        {
            this.r = r;
            var me = r.Player; double t = r.World.time;
            bool want = me != null && !r.thirdPerson && me.alive;
            if (want && (model == null || heroId != me.def.id)) want = Build(me);
            SetActive(want);
            if (!want) { hasLast = false; return; }
            actor = me; simT = t; frameDt = Mathf.Min(0.05f, Time.deltaTime);
            watch.Restart();
            Think(me, t, frameDt);
            watch.Stop();
        }

        bool Build(Actor me)
        {
            if (model != null) { Destroy(model); model = null; }
            var lib = HeroLibrary.Get(); var e = lib != null ? lib.Find(me.def.id) : null;
            if (e == null) return false;
            heroId = me.def.id; style = FpStyle.For(heroId);
            model = Instantiate(e.prefab, pivot);
            model.name = "fp_" + heroId;
            var lod = model.GetComponent<LODGroup>(); if (lod != null) Destroy(lod);
            var dyn = model.GetComponent<ZU.Dynamics.ZuDynamics>(); if (dyn != null) Destroy(dyn);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var tr in model.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = LAYER;
            // the HD LOD0 is the one worth looking at up close; the game mesh goes
            var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var hd = smrs.Where(s => s.name.StartsWith("hd_")).ToArray();
            var use = hd.Length > 0 ? hd : smrs;
            foreach (var s in smrs) if (!use.Contains(s)) Destroy(s.gameObject);
            int kept = 0, total = 0;
            foreach (var s in use)
            {
                s.sharedMesh = ArmsMesh.For(heroId, s, style.keep, style.drape, style.squeeze, out var st);
                kept += st.kept; total += st.total;
                s.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; s.updateWhenOffscreen = true; s.enabled = true;
            }
            rig = new RigPose(model.transform);
            if (!rig.ok) { Debug.LogWarning("ZU viewmodel: no rig under " + heroId); Destroy(model); model = null; return false; }
            ArmReach();
            var H = rig.height;
            eye = rig.rest["head"].p + new Vector3(0, H * 0.06f, H * 0.05f + style.push);
            eye -= ViewmodelOffset();
            // the clips: the hero's override controller (none: procedural only)
            anim = model.GetComponentInChildren<Animator>();
            clipLen.Clear();
            var oc = Resources.Load<AnimatorOverrideController>("ZUFp/fp_" + heroId);
            if (anim != null && oc != null)
            {
                anim.runtimeAnimatorController = oc; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.enabled = true;
                var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(); oc.GetOverrides(list);
                foreach (var kv in list) if (kv.Value != null) clipLen[kv.Key.name] = kv.Value.length;
            }
            else if (anim != null) anim.enabled = false;
            held = HeldRig.Attach(model, rig, me.def);
            if (held != null) { held.gunScale = style.gunScale; foreach (var rd in held.Renderers()) { rd.gameObject.layer = LAYER; rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } }
            fingers = Fingers.Build(model.transform);
            if (!string.IsNullOrEmpty(style.gauntlets)) LoadGauntlets();
            archerOn = WantArcher;
            if (archerOn) ArcherBuild();
            else if (fpArrow != null) { Destroy(fpArrow.gameObject); fpArrow = null; }
            overlay.nearClipPlane = Mathf.Max(0.02f, style.clip);
            // state
            playing = ""; oneShot = null; equipped = false; swings = 0; idleSince = r.World.time; hasLast = false;
            walk = swayX = swayY = dip = dipV = aimK = leapK = 0; hSwing = (-99, 1);
            trisKept = kept;
            Debug.Log($"ZU viewmodel {heroId}: {kept}/{total} triangles kept, {clipLen.Count} clips, eye {eye:F2}, H {H:F2}");
            return true;
        }

        /// <summary>Style.shoulderW / reach on this viewmodel's own rig: shoulders moved in toward the spine, upper arms
        /// stretched, the rest pose rebound to match so the arm IK stays exact</summary>
        void ArmReach()
        {
            if (style.shoulderW <= 0 && style.reach <= 0) return;
            var uL = rig.B("upperarm_L"); var uR = rig.B("upperarm_R"); if (uL == null || uR == null) return;
            float mid = (rig.rest["upperarm_L"].p.x + rig.rest["upperarm_R"].p.x) / 2;
            foreach (var S in new[] { "L", "R" })
            {
                var ua = rig.B("upperarm_" + S); var fa = rig.B("forearm_" + S); var rp = rig.rest["upperarm_" + S].p;
                if (style.shoulderW > 0) ua.position += rig.root.TransformVector(new Vector3(mid + Mathf.Sign(rp.x - mid) * style.shoulderW - rp.x, 0, 0));
                if (style.reach > 0 && fa != null) fa.localPosition *= style.reach;
            }
            rig.Bind();
        }

        /// <summary>Where the arms rig sits relative to the camera: auto-rigged arms vary a lot in length, so instead of
        /// bending the grip to the rig, the rig moves (in model space, applied to the eye) until both rest hand targets are
        /// within reach - the hands land where the style wants them on screen and the shoulders stay off-screen</summary>
        Vector3 ViewmodelOffset()
        {
            var arms = new List<(Vector3 t, Vector3 s, float reach)>();
            foreach (var (S, v) in new[] { ("L", style.L), ("R", (Vector3?)style.R) })
            {
                if (!v.HasValue || !rig.rest.ContainsKey("upperarm_" + S) || !rig.rest.ContainsKey("forearm_" + S) || !rig.rest.ContainsKey("hand_" + S)) continue;
                var ua = rig.rest["upperarm_" + S].p; var fa = rig.rest["forearm_" + S].p; var hd = rig.rest["hand_" + S].p;
                arms.Add((v.Value + eye, ua, (Vector3.Distance(ua, fa) + Vector3.Distance(fa, hd)) * 0.92f));
            }
            if (arms.Count == 0) return Vector3.zero;
            var o = Vector3.zero;
            // the shoulders must stay behind the camera - by the style's push too
            float maxZ = arms.Min(a => eye.z - a.s.z) - (0.03f + style.push);
            void Pull((Vector3 t, Vector3 s, float reach) a, float f)
            {
                var d = a.t - o - a.s; float ex = d.magnitude - a.reach;
                if (ex > 0) o += d.normalized * ex * f;
                o.z = Mathf.Min(o.z, maxZ);
            }
            for (int it = 0; it < 40; it++) foreach (var a in arms) Pull(a, 0.6f);
            Pull(arms[arms.Count - 1], 1);          // when both grips can't be reached, the main hand wins
            return Vector3.ClampMagnitude(o, Mathf.Max(0.5f, 2 * arms.Max(a => a.reach)));
        }

        // ------------------------------------------------------------------ per frame: which source, which clip
        bool Play(string name, bool loop, float rate = 1, float scrub = -1)
        {
            if (anim == null || !HasClip(name)) return false;
            anim.SetFloat(FpClips.RATE, scrub >= 0 ? 0 : rate);
            if (scrub >= 0) { anim.Play(name, 0, scrub); playing = name; return true; }
            if (playing == name) return true;
            // a one-shot retriggered (auto fire) restarts in place at full weight (a cross-fade from itself would flash the
            // arms through the bind pose); anything else cross-fades in 80 ms
            if (playing == "" || playing == null) anim.Play(name, 0, 0f); else anim.CrossFadeInFixedTime(name, 0.08f, 0, 0f);
            playing = name;
            return true;
        }

        void Think(Actor a, double t, float dt)
        {
            // ---- motion layers shared by both sources: walk bob, mouse sway, landing dip
            float sp = (float)System.Math.Sqrt(a.vel.x * a.vel.x + a.vel.z * a.vel.z);
            walk += sp * dt * (a.grounded ? 1.6f : 0.3f);
            double yaw = a.input.yaw, pitch = a.input.pitch;
            float yawRate = 0, pitchRate = 0;
            if (hasLast && dt > 0)
            {
                double dy = yaw - lastYaw; while (dy > System.Math.PI) dy -= 2 * System.Math.PI; while (dy < -System.Math.PI) dy += 2 * System.Math.PI;
                yawRate = (float)(dy / dt); pitchRate = (float)((pitch - lastPitch) / dt);
            }
            lastYaw = yaw; lastPitch = pitch; hasLast = true;
            swayX += (Mathf.Clamp(-yawRate * 0.012f, -0.05f, 0.05f) - swayX) * Mathf.Min(1, dt * 10);
            swayY += (Mathf.Clamp(-pitchRate * 0.01f, -0.04f, 0.04f) - swayY) * Mathf.Min(1, dt * 10);
            var c = a.anim;
            bool newAttack = t - c.attackAt < pAttack - 1e-6, newCast = t - c.castAt < pCast - 1e-6, newHit = t - c.hitAt < pHit - 1e-6, newLand = t - c.landAt < pLand - 1e-6;
            pAttack = t - c.attackAt; pCast = t - c.castAt; pHit = t - c.hitAt; pLand = t - c.landAt;
            if (newLand) dipV -= 0.35f;
            bool leap = a.Has("stompair", t);
            if (wasLeap && !leap && a.grounded) dipV -= 1.3f;                 // the slam lands
            wasLeap = leap;
            leapK += ((leap && !a.grounded ? 1 : 0) - leapK) * Mathf.Min(1, dt * (leap ? 8 : 16));
            if (newAttack && c.attackKind != "punch") swings++;
            dipV += (-dip * 120 - dipV * 14) * dt; dip += dipV * dt;
            // aiming down the arrow (Freja's Take Aim): the bow comes up and a little to the right, rolled more upright
            // (the keyed archers carry their own aim pose: FirstPersonView.Archer.cs)
            aimK += ((style.grip == Grip.Bow && !Archer && a.Sv("zoom", 0) > 0 ? 1 : 0) - aimK) * Mathf.Min(1, dt * 14);
            if (held != null) { held.gunAim = null; held.bowCant = 0; held.bowTilt = 0; held.orbit[0] = held.orbit[1] = null; held.gunHide[0] = held.gunHide[1] = false; }
            procFrame = false; clipFrame = false; procT = t; procNewAttack = newAttack;
            // Mirei's Stellar Rebirth: a procedural moment over her clips
            double rb = heroId == "mirei" && a.sv.TryGetValue("rebirthAt", out var rba) ? t - rba : 9;
            if (rb < 1.3) { procFrame = true; return; }
            // the archers are keyed on gameplay (FirstPersonView.Archer.cs), not clipped
            if (Archer) { procFrame = true; return; }
            // ---- 1. authored clips
            if (clipLen.Count > 0)
            {
                string kind = c.attackKind;
                bool busy = newAttack || newCast || a.reloadUntil > t || a.charging || a.beamOn || a.flameOn || sp > 0.5f || !a.grounded;
                if (busy || !equipped) idleSince = t;
                string fire = swings % 2 == 0 && HasClip("fp_fire2") ? "fp_fire2" : "fp_fire";
                // chain blades (Enra): the fists clips are punches - a swing or a throw is the procedural whip instead; and
                // Hayate's shuriken throws while the nodachi is sheathed (the baked fire clip is a generic flick)
                bool chains = held != null && held.spec.chains;
                bool throwing = heroId == "hayate" && !a.Has("dragonblade", t) && (kind == "primary" || kind == "secondary") && t - c.attackAt < 0.5;
                if ((chains && (kind == "primary" || kind == "secondary") && t - c.attackAt < CB_SWING) || throwing)
                {
                    oneShot = null; procFrame = true; return;
                }
                string ev = newCast ? (c.castId == a.def.ult.id ? "fp_ult" : c.castId == a.def.ability2.id ? "fp_ability2" : "fp_ability1")
                    : newAttack ? (kind == "punch" ? "fp_melee" : kind == "secondary" ? "fp_alt" : fire) : newHit ? "fp_hit" : newLand ? "fp_land" : "";
                if (!equipped) { equipped = true; if (HasClip("fp_equip")) ev = "fp_equip"; }
                if (ev == "" && t - idleSince > INSPECT_AFTER && HasClip("fp_inspect") && oneShot == null) { ev = "fp_inspect"; idleSince = t; }
                if (ev != "" && HasClip(ev) && (ev != "fp_hit" && ev != "fp_land" || oneShot == null)) { oneShot = (ev, t + clipLen[ev]); playing = ""; }
                if (oneShot.HasValue && (t >= oneShot.Value.until || (oneShot.Value.name == "fp_inspect" && busy))) oneShot = null;
                // the deflect guard ends when the window does: the blade comes down with it
                if (oneShot.HasValue && oneShot.Value.name == "fp_ability2" && heroId == "hayate" && !a.Has("deflect", t) && t - c.castAt > 0.2) oneShot = null;
                string want = "fp_idle"; bool loop = true; float rate = 1, scrub = -1;
                if ((a.beamOn || a.flameOn) && HasClip("fp_beam")) want = "fp_beam";
                if (a.charging && HasClip("fp_draw")) { want = "fp_draw"; loop = false; scrub = Mathf.Clamp01((float)a.charge); }
                if (a.reloadUntil > t && HasClip("fp_reload"))
                {
                    // the reload clip is authored at the weapon's reload time: stretch it to the reload actually running
                    if (playing != "fp_reload") { double left = a.reloadUntil - t; reloadRate = Mathf.Clamp(clipLen["fp_reload"] / Mathf.Max(0.05f, (float)left), 0.3f, 3f); }
                    want = "fp_reload"; loop = false; rate = reloadRate;
                }
                if (oneShot.HasValue && !(want == "fp_reload" && oneShot.Value.name == "fp_inspect")) { want = oneShot.Value.name; loop = false; rate = 1; scrub = -1; }
                if (HasClip(want) && Play(want, loop, rate, scrub))
                {
                    clipFrame = true;
                    if (held != null)
                    {
                        // bows canted in (Hanzo's hold: rolled nearly flat, upper limb to the right, tilted so that limb recedes)
                        held.bowCant = style.grip == Grip.Bow ? -1.35f + 0.45f * aimK : 0; held.bowTilt = style.grip == Grip.Bow ? 0.35f - 0.15f * aimK : 0;
                        // chain blades: the fists clip holds the forearms up like a boxer's - the blades point at the reticle instead
                        if (chains) held.gunAim = new Vector3(0, 0, 14) + eye;
                    }
                    source = "clip:" + want;
                    return;
                }
            }
            // ---- 2. procedural personality
            procFrame = true;
        }

        // ------------------------------------------------------------------ after the Animator: the pose and the props
        void LateUpdate()
        {
            if (!active || model == null || actor == null) return;
            watch.Start();
            var a = actor; double t = simT; float dt = frameDt;
            // the head (and its hair) out of the camera; not the neck - high collars are weighted to it and would smear
            var head = rig.B("head"); if (head != null) head.localScale = Vector3.one * 1e-3f;
            // materials: stealth like the world view (hidden hero: the arms fade)
            // rig placement: eye at the origin, then the whole viewmodel offset by bob / sway
            float sp = (float)System.Math.Sqrt(a.vel.x * a.vel.x + a.vel.z * a.vel.z);
            float mv = Mathf.Min(1, sp / 6) * (a.grounded ? 1 : 0.3f);
            float bobX = Mathf.Sin(walk) * 0.012f * mv + swayX, bobY = -Mathf.Abs(Mathf.Cos(walk)) * 0.016f * mv + swayY + dip * 0.2f;
            float ak = aimK * aimK * (3 - 2 * aimK);
            // (the TS scene's +X is view-left, so its -bob / -0.035 ak on x are +bob / +0.035 ak here; a yaw sway turns the other way)
            pivot.localPosition = new Vector3(-eye.x + bobX + 0.035f * ak, -eye.y + bobY + 0.035f * ak, -eye.z);
            pivot.localRotation = Quaternion.Euler(swayY * 0.6f * Mathf.Rad2Deg, -swayX * 0.8f * Mathf.Rad2Deg, 0);
            // Mirror Water (Hayate's deflect): each shot turned on the blade flicks the whole viewmodel toward where it came
            // from and rolls it that way, over both sources of motion
            double dfa = t - a.anim.deflectAt;
            if (dfa < 0.16 && a.Has("deflect", t))
            {
                float k = Mathf.Sin(Mathf.Min(1, (float)dfa / 0.16f) * Mathf.PI); var d = a.anim.deflectDir;
                // view space: right, up (the sim's world direction turned by the yaw; Unity's right is the sim's -x)
                float dx = (float)(d.x * System.Math.Cos(a.yaw) - d.z * System.Math.Sin(a.yaw)), dy = (float)d.y;
                pivot.localPosition += new Vector3(dx * 0.06f * k, dy * 0.04f * k, 0);
                pivot.localRotation = pivot.localRotation * Quaternion.Euler(dy * 0.25f * k * Mathf.Rad2Deg, 0, dx * 0.4f * k * Mathf.Rad2Deg);
            }
            if (procFrame) Proc(a, t, procNewAttack);
            else if (held != null) { if (held.prop != null) held.PlacePropAtRest(); held.Place(); }
            held?.UpdateState(a, t, true);
            // the props' live details as the world view has them (Tenkai-Oh's thruster flame, Hayate's nodachi glow, Hibiki's woofer)
            held?.UpdateDetails(a, t);
            if (fingers != null && Archer) { fingers.Set(0, arcGrips.L); fingers.Set(1, arcGrips.R); fingers.Update(dt, 22); }
            else fingers?.Drive(a, t, dt, true);
            watch.Stop();
            costMs = Mathf.Lerp(costMs, (float)watch.Elapsed.TotalMilliseconds, 0.05f);
        }

        void OnDestroy() { if (main != null && overlay != null) main.GetUniversalAdditionalCameraData().cameraStack.Remove(overlay); }
    }
}
