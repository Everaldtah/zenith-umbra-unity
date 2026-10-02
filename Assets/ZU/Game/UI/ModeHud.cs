// The parts of the HUD a mode adds: Stadium's round banner and, between rounds, the Armory (cash, the 17 items to buy or
// sell in six slots, the hero power due this round, READY) with the cursor free; the campaign's objective line, wave
// count and the boss's health bar with its name and weak point.
using System.Linq;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game.UI
{
    public static class ModeHud
    {
        /// <summary>the player is shopping in the Armory (controls off, cursor free)</summary>
        public static bool Shopping(MatchRunner r) => r.World?.stadium != null && r.Player != null && r.World.stadium.phase == "armory" && !r.World.stadium.ready.Contains(r.Player.id);

        public static void Update(MatchRunner r)
        {
            if (Shopping(r) && !PauseMenu.Paused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        public static void Draw(MatchRunner r)
        {
            var w = r.World; if (w == null) return;
            if (w.stadium != null) Stadium(r, w.stadium);
            if (w.director is Director d) Campaign(r, d);
        }

        static void Stadium(MatchRunner r, Sim.Stadium st)
        {
            var cv = UiStyle.Canvas(); float W = cv.x, H = cv.y;
            var w = r.World; var me = r.Player;
            // the round banner
            string phase = st.phase == "armory" ? $"ARMORY  {Mathf.Max(0, (float)(st.phaseEnd - w.time)):0}s" : st.phase == "fight" ? $"ROUND {st.round}" : "MATCH OVER";
            var banner = new GUIStyle(UiStyle.H2) { alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, 70, W, 30), $"<color=#5cc8ff>{st.wins["zenith"]}</color>   {phase}   <color=#ff3b5c>{st.wins["umbra"]}</color>", new GUIStyle(banner) { richText = true, fontSize = 26 });
            if (me == null || st.phase != "armory" || st.ready.Contains(me.id)) return;
            // the Armory
            var panel = new Rect(W / 2 - 560, 130, 1120, 760);
            UiStyle.Panel_(panel);
            GUI.Label(new Rect(panel.x + 24, panel.y + 12, 600, 40), "ARMORY", UiStyle.H1);
            GUI.Label(new Rect(panel.xMax - 340, panel.y + 18, 320, 40), $"<color=#ffd76a>{me.cash:0}</color> cash", new GUIStyle(UiStyle.H1) { richText = true, alignment = TextAnchor.UpperRight });
            float colW = (panel.width - 80) / 3;
            string[] cats = { "weapon", "ability", "survival" };
            for (int c = 0; c < 3; c++)
            {
                float x = panel.x + 24 + c * (colW + 16), y = panel.y + 70;
                GUI.Label(new Rect(x, y, colW, 28), cats[c].ToUpperInvariant(), UiStyle.H2); y += 32;
                foreach (var it in Sim.Stadium.ITEMS.Where(i => i.cat == cats[c]))
                {
                    bool own = me.items.Contains(it.id), can = !own && me.cash >= it.cost && me.items.Count < Sim.Stadium.MAX_ITEMS;
                    var tier = it.tier == "epic" ? "#c77dff" : it.tier == "rare" ? "#5cc8ff" : "#d8dde6";
                    var st2 = new GUIStyle(own ? UiStyle.ButtonOn : UiStyle.Button) { richText = true, fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft };
                    string label = $"<color={tier}><b>{it.name}</b></color>   <color=#ffd76a>{it.cost:0}</color>{(own ? "   (sell)" : "")}\n<size=12>{it.desc}</size>";
                    GUI.enabled = own || can;
                    if (GUI.Button(new Rect(x, y, colW, 58), label, st2)) { if (own) st.Sell(me, it.id); else st.Buy(me, it.id); }
                    GUI.enabled = true;
                    y += 62;
                }
            }
            // the hero power due this round
            float py = panel.y + 520;
            if (st.PowerPending(me))
            {
                GUI.Label(new Rect(panel.x + 24, py, 800, 28), "CHOOSE A POWER", UiStyle.H2); py += 32;
                var powers = Sim.Stadium.PowersFor(me.baseDef).Where(p => !me.powers.Contains(p.id)).ToList();
                float pw = (panel.width - 48 - 10 * (powers.Count - 1)) / Mathf.Max(1, powers.Count);
                for (int k = 0; k < powers.Count; k++)
                    if (GUI.Button(new Rect(panel.x + 24 + k * (pw + 10), py, pw, 92), $"<b>{powers[k].name}</b>\n<size=12>{powers[k].desc}</size>", new GUIStyle(UiStyle.Button) { richText = true, wordWrap = true, fontSize = 14, alignment = TextAnchor.UpperLeft }))
                        st.PickPower(me, powers[k].id);
            }
            else if (me.powers.Count > 0)
                GUI.Label(new Rect(panel.x + 24, py, panel.width - 48, 60), "POWERS: " + string.Join("  ·  ", me.powers.Select(id => Sim.Stadium.PowersFor(me.baseDef).FirstOrDefault(p => p.id == id)?.name ?? id)), UiStyle.Body);
            GUI.enabled = !st.PowerPending(me);
            if (GUI.Button(new Rect(panel.xMax - 284, panel.yMax - 76, 260, 56), "READY", UiStyle.Big)) { st.SetReady(me); Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            GUI.enabled = true;
        }

        static void Campaign(MatchRunner r, Director d)
        {
            var cv = UiStyle.Canvas(); float W = cv.x;
            GUI.Label(new Rect(0, 70, W, 30), d.objective, new GUIStyle(UiStyle.H2) { alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(1f, 0.86f, 0.55f) }, fontSize = 22 });
            var b = d.boss;
            if (b != null && b.alive)
            {
                float bw = 900, x = W / 2 - bw / 2, y = 110;
                GUI.Label(new Rect(x, y, bw, 30), $"{b.def.name} - {b.def.title}", new GUIStyle(UiStyle.H2) { alignment = TextAnchor.UpperCenter, normal = { textColor = Conv.Hex(b.def.glow, Color.white) } });
                UiStyle.Box(new Rect(x, y + 34, bw, 16), new Color(0, 0, 0, 0.6f));
                UiStyle.Box(new Rect(x, y + 34, bw * Mathf.Clamp01((float)(b.Health / b.MaxHp)), 16), Conv.Hex(b.def.glow, Color.red));
                if (!string.IsNullOrEmpty(b.def.weak)) GUI.Label(new Rect(x, y + 54, bw, 24), "WEAK POINT: " + b.def.weak, new GUIStyle(UiStyle.Small) { alignment = TextAnchor.UpperCenter });
            }
        }
    }
}
