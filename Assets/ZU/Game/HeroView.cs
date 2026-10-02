// A hero drawn with its imported model (HeroLibrary), the TS CharacterView's job: the prefab, its humanoid Animator driven
// by the simulation (local velocity into the 8-way locomotion blend, grounded / air / fly / stun / dead states, one-shot
// triggers from the sim's animation cues), then the procedural animator on top of it (Anim/ProcAnimator, the TS
// Animator.ts: planted feet, aim-twisted spine, per-hero personas, every hero-specific arm pose, Tenkai-Oh's hammer path),
// the performance layer's squash / whole-body tilt / knockdown applied to the root, the held weapons following the hands
// and the fingers closing on them - interpolated between simulation steps.
// Order each frame: Sync (MatchRunner.Update) -> Animator -> ProcDriver.LateUpdate (-100: the procedural pose) ->
// AbilityFx (-50: ragdolls, eyelids) -> HeroView.LateUpdate (0: weapons, fingers) -> ZuDynamics (500: hair, cloth).
using UnityEngine;
using ZU.Dynamics;
using ZU.Game.Anim;
using ZU.Sim;

namespace ZU.Game
{
    /// <summary>what a hero view reads from whoever drives it: a match (MatchRunner), or the Hero Viewer's turntable</summary>
    public interface IViewHost { double SimTime { get; } Actor Player { get; } bool ThirdPerson { get; } Vector3 DrawPos(Actor a); }

    public interface IActorView { void Sync(IViewHost r, Actor a); }

    public static class ActorViews
    {
        /// <summary>the model a hero's equipped skin wears (a model skin such as Hibiki's Bassline Armor -> "hibiki_armor"), or
        /// null; the front end sets it once skins can be chosen</summary>
        public static System.Func<Actor, string> SkinModel;

        /// <summary>the hero's model when the library has it, else the prototype capsule</summary>
        public static IActorView Create(Actor a, Transform parent)
        {
            var lib = HeroLibrary.Get();
            var e = lib != null ? lib.Find(ModelId(a, lib)) : null;
            if (e != null) return HeroView.Create(a, e, lib, parent);
            return ActorView.Create(a, parent);
        }

        /// <summary>TS CharacterView.modelIdFor: a summon wears its own form (def.model: Enra's effigy -> enra_susanoo), a model
        /// skin swaps the whole body, otherwise the hero's own id</summary>
        static string ModelId(Actor a, HeroLibrary lib)
        {
            var d = a.def; var skin = SkinModel?.Invoke(a);
            return d.model != null && lib.Find(d.model) != null ? d.model : skin != null && lib.Find(skin) != null ? skin : d.id;
        }
    }

    public class HeroView : MonoBehaviour, IActorView
    {
        /// <summary>the TS keeps a dead body on screen this long (the ragdoll or death clip, then the sink from 2.6 s)</summary>
        public const double BODY_SECS = 3.8;
        Animator anim;
        Renderer[] rends;
        ZuDynamics dyn;
        HeldRig held; Fingers fingers;      // the weapon in the hands, the hands closed on it
        ProcAnimator proc; AnimState state; // the procedural layer (TS Animator.ts) and what it reads
        Actor actor; double syncT; float syncDt; Vector3 drawPos;
        Vector3 baseScale = Vector3.one; float? downYaw;
        bool wasAlive = true;
        double seenAttack = -9, seenCast = -9, seenHit = -9, seenJump = -9, seenLand = -9;
        Vector2 vel;            // smoothed local velocity for the blend tree
        static readonly int VelX = Animator.StringToHash("VelX"), VelZ = Animator.StringToHash("VelZ"), Speed = Animator.StringToHash("Speed"), VelY = Animator.StringToHash("VelY"),
            Grounded = Animator.StringToHash("Grounded"), Dead = Animator.StringToHash("Dead"), Stun = Animator.StringToHash("Stun"), Fly = Animator.StringToHash("Fly"),
            Jump = Animator.StringToHash("Jump"), Land = Animator.StringToHash("Land"), Shoot = Animator.StringToHash("Shoot"), Cast = Animator.StringToHash("Cast"),
            Melee = Animator.StringToHash("Melee"), Hit = Animator.StringToHash("Hit");

        /// <summary>Enra's chain blades: how far out on its chain each blade is (0 in the fist .. 1 full length) - the chain view reads it</summary>
        public float[] ChainExt => proc?.chainExt;
        /// <summary>1 on the frame a heavy strike lands (the camera kicks)</summary>
        public float Impact => proc?.impact ?? 0;

        public static HeroView Create(Actor a, HeroLibrary.Entry e, HeroLibrary lib, Transform parent)
        {
            var go = Instantiate(e.prefab, parent);
            go.name = $"{a.def.id} #{a.id} ({a.team})";
            var v = go.AddComponent<HeroView>();
            v.baseScale = go.transform.localScale;
            v.anim = go.GetComponentInChildren<Animator>();
            if (v.anim != null) { v.anim.runtimeAnimatorController = e.controller != null ? e.controller : lib.baseController; v.anim.applyRootMotion = false; }
            v.rends = go.GetComponentsInChildren<Renderer>(true);
            v.dyn = go.GetComponent<ZuDynamics>();       // hair and cloth (HeroImport puts it on the prefab)
            if (v.dyn != null) v.dyn.overrideVelocity = true;
            // the held weapons (HeldProps), finger grips (Fingers) and the procedural animator bind to the bind pose: before the
            // Animator's first frame
            var rig = new FirstPerson.RigPose(go.transform);
            v.held = HeldRig.Attach(go, rig, a.def);
            v.fingers = Fingers.Build(go.transform);
            if (v.anim != null)
            {
                var p = new ProcAnimator(rig);
                if (p.ok)
                {
                    v.proc = p;
                    p.hasProp = v.held != null && v.held.prop != null; if (p.hasProp) p.hammerLen = v.held.hammerLen;
                    go.AddComponent<ProcDriver>().view = v;
                }
            }
            return v;
        }

        public void Sync(IViewHost r, Actor a)
        {
            double t = r.SimTime;
            bool firstPerson = a == r.Player && !r.ThirdPerson;
            bool hidden = a.Has("stealth", t) && r.Player != null && a.team != r.Player.team && !a.Has("revealed", t);
            bool show = !(firstPerson || hidden) && (a.alive || t - a.deathAt < BODY_SECS);
            foreach (var x in rends) if (x != null) x.enabled = show;
            actor = a; syncT = t; syncDt = Time.deltaTime; shown = show;
            drawPos = r.DrawPos(a);
            transform.SetPositionAndRotation(drawPos, Conv.Yaw(a.yaw));
            if (dyn != null)
            {
                // the sim's velocity and footing; a respawn snaps the chains to the new pose instead of whipping them across
                // the map; nobody sees a hidden hero's hair, so it isn't simulated
                dyn.velocity = Conv.U(a.vel); dyn.grounded = a.grounded;
                if (a.alive && !wasAlive) dyn.Teleport();
                dyn.quality = show ? ZuDynamics.QualityMode.Auto : ZuDynamics.QualityMode.Off;
            }
            wasAlive = a.alive;
            if (anim == null) return;
            // local velocity (Unity space), smoothed: the sim's velocity is what the legs should be doing
            var wv = Conv.U(a.vel);
            var local = Quaternion.Inverse(transform.rotation) * wv;
            var want = new Vector2(local.x, local.z);
            vel = Vector2.Lerp(vel, want, 1 - Mathf.Exp(-Time.deltaTime * 12));
            anim.SetFloat(VelX, vel.x); anim.SetFloat(VelZ, vel.y); anim.SetFloat(Speed, vel.magnitude); anim.SetFloat(VelY, (float)a.vel.y);
            anim.SetBool(Grounded, a.grounded);
            anim.SetBool(Dead, !a.alive);
            anim.SetBool(Stun, a.alive && (a.Has("stun", t) || a.Has("knockdown", t)));
            anim.SetBool(Fly, a.flying);
            // one-shots: each cue fires its trigger once
            var c = a.anim;
            if (c.jumpAt > seenJump) { seenJump = c.jumpAt; if (t - c.jumpAt < 0.2) anim.SetTrigger(Jump); }
            if (c.landAt > seenLand) { seenLand = c.landAt; if (t - c.landAt < 0.2) anim.SetTrigger(Land); }
            if (c.attackAt > seenAttack)
            {
                seenAttack = c.attackAt;
                if (t - c.attackAt < 0.2) anim.SetTrigger(c.attackKind == "punch" || a.def.primary.kind == "melee" ? Melee : Shoot);
            }
            if (c.castAt > seenCast) { seenCast = c.castAt; if (t - c.castAt < 0.2) anim.SetTrigger(Cast); }
            if (c.hitAt > seenHit) { seenHit = c.hitAt; if (t - c.hitAt < 0.2 && a.alive) anim.SetTrigger(Hit); }
        }

        bool shown;
        /// <summary>after the Animator (ProcDriver, order -100): the procedural pose over the clips, then the performance layer on
        /// the root (TS CharacterView: squash and stretch about the feet, the whole-body tilt about the hips, knocked flat about
        /// the feet along the push)</summary>
        internal void Animate()
        {
            transform.localScale = baseScale;          // last frame's squash off before the pose is read
            var a = actor;
            if (a == null || proc == null) return;
            // the dead belong to the Animator's death state and the ragdoll (AbilityFx); the hidden aren't worth posing
            if (!shown || !a.alive) { proc.prop = null; downYaw = null; return; }
            bool hammer = held != null && held.prop != null && (a.def.id != "tomoe" || Held.AxeOut(a, syncT));
            var sp = Conv.S(drawPos);           // where the body is drawn, in the sim's frame
            state = AnimState.From(a, syncT, Time.deltaTime, hammer, new Vector3((float)sp.x, (float)sp.y, (float)sp.z), transform.lossyScale.y, state);
            proc.Update(state, anim);
            // the performance layer, in the TS frame: tilt about a pivot at the hips, then a knockdown laid along the push
            float piv = (float)a.Height * 0.55f;
            var q = Quaternion.AngleAxis(proc.tiltPitch * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(proc.tiltRoll * Mathf.Rad2Deg, Vector3.forward);
            var jp = q * new Vector3(0, piv, 0);
            var pos = new Vector3(-jp.x, piv - jp.y, -jp.z);
            float yaw = (float)a.yaw;
            if (proc.down > 0)
            {
                // knocked flat: the body keeps the facing it fell with (the aim may turn, a body on the floor doesn't spin)
                downYaw ??= yaw;
                float dy = downYaw.Value - yaw; dy = Mathf.Atan2(Mathf.Sin(dy), Mathf.Cos(dy));
                yaw += dy * Mathf.Min(1, proc.down * 3);
                float th = proc.down * 1.5f; var d = proc.downDir;
                var kq = Quaternion.AngleAxis(th * Mathf.Rad2Deg, new Vector3(d.z, 0, -d.x).normalized);     // up x the push
                q = kq * q; pos = kq * pos;
                pos.y += Mathf.Sin(th) * (float)a.Height * 0.09f;
            }
            else downYaw = null;
            var yawQ = Conv.Yaw(yaw);
            transform.SetPositionAndRotation(drawPos + yawQ * ProcAnimator.M(pos), yawQ * ProcAnimator.M(q));
            transform.localScale = Vector3.Scale(baseScale, new Vector3(proc.sqXZ, proc.sqY, proc.sqXZ));
            if (proc.impact > 0 && Fx.MatchFx.Current != null) Fx.MatchFx.Current.Shake = Mathf.Max(Fx.MatchFx.Current.Shake, 0.6f);
        }

        /// <summary>after the Animator and the procedural pose: the weapons follow the hands, the fingers close on them</summary>
        void LateUpdate()
        {
            if (actor == null) return;
            if (held != null)
            {
                // the hammer along the animator's swing path (or riding the fist without one); props out of the hand (Tomoe's
                // Fang in the Warpath, Enra's blades on their chains) where the animator flung them
                var pp = proc?.prop;
                if (held.prop != null) { if (pp.HasValue) held.PlacePropFrame(ProcAnimator.M(pp.Value.pos), ProcAnimator.M(pp.Value.haft), ProcAnimator.M(pp.Value.side)); else held.PlacePropAtRest(); }
                for (int i = 0; i < 2; i++)
                {
                    var o = proc != null && actor.alive ? proc.gunOrbit[i] : null;
                    held.orbit[i] = o.HasValue ? (ProcAnimator.M(o.Value.p), ProcAnimator.M(o.Value.z), ProcAnimator.M(o.Value.y), o.Value.w) : ((Vector3, Vector3, Vector3, float)?)null;
                }
                held.Place(); held.UpdateState(actor, syncT, shown);
            }
            if (fingers != null && shown) fingers.Drive(actor, syncT, syncDt, false);
        }
    }

    /// <summary>runs the hero's procedural animator after the Animator and before AbilityFx (-50) / HeroView (0) / ZuDynamics (500)</summary>
    [DefaultExecutionOrder(-100)]
    public class ProcDriver : MonoBehaviour
    {
        public HeroView view;
        void LateUpdate() { if (view != null) view.Animate(); }
    }
}
