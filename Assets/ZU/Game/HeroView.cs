// A hero drawn with its imported model (HeroLibrary): the prefab, its humanoid Animator driven by the simulation - local
// velocity into the 8-way locomotion blend, grounded / air / fly / stun / dead states, and one-shot triggers from the
// sim's animation cues (Actor.anim: attackAt, castAt, hitAt, jumpAt, landAt) - interpolated between simulation steps.
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public interface IActorView { void Sync(MatchRunner r, Actor a); }

    public static class ActorViews
    {
        /// <summary>the hero's model when the library has it, else the prototype capsule</summary>
        public static IActorView Create(Actor a, Transform parent)
        {
            var lib = HeroLibrary.Get();
            var e = lib != null ? lib.Find(a.def.id) : null;
            if (e != null) return HeroView.Create(a, e, lib, parent);
            return ActorView.Create(a, parent);
        }
    }

    public class HeroView : MonoBehaviour, IActorView
    {
        Animator anim;
        Renderer[] rends;
        double seenAttack = -9, seenCast = -9, seenHit = -9, seenJump = -9, seenLand = -9;
        Vector3 lastPos; bool hasLast;
        Vector2 vel;            // smoothed local velocity for the blend tree
        static readonly int VelX = Animator.StringToHash("VelX"), VelZ = Animator.StringToHash("VelZ"), Speed = Animator.StringToHash("Speed"), VelY = Animator.StringToHash("VelY"),
            Grounded = Animator.StringToHash("Grounded"), Dead = Animator.StringToHash("Dead"), Stun = Animator.StringToHash("Stun"), Fly = Animator.StringToHash("Fly"),
            Jump = Animator.StringToHash("Jump"), Land = Animator.StringToHash("Land"), Shoot = Animator.StringToHash("Shoot"), Cast = Animator.StringToHash("Cast"),
            Melee = Animator.StringToHash("Melee"), Hit = Animator.StringToHash("Hit");

        public static HeroView Create(Actor a, HeroLibrary.Entry e, HeroLibrary lib, Transform parent)
        {
            var go = Instantiate(e.prefab, parent);
            go.name = $"{a.def.id} #{a.id} ({a.team})";
            var v = go.AddComponent<HeroView>();
            v.anim = go.GetComponentInChildren<Animator>();
            if (v.anim != null) { v.anim.runtimeAnimatorController = e.controller != null ? e.controller : lib.baseController; v.anim.applyRootMotion = false; }
            v.rends = go.GetComponentsInChildren<Renderer>(true);
            // hair / cloth dynamics attach here once ZU.Dynamics lands (ZuDynamics on the rig root)
            return v;
        }

        public void Sync(MatchRunner r, Actor a)
        {
            double t = r.World.time;
            bool firstPerson = a == r.Player && !r.thirdPerson;
            bool hidden = a.Has("stealth", t) && r.Player != null && a.team != r.Player.team && !a.Has("revealed", t);
            bool show = !(firstPerson || hidden) && (a.alive || t - a.deathAt < 2.5);
            foreach (var x in rends) if (x != null) x.enabled = show;
            var pos = r.DrawPos(a);
            transform.SetPositionAndRotation(pos, Conv.Yaw(a.yaw));
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
    }
}
