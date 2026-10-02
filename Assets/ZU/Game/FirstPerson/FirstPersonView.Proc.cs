// The procedural half of the viewmodel (FirstPerson.ts proc / hammerProc): hand targets in view space (right, up, forward
// from the eye, metres) per grip and moment, solved onto the rig's arms by two-bone IK, with the held props following.
// Used for the heroes without authored clips (Tomoe, Tenkai-Oh, Gantetsu, Hibiki) and for the moments the clips don't
// cover (Enra's chain whips, Hayate's shuriken throws, Mirei's Stellar Rebirth).
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.FirstPerson
{
    public partial class FirstPersonView
    {
        // Enra's Hellfire Chains timelines (ChainBlades.ts): a light swing 0.15 s wind-up, 0.25 s arc, recover to 0.62 s;
        // the throw 0.25 s out, a beat taut, 0.3 s back
        const float CB_WIND = 0.15f, CB_ARC = 0.25f, CB_SWING = 0.62f, CB_THROW = 0.6f, CB_THROW_OUT = 0.08f, CB_THROW_HIT = 0.25f, CB_THROW_BACK = 0.34f;
        // Tomoe's Crescent Reaping: the cleave with a 3-frame hit-stop as it crosses the target
        const float REAP_SECS = 0.75f, REAP_HIT = 0.58f, REAP_STOP = 0.05f;
        static float ReapPhase(float castAge) { float cp = castAge / REAP_SECS; return cp < REAP_HIT ? cp : Mathf.Max(REAP_HIT, cp - REAP_STOP / REAP_SECS); }
        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3 - 2 * u); }
        static float Bump(float u) => u <= 0 || u >= 1 ? 0 : Mathf.Sin(u * Mathf.PI);
        static float SwingExt(float t)
        {
            if (t < 0) return 0;
            if (t < CB_WIND) return 0.12f * Smooth(t / CB_WIND);
            if (t < CB_WIND + CB_ARC) { float u = (t - CB_WIND) / CB_ARC; return 0.12f + 0.88f * Smooth(u / 0.45f); }
            if (t < CB_SWING) return 1 - Smooth((t - CB_WIND - CB_ARC) / (CB_SWING - CB_WIND - CB_ARC));
            return 0;
        }
        static float SwingArc(float t) => t < CB_WIND ? 0 : t < CB_WIND + CB_ARC ? Smooth((t - CB_WIND) / CB_ARC) : 1;
        static float ThrowExt(float t)
        {
            if (t < CB_THROW_OUT) return 0;
            if (t < CB_THROW_HIT) return Smooth((t - CB_THROW_OUT) / (CB_THROW_HIT - CB_THROW_OUT));
            if (t < CB_THROW_BACK) return 1;
            if (t < CB_THROW) return 1 - Smooth((t - CB_THROW_BACK) / (CB_THROW - CB_THROW_BACK));
            return 0;
        }
        static float ThrowSpin(float t)
        {
            if (t < CB_THROW_OUT) return 0;
            if (t < CB_THROW_HIT) return (t - CB_THROW_OUT) / (CB_THROW_HIT - CB_THROW_OUT);
            if (t < CB_THROW_BACK) return 1;
            if (t < CB_THROW) return 1 + (t - CB_THROW_BACK) / (CB_THROW - CB_THROW_BACK);
            return 0;
        }
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        /// <summary>a TS model-space wrist twist (Euler XYZ, radians) mirrored into Unity's model space: Y and Z turn the other way</summary>
        static Quaternion Wrist(float x, float y, float z) => Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(-z * Mathf.Rad2Deg, Vector3.forward);
        static readonly string[] BODY = { "hips", "spine", "chest", "neck", "head", "thigh_L", "shin_L", "foot_L", "thigh_R", "shin_R", "foot_R" };

        /// <summary>view metres -> model space: hand targets are camera-relative; the rig sits wherever puts the grip in reach</summary>
        Vector3 ToM(Vector3 v) => v + eye;

        void Proc(Actor a, double tt, bool newAttack)
        {
            float t = (float)tt;
            var S = style;
            // torso and legs at rest (the clips may have left them elsewhere)
            foreach (var n in BODY) rig.ResetBone(n);
            if (S.grip == Grip.Hammer && held != null && held.prop != null) { HammerProc(a, t); return; }
            Vector3 R = S.R; Vector3? L = S.L;
            var c = a.anim;
            float atk = t - (float)c.attackAt, cast = t - (float)c.castAt; string kind = c.attackKind;
            string src = "idle";
            // idle breathing / personality
            float br = Mathf.Sin(t * 1.7f) * 0.004f;
            R += V(0, br, 0); if (L.HasValue) L = L.Value + V(0, br * 0.8f, 0);
            Quaternion? wristR = null, wristL = null;
            if (S.grip == Grip.Katana) wristR = Wrist(-0.6f, 0, 0.3f);
            // Tomoe's Fang hand: held knuckles-up, the blade out past the fingers
            if (heroId == "tomoe") wristL = Wrist(0, 0, 1.5f);
            // reload: hands dip, off hand comes to the weapon
            if (a.reloadUntil > t)
            {
                float dur = a.def.primary.reload.HasValue && a.def.primary.reload.Value > 0 ? (float)a.def.primary.reload.Value : 1.4f;
                float u = 1 - (float)(a.reloadUntil - t) / dur, k = Bump(u);
                R += V(-0.04f, -0.1f, -0.06f) * k; if (L.HasValue) L = Vector3.Lerp(L.Value, R + V(-0.05f, -0.02f, 0), k * 0.8f);
                src = "reload";
            }
            bool chains = held != null && held.spec.chains;
            bool melee = a.def.primary.kind == "melee" && kind == "primary" || kind == "secondary" && a.def.secondary != null && a.def.secondary.kind == "melee";
            if (kind == "punch" && atk < 0.45f)
            {
                // quick melee: off-hand jab (everyone), pulled back then snapped straight out
                float u = atk / 0.42f, ext = u < 0.14f ? -0.4f * u / 0.14f : u < 0.28f ? -0.4f + 1.4f * (u - 0.14f) / 0.14f : Mathf.Max(0, 1 - (u - 0.28f) / 0.72f);
                var tgt = V(-0.03f, -0.1f, 0.36f + 0.3f * ext);
                if (L.HasValue && S.grip != Grip.Hammer) L = Vector3.Lerp(L.Value, tgt, Mathf.Clamp01(ext + 0.4f)); else R = Vector3.Lerp(R, V(0.05f, -0.1f, 0.36f + 0.3f * ext), 0.8f);
                src = "melee";
            }
            else if (melee && chains && atk < CB_SWING)
            {
                // Hellfire Chains (Kratos' Blades of Chaos): the swinging hand alternates - wound back to its own edge of the
                // frame, whipped across the whole view, recovered low across the body - and the BLADE leaves the fist on its
                // chain: out to the chain's reach, sweeping the width of the view edge-first; the throw shoots the right blade
                // straight out to the reticle (7.5 m), spinning once, holds it taut a beat and yanks it back
                if (kind == "secondary" && atk < CB_THROW)
                {
                    var back = V(0.3f, 0.08f, 0.22f); var outp = V(0.02f, -0.06f, 0.98f);
                    R = atk < 0.08f ? Vector3.Lerp(S.R, back, Smooth(atk / 0.08f)) : atk < 0.25f ? Vector3.Lerp(back, outp, Smooth((atk - 0.08f) / 0.17f)) : atk < 0.34f ? outp : Vector3.Lerp(outp, S.R, Smooth((atk - 0.34f) / 0.28f));
                    wristR = Wrist(-0.2f, 0, 0);
                    float ext = ThrowExt(atk), spin = ThrowSpin(atk) * 360;
                    var qs = Quaternion.AngleAxis(spin, Vector3.right);
                    held.orbit[1] = (ToM(V(0.02f, -0.06f + 0.05f * ext, 0.98f + 6.4f * ext)), qs * Vector3.forward, qs * Vector3.up, Mathf.Min(1, ext * 3));
                    src = "throw";
                }
                else if (kind == "secondary") src = "idle";
                else
                {
                    int dir = swings % 2 == 1 ? -1 : 1; var bas = dir > 0 ? S.R : (S.L ?? S.R);
                    var ownEdge = V(0.44f * dir, 0.04f, 0.26f); var far = V(-0.4f * dir, -0.2f, 0.5f);
                    var h = atk < 0.12f ? Vector3.Lerp(bas, ownEdge, Smooth(atk / 0.12f)) : atk < 0.38f ? Vector3.Lerp(ownEdge, far, Smooth((atk - 0.12f) / 0.26f)) : Vector3.Lerp(far, bas, Smooth((atk - 0.38f) / 0.24f));
                    var wq = Wrist(-0.4f, 0, dir * 0.6f);
                    if (dir > 0) { R = h; wristR = wq; } else { L = h; wristL = wq; }
                    float ext = SwingExt(atk), arc = SwingArc(atk); int i = dir > 0 ? 1 : 0;
                    float xB = (1.7f - 3.4f * arc) * dir, zB = 0.5f + 3.6f * ext, yB = 0.15f - 0.45f * arc;
                    held.orbit[i] = (ToM(V(xB, yB, zB)), V(-dir, -0.12f, 0.3f).normalized, Vector3.up, Mathf.Min(1, ext * 1.6f));
                    src = "slash";
                }
            }
            else if (melee && atk < 0.6f)
            {
                // blade / fist swings alternate direction through the combo
                int dir = swings % 2 == 1 ? -1 : 1; float u = Smooth(atk / 0.35f);
                if (S.grip == Grip.Fists) { R = Vector3.Lerp(R, V(0.04f, -0.12f, 0.62f), Bump(atk / 0.4f)); src = "punch"; }
                else if (S.grip == Grip.Kunai) { L = Vector3.Lerp(L ?? S.R, Vector3.Lerp(V(-0.3f, 0.02f, 0.35f), V(0.2f, -0.3f, 0.45f), u), Bump(atk / 0.5f)); src = "slash"; }
                else
                {
                    var from = V(0.32f * dir, 0.06f, 0.34f); var to = V(-0.28f * dir, -0.32f, 0.42f);
                    R = Vector3.Lerp(R, Vector3.Lerp(from, to, u), Mathf.Min(1, Bump(Mathf.Min(1, atk / 0.55f)) * 1.4f));
                    if (L.HasValue && S.grip != Grip.Hammer) L = Vector3.Lerp(L.Value, R + V(-0.07f, -0.03f, -0.02f), 0.7f);
                    wristR = Wrist(-0.3f - u * 0.6f, 0, dir * (0.9f - u * 1.4f));
                    src = "slash";
                }
            }
            else if (S.grip == Grip.Dual && L.HasValue && leapK > 0.05f)
            {
                // the leap: both guns hauled up and in, muzzles to the sky, ready to come down with him
                R += V(-0.06f, 0.2f, -0.1f) * leapK; L = L.Value + V(0.06f, 0.2f, -0.1f) * leapK;
                src = "leap";
            }
            else if (S.grip == Grip.Dual && L.HasValue)
            {
                // twin chainguns: each gun kicks straight back on its own rounds and the spun-up guns chatter
                foreach (var (k, age) in new[] { (1, t - (float)c.fireR), (0, t - (float)c.fireL) })
                {
                    float kk = Mathf.Max(0, 1 - age / 0.06f), jit = age < 0.1f ? 0.004f : 0;
                    var d = V((Random.value - 0.5f) * jit, 0.005f * kk + (Random.value - 0.5f) * jit, -S.recoil * kk);
                    if (k == 1) R += d; else L = L.Value + d;
                    if (age < 0.1f) src = "chaingun";
                }
            }
            else if (kind != "punch" && atk < 0.5f && S.grip != Grip.Bow)
            {
                // ranged: per-grip kick
                float rr = Mathf.Max(0, 1 - atk / 0.18f);
                if (S.grip == Grip.Kunai && heroId == "hayate")
                {
                    // Genji's throws. Primary: each shuriken is its own short straight thrust of the forearm at the reticle from
                    // low right, the wrist flicking as it leaves, the hand snapping back for the next. Fan: the hand loads across
                    // the body low left, whips back across the bottom of the view and lets the fan go at the middle
                    if (kind == "secondary")
                    {
                        var load = V(-0.2f, -0.27f, 0.36f); var rel = V(0.3f, -0.19f, 0.5f);
                        R = atk < 0.08f ? Vector3.Lerp(S.R, load, Smooth(atk / 0.08f)) : atk < 0.26f ? Vector3.Lerp(load, rel, Smooth((atk - 0.08f) / 0.18f)) : Vector3.Lerp(rel, S.R, Smooth((atk - 0.26f) / 0.24f));
                        float roll = atk < 0.08f ? -1.1f * Smooth(atk / 0.08f) : atk < 0.26f ? -1.1f + 1.9f * Smooth((atk - 0.08f) / 0.18f) : 0.8f * (1 - Smooth((atk - 0.26f) / 0.24f));
                        wristR = Wrist(-0.3f, 0, roll);
                        src = "fan";
                    }
                    else
                    {
                        var back = S.R + V(0.02f, 0.02f, 0); var outp = V(0.1f, -0.17f, 0.62f);
                        R = atk < 0.1f ? Vector3.Lerp(back, outp, Smooth(atk / 0.1f)) : Vector3.Lerp(outp, S.R, Smooth((atk - 0.1f) / 0.3f));
                        wristR = Wrist(-0.9f * Bump(Mathf.Min(1, atk / 0.14f)), 0, 0);
                        src = "throw";
                    }
                }
                else if (S.grip == Grip.Kunai) { float u = atk / 0.3f; R = Vector3.Lerp(R, u < 0.35f ? V(0.24f, -0.04f, 0.16f) : V(0.06f, -0.12f, 0.58f), Bump(Mathf.Min(1, u))); src = "throw"; }
                else if (S.grip == Grip.Caster) { R += V(-0.03f, 0.04f, 0.12f) * Bump(atk / 0.22f); src = "flick"; }
                else { R += V(0, 0.02f, -S.recoil) * rr; if (L.HasValue) L = L.Value + V(0, 0.02f, -S.recoil) * rr; wristR = Wrist(-0.5f * rr, 0, 0); src = "recoil"; }
            }
            // bow: the drawing hand comes back to the cheek with the charge, snaps forward on release
            if (S.grip == Grip.Bow)
            {
                float draw = a.charging ? Mathf.Min(1, (float)a.charge) : 0, rel = atk < 0.25f ? Bump(atk / 0.25f) : 0;
                R = Vector3.Lerp(R, V(0.1f, -0.06f, 0.08f), Smooth(draw));
                R += V(0.04f, 0.02f, -0.05f) * rel;
                if (draw > 0.01f) src = "draw"; else if (rel > 0) src = "release";
            }
            // beams: both palms / focus forward and steady (flame, heal beams)
            if (a.beamOn || a.flameOn)
            {
                float sh = a.flameOn ? 0.006f : 0.002f;
                var j = V((Random.value - 0.5f) * sh, (Random.value - 0.5f) * sh, 0);
                if (S.grip == Grip.Fists) { R = V(0.08f, -0.14f, 0.46f) + j; L = V(-0.08f, -0.14f, 0.46f) + j; }
                else if (L.HasValue) L = V(-0.06f, -0.12f, 0.5f) + j;
                src = "beam";
            }
            // abilities: a two-handed gesture toward the aim (ults reach higher)
            bool own = heroId == "tomoe" && (c.castId == "crescent" || c.castId == "recall" || c.castId == "reaping" || c.castId == "tide");
            if (cast < 0.6f && !string.IsNullOrEmpty(c.castId) && !own)
            {
                float k = Bump(cast / 0.6f); bool ult = c.castId == a.def.ult.id;
                R = Vector3.Lerp(R, V(0.1f, ult ? 0.02f : -0.08f, 0.5f), k);
                if (L.HasValue) L = Vector3.Lerp(L.Value, V(-0.1f, ult ? 0.02f : -0.08f, 0.5f), k); else L = V(-0.1f, -0.08f, 0.5f);
                src = ult ? "ult" : "ability";
            }
            // Tomoe: the Fang leaves the left hand in an overhand throw and is called back palm-out; the great axe is heaved
            // across the screen from high right to low left for Crescent Reaping, and held out in front through the Warpath
            string axe = null; float spinPh = 0;
            if (heroId == "tomoe" && S.L.HasValue)
            {
                string id = c.castId; float fang = (float)a.Sv("fang", 0), fs = t - (float)a.Sv("fangAt", -9);
                if (id == "crescent" && cast < 0.45f && fang != 4)
                {
                    float u = cast / 0.45f; var back = V(-0.32f, 0.06f, 0.12f); var outp = V(-0.02f, -0.06f, 0.62f);
                    L = u < 0.35f ? Vector3.Lerp(S.L.Value, back, Smooth(u / 0.35f)) : Vector3.Lerp(back, outp, Smooth((u - 0.35f) / 0.25f));
                    src = "throw";
                }
                else if (fang == 4) { L = Vector3.Lerp(S.L.Value, V(-0.1f, -0.03f, 0.56f), Smooth(Mathf.Min(1, fs / 0.15f))); wristL = Wrist(-1.2f, 0, 0.4f); src = "recall"; }
                else if (fang > 0) { L = Vector3.Lerp(V(-0.02f, -0.06f, 0.62f), S.L.Value, Smooth(Mathf.Max(0, (cast - 0.45f) / 0.3f))); src = "thrown"; }
                else if (fs < 0.25f && id == "recall") { L = S.L.Value + V(0, -0.02f, -0.05f) * Bump(fs / 0.25f); src = "catch"; }
                if (id == "reaping" && cast < REAP_SECS + REAP_STOP)
                {
                    // the cleave: a beat of anticipation with the axe loaded high right, the fast swing down across the view, a
                    // hit-stop as it crosses the target, then the recovery
                    float u = ReapPhase(cast); var hi = V(0.3f, 0.14f, 0.44f); var lo = V(-0.34f, -0.36f, 0.52f); var home = V(0.2f, -0.22f, 0.44f);
                    R = u < 0.3f ? Vector3.Lerp(home, hi, Smooth(u / 0.3f)) : u < 0.8f ? Vector3.Lerp(hi, lo, Smooth((u - 0.3f) / 0.32f)) : Vector3.Lerp(lo, home, Smooth((u - 0.8f) / 0.2f));
                    L = R + V(-0.06f, -0.1f, -0.04f); axe = "cleave"; src = "cleave";
                }
                else if (a.forced != null && a.forced.kind == "tide")
                {
                    // Crescent Warpath in first person: the camera doesn't spin - the great axe sweeps round her in flat
                    // circles, crossing the view from right to left once a turn, the Fang in the left hand half a turn behind
                    float t0 = (float)a.Sv("tideT0", c.castAt), dur = Mathf.Max(0.2f, (float)a.Sv("tideDur", 1.2));
                    float ph = (t - t0) / dur * 2 * 2 * Mathf.PI;
                    R = V(0.3f * Mathf.Cos(ph), -0.2f + 0.03f * Mathf.Sin(ph * 2), 0.42f + 0.16f * Mathf.Sin(ph));
                    L = V(0.26f * Mathf.Cos(ph + Mathf.PI), -0.24f + 0.03f * Mathf.Sin(ph * 2 + 1), 0.4f + 0.14f * Mathf.Sin(ph + Mathf.PI));
                    axe = "spin"; spinPh = ph; src = "warpath";
                }
            }
            // Stellar Rebirth: the right hand rises open through the middle of the view to the top of the frame, the left drops
            if (heroId == "mirei" && a.sv.TryGetValue("rebirthAt", out var rba) && t - rba < 1.3)
            {
                float u = t - (float)rba; var lo = V(0.06f, -0.14f, 0.4f); var hi = V(0.05f, 0.2f, 0.34f);
                R = u < 0.3f ? Vector3.Lerp(S.R, lo, Smooth(u / 0.3f)) : u < 1.0f ? Vector3.Lerp(lo, hi, Smooth((u - 0.3f) / 0.7f)) : Vector3.Lerp(hi, S.R, Smooth((u - 1.0f) / 0.3f));
                L = Vector3.Lerp(S.L ?? S.R, V(-0.3f, -0.34f, 0.3f), Smooth(Mathf.Min(1, u / 0.25f)) * (u < 1.0f ? 1 : Mathf.Max(0, 1 - (u - 1.0f) / 0.3f)));
                wristR = Wrist(-1.3f, 0, 0);
                src = "rebirth";
            }
            // hit flinch
            float hit = t - (float)c.hitAt;
            if (hit < 0.25f) { float k = Bump(hit / 0.25f) * 0.02f; R += V(0, k, -k); if (L.HasValue) L = L.Value + V(0, k, -k); }
            // the arms onto their targets (model space)
            Vector3? hL = L.HasValue ? ToM(L.Value) : (Vector3?)null; var hR = ToM(R);
            rig.SolveArm(0, hL, wristL); rig.SolveArm(1, hR, wristR);
            if (held != null)
            {
                // Tomoe's axe along the grip (the Fang hidden while the axe is out: HeldRig.UpdateState); twin chainguns
                // converge on a point well past the reticle (hip-held guns never follow the bent forearms)
                if (axe == "spin") held.PlaceProp(hR, new Vector3(Mathf.Cos(spinPh), 0.12f, Mathf.Sin(spinPh)).normalized, Vector3.up);
                else if (axe != null) held.PlaceProp(hR, (hR - hL.Value).normalized, axe == "cleave" ? new Vector3(-1, -0.3f, 0) : Vector3.forward);
                else if (held.prop != null) held.PlacePropAtRest();
                if (S.grip == Grip.Dual) held.gunAim = ToM(V(0, 0, 14));
                held.Place();
            }
            source = "proc:" + src;
        }

        // ---------------- Tenkai-Oh's rocket hammer in first person, after Reinhardt's viewmodel (studied frame by frame from
        // Overwatch 2 footage): at rest the gauntlets hold the haft low right and the head rests right of centre; a swing drops
        // the head back past the bottom-right corner (~0.13 s), whips it flat across the upper middle - over the reticle for a
        // couple of frames - and follows through off the left edge (the hit lands 0.24 s in, 0.96 s a swing). Swings alternate:
        // the next one comes back across from the left; with no follow-up the hammer rises back from the bottom left into the
        // rest. A key: [time s, grip x, y, z (the lower, right hand; view metres), haft yaw (+ = right), haft pitch, lead (left)
        // hand distance up the haft (m)] - the lead hand slides up the haft on the wind-up.
        static readonly float[] HAMMER_REST = { 0, 0.2f, -0.27f, 0.5f, 0.7f, 0, 0.2f };
        static readonly float[] HOLD_L = { -0.44f, -0.44f, 0.3f, -1.9f, -0.12f, 0.2f };
        static float[] K(float t, params float[] v) { var k = new float[7]; k[0] = t; for (int i = 0; i < 6; i++) k[i + 1] = v[i]; return k; }
        static float[] Rest(float t) => K(t, HAMMER_REST[1], HAMMER_REST[2], HAMMER_REST[3], HAMMER_REST[4], HAMMER_REST[5], HAMMER_REST[6]);
        /// <summary>swing 1, right to left (counter-clockwise, as Reinhardt's first swing)</summary>
        static readonly float[][] SWING_RL = { HAMMER_REST, K(0.065f, 0.27f, -0.32f, 0.42f, 1.2f, -0.14f, 0.22f), K(0.13f, 0.3f, -0.36f, 0.32f, 1.75f, -0.28f, 0.24f),
            K(0.2f, 0.2f, -0.27f, 0.4f, 0.85f, 0.24f, 0.2f), K(0.245f, 0.02f, -0.26f, 0.43f, 0, 0.33f, 0.18f), K(0.29f, -0.2f, -0.29f, 0.39f, -1.0f, 0.24f, 0.18f),
            K(0.4f, -0.42f, -0.42f, 0.3f, -1.85f, -0.08f, 0.2f), K(0.96f, HOLD_L) };
        /// <summary>swing 2, left to right (clockwise), from the left hold back across into the rest</summary>
        static readonly float[][] SWING_LR = { K(0, HOLD_L), K(0.13f, -0.45f, -0.42f, 0.28f, -2.05f, -0.22f, 0.24f), K(0.2f, -0.22f, -0.28f, 0.38f, -0.85f, 0.24f, 0.2f),
            K(0.245f, 0, -0.26f, 0.43f, 0, 0.33f, 0.18f), K(0.29f, 0.22f, -0.27f, 0.4f, 1.0f, 0.24f, 0.18f), K(0.42f, 0.32f, -0.33f, 0.34f, 1.55f, 0.02f, 0.2f), Rest(0.75f) };
        /// <summary>no follow-up swing: back up from the bottom left, head low across the bottom, into the rest</summary>
        static readonly float[][] RECOVER = { K(0.96f, HOLD_L), K(1.12f, -0.12f, -0.46f, 0.36f, -0.5f, -0.55f, 0.2f), Rest(1.34f) };
        /// <summary>Solar Shatter (E): heaved up out of the top of the frame, brought over the top and driven down in front</summary>
        static readonly float[][] SHATTER = { HAMMER_REST, K(0.18f, 0.1f, -0.46f, 0.5f, 0.35f, 0.85f, 0.08f), K(0.32f, 0.06f, -0.46f, 0.48f, 0.1f, 1.2f, 0.08f),
            K(0.45f, 0.05f, -0.47f, 0.46f, 0.05f, 1.3f, 0.08f), K(0.55f, 0.02f, -0.42f, 0.47f, 0, -0.08f, 0.16f), K(0.78f, 0.02f, -0.42f, 0.46f, 0, -0.06f, 0.16f), Rest(1.1f) };
        /// <summary>quick melee: a short thrust of the head</summary>
        static readonly float[][] JAB = { HAMMER_REST, K(0.06f, 0.08f, -0.18f, 0.56f, 0.3f, 0.2f, 0.2f), K(0.14f, 0.07f, -0.18f, 0.58f, 0.28f, 0.2f, 0.2f), Rest(0.45f) };
        /// <summary>Dawn Colossus (Q): the hammer raised high while the frame grows</summary>
        static readonly float[][] RAISE = { HAMMER_REST, K(0.25f, 0.12f, -0.12f, 0.4f, 0.3f, 1.2f, 0.24f), K(0.9f, 0.12f, -0.12f, 0.4f, 0.3f, 1.2f, 0.24f), Rest(1.3f) };
        /// <summary>the pose at t: Catmull-Rom through the keys, so a sweep keeps its speed through them instead of stopping at each</summary>
        static float[] HammerAt(float[][] Ks, float t)
        {
            int n = Ks.Length - 1;
            if (t <= Ks[0][0]) return Ks[0];
            if (t >= Ks[n][0]) return Ks[n];
            int i = 0; while (i < n - 1 && t > Ks[i + 1][0]) i++;
            var p0 = Ks[Mathf.Max(0, i - 1)]; var p1 = Ks[i]; var p2 = Ks[i + 1]; var p3 = Ks[Mathf.Min(n, i + 2)];
            float u = (t - p1[0]) / (p2[0] - p1[0]), u2 = u * u, u3 = u2 * u;
            var o = new float[7]; o[0] = t;
            for (int j = 1; j < 7; j++) o[j] = 0.5f * (2 * p1[j] + (p2[j] - p0[j]) * u + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * u2 + (3 * p1[j] - p0[j] - 3 * p2[j] + p3[j]) * u3);
            return o;
        }
        static Vector3 HaftDir(float[] k) => new Vector3(Mathf.Sin(k[4]) * Mathf.Cos(k[5]), Mathf.Sin(k[5]), Mathf.Cos(k[4]) * Mathf.Cos(k[5]));
        /// <summary>a closed gauntlet's grip centre past the wrist bone, in forearm lengths</summary>
        const float FIST = 0.32f;
        /// <summary>Style.gauntlets: where each rigid forearm points back to (view metres: its shoulder, below and beside the lens) [L, R]</summary>
        static readonly Vector3[] GAUNT_SHOULDER = { new Vector3(-0.34f, -0.5f, 0), new Vector3(0.34f, -0.5f, 0) };
        (double at, int dir) hSwing = (-99, 1);
        Transform[] gaunt;

        void HammerProc(Actor a, float t)
        {
            var c = a.anim; string kind = c.attackKind; float atk = t - (float)c.attackAt, cast = t - (float)c.castAt;
            // a new swing goes right to left - unless the last went right to left and is still held off the left edge: back across
            if (kind == "primary" && c.attackAt != hSwing.at && atk < 0.5f)
            {
                bool heldL = hSwing.dir == 1 && c.attackAt - hSwing.at < RECOVER[1][0];
                hSwing = (c.attackAt, heldL ? -1 : 1);
            }
            float sa = t - (float)hSwing.at;
            var k = HAMMER_REST; string src = "idle"; int dir = 1;
            if (sa < 1.4f)
            {
                dir = hSwing.dir;
                k = dir == 1 ? HammerAt(sa < 0.96f ? SWING_RL : RECOVER, sa) : HammerAt(SWING_LR, sa);
                src = sa < 0.45f ? "swing" : "recover";
            }
            if (c.castId == "shatter" && cast < 1.1f) { k = HammerAt(SHATTER, cast); src = "shatter"; }
            else if (c.castId == a.def.ult.id && cast < 1.3f) { k = HammerAt(RAISE, cast); src = "ult"; }
            if (kind == "punch" && atk < 0.45f) { k = HammerAt(JAB, atk); src = "melee"; }
            // breathing, and a flinch when hit
            float hit = t - (float)c.hitAt, fl = hit < 0.25f ? Bump(hit / 0.25f) * 0.02f : 0;
            var g = new Vector3(k[1], k[2] + Mathf.Sin(t * 1.7f) * 0.004f + fl, k[3] - fl); var H = HaftDir(k);
            // the head's striking face leads the motion (the swing's tangent)
            var side = src == "shatter" ? new Vector3(Mathf.Sin(k[4]) * Mathf.Sin(k[5]), -Mathf.Cos(k[5]), Mathf.Cos(k[4]) * Mathf.Sin(k[5])) : new Vector3(-Mathf.Cos(k[4]) * dir, 0, Mathf.Sin(k[4]) * dir);
            var gL = g + H * k[6];
            rig.SolveArm(0, ToM(gL), null); rig.SolveArm(1, ToM(g), null);
            held.PlaceProp(ToM(g), H, side);
            // dedicated first-person gauntlets sit on the haft by construction; the rig's own hands are locked on after the IK
            if (gaunt != null) PlaceGauntlets(g, gL, H);
            else HaftThroughFists(H, side);
            held.Place();
            source = "proc:" + src;
        }

        /// <summary>Style.gauntlets: rigid forearms + fists for a rig whose arms can't carry its hands (Resources/ZUProps/<name>,
        /// nodes gauntlet_L / gauntlet_R); the rig's skinned body is hidden once they're in</summary>
        void LoadGauntlets()
        {
            var pf = Resources.Load<GameObject>("ZUProps/" + style.gauntlets);
            if (pf == null) return;
            var go = Instantiate(pf, model.transform);
            go.name = "gauntlets";
            Transform Find(string n) { foreach (var tr in go.GetComponentsInChildren<Transform>(true)) if (tr.name == n) return tr; return null; }
            var L = Find("gauntlet_L"); var R = Find("gauntlet_R");
            if (L == null || R == null) { Destroy(go); return; }
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = LAYER;
            foreach (var rd in go.GetComponentsInChildren<Renderer>(true)) rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // PropImport normalised the prefab to 1 m tall ("model" scaled by k): undo that, and put the gauntlets in the rig's
            // own units (the GLB was built in the hero model's space, which the prefab scales to its true height)
            var mn = go.transform.Find("model"); float k = mn != null ? mn.localScale.x : 1;
            var rigT = model.transform.Find("rig"); float rs = rigT != null ? rigT.localScale.x : 1;
            L.SetParent(model.transform, true); R.SetParent(model.transform, true);
            L.localScale = L.localScale / k * rs; R.localScale = R.localScale / k * rs;
            gaunt = new[] { L, R };
            foreach (var s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.enabled = false;
        }

        /// <summary>each rigid gauntlet's fist round the haft at its grip, the forearm rising back toward its own shoulder below the lens</summary>
        void PlaceGauntlets(Vector3 gR, Vector3 gL, Vector3 H)
        {
            var Hm = H.normalized;
            for (int i = 0; i < 2; i++)
            {
                var o = gaunt[i]; var p = ToM(i == 0 ? gL : gR);
                var f = ToM(GAUNT_SHOULDER[i]) - p;
                f = (f - Hm * Vector3.Dot(f, Hm)).normalized;                 // the forearm square to the haft
                o.localPosition = p;
                o.localRotation = Quaternion.LookRotation(Hm, f);              // +Z along the haft (the grip tunnel), +Y up the forearm
            }
        }

        /// <summary>the haft through both closed fists as the arms actually solved (the IK falls a little short at the extremes):
        /// fist centres FIST forearm-lengths past the wrists, the pommel under the right fist, the haft on through the left</summary>
        void HaftThroughFists(Vector3 want, Vector3 side)
        {
            if (!rig.Has("hand_R") || !rig.Has("hand_L") || !rig.Has("forearm_R") || !rig.Has("forearm_L")) return;
            float lf = Vector3.Distance(rig.rest["forearm_R"].p, rig.rest["hand_R"].p);
            Vector3 Fist(string h, string f) { var ph = rig.Pos(h); var pf = rig.Pos(f); return ph + (ph - pf).normalized * lf * FIST; }
            var cR = Fist("hand_R", "forearm_R"); var cL = Fist("hand_L", "forearm_L");
            var H = (cL - cR).normalized * 0.65f + want.normalized * 0.35f;     // a short lead-hand gap swings the haft a lot per cm of IK error: lean on the authored direction
            held.PlaceProp(cR, H.normalized, side);
        }
    }
}
