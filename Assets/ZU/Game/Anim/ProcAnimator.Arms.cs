// ProcAnimator: the TS Animator.update's arms, held hammer, wings and whole-body tilt sections (TS frame, see ProcAnimator.cs).
using System;
using UnityEngine;

namespace ZU.Game.Anim
{
    public sealed partial class ProcAnimator
    {
        struct Over { public Vector3 hand; public float w; public Vector3? pole; public Over(Vector3 hand, float w, Vector3? pole = null) { this.hand = hand; this.w = w; this.pole = pole; } }
        /// <summary>the view carries a hammer-frame prop (Tenkai-Oh's hammer, Tomoe's axe): the held-hammer section runs</summary>
        public bool hasProp;

        void Arms(AnimState s, ClipOut L, float cw, Quaternion Dc, Vector3 hipsOff, HState hs, bool charging, bool heavy, bool flyer, float run, float speed, float idleW,
            bool archer, float shotAge, HeldSpec carry, float skSway, Func<int, float> SphOf)
        {
            var R = rest;
            float armSwing = Mathf.Sin(phase * 2 * Mathf.PI) * (heavy ? 0.25f : 0.4f) * moveBlend * Mathf.Min(1, speed / 5 + 0.3f);
            var aimDir = new Vector3(0, Mathf.Sin(s.pitch), Mathf.Cos(s.pitch)).normalized;
            // a tilted body (a swoop) still aims where the camera looks: undo the whole-body tilt on the aim line
            if (Mathf.Abs(tiltPitch) + Mathf.Abs(tiltRoll) > 1e-3f) aimDir = Quaternion.Inverse(Rot(X, tiltPitch) * Rot(Z, tiltRoll)) * aimDir;
            float armAct = L != null ? L.armsAction * cw : 0;
            for (int i = 0; i < 2; i++)
            {
                string S = i == 0 ? "L" : "R"; float side = i == 0 ? 1 : -1;
                string ua = "upperarm_" + S, fa = "forearm_" + S, hn = "hand_" + S;
                if (!Has(ua) || !Has(fa)) continue;
                var cu = L != null ? PoseDir(ua, fa) : null;
                var cl = L != null ? PoseDir(fa, hn) : null;
                if (carry != null && carry.chains) { gunOrbit[i] = null; chainExt[i] = 0; }     // the blade back in the fist unless a branch below flings it
                var shoulder = Dc * (R[ua].p - R["chest"].p) + R["chest"].p + hipsOff;
                float l1 = Vector3.Distance(R[ua].p, R[fa].p), l2 = Vector3.Distance(R[fa].p, (R.TryGetValue(hn, out var rh) ? rh : R[fa]).p);
                if (l2 <= 0) l2 = l1;
                // relaxed pose: rest direction pulled 30% toward straight down, swung with the gait
                var restD = Dc * R[ua].dir;
                var relaxed = Lerp(restD, new Vector3(side * 0.25f, -1, 0.05f), s.angel ? 0.1f : flyer && s.flying ? 0.05f : 0.3f).normalized;
                relaxed = Rot((Dc * new Vector3(side, 0, 0)).normalized, -armSwing * side * (1 - skW)) * relaxed;
                // the speed tuck: arms swept back along the body
                if (tuck > 0.01f) relaxed = Lerp(relaxed, (Dc * new Vector3(side * 0.22f, -0.5f, -0.84f)).normalized, tuck * 0.85f).normalized;
                if (skW > 0.01f && i == 0)
                {
                    float sw = Mathf.Sin(2 * Mathf.PI * SphOf(1) + 0.19f);            // forward while the right leg pushes
                    relaxed = Rot((Dc * X).normalized, -sw * 0.85f * skW) * relaxed;
                    relaxed = Rot(Y, -sw * 0.35f * skW) * relaxed;
                }
                if (flyer && s.flying) relaxed = Rot(X, -0.3f * flyBlend) * relaxed;
                if (s.angel && (s.flying || s.gliding)) relaxed = Lerp(relaxed, new Vector3(side * 0.55f, -0.8f, -0.15f), 0.35f * Mathf.Max(flyBlend, s.gliding ? 1 : 0)).normalized;
                // weapon-ready stance: elbows forward, hands up in front of the body; relaxes into arm swing at full sprint
                float rW = (1 - 0.7f * run) * (1 - airBlend * 0.5f) * (heavy ? 0.45f : 0.72f) * (flyer && s.flying ? 0.35f : 1) * (s.angel ? 0.12f : 1);
                relaxed = Lerp(relaxed, (Dc * new Vector3(side * 0.3f, -0.72f, 0.62f)).normalized, rW).normalized;
                readyW = rW;
                // overrides: the hammer's grip (both hands, or the right one while the left is busy) and every hero's own pose
                Over? over = null;
                bool leftFree = s.move == "shatter" || s.move == "reaping" ? false : (s.move == "tide" || s.barrier || (cast > 0.05f && s.move != "dawncharge") || punchW > 0.01f || charging);
                if (hs != null && s.move != "tide" && (i == 1 || !leftFree))
                {
                    float HH = height;
                    var Sh = Has("upperarm_L") && Has("upperarm_R") ? (R["upperarm_L"].p + R["upperarm_R"].p) * 0.5f + hipsOff : R["chest"].p + hipsOff;
                    var dirH = new Vector3(Mathf.Sin(hs.th), 0, Mathf.Cos(hs.th));
                    var Hd = new Vector3(Mathf.Sin(hs.th) * Mathf.Cos(hs.ph), Mathf.Sin(hs.ph), Mathf.Cos(hs.th) * Mathf.Cos(hs.ph));
                    var G = Sh + new Vector3(0, hs.gy * HH, 0) + dirH * (hs.d * armLen);
                    var hand = i == 1 ? G : G + Hd * (0.12f * hammerLen);
                    // the off hand lets go at the extremes (grip behind its shoulder or out of reach); it rejoins as the hammer comes round
                    float wh = 1;
                    if (i == 0)
                    {
                        var rel = hand - shoulder; float reach = l1 + l2;
                        wh = Mathf.Clamp01(1 - (rel.magnitude / reach - 1.0f) * 4) * Mathf.Clamp01(rel.z / (0.35f * reach) + 0.6f);
                    }
                    over = new Over(hand, wh);
                    if (i == 1) { gripG = G; gripH = Hd; gripT = new Vector3(Mathf.Cos(hs.th), 0, -Mathf.Sin(hs.th)) * hs.side; }
                }
                else if (s.rebirth.HasValue && s.rebirth < 2.4f)
                {
                    // Stellar Rebirth: the left hand reaches out and down, palm up, toward the souls; then sweeps up over her head
                    // as the weapon hand rises too; both arms held in a V for a second, then they settle
                    float r = s.rebirth.Value, Lr = l1 + l2;
                    float K(float a, float b) => Mathf.Clamp01((r - a) / (b - a));
                    Vector3 V(float x, float y, float z) => shoulder + Dc * (new Vector3(side * x, y, z) * Lr);
                    Vector3 downV = i == 0 ? V(0.55f, -0.62f, 0.62f) : V(0.3f, -0.75f, 0.3f), up = V(i == 0 ? 0.5f : 0.62f, 0.86f, 0.1f);
                    var rel = relaxed * Lr + shoulder;
                    var hand = r < 0.4f ? Lerp(rel, downV, Smooth(K(0, 0.4f)))
                        : r < 0.85f ? Lerp(downV, up, Smooth(K(0.4f, 0.85f)))
                        : r < 1.9f ? up + new Vector3(0, Mathf.Sin(s.time * 2.2f + i) * 0.02f * Lr, 0)
                        : Lerp(up, rel, Smooth(K(1.9f, 2.4f)));
                    over = new Over(hand, 1, new Vector3(side * 0.9f, r > 0.6f ? 0.3f : -0.6f, 0.4f));
                }
                else if (s.skyward)
                {
                    // the Storm Sovereign hurling the thunderbolt: the free hand open to the sky; the sword arm raised while the storm
                    // gathers, snapped down at the foe as each bolt lands, held through the follow-through, wound back up
                    float Lr = l1 + l2;
                    Vector3 At(float x, float y, float z) => shoulder + Dc * (new Vector3(side * x, y, z).normalized * (Lr * 0.98f));
                    float k = s.skyStrike;
                    float hurl = k < 0 ? 0 : k < 0.08f ? Smooth(k / 0.08f) : k < 0.3f ? 1 : 1 - Smooth(Mathf.Clamp01((k - 0.3f) / 0.45f));
                    var hand = i == 1
                        ? Lerp(At(0.16f + 0.03f * Mathf.Sin(s.time * 31) * (1 - hurl), 1, -0.06f), At(0.12f, 0.05f, 1), hurl)
                        : Lerp(At(0.45f + 0.04f * Mathf.Sin(s.time * 1.3f), 0.92f, 0.12f), At(0.6f, 0.5f, 0.35f), hurl * 0.6f);
                    float raise = Smooth(Mathf.Clamp01((s.skyRise ?? 1) / 0.5f));
                    over = new Over(Lerp(relaxed * Lr + shoulder, hand, raise), 1, new Vector3(side * 0.9f, 0.1f, i == 1 ? 0.5f - 0.6f * hurl : 0.5f));
                }
                else if (i == 1 && s.move == "tide")
                {
                    // Crescent Warpath: the right arm up as the hub the weapons wheel round; the left swings with the run
                    over = new Over(shoulder + Dc * (new Vector3(side * 0.12f, 0.8f, 0.28f) * (l1 + l2)), orbitW, new Vector3(side * 0.8f, 0.2f, 0.9f));
                }
                else if (i == 0 && charging)
                {
                    over = new Over(shoulder + new Vector3(-0.15f, 0.05f, 0.75f) * (l1 + l2), 1);
                }
                else if (i == 0 && punchW > 0.01f && armAct < 0.3f)
                {
                    var hand = shoulder + aimDir * ((l1 + l2) * (0.35f + 0.63f * Mathf.Max(punchExt, -0.35f)));
                    hand.x += -side * (l1 + l2) * 0.12f; hand.y -= 0.05f * (l1 + l2);
                    over = new Over(hand, punchW);
                }
                else if (i == 1 && s.hero == "hayate" && !s.melee && guardW < 0.02f && (s.attackKind == "primary" || s.attackKind == "secondary") && s.attackAge < 0.5f)
                {
                    // Hayate's throws (Genji's): a shuriken is a short straight thrust of the throwing arm along the aim from the hip,
                    // snapped back for the next; the fan loads across the body and whips out level to his own side
                    float t = s.attackAge, Lr = l1 + l2;
                    Vector3 At(float x, float y, float z) => shoulder + Dc * (new Vector3(side * x, y, z) * Lr);
                    var home = At(0.25f, -0.5f, 0.35f);
                    Vector3 hand; float w = 1;
                    if (s.attackKind == "secondary")
                    {
                        Vector3 load = At(-0.45f, -0.35f, 0.55f), rel = At(0.7f, -0.2f, 0.62f);
                        hand = t < 0.08f ? Lerp(home, load, Smooth(t / 0.08f)) : t < 0.26f ? Lerp(load, rel, Smooth((t - 0.08f) / 0.18f)) : Lerp(rel, home, Smooth((t - 0.26f) / 0.24f));
                        if (t >= 0.26f) w = 1 - 0.6f * Smooth((t - 0.26f) / 0.24f);
                    }
                    else
                    {
                        var outP = shoulder + aimDir * (0.95f * Lr) + new Vector3(side * 0.05f * Lr, -0.08f * Lr, 0);
                        float k = t < 0.1f ? Smooth(t / 0.1f) : 1 - Smooth(Mathf.Min(1, (t - 0.1f) / 0.3f));
                        hand = Lerp(home, outP, k); w = Mathf.Max(0.4f, k);
                    }
                    over = new Over(hand, w, new Vector3(side * 0.7f, -0.6f, -0.4f));
                }
                else if (carry != null && carry.chains && s.attackKind == "primary" && s.attackAge < CB_SWING && i == (s.swingSide > 0 ? 1 : 0))
                {
                    // Hellfire Chains, a light swing (Kratos' Blades of Chaos): the swinging hand winds back to its own side, whips
                    // through a wide flat arc across the front at the chain's full length, and recovers low across the body
                    float t = s.attackAge, Lr = l1 + l2;
                    Vector3 At(float phi, float rr, float y) => shoulder + Dc * (new Vector3(Mathf.Sin(phi) * rr, y, Mathf.Cos(phi) * rr) * Lr);
                    float phi0 = side * 1.9f, phi1 = -side * 1.25f;
                    Vector3 hand; float w = 1;
                    if (t < CB_WIND) { float u = Smooth(t / CB_WIND); hand = At(phi0, 0.55f + 0.3f * u, 0.02f + 0.13f * u); w = u; }
                    else if (t < CB_WIND + CB_ARC) { float u = Smooth((t - CB_WIND) / CB_ARC); hand = At(phi0 + (phi1 - phi0) * u, 0.97f, 0.15f - 0.3f * u); }
                    else { float u = Smooth((t - CB_WIND - CB_ARC) / (CB_SWING - CB_WIND - CB_ARC)); hand = At(phi1 + (-side * 0.5f - phi1) * u, 0.97f - 0.4f * u, -0.15f - 0.2f * u); w = 1 - u; }
                    over = new Over(hand, w, new Vector3(side * 0.8f, -0.3f, -0.3f));
                    // ...and the blade leaves the fist on its chain: out round him to the chain's full length, a little ahead of the
                    // hand's bearing with its edge along the arc, hauled back in the recover
                    float ext = SwingExt(t), reach = CB_REACH / Mathf.Max(1e-6f, s.scale), phiB = SwingPhi(t, side);
                    float rB = 0.97f * Lr + (reach * 0.82f - 0.97f * Lr) * ext;
                    var pB = shoulder + Dc * new Vector3(Mathf.Sin(phiB) * rB, (0.12f - 0.3f * SwingArc(t)) * Lr, Mathf.Cos(phiB) * rB);
                    var tan = Dc * (new Vector3(Mathf.Cos(phiB), 0, -Mathf.Sin(phiB)) * -side);   // along the arc, toward the far side
                    gunOrbit[i] = (pB, tan, Y, Mathf.Min(1, ext * 1.6f));
                    chainExt[i] = ext;
                }
                else if (carry != null && carry.chains && i == 1 && s.attackKind == "secondary" && s.attackAge < CB_THROW)
                {
                    // ...and the Chain Throw: the right blade drawn back over the shoulder, shot straight out along the aim to the
                    // chain's full length, held taut a beat, and yanked back
                    float t = s.attackAge, Lr = l1 + l2;
                    var back = shoulder + Dc * (new Vector3(side * 0.3f, 0.55f, -0.35f) * Lr);
                    var outP = shoulder + aimDir * (Lr * 0.99f); outP.x += -side * 0.08f * Lr;
                    Vector3 hand; float w = 1;
                    if (t < 0.08f) { hand = back; w = Smooth(t / 0.08f); }
                    else if (t < 0.25f) hand = Lerp(back, outP, Smooth((t - 0.08f) / 0.17f));
                    else if (t < 0.34f) hand = outP;
                    else { hand = outP; w = 1 - Smooth((t - 0.34f) / (CB_THROW - 0.34f)); }
                    over = new Over(hand, w, new Vector3(side * 0.6f, 0.4f, -0.2f));
                    // the blade shot out along the aim, spinning once on the way, held taut a beat, spinning back
                    float ext = ThrowExt(t), reach = CB_THROW_REACH / Mathf.Max(1e-6f, s.scale), spin = ThrowSpin(t) * Mathf.PI * 2;
                    var pB = outP + aimDir * (Mathf.Max(0, reach * 0.9f - Lr) * ext);
                    var ax = Vector3.Cross(Y, aimDir); if (ax.sqrMagnitude < 1e-6f) ax = X; ax.Normalize();
                    var qs = Rot(ax, spin);
                    gunOrbit[i] = (pB, qs * aimDir, qs * Y, Mathf.Min(1, ext * 3));
                    chainExt[i] = ext;
                }
                else if (s.climb)
                {
                    // up the wall hand over hand: each hand reaches high on the wall in turn, pulls down past the shoulder
                    float Lr = l1 + l2, ph = s.time * 7 + (i == 0 ? 0 : Mathf.PI);
                    float reachK = 0.5f + 0.5f * Mathf.Sin(ph);
                    over = new Over(shoulder + Dc * new Vector3(side * 0.28f * Lr, (-0.1f + 0.95f * reachK) * Lr, 0.62f * Lr), 1, new Vector3(side * 0.8f, -0.6f, -0.2f));
                }
                else if (archer && drawW > 0.02f)
                {
                    // the draw: the bow arm straight out along the aim, the string hand pulled back to the cheek; on the release the
                    // string hand snaps back and open, reaches over the shoulder to the quiver, nocks the next arrow
                    float Lr = l1 + l2;
                    var face = (R.TryGetValue("neck", out var rn) ? rn.p : R["chest"].p) + hipsOff + new Vector3(0, 0.06f * height, 0);
                    var bowHand = shoulder;
                    if (i == 0)
                    {
                        var hand = bowHand + aimDir * (Lr * 0.97f);
                        bowHandM = hand;
                        over = new Over(hand, drawW, new Vector3(side * 0.6f, -0.8f, 0));
                    }
                    else
                    {
                        var anchor = face + aimDir * (0.05f * height) + new Vector3(-side * 0.05f * height, 0, 0);
                        var nock = bowHandM + aimDir * (-0.12f * Lr) + new Vector3(-side * 0.03f * Lr, 0, 0);
                        var quiver = shoulder + Dc * new Vector3(-side * 0.08f * Lr, 0.34f * Lr, -0.42f * Lr);
                        var snap = anchor + Dc * new Vector3(-side * 0.14f * Lr, 0.05f * Lr, -0.26f * Lr);
                        float K(float a, float b) => Mathf.Clamp01((shotAge - a) / (b - a));
                        Vector3 hand;
                        if (s.charging) hand = Lerp(nock, anchor, Mathf.Min(1, 0.25f + s.charge));
                        else if (shotAge < 0.1f) hand = Lerp(anchor, snap, Smooth(K(0, 0.07f)));
                        else if (shotAge < 0.34f) hand = Lerp(snap, quiver, Smooth(K(0.1f, 0.34f)));
                        else if (shotAge < 0.6f) hand = Lerp(quiver, nock, Smooth(K(0.4f, 0.6f)));
                        else hand = nock;
                        over = new Over(hand, drawW, new Vector3(side * 0.9f, 0.5f, -0.9f));
                    }
                }
                else if (guardW > 0.02f && carry?.R != null && carry.R.kind == HeldKind.Blade)
                {
                    // the deflect guard (Genji's): the blade hand in front of the chest, the blade across the body turning in a slow
                    // circle; the off hand low and forward. Every shot turned on the blade: the hand snaps out toward it and back
                    float Lr = l1 + l2, t = s.time * 5.5f;
                    var hand = i == 1
                        ? shoulder + Dc * new Vector3(side * 0.55f * Lr + Mathf.Cos(t) * 0.06f * Lr, -0.12f * Lr + Mathf.Sin(t) * 0.06f * Lr, 0.62f * Lr)
                        : shoulder + Dc * new Vector3(-side * 0.1f * Lr, -0.42f * Lr, 0.5f * Lr);
                    var df = s.deflect;
                    if (df != null && df.age < 0.16f)
                    {
                        float k = Mathf.Sin(Mathf.Min(1, df.age / 0.16f) * Mathf.PI); var outD = new Vector3(df.x, df.y, df.z).normalized;
                        hand += outD * ((i == 1 ? 0.28f : -0.08f) * Lr * k);
                    }
                    over = new Over(hand, guardW, new Vector3(side * 0.9f, -0.5f, -0.3f));
                }
                else if (s.dual.HasValue)
                {
                    // twin chainguns held at the hips, barrels along the aim; each gun kicks back on its own rounds and chatters while
                    // it fires; running they ride lower and bounce, in the rush they tuck in under the shoulders
                    float age = i == 0 ? s.dual.Value.x : s.dual.Value.y, Lr = l1 + l2, kickG = Mathf.Max(0, 1 - age / 0.06f);
                    var hand = shoulder + new Vector3(side * 0.34f * Lr, -0.44f * Lr, 0.12f * Lr) + aimDir * ((0.58f - 0.07f * kickG) * Lr);
                    if (age < 0.12f) hand += new Vector3(UnityEngine.Random.value - 0.5f, UnityEngine.Random.value - 0.5f, 0) * (0.02f * Lr);
                    hand.y -= (0.06f * run + Mathf.Abs(armSwing) * 0.05f + (s.rush ? 0.08f : 0)) * Lr;
                    if (s.rush) hand.x -= side * 0.06f * Lr;
                    // the leap: both guns hauled up overhead; the slam drives them down in front as he lands
                    if (leapW > 0.01f) hand = Lerp(hand, shoulder + Dc * new Vector3(side * 0.2f * Lr, 0.8f * Lr, 0.2f * Lr), leapW);
                    if (slamDip > 0.01f) hand = Lerp(hand, shoulder + Dc * new Vector3(side * 0.3f * Lr, -0.62f * Lr, 0.5f * Lr), Mathf.Min(1, slamDip * 1.6f));
                    over = new Over(hand, 1, new Vector3(side * 0.9f, -0.6f, -0.5f));
                }
                else if (s.angel)
                {
                    var aa = AngelArm(i, side, shoulder, l1 + l2, s, idleW, run);
                    if (aa.HasValue) over = new Over(aa.Value.hand, aa.Value.w, aa.Value.pole);
                }
                else if (carry != null && moveBlend > 0.05f)
                {
                    // a blade or a bow carried on the move: the sword hand low and back (the blade trailing), the bow hand low at the
                    // side, bow upright - instead of pumping the weapon through the arm swing; any attack or cast takes it back
                    var it = i == 0 ? carry.L : carry.R;
                    if (it != null && it.kind != HeldKind.Arrow)
                    {
                        float Lr = l1 + l2, busy = Mathf.Min(1, Mathf.Max(atk, Mathf.Max(cast, s.charging ? 1 : 0)) * 1.6f);
                        // TS-PARITY (deliberate divergence): the TS "samurai's run" carries a blade low and 0.3 arm lengths BEHIND
                        // the hip (0.3, -0.76, -0.3) - it read as "hands dangling behind" on Raijin, Hayate and Enra (the user's
                        // complaint; evera-a0's run study), so the blade hand rides at the hip line
                        var local = it.kind == HeldKind.Blade ? new Vector3(side * 0.34f, -0.78f, -0.02f) : new Vector3(side * 0.36f, -0.8f, 0.06f);
                        var hand = shoulder + Dc * (local * Lr);
                        hand.z += armSwing * side * 0.05f * Lr;
                        float w = moveBlend * (1 - busy);
                        if (w > 0.01f) over = new Over(hand, w, new Vector3(side * 0.6f, -0.4f, -0.8f));
                    }
                }
                float lead = i == 1 ? 1 : 0.35f;
                float act = (1 - armAct) * Mathf.Max(Mathf.Max(atk * lead * (s.attackKind == "secondary" && i == 0 ? 2.5f : 1), cast * 0.9f),
                    Mathf.Max(Mathf.Max(s.beam ? 0.9f * lead : 0, s.charging ? 1 * (i == 0 ? 1 : 0.8f) : 0), s.barrier && i == 0 ? 1 : 0));
                // clip arms: a one-shot owns them; idle / locomotion arms give way to the weapon guard
                float wArm = over.HasValue ? 0 : armAct + (1 - armAct) * (L != null ? L.armsLoco * cw : 0) * (1 - readyW * 0.8f) * (1 - Mathf.Min(1, act));
                Quaternion Put(string n, Vector3 dir, Vector3? c) => AimBone(n, c.HasValue && wArm > 0 ? Lerp(dir, c.Value, wArm).normalized : dir);
                if (bones.ContainsKey("shoulder_" + S)) ApplyDelta("shoulder_" + S, BlendD(Dc, Cq("shoulder_" + S), wArm));
                if (over.HasValue)
                {
                    var o = over.Value;
                    var (u, l) = Ik(shoulder, o.hand, l1, l2, o.pole ?? new Vector3(side * 0.5f, -1, -0.4f));
                    AimBone(ua, Lerp(relaxed, u, o.w).normalized);
                    AimBone(fa, Lerp(relaxed, l, o.w).normalized);
                }
                else if (act > 0.01f)
                {
                    var reach = aimDir * ((l1 + l2) * (0.72f + 0.15f * Mathf.Sin(Mathf.Min(1, act) * Mathf.PI)));
                    var hand = shoulder + reach;
                    hand.x += -side * (l1 + l2) * 0.25f;             // hands converge toward the centre line
                    if (s.attackKind == "secondary" && atk > 0) hand.x += side * Mathf.Sin(atk * Mathf.PI) * (l1 + l2) * 0.8f;   // slash arc
                    var (u, l) = Ik(shoulder, hand, l1, l2, new Vector3(side * 0.4f, -1, -0.6f));
                    float w = Mathf.Min(1, act);
                    Put(ua, Lerp(relaxed, u, w).normalized, cu);
                    Put(fa, Lerp(relaxed, l, w).normalized, cl);
                }
                else
                {
                    var Du = Put(ua, relaxed, cu);
                    // forearms bend up toward the centre line (holding the weapon / focus) in the ready stance; the angel's forearm
                    // keeps its sculpted angle to the upper arm (her feather-blades hang straight down)
                    var fore = (Dc * new Vector3(-side * 0.35f, -0.12f, 1)).normalized;
                    var lD = s.angel
                        ? Lerp(Du * R[fa].dir, fore, 0.04f + 0.08f * moveBlend).normalized
                        : Lerp(relaxed, fore, 0.18f + 0.15f * moveBlend + readyW * 0.55f).normalized;
                    Put(fa, lD, cl);
                }
                if (bones.ContainsKey(hn) && R.ContainsKey(hn))
                {
                    var Qh = CurQ(fa) * Quaternion.Inverse(R[fa].q) * R[hn].q;
                    var ch = Cq(hn);
                    if (ch.HasValue && wArm > 0)
                    {
                        // clip wrist: bend only (swing-twist about the forearm, twist dropped, bend clamped to a natural range) - auto-rigged
                        // meshes weight sleeve cuffs and held weapons to the hand, so the mocap's forearm roll would spin them
                        var fq = CurQ(fa);
                        var axis = ((fq * Quaternion.Inverse(R[fa].q)) * R[fa].dir).normalized;
                        var rel = ch.Value * R[hn].q * Quaternion.Inverse(Qh);
                        float d = rel.x * axis.x + rel.y * axis.y + rel.z * axis.z;
                        var tw = new Quaternion(axis.x * d, axis.y * d, axis.z * d, rel.w);
                        float tl = tw.x * tw.x + tw.y * tw.y + tw.z * tw.z + tw.w * tw.w;
                        tw = tl < 1e-9f ? Quaternion.identity : Quaternion.Normalize(tw);
                        var sw = rel * Quaternion.Inverse(tw);
                        float ang = 2 * Mathf.Acos(Mathf.Min(1, Mathf.Abs(sw.w)));
                        if (ang > WRIST_MAX) sw = Quaternion.Slerp(sw, Quaternion.identity, 1 - WRIST_MAX / ang);
                        Qh = Quaternion.Slerp(Quaternion.identity, sw, wArm) * Qh;
                    }
                    SetModelQ(hn, Qh);
                }
            }
        }
        const float WRIST_MAX = 0.7f;       // radians of wrist bend a clip may add on top of the forearm (~40 deg)

        /// <summary>held hammer: pommel just below the right hand, haft along the swing path, head across it; in the Crescent
        /// Warpath the axe and the Fang leave her hands and wheel round her at shoulder height</summary>
        void HeldHammer(AnimState s, HState hs, Vector3 hipsOff, float dt)
        {
            if (!hasProp) { prop = null; return; }
            if (hs == null) { prop = null; gunOrbit[0] = null; return; }
            var Hh = gripH; var T = gripT - Hh * Vector3.Dot(gripT, Hh);
            if (T.sqrMagnitude < 1e-6f) T = X;
            T.Normalize();
            var Zb = Vector3.Cross(T, Hh);
            var pos = gripG - Hh * (0.1f * hammerLen);
            var q = Quaternion.LookRotation(Zb, Hh);                 // the basis (T, H, T x H)
            float? tide = s.move == "tide" ? s.tide : null;
            orbitW += ((tide.HasValue ? 1 : 0) - orbitW) * Mathf.Min(1, dt * (tide.HasValue ? 22 : 14));
            if (tide.HasValue || orbitW > 0.01f)
            {
                float age = tide ?? 9, phi = age * TIDE_TURNS * 2 * Mathf.PI, tilt = 1.05f * Mathf.Max(0, 1 - age / 0.15f);
                var centre = rest["chest"].p + hipsOff + new Vector3(0, 0.2f * height, 0.05f * height);
                float rad = 0.62f * height; var ring = Rot(Z, tilt);
                Vector3 At(float a) => ring * new Vector3(Mathf.Sin(a) * rad, 0, Mathf.Cos(a) * rad) + centre;
                Vector3 Tangent(float a) => (ring * new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a))).normalized;
                var up = ring * Y;
                // the axe: its pommel toward the hub, the crescent head out past the ring, the edge leading round
                var pa = At(phi); var outD = (pa - centre).normalized; var pommel = pa + outD * (-0.45f * hammerLen);
                var Ta = Tangent(phi); var Za = Vector3.Cross(Ta, outD);
                pos = Lerp(pos, pommel, orbitW);
                q = Quaternion.Slerp(q, Quaternion.LookRotation(Za, outD), orbitW);
                // the Fang half a turn behind, its blade pointing in at the hub
                var pf = At(phi + Mathf.PI);
                gunOrbit[0] = (pf, (centre - pf).normalized, up, orbitW);
            }
            else gunOrbit[0] = null;
            prop = (pos, q * Y, q * X);
        }

        void Wings(AnimState s, Quaternion Dc, float speed, float dt)
        {
            if (!bones.ContainsKey("wing_L") && !bones.ContainsKey("wing_R")) return;
            if (s.angel && !PERF)
            {
                // (web edition) the original angel wings
                float fastK = Mathf.Min(1, speed / (legLen * 6)) * flyBlend, air = Mathf.Max(flyBlend, s.gliding ? 1 : 0);
                flap += dt * (s.gliding ? 0.4f : 1.1f + 0.8f * fastK);
                float beat = Mathf.Sin(flap * 2 * Mathf.PI) * (s.gliding ? 0.04f : 0.16f * (1 - fastK) + 0.06f);
                float liftK = s.gliding ? 0.38f : 0.12f * (1 - fastK) * air, sweep = 0.55f * fastK + (1 - air) * 0.12f;
                foreach (var (n, side) in new[] { ("wing_L", 1f), ("wing_R", -1f) })
                    if (bones.ContainsKey(n)) ApplyDelta(n, Dc * Rot(Z, side * (beat * air + liftK - (1 - air) * 0.06f + (1 - air) * Mathf.Sin(s.time * 1.3f) * 0.02f)) * Rot(Y, side * sweep));
            }
            else if (s.angel)
            {
                // angel wings on springs: folded back on the ground (a stretch now and then), a flare on takeoff, spread wide with
                // slow deep strokes hovering, swept hard back in a swoop, thrown forward to brake, a dart in a superjump, a V descending
                float fastK = Mathf.Min(1, speed / (legLen * 6)) * flyBlend, sw = aSwoop + aSling;
                float air = Mathf.Min(1, Mathf.Max(Mathf.Max(aHover, aGlide), Mathf.Max(Mathf.Max(sw, aSup), Mathf.Max(aFlare, airBlend)))), ground = 1 - air;
                wFidget = Mathf.Max(0, wFidget - dt);
                if (ground > 0.9f && moveBlend < 0.1f && UnityEngine.Random.value < dt / 7) wFidget = 1.1f;
                float fid = wFidget > 0 ? Mathf.Sin((1.1f - wFidget) / 1.1f * Mathf.PI) : 0;
                float jumpFlare = !s.grounded && s.jumpAge < 0.5f ? Mathf.Sin(Mathf.Min(1, s.jumpAge / 0.5f) * Mathf.PI) * (1 - aHover) : 0;
                float liftT = ground * (-0.13f + 0.26f * fid) + aHover * 0.1f + sw * 0.16f + aGlide * 0.32f + aSup * 0.42f + aFlare * 0.38f + jumpFlare * 0.3f;
                float sweepT = ground * (0.46f - 0.3f * fid + 0.04f * moveBlend) + aHover * (0.05f + 0.3f * fastK) + sw * 0.55f - aGlide * 0.06f + aSup * 0.5f - aFlare * 0.3f - jumpFlare * 0.25f;
                float liftV = Step(wLift, liftT, 85, 8, dt), sweepV = Step(wSweep, sweepT, 85, 8, dt);
                flap += dt * (0.9f * aHover + 6 * sw + 0.55f * aGlide + 1.6f * fastK * aHover + 0.3f * ground);
                stepFlutter *= Mathf.Exp(-dt * 10);
                float beat = Mathf.Sin(flap * 2 * Mathf.PI) * (0.17f * aHover * (1 - 0.5f * fastK) + 0.025f * sw + 0.05f * aGlide + 0.015f * ground) + stepFlutter;
                foreach (var (n, side) in new[] { ("wing_L", 1f), ("wing_R", -1f) })
                    if (bones.ContainsKey(n)) ApplyDelta(n, Dc * Rot(Z, side * (liftV + beat)) * Rot(Y, side * sweepV));
            }
            else
            {
                float rate = s.flying ? (s.vel.y > 1 ? 4.2f : 2.6f) : 0.8f;
                flap += dt * rate;
                float amp = s.flying ? (s.vel.y > 1 ? 0.55f : 0.35f) : 0.08f;
                float f = Mathf.Sin(flap * 2 * Mathf.PI) * amp, fold = (1 - flyBlend) * 0.35f;
                foreach (var (n, side) in new[] { ("wing_L", 1f), ("wing_R", -1f) })
                    if (bones.ContainsKey(n)) ApplyDelta(n, Dc * Rot(Z, side * (f - fold)) * Rot(Y, side * fold * 0.6f));
            }
        }

        /// <summary>whole-body tilt, applied by the view about the hips: the angel leans along a swoop, flares back to brake, rocks
        /// in the descent and banks into a hover; every flyer banks into its flight; Hibiki leans into his strokes and grinds</summary>
        void Tilt(AnimState s, float lvx, float lvz, float dt, bool flyer)
        {
            float pT = 0, rT = 0, k = 55, d = 9;
            float hs2 = Hypot(lvx, lvz) * s.scale, dx = hs2 > 0.5f ? lvx * s.scale / hs2 : 0, dz = hs2 > 0.5f ? lvz * s.scale / hs2 : 0;
            if (s.angel)
            {
                float wt = aSwoop + aSling * 0.8f;
                float along = hs2 > 0.5f ? Mathf.Min(1.2f, Mathf.Atan2(hs2, s.vel.y) * 0.8f) : 0;     // 0 = straight up
                pT = wt * along * dz - aFlare * 0.42f - aSup * 0.1f + aGlide * (-0.1f + ClampA(lvz * s.scale * 0.025f, 0.2f))
                    + aHover * (ClampA(lvz * s.scale / 9, 1) * 0.4f + ClampA(acc.z / 30, 0.25f));
                rT = -wt * along * dx + aGlide * (Mathf.Sin(s.time * 2.9f) * 0.08f - ClampA(lvx * s.scale * 0.025f, 0.2f))
                    - aHover * (ClampA(lvx * s.scale / 9, 1) * 0.35f + ClampA(acc.x / 30, 0.25f));
                if (aSwoop > 0.5f) { k = 90; d = 11; } else if (aGlide > 0.5f) { k = 40; d = 7; }
            }
            else if (flyer && s.flying) { pT = ClampA(lvz * s.scale / 9, 1) * 0.3f; rT = -ClampA(lvx * s.scale / 9, 1) * 0.3f; }
            if (s.grind != 0) { rT += -s.grind * 0.42f; pT += 0.16f; k = 70; d = 10; }
            else if (s.skate && s.grounded) { pT += ClampA(lvz * s.scale / 8, 1) * 0.14f; rT += -ClampA(lvx * s.scale / 8, 1) * 0.12f + Mathf.Sin(phase * Mathf.PI * 1.1f) * 0.05f * moveBlend; }
            if (!PERF) { pT = 0; rT = 0; }
            tiltPitch = Step(tiltP, pT, k, d, dt); tiltRoll = Step(tiltR, rT, k, d, dt);
        }
    }
}
