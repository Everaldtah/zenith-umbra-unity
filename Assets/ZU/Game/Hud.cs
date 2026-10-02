// The in-match HUD (the TS client/Hud.ts, ported to IMGUI on the 1080p canvas): health / armor / shields in 25-point
// segments with the hero's name, the ability bar (cooldowns filling down, key labels), the ultimate's segmented ring,
// ammo / reload / bow draw, the flight gauge, status chips, the reticle for the weapon (circle-and-crosshairs, a ring for
// melee and beams, a dot when zoomed), hit markers, floating damage numbers, health bars over heads, the objective bar for
// the mode (control / push / point / Stadium / campaign), the kill feed, callouts, the death banner, and the Tab
// scoreboard (eliminations, assists, deaths, damage, healing, mitigation; your own numbers underneath).
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using ZU.Game.UI;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game
{
    public static class Hud
    {
        static readonly Color ZEN = new Color(0.36f, 0.78f, 1f), UMB = new Color(1f, 0.23f, 0.36f), GOLD = new Color(1f, 0.84f, 0.42f);
        static Texture2D ring, dot, white;
        static GUIStyle big, mid, small, tiny, right, center;
        static World lastWorld;

        // ---- event-driven pieces
        struct Num { public Vector3 pos; public float born; public string text; public Color col; }
        struct Feed { public string a, b, mark; public Color ca, cb; public float until; public bool me; }
        struct Call { public string head, text; public Color col; public float until; }
        static readonly List<Num> nums = new List<Num>();
        static readonly List<Feed> feed = new List<Feed>();
        static readonly List<Call> calls = new List<Call>();
        static float hitUntil, hurtUntil; static bool hitCrit, hitKill;
        static bool subscribed;

        static void Init()
        {
            if (big != null) return;
            white = Texture2D.whiteTexture;
            ring = Circle(128, 0.86f, 1f);
            dot = Circle(32, 0f, 1f);
            big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.9f, 0.92f, 0.96f) } };
            tiny = new GUIStyle(small) { fontSize = 11 };
            right = new GUIStyle(mid) { alignment = TextAnchor.UpperRight };
            center = new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter };
            if (!subscribed) { EventSink.OnEvent += OnEvent; subscribed = true; }
        }

        /// <summary>an anti-aliased ring (inner .. outer radius, as fractions of the texture's half size)</summary>
        static Texture2D Circle(int n, float r0, float r1)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n]; float h = n / 2f, aa = 1.5f / h;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - h, y + 0.5f - h).magnitude / h;
                    float a = Mathf.Clamp01((d - r0) / aa + 0.5f) * Mathf.Clamp01((r1 - d) / aa + 0.5f);
                    px[y * n + x] = new Color(1, 1, 1, r0 <= 0 ? Mathf.Clamp01((r1 - d) / aa + 0.5f) : a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
        /// <summary>rotate what's drawn next about a canvas point (RotateAroundPivot takes its pivot in screen pixels)</summary>
        static void Rot(float deg, Vector2 canvasPivot) => GUIUtility.RotateAroundPivot(deg, canvasPivot * (Screen.height / 1080f));
        static void Tex(Rect r, Texture t, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t); GUI.color = o; }
        static string Time2(double s) { s = System.Math.Max(0, s); return $"{(int)(s / 60)}:{(int)(s % 60):00}"; }

        static void OnEvent(MatchRunner r, SimEvent e)
        {
            var me = r.Player; float now = Time.time;
            switch (e)
            {
                case DmgEvent d:
                    bool mine = me != null && d.src == me;
                    if (mine || (me == null && d.amt > 30))
                    {
                        nums.Add(new Num { pos = Conv.U(d.pos) + new Vector3(Random.Range(-0.3f, 0.3f), 0.4f, 0), born = now, text = (d.heal ? "+" : "") + Mathf.RoundToInt((float)d.amt), col = d.heal ? new Color(0.5f, 1f, 0.6f) : d.crit ? new Color(1f, 0.3f, 0.3f) : Color.white });
                        if (nums.Count > 40) nums.RemoveAt(0);
                        if (mine && !d.heal) { hitUntil = now + 0.12f; hitCrit = d.crit; hitKill = false; }
                    }
                    if (me != null && d.tgt == me && !d.heal) hurtUntil = now + 0.15f;
                    break;
                case KillEvent k:
                    feed.Insert(0, new Feed { a = k.src != null ? k.src.def.name : "The Void", b = k.tgt.def.name, mark = k.src != null ? ">" : "v", ca = k.src != null ? Team(k.src.team) : Color.gray, cb = Team(k.tgt.team), until = now + 6, me = me != null && (k.src == me || k.tgt == me) });
                    if (me != null && k.src == me) { hitUntil = now + 0.25f; hitKill = true; }
                    break;
                case DemechEvent dm:
                    feed.Insert(0, new Feed { a = dm.src != null ? dm.src.def.name : "The Void", b = dm.tgt.baseDef.name, mark = ">#", ca = dm.src != null ? Team(dm.src.team) : Color.gray, cb = Team(dm.tgt.team), until = now + 6 });
                    break;
                case CounterEvent c: calls.Insert(0, new Call { head = "COUNTER", text = $"{c.text}\n{c.actor.def.name} vs {c.target.def.name}", col = Conv.Hex(c.actor.def.color, GOLD), until = now + 3 }); break;
                case MsgEvent m: calls.Insert(0, new Call { text = m.text, col = Conv.Hex(m.color, Color.white), until = now + 3 }); break;
            }
            while (feed.Count > 6) feed.RemoveAt(feed.Count - 1);
            while (calls.Count > 3) calls.RemoveAt(calls.Count - 1);
        }
        static Color Team(string t) => t == "zenith" ? ZEN : UMB;

        public static void Draw(MatchRunner r)
        {
            var w = r.World; if (w == null) return;
            Init();
            if (w != lastWorld) { lastWorld = w; nums.Clear(); feed.Clear(); calls.Clear(); }
            var cv = UiStyle.Canvas(); float W = cv.x, H = cv.y, k = Screen.height / 1080f;
            var me = r.Player; double t = w.time; float now = Time.time;
            var cam = Camera.main;
            if (Event.current.type == EventType.Repaint && hurtUntil > now) Box(new Rect(0, 0, W, H), new Color(1, 0, 0, 0.12f));
            Overheads(w, me, cam, k, t);
            Numbers(cam, k, now);
            if (me != null) Player(r, w, me, W, H, t, now);
            else if (r.mode != "spectate") GUI.Label(new Rect(0, H - 80, W, 40), "SPECTATING", center);
            Objective(r, w, me, W, t);
            Feeds(W, now);
            if ((Keyboard.current != null && Keyboard.current.tabKey.isPressed) || !string.IsNullOrEmpty(w.winner)) Board(w, me, W, H, t);
        }

        // ------------------------------------------------------------------------------------------------ the player
        static void Player(MatchRunner r, World w, Actor me, float W, float H, double t, float now)
        {
            // health: 25-point segments, white health / yellow armor / blue shields
            float x0 = 70, y0 = H - 120, bw = 380, bh = 22;
            double max = me.MaxHp, shield = System.Math.Min(me.ShieldAmt, max);
            GUI.Label(new Rect(x0, y0 - 64, 500, 40), me.def.name.ToUpperInvariant(), big);
            GUI.Label(new Rect(x0 + 2, y0 - 26, 500, 22), me.def.title, small);
            int segs = Mathf.Max(1, Mathf.CeilToInt((float)(max + shield) / 25f)); float sw = (bw - (segs - 1) * 2) / segs;
            for (int i = 0; i < segs; i++)
            {
                double lo = i * 25, v = me.hp - lo, va = me.hp + me.armor - lo, vs = me.hp + me.armor + shield - lo;
                var rr = new Rect(x0 + i * (sw + 2), y0, sw, bh);
                Box(rr, new Color(0, 0, 0, 0.45f));
                float fh = Mathf.Clamp01((float)(v / 25)), fa = Mathf.Clamp01((float)(va / 25)), fs = Mathf.Clamp01((float)(vs / 25));
                if (fs > 0) Box(new Rect(rr.x, rr.y, rr.width * fs, bh), new Color(0.45f, 0.8f, 1f));
                if (fa > 0) Box(new Rect(rr.x, rr.y, rr.width * fa, bh), new Color(1f, 0.82f, 0.35f));
                if (fh > 0) Box(new Rect(rr.x, rr.y, rr.width * fh, bh), me.hp < max * 0.3 ? new Color(1f, 0.35f, 0.35f) : Color.white);
            }
            GUI.Label(new Rect(x0, y0 + 26, 300, 40), $"{Mathf.CeilToInt((float)me.Health)}<size=16>/{max:0}</size>{(shield > 1 ? $"  <color=#73ccff>+{Mathf.CeilToInt((float)shield)}</color>" : "")}", new GUIStyle(big) { richText = true, fontSize = 28 });

            // abilities (bottom right): RMB, SHIFT, E, C - a dark fill drops as the cooldown runs out
            var S = me.def.secondary;
            var list = new List<(string key, SlotDef def, string name, double left, double cd)>
            {
                ("RMB", S != null && S.IsAbility ? S : null, S != null && S.IsAbility ? S.name : (S != null && S.heal ? "Heal" : "Alt fire"), S != null && S.IsAbility ? me.CdLeft(S.id, t) : 0, S != null && S.IsAbility ? S.cooldown : 1),
                ("SHIFT", me.def.ability1, me.def.ability1?.name, me.CdLeft(me.def.ability1?.id ?? "", t), me.def.ability1?.cooldown ?? 1),
                ("E", me.def.ability2, me.def.ability2?.name, me.CdLeft(me.def.ability2?.id ?? "", t), me.def.ability2?.cooldown ?? 1),
                ("C", null, "Melee", System.Math.Max(0, me.nextMelee - t), Weapons.QUICK_MELEE_COOLDOWN),
            };
            float ax = W - 560, ay = H - 150, aw = 92, ah = 70;
            bool silenced = me.Has("silence", t);
            for (int i = 0; i < list.Count; i++)
            {
                var (key, def, name, left, cd) = list[i];
                var rr = new Rect(ax + i * (aw + 10), ay, aw, ah);
                bool ready = left <= 0 && !silenced;
                Box(rr, ready ? new Color(0.36f, 0.78f, 1f, 0.28f) : new Color(0, 0, 0, 0.5f));
                if (left > 0) Box(new Rect(rr.x, rr.y, rr.width, rr.height * Mathf.Clamp01((float)(left / System.Math.Max(0.1, cd)))), new Color(0, 0, 0, 0.45f));
                Box(new Rect(rr.x, rr.yMax - 3, rr.width, 3), ready ? ZEN : new Color(1, 1, 1, 0.25f));
                GUI.Label(new Rect(rr.x, rr.y + 6, rr.width, 30), left > 0 ? left.ToString(left < 3 ? "0.0" : "0") : key, center);
                GUI.Label(new Rect(rr.x, rr.yMax + 4, rr.width, 18), name ?? "", new GUIStyle(tiny) { alignment = TextAnchor.UpperCenter });
            }

            // ultimate: a ring of 40 segments filling clockwise, the % (or the key when ready) in the middle
            float u = me.def.ult != null && me.def.ult.charge > 0 ? Mathf.Clamp01((float)(me.ult / me.def.ult.charge)) : 0;
            bool ultReady = u >= 1;
            var c = new Vector2(W / 2, H - 110); float R = 52;
            Tex(new Rect(c.x - R - 8, c.y - R - 8, (R + 8) * 2, (R + 8) * 2), dot, new Color(0, 0, 0, 0.45f));
            int n = 40;
            for (int i = 0; i < n; i++)
            {
                var m = GUI.matrix;
                Rot(i * 360f / n, c);
                Box(new Rect(c.x - 2, c.y - R, 4, 12), (float)i / n < u ? (ultReady ? GOLD : ZEN) : new Color(1, 1, 1, 0.18f));
                GUI.matrix = m;
            }
            GUI.Label(new Rect(c.x - 60, c.y - 22, 120, 44), ultReady ? "Q" : Mathf.FloorToInt(u * 100) + "%", new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, normal = { textColor = ultReady ? GOLD : Color.white } });
            GUI.Label(new Rect(c.x - 150, c.y + R + 6, 300, 20), me.def.ult?.name ?? "", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });

            // ammo / bow draw / reload
            var P = me.def.primary; string ammo;
            if (P.kind == "charge") ammo = me.charging ? Mathf.RoundToInt((float)me.charge * 100) + "%" : "DRAW";
            else if (me.def.dualGuns) ammo = me.reloadUntil > 0 ? "RELOADING" : $"{me.ammo:0} | {(me.sv.TryGetValue("ammo2", out var a2) ? a2 : 0):0}";
            else if (P.ammo.HasValue && P.ammo > 0) ammo = me.reloadUntil > 0 ? "RELOADING" : $"{me.ammo:0}<size=18>/{me.MaxAmmo:0}</size>";
            else ammo = "∞";
            GUI.Label(new Rect(W - 300, H - 230, 230, 50), ammo, new GUIStyle(big) { alignment = TextAnchor.UpperRight, richText = true });

            // flight gauge (fliers, jets)
            if (me.def.frame == "flyer" || me.def.jets.HasValue)
            {
                var fr = new Rect(W / 2 + 120, H - 170, 12, 110);
                Box(fr, new Color(0, 0, 0, 0.5f));
                float f = Mathf.Clamp01((float)me.flight / 100f);
                Box(new Rect(fr.x, fr.yMax - fr.height * f, fr.width, fr.height * f), ZEN);
                GUI.Label(new Rect(fr.xMax + 8, fr.yMax - 20, 120, 20), me.Has("grounded", t) ? "GROUNDED" : me.def.jets.HasValue ? "THRUSTERS" : "FLIGHT", tiny);
            }

            // status chips over the health
            var chips = STATUS.Where(s => me.Has(s.id, t)).ToList();
            float sx = x0;
            foreach (var s in chips.Take(6))
            {
                var col = Conv.Hex(s.color, Color.white); float cw = 12 + s.label.Length * 9;
                Box(new Rect(sx, y0 - 96, cw, 22), new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f, 0.75f));
                GUI.Label(new Rect(sx + 6, y0 - 95, cw, 22), s.label, new GUIStyle(tiny) { fontStyle = FontStyle.Bold, normal = { textColor = col } });
                sx += cw + 6;
            }

            // the reticle (and hit markers), or the death banner
            if (!me.alive)
            {
                Box(new Rect(0, H / 2 - 60, W, 120), new Color(0, 0, 0, 0.45f));
                GUI.Label(new Rect(0, H / 2 - 50, W, 60), "ELIMINATED", new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = 48, normal = { textColor = UMB } });
                GUI.Label(new Rect(0, H / 2 + 8, W, 30), $"Respawn in {System.Math.Max(0, me.respawnAt - t):0.0}s", new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter });
                return;
            }
            Reticle(new Vector2(W / 2, H / 2), P.kind, me.sv.TryGetValue("zoom", out var z) && z > 0);
            if (now < hitUntil)
            {
                var hc = hitKill ? new Color(1, 0.25f, 0.25f) : hitCrit ? new Color(1, 0.4f, 0.4f) : Color.white; float L = hitKill ? 14 : 10;
                foreach (var ang in new[] { 45f, 135f, 225f, 315f })
                {
                    var mtx = GUI.matrix; Rot(ang, new Vector2(W / 2, H / 2));
                    Box(new Rect(W / 2 - 1.5f, H / 2 - 10 - L, 3, L), hc);
                    GUI.matrix = mtx;
                }
            }
        }

        /// <summary>Overwatch 2's stock reticle: circle + crosshairs + dot with a dark outline; melee / beam = the ring alone</summary>
        static void Reticle(Vector2 c, string kind, bool zoom)
        {
            var dark = new Color(0, 0, 0, 0.75f);
            if (zoom) { Tex(new Rect(c.x - 4, c.y - 4, 8, 8), dot, dark); Tex(new Rect(c.x - 2.5f, c.y - 2.5f, 5, 5), dot, Color.white); return; }
            float gap = kind == "melee" ? 14 : kind == "beam" ? 26 : kind == "charge" ? 8 : kind == "projectile" ? 5 : 6;
            float len = kind == "melee" || kind == "beam" ? 0 : kind == "projectile" || kind == "charge" ? 8 : 7;
            float rr = gap + len + 4;
            Tex(new Rect(c.x - rr - 1, c.y - rr - 1, (rr + 1) * 2, (rr + 1) * 2), ring, dark);
            Tex(new Rect(c.x - rr, c.y - rr, rr * 2, rr * 2), ring, new Color(1, 1, 1, 0.9f));
            if (len > 0)
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var p = c + new Vector2(dx, dy) * (gap + len / 2);
                    var rect = dx != 0 ? new Rect(p.x - len / 2, p.y - 1, len, 2) : new Rect(p.x - 1, p.y - len / 2, 2, len);
                    Box(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), dark); Box(rect, Color.white);
                }
            Tex(new Rect(c.x - 3, c.y - 3, 6, 6), dot, dark); Tex(new Rect(c.x - 2, c.y - 2, 4, 4), dot, Color.white);
        }

        static readonly (string id, string label, string color)[] STATUS =
        {
            ("stun", "STUNNED", "#ffee58"), ("root", "ROOTED", "#c77dff"), ("silence", "SILENCED", "#ff4d6d"), ("grounded", "GROUNDED", "#ff4d6d"), ("chained", "TRAPPED", "#ffd76a"),
            ("antiheal", "GRIEVOUS HEX", "#b56dff"), ("brand", "ECLIPSE BRAND", "#ff6a2a"), ("tethered", "STRUNG", "#c77dff"), ("linked", "LINKED", "#bfe8ff"), ("ccimmune", "PURIFIED", "#ffd76a"),
            ("stealth", "VEILED", "#9d7bff"), ("revealed", "REVEALED", "#ffd27a"), ("sealed", "SEALED", "#ffe28a"), ("undying", "SANCTUARY", "#ffe28a"), ("dmgamp", "NOVA +30%", "#bfe8ff"),
            ("vuln", "PUPPETED +30%", "#c77dff"), ("judgment", "RAIJIN'S JUDGMENT", "#8ad8ff"), ("asura", "ASURA", "#ff6a2a"), ("burning", "BURNING", "#ff8a3d"), ("tachiai", "UNSTOPPABLE", "#34d1bf"),
            ("taiko", "TAIKO HEARTBEAT", "#ffb35c"), ("dohyo", "GRAND DOHYO", "#ffe6a8"), ("tempo", "TEMPO RUSH", "#ffd23f"), ("groove", "HEALING GROOVE", "#7dffcf"), ("amp", "MAX VOLUME", "#39d6ff"),
            ("pumped", "PUMPED", "#ffd23f"), ("wound", "WOUNDED", "#ff2d55"), ("warcall", "WAR CALL", "#ffd98a"), ("tideult", "UNSTOPPABLE", "#5ff2e0"), ("reborn", "REBORN", "#ffe9a8"),
            ("tithe", "LIFE TITHE", "#c77dff"), ("sovereign", "STORM SOVEREIGN", "#8ad8ff"),
        };

        // ------------------------------------------------------------------------------------------------ world-anchored
        static bool Project(Camera cam, Vector3 world, float k, out Vector2 p)
        {
            p = default;
            if (cam == null) return false;
            var s = cam.WorldToScreenPoint(world);
            if (s.z <= 0) return false;
            p = new Vector2(s.x / k, (Screen.height - s.y) / k);
            return true;
        }

        static void Overheads(World w, Actor me, Camera cam, float k, double t)
        {
            if (cam == null) return;
            foreach (var a in w.actors)
            {
                if (!a.alive || a == me || a.IsSummon) continue;
                if (me != null && a.team != me.team && a.Has("stealth", t) && !a.Has("revealed", t)) continue;
                var head = Conv.U(a.pos) + Vector3.up * ((float)a.Height + 0.35f);
                float d = Vector3.Distance(cam.transform.position, head);
                if (d > 60 || !Project(cam, head, k, out var p)) continue;
                bool enemy = me == null ? a.team == "umbra" : a.team != me.team;
                float sc = Mathf.Clamp(14 / d, 0.55f, 1f), bw = 90 * sc, bh = 7 * sc;
                var col = enemy ? UMB : ZEN;
                GUI.Label(new Rect(p.x - 100, p.y - bh - 22 * sc, 200, 20), a.def.name, new GUIStyle(tiny) { alignment = TextAnchor.LowerCenter, fontSize = Mathf.RoundToInt(12 * sc + 2), normal = { textColor = col } });
                Box(new Rect(p.x - bw / 2 - 1, p.y - bh - 1, bw + 2, bh + 2), new Color(0, 0, 0, 0.6f));
                Box(new Rect(p.x - bw / 2, p.y - bh, bw * Mathf.Clamp01((float)(a.Health / a.MaxHp)), bh), col);
                if (a.ShieldAmt > 1) Box(new Rect(p.x - bw / 2, p.y - bh, bw * Mathf.Clamp01((float)(a.ShieldAmt / a.MaxHp)), 2), new Color(0.6f, 0.85f, 1f));
            }
        }

        static void Numbers(Camera cam, float k, float now)
        {
            for (int i = nums.Count - 1; i >= 0; i--)
            {
                var n = nums[i]; float a = (now - n.born) / 0.9f;
                if (a >= 1) { nums.RemoveAt(i); continue; }
                if (!Project(cam, n.pos + Vector3.up * a * 1.2f, k, out var p)) continue;
                var c = n.col; c.a = 1 - a * a;
                GUI.Label(new Rect(p.x - 60, p.y - 14, 120, 28), n.text, new GUIStyle(mid) { alignment = TextAnchor.MiddleCenter, normal = { textColor = c } });
            }
        }

        // ------------------------------------------------------------------------------------------------ objective
        static void Objective(MatchRunner r, World w, Actor me, float W, double t)
        {
            if (w.director != null || w.stadium != null) return;    // ModeHud draws the campaign's and Stadium's own
            string my = me?.team ?? "zenith", them = my == "zenith" ? "umbra" : "zenith";
            float cx = W / 2, y = 22;
            void Side(float x, double pct, Color c, bool flip)
            {
                Box(new Rect(x, y + 12, 170, 12), new Color(0, 0, 0, 0.5f));
                float f = Mathf.Clamp01((float)pct / 100f);
                Box(new Rect(flip ? x + 170 * (1 - f) : x, y + 12, 170 * f, 12), c);
                GUI.Label(new Rect(x, y + 26, 170, 22), $"{pct:0}%", new GUIStyle(small) { alignment = flip ? TextAnchor.UpperLeft : TextAnchor.UpperRight, fontStyle = FontStyle.Bold });
            }
            string head, sub;
            if (w.rules == "push")
            {
                var M = w.push; double unlock = System.Math.Max(0, M.unlockAt - t);
                head = "MIKOSHI RUSH";
                sub = unlock > 0 ? $"THE MIKOSHI RISES IN {unlock:0}" : M.overtime ? "OVERTIME" : M.contested ? "CONTESTED" : M.owner != null ? (M.owner == my ? "YOUR TEAM PUSHES" : "ENEMY PUSHES") : "STANDING STILL";
                sub += $"  ·  {Time2(w.timeLimit - t)}  ·  YOU {M.best[my]:0}m / THEM {M.best[them]:0}m";
            }
            else
            {
                var P = w.point; double unlock = System.Math.Max(0, P.unlockAt - t);
                string st = P.contested ? "CONTESTED" : P.capTeam != null ? $"{(P.capTeam == my ? "CAPTURING" : "LOSING")} {P.capture:0}%" : P.owner != null ? (P.owner == my ? "HOLDING" : "ENEMY HOLDS") : unlock > 0 ? $"POINT OPENS {unlock:0}" : "NEUTRAL";
                if (w.rules == "control")
                {
                    var C = w.control;
                    head = $"ROUND {C.round}   <color=#5cc8ff>{C.wins[my]}</color> - <color=#ff3b5c>{C.wins[them]}</color>";
                    sub = C.phase == "intermission" ? $"ROUND {C.round + 1} IN {System.Math.Max(0, System.Math.Ceiling(C.phaseEnd - t)):0}" : C.overtime ? "OVERTIME" : st;
                }
                else { head = st; sub = Time2(w.timeLimit - t); }
                Side(cx - 290, P.progress[my], ZEN, false); Side(cx + 120, P.progress[them], UMB, true);
            }
            GUI.Label(new Rect(cx - 220, y, 440, 34), head, new GUIStyle(mid) { alignment = TextAnchor.UpperCenter, richText = true, fontSize = 22 });
            GUI.Label(new Rect(cx - 320, y + 32, 640, 24), sub, new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }

        // ------------------------------------------------------------------------------------------------ feed + callouts
        static void Feeds(float W, float now)
        {
            feed.RemoveAll(f => f.until < now); calls.RemoveAll(c => c.until < now);
            float y = 24;
            foreach (var f in feed)
            {
                var rr = new Rect(W - 470, y, 440, 30);
                Box(rr, f.me ? new Color(1, 1, 1, 0.16f) : new Color(0, 0, 0, 0.45f));
                GUI.Label(new Rect(rr.x + 10, rr.y + 4, 420, 24), $"<color=#{ColorUtility.ToHtmlStringRGB(f.ca)}><b>{f.a}</b></color>   {f.mark}   <color=#{ColorUtility.ToHtmlStringRGB(f.cb)}><b>{f.b}</b></color>", new GUIStyle(small) { richText = true, alignment = TextAnchor.UpperRight });
                y += 34;
            }
            float cy = 260;
            foreach (var c in calls)
            {
                var st = new GUIStyle(mid) { alignment = TextAnchor.UpperCenter, normal = { textColor = c.col }, fontSize = c.head != null ? 22 : 24 };
                if (c.head != null) { GUI.Label(new Rect(0, cy, W, 18), c.head, new GUIStyle(tiny) { alignment = TextAnchor.UpperCenter, normal = { textColor = c.col } }); cy += 16; }
                GUI.Label(new Rect(0, cy, W, 60), c.text, st);
                cy += c.head != null ? 54 : 34;
            }
        }

        // ------------------------------------------------------------------------------------------------ Tab
        static void Board(World w, Actor me, float W, float H, double t)
        {
            string my = me?.team ?? "zenith", them = my == "zenith" ? "umbra" : "zenith";
            var panel = new Rect(W / 2 - 620, 150, 1240, 620);
            Box(panel, new Color(0.03f, 0.04f, 0.08f, 0.88f));
            GUI.Label(new Rect(panel.x + 24, panel.y + 14, 800, 34), $"{(r_ModeName(w.mode))}   ·   {w.map.name}", mid);
            GUI.Label(new Rect(panel.xMax - 224, panel.y + 14, 200, 34), Time2(t), right);
            void Team(string team, float y, Color c)
            {
                GUI.Label(new Rect(panel.x + 24, y, 600, 26), team == "zenith" ? "ZENITH VANGUARD" : "UMBRA SYNDICATE", new GUIStyle(mid) { normal = { textColor = c } });
                string[] heads = { "E", "A", "D", "DMG", "H", "MIT" }; float[] xs = { 520, 590, 660, 760, 880, 1000 };
                for (int i = 0; i < heads.Length; i++) GUI.Label(new Rect(panel.x + xs[i], y + 4, 100, 22), heads[i], small);
                y += 30;
                foreach (var a in w.actors.Where(a => a.team == team && !a.isRobot && !a.IsSummon))
                {
                    var row = new Rect(panel.x + 20, y, panel.width - 40, 30);
                    Box(row, a == me ? new Color(1, 1, 1, 0.12f) : new Color(1, 1, 1, 0.04f));
                    float up = a.def.ult != null && a.def.ult.charge > 0 ? (float)(a.ult / a.def.ult.charge) : 0;
                    GUI.Label(new Rect(row.x + 10, y + 4, 420, 24), $"{a.def.name}{(a == me ? "  <color=#ffd76a>YOU</color>" : "")}{(a.alive ? "" : "  <color=#888888>(dead)</color>")}   <size=12>{(up >= 1 ? "<color=#ffd76a>ULT</color>" : Mathf.FloorToInt(up * 100) + "%")}</size>", new GUIStyle(small) { richText = true, fontStyle = FontStyle.Bold });
                    string[] vals = { (a.kills + a.assists).ToString(), (a.stats.TryGetValue("healAssists", out var ha) ? ha : 0).ToString("0"), a.deaths.ToString(), a.dmgDone.ToString("N0"), a.healDone.ToString("N0"), a.mitigated.ToString("N0") };
                    for (int i = 0; i < vals.Length; i++) GUI.Label(new Rect(panel.x + xs[i], y + 4, 110, 24), vals[i], small);
                    y += 34;
                }
            }
            Team(my, panel.y + 60, my == "zenith" ? ZEN : UMB);
            Team(them, panel.y + 300, them == "zenith" ? ZEN : UMB);
            if (me != null)
            {
                string Pct(int a, int b) => b > 0 ? Mathf.RoundToInt(100f * a / b) + "%" : "-";
                GUI.Label(new Rect(panel.x + 24, panel.yMax - 44, panel.width - 48, 30),
                    $"ACCURACY {Pct(me.hits, me.shots)}   ·   CRITS {Pct(me.crits, me.hits)}   ·   FINAL BLOWS {me.kills}   ·   OBJECTIVE TIME {Time2(me.objTime)}   ·   BEST STREAK {me.bestStreak}   ·   ULTS {me.ults}", small);
            }
        }
        static string r_ModeName(string mode) => mode switch { "quickplay" => "QUICK PLAY", "competitive" => "COMPETITIVE", "practice" => "AI QUICK MATCH", "skirmish" => "PLAY VS AI", "spectate" => "WATCH", "stadium" => "STADIUM", "campaign" => "OPERATION STARFALL", _ => (mode ?? "").ToUpperInvariant() };
    }
}
