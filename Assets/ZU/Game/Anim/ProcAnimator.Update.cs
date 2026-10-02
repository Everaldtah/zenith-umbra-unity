// ProcAnimator.Update: the TS Animator.update, section for section (clip layer, squash & stretch, lower-body yaw, gait,
// hips, spine chain, legs, arms, held hammer, wings, whole-body tilt, sprawl). See ProcAnimator.cs for the frames.
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game.Anim
{
    public sealed partial class ProcAnimator
    {
        // ------------------------------------------------------------------------------------------------ clip layer
        /// <summary>the Animator's pose this frame as the TS ClipLayer's output: per-bone model-space deltas (TS frame), bone
        /// positions, and the region weights</summary>
        sealed class ClipOut
        {
            public bool ok; public float legs = 1, torso = 1, armsLoco = 1, armsAction, loco;
            public readonly Dictionary<string, Quaternion> q = new Dictionary<string, Quaternion>();
            public readonly Dictionary<string, Vector3> p = new Dictionary<string, Vector3>();
            public readonly float[] contact = new float[2];
        }
        readonly ClipOut clip = new ClipOut();

        /// <summary>read the Animator's pose before anything procedural is written (the TS layer.update)</summary>
        void SampleClip(Animator anim, float dt)
        {
            clip.ok = anim != null && anim.isActiveAndEnabled && anim.runtimeAnimatorController != null;
            if (!clip.ok) return;
            var inv = Quaternion.Inverse(root.rotation);
            foreach (var kv in bones)
            {
                if (!rest.TryGetValue(kv.Key, out var r)) continue;
                clip.q[kv.Key] = M(inv * kv.Value.rotation) * Quaternion.Inverse(r.q);
                clip.p[kv.Key] = M(root.InverseTransformPoint(kv.Value.position));
            }
            clip.loco = anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") ? 1 : 0;
            bool action = anim.layerCount > 1 && !anim.GetCurrentAnimatorStateInfo(1).IsName("Empty");
            clip.armsAction += ((action ? 1 : 0) - clip.armsAction) * Mathf.Min(1, dt * 14);
            clip.armsLoco = 1 - clip.armsAction;
            for (int i = 0; i < 2; i++) clip.contact[i] = clip.p.TryGetValue(i == 0 ? "foot_L" : "foot_R", out var f) && f.y - footY < 0.05f * legLen ? 1 : 0;
        }
        Quaternion? Cq(string b) => clip.ok && clip.q.TryGetValue(b, out var q) ? q : (Quaternion?)null;
        Vector3? PoseDir(string a, string b) => clip.ok && clip.p.TryGetValue(a, out var pa) && clip.p.TryGetValue(b, out var pb) ? (pb - pa).normalized : (Vector3?)null;
        static Quaternion BlendD(Quaternion D, Quaternion? q, float w) => q.HasValue && w > 0 ? Quaternion.Slerp(D, q.Value, w) : D;

        // ------------------------------------------------------------------------------------------------ update
        public void Update(AnimState s, Animator anim)
        {
            if (!ok) return;
            float dt = Mathf.Min(0.05f, s.dt);
            modelQ.Clear();
            SampleClip(anim, s.dt);
            var R = rest;
            bool heavy = s.frame == "mech", flyer = s.frame == "flyer";
            var PS = PERF ? PersonaOf(s.hero ?? "", heavy) : PERSONA_LEGACY;
            isAngel = s.angel;
            // velocity in model space (character faces +Z at its yaw)
            float cy = Mathf.Cos(s.yaw), sy = Mathf.Sin(s.yaw);
            float lvx = (s.vel.x * cy - s.vel.z * sy) / s.scale, lvz = (s.vel.x * sy + s.vel.z * cy) / s.scale;
            float speed = Hypot(lvx, lvz);
            float moving = s.grounded && speed > 0.4f && !s.rooted ? 1 : 0;
            moveBlend += (moving - moveBlend) * Mathf.Min(1, dt * 10);
            airBlend += ((s.grounded ? 0 : 1) - airBlend) * Mathf.Min(1, dt * 8);
            flyBlend += ((s.flying ? 1 : 0) - flyBlend) * Mathf.Min(1, dt * 5);
            // ---------------- clip layer: the Animator's pose, blended per body region below
            bool angelAir = PERF && s.angel && (s.swoop >= 0 || s.gliding || s.superjump || s.slingshot || s.swoopFlare < 0.4f);
            bool skating = PERF && s.skate && (moving > 0 || s.grind != 0);
            bool eligible = !heavy && s.frame != "drone" && !s.flying && !s.hammer && s.move == "" && !angelAir && !skating;
            clipW += ((eligible || s.dead ? 1 : 0) - clipW) * Mathf.Min(1, dt * 8);
            var L = clip.ok ? clip : null;
            float cw = L != null ? (s.dead ? 1 : clipW) : 0;
            float wLegs = L != null ? L.legs * cw : 0, wTorso = L != null ? L.torso * cw : 0;
            cLegs = wLegs;
            atk = Mathf.Max(0, 1 - s.attackAge / (s.attackKind == "secondary" || s.attackKind == "lance" ? 0.45f : 0.3f));
            bool punching = s.attackKind == "punch", swinging = s.hammer && s.attackKind == "primary";
            if (punching || swinging) atk = 0;
            // quick melee jab (left hand): short pull-back, snap out, slower recovery
            float pq = punching ? s.attackAge / 0.42f : 9;
            punchExt = pq < 0.14f ? -0.35f * pq / 0.14f : pq < 0.26f ? -0.35f + 1.35f * (pq - 0.14f) / 0.12f : Mathf.Max(0, 1 - (pq - 0.26f) / 0.74f);
            punchW = pq >= 1 ? 0 : pq < 0.85f ? 1 : (1 - pq) / 0.15f;
            float cp = s.move == "shatter" ? s.castAge / 0.75f : s.move == "reaping" ? ReapPhase(s.castAge) : 9;
            // the cleave's impact frame: a squash into the strike and a flag the view turns into a camera kick
            bool reapHit = s.move == "reaping" && s.castAge >= REAP_HIT * REAP_SECS && lastCastAge < REAP_HIT * REAP_SECS;
            lastCastAge = s.move == "reaping" ? s.castAge : -1;
            if (reapHit) { sq.v -= 1.4f * PS.squash; impact = 1; } else impact = 0;
            var hs = s.hammer ? HammerPose(swinging ? s.attackAge / (s.swingSecs ?? SWING_TIME) : 9, s.swingSide, s.barrier, cast > 0.05f && s.move != "shatter", s.move, cp) : null;
            bool charging = s.move == "dawncharge";
            float hTw = hs != null ? Mathf.Clamp(hs.th * 0.6f, -1.1f, 1.1f) * hs.w : 0;
            float pTw = -0.45f * Mathf.Max(0, punchExt) * punchW;
            cast = Mathf.Max(0, 1 - s.castAge / 0.55f);
            landDip = Mathf.Max(0, 1 - s.landAge / 0.3f) * (heavy ? 0.14f : 0.1f);
            // ---------------- squash & stretch (stretch on takeoff, hang at the top, squash hard on landing, settle past neutral)
            if (s.jumpAge < lastJump - 1e-6f) sq.v += 1.6f * PS.squash * (1 - 0.55f * PS.weight);
            if (s.landAge < lastLand - 1e-6f) sq.v -= (1.1f + 1.4f * PS.weight) * PS.squash;
            lastJump = s.jumpAge; lastLand = s.landAge;
            float rise = !s.grounded && !s.flying ? Mathf.Clamp(s.vel.y / 12, -0.4f, 1) : 0;
            Step(sq, rise * 0.05f * PS.squash, 160, 11, dt);
            sqY = 1 + Mathf.Clamp(sq.x, -0.2f, 0.18f); sqXZ = 1 / Mathf.Sqrt(sqY);
            // model-space acceleration (world m/s^2): legs and wings swing behind it like pendulums
            var vNow = new Vector3(lvx * s.scale, s.vel.y, lvz * s.scale);
            if (dt > 0) acc = Lerp(acc, (vNow - prevVel) / dt, Mathf.Min(1, dt * 8));
            prevVel = vNow;
            // knocked about in the air: a tumble pose blends in
            tumble += ((PERF && s.knocked && !s.grounded ? 1 : 0) - tumble) * Mathf.Min(1, dt * 7);
            // Gantetsu's Shiko leap: up with the guns overhead, then the two-footed slam - a deep squat the frame he lands
            bool leaping = PERF && s.leap && !s.grounded;
            leapW += ((leaping ? 1 : 0) - leapW) * Mathf.Min(1, dt * (leaping ? 9 : 16));
            if (PERF && lastLeap && !s.leap && s.grounded) { slamDip = 1; sq.v -= 2.2f * PS.squash; }
            lastLeap = s.leap;
            slamDip = Mathf.Max(0, slamDip - dt / 0.5f);
            // knocked flat by a slam: falls along the push, lies there, gets up over the last 0.3 s
            float kd = s.knockdown;
            if (kd > 0 && down < 0.02f)
            {
                downDir = new Vector3(s.hitDir.HasValue ? -s.hitDir.Value.x : 0, 0, s.hitDir.HasValue ? -s.hitDir.Value.y : -1);
                if (downDir.sqrMagnitude < 1e-6f) downDir = new Vector3(0, 0, -1);
                downDir.Normalize();
            }
            down += ((kd > 0.32f ? 1 : 0) - down) * Mathf.Min(1, dt * (kd > 0.32f ? 11 : 5.5f));
            if (down < 1e-3f) down = 0;
            // angel state weights (Mirei): smoothed so every change of state reads as a follow-through
            bool angel = s.angel, inSwoop = angel && s.swoop >= 0;
            float Tw(float v, bool on, float rate) => v + ((on ? 1 : 0) - v) * Mathf.Min(1, dt * rate);
            aSwoop = Tw(aSwoop, inSwoop, 14); aFlare = Tw(aFlare, angel && (s.swoopFlare < 0.35f || (s.rebirth.HasValue && s.rebirth > 0.85f && s.rebirth < 2.2f)), 16);
            aSup = Tw(aSup, angel && s.superjump && s.vel.y > 1.5f, 9); aSling = Tw(aSling, angel && s.slingshot && !s.grounded && !inSwoop, 9);
            aGlide = Tw(aGlide, angel && s.gliding && !inSwoop, 6); aHover = Tw(aHover, angel && s.flying && !inSwoop, 6);
            float idleW = (1 - moveBlend) * (1 - airBlend);
            // ---------------- lower-body yaw: legs face where we move, the torso twists back to the aim
            {
                float mv = speed > 0.6f && moving > 0 ? Mathf.Atan2(lvx, lvz) : 0;
                if (Mathf.Abs(mv) > 1.95f) mv -= Mathf.Sign(mv) * Mathf.PI;          // backpedal: legs face forward
                float target = Mathf.Clamp(mv, -1.05f, 1.05f) * moveBlend * (1 - wLegs);   // 8-way clips turn the legs themselves
                float d = s.yaw - lastYaw; while (d > Mathf.PI) d -= 2 * Mathf.PI; while (d < -Mathf.PI) d += 2 * Mathf.PI;
                float yr = dt > 0 ? d / dt : 0;
                lastYaw = s.yaw; yawRate = yr;
                // when turning in place the legs lag behind the torso for a beat
                hipYawV += ((target - hipYaw) * 90 - hipYawV * 14) * dt;
                hipYaw += hipYawV * dt - (moveBlend < 0.5f ? yr * dt * 0.35f : 0);
                hipYaw = Mathf.Clamp(hipYaw, -1.2f, 1.2f) * (moveBlend < 0.1f ? 0.97f : 1);
                turnRoll += (Mathf.Clamp(-yr * 0.04f * moveBlend, -0.22f, 0.22f) - turnRoll) * Mathf.Min(1, dt * 8);
            }
            // ---------------- gait: feet LOCKED IN WORLD SPACE while planted; each swing lands where the stride predicts
            float run = Mathf.Min(1, speed / (legLen * 7));
            float stride = legLen * (heavy ? 0.6f + 0.4f * run : 0.55f + 0.55f * run);
            float duty = heavy ? 0.6f - 0.1f * run : 0.62f - 0.22f * run;
            float cycleLen = stride * 2, travel = cycleLen * duty;
            phase += Mathf.Min(speed / cycleLen, s.skate ? 2.4f : float.PositiveInfinity) * dt * moving;
            float tuckT = PERF && s.skate && s.grounded ? Mathf.Clamp01((speed - 9) / 14) : 0;
            tuck += (tuckT - tuck) * Mathf.Min(1, dt * 4);
            var md = speed > 0.01f ? new Vector3(lvx / speed, 0, lvz / speed) : Z;
            float lift = legLen * (heavy ? 0.16f : 0.22f) * Mathf.Min(1, speed / 3 + 0.3f);
            bool skateGait = PERF && s.skate && s.grounded && moving > 0;
            skW += ((skateGait ? 1 : 0) - skW) * Mathf.Min(1, dt * 6);
            float sph0 = Mod1(phase * 0.36f);
            float SphOf(int i) => (sph0 + i * 0.5f) % 1;
            var hipsOff = Vector3.zero;
            Vector3 ToModel(Vector3 w) { float dx = (w.x - s.pos.x) / s.scale, dz = (w.z - s.pos.z) / s.scale; return new Vector3(dx * cy - dz * sy, (w.y - s.pos.y) / s.scale, dx * sy + dz * cy); }
            Vector3 ToWorld(Vector3 m) => new Vector3(s.pos.x + (m.x * cy + m.z * sy) * s.scale, s.pos.y + m.y * s.scale, s.pos.z + (-m.x * sy + m.z * cy) * s.scale);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1 : -1;
                var rf = i == 0 ? R["foot_L"] : R["foot_R"];
                // feet sit under the hips, which turn toward the movement direction (lower-body yaw)
                var restFoot = Rot(Y, hipYaw) * new Vector3(rf.p.x + side * hipW * 0.05f, footY, rf.p.z);
                float ph = Mod1(phase + i * 0.5f);
                Vector3 tgt;
                if (!s.grounded || airBlend > 0.5f) { pplant[i] = null; tgt = restFoot; }
                else if (moving > 0 && PERF && s.skate)
                {
                    // no planted foot - the wheels roll with him: push, recovery, glide
                    float sp = SphOf(i); float outK, back, up = 0;
                    if (sp < 0.42f) { float u = sp / 0.42f, e = u * u * (3 - 2 * u); outK = 0.05f + 0.36f * e; back = 0.08f - 0.42f * e; }
                    else if (sp < 0.62f) { float u = (sp - 0.42f) / 0.2f, e = u * u * (3 - 2 * u); outK = 0.41f - 0.36f * e; back = -0.34f + 0.46f * e; up = Mathf.Sin(u * Mathf.PI) * 0.09f; }
                    else { outK = 0.05f; back = 0.12f - 0.04f * (sp - 0.62f) / 0.38f; }
                    float amp = 1 - 0.55f * tuck;
                    tgt = restFoot + Rot(Y, hipYaw) * new Vector3(side * outK * amp * legLen, 0, back * amp * legLen);
                    tgt.y = footY + up * amp * legLen;
                    bool pushing = sp < 0.42f;
                    if (pushing && !lastStance[i] && cLegs < 0.5f) Footfall(i, heavy, PS);
                    lastStance[i] = pushing;
                    pplant[i] = null;
                }
                else if (moving > 0)
                {
                    bool stance = ph < duty;
                    if (stance)
                    {
                        if (!pplant[i].HasValue)
                        {
                            // touch-down: plant where the stride says this foot lands, then keep it there in the world
                            var land = restFoot + md * (travel * (0.5f - ph / duty));
                            var w = ToWorld(land); w.y = s.pos.y + footY * s.scale; pplant[i] = w;
                            if (cLegs < 0.5f) Footfall(i, heavy, PS);
                        }
                        tgt = ToModel(pplant[i].Value);
                    }
                    else
                    {
                        // swing: from lift-off toward the predicted landing spot, with a lift arc
                        float u = (ph - duty) / (1 - duty);
                        if (pplant[i].HasValue) { swingFrom[i] = ToModel(pplant[i].Value); pplant[i] = null; }
                        var from = swingFrom[i] ?? restFoot + md * (-travel / 2);
                        var land = restFoot + md * (travel / 2);
                        float k = u * u * (3 - 2 * u);
                        tgt = Lerp(from, land, k);
                        tgt.y = footY + Mathf.Sin(u * Mathf.PI) * lift;
                        // swingFrom is in model space of lift-off; the body has moved since, so correct by the distance covered
                        swingFrom[i] = from + md * (-(speed * dt));
                    }
                    lastStance[i] = stance;
                }
                else
                {
                    // standing: feet stay put in the world; re-step if the body turned or drifted too far from them
                    if (!pplant[i].HasValue) { var w = ToWorld(restFoot); w.y = s.pos.y + footY * s.scale; pplant[i] = w; }
                    var m = ToModel(pplant[i].Value);
                    if (Vector3.Distance(m, restFoot) > legLen * 0.32f && restepT[i] <= 0 && restepT[1 - i] <= 0) { restepT[i] = 0.22f; swingFrom[i] = m; }
                    if (restepT[i] > 0)
                    {
                        restepT[i] -= dt;
                        float u = 1 - Mathf.Max(0, restepT[i]) / 0.22f;
                        m = Lerp(swingFrom[i] ?? m, restFoot, u); m.y = footY + Mathf.Sin(u * Mathf.PI) * lift * 0.5f;
                        if (restepT[i] <= 0) { var w = ToWorld(restFoot); w.y = s.pos.y + footY * s.scale; pplant[i] = w; if (cLegs < 0.5f) Footfall(i, heavy, PS); }
                    }
                    tgt = m;
                }
                if (PERF && s.grind != 0)
                {
                    // Mag-Grind: both skates on the wall side, knees bent, the outside leg a little behind
                    float g = s.grind;
                    tgt = restFoot + new Vector3(g * legLen * 0.1f, legLen * 0.12f, (side == g ? 0.12f : -0.2f) * legLen);
                }
                // airborne: knees up (jump), trailing dangle (flying)
                else if (airBlend > 0.01f)
                {
                    float tk = flyer ? 0.25f : Mathf.Clamp01((s.vel.y + 4) / 10);
                    var air = new Vector3(rf.p.x + side * hipW * 0.1f, footY + legLen * (0.35f * tk + 0.1f), (flyer ? -0.25f : i == 0 ? 0.15f : -0.05f) * legLen);
                    if (flyer) { air.y += Mathf.Sin(s.time * 2.2f + i) * 0.03f * legLen; air.z += Mathf.Sin(s.time * 1.7f + i * 2) * 0.05f * legLen; }
                    if (s.angel && !PERF)
                    {
                        float fastK = Mathf.Min(1, speed / (legLen * 6));
                        air = new Vector3(rf.p.x * 0.35f, footY + legLen * (s.gliding ? 0.04f : (i == 1 ? 0.14f : 0.05f) * (1 - fastK) + 0.08f * fastK),
                            legLen * (-0.04f - 0.5f * fastK - (i == 1 && !s.gliding ? 0.1f * (1 - fastK) : 0)));
                        air.y += Mathf.Sin(s.time * 1.6f + i * 0.6f) * 0.015f * legLen;
                    }
                    else if (s.angel)
                    {
                        // angelic flight in the BODY frame: hover / swoop + slingshot / superjump dart / descent / flare
                        float Lg = legLen, rx = rf.p.x, bent = i == 1 ? 1 : 0;
                        Vector3 T(float x, float y, float z) => new Vector3(rx * x, footY + Lg * y, Lg * z);
                        var pend = new Vector3(acc.x, 0, acc.z) * (-Lg * 0.3f / 24);
                        var hover = T(0.35f, 0.05f + 0.09f * bent, -0.05f - 0.1f * bent) + pend;
                        hover.y += Mathf.Sin(s.time * 1.6f + i * 0.6f) * 0.015f * Lg;
                        var dart = T(0.28f, 0.02f + 0.13f * bent * (1 - aSup), -0.1f - 0.14f * bent * (1 - aSup));
                        var desc = T(0.4f, 0.07f + 0.05f * bent, -0.12f - 0.08f * bent + Mathf.Sin(s.time * 2.3f + i * Mathf.PI) * 0.035f);
                        var flare = T(0.45f, 0.24f - 0.06f * bent, 0.1f);
                        float wd = aSwoop + aSling + aSup, tot = aHover + wd + aGlide + aFlare;
                        if (tot > 1e-3f) air = Lerp(air, (hover * aHover + dart * wd + desc * aGlide + flare * aFlare) / tot, Mathf.Min(1, tot));
                        else { air.x *= 0.7f; if (i == 1) { air.y += 0.06f * Lg; air.z -= 0.06f * Lg; } }   // a plain jump: one knee up
                    }
                    tgt = Lerp(tgt, air, airBlend);
                }
                // never reach further than the leg allows (keeps IK stable on hard stops)
                var off = tgt - restFoot; off.y = 0;
                float maxOff = legLen * 0.75f;
                if (off.magnitude > maxOff) { tgt = tgt - off + off.normalized * maxOff; pplant[i] = null; }
                foot[i] = tgt;
            }
            // clip feet: the Animator's feet locked in the world while in contact
            if (L != null && wLegs > 0.001f)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (!L.p.TryGetValue(i == 0 ? "foot_L" : "foot_R", out var ct)) continue;
                    var tgt = ct;
                    if (L.contact[i] > 0.5f && L.loco > 0.5f && moving > 0 && s.grounded)
                    {
                        if (!cplant[i].HasValue) { cplant[i] = ToWorld(ct); if (wLegs > 0.5f) Footfall(i, heavy, PS); }
                        var lk = ToModel(cplant[i].Value); lk.y = ct.y;
                        if (Vector3.Distance(lk, ct) > legLen * 0.25f) { cplant[i] = null; cerr[i] = lk - ct; }
                        else { tgt = lk; cerr[i] = lk - ct; }
                    }
                    else
                    {
                        // released: ease out the lock error instead of popping to the clip
                        cplant[i] = null;
                        cerr[i] *= Mathf.Exp(-dt * 18);
                        tgt = ct + cerr[i];
                    }
                    foot[i] = Lerp(foot[i], tgt, wLegs);
                }
            }
            else { cplant[0] = cplant[1] = null; }
            // persona stance width at rest (a sumo's planted base, a medic's feet together)
            if (idleW > 0.01f && Mathf.Abs(PS.width - 1) > 0.01f) for (int i = 0; i < 2; i++) foot[i].x += (i == 0 ? 1 : -1) * hipW * (PS.width - 1) * 0.9f * idleW;
            // body bob: lowest at mid-stance; heavies drop hard onto each foot (staccato), light heroes float
            float bobPh = phase * 2 * Mathf.PI * 2;
            float bc = 0.5f - Mathf.Cos(bobPh) * 0.5f;
            bob = -Mathf.Pow(bc, 1 + PS.weight * 2.5f) * legLen * (heavy ? 0.06f : 0.02f + 0.03f * PS.bounce) * moveBlend;
            // combat stance: knees soft, weight low (per hero); melee heroes lunge into their swings
            float stanceK = idleW * (heavy ? 0.03f : 0.02f + 0.05f * PS.stance);
            float lunge = s.melee ? Mathf.Sin(Mathf.Min(1, atk) * Mathf.PI) * 0.12f * legLen : 0;
            hipsOff.y = bob - landDip * legLen - (s.charging ? 0.04f * legLen : 0) - stanceK * legLen - lunge * 0.3f;
            // skating: knees ~105 deg, weight rolls over the gliding leg every stroke; standing, he nods to the beat
            float skSway = Mathf.Sin(2 * Mathf.PI * sph0 + Mathf.PI);
            if (skW > 0.01f) { hipsOff.y -= (0.12f + 0.1f * tuck) * legLen * skW; hipsOff.x += skSway * 0.085f * (1 - 0.6f * tuck) * legLen * skW; }
            if (PERF && s.skate && idleW > 0.01f) hipsOff.y -= (0.5f - 0.5f * Mathf.Cos(s.time * Mathf.PI * 3)) * 0.018f * legLen * idleW;
            hipsOff.z += lunge;
            // hammer: weight rolls onto the front foot at impact; jab: a small step into the punch
            if (hs != null) { hipsOff.z += hs.imp * 0.09f * legLen; hipsOff.y -= hs.imp * 0.06f * legLen + (hs.w > 0 ? 0.02f * legLen : 0); hipsOff.x += Mathf.Sin(hs.th) * 0.05f * legLen * hs.w; }
            if (charging) hipsOff.y -= 0.07f * legLen;
            hipsOff.z += Mathf.Max(0, punchExt) * punchW * 0.05f * legLen;
            if (s.barrier) hipsOff.y -= 0.06f * legLen;
            if (s.angel && s.flying) hipsOff.y += Mathf.Sin(s.time * 1.8f) * 0.025f * legLen * (1 - Mathf.Min(1, speed / (legLen * 6)));
            if (L != null && wLegs > 0 && L.p.TryGetValue("hips", out var chp)) hipsOff = Lerp(hipsOff, chp - R["hips"].p, wLegs);
            // overlays that ride on clips too: contrapposto, stance depth, footfall punctuation
            float wsh = Step(shift, (float)System.Math.Tanh(Mathf.Sin(s.time * 0.19f + 1.3f) * 5) * PS.hip * idleW, 30, 9, dt);
            Step(kick, 0, 180, 12, dt);
            hipsOff.x += wsh * 0.045f * legLen;
            hipsOff.y -= (L != null && wLegs > 0 ? idleW * 0.03f * PS.stance + Mathf.Abs(wsh) * 0.012f : 0) * legLen + kick.x * 0.05f * legLen;
            // lean into velocity with a damped spring (overshoots when you stop or turn) + roll into turns
            var leanTarget = new Vector2(
                Mathf.Clamp(lvx / 8, -1, 1) * (flyer && s.flying ? (!PERF ? 0.35f : s.angel ? 0.1f : 0.2f) : 0.14f) + turnRoll,
                Mathf.Clamp(lvz / 8, -1, 1) * (flyer && s.flying ? (!PERF ? (s.angel ? 0.75f : 0.45f) : s.angel ? 0.15f : 0.3f) : heavy ? 0.1f : 0.08f + 0.14f * PS.lean) + (s.rush ? 0.3f : 0));
            leanV.x += ((leanTarget.x - lean.x) * 70 - leanV.x * 9) * dt;
            leanV.y += ((leanTarget.y - lean.y) * 70 - leanV.y * 9) * dt;
            lean.x += leanV.x * dt; lean.y += leanV.y * dt;
            // hit flinch + weapon recoil: impulses into springs (directional: shoved away from the attacker)
            if (s.hitAge < lastHitAge)
            {
                flinchV += 7;
                if (PERF && s.hitDir.HasValue) { hitX = s.hitDir.Value.x; hitZ = s.hitDir.Value.y; } else if (PERF) { hitX = Random.value < 0.5f ? -0.5f : 0.5f; hitZ = 0.85f; }
                else { hitX = (Random.value < 0.5f ? -1 : 1) * 0.89f; hitZ = 1; }
                flinchDir = hitX >= 0 ? 1 : -1;
            }
            lastHitAge = s.hitAge;
            flinchV += (-flinch * 160 - flinchV * 14) * dt; flinch += flinchV * dt;
            Step(headF, flinch, 70, 6, dt);
            if (s.attackAge < lastAtkAge && !s.melee && s.attackKind != "punch") recoilV += s.dual.HasValue ? 0.7f : heavy ? 3 : 5;
            lastAtkAge = s.attackAge;
            recoilV += (-recoil * 220 - recoilV * 16) * dt; recoil += recoilV * dt;
            float hipSway = Mathf.Sin(phase * 2 * Mathf.PI) * (heavy ? 0.09f : 0.06f) * moveBlend;
            float idleShift = Mathf.Sin(s.time * 0.55f) * 0.03f * (1 - moveBlend);
            float stepRoll = heavy ? Mathf.Sin(phase * 2 * Mathf.PI) * 0.05f * moveBlend : 0;   // mechs rock side to side per stomp
            // ---------------- hips (face the movement direction)
            float tumP = -0.45f * tumble * hitZ, tumR = 0.4f * tumble * hitX;
            var Dh = Rot(Z, -lean.x * 0.5f + idleShift * PS.sway * 2 + stepRoll + wsh * 0.07f + tumR) * Rot(X, lean.y * 0.4f + tumP) * Rot(Y, hipYaw + hipSway + hTw * 0.34f);
            if (s.stunned) Dh = Rot(Z, Mathf.Sin(s.time * 9) * 0.06f) * Dh;
            Quaternion? WithAim(Quaternion? q, float pitch) => q.HasValue ? Rot(X, pitch) * q.Value : (Quaternion?)null;
            // the slam's landing: down into a deep squat within two frames, then back up
            if (slamDip > 0.01f) hipsOff.y -= (slamDip > 0.85f ? (1 - slamDip) / 0.15f : slamDip / 0.85f) * 0.14f * height;
            // called back from the dead: up from a crouch to standing over the first 0.4 s
            if (s.rising.HasValue && s.rising < 0.4f) { float u = s.rising.Value / 0.4f; hipsOff.y -= (1 - u * u * (3 - 2 * u)) * 0.34f * height; }
            // getting up off the floor: through a crouch
            if (down > 0.01f && kd <= 0.32f) hipsOff.y -= Mathf.Sin(down * Mathf.PI) * 0.13f * height;
            Dh = BlendD(Dh, Cq("hips"), wLegs);
            ApplyDelta("hips", Dh);
            bones["hips"].position = root.TransformPoint(M(R["hips"].p + hipsOff));
            // ---------------- spine chain (unwinds the hip yaw so the chest faces the aim)
            float aimP = -s.pitch;
            float breath = Mathf.Sin(s.time * 1.6f) * 0.015f;
            // an archer draws side-on, the bow shoulder toward the target, and holds it through the release
            var carry = Held.For(s.hero);
            bool archer = carry?.L != null && carry.L.kind == HeldKind.Bow;
            float shotAge = s.attackKind == "primary" || s.attackKind == "secondary" ? s.attackAge : 9;
            drawW += ((archer && (s.charging || shotAge < 0.85f) ? 1 : 0) - drawW) * Mathf.Min(1, dt * (s.charging ? 14 : 6));
            guardW += ((s.parry ? 1 : 0) - guardW) * Mathf.Min(1, dt * (s.parry ? 16 : 8));
            float twist = atk * (s.melee || s.attackKind == "secondary" ? -0.55f : -0.12f) * (1 - drawW) + 0.55f * drawW;
            // the upper body trails a fast aim turn and springs back past centre (spring aims per persona)
            float pitchRate = dt > 0 ? (s.pitch - lastPitch) / dt : 0; lastPitch = s.pitch;
            float lagYaw = Step(lagY, ClampA(-yawRate * 0.045f * PS.lag, 0.35f), PS.aimK, PS.aimD, dt);
            float lagPitch = Step(lagP, ClampA(pitchRate * 0.03f * PS.lag, 0.2f), PS.aimK, PS.aimD, dt);
            float carriage = -PS.chest * 0.28f * (1 - moveBlend * 0.4f) - (s.rush ? 0.1f : 0);
            float fP = -flinch * hitZ, fR = flinch * 0.45f * hitX;
            // an Overwatch hero stays upright on the run (readability): the clip's lean is pulled back up as the run speeds up
            float upright = PERF ? -0.2f * moveBlend * run * (heavy ? 0.4f : 1) : 0;
            Quaternion? WithAdd(Quaternion? q, float p, float y, float r) => q.HasValue ? Rot(X, p) * Rot(Y, y) * Rot(Z, r) * q.Value : (Quaternion?)null;
            float skLean = skW * (0.24f + 0.36f * tuck), skTwist = skW * skSway * 0.14f * (1 - 0.6f * tuck);
            var Ds = Dh * Rot(X, lean.y * 0.5f + aimP * 0.2f + fP * 0.6f + breath + cast * 0.1f + stanceK * 1.2f + (hs != null ? hs.imp * 0.16f + hs.lean : 0) + (charging ? 0.38f : 0) + lagPitch * 0.3f + carriage * 0.4f + kick.x * 0.06f + upright + skLean)
                * Rot(Y, -(hipYaw + hipSway + hTw * 0.22f) * 0.45f + twist * 0.4f + (hTw + pTw) * 0.4f + lagYaw * 0.35f + skTwist) * Rot(Z, fR - wsh * 0.04f - skSway * 0.06f * skW);
            Ds = BlendD(Ds, WithAdd(Cq("spine"), aimP * 0.2f + fP * 0.6f + lagPitch * 0.3f + carriage * 0.4f + kick.x * 0.06f + upright, lagYaw * 0.35f, fR - wsh * 0.04f), wTorso);
            if (bones.ContainsKey("spine")) ApplyDelta("spine", Ds);
            var Dc = Ds * Rot(X, aimP * 0.3f + fP * 0.4f - recoil * 0.9f + breath + lagPitch * 0.7f + carriage * 0.6f + kick.x * 0.08f + upright * 0.6f)
                * Rot(Y, -(hipYaw + hipSway + hTw * 0.22f) * 0.55f + twist * 0.6f - idleShift * 0.5f + (hTw + pTw) * 0.6f + (charging ? -0.35f : 0) + lagYaw * 0.65f) * Rot(Z, -wsh * 0.08f);
            Dc = BlendD(Dc, WithAdd(Cq("chest"), aimP * 0.5f + fP - recoil * 0.9f + lagPitch * 0.7f + carriage * 0.6f + kick.x * 0.08f + upright * 0.6f, lagYaw * 0.65f, -wsh * 0.08f), wTorso);
            ApplyDelta("chest", Dc);
            var Dn = Dc * Rot(X, aimP * 0.2f);
            Dn = BlendD(Dn, WithAim(Cq("neck"), aimP * 0.7f + fP), wTorso);
            if (bones.ContainsKey("neck")) ApplyDelta("neck", Dn);
            // the head keeps the eyes on the aim, glances around when idle, follows a hit through, tucks on a hard landing
            float look = Mathf.Sin(s.time * 0.37f) * 0.12f * (1 - moveBlend) * (1 - Mathf.Min(1, atk * 3));
            float headFollow = PERF ? (headF.x - flinch) * -hitZ * 1.1f : 0;
            float rebornTilt = s.rebirth.HasValue && s.rebirth < 2.4f ? -0.45f * Mathf.Clamp01((s.rebirth.Value - 0.6f) / 0.4f) * (s.rebirth < 1.9f ? 1 : Mathf.Max(0, 1 - (s.rebirth.Value - 1.9f) / 0.5f)) : 0;
            float headP = -lagPitch * 0.9f - carriage * 0.8f + (PERF ? landDip * 2.5f : 0) + headFollow - tiltPitch * 0.55f - upright * 1.3f + rebornTilt;
            var Dhd = Dn * Rot(Y, look - twist * 0.5f - (hTw + pTw) * 0.85f - lagYaw * 0.95f) * Rot(X, aimP * 0.3f + recoil * 0.3f + Mathf.Sin(s.time * 0.7f) * 0.02f + headP);
            Dhd = BlendD(Dhd, WithAdd(Cq("head"), aimP + recoil * 0.3f + fP + headP, -lagYaw * 0.95f, 0), wTorso);
            // head stabilisation: the eyes stay level with the aim while the body leans into the run
            float stabW = PERF && !s.dead && !s.climb ? 0.85f * moveBlend : 0;
            if (stabW > 0.01f || Mathf.Abs(headStab) > 1e-3f)
            {
                var fwd = Dhd * Z;
                float want = aimP + recoil * 0.3f + fP + (PERF ? landDip * 2.5f : 0) + headFollow - lagPitch * 0.9f;
                float has = -Mathf.Asin(Mathf.Clamp(fwd.y, -1, 1));
                headStab += (ClampA((want - has) * stabW, 0.6f) - headStab) * (1 - Mathf.Exp(-dt * 25));
                var axis = new Vector3(fwd.z, 0, -fwd.x);
                if (axis.sqrMagnitude > 1e-6f) Dhd = Rot(axis.normalized, headStab) * Dhd;
            }
            ApplyDelta("head", Dhd);
            // ---------------- legs (IK)
            var hipsPos = R["hips"].p + hipsOff;
            for (int i = 0; i < 2; i++)
            {
                string S = i == 0 ? "L" : "R";
                var th = R["thigh_" + S];
                var hipJ = Dh * (th.p - R["hips"].p) + hipsPos;
                var ft = foot[i]; ft.y = Mathf.Max(ft.y, footY * 0.6f);
                // the Shiko leap: knees up and wide, a sumo's stomp wound up in the air
                if (leapW > 0.01f) { ft.y += leapW * 0.26f * legLen; ft.z += leapW * 0.1f * legLen; ft.x += (i == 0 ? 1 : -1) * leapW * 0.14f * legLen; }
                var pole = Dh * Z;
                if (L != null && wLegs > 0 && L.p.TryGetValue("thigh_" + S, out var a) && L.p.TryGetValue("shin_" + S, out var kk) && L.p.TryGetValue("foot_" + S, out var f))
                {
                    // the clip's knee direction (off the hip-ankle line) steers the IK bend: crouches, rolls, kneeling deaths
                    var af = (f - a).normalized; var kdv = kk - a; kdv -= af * Vector3.Dot(kdv, af);
                    if (kdv.sqrMagnitude > 1e-6f) pole = Lerp(pole, kdv.normalized, wLegs).normalized;
                }
                var (ud, ld) = Ik(hipJ, ft, thigh, shin, pole);
                AimBone("thigh_" + S, ud);
                AimBone("shin_" + S, ld);
                // feet stay level with the ground, toes pitch a little during swing
                float toe = (foot[i].y - footY) / Mathf.Max(1e-3f, legLen) * -1.2f;
                ApplyDelta("foot_" + S, BlendD(Rot(Y, hipSway * 0.3f) * Rot(X, toe), Cq("foot_" + S), wLegs));
            }
            Arms(s, L, cw, Dc, hipsOff, hs, charging, heavy, flyer, run, speed, idleW, archer, shotAge, carry, skSway, SphOf);
            HeldHammer(s, hs, hipsOff, dt);
            Wings(s, Dc, speed, dt);
            Tilt(s, lvx, lvz, dt, flyer);
            // ---------------- knockdown sprawl
            if (down > 0.01f) Sprawl(kd > 0.32f ? down : down * down);
        }
    }
}
