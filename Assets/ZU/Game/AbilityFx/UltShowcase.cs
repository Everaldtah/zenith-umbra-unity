// Ult Viewer (TS client/UltShowcase.ts, ported; Hero Viewer > ULT VIEWER): a hero's ultimate played for real, outside a
// match. It runs a live World (the 'gallery' bench on the Proving Grounds) through the full game pipeline - abilities,
// effects, spirit dragons, voice lines, ragdolls - with a row of target dummies to hit (and wounded allied dummies for
// the support ults to heal), a routine that aims, charges and casts the ult, fights through the timed ones (Dragon Gate
// Blade, Judgment, Asura, the Dohyo, the Colossus) and resets for a replay, and a cinematic camera sized to the effect.
//
// Unity: UltShowcase.Open(hero) starts the Match scene in 'gallery' mode on 'training'; AbilityFx.Register sees the
// pending showcase and attaches it to the runner (the TS game.start({ setup })). The overlay is IMGUI for now (the TS was
// HTML/CSS: restyle it with the rest of the UI); the camera overrides MatchCamera from LateUpdate.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ZU.Game.UI;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed class UltShowcase : MonoBehaviour, IController
    {
        /// <summary>how each ult is shown: `secs` from the cast to the replay; `near` walks within that of a target before
        /// casting (the Dohyo is stamped around you); `fight` closes to that range after the cast and keeps attacking (the
        /// timed ults); `flat` aims level (the ult travels along the ground); `wide` pulls the camera out and ahead (45 m
        /// koi-dragons); `tough` armours the enemy dummies to this many times their health, so a long ult has targets for its
        /// whole length; `row` keeps the camera on the target row instead of following the hero (a 20 m flight through it)</summary>
        sealed class Plan { public float secs; public float? near, fight, wide, tough, dead; public bool flat, row; }
        static readonly Dictionary<string, Plan> PLAN = new Dictionary<string, Plan>
        {
            ["colossus"] = new Plan { secs = 10, fight = 4 },
            ["nova"] = new Plan { secs = 5 },
            ["rebirth"] = new Plan { secs = 6, dead = 1.5f },
            ["sanctuary"] = new Plan { secs = 6 },                       // (the web edition's Kaien)
            ["sealstorm"] = new Plan { secs = 16, wide = 0.4f, tough = 3 },  // Divine Seal Storm: bursts on every enemy within 18 m for 15 s
            ["judgment"] = new Plan { secs = 7, fight = 2.2f },
            ["susanoo"] = new Plan { secs = 11, wide = 0.6f, tough = 2.5f },
            ["hundredsuns"] = new Plan { secs = 4.5f },
            ["singularity"] = new Plan { secs = 4.5f, flat = true },
            ["requiem"] = new Plan { secs = 4.5f },
            ["theater"] = new Plan { secs = 17, wide = 1, tough = 2.5f },
            ["thousandcuts"] = new Plan { secs = 3.2f },
            ["asura"] = new Plan { secs = 8.5f, fight = 5 },                 // (the web edition's Enra)
            ["effigy"] = new Plan { secs = 11, fight = 5, wide = 0.5f, tough = 2 },   // the Crimson Effigy rises behind him and sweeps 12 m for 10 s
            ["dohyo"] = new Plan { secs = 7, near = 3, fight = 2.5f },
            ["bassdrop"] = new Plan { secs = 5 },
            ["tide"] = new Plan { secs = 6, flat = true, wide = 0.45f, row = true },
            ["dragongate"] = new Plan { secs = 10, fight = 2.6f },
            ["twinkoi"] = new Plan { secs = 5, flat = true, wide = 1 },
        };

        // the stage on the Proving Grounds: a lane at z = LZ, clear of the training barriers (z = +-6), the hero at HX facing
        // +x, dummies 8-15 m ahead (inside every ult's reach: the 15 m Theater / Thousand Cuts, the Singularity's 15 m throw);
        // Twin Koi swims on through the low trim wall at x = 10. The camera sits on the -z side, so the barriers are backdrop.
        const double HX = -8, LZ = -10;
        static readonly (double x, double z)[] FOES = { (0, LZ), (3, LZ - 2.8), (3, LZ + 2.8), (6.5, LZ - 1.4), (6.5, LZ + 1.6) };
        static readonly (double x, double z)[] FRIENDS = { (-4.5, LZ + 2), (-5, LZ - 2.2) };     // in the middle ground, where the heal shows
        const double FACE = System.Math.PI / 2;               // yaw toward +x

        static string pending;
        /// <summary>a showcase is waiting for its match (set by Open, taken by AbilityFx.Register)</summary>
        public static bool Pending => pending != null;

        /// <summary>open the Ult Viewer for a hero: a gallery-bench match on the Proving Grounds with the showcase as the hero's
        /// controller and camera (TS startUltShowcase)</summary>
        public static void Open(string heroId)
        {
            pending = heroId;
            MatchSettings.Start("training", heroId, "gallery", 0.7f, true);
        }

        public static UltShowcase Attach(MatchRunner r)
        {
            if (pending == null || r.World == null || r.World.actors.Count == 0) return null;
            pending = null;
            var s = r.gameObject.AddComponent<UltShowcase>();
            s.Stage(r);
            return s;
        }

        MatchRunner runner;
        World w;
        Actor hero;
        readonly List<Actor> foes = new List<Actor>(), friends = new List<Actor>();
        Plan plan;
        /// <summary>the spectator label (TS: Game reads controller.step on the gallery bench)</summary>
        public string step = "ult viewer";
        enum Phase { Ready, Approach, Cast, Show }
        Phase phase = Phase.Ready;
        double at;
        int castTries;
        bool felled;
        int ultsBefore;
        float barLeft = 1;
        // camera state (orbit offset / zoom from the mouse, smoothed focus)
        float orbit, zoom = 1, lift = 0.36f, swing; bool camInit;
        Vector3 focus;
        float? drag;

        void Stage(MatchRunner r)
        {
            runner = r; w = r.World;
            hero = w.actors[0];
            plan = PLAN.TryGetValue(hero.def.ult?.id ?? "", out var p) ? p : new Plan { secs = 5 };
            hero.spawn = new[] { HX, LZ };
            hero.controller = this;
            foreach (var (x, z) in FOES) { var d = w.AddHero("bot_dummy", "umbra"); d.spawn = new[] { x, z }; foes.Add(d); }
            // the healing / temporary-health / cannot-die ults need wounded allies to show anything
            if (hero.baseDef.role == "support") foreach (var (x, z) in FRIENDS) { var d = w.AddHero("bot_dummy", "zenith"); d.spawn = new[] { x, z }; friends.Add(d); }
            Reset();
        }

        void Place(Actor a, double x, double z, double yaw)
        {
            a.pos = new V3(x, System.Math.Max(0, w.level.GroundAt(x, z, 4)), z);
            a.vel = new V3(0, 0, 0); a.yaw = a.input.yaw = yaw; a.pitch = a.input.pitch = 0;
        }

        /// <summary>everyone back on their marks, the hero's ult full</summary>
        public void Reset()
        {
            var h = hero;
            w.Respawn(h, true);                  // puts back swapped weapons (Dragon Gate Blade), the Colossus scale, statuses
            Place(h, HX, LZ, FACE);
            h.cd.Clear(); h.ult = h.def.ult.charge;
            void Put(Actor d, double x, double z, double hp)
            {
                if (!d.alive) w.Respawn(d, true);
                d.st.Clear(); d.sv.Clear(); d.forced = null; d.wounds.Clear(); d.shields.Clear();
                d.hp = d.def.hp * hp; d.lastDamagedAt = w.time;      // no out-of-combat regen before the ult has healed them
                // (armour, not health: the bars over their heads stay full-scale)
                d.maxArmor = d.armor = d.team == h.team ? 0 : d.def.hp * System.Math.Max(0, (plan.tough ?? 1) - 1);
                Place(d, x, z, d.team == h.team ? FACE : -FACE);
            }
            for (int i = 0; i < foes.Count; i++) Put(foes[i], FOES[i].x, FOES[i].z, 1);
            for (int i = 0; i < friends.Count; i++) Put(friends[i], FRIENDS[i].x, FRIENDS[i].z, 0.35);
            phase = Phase.Ready; at = w.time; castTries = 0; felled = false;
        }

        /// <summary>the nearest standing target (or the middle of the row)</summary>
        V3 Target()
        {
            var h = hero; Actor best = null; double bd = double.PositiveInfinity;
            foreach (var d in foes) if (d.alive) { double k = M.Hypot(d.pos.x - h.pos.x, d.pos.z - h.pos.z); if (k < bd) { bd = k; best = d; } }
            return best != null ? best.Center : new V3(3, 1, LZ);
        }

        double AimAt(V3 p, bool flat)
        {
            var h = hero; var e = h.Eye; double dx = p.x - e.x, dz = p.z - e.z, dist = M.Hypot(dx, dz);
            h.input.yaw = System.Math.Atan2(dx, dz);
            // aimed ults (Hundred Suns lands where you look): the middle of the row on the ground
            h.input.pitch = flat ? 0 : System.Math.Atan2(p.y - e.y, System.Math.Max(1, dist));
            return dist;
        }

        // controller: called by World.Step for the hero
        public void Think(double dt)
        {
            var h = hero; var i = h.input; var P = plan; double t = w.time - at;
            i.mx = i.mz = 0; i.fire = i.alt = i.jump = i.jumpHeld = i.melee = i.reload = false; i.a1 = i.a2 = i.ult = false;
            h.hp = System.Math.Max(h.hp, h.def.hp * 0.6);
            if (h.MaxAmmo > 0) h.ammo = h.MaxAmmo;
            var tgt = Target();
            var mid = new V3(3.5, 0, LZ);
            double dist = AimAt(P.flat || phase == Phase.Ready ? new V3(tgt.x, h.Eye.y, tgt.z) : phase == Phase.Cast && P.fight == null ? mid : tgt, P.flat);
            if (phase == Phase.Ready)
            {
                // (a resurrection needs someone to call back: the friends fall `dead` seconds before the cast)
                if (P.dead.HasValue && t > 1.1 && !felled) { felled = true; foreach (var d in friends) w.Kill(d, null); }
                if (t > 1.1 + (P.dead ?? 0)) { phase = P.near.HasValue ? Phase.Approach : Phase.Cast; at = w.time; ultsBefore = h.ults; }
            }
            else if (phase == Phase.Approach)
            {
                if (dist > (P.near ?? 0) && t < 3.5) i.mz = 1;
                else { phase = Phase.Cast; at = w.time; ultsBefore = h.ults; }
            }
            else if (phase == Phase.Cast)
            {
                // a fresh press each try (World.pressed is edge-triggered); some ults refuse without a target in reach
                i.ult = (int)System.Math.Floor(t * 8) % 2 == 0;
                if (h.ults > ultsBefore) { phase = Phase.Show; at = w.time; }
                else if (t > 2.5) { castTries++; at = w.time; if (castTries > 2) Reset(); }
            }
            else
            {
                // the timed ults: close in and keep swinging / firing until the replay
                if (P.fight.HasValue) { if (dist > P.fight.Value) i.mz = 1; i.fire = dist < P.fight.Value + 3; }
                if (t > P.secs) Reset();
            }
            double left = phase == Phase.Show ? System.Math.Max(0, P.secs - t) : 0;
            step = phase == Phase.Show ? $"{h.baseDef.ult.name} · replay in {left:0}s" : h.baseDef.ult.name;
            barLeft = phase == Phase.Show ? (float)(left / P.secs) : 1;
        }

        // ------------------------------------------------------------------ camera
        void LateUpdate()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || hero == null) return;
            ReadInput();
            Shot(cam.transform, Time.unscaledDeltaTime);
        }

        /// <summary>a slow three-quarter orbit behind the hero's shoulder, framing the hero and the target row (wider for the koi)
        /// - in sim space, the camera placed through Sp</summary>
        void Shot(Transform cam, float dt)
        {
            var h = hero; float H = (float)h.Height; var P = plan;
            float wide = P.wide ?? 0, ahead = 5 + 9 * wide;
            var want = P.row ? new Vector3(3.5f, 1.2f, (float)LZ)
                : new Vector3((float)h.pos.x + ahead * 0.9f, (float)h.pos.y + H * 0.45f + 0.4f, (float)(LZ + (h.pos.z - LZ) * 0.6));
            float k = camInit ? 1 - Mathf.Exp(-dt * 3) : 1;
            focus += (want - focus) * k;
            float r = Mathf.Max(9, H * 3.2f) * (1 + 1.1f * wide) * zoom;
            float bas = -2.2f + Mathf.Sin((float)w.time * 0.18f) * 0.28f + orbit; var F = focus;
            // keep a clear shot: nothing between the lens and the action, and no prop right in front of the lens (the Proving
            // Grounds' barriers) - swing around a little first, then rise over it
            var solids = (w.level as BoxLevel)?.Solids;
            (float sw, float e) pick = (0, 1.1f);
            foreach (var (sw, e) in new[] { (0f, 0.36f), (0.4f, 0.36f), (-0.4f, 0.36f), (0f, 0.55f), (0.4f, 0.55f), (-0.4f, 0.55f), (0f, 0.8f), (0.7f, 0.8f) })
            {
                float a = bas + sw, sx = Mathf.Sin(a) * r, sz = Mathf.Cos(a) * r, dy = r * e + H * 0.2f, len = Mathf.Sqrt(sx * sx + dy * dy + sz * sz);
                float cx = F.x + sx, cy = F.y + dy, cz = F.z + sz;
                bool near = false;
                if (solids != null) foreach (var o in solids) if (o.y1 > cy - 2.5 && M.Hypot(o.x - cx, o.z - cz) < o.r + 3.5) { near = true; break; }
                if (!near && w.level.Ray(Sp.ToV3(F), new V3(sx / len, dy / len, sz / len), len) == null) { pick = (sw, e); break; }
            }
            float ke = camInit ? 1 - Mathf.Exp(-dt * 1.8f) : 1;
            swing += (pick.sw - swing) * ke; lift += (pick.e - lift) * ke;
            float ang = bas + swing;
            var want2 = new Vector3(F.x + Mathf.Sin(ang) * r, F.y + r * lift + H * 0.2f, F.z + Mathf.Cos(ang) * r);
            var pos = Sp.U(want2);
            if (!camInit) { cam.position = pos; camInit = true; }
            else { float kc = 1 - Mathf.Exp(-dt * 4); cam.position += (pos - cam.position) * kc; }
            cam.LookAt(Sp.U(focus));
        }

        // ------------------------------------------------------------------ input and overlay
        void ReadInput()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb != null && !PauseMenu.Paused)
            {
                if (kb.rKey.wasPressedThisFrame) Act("replay");
                if (kb.tKey.wasPressedThisFrame) Act("slow");
            }
            // orbit / zoom (the gallery bench never takes the pointer lock)
            if (mouse == null || PauseMenu.Paused) { drag = null; return; }
            float mx = mouse.position.ReadValue().x;
            if (mouse.leftButton.wasPressedThisFrame && !overUi) drag = mx;
            if (!mouse.leftButton.isPressed) drag = null;
            if (drag.HasValue) { orbit -= (mx - drag.Value) * 0.006f; drag = mx; }
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0) zoom = Mathf.Clamp(zoom * (wheel < 0 ? 1.1f : 0.9f), 0.45f, 2.2f);
        }

        void Act(string k)
        {
            if (k == "replay") Reset();
            else if (k == "slow") Time.timeScale = Time.timeScale < 1 ? 1 : 0.35f;
            else if (k == "back") { Time.timeScale = 1; MatchSettings.BackToMenu(); }
        }

        bool overUi;
        GUIStyle small, title, body, btn;
        void OnGUI()
        {
            if (hero == null) return;
            // a 1080p virtual canvas, like the HUD
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s;
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 12 };
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            body ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            btn ??= new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            var h = hero.baseDef; var u = h.ult; var c = Conv.Hex(h.color, Color.white);
            var mouse = Event.current.mousePosition;
            // the card: name, the ult, its description, the replay bar
            var card = new Rect(24, 70, 430, 150);
            GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.72f); GUI.DrawTexture(card, Texture2D.whiteTexture); GUI.color = c; GUI.DrawTexture(new Rect(24, 70, 4, 150), Texture2D.whiteTexture);
            GUI.color = new Color(1, 1, 1, 0.7f); GUI.Label(new Rect(40, 80, 400, 18), $"ULT VIEWER · {h.name.ToUpperInvariant()}", small);
            GUI.color = c; GUI.Label(new Rect(40, 98, 400, 30), u.name, title);
            GUI.color = new Color(0.84f, 0.86f, 0.96f); GUI.Label(new Rect(40, 130, 400, 66), u.desc, body);
            GUI.color = new Color(1, 1, 1, 0.12f); GUI.DrawTexture(new Rect(40, 204, 398, 4), Texture2D.whiteTexture);
            GUI.color = c; GUI.DrawTexture(new Rect(40, 204, 398 * barLeft, 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
            // the buttons
            var r1 = new Rect(24, 230, 120, 32); var r2 = new Rect(150, 230, 110, 32); var r3 = new Rect(266, 230, 188, 32);
            if (GUI.Button(r1, "⟲ REPLAY  [R]", btn)) Act("replay");
            GUI.color = Time.timeScale < 1 ? new Color(1, 0.84f, 0.42f) : Color.white;
            if (GUI.Button(r2, "SLOW-MO  [T]", btn)) Act("slow");
            GUI.color = Color.white;
            if (GUI.Button(r3, "BACK TO HERO VIEWER", btn)) Act("back");
            // the hero strip
            var roster = ZU.Sim.Setup.RosterFor(true);
            float bw = 48, gap = 6, total = roster.Count * (bw + gap) - gap, x0 = (W - total) / 2, y0 = 1080 - 22 - bw;
            for (int k = 0; k < roster.Count; k++)
            {
                var x = roster[k]; var rr = new Rect(x0 + k * (bw + gap), y0, bw, bw);
                GUI.color = x.id == h.id ? Conv.Hex(x.color, Color.white) : new Color(1, 1, 1, 0.75f);
                if (GUI.Button(rr, new GUIContent(x.name.Length > 4 ? x.name.Substring(0, 4) : x.name, $"{x.name}: {x.ult?.name}"), btn) && x.id != h.id) { Time.timeScale = 1; Open(x.id); }
            }
            GUI.color = new Color(1, 1, 1, 0.55f);
            GUI.Label(new Rect(W / 2 - 150, 1080 - 80 - 20, 300, 20), "Drag to orbit · wheel to zoom", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUI.color = Color.white;
            overUi = card.Contains(mouse) || r1.Contains(mouse) || r2.Contains(mouse) || r3.Contains(mouse) || mouse.y > y0 - 4;
            GUI.matrix = Matrix4x4.identity;
        }

        void OnDestroy() { if (Time.timeScale < 1) Time.timeScale = 1; }
    }
}
