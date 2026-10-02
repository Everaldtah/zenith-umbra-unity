// A working HUD for the prototype (IMGUI): health / armor / shields, ammo, the ability cooldowns and ult charge, the
// objective, the kill feed and a crosshair. The designed UI Toolkit HUD replaces it.
using System.Linq;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public static class Hud
    {
        static GUIStyle big, small, right;
        static Texture2D white;

        static void Init()
        {
            if (big != null) return;
            white = Texture2D.whiteTexture;
            big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            right = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
        }

        static void Bar(Rect r, float k, Color c) { GUI.color = new Color(0, 0, 0, 0.5f); GUI.DrawTexture(r, white); GUI.color = c; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), white); GUI.color = Color.white; }

        public static void Draw(MatchRunner r)
        {
            var w = r.World; if (w == null) return;
            Init();
            float W = Screen.width, H = Screen.height;
            var me = r.Player;
            // crosshair
            if (me != null && !r.thirdPerson) { GUI.color = Color.white; GUI.DrawTexture(new Rect(W / 2 - 1, H / 2 - 8, 2, 16), white); GUI.DrawTexture(new Rect(W / 2 - 8, H / 2 - 1, 16, 2), white); }
            // objective
            string obj = w.rules == "push"
                ? $"MIKOSHI RUSH   {(w.push.d >= 0 ? "ZENITH" : "UMBRA")} {System.Math.Abs(w.push.d):0} m / {w.push.half:0} m{(w.push.contested ? "   CONTESTED" : "")}"
                : w.rules == "control"
                    ? $"ROUND {w.control.round}   ZENITH {w.control.wins["zenith"]} - {w.control.wins["umbra"]} UMBRA   {w.point.progress["zenith"]:0}% / {w.point.progress["umbra"]:0}%{(w.point.contested ? "   CONTESTED" : "")}"
                    : $"POINT  {w.point.owner ?? "neutral"}   {w.point.progress["zenith"]:0}% / {w.point.progress["umbra"]:0}%";
            GUI.Label(new Rect(W / 2 - 300, 10, 600, 30), obj, new GUIStyle(big) { alignment = TextAnchor.UpperCenter, fontSize = 18 });
            if (w.winner != null) GUI.Label(new Rect(W / 2 - 300, H / 2 - 60, 600, 40), w.winner == "zenith" ? "ZENITH VANGUARD WINS" : "UMBRA SYNDICATE WINS", new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = 34 });
            // kill feed
            float y = 50, now = Time.time;
            foreach (var l in EventSink.feed.Where(l => l.until > now)) { GUI.color = Conv.Hex(l.color); GUI.Label(new Rect(W - 520, y, 500, 22), l.text, right); y += 22; }
            GUI.color = Color.white;
            if (me == null) { GUI.Label(new Rect(20, H - 40, 600, 30), $"SPECTATING  {w.map.name}  {w.time:0}s", small); return; }
            // health block
            var d = me.def;
            GUI.Label(new Rect(30, H - 130, 400, 30), $"{d.name.ToUpperInvariant()}  {(me.alive ? "" : $"respawn {System.Math.Max(0, me.respawnAt - w.time):0.0}s")}", big);
            Bar(new Rect(30, H - 95, 300, 18), (float)(me.hp / d.hp), new Color(0.95f, 0.95f, 0.95f));
            if (me.maxArmor > 0) Bar(new Rect(30, H - 74, 300 * (float)(me.maxArmor / me.MaxHp), 8), (float)(me.armor / me.maxArmor), new Color(1f, 0.75f, 0.2f));
            if (me.ShieldAmt > 0) GUI.Label(new Rect(340, H - 98, 120, 22), $"+{me.ShieldAmt:0}", small);
            GUI.Label(new Rect(30, H - 62, 300, 22), $"{me.Health:0} / {me.MaxHp:0}", small);
            // weapon + abilities
            var P = d.primary;
            string ammo = (P.ammo ?? 0) > 0 ? (me.reloadUntil > 0 ? "RELOADING" : $"{me.ammo:0} / {me.MaxAmmo:0}") : P.kind.ToUpperInvariant();
            GUI.Label(new Rect(W - 330, H - 130, 300, 30), ammo, new GUIStyle(big) { alignment = TextAnchor.UpperRight });
            float x = W - 330;
            foreach (var (key, slot) in new[] { ("SHIFT", d.ability1), ("E", d.ability2), ("RMB", d.secondary) })
            {
                if (slot == null || !slot.IsAbility || slot.id == "none") continue;
                double left = me.CdLeft(slot.id, w.time);
                GUI.Label(new Rect(x, H - 92, 110, 22), $"{key} {slot.name}", small);
                GUI.Label(new Rect(x, H - 72, 110, 22), left > 0 ? $"{left:0.0}s" : "READY", small);
                x += 112;
            }
            float ult = (float)(me.ult / d.ult.charge);
            Bar(new Rect(W / 2 - 60, H - 60, 120, 10), ult, ult >= 1 ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.8f, 1f));
            GUI.Label(new Rect(W / 2 - 100, H - 48, 200, 22), $"Q  {d.ult.name}  {Mathf.Min(100, ult * 100):0}%", new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
        }
    }
}
