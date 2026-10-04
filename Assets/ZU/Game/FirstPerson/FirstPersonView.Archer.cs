// The archers in first person, keyed from our own frame-by-frame study of the two Overwatch 2 archer viewmodels
// (docs/research/fp_archers.md: timings, screen positions and arcs measured and written up in our own numbers; no footage,
// curves or assets from the game). Seiran holds his bow the way the bow archer does: rolled almost flat across the bottom
// of the screen, the fist low at the centre, the arrow pointing down the view; a 0.45 s draw lifts the bow ~0.11 of the
// screen as the string hand goes back past the jaw out of frame; the loose kicks the bow down and flattens it, then the
// string hand sweeps up the right edge over the shoulder to the quiver and brings the next arrow down through the upper
// right, upright, and nocks it (nocked ~0.36 s after the loose, settled by 0.5 s - inside the 0.57 s recovery). Yuzu holds
// hers the way the crossbow hunter holds her weapon: two-handed and flat in the bottom-right quadrant, the arrow running up
// toward the reticle; Hawk Eye is Take Aim (the bow slides right and down, the arrow onto the line of sight, ~0.11 s in,
// ~0.05 s out), every loose kicks the bow up, and the re-nock tips the bow up on the right while the string hand brings
// the next arrow up from the hip quiver.
//
// Gameplay owns the clock: the draw is scrubbed by the sim's charge (0.9 s to full), the loose runs on the time since the
// shot, the casts on the time since the cast. Each pose is a key in VIEW space (metres right, up, forward from the eye):
// the bow fist, the bow's roll / yaw / pitch, the string hand, the string elbow's pole and how far the string hand's
// knuckles lie along the bow. Keys are joined by Catmull-Rom (a sweep keeps its speed through them), a change of moment
// is blended by a critically damped offset (no overshoot, so nothing wobbles), the elbows follow keyed poles (so they never
// flip), and the hands are turned onto the bow with the twist shared between forearm and wrist (so the wrist never wrings).
// The bow and its own first-person arrow are placed from the solved fists, not from the forearm.
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.FirstPerson
{
    public partial class FirstPersonView
    {
        /// <summary>key width: t, grip xyz, bow roll / yaw / pitch, draw hand xyz, draw pole xyz, on-string</summary>
        const int AK = 14;
        static float[] A(float t, float gx, float gy, float gz, float roll, float yaw, float pitch, float dx, float dy, float dz, float px, float py, float pz, float str) =>
            new[] { t, gx, gy, gz, roll, yaw, pitch, dx, dy, dz, px, py, pz, str };
        static float[] At(float t, float[] k) { var o = (float[])k.Clone(); o[0] = t; return o; }

        // ---------------------------------------------------------------- Seiran (the bow archer's hold)
        // roll: the upper limb from vertical toward the right (1.40 = 80 deg); yaw -: the upper limb recedes to the right
        static readonly float[] S_IDLE = A(0, -0.04f, -0.215f, 0.5f, 1.4f, -0.32f, 0.02f, -0.03f, -0.24f, 0.24f, 1, -0.6f, -0.4f, 1);
        static readonly float[] S_FULL = A(0.45f, -0.03f, -0.139f, 0.5f, 1.36f, -0.3f, 0.04f, 0.05f, -0.1f, -0.02f, 1, 0.4f, -1, 1);
        /// <summary>the draw, on seconds of draw (charge x 0.9): a small dip, most of the lift in the first 0.2 s, held from 0.45 s</summary>
        static readonly float[][] S_DRAW =
        {
            S_IDLE,
            A(0.06f, -0.04f, -0.222f, 0.5f, 1.4f, -0.32f, 0.0f, -0.03f, -0.245f, 0.23f, 1, -0.4f, -0.6f, 1),
            A(0.2f, -0.033f, -0.152f, 0.5f, 1.37f, -0.3f, 0.03f, 0.0f, -0.19f, 0.13f, 1, 0.1f, -0.9f, 1),
            S_FULL,
            At(0.9f, S_FULL),
        };
        /// <summary>the loose, on seconds since the shot: the kick, the over-the-shoulder reach, the nock</summary>
        static readonly float[][] S_LOOSE =
        {
            At(0, S_FULL),
            A(0.03f, -0.028f, -0.15f, 0.51f, 1.43f, -0.3f, 0.0f, 0.09f, -0.09f, -0.06f, 1, 0.2f, -1, 0),
            A(0.1f, -0.033f, -0.16f, 0.5f, 1.43f, -0.31f, 0.01f, 0.17f, -0.22f, 0.02f, 1, -0.6f, -0.6f, 0),
            A(0.16f, -0.035f, -0.168f, 0.5f, 1.42f, -0.31f, 0.01f, 0.25f, -0.13f, 0.3f, 1, -0.7f, -0.2f, 0),
            A(0.19f, -0.036f, -0.174f, 0.5f, 1.42f, -0.31f, 0.01f, 0.24f, 0.02f, 0.18f, 1, -0.3f, -0.3f, 0),
            A(0.22f, -0.037f, -0.18f, 0.5f, 1.41f, -0.31f, 0.01f, 0.16f, 0.16f, -0.02f, 1, 0.3f, -0.6f, 0),
            A(0.25f, -0.038f, -0.188f, 0.5f, 1.41f, -0.32f, 0.01f, 0.14f, 0.06f, 0.2f, 1, -0.2f, -0.5f, 0),
            A(0.29f, -0.039f, -0.198f, 0.5f, 1.41f, -0.32f, 0.02f, 0.05f, -0.1f, 0.3f, 1, -0.6f, -0.3f, 0),
            A(0.32f, -0.04f, -0.205f, 0.5f, 1.4f, -0.32f, 0.02f, 0.02f, -0.2f, 0.3f, 1, -0.6f, -0.4f, 0),
            A(0.36f, -0.04f, -0.212f, 0.5f, 1.4f, -0.32f, 0.02f, -0.03f, -0.24f, 0.24f, 1, -0.6f, -0.4f, 1),
            At(0.5f, S_IDLE),
        };
        /// <summary>Scatter Current: a snap half-draw loosed at once (the volley leaves on the click), a harder kick, the same reach</summary>
        static readonly float[][] S_SCATTER = Snap(S_LOOSE, A(0, -0.035f, -0.17f, 0.5f, 1.38f, -0.3f, 0.02f, 0.02f, -0.16f, 0.1f, 1, 0, -0.8f, 1),
            A(0.04f, -0.03f, -0.185f, 0.52f, 1.5f, -0.3f, -0.02f, 0.08f, -0.13f, 0.0f, 1, 0.1f, -1, 0));
        /// <summary>quick melee: the bow driven forward like a staff - wound back left, struck in toward the centre, recovered</summary>
        static readonly float[][] S_MELEE =
        {
            S_IDLE,
            A(0.07f, -0.1f, -0.22f, 0.42f, 1.05f, -0.2f, 0.05f, -0.06f, -0.25f, 0.2f, 1, -0.6f, -0.4f, 0.5f),
            A(0.15f, 0.04f, -0.12f, 0.68f, 0.7f, 0.1f, 0.12f, 0.0f, -0.2f, 0.36f, 1, -0.6f, -0.4f, 0.5f),
            A(0.25f, 0.03f, -0.13f, 0.64f, 0.75f, 0.08f, 0.1f, 0.0f, -0.21f, 0.34f, 1, -0.6f, -0.4f, 0.5f),
            At(0.45f, S_IDLE),
        };
        /// <summary>Riverstep: the bow arm braces down and in for the kick off the air, then comes back</summary>
        static readonly float[][] S_STEP = { S_IDLE, A(0.08f, -0.07f, -0.25f, 0.46f, 1.5f, -0.25f, -0.04f, -0.05f, -0.27f, 0.21f, 1, -0.6f, -0.4f, 1), At(0.35f, S_IDLE) };

        // ---------------------------------------------------------------- Yuzu (the crossbow hunter's hold)
        // the bow flat (roll ~97 deg: the right limb a little low), the right limb turned toward the lens; the arrow on the
        // right of the fist, running up toward the reticle; the string hand low right under the weapon
        static readonly float[] Y_IDLE = A(0, 0.14f, -0.19f, 0.45f, 1.69f, 0.3f, 0.03f, 0.15f, -0.17f, 0.18f, 1, -0.8f, -0.3f, 1);
        static readonly float[] Y_FULL = A(0.3f, 0.14f, -0.183f, 0.45f, 1.69f, 0.3f, 0.04f, 0.17f, -0.15f, -0.03f, 1, -0.2f, -1, 1);
        static readonly float[][] Y_DRAW = { Y_IDLE, A(0.05f, 0.14f, -0.193f, 0.45f, 1.69f, 0.3f, 0.025f, 0.15f, -0.172f, 0.17f, 1, -0.8f, -0.3f, 1), Y_FULL, At(0.9f, Y_FULL) };
        /// <summary>Hawk Eye (Take Aim): the bow down and right, the arrow brought onto the line of sight, the lower limb out of frame</summary>
        static readonly float[] Y_AIM = A(0, 0.15f, -0.245f, 0.36f, 1.75f, 0.25f, 0.06f, 0.16f, -0.2f, 0.1f, 1, -0.8f, -0.3f, 1);
        static readonly float[] Y_AIM_FULL = A(0.3f, 0.15f, -0.24f, 0.36f, 1.75f, 0.25f, 0.065f, 0.17f, -0.17f, -0.06f, 1, -0.2f, -1, 1);
        static readonly float[][] Y_AIM_DRAW = { Y_AIM, A(0.05f, 0.15f, -0.247f, 0.36f, 1.75f, 0.25f, 0.058f, 0.16f, -0.202f, 0.09f, 1, -0.8f, -0.3f, 1), Y_AIM_FULL, At(0.9f, Y_AIM_FULL) };
        /// <summary>the loose from the hip: the kick up and back, then the re-nock - the bow tipped up on the right while the
        /// string hand goes down to the hip quiver and lays the next arrow on - settled by 0.5 s</summary>
        static readonly float[][] Y_LOOSE =
        {
            At(0, Y_FULL),
            A(0.04f, 0.14f, -0.171f, 0.42f, 1.69f, 0.3f, 0.11f, 0.18f, -0.16f, -0.05f, 1, -0.2f, -1, 0),
            A(0.15f, 0.14f, -0.19f, 0.45f, 1.66f, 0.3f, 0.04f, 0.24f, -0.3f, 0.1f, 1, -1, 0, 0),
            A(0.24f, 0.15f, -0.17f, 0.44f, 1.35f, 0.28f, 0.3f, 0.22f, -0.27f, 0.16f, 1, -1, 0, 0),
            A(0.32f, 0.15f, -0.168f, 0.44f, 1.33f, 0.28f, 0.3f, 0.18f, -0.17f, 0.24f, 1, -0.8f, -0.2f, 0),
            A(0.4f, 0.145f, -0.175f, 0.445f, 1.4f, 0.29f, 0.26f, 0.15f, -0.17f, 0.18f, 1, -0.8f, -0.3f, 1),
            At(0.5f, Y_IDLE),
        };
        /// <summary>the loose in Hawk Eye: a sharper kick, and the re-nock keeps the arrow near the line of sight</summary>
        static readonly float[][] Y_AIM_LOOSE =
        {
            At(0, Y_AIM_FULL),
            A(0.04f, 0.15f, -0.222f, 0.33f, 1.75f, 0.25f, 0.15f, 0.18f, -0.16f, -0.08f, 1, -0.2f, -1, 0),
            A(0.15f, 0.15f, -0.245f, 0.36f, 1.74f, 0.25f, 0.07f, 0.24f, -0.32f, 0.04f, 1, -1, 0, 0),
            A(0.24f, 0.15f, -0.235f, 0.36f, 1.6f, 0.25f, 0.14f, 0.22f, -0.29f, 0.1f, 1, -1, 0, 0),
            A(0.32f, 0.15f, -0.235f, 0.36f, 1.6f, 0.25f, 0.14f, 0.18f, -0.22f, 0.16f, 1, -0.8f, -0.2f, 0),
            A(0.4f, 0.15f, -0.24f, 0.36f, 1.68f, 0.25f, 0.1f, 0.16f, -0.2f, 0.1f, 1, -0.8f, -0.3f, 1),
            At(0.5f, Y_AIM),
        };
        static readonly float[][] Y_MELEE =
        {
            Y_IDLE,
            A(0.07f, 0.18f, -0.21f, 0.38f, 1.75f, 0.35f, 0.0f, 0.19f, -0.2f, 0.12f, 1, -0.8f, -0.3f, 1),
            A(0.15f, 0.06f, -0.13f, 0.66f, 1.6f, 0.15f, 0.08f, 0.1f, -0.15f, 0.42f, 1, -0.8f, -0.3f, 1),
            A(0.25f, 0.07f, -0.14f, 0.62f, 1.62f, 0.17f, 0.07f, 0.11f, -0.16f, 0.38f, 1, -0.8f, -0.3f, 1),
            At(0.45f, Y_IDLE),
        };
        /// <summary>Sunhop (the updraft): pressed down by the launch, then floating a little high through the glide</summary>
        static readonly float[][] Y_HOP =
        {
            Y_IDLE, A(0.1f, 0.14f, -0.235f, 0.44f, 1.72f, 0.3f, -0.08f, 0.15f, -0.21f, 0.17f, 1, -0.8f, -0.3f, 1),
            A(0.4f, 0.14f, -0.172f, 0.46f, 1.66f, 0.3f, 0.06f, 0.15f, -0.153f, 0.19f, 1, -0.8f, -0.3f, 1), At(1.3f, Y_IDLE), At(1.6f, Y_IDLE),
        };
        /// <summary>Hundred Suns Barrage: the bow swung up at the sky, loosed, and brought back down</summary>
        static readonly float[][] Y_ULT =
        {
            A(0, 0.1f, -0.06f, 0.42f, 1.3f, 0.2f, 0.95f, 0.14f, -0.12f, 0.04f, 1, -0.2f, -1, 1),
            A(0.05f, 0.1f, -0.03f, 0.4f, 1.3f, 0.2f, 1.05f, 0.16f, -0.1f, 0.0f, 1, 0.2f, -1, 0),
            A(0.3f, 0.11f, -0.07f, 0.42f, 1.32f, 0.2f, 0.9f, 0.2f, -0.2f, 0.08f, 1, -0.8f, -0.3f, 0),
            A(0.6f, 0.13f, -0.16f, 0.44f, 1.6f, 0.28f, 0.2f, 0.17f, -0.18f, 0.16f, 1, -0.8f, -0.3f, 0),
            At(0.85f, Y_IDLE),
        };

        /// <summary>a loose variant that starts from its own snap pose: `first` replaces the first key, `kick` follows it, the rest
        /// of `loose` from its third key on</summary>
        static float[][] Snap(float[][] loose, float[] first, float[] kick)
        {
            var o = new float[loose.Length][]; o[0] = first; o[1] = kick;
            for (int i = 2; i < loose.Length; i++) o[i] = loose[i];
            return o;
        }

        /// <summary>the pose at t: Catmull-Rom through the keys (HammerAt for any key width)</summary>
        static float[] KeyAt(float[][] Ks, float t)
        {
            int n = Ks.Length - 1, w = Ks[0].Length;
            if (t <= Ks[0][0]) return (float[])Ks[0].Clone();
            if (t >= Ks[n][0]) return (float[])Ks[n].Clone();
            int i = 0; while (i < n - 1 && t > Ks[i + 1][0]) i++;
            var p0 = Ks[Mathf.Max(0, i - 1)]; var p1 = Ks[i]; var p2 = Ks[i + 1]; var p3 = Ks[Mathf.Min(n, i + 2)];
            float u = (t - p1[0]) / (p2[0] - p1[0]), u2 = u * u, u3 = u2 * u;
            var o = new float[w]; o[0] = t;
            for (int j = 1; j < w; j++) o[j] = 0.5f * (2 * p1[j] + (p2[j] - p0[j]) * u + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * u2 + (3 * p1[j] - p0[j] - 3 * p2[j] + p3[j]) * u3);
            return o;
        }
        static float[] Mix(float[] a, float[] b, float k)
        {
            if (k <= 0) return a; if (k >= 1) return b;
            var o = new float[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = a[i] + (b[i] - a[i]) * k; return o;
        }

        // ---------------------------------------------------------------- state
        /// <summary>off: the archers go back to their converted clips (fp_seiran / fp_yuzu) and the canted-bow liberty - takes
        /// effect on the next viewmodel build (a hero swap or a new match)</summary>
        public static bool KeyedArchers = true;
        /// <summary>decided once per viewmodel build (ArcherBuild), so a flip of KeyedArchers never strands a half-switched rig</summary>
        bool archerOn;
        bool Archer => archerOn;
        bool WantArcher => KeyedArchers && style != null && style.grip == Grip.Bow && (heroId == "seiran" || heroId == "yuzu");
        /// <summary>captures / tests: hold the archer at a moment ("idle", "draw", "loose", "scatter", "melee", "step", "hop",
        /// "ult", with "aim" for Yuzu's Hawk Eye) and a time in it, instead of gameplay</summary>
        public static (string moment, float t, bool aim)? ArcherFreeze;
        float[] arcOut, arcOff, arcOffV; string arcMoment = ""; float aimA;
        // first-person foley between the sim's own draw / loose sounds (evera-eb's AudioKit.PlayFp, found by reflection so this
        // compiles and stays silent until the audio branch is in): each fires once as its moment's clock crosses its time
        static System.Action<string, float> playFp; static bool playFpLooked;
        string sfxMoment = ""; float sfxT = -1; bool sfxAim;
        static void Fp(string id, float vol = 1)
        {
            if (!playFpLooked)
            {
                playFpLooked = true;
                var m = typeof(ZU.Game.Audio.AudioKit).GetMethod("PlayFp", new[] { typeof(string), typeof(float) });
                if (m != null && m.IsStatic)
                {
                    if (m.ReturnType == typeof(void)) playFp = (System.Action<string, float>)System.Delegate.CreateDelegate(typeof(System.Action<string, float>), m);
                    else playFp = (s, v) => m.Invoke(null, new object[] { s, v });
                }
            }
            playFp?.Invoke(id, vol);
        }
        /// <summary>the foley cues of this frame: moment `id` ran from the last frame's clock to `mt`</summary>
        void ArcherSfx(string id, string moment, float mt, bool aimWant)
        {
            if (ArcherFreeze.HasValue) { sfxMoment = id; sfxT = mt; return; }
            bool yz = heroId == "yuzu";
            if (yz && aimWant != sfxAim) Fp(aimWant ? "fp_aim_in" : "fp_aim_out");
            sfxAim = aimWant;
            float from = id == sfxMoment ? sfxT : -1;
            bool Cross(float at) => from < at && mt >= at;
            if (moment == "draw" && Cross(0.9f)) Fp("fp_bow_ready", 0.6f);
            if (moment == "loose" || moment == "scatter")
            {
                if (Cross(0.1f)) Fp("fp_quiver_reach");
                if (Cross(yz ? 0.2f : 0.19f)) Fp("fp_arrow_draw");
                if (Cross(yz ? 0.4f : 0.345f)) Fp("fp_nock");
                if (yz && aimA > 0.5f && Cross(0.04f)) Fp("fp_bow_kick", 0.7f);
            }
            sfxMoment = id; sfxT = mt;
        }
        Transform fpArrow; Renderer[] fpArrowRends; float arrowLen;
        (FingerGrip L, FingerGrip R) arcGrips = (FingerGrip.Fist, FingerGrip.Hook);

        /// <summary>called from Build for an archer: the clips are not used (gameplay drives the keyed poses), the viewmodel's own
        /// arrow is made, the pose starts from below the frame (the equip)</summary>
        void ArcherBuild()
        {
            if (anim != null) anim.enabled = false;
            arcOut = null; arcMoment = ""; aimA = 0;
            if (fpArrow != null) Destroy(fpArrow.gameObject);
            fpArrow = null;
            var spec = Held.For(heroId); var it = spec?.R;
            if (it == null || it.kind != HeldKind.Arrow) return;
            float H = rig.height; arrowLen = it.size * H;
            var g = ProcProps.Arrow(arrowLen, H, it);
            g.name = "fp_arrow";
            g.transform.SetParent(model.transform, false);
            foreach (var tr in g.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = LAYER;
            fpArrowRends = g.GetComponentsInChildren<Renderer>(true);
            foreach (var rd in fpArrowRends) rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var col in g.GetComponentsInChildren<Collider>(true)) Destroy(col);
            fpArrow = g.transform;
        }

        /// <summary>which moment the archer is in, and its pose</summary>
        float[] ArcherPose(Actor a, float t, float dt, out string moment, out float mt)
        {
            bool yz = heroId == "yuzu";
            var c = a.anim; float atk = t - (float)c.attackAt, cast = t - (float)c.castAt; string kind = c.attackKind;
            // Hawk Eye: in over ~0.11 s, out over ~0.05 s (the aim snaps off faster than it settles on)
            float want = yz && a.Sv("zoom", 0) > 0 ? 1 : 0;
            aimA += (want - aimA) * Mathf.Min(1, dt * (want > aimA ? 20 : 45));
            bool shot = (kind == "primary" || kind == "secondary") && atk < 0.5f;
            moment = "idle"; mt = 0;
            if (a.charging) { moment = "draw"; mt = (float)a.charge * 0.9f; }
            else if (shot) { moment = kind == "secondary" && !yz ? "scatter" : "loose"; mt = atk; }
            else if (kind == "punch" && atk < 0.45f) { moment = "melee"; mt = atk; }
            else if (c.castId == a.def.ult.id && cast < (yz ? 0.85f : 0.5f)) { moment = yz ? "ult" : "loose"; mt = cast; }
            else if (c.castId == a.def.ability2.id && cast < 0.5f) { moment = "loose"; mt = cast; }
            else if (c.castId == a.def.ability1.id && cast < (yz ? 1.6f : 0.35f)) { moment = yz ? "hop" : "step"; mt = cast; }
            if (ArcherFreeze.HasValue) { var f = ArcherFreeze.Value; moment = f.moment; mt = f.t; aimA = yz && f.aim ? 1 : 0; }
            if (!yz)
                switch (moment)
                {
                    case "draw": return KeyAt(S_DRAW, mt);
                    case "loose": return KeyAt(S_LOOSE, mt);
                    case "scatter": return KeyAt(S_SCATTER, mt);
                    case "melee": return KeyAt(S_MELEE, mt);
                    case "step": return KeyAt(S_STEP, mt);
                    default: return (float[])S_IDLE.Clone();
                }
            switch (moment)
            {
                case "draw": return Mix(KeyAt(Y_DRAW, mt), KeyAt(Y_AIM_DRAW, mt), aimA);
                case "loose": return Mix(KeyAt(Y_LOOSE, mt), KeyAt(Y_AIM_LOOSE, mt), aimA);
                case "melee": return KeyAt(Y_MELEE, mt);
                case "hop": return KeyAt(Y_HOP, mt);
                case "ult": return KeyAt(Y_ULT, mt);
                default: return Mix((float[])Y_IDLE.Clone(), (float[])Y_AIM.Clone(), aimA);
            }
        }

        /// <summary>a change of moment keeps the pose continuous: the old pose's offset from the new one decays as a critically
        /// damped spring (it never overshoots, so a cut can't set the arms wobbling); the first frame rises from below the frame</summary>
        float[] Inertia(string id, float[] src, float dt)
        {
            const float W = 2 * Mathf.PI * 4.5f;
            if (arcOut == null)
            {
                arcOut = (float[])src.Clone(); arcOff = new float[AK]; arcOffV = new float[AK]; arcMoment = id;
                arcOff[2] = -0.32f; arcOff[8] = -0.32f; arcOff[6] = -0.25f;          // the equip: up from below, the bow tipped down
            }
            else if (id != arcMoment)
            {
                for (int i = 1; i < AK; i++) arcOff[i] = arcOut[i] - src[i];
                arcMoment = id;
            }
            float e = Mathf.Exp(-W * dt);
            var o = new float[AK]; o[0] = src[0];
            for (int i = 1; i < AK; i++)
            {
                float x0 = arcOff[i], v0 = arcOffV[i], j = v0 + W * x0;
                arcOff[i] = (x0 + j * dt) * e; arcOffV[i] = (v0 - W * j * dt) * e;
                o[i] = src[i] + arcOff[i];
            }
            arcOut = o;
            return o;
        }

        /// <summary>the bow's frame in view space from roll / yaw / pitch: U up the upper limb, F out of the belly</summary>
        static void BowFrame(float roll, float yaw, float pitch, out Vector3 U, out Vector3 F)
        {
            var q = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(-pitch * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-roll * Mathf.Rad2Deg, Vector3.forward);
            U = q * Vector3.up; F = q * Vector3.forward;
        }

        void ArcherProc(Actor a, float t)
        {
            float dt = frameDt;
            foreach (var n in BODY) rig.ResetBone(n);
            var src = ArcherPose(a, t, dt, out var moment, out var mt);
            string id = moment == "draw" || moment == "idle" ? moment : moment + "@" + (moment == "loose" || moment == "scatter" || moment == "melee" ? a.anim.attackAt : a.anim.castAt).ToString("F3");
            if (ArcherFreeze.HasValue) { arcOut = null; arcOff = null; }
            ArcherSfx(id, moment, mt, heroId == "yuzu" && a.Sv("zoom", 0) > 0);
            var k = ArcherFreeze.HasValue ? src : Inertia(id, src, dt);
            // breathing (less while drawn: the archer holds still), and the flinch of a hit
            float still = moment == "draw" ? 0.25f : 1;
            float hit = t - (float)a.anim.hitAt, fl = hit < 0.25f ? Bump(hit / 0.25f) * 0.015f : 0;
            var G = new Vector3(k[1], k[2] + Mathf.Sin(t * 1.7f) * 0.003f * still + fl, k[3] - fl);
            BowFrame(k[4] + Mathf.Sin(t * 0.9f) * 0.008f * still, k[5], k[6], out var U, out var F);
            var D = new Vector3(k[7], k[8] + Mathf.Sin(t * 1.7f) * 0.003f * still + fl, k[9] - fl);
            var pole = new Vector3(k[10], k[11], k[12]); float str = Mathf.Clamp01(k[13]);
            // ---- arms: the bow fist and the string hand onto their targets, the hands turned onto the bow
            Vector3 fistL = FistOffset("L");
            rig.SolveArm(0, ToM(G) - fistL, null, new Vector3(-0.8f, -1, -0.3f));
            AlignHand("L", -U, 1);
            rig.SolveArm(1, ToM(D), null, pole);
            AlignHand("R", -U, str);
            // ---- the bow on the fist as solved (the IK falls a little short at full stretch: the bow stays in the hand)
            var fist = FistCentre("L") ?? ToM(G);
            if (held != null)
            {
                held.orbit[0] = (fist, F, U, 1);
                held.gunHide[1] = true;            // the world arrow in the string hand: the viewmodel draws its own
                held.Place();
            }
            // ---- the arrow: nocked on the string (from the string fingers through the shelf above the fist), carried from
            // the quiver, or gone just after a loose
            float H = rig.height * (held != null ? held.gunScale : 1);
            var shelf = fist + U * 0.028f * H;
            var nock = FingerTip("R") ?? ToM(D);
            int mode = 1; Vector3 carry = Vector3.forward;   // 0 hidden, 1 nocked, 2 carried
            if (moment == "loose" || moment == "scatter")
            {
                bool yz = heroId == "yuzu";
                float gone = 0.0f, grab = yz ? 0.2f : 0.19f, nocked = yz ? 0.4f : 0.345f;
                if (mt >= gone && mt < grab) mode = 0;
                else if (mt < nocked)
                {
                    mode = 2;
                    // carried: Seiran's comes over the shoulder tip-up and is brought upright, then laid forward; Yuzu's comes
                    // up from the hip already pointing ahead
                    float u = Mathf.InverseLerp(grab, nocked, mt);
                    carry = yz ? Vector3.Slerp(new Vector3(-0.35f, 0.2f, 0.92f), new Vector3(-0.1f, 0.05f, 1), Smooth(u)).normalized
                               : (u < 0.75f ? Vector3.Slerp(new Vector3(-0.35f, 0.85f, 0.4f), new Vector3(0, 0.95f, 0.3f), Smooth(u / 0.75f)) : Vector3.Slerp(new Vector3(0, 0.95f, 0.3f), Vector3.forward, Smooth((u - 0.75f) / 0.25f))).normalized;
                }
            }
            else if (moment == "ult" && mt > 0.05f && mt < 0.6f) mode = 0;
            PlaceArrow(mode, nock, shelf, carry, U, H);
            // ---- fingers: a fist round the grip; the string hand hooked on the string, flicked open on the loose, loose on the
            // way to the quiver, pinching the next arrow
            var gr = FingerGrip.Hook;
            if (moment == "loose" || moment == "scatter" || moment == "ult") gr = mt < 0.08f ? FingerGrip.Open : mode == 0 ? FingerGrip.Relaxed : mode == 2 ? FingerGrip.Pinch : FingerGrip.Hook;
            else if (str < 0.5f) gr = FingerGrip.Relaxed;
            arcGrips = (FingerGrip.Fist, gr);
            source = "proc:archer-" + moment + (aimA > 0.5f ? "-aim" : "");
        }

        /// <summary>model-space offset from the wrist bone to the centre of the closed fist, as the rest pose has it (the bow's
        /// grip is held there, not at the wrist)</summary>
        Vector3 FistOffset(string S)
        {
            if (!rig.rest.TryGetValue("hand_" + S, out var h) || !rig.rest.TryGetValue("middle1_" + S, out var m)) return Vector3.zero;
            // view space and model space share axes: the forearm's line at rest is the arm reaching forward, near enough
            var d = m.p - h.p; return new Vector3(0, 0, d.magnitude * 0.85f);
        }
        Vector3? FistCentre(string S)
        {
            if (!rig.Has("hand_" + S) || !rig.Has("middle1_" + S)) return null;
            var h = rig.Pos("hand_" + S); var m = rig.Pos("middle1_" + S);
            return h + (m - h) * 0.85f;
        }
        /// <summary>where the string fingers are (between the index and middle middle joints)</summary>
        Vector3? FingerTip(string S)
        {
            if (!rig.Has("index2_" + S) || !rig.Has("middle2_" + S)) return FistCentre(S);
            return (rig.Pos("index2_" + S) + rig.Pos("middle2_" + S)) * 0.5f;
        }

        /// <summary>turn a solved hand about its forearm until its knuckle line (index to little finger) lies along `across`
        /// (model space), by `w`: half the turn in the forearm (it rotates about its own axis, so the wrist stays put), half in
        /// the wrist - a single forearm bone wrung the whole way at the wrist pinches the skin there</summary>
        void AlignHand(string S, Vector3 across, float w)
        {
            if (w <= 0.001f || !rig.Has("forearm_" + S) || !rig.Has("hand_" + S) || !rig.Has("index1_" + S) || !rig.Has("pinky1_" + S)) return;
            var fa = rig.B("forearm_" + S); var hn = rig.B("hand_" + S); var root = rig.root;
            var axis = (hn.position - fa.position).normalized;
            var now = rig.B("pinky1_" + S).position - rig.B("index1_" + S).position;
            var want = root.TransformDirection(across);
            now -= axis * Vector3.Dot(now, axis); want -= axis * Vector3.Dot(want, axis);
            if (now.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            float ang = Vector3.SignedAngle(now, want, axis) * w;
            fa.rotation = Quaternion.AngleAxis(ang * 0.5f, axis) * fa.rotation;
            hn.rotation = Quaternion.AngleAxis(ang * 0.5f, axis) * hn.rotation;
        }

        void PlaceArrow(int mode, Vector3 nock, Vector3 shelf, Vector3 carryView, Vector3 U, float H)
        {
            if (fpArrow == null) return;
            bool show = mode != 0 && active;
            foreach (var rd in fpArrowRends) rd.enabled = show;
            if (!show) return;
            Vector3 dir;
            if (mode == 1)
            {
                // nocked: along the string fingers -> shelf line, leaned toward the aim far ahead so it reads as pointing
                // at the target, not across the view
                dir = (shelf - nock).normalized;
                var far = (ToM(new Vector3(0, 0, 20)) - shelf).normalized;
                dir = Vector3.Slerp(dir, far, 0.35f).normalized;
                float len = arrowLen * (held != null ? held.gunScale : 1);
                if (Vector3.Distance(shelf, nock) > len * 0.98f) nock = shelf - dir * len * 0.98f;   // never drawn off the rest
            }
            else dir = carryView.normalized;
            var up = U - dir * Vector3.Dot(U, dir); if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            fpArrow.localPosition = nock;
            fpArrow.localRotation = Quaternion.LookRotation(dir, up);
            fpArrow.localScale = Vector3.one * (held != null ? held.gunScale : 1);
        }
    }
}
