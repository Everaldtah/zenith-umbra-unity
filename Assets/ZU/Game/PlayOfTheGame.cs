// The Play of the Game at the end of every match, and the local player's best play of the match for their highlights
// (the user, 2026-10-04: "a play of the game detector system at the end of every game both stadium and fps modes, gives
// play of the game end screen to the best playmaker of the whole match" and "your best play every game even if you dont
// have play of the game"). Unity only.
//
// Sim/Game/Plays.cs scores the match as it runs (Overwatch's four categories and sliding window). Twice a second this
// asks it for the best play so far - of everyone, and of the local player - and, once that play's window has closed,
// cuts it out of the kill cam's 20 s record (Fx.KillCam.Cut): a clip is only frames the recorder already holds, so it
// costs nothing to keep and is replaced whenever a better play comes. When the match has a winner the sequence runs
// before the result screen: the victory plays out, a card names the play and its hero, the clip replays through the hero
// views from behind that hero's shoulder, and then the results open (PauseMenu waits for Busy to clear). Space, Enter
// or Esc skips. The local player's best play - the Play of the Game itself when it is theirs - goes to the highlights
// library (Highlights.cs), which the Career > History tab lists.
// Off switch: `-zu-potg=0`.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ZU.Game.Fx;
using ZU.Sim;

namespace ZU.Game
{
    [DefaultExecutionOrder(9100)]
    public sealed class PlayOfTheGame : MonoBehaviour
    {
        /// <summary>seconds of replay before the play's first event and after its last; the shortest clip; the card; the
        /// victory playing out before the card</summary>
        const float LEAD = 3f, TAIL = 1.6f, MIN_CLIP = 7f, CARD = 3.2f, AFTER_WIN = 2.2f;
        static readonly HashSet<string> OFF_MODES = new HashSet<string> { "training", "ultviewer", "replay", "aitest" };

        static bool? enabled;
        public static bool Enabled
        {
            get { enabled ??= System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-zu-potg=0") < 0; return enabled.Value; }
            set { enabled = value; }
        }
        public static PlayOfTheGame Current { get; private set; }

        /// <summary>a test hook: `--zu-endafter 45` decides the match for the player's side after that many seconds, so the
        /// end-of-match sequence can be looked at without playing a whole match (0: off)</summary>
        static double? endAfter;
        static double EndAfter
        {
            get
            {
                if (endAfter == null)
                {
                    var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, "--zu-endafter");
                    endAfter = i >= 0 && i + 1 < a.Length && double.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
                }
                return endAfter.Value;
            }
        }

        sealed class Slot { public Play play; public PlayClip clip; }
        enum Phase { Match, Wait, Card, Replay, Done }

        MatchRunner r; World world; Plays plays;
        Slot potg, mine;
        double nextScan;
        Phase phase; float phaseAt; bool saved, hudHidden;

        public static PlayOfTheGame Attach(MatchRunner runner)
        {
            var p = runner.GetComponent<PlayOfTheGame>();
            if (p == null) p = runner.gameObject.AddComponent<PlayOfTheGame>();
            p.r = runner; Current = p;
            return p;
        }

        /// <summary>true from the moment the match is decided until the Play of the Game has been shown (or there is none):
        /// the result screen waits for it</summary>
        public static bool Busy(MatchRunner runner)
        {
            var p = Current;
            return p != null && p.r == runner && (p.phase == Phase.Wait || p.phase == Phase.Card || p.phase == Phase.Replay);
        }

        /// <summary>the match's Play of the Game, once it is decided (the result screen names it)</summary>
        public static Play Final(MatchRunner runner) => Current != null && Current.r == runner && Current.phase == Phase.Done ? Current.potg?.play : null;

        /// <summary>state in one line, for the Editor checks</summary>
        public string Diag => $"enabled {Enabled} phase {phase} events {plays?.Log.Count ?? 0} potg {Describe(potg)} mine {Describe(mine)} saved {saved}";
        static string Describe(Slot s) => s == null ? "-" : $"[{s.play.actor.baseDef.id} {s.play.category} {s.play.score:0} '{s.play.summary}' clip {s.clip.Seconds:0.0}s frames {s.clip.frames.Count}]";

        bool Active => Enabled && r != null && !OFF_MODES.Contains(r.mode ?? "") && !UI.Toolkit.UiTour.Active;

        void OnDisable() { HideHud(false); if (Current == this) Current = null; }

        void Update()
        {
            if (r == null || r.World == null) return;
            if (r.World != world) { world = r.World; plays = Plays.Attach(world); potg = mine = null; phase = Phase.Match; saved = false; nextScan = 0; }
            if (!Active) return;
            var w = world;
            if (EndAfter > 0 && string.IsNullOrEmpty(w.winner) && w.time >= EndAfter) { w.winner = r.Player?.team ?? "zenith"; Debug.Log($"[ZU] --zu-endafter: the match is decided at {w.time:0.0} s"); }
            bool over = !string.IsNullOrEmpty(w.winner);
            var kb = Keyboard.current;
            bool skip = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame);
            switch (phase)
            {
                case Phase.Match:
                    if (over || w.time >= nextScan) { nextScan = w.time + 0.5; Scan(over); }
                    if (over) { phase = Phase.Wait; phaseAt = Time.unscaledTime; }
                    break;
                case Phase.Wait:
                    if (Time.unscaledTime - phaseAt < AFTER_WIN) break;
                    if (potg?.clip == null || !potg.clip.HasBody) { Finish(); break; }
                    KillCam.Stop();                                  // (a kill cam still running belongs to the match that is over)
                    HideHud(true);
                    phase = Phase.Card; phaseAt = Time.unscaledTime;
                    Debug.Log($"[ZU] play of the game: {potg.play.actor.baseDef.name} - {potg.play.Label} - {potg.play.summary} ({potg.play.score:0} pts, clip {potg.clip.Seconds:0.0} s)");
                    break;
                case Phase.Card:
                    if (!skip && Time.unscaledTime - phaseAt < CARD) break;
                    if (skip) { Finish(); break; }
                    if (Time.timeScale <= 0) Time.timeScale = 1;     // (the replay's clock is Time.deltaTime)
                    if (KillCam.Play(r, potg.clip, OnClipDone)) { phase = Phase.Replay; phaseAt = Time.unscaledTime; }
                    else Finish();
                    break;
                case Phase.Replay:
                    // (a clip that never ends must not hold the results for ever)
                    if (skip || Time.unscaledTime - phaseAt > potg.clip.Seconds + 3) { KillCam.Stop(); Finish(); }
                    break;
            }
        }

        void OnClipDone() { if (phase == Phase.Replay) Finish(); }

        void Finish()
        {
            HideHud(false);
            phase = Phase.Done;
            if (saved) return;
            saved = true;
            // the highlights: the local player's best play of this match - the Play of the Game itself when it is theirs
            var me = r.Player;
            if (me == null) return;
            var keep = potg != null && potg.play.actor == me ? potg : mine;
            if (keep?.clip == null || !keep.clip.HasBody) return;
            keep.clip.potg = keep == potg;
            keep.clip.mine = true;
            Highlights.Add(keep.clip);
        }

        void HideHud(bool hide)
        {
            if (hide == hudHidden) return;
            hudHidden = hide;
            var hud = UI.Toolkit.UiRoot.Existing?.HudLayer;
            if (hud != null) hud.style.display = hide ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------------------------------------ the cut
        void Scan(bool final)
        {
            Consider(ref potg, plays.Best(), final);
            var me = r.Player;
            if (me != null) Consider(ref mine, plays.Best(a => a == me), final);
        }

        void Consider(ref Slot s, Play p, bool final)
        {
            if (p == null) return;
            if (s != null && p.score <= s.play.score + 1e-6) return;                  // not better than the play already cut
            double end = p.t1 + TAIL, now = world.time;
            if (!final && now < end) return;                                           // its window is still open
            double t1 = System.Math.Min(end, now), t0 = p.t0 - LEAD;
            if (t1 - t0 < MIN_CLIP) t0 = t1 - MIN_CLIP;
            var c = KillCam.Cut(r, System.Math.Max(0, t0), t1, p.actor.id);
            if (c == null || !c.HasBody) return;                                       // (older than the record: keep what we have)
            var me = r.Player; var hero = p.actor.baseDef;
            c.map = r.mapId; c.mode = world.stadium != null ? "stadium" : r.mode;
            c.heroId = hero.id; c.heroName = hero.name; c.team = p.actor.team;
            c.category = p.Label; c.summary = p.summary; c.score = p.score;
            c.lines = p.events.Where(e => e.text != null).Select(e => e.text).Take(8).ToArray();
            c.mine = p.actor == me;
            c.playerName = c.mine ? "YOU" : ZU.Net.PlayerNames.Of(p.actor) ?? hero.name;      // (online: the username, or "Bot 3")
            s = new Slot { play = p, clip = c };
        }

        // ------------------------------------------------------------------------------------------------ the card and the banner
        GUIStyle big, heroName, sub, small; Texture2D px; Font orbitron, rajdhani;

        void Styles(float u)
        {
            if (big == null)
            {
                px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply();
                orbitron = Resources.Load<Font>("ZUUI/Fonts/Orbitron-800"); rajdhani = Resources.Load<Font>("ZUUI/Fonts/Rajdhani-700");
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                heroName = new GUIStyle(big); sub = new GUIStyle(big); small = new GUIStyle(big);
                if (orbitron != null) { big.font = orbitron; heroName.font = orbitron; }
                if (rajdhani != null) { sub.font = rajdhani; small.font = rajdhani; }
            }
            big.fontSize = Mathf.RoundToInt(64 * u); heroName.fontSize = Mathf.RoundToInt(54 * u); sub.fontSize = Mathf.RoundToInt(30 * u); small.fontSize = Mathf.RoundToInt(22 * u);
        }

        void Box(Rect rc, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(rc, px); GUI.color = old; }

        void OnGUI()
        {
            if (phase != Phase.Card && phase != Phase.Replay) return;
            var p = potg?.play; if (p == null) return;
            float W = Screen.width, H = Screen.height, u = H / 1080f;
            Styles(u);
            var hero = p.actor.baseDef;
            var gold = new Color(1f, 0.84f, 0.42f); var heroCol = Conv.Hex(hero.color);
            bool mineToo = p.actor == r.Player;
            if (phase == Phase.Card)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - phaseAt) / 0.35f);          // the card slides in
                Box(new Rect(0, 0, W, H), new Color(0.02f, 0.03f, 0.06f, 0.9f * k));
                // the hero's picture on the left, the words on the right
                var art = UI.Toolkit.U.Img("key_" + hero.id) ?? UI.Toolkit.U.Img("portrait_" + hero.id);
                float ah = H * 0.78f, aw = ah * 864f / 1184f, ax = W * 0.14f - (1 - k) * 60 * u, ay = (H - ah) / 2;
                if (art != null)
                {
                    Box(new Rect(ax - 4 * u, ay - 4 * u, aw + 8 * u, ah + 8 * u), new Color(heroCol.r, heroCol.g, heroCol.b, 0.9f * k));
                    var oc = GUI.color; GUI.color = new Color(1, 1, 1, k);
                    GUI.DrawTexture(new Rect(ax, ay, aw, ah), art, ScaleMode.ScaleAndCrop);
                    GUI.color = oc;
                }
                float tx = ax + aw + 70 * u, ty = H * 0.28f, tw = W - tx - 40 * u;
                Box(new Rect(tx, ty - 14 * u, 120 * u, 5 * u), gold);
                big.normal.textColor = new Color(gold.r, gold.g, gold.b, k);
                GUI.Label(new Rect(tx, ty, tw, 84 * u), "PLAY OF THE GAME", big);
                heroName.normal.textColor = new Color(heroCol.r, heroCol.g, heroCol.b, k);
                GUI.Label(new Rect(tx, ty + 104 * u, tw, 70 * u), hero.name.ToUpperInvariant(), heroName);
                sub.normal.textColor = new Color(0.86f, 0.89f, 0.96f, k);
                string who = mineToo ? "YOUR PLAY" : potg.clip.playerName != null && potg.clip.playerName != hero.name ? potg.clip.playerName.ToUpperInvariant() : null;
                GUI.Label(new Rect(tx, ty + 178 * u, tw, 40 * u), (who != null ? who + "  ·  " : "") + (hero.title ?? "").ToUpperInvariant(), sub);
                // the category chip and what the play was
                var chip = new Rect(tx, ty + 250 * u, Mathf.Min(tw, 26 * u * p.Label.Length + 50 * u), 50 * u);
                Box(chip, new Color(gold.r, gold.g, gold.b, 0.95f * k));
                sub.normal.textColor = new Color(0.08f, 0.06f, 0.02f, k); sub.alignment = TextAnchor.MiddleCenter;
                GUI.Label(chip, p.Label, sub);
                sub.alignment = TextAnchor.MiddleLeft; sub.normal.textColor = new Color(1, 1, 1, k);
                GUI.Label(new Rect(tx, ty + 318 * u, tw, 40 * u), p.summary, sub);
                small.normal.textColor = new Color(0.7f, 0.74f, 0.82f, k);
                var lines = potg.clip.lines ?? new string[0];
                for (int i = 0; i < lines.Length && i < 6; i++) GUI.Label(new Rect(tx, ty + (372 + i * 30) * u, tw, 30 * u), lines[i], small);
                small.normal.textColor = new Color(gold.r, gold.g, gold.b, 0.9f * k);
                GUI.Label(new Rect(tx, H - 110 * u, tw, 30 * u), "SPACE  skip", small);
                return;
            }
            // the replay: a banner top left, the clip's progress along the bottom
            float bw = 940 * u, bh = 150 * u;
            Box(new Rect(40 * u, 40 * u, bw, bh), new Color(0.02f, 0.03f, 0.06f, 0.72f));
            Box(new Rect(40 * u, 40 * u, 6 * u, bh), gold);
            small.normal.textColor = gold;
            GUI.Label(new Rect(64 * u, 48 * u, bw, 30 * u), "PLAY OF THE GAME" + (mineToo ? "  ·  YOUR PLAY" : ""), small);
            sub.normal.textColor = heroCol;
            GUI.Label(new Rect(64 * u, 80 * u, bw, 44 * u), hero.name.ToUpperInvariant(), sub);
            small.normal.textColor = new Color(0.9f, 0.92f, 0.97f);
            GUI.Label(new Rect(64 * u, 128 * u, bw, 30 * u), $"{p.Label}  ·  {p.summary}", small);
            float prog = potg.clip.Seconds > 0 ? Mathf.Clamp01((float)(KillCam.ClipTime / potg.clip.Seconds)) : 0;
            Box(new Rect(0, H - 6 * u, W, 6 * u), new Color(0, 0, 0, 0.5f));
            Box(new Rect(0, H - 6 * u, W * prog, 6 * u), gold);
            small.normal.textColor = new Color(gold.r, gold.g, gold.b, 0.9f); small.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(W - 340 * u, H - 60 * u, 300 * u, 30 * u), "SPACE  skip", small);
            small.alignment = TextAnchor.MiddleLeft;
        }
    }
}
