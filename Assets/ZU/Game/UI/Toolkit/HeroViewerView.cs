// Hero viewer (src/client/HeroViewer.ts): a studio-lit turntable for every hero, villain, pilot and campaign model,
// with animation states (treadmill locomotion so the legs and the hair / cloth springs can be inspected), the skins
// locker, the kit, and the way into the Ult Viewer. The studio renders into a texture the stage shows (its own camera
// and lights, built when the viewer opens and gone when it closes); the model's Animator gets the same parameters the
// match's HeroView drives.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class HeroViewerView
    {
        static readonly string[] MODES = { "idle", "walk", "run", "strafe", "back", "attack", "alt", "melee", "shift", "e", "ult", "jump", "fly", "swoop", "descend", "superjump", "hit" };
        static readonly int VelX = Animator.StringToHash("VelX"), VelZ = Animator.StringToHash("VelZ"), Speed = Animator.StringToHash("Speed"), VelY = Animator.StringToHash("VelY"),
            Grounded = Animator.StringToHash("Grounded"), Dead = Animator.StringToHash("Dead"), Fly = Animator.StringToHash("Fly"), Jump = Animator.StringToHash("Jump"),
            Land = Animator.StringToHash("Land"), Shoot = Animator.StringToHash("Shoot"), Cast = Animator.StringToHash("Cast"), Melee = Animator.StringToHash("Melee"), Hit = Animator.StringToHash("Hit");

        readonly VisualElement el, stage, skins, info, vname, list;
        readonly Label clipHint;
        readonly ZButton ultBtn;
        readonly Action onClose;
        readonly GameData d = ZuData.Get();
        readonly List<HeroDef> roster, extra;
        readonly Dictionary<string, HeroDef> all = new Dictionary<string, HeroDef>();
        string id = "tenkai", mode = "idle";
        float yaw = 0.5f, tilt = 0.12f, zoom = 1, t;
        bool auto = true;
        Vector2? drag;

        // the studio
        GameObject studio, model;
        Camera cam;
        RenderTexture rt;
        Animator anim;
        Transform ring;
        Bounds fit; float fitAge;
        string skinShown;

        HeroViewerView(VisualElement parent, Action onClose)
        {
            this.onClose = onClose;
            roster = MenuState.Roster(d);
            extra = Extras();
            foreach (var h in roster.Concat(extra)) all[h.id] = h;
            el = U.Div("viewer", parent, pick: true);
            Grad.Fill(Grad.Radial(50, 35, (Grad.C("#2a2f4a"), 0), (Grad.C("#0c0e18"), 60), (Grad.C("#05060a"), 100)), el);
            // the hero list
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.AddToClassList("vlist"); el.Add(sv);
            list = sv.contentContainer;
            void Group(string title, string cls, IEnumerable<HeroDef> hs)
            {
                U.Txt(title, "v-h3 " + cls, list);
                var row = U.Div("vrow", list);
                foreach (var h in hs) Chip(row, h);
            }
            Group(U.Up(MenuState.TEAM_NAME["zenith"]), "zenith", roster.Where(h => h.team == "zenith"));
            Group(U.Up(MenuState.TEAM_NAME["umbra"]), "umbra", roster.Where(h => h.team == "umbra"));
            Group("PILOTS & CAMPAIGN", "", extra);
            // the stage: the studio's picture, the name, the animation buttons, the hints
            stage = U.Div("vstage", el, pick: true);
            vname = U.Div("vname", stage);
            var anims = U.Div("vanims", stage);
            foreach (var m in MODES)
            {
                string label = m == "alt" ? "ALT" : m == "melee" ? "MELEE (C)" : m == "shift" ? "SHIFT" : m == "e" ? "E" : U.Up(m);
                var b = U.Btn(label, "va", () => SetMode(m), anims); b.userData = m;
            }
            U.Btn("⟳ AUTO", "va spin", () => auto = !auto, anims);
            U.Txt("Drag to rotate · wheel to zoom · double-click to reset", "vhint", stage);
            clipHint = U.Txt("", "vhint vclip", stage);
            stage.RegisterCallback<PointerDownEvent>(e => { if (e.target == stage) { drag = e.position; auto = false; stage.CapturePointer(e.pointerId); } });
            stage.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (drag == null) return;
                var dp = (Vector2)e.position - drag.Value;
                yaw -= dp.x * 0.01f; tilt = Mathf.Clamp(tilt + dp.y * 0.005f, -0.25f, 0.9f);
                drag = e.position;
            });
            stage.RegisterCallback<PointerUpEvent>(e => { drag = null; stage.ReleasePointer(e.pointerId); });
            stage.RegisterCallback<WheelEvent>(e => { zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? 1.1f : 0.9f), 0.3f, 2.2f); e.StopPropagation(); });
            stage.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) { yaw = 0.5f; tilt = 0.12f; zoom = 1; auto = true; } });
            stage.RegisterCallback<GeometryChangedEvent>(_ => Resize());
            // the side: skins, the kit, ULT VIEWER, BACK
            var side = new ScrollView(ScrollViewMode.Vertical); side.AddToClassList("vside"); el.Add(side);
            skins = U.Div("vskins", side.contentContainer);
            info = U.Div("vinfo", side.contentContainer);
            ultBtn = U.Btn("▶ ULT VIEWER", "primary vult", () => OpenUlt(id), side.contentContainer);
            U.Btn("BACK", "vback", Close, side.contentContainer);
            BuildStudio();
            UiRoot.Get().Tick += Frame;
        }

        public static HeroViewerView Open(VisualElement parent, string id, Action onClose)
        {
            var v = new HeroViewerView(parent, onClose);
            v.Select(string.IsNullOrEmpty(id) || !v.all.ContainsKey(id) ? "tenkai" : id);
            return v;
        }

        /// <summary>the pilots and the campaign's models (HeroViewer.ts EXTRA)</summary>
        List<HeroDef> Extras()
        {
            var o = new List<HeroDef>();
            if (d.Pilots.TryGetValue("tenkai", out var haruto)) { var x = haruto.Clone(); x.lore = d.Def("tenkai")?.pilot?.bio ?? x.lore; o.Add(x); }
            var gorgoth = d.Def("gorgoth");
            if (gorgoth != null) { var v = gorgoth.Clone(); v.id = "vorn"; v.name = "Warlord Vorn"; v.title = "Gorgoth's pilot"; v.frame = "human"; v.height = 1.85; v.radius = 0.4; v.lore = gorgoth.pilot?.bio; v.pilot = null; o.Add(v); }
            if (d.Boss.TryGetValue("qelvaris", out var q)) { var x = q.Clone(); x.lore = "The Umbra Syndicate's alien scientist - builder of the space colossi in Operation Starfall."; o.Add(x); }
            foreach (var b in new[] { "boss_ironmaw", "boss_reaper", "boss_leviathan", "boss_phoenix", "boss_genesis" })
                if (d.Boss.TryGetValue(b, out var bd)) { var x = bd.Clone(); x.lore = $"Campaign colossus. Weak point: {bd.weak}."; o.Add(x); }
            foreach (var e in d.Enemies) { var x = e.Clone(); x.lore = $"{e.title}: one of the Star-Forger's mass-produced robots in Operation Starfall."; o.Add(x); }
            return o;
        }

        void Chip(VisualElement row, HeroDef h)
        {
            var b = U.Btn(null, "vchip", () => Select(h.id), row);
            b.userData = h.id;
            var img = U.Pic("portrait_" + h.id, "vc-img", b, "key_" + h.id);
            img.style.borderTopColor = img.style.borderBottomColor = img.style.borderLeftColor = img.style.borderRightColor = U.Hex(h.color ?? "#ffffff");
            U.Txt(h.name, "vc-t", b);
        }

        // ------------------------------------------------------------------ the studio
        void BuildStudio()
        {
            studio = new GameObject("ZU Hero Viewer Studio");
            studio.transform.position = new Vector3(0, -2000, 0);          // far below whatever the scene holds
            var camGo = new GameObject("Viewer Camera"); camGo.transform.SetParent(studio.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32; cam.nearClipPlane = 0.05f; cam.farClipPlane = 400;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.allowHDR = false; cam.allowMSAA = true;
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = false; cd.renderShadows = true;
            Light L(string n, Color c, float i, Vector3 euler, bool shadows)
            {
                var g = new GameObject(n); g.transform.SetParent(studio.transform, false);
                var l = g.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = i; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                g.transform.rotation = Quaternion.Euler(euler);
                return l;
            }
            // (three.js lights are pi x the URP ones: the TS key 2.6 / rim 2.2 / fill 0.7 over pi)
            L("Key", Grad.C("#fff4e6"), 2.6f / Mathf.PI * 1.6f, new Vector3(45, -145, 0), true);
            L("Rim", Grad.C("#7fb8ff"), 2.2f / Mathf.PI * 1.6f, new Vector3(35, 40, 0), false);
            L("Fill", Grad.C("#ffd2e6"), 0.7f / Mathf.PI * 1.6f, new Vector3(15, -50, 0), false);
            // the gold ring on the floor
            var rg = new GameObject("Ring"); rg.transform.SetParent(studio.transform, false); rg.transform.localPosition = new Vector3(0, 0.005f, 0);
            var lr = rg.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 96; lr.widthMultiplier = 0.06f;
            for (int i = 0; i < 96; i++) { float a = i / 96f * Mathf.PI * 2; lr.SetPosition(i, new Vector3(Mathf.Cos(a) * 1.63f, 0, Mathf.Sin(a) * 1.63f)); }
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(sh); var gold = Grad.C("#ffd76a", 0.5f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", gold); else mat.color = gold;
            lr.sharedMaterial = mat; lr.alignment = LineAlignment.TransformZ; rg.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ring = rg.transform;
        }

        void Resize()
        {
            var r = stage.contentRect;
            if (r.width < 2 || r.height < 2 || cam == null) return;
            // the panel is laid out in 1080p canvas pixels: the texture at the screen's own resolution
            float k = Screen.height / 1080f;
            int w = Mathf.Max(64, Mathf.RoundToInt(r.width * k)), h = Mathf.Max(64, Mathf.RoundToInt(r.height * k));
            if (rt != null && rt.width == w && rt.height == h) return;
            if (rt != null) { cam.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt); }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Hero Viewer" };
            cam.targetTexture = rt; cam.aspect = (float)w / h;
            stage.style.backgroundImage = Background.FromRenderTexture(rt);
        }

        // ------------------------------------------------------------------ selection
        void Select(string heroId)
        {
            id = heroId;
            var def = all[heroId];
            foreach (var c in list.Query<VisualElement>(className: "vchip").ToList()) U.Toggle(c, "sel", (string)c.userData == heroId);
            vname.Clear();
            U.Txt(def.name, "vn-b", vname).style.color = U.Hex(def.color ?? "#ffffff");
            U.Txt(U.Up(def.title), "vn-s", vname);
            skinShown = null;
            ShowModel(EquippedSkin(heroId));
            RenderSkins(); RenderInfo(def);
            // the Ult Viewer plays the playable heroes' ultimates (not pilots / campaign models)
            U.Show(ultBtn, d.Hero.ContainsKey(heroId));
            SetMode("idle");
        }

        void ShowModel(string skinId)
        {
            if (model != null) UnityEngine.Object.Destroy(model);
            model = null; anim = null; fitAge = 0;
            var lib = HeroLibrary.Get();
            string modelId = id;
            var sk = SkinsFor(id).FirstOrDefault(s => s.id == skinId);
            if (sk != null && !string.IsNullOrEmpty(sk.model) && lib?.Find(sk.model) != null) modelId = sk.model;     // model skins (Hibiki's Bassline Armor)
            var e = lib?.Find(modelId);
            skinShown = skinId;
            if (e == null) { U.Set(clipHint, "model not imported yet"); return; }
            model = UnityEngine.Object.Instantiate(e.prefab, studio.transform);
            model.transform.localPosition = Vector3.zero;
            anim = model.GetComponentInChildren<Animator>();
            if (anim != null) { anim.runtimeAnimatorController = e.controller != null ? e.controller : lib.baseController; anim.applyRootMotion = false; anim.SetBool(Grounded, true); }
            var def = all[id];
            ring.localScale = Vector3.one * Mathf.Max(1, (float)def.radius * 1.6f);
            U.Set(clipHint, "");
        }

        void SetMode(string m)
        {
            mode = m; t = 0;
            foreach (var b in stage.Query<VisualElement>(className: "va").ToList()) U.Toggle(b, "on", b.userData as string == m);
            if (anim != null) { anim.SetBool(Fly, false); anim.SetBool(Grounded, true); anim.SetFloat(VelY, 0); }
        }

        // ------------------------------------------------------------------ skins
        List<Skin> SkinsFor(string heroId) => d.Skins.TryGetValue(heroId, out var l) ? l : new List<Skin>();
        public static string EquippedSkin(string heroId) => PlayerPrefs.GetString("zu-skin-" + heroId, "classic");
        public static void EquipSkin(string heroId, string skin) { PlayerPrefs.SetString("zu-skin-" + heroId, skin); PlayerPrefs.Save(); }
        /// <summary>the model a hero's equipped skin swaps in (null: the hero's own) - for the match's ActorViews.SkinModel</summary>
        public static string EquippedSkinModel(string heroId)
        {
            var d = ZuData.Get(); string eq = EquippedSkin(heroId);
            return d.Skins.TryGetValue(heroId, out var l) ? l.FirstOrDefault(s => s.id == eq)?.model : null;
        }

        void RenderSkins()
        {
            skins.Clear();
            U.Txt("SKINS", "vs-h4", skins);
            if (!d.Hero.ContainsKey(id)) { U.Txt("Skins are available for the playable heroes.", "dim", skins); return; }
            var def = all[id]; string eq = EquippedSkin(id);
            foreach (var s in SkinsFor(id))
            {
                var b = U.Btn(null, $"skin r-{(s.rarity ?? "").ToLowerInvariant()}{(s.id == skinShown ? " sel" : "")}", () => { ShowModel(s.id); Sfx("ui_click"); RenderSkins(); }, skins);
                var sw = U.Div("sk-i", b);
                Grad.Set(sw, Grad.Linear(90, (U.Hex(s.primary ?? def.color ?? "#ffffff"), 0), (U.Hex(s.accent ?? (s.pattern > 0 ? s.patternColor : "#222222")), 100)));
                var tx = U.Div("sk-t", b);
                U.Txt(s.name, "sk-n", tx);
                U.Txt(U.Up(s.rarity) + (s.id == eq ? " · EQUIPPED" : ""), "sk-r", tx);
            }
            U.Btn("EQUIP SELECTED", "primary equip", () => { EquipSkin(id, skinShown ?? "classic"); Sfx("capture"); RenderSkins(); }, skins);
        }

        void RenderInfo(HeroDef h)
        {
            info.Clear();
            bool playable = d.Hero.ContainsKey(h.id);
            U.Txt(playable ? $"{MenuState.TEAM_NAME[h.team]} · {U.Up(h.role)} · {h.hp + h.armor:0} HP{(h.frame == "mech" ? " · MECHA" : "")}{(h.frame == "flyer" ? " · FLYER" : "")}" : "Non-playable", "vmeta", info);
            U.Txt(h.lore ?? "", "lore", info);
            if (!playable) return;
            void Row(string k, string n, string desc, string counter = null)
            {
                var r = U.Div("vab", info); U.Txt(k, "kbd", r);
                var tx = U.Div("vab-t", r); U.Txt(n, "vab-b", tx); U.Txt(desc ?? "", "vab-p", tx);
                if (!string.IsNullOrEmpty(counter)) U.Txt("⚔ " + counter, "vab-p ctr", tx);
            }
            var S = h.secondary;
            if (S != null) Row("RMB", S.IsAbility ? S.name : S.heal ? "Healing" : "Alt fire", S.IsAbility ? S.desc : $"{S.damage:0.##}", S.IsAbility ? S.counter : null);
            if (h.ability1 != null) Row("SHIFT", h.ability1.name, h.ability1.desc, h.ability1.counter);
            if (h.ability2 != null) Row("E", h.ability2.name, h.ability2.desc, h.ability2.counter);
            if (h.ult != null) Row("Q", h.ult.name, h.ult.desc);
        }

        static void Sfx(string s) { try { if (Audio.AudioKit.Has(s)) Audio.AudioKit.Play(s, null); } catch (Exception) { /* no bank */ } }

        /// <summary>the Ult Viewer (Fx/UltShowcase on the ability-visuals branch: found by name so this compiles before it merges)</summary>
        void OpenUlt(string heroId)
        {
            var type = Type.GetType("ZU.Game.Fx.UltShowcase, ZU.Game");
            var open = type?.GetMethod("Open", new[] { typeof(string) });
            if (open == null) { U.Set(clipHint, "the Ult Viewer isn't in this build yet"); return; }
            Sfx("ui_click");
            Dispose();
            open.Invoke(null, new object[] { heroId });
        }

        // ------------------------------------------------------------------ frame
        void Frame()
        {
            if (studio == null) return;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            t += dt;
            if (auto) yaw += dt * 0.35f;
            if (anim != null) Drive(dt);
            // camera orbit around the model, the distance fitted to the posed bounds (the whole body in frame, feet clear of
            // the animation buttons along the bottom of the stage)
            if (model != null && fitAge < 0.6f)
            {
                fitAge += dt;
                var rs = model.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); fit = b; }
            }
            var o = studio.transform.position;
            float minY = Mathf.Min(0, fit.min.y - o.y), maxY = Mathf.Max(0.5f, fit.max.y - o.y);
            float rad = Mathf.Max(0.3f, Mathf.Max(fit.extents.x, fit.extents.z));
            float tanH = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2), fh = maxY - minY;
            float fitR = Mathf.Max(fh / 0.74f, 2.25f * rad / Mathf.Max(0.5f, cam.aspect)) / (2 * tanH) + rad * 0.5f;
            float R = Mathf.Max(2.4f, fitR) * zoom, vh = 2 * tanH * Mathf.Max(2.4f, fitR);
            float zk = Mathf.Clamp01((1 - zoom) / 0.65f);
            float headY = maxY * 0.92f;
            float body = Mathf.Max(minY + vh * 0.31f, (minY + maxY) / 2);
            bool air = mode == "fly" || mode == "swoop" || mode == "descend" || mode == "superjump";
            float focus = body * (1 - zk) + headY * zk + (air ? 1.2f : 0);
            var pos = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(tilt) * R, focus + Mathf.Sin(tilt) * R, Mathf.Cos(yaw) * Mathf.Cos(tilt) * R);
            cam.transform.position = o + pos;
            cam.transform.LookAt(o + new Vector3(0, focus, 0));
            if (model != null) model.transform.localPosition = new Vector3(0, air ? 1.2f + Mathf.Sin(t * 1.5f) * 0.2f : mode == "jump" ? Mathf.Sin(t % 1.2f / 1.2f * Mathf.PI) * 1.4f : 0, 0);
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                var ci = anim.GetCurrentAnimatorClipInfo(0);
                U.Set(clipHint, ci.Length > 0 && ci[0].clip != null ? "clip: " + ci[0].clip.name : "");
            }
        }

        /// <summary>the animation state as the match's HeroView would set it for an actor doing this (a treadmill: the
        /// actor moves, the stage keeps it centred)</summary>
        void Drive(float dt)
        {
            var def = all[id];
            float sp = (float)def.speed;
            float speed = mode == "walk" ? sp * 0.45f : mode == "run" || mode == "fly" || mode == "strafe" || mode == "back" ? sp : 0;
            var v = mode == "strafe" ? new Vector2(speed, 0) : mode == "back" ? new Vector2(0, -speed) : new Vector2(0, speed);
            anim.SetFloat(VelX, v.x); anim.SetFloat(VelZ, v.y); anim.SetFloat(Speed, v.magnitude);
            bool air = mode == "jump" || mode == "fly" || mode == "swoop" || mode == "descend" || mode == "superjump";
            anim.SetBool(Grounded, !air);
            anim.SetBool(Fly, mode == "fly" || mode == "swoop" || mode == "descend");
            anim.SetBool(Dead, false);
            bool Every(float s) => t % s < dt;
            float swing = def.primary != null && def.primary.sweep ? 0.75f : 0.6f;
            switch (mode)
            {
                case "attack": if (Every(swing)) anim.SetTrigger(def.primary?.kind == "melee" ? Melee : Shoot); break;
                case "alt": if (Every(1.1f)) anim.SetTrigger(Shoot); break;
                case "melee": if (Every(0.9f)) anim.SetTrigger(Melee); break;
                case "shift": if (Every(1.4f)) anim.SetTrigger(Cast); break;
                case "e": if (Every(1.6f)) anim.SetTrigger(Cast); break;
                case "ult": if (Every(1.6f)) anim.SetTrigger(Cast); break;
                case "hit": if (Every(0.8f)) anim.SetTrigger(Hit); break;
                case "jump":
                {
                    float p = t % 1.2f / 1.2f;
                    anim.SetFloat(VelY, Mathf.Cos(p * Mathf.PI) * 6);
                    if (p < dt / 1.2f) anim.SetTrigger(Jump);
                    if (p > 0.97f) { anim.SetBool(Grounded, true); anim.SetTrigger(Land); }
                    break;
                }
                case "superjump": anim.SetFloat(VelY, t % 2.2f < 0.77f ? 12 : -2.2f); break;
                case "descend": anim.SetFloat(VelY, -2.2f); break;
            }
        }

        void Close() { Dispose(); onClose?.Invoke(); }

        /// <summary>tear down without going back to the menu (the Ult Viewer takes over the screen)</summary>
        void Dispose()
        {
            var ui = UiRoot.Existing; if (ui != null) ui.Tick -= Frame;
            if (studio != null) UnityEngine.Object.Destroy(studio);
            studio = null; model = null; anim = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            el.RemoveFromHierarchy();
        }
    }
}
