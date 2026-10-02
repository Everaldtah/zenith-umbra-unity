// A map's look outside its geometry: the surface materials (Resources/ZUEnv, built by `zu_import_env`), the HDRI sky
// lighting the scene (ambient + reflections from the same panorama the player sees), the sun, aerial fog that lets the
// outer world fade into the sky, and the post stack (ACES, bloom, grade, a far-only depth of field that gives the
// skyline depth without ever softening anything you can shoot).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZU.Sim.Data;

namespace ZU.Game.Env
{
    public static class EnvKit
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        /// <summary>the map's surface material for a kind (ground, wall, roof, wood, trim, rock), or null if the env kit has none</summary>
        public static Material Surface(MapDef map, string kind)
        {
            string key = map.id + "_" + kind;
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = Resources.Load<Material>("ZUEnv/" + key);
            // the TS fallbacks: a map without its own set borrows its closest sibling's (Surfaces.ts / MapScene ENV_FALLBACK)
            if (m == null && FALLBACK.TryGetValue(map.id, out var alt)) m = Resources.Load<Material>("ZUEnv/" + alt + "_" + kind);
            // rock without a map set: canyon sandstone in the west, grey cliff everywhere else (floating islands, mountains)
            if (m == null && kind == "rock") m = Resources.Load<Material>(map.id == "mile" || map.id == "gulch" ? "ZUEnv/gulch_rock" : "ZUEnv/common_cliff");
            cache[key] = m;
            return m;
        }
        static readonly Dictionary<string, string> FALLBACK = new Dictionary<string, string> { ["lantern"] = "hanabi", ["starfall"] = "cloudstep", ["mile"] = "foundry", ["gulch"] = "foundry" };

        /// <summary>sky, sun, ambient, reflections, fog, post-processing and camera for `map`; `extent` = the radius the
        /// outer world reaches (fog and far plane follow it)</summary>
        public static void Apply(MapDef map, Transform root, float extent)
        {
            // the painted sky is what you see; the HDRI only lights the scene (TS: env map for PBR + painted panorama)
            var sky = Load("sky_", map.id);
            var hdr = Load("skyhdr_", map.id);
            float mood = Mood(map.id);
            if (hdr != null)
            {
                RenderSettings.skybox = hdr;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                DynamicGI.UpdateEnvironment();
            }
            var sh = RenderSettings.ambientProbe;
            // a normalised HDRI fills less than the old studio light: the TS scales it 1.6 x mood (Game.ts envK); and its
            // hemisphere light from the map's ambient colours tints the fill to the map's palette
            // (an environment map lights diffuse the same in both engines: three's envK 1.6 x mood x environmentIntensity 0.55)
            sh *= hdr != null ? 0.88f * mood : 0f;
            if (map.ambient != null && map.ambient.Length == 3)
            {
                // three's HemisphereLight goes through the same 1/pi Lambert as the sun (see LevelView.Sun)
                float k = System.Convert.ToSingle(map.ambient[2]) / Mathf.PI;
                Color skyC = Conv.Hex(map.ambient[0] as string) * k, groundC = Conv.Hex(map.ambient[1] as string) * k;
                sh.AddAmbientLight((skyC + groundC) * 0.5f);
                sh.AddDirectionalLight(Vector3.up, (skyC - groundC) * 0.5f, 1.2f);
            }
            if (sky != null) RenderSettings.skybox = sky;
            else if (hdr != null) RenderSettings.skybox = hdr;
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.9f;

            // the map's own linear fog (MapScene: THREE.Fog(colour, near, far) - Hanabi 55-180 m, Kagura 90-260 m ...): the
            // haze that gives the PC game its depth; the sky isn't fogged in either engine
            if (map.fog != null && map.fog.Length == 3)
            {
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Conv.Hex(map.fog[0] as string, Color.gray);
                RenderSettings.fogStartDistance = System.Convert.ToSingle(map.fog[1]);
                RenderSettings.fogEndDistance = System.Convert.ToSingle(map.fog[2]);
            }

            // a real-time reflection probe over the play space, rendered once the level stands (the outer world included)
            var probeGo = new GameObject("ZU Reflections");
            probeGo.transform.SetParent(root, false);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 256; probe.hdr = true;
            probe.size = new Vector3((float)map.size[0] * 2 + 40, 80, (float)map.size[1] * 2 + 40);
            probe.transform.localPosition = new Vector3(0, 6, 0);
            probe.farClipPlane = extent * 1.2f;
            probe.importance = 1;
            root.gameObject.AddComponent<ProbeKick>().probe = probe;

            PostStack(map, root, mood);
            CameraSetup(extent);
        }

        static Material Load(string prefix, string id)
        {
            var m = Resources.Load<Material>("ZUEnv/" + prefix + id);
            if (m == null && FALLBACK.TryGetValue(id, out var alt)) m = Resources.Load<Material>("ZUEnv/" + prefix + alt);
            return m;
        }

        static float Mood(string id)
        {
            var json = Resources.Load<TextAsset>("ZUData/env");
            if (json == null) return 1;
            var j = Newtonsoft.Json.Linq.JObject.Parse(json.text);
            var m = j["hdri"]?[id]?["mood"];
            return m != null ? (float)m : 1f;
        }

        static void PostStack(MapDef map, Transform root, float mood)
        {
            var go = new GameObject("ZU PostFX");
            go.transform.SetParent(root, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = p;
            var tint = Conv.Hex(map.tint, Color.white);

            // the TS renders with Neutral tone mapping at exposure 0.95 (Game.ts)
            var tm = p.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.Neutral;
            // bloom (Game.ts): strength .55 over .82; bright daylight maps (sun >= 2.1) only bloom real highlights (.38 over
            // .97); a map's own [threshold, strength] wins
            bool day = map.sun != null && map.sun.intensity >= 2.1;
            float threshold = day ? 0.97f : 0.82f, strength = day ? 0.38f : 0.55f;
            if (map.bloom != null && map.bloom.Length == 2) { threshold = (float)map.bloom[0]; strength = (float)map.bloom[1]; }
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.value = threshold; bloom.intensity.value = strength; bloom.scatter.value = 0.55f;
            bloom.highQualityFiltering.value = true;
            // the map's colour grade (PostFx.ts GRADES): contrast as the TS soft S-curve's strength, vibrance as a gentle
            // saturation lift, the split tone into shadows / highlights, the vignette
            var g = Grade(map.id);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.value = Mathf.Log(0.95f, 2);
            ca.contrast.value = (g.contrast - 1) * 125f;
            ca.saturation.value = g.vibrance * 40f;
            var st = p.Add<SplitToning>(true);
            st.shadows.value = new Color(0.5f + g.shadows.x * 4, 0.5f + g.shadows.y * 4, 0.5f + g.shadows.z * 4);
            st.highlights.value = new Color(0.5f + g.highlights.x * 4, 0.5f + g.highlights.y * 4, 0.5f + g.highlights.z * 4);
            var vig = p.Add<Vignette>(true); vig.intensity.value = g.vignette; vig.smoothness.value = 0.45f;
            // far-only depth of field (a Unity extra, Options > Video): everything inside the play space stays sharp, the
            // skyline and mountains soften
            if (!UI.Toolkit.ZuSettings.Current.video.farDof) return;
            var dof = p.Add<DepthOfField>(true);
            dof.mode.value = DepthOfFieldMode.Gaussian;
            float reach = Mathf.Max((float)map.size[0], (float)map.size[1]) * 2.2f;
            dof.gaussianStart.value = reach * 1.6f; dof.gaussianEnd.value = reach * 6f; dof.gaussianMaxRadius.value = 0.6f;
            dof.highQualitySampling.value = true;
        }

        /// <summary>PostFx.ts GRADES: contrast, vibrance, the split tone's shadow / highlight offsets (sRGB), vignette</summary>
        public struct GradeDef { public float contrast, vibrance, vignette; public Vector3 shadows, highlights; }
        static GradeDef G(float c, float v, Vector3 s, Vector3 h, float vig) => new GradeDef { contrast = c, vibrance = v, shadows = s, highlights = h, vignette = vig };
        static readonly GradeDef NEUTRAL = G(1.04f, 0.12f, new Vector3(0, 0.004f, 0.02f), new Vector3(0.02f, 0.01f, -0.01f), 0.16f);
        static readonly Dictionary<string, GradeDef> GRADES = new Dictionary<string, GradeDef>
        {
            ["hanabi"] = G(1.07f, 0.18f, new Vector3(0.01f, -0.004f, 0.026f), new Vector3(0.045f, 0.02f, -0.025f), 0.2f),
            ["cloudstep"] = G(1.05f, 0.16f, new Vector3(-0.01f, 0.01f, 0.035f), new Vector3(0.025f, 0.018f, -0.008f), 0.14f),
            ["kagura"] = G(1.06f, 0.16f, new Vector3(-0.005f, 0.005f, 0.03f), new Vector3(0.03f, 0.015f, -0.01f), 0.15f),
            ["lantern"] = G(1.08f, 0.2f, new Vector3(0.006f, -0.003f, 0.035f), new Vector3(0.035f, 0.015f, -0.02f), 0.22f),
            ["starfall"] = G(1.06f, 0.18f, new Vector3(-0.015f, 0.012f, 0.04f), new Vector3(0.04f, 0.022f, -0.012f), 0.18f),
            ["foundry"] = G(1.1f, 0.14f, new Vector3(-0.02f, 0.012f, 0.03f), new Vector3(0.05f, 0.018f, -0.03f), 0.22f),
            ["mile"] = G(1.07f, 0.18f, new Vector3(-0.012f, 0.004f, 0.032f), new Vector3(0.04f, 0.018f, -0.02f), 0.17f),
            ["gulch"] = G(1.08f, 0.18f, new Vector3(-0.008f, 0f, 0.03f), new Vector3(0.05f, 0.022f, -0.024f), 0.2f),
        };
        public static GradeDef Grade(string mapId) => mapId != null && GRADES.TryGetValue(mapId, out var g) ? g : NEUTRAL;

        static void CameraSetup(float extent)
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, extent * 1.3f);
            cam.nearClipPlane = 0.05f;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.requiresDepthTexture = true; data.requiresColorTexture = true;
        }

        /// <summary>renders the reflection probe a couple of frames after the level is built (so the outer world, the
        /// props and the sky are all in it), then once more after the lighting settles</summary>
        sealed class ProbeKick : MonoBehaviour
        {
            public ReflectionProbe probe;
            int frames;
            void Update()
            {
                frames++;
                if (probe != null && (frames == 3 || frames == 90)) probe.RenderProbe();
                if (frames > 90) Destroy(this);
            }
        }
    }
}
