// The desktop app's main menu: the selected hero standing on a stone plinth under Hanabi's dusk sky (its real model,
// idling, cloth and hair moving), the roster by role on the left, the hero's kit underneath, and on the right the map,
// mode, bot difficulty, view and quality - then PLAY (or SPECTATE: bots only). Hands the choice to MatchSettings.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ZU.Game.Env;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI
{
    public class MainMenu : MonoBehaviour
    {
        GameData data;
        List<HeroDef> heroes;
        List<MapDef> maps;
        GameObject model;
        Transform stage;
        string shown;
        float spin;
        Vector2 heroScroll, mapScroll;
        static int quality = 2;
        static readonly (string id, string label)[] MODES = { ("quickplay", "QUICK PLAY"), ("competitive", "COMPETITIVE"), ("stadium", "STADIUM"), ("campaign", "CAMPAIGN \u00b7 STARFALL"), ("skirmish", "SKIRMISH"), ("practice", "PRACTICE RANGE") };
        Career.CareerData career;
        List<CampaignLevel> levels;
        static readonly (float skill, string label)[] SKILLS = { (0.45f, "EASY"), (0.6f, "NORMAL"), (0.75f, "HARD"), (0.9f, "ELITE") };

        void Start()
        {
            Time.timeScale = 1;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            data = ZuData.Get();
            heroes = data.Heroes.Where(h => !h.summoned).ToList();
            maps = data.Maps.Where(m => !m.retired).OrderBy(m => m.id == "training" ? 1 : 0).ToList();
            if (data.Def(MatchSettings.Hero) == null) MatchSettings.Hero = heroes[0].id;
            if (!data.Map.ContainsKey(MatchSettings.Map)) MatchSettings.Map = maps[0].id;
            career = Career.Ranks.Load();
            levels = CampaignLevel.All(data).Values.ToList();
            Stage();
            ApplyQuality(quality);
        }

        void Stage()
        {
            stage = new GameObject("Stage").transform;
            var look = data.Map["hanabi"];
            // the plinth: Kagura's paving on a stone drum, a lit cyan ring
            var bins = new MeshBins();
            bins["stone"].Cylinder(new Vector3(0, -0.6f, 0), 2.4f, 2.2f, 0.6f, 40);
            bins["trim"].Cylinder(new Vector3(0, -0.09f, 0), 2.43f, 2.43f, 0.05f, 48, top: false);   // a thin lit band round the rim
            bins["ground"].Cylinder(new Vector3(0, -40, 0), 60, 60, 39.4f, 48);
            bins.Emit(stage, k =>
            {
                var m = k == "stone" ? Resources.Load<Material>("ZUEnv/common_stone") : k == "ground" ? Resources.Load<Material>("ZUEnv/hanabi_ground") : null;
                if (m != null) return m;
                var e = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                e.SetColor("_BaseColor", new Color(0.05f, 0.08f, 0.1f)); e.EnableKeyword("_EMISSION"); e.SetColor("_EmissionColor", UiStyle.Zenith * 2.2f);
                return e;
            });
            var sun = new GameObject("ZU Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.color = new Color(1f, 0.78f, 0.62f); sun.intensity = 1.3f;
            sun.transform.rotation = Quaternion.Euler(28, -150, 0);
            RenderSettings.sun = sun;
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional; rim.color = new Color(0.55f, 0.6f, 1f); rim.intensity = 0.9f; rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.Euler(15, 30, 0);
            EnvKit.Apply(look, stage, 400);
            RenderSettings.fogStartDistance = 30; RenderSettings.fogEndDistance = 140;
            var cam = Camera.main;
            // the hero stands in the upper middle of the frame, clear of the kit panel at the bottom
            if (cam != null) { cam.transform.position = new Vector3(0, 1.2f, 5.4f); cam.transform.LookAt(new Vector3(0, 0.55f, 0)); cam.fieldOfView = 40; }
        }

        void Show(string id)
        {
            if (shown == id) return;
            shown = id;
            if (model != null) Destroy(model);
            var lib = HeroLibrary.Get();
            var e = lib != null ? lib.Find(id) : null;
            if (e == null || e.prefab == null) return;
            model = Instantiate(e.prefab, stage);
            model.transform.localPosition = Vector3.zero;
            var anim = model.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = e.controller != null ? e.controller : lib.baseController;
                anim.applyRootMotion = false;
                anim.SetBool("Grounded", true);
            }
            // a tall mech or monster would fill the frame: fit the hero to the plinth's camera
            var def = data.Def(id);
            float h = def != null ? (float)def.height : 1.8f;
            model.transform.localScale = Vector3.one * Mathf.Min(1f, 2.1f / Mathf.Max(0.5f, h));
        }

        void Update()
        {
            Show(MatchSettings.Hero);
            // +Z is the model's front: face the camera, turning a little to show the silhouette
            if (model != null) { spin += Time.deltaTime * 14f; model.transform.localRotation = Quaternion.Euler(0, Mathf.Sin(spin * Mathf.Deg2Rad * 2.2f) * 28f, 0); }
        }

        void OnGUI()
        {
            var cv = UiStyle.Canvas();
            float s = 1, W = cv.x, H = cv.y, pad = 28 * s;
            // title
            GUI.Label(new Rect(pad, pad * 0.6f, 900 * s, 70 * s), "ZENITH<color=#5cc8ff>//</color>UMBRA", new GUIStyle(UiStyle.Title) { richText = true });
            GUI.Label(new Rect(pad + 4 * s, pad * 0.6f + 66 * s, 600 * s, 30 * s), "UNITY EDITION", UiStyle.H2);
            if (career != null)
            {
                // the career strip: the rank per role (Overwatch 2's role queue), quick play's record
                float cx = 700 * s;
                foreach (var role in new[] { "tank", "damage", "support" })
                {
                    var rr = career.roles[role]; var v = Career.Ranks.RankOf(rr.rating, rr.games);
                    GUI.Label(new Rect(cx, pad * 0.6f + 20 * s, 240 * s, 22 * s), role.ToUpperInvariant(), UiStyle.Small);
                    string pct = v.placed ? "  " + v.pct.ToString("0") + "%" : "";
                    GUI.Label(new Rect(cx, pad * 0.6f + 40 * s, 240 * s, 30 * s), "<color=#" + ColorUtility.ToHtmlStringRGB(v.color) + ">" + v.label + "</color>" + pct, new GUIStyle(UiStyle.Body) { richText = true, fontStyle = FontStyle.Bold });
                    cx += 230 * s;
                }
                GUI.Label(new Rect(cx, pad * 0.6f + 20 * s, 300 * s, 22 * s), "QUICK PLAY", UiStyle.Small);
                GUI.Label(new Rect(cx, pad * 0.6f + 40 * s, 300 * s, 30 * s), career.qp.wins + "W  " + (career.qp.games - career.qp.wins) + "L", UiStyle.Body);
            }

            // roster
            var left = new Rect(pad, 140 * s, 330 * s, H - 140 * s - pad);
            UiStyle.Panel_(left);
            GUI.Label(new Rect(left.x + 16 * s, left.y + 10 * s, left.width, 34 * s), "HEROES", UiStyle.H2);
            var inner = new Rect(left.x + 10 * s, left.y + 48 * s, left.width - 20 * s, left.height - 58 * s);
            float rowH = 40 * s;
            var groups = heroes.GroupBy(h => h.role).OrderBy(g => g.Key == "tank" ? 0 : g.Key == "support" ? 2 : 1).ToList();
            float contentH = groups.Sum(g => 30 * s + g.Count() * (rowH + 4 * s));
            heroScroll = GUI.BeginScrollView(inner, heroScroll, new Rect(0, 0, inner.width - 18 * s, contentH));
            float y = 0;
            foreach (var g in groups)
            {
                GUI.Label(new Rect(4 * s, y, 300 * s, 26 * s), (g.Key ?? "hero").ToUpperInvariant(), UiStyle.Small); y += 30 * s;
                foreach (var h in g)
                {
                    var r = new Rect(0, y, inner.width - 22 * s, rowH);
                    bool on = MatchSettings.Hero == h.id;
                    if (GUI.Button(r, "    " + h.name, on ? UiStyle.ButtonOn : UiStyle.Button)) MatchSettings.Hero = h.id;
                    UiStyle.Box(new Rect(r.x + 8 * s, r.y + 10 * s, 6 * s, rowH - 20 * s), Conv.Hex(h.color, Color.white));
                    y += rowH + 4 * s;
                }
            }
            GUI.EndScrollView();

            // the hero's kit
            var def = data.Def(MatchSettings.Hero);
            if (def != null)
            {
                var kit = new Rect(left.xMax + pad, H - 262 * s - pad, W - 2 * (330 * s + 2 * pad), 262 * s);
                UiStyle.Panel_(kit);
                GUI.Label(new Rect(kit.x + 20 * s, kit.y + 10 * s, kit.width, 40 * s), def.name.ToUpperInvariant(), UiStyle.H1);
                GUI.Label(new Rect(kit.x + 22 * s, kit.y + 50 * s, kit.width, 26 * s), $"{def.title}  ·  {(def.role ?? "").ToUpperInvariant()}  ·  {def.hp + def.armor:0} HP", UiStyle.Small);
                float ky = kit.y + 80 * s, colW = (kit.width - 60 * s) / 2;
                int i = 0;
                foreach (var (slot, label) in new[] { (def.primary, "PRIMARY"), (def.secondary, "SECONDARY"), (def.ability1, def.ability1?.key ?? "SHIFT"), (def.ability2, def.ability2?.key ?? "E"), (def.ult, "ULT · " + (def.ult?.key ?? "Q")) })
                {
                    if (slot == null || string.IsNullOrEmpty(slot.name)) continue;
                    float x = kit.x + 20 * s + (i % 2) * (colW + 20 * s), yy = ky + (i / 2) * 58 * s;
                    GUI.Label(new Rect(x, yy, colW, 22 * s), $"<color=#5cc8ff>{label}</color>  <b>{slot.name}</b>", new GUIStyle(UiStyle.Body) { richText = true });
                    GUI.Label(new Rect(x, yy + 21 * s, colW, 36 * s), slot.desc ?? slot.note ?? "", UiStyle.Small);
                    i++;
                }
                if (def.passive != null && i < 6)
                {
                    float x = kit.x + 20 * s + (i % 2) * (colW + 20 * s), yy = ky + (i / 2) * 58 * s;
                    GUI.Label(new Rect(x, yy, colW, 22 * s), $"<color=#5cc8ff>PASSIVE</color>  <b>{def.passive.name}</b>", new GUIStyle(UiStyle.Body) { richText = true });
                    GUI.Label(new Rect(x, yy + 21 * s, colW, 36 * s), def.passive.desc ?? "", UiStyle.Small);
                }
            }

            // match setup
            var right = new Rect(W - 330 * s - pad, 140 * s, 330 * s, H - 140 * s - pad);
            UiStyle.Panel_(right);
            float ry = right.y + 10 * s, rx = right.x + 14 * s, rw = right.width - 28 * s;
            GUI.Label(new Rect(rx, ry, rw, 30 * s), "MAP", UiStyle.H2); ry += 34 * s;
            float fixedH = (32 + MODES.Length * 38 + 32 + 40 + 32 + 36 + 36 + 150 + 16) * s;
            float listH = Mathf.Max(110 * s, right.yMax - ry - fixedH);
            var mapRect = new Rect(rx, ry, rw, listH);
            // the campaign lists its five levels instead of the maps
            bool camp = MatchSettings.Mode == "campaign";
            int rows = camp ? levels.Count : maps.Count;
            mapScroll = GUI.BeginScrollView(mapRect, mapScroll, new Rect(0, 0, rw - 18 * s, rows * 38 * s));
            for (int k = 0; k < rows; k++)
            {
                string id = camp ? levels[k].id : maps[k].id, label = camp ? (k + 1) + ". " + levels[k].name : maps[k].name;
                bool on = camp ? MatchSettings.Level == id : MatchSettings.Map == id;
                if (GUI.Button(new Rect(0, k * 38 * s, rw - 22 * s, 34 * s), label, on ? UiStyle.ButtonOn : UiStyle.Button)) { if (camp) MatchSettings.Level = id; else MatchSettings.Map = id; }
            }
            GUI.EndScrollView();
            ry += listH + 10 * s;
            GUI.Label(new Rect(rx, ry, rw, 30 * s), "MODE", UiStyle.H2); ry += 32 * s;
            foreach (var (id, label) in MODES)
            {
                if (GUI.Button(new Rect(rx, ry, rw, 34 * s), label, MatchSettings.Mode == id ? UiStyle.ButtonOn : UiStyle.Button)) MatchSettings.Mode = id;
                ry += 38 * s;
            }
            GUI.Label(new Rect(rx, ry, rw, 30 * s), "BOTS", UiStyle.H2); ry += 32 * s;
            float bw = (rw - 9 * s) / 4;
            for (int k = 0; k < SKILLS.Length; k++)
            {
                var st = new GUIStyle(Mathf.Abs(MatchSettings.Skill - SKILLS[k].skill) < 0.01f ? UiStyle.ButtonOn : UiStyle.Button) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(13 * s) };
                if (GUI.Button(new Rect(rx + k * (bw + 3 * s), ry, bw, 32 * s), SKILLS[k].label, st)) MatchSettings.Skill = SKILLS[k].skill;
            }
            ry += 40 * s;
            GUI.Label(new Rect(rx, ry, rw, 30 * s), "VIEW · QUALITY", UiStyle.H2); ry += 32 * s;
            var cst = new GUIStyle(UiStyle.Button) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(13 * s) };
            float hw = (rw - 4 * s) / 2;
            if (GUI.Button(new Rect(rx, ry, hw, 32 * s), "FIRST PERSON", !MatchSettings.Third ? new GUIStyle(cst) { normal = UiStyle.ButtonOn.normal } : cst)) MatchSettings.Third = false;
            if (GUI.Button(new Rect(rx + hw + 4 * s, ry, hw, 32 * s), "THIRD PERSON", MatchSettings.Third ? new GUIStyle(cst) { normal = UiStyle.ButtonOn.normal } : cst)) MatchSettings.Third = true;
            ry += 36 * s;
            string[] q = { "LOW", "MEDIUM", "HIGH" };
            float qw = (rw - 6 * s) / 3;
            for (int k = 0; k < 3; k++)
                if (GUI.Button(new Rect(rx + k * (qw + 3 * s), ry, qw, 32 * s), q[k], quality == k ? new GUIStyle(cst) { normal = UiStyle.ButtonOn.normal } : cst)) { quality = k; ApplyQuality(k); }
            if (GUI.Button(new Rect(right.x + 14 * s, right.yMax - 140 * s, rw, 74 * s), "PLAY", UiStyle.Big))
            {
                float skill = MatchSettings.Skill;
                if (MatchSettings.Mode == "competitive")
                {
                    // ranked: the bots play at your role's matchmaking rating; the lobby is rated around it
                    var hd = data.Def(MatchSettings.Hero);
                    string role = Career.Ranks.ROLE_OF.TryGetValue(hd?.role ?? "dps", out var rr) ? rr : "damage";
                    MatchSettings.Opp = Career.Ranks.LobbyRating(career.roles[role].mmr);
                    skill = (float)Career.Ranks.SkillFor(MatchSettings.Opp);
                }
                else if (MatchSettings.Mode == "quickplay") MatchSettings.Opp = Career.Ranks.LobbyRating(career.qp.mmr);
                // Stadium is the third-person mode
                bool third = MatchSettings.Mode == "stadium" || MatchSettings.Third;
                MatchSettings.Start(MatchSettings.Mode == "campaign" ? MatchSettings.Level : MatchSettings.Map, MatchSettings.Hero, MatchSettings.Mode, skill, third);
            }
            if (GUI.Button(new Rect(right.x + 14 * s, right.yMax - 58 * s, rw * 0.6f, 44 * s), "SPECTATE", new GUIStyle(UiStyle.Button) { alignment = TextAnchor.MiddleCenter }))
                MatchSettings.Start(MatchSettings.Map, "", "quickplay", MatchSettings.Skill, true);
            if (GUI.Button(new Rect(right.x + 14 * s + rw * 0.62f, right.yMax - 58 * s, rw * 0.38f, 44 * s), "QUIT", new GUIStyle(UiStyle.Button) { alignment = TextAnchor.MiddleCenter }))
                Application.Quit();
        }

        /// <summary>LOW / MEDIUM / HIGH: render scale, shadow distance, MSAA on the active URP asset</summary>
        public static void ApplyQuality(int level)
        {
            // (the player changes its in-memory asset; in the editor that would rewrite the project's URP asset)
            if (Application.isEditor) return;
            var a = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a == null) return;
            a.renderScale = level == 0 ? 0.75f : 1f;
            a.shadowDistance = level == 0 ? 60 : level == 1 ? 110 : 160;
            a.msaaSampleCount = level == 2 ? 4 : level == 1 ? 2 : 1;
        }
    }
}
