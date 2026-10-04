// The UI Toolkit front end's root: one persistent panel (it survives the Menu -> Match scene switch, so the loading
// screen can stay up while the match builds) laid out on a 1920x1080 reference canvas that scales with the window's
// height - the PC game's HUD and menus are CSS pixels at 1080p. Layers, bottom to top: the match HUD, the menus
// (title, hero select, pause, results, options), and the overlay (loading screen). The stylesheet is
// Assets/ZU/UI/Resources/ZUUI/zu.uss, a rule-for-rule port of src/client/style.css.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ZU.Game.UI.Toolkit
{
    public sealed class UiRoot : MonoBehaviour
    {
        static UiRoot inst;
        UIDocument doc;

        /// <summary>the panel's root (class "zu": the page's font, colour and the :root variables)</summary>
        public VisualElement Root { get; private set; }
        public VisualElement HudLayer { get; private set; }
        public VisualElement MenuLayer { get; private set; }
        public VisualElement OverlayLayer { get; private set; }
        public IPanel Panel => Root?.panel;

        /// <summary>the panel if it exists (teardown code must not create one)</summary>
        public static UiRoot Existing => inst;

        /// <summary>called every frame (LateUpdate) - screens that animate or follow the simulation hook in here</summary>
        public event System.Action Tick;

        public static UiRoot Get()
        {
            if (inst != null) return inst;
            // built inactive so the document sees its panel settings on its first OnEnable
            var go = new GameObject("ZU UI Toolkit");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            inst = go.AddComponent<UiRoot>();
            inst.doc = go.AddComponent<UIDocument>();
            inst.doc.panelSettings = MakePanel();
            // linear project: the panel blends like the PC game's web page (CSS, on encoded values) - UiGamma.cs
            UiGamma.Attach(go, inst.doc.panelSettings);
            go.SetActive(true);
            inst.Build();
            return inst;
        }

        static PanelSettings MakePanel()
        {
            // a PanelSettings ASSET (Resources/ZUUI/ZUPanel, made in the Editor) carries the runtime shaders a player needs -
            // the colour-effect and blur filters, SDF text, sprites. CreateInstance leaves them null outside the Editor, and
            // every filter then threw on each repaint and froze the menu (v0.2.0's player). A copy, so the asset stays as made.
            var asset = Resources.Load<PanelSettings>("ZUUI/ZUPanel");
            var ps = asset != null ? Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            Filters.Enabled = asset != null;                        // (no shaders, no filters: plain is better than frozen)
            ps.name = "ZU Panel";
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1;                                           // height: 1080 canvas pixels at any aspect
            ps.sortingOrder = 100;
            ps.clearColor = false;
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ZUUI/ZUTheme");
            // a browser falls back to a system font for the arrows, triangles and symbols Orbitron and Rajdhani lack;
            // here that is a DejaVu subset (ZUSymbols-Bold) every text element can fall back to
            var tsAsset = Resources.Load<PanelTextSettings>("ZUUI/ZUText");
            var ts = tsAsset != null ? Object.Instantiate(tsAsset) : ScriptableObject.CreateInstance<PanelTextSettings>();
            var fallbacks = new List<FontAsset>();
            var sym = Resources.Load<Font>("ZUUI/Fonts/ZUSymbols-Bold");
            if (sym != null) { var fa = FontAsset.CreateFontAsset(sym); if (fa != null) fallbacks.Add(fa); }
            var body = Resources.Load<Font>("ZUUI/Fonts/Rajdhani-600");
            if (body != null) { var fb = FontAsset.CreateFontAsset(body); if (fb != null) { fb.fallbackFontAssetTable = new List<FontAsset>(fallbacks); ts.defaultFontAsset = fb; } }
            ts.fallbackFontAssets = fallbacks;
            ps.textSettings = ts;
            return ps;
        }

        void Build()
        {
            Root = doc.rootVisualElement;
            Root.AddToClassList("zu");
            Root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("ZUUI/zu");
            if (sheet != null) Root.styleSheets.Add(sheet);
            else Debug.LogWarning("[ZU UI] Resources/ZUUI/zu.uss is missing - the front end draws unstyled");
            HudLayer = Layer("hud-layer"); MenuLayer = Layer("menu-layer"); OverlayLayer = Layer("overlay-layer");
            // letter-spacing (the standard text generator) and glows that outreach the font atlas - UiText.cs
            UiText.Install(this);
            // the skins chosen in the Hero Viewer dress the heroes in a match (model skins: Hibiki's Bassline Armor)
            ActorViews.SkinModel = HeroViewerView.SkinModelFor;
        }

        VisualElement Layer(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("layer"); e.AddToClassList(cls);
            Root.Add(e);
            return e;
        }

        void LateUpdate() => Tick?.Invoke();

        /// <summary>a point in the world -> canvas pixels (false behind the camera)</summary>
        public bool WorldToCanvas(Camera cam, Vector3 world, out Vector2 p)
        {
            p = default;
            if (cam == null || Panel == null) return false;
            var s = cam.WorldToScreenPoint(world);
            if (s.z <= 0) return false;
            p = RuntimePanelUtils.ScreenToPanel(Panel, new Vector2(s.x, Screen.height - s.y));
            return true;
        }

        /// <summary>the canvas size in panel pixels (1080 tall)</summary>
        public Vector2 Canvas => Root != null && Root.layout.width > 0 ? new Vector2(Root.layout.width, Root.layout.height) : new Vector2(1080f * Screen.width / Mathf.Max(1, Screen.height), 1080);
    }
}
