// Hero viewer (src/client/HeroViewer.ts): a studio-lit turntable for every hero, villain, pilot and campaign model,
// with animation states (treadmill locomotion so the legs and the hair / cloth springs can be inspected), the skins
// locker, the kit, and the way into the Ult Viewer. The studio renders into a texture the stage shows (its own camera
// and lights, built when the viewer opens and gone when it closes). As in the TS, a real Actor is driven through the
// match's own character view (ActorViews: the Animator, the procedural layer, held weapons, hair and cloth), with this
// screen as its IViewHost.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public sealed class HeroViewerView : IViewHost
    {
        static readonly string[] MODES = { "idle", "walk", "run", "strafe", "back", "attack", "alt", "melee", "shift", "e", "ult", "jump", "fly", "swoop", "descend", "superjump", "hit" };

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
        Actor actor;
        IActorView view;
        Transform ring;
        float fMin, fMax = 1.8f, fRad = 0.5f, fitAge;
        string skinShown;

        /// <summary>the viewer on screen (the UI tour closes it)</summary>
        public static HeroViewerView Current { get; private set; }
        public void CloseToTitle() => Close();

        HeroViewerView(VisualElement parent, Action onClose)
        {
            this.onClose = onClose;
            Current = this;
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

        /// <summary>a preview's model skin (null: the equipped one) - ActorViews.SkinModel asks this first</summary>
        public static (Actor actor, string model)? Preview;
        /// <summary>the skin hook the front end installs: the viewer's preview, else the equipped skin - on the local player only;
        /// bots and other players wear the hero's own model (TS Game.addView: a.isPlayer ? equippedSkin : 'classic')</summary>
        public static string SkinModelFor(Actor a) => Preview.HasValue && Preview.Value.actor == a ? Preview.Value.model : a.isPlayer ? EquippedSkinModel(a.baseDef.id) : null;

        void ShowModel(string skinId)
        {
            if (model != null) UnityEngine.Object.Destroy(model);
            model = null; view = null; fitAge = 0;
            skinShown = skinId;
            var def = all[id];
            var a = actor = new Actor(def, def.team ?? "zenith") { isPlayer = true, grounded = true };
            var sk = SkinsFor(id).FirstOrDefault(s => s.id == skinId);
            Preview = (a, sk != null && !string.IsNullOrEmpty(sk.model) ? sk.model : null);
            view = ActorViews.Create(a, studio.transform);
            model = (view as Component)?.gameObject;
            ring.localScale = Vector3.one * Mathf.Max(1, (float)def.radius * 1.6f);
            U.Set(clipHint, HeroLibrary.Get()?.Find(def.id) == null && (sk?.model == null) ? "model not imported yet" : "");
        }

        void SetMode(string m)
        {
            mode = m; t = 0;
            if (actor != null) { actor.scale = 1; actor.Clear("titan"); }
            foreach (var b in stage.Query<VisualElement>(className: "va").ToList()) U.Toggle(b, "on", b.userData as string == m);
        }

        // IViewHost: the turntable's clock, nobody's first-person view, the actor kept on the stage
        public double SimTime => t;
        public Actor Player => null;
        public bool ThirdPerson => true;
        public Vector3 DrawPos(Actor a) => studio.transform.position + new Vector3(0, (float)a.pos.y, 0);

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

        /// <summary>the Ult Viewer: the hero's ultimate played for real on the Proving Grounds (Fx/UltShowcase)</summary>
        void OpenUlt(string heroId)
        {
            Sfx("ui_click");
            Dispose();
            MenuState.ReturnTo = "viewer:" + heroId;
            Fx.UltShowcase.Open(heroId);
        }

        // ------------------------------------------------------------------ frame
        void Frame()
        {
            if (studio == null) return;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            t += dt;
            if (auto) yaw += dt * 0.35f;
            if (actor != null) { Drive(dt); view?.Sync(this, actor); }
            // camera orbit around the model, the distance fitted to the posed bounds (the whole body in frame, feet clear of
            // the animation buttons along the bottom of the stage)
            // posed bounds, re-measured when the model swaps and settled after the first frames, stored at scale 1 (the frame
            // multiplies by the actor's scale: Tenkai-Oh's giant preview)
            var o = studio.transform.position;
            float ks = actor != null ? (float)actor.scale : 1;
            if (model != null && fitAge < 0.6f)
            {
                fitAge += dt;
                var rs = model.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float gy = DrawPos(actor).y;
                    fMin = Mathf.Min(0, b.min.y - gy) / ks; fMax = (b.max.y - gy) / ks;
                    fRad = Mathf.Max(0.3f, Mathf.Max(Mathf.Max(Mathf.Abs(b.min.x - o.x), Mathf.Abs(b.max.x - o.x)), Mathf.Max(Mathf.Abs(b.min.z - o.z), Mathf.Abs(b.max.z - o.z)))) / ks;
                    ring.localScale = Vector3.one * Mathf.Max(1, Mathf.Max((float)actor.def.radius * 1.6f, fRad * 0.8f));
                }
            }
            float minY = fMin * ks, maxY = Mathf.Max(0.5f, fMax * ks), rad = fRad * ks;
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
            // which clip drives the body (the clip library) - or procedural
            var an = model != null ? model.GetComponentInChildren<Animator>() : null;
            if (an != null && an.runtimeAnimatorController != null)
            {
                var ci = an.GetCurrentAnimatorClipInfo(0);
                U.Set(clipHint, ci.Length > 0 && ci[0].clip != null ? "clip: " + ci[0].clip.name : "procedural (no clip for this state)");
            }
        }

        /// <summary>the actor state for the chosen animation (HeroViewer.ts frame): a treadmill - the actor walks, the stage
        /// keeps it centred; the cues (attack / cast / hit / jump / land) fire on the animation's own beat</summary>
        void Drive(float dt)
        {
            var a = actor; var def = a.def; string m = mode; double T = t;
            double speed = m == "walk" ? def.speed * 0.45 : m == "run" || m == "fly" || m == "strafe" || m == "back" ? def.speed : 0;
            a.yaw = 0; a.input.yaw = 0; a.pitch = 0;
            // strafe: sideways to the character's left; back: backpedal (the 8-way blend space)
            a.vel = m == "strafe" ? new V3(speed, 0, 0) : m == "back" ? new V3(0, 0, -speed) : new V3(0, 0, speed);
            a.pos.x += a.vel.x * dt; a.pos.z += a.vel.z * dt;
            a.grounded = m != "jump" && m != "fly" && m != "swoop" && m != "descend" && m != "superjump";
            a.flying = m == "fly" && (def.frame == "flyer" || def.frame == "drone" || def.jets.HasValue);
            // angelic flight previews (Mirei): a guardian-angel swoop that flares to a stop, the slow descent, a superjump into it
            a.Clear("swoop"); a.Clear("swoopflare"); a.Clear("angelglide"); a.Clear("superjump");
            if (m == "swoop")
            {
                double p = T % 1.8 / 1.8;
                a.pos.y = 1.2;
                if (p < 0.8) { a.Set("swoop", T, 0.1); a.sv["swoopProg"] = p / 0.8; a.vel = new V3(0, 0, 13 + p * 10); }
                else { a.st["swoopflare"] = T + 0.4 - (p - 0.8) * 1.8; a.vel = new V3(0, 1, 4); }
            }
            else if (m == "descend") { a.Set("angelglide", T, 0.2); a.vel = new V3(Math.Sin(T * 0.7) * 1.2, -2.2, 1); a.pos.y = 1.4; }
            else if (m == "superjump")
            {
                double p = T % 2.2 / 2.2;
                if (p < 0.35) { a.Set("superjump", T, 0.2); a.vel = new V3(0, 17 * (1 - p / 0.35) + 2, 0); a.pos.y = 0.3 + p * 4; }
                else { a.Set("angelglide", T, 0.2); a.vel = new V3(0, -2.2, 0.6); a.pos.y = 1.7 - (p - 0.35) * 1.2; }
            }
            if (m == "jump")
            {
                double p = T % 1.2 / 1.2;
                a.pos.y = Math.Sin(p * Math.PI) * 1.4; a.vel.y = Math.Cos(p * Math.PI) * 6; a.grounded = p > 0.97;
                if (p < 0.05) a.anim.jumpAt = T; if (p > 0.97) a.anim.landAt = T;
            }
            else if (m == "fly") { a.pos.y = 1.2 + Math.Sin(T * 1.5) * 0.2; a.vel.y = Math.Cos(T * 1.5) * 0.3; }
            else if (m != "swoop" && m != "descend" && m != "superjump") a.pos.y = 0;
            double swingEvery = def.primary != null && def.primary.sweep ? Anim.ProcAnimator.SWING_TIME + 0.1 : 0.6;
            bool Beat(double every) => T % every < dt;
            if (def.dualGuns)
            {
                bool firing = m == "attack" || m == "alt";
                a.sv["spin1"] = Math.Max(0, Math.Min(1, a.Sv("spin1") + (m == "attack" ? dt / 0.35 : -dt / 0.8)));
                a.sv["spin2"] = Math.Max(0, Math.Min(1, a.Sv("spin2") + (firing ? dt / 0.35 : -dt / 0.8)));
                if (firing && Beat(1.0 / 16)) { if (m == "attack") a.anim.fireL = T; a.anim.fireR = T; a.anim.attackAt = T; a.anim.attackKind = "primary"; }
            }
            else if (m == "attack" && Beat(swingEvery)) { a.anim.attackAt = T; a.anim.attackKind = "primary"; a.anim.attackSide = -a.anim.attackSide; }
            if (m == "melee" && Beat(0.9)) { a.anim.attackAt = T; a.anim.attackKind = "punch"; }
            // abilities: plays the cast (Tenkai-Oh: Dawn Charge pose on SHIFT, the overhead Solar Shatter slam on E)
            if (m == "shift")
            {
                if (def.ability1?.id == "dawncharge") a.forced = new Forced { vx = 0, vy = 0, vz = 0, until = 1e9, kind = "dawncharge" };
                else if (Beat(1.4)) { a.anim.castAt = T; a.anim.castId = def.ability1?.id ?? ""; }
            }
            else if (a.forced?.kind == "dawncharge") a.forced = null;
            if (m == "e" && Beat(1.6)) { a.anim.castAt = T; a.anim.castId = def.ability2?.id ?? ""; }
            // the deflects hold a guard stance while they last (Raijin's Thunder Parry, Hayate's Mirror Water)
            bool Guard(SlotDef x) => x != null && (x.id == "parry" || x.id == "mirrorwater");
            if ((m == "e" && Guard(def.ability2)) || (m == "shift" && Guard(def.ability1))) a.Set("parry", T, 0.2);
            if (m == "cast" && Beat(1.4)) a.anim.castAt = T;
            if (m == "ult")
            {
                // Tenkai-Oh previews the giant form; everyone else plays their ult cast
                if (def.ult?.id == "colossus") { a.Set("titan", T, 9999); a.scale += (World.TITAN_SCALE - a.scale) * Math.Min(1, dt * 2.6); }
                // Tomoe: the Crescent Warpath flight, looped - the 1.2 s twirl, a hop for the arc, a beat on the ground
                else if (def.ult?.id == "tide")
                {
                    double u = T % 2.6, dur = 1.2; bool fly = u < dur;
                    a.forced = fly ? new Forced { vx = 0, vy = 0, vz = 0, until = 1e9, kind = "tide", ignoreGravity = true } : null;
                    if (fly) { a.sv["tideT0"] = T - u; a.sv["tideDur"] = dur; a.sv["tideApex"] = 3; a.Set("tideult", T, 0.2); a.vel = new V3(0, 4 * 3 * (1 - 2 * u / dur) / dur, 20 / dur); a.pos.y = 4 * 0.9 * (u / dur) * (1 - u / dur); a.grounded = false; }
                    else { a.sv.Remove("tideT0"); a.grounded = true; }
                }
                else if (Beat(1.6)) { a.anim.castAt = T; a.anim.castId = def.ult?.id ?? ""; }
            }
            else if (a.forced?.kind == "tide") a.forced = null;
            if (m == "alt" && !def.dualGuns && Beat(1.1)) { a.anim.attackAt = T; a.anim.attackKind = "secondary"; }
            // SHIFT for Gantetsu: the Tachiai Rush (head down, guns tucked)
            if (m == "shift" && def.ability1?.id == "tachiai") { a.Set("tachiai", T, 0.1); a.vel = new V3(0, 0, def.speed * 1.85); }
            else a.Clear("tachiai");
            if (m == "hit" && Beat(0.8)) a.anim.hitAt = T;
            a.charging = false; a.beamOn = false;
        }

        void Close() { Dispose(); if (Current == this) Current = null; onClose?.Invoke(); }

        /// <summary>tear down without going back to the menu (the Ult Viewer takes over the screen)</summary>
        void Dispose()
        {
            var ui = UiRoot.Existing; if (ui != null) ui.Tick -= Frame;
            if (studio != null) UnityEngine.Object.Destroy(studio);
            studio = null; model = null; view = null; actor = null; Preview = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            el.RemoveFromHierarchy();
        }
    }
}
