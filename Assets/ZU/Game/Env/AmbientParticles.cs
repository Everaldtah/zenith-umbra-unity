// A map's ambient particles (MapScene.ts makeParticles): petals drifting down over Kagura and Cloudstep, embers rising
// over Hanabi and Lantern, the foundry's sparks, Starfall's motes, the desert's dust (rain where a map asks for it) -
// 1400 of them (rain 5000) times Options > Effects Detail, in a box over the arena 30 m tall, swaying as they fall or
// rise. A ParticleSystem on the shipped ZU/Fx shaders: petals and dust blend normally, the glowing kinds add.
using UnityEngine;
using ZU.Sim.Data;

namespace ZU.Game.Env
{
    public static class AmbientParticles
    {
        // MapScene cfg: colour, size (point size at 600 px / depth ~ 0.74 m at 1080p), fall (m/s, negative rises), sway (m)
        static (string col, float size, float fall, float sway)? Cfg(string kind) => kind switch
        {
            "petals" => ("#ffb7d5", 0.22f, 1.2f, 1.5f),
            "rain" => ("#9fb6ff", 0.06f, 22f, 0.1f),
            "sparks" => ("#ffb040", 0.08f, -1.5f, 0.6f),
            "embers" => ("#ff4d2a", 0.1f, -1.2f, 0.8f),
            "motes" => ("#c9a2ff", 0.12f, -0.4f, 1.2f),
            "dust" => ("#e8c9a0", 0.1f, -0.15f, 2.2f),
            _ => null,
        };

        static Texture2D dot;
        /// <summary>the TS point sprite: a soft disc (1 - smoothstep(0.2, 0.5, r))</summary>
        static Texture2D Dot()
        {
            if (dot != null) return dot;
            const int N = 32;
            dot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float r = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude;
                    float a = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.2f, 0.5f, r));
                    px[y * N + x] = new Color(1, 1, 1, a);
                }
            dot.SetPixels(px); dot.Apply();
            return dot;
        }

        public static void Build(MapDef map, Transform parent)
        {
            var cfg = Cfg(map.particles ?? "none");
            if (cfg == null) return;
            var (col, size, fall, sway) = cfg.Value;
            string fx = UI.Toolkit.ZuSettings.Current.video.effects;
            float q = fx == "low" ? 0.35f : fx == "medium" ? 0.65f : fx == "ultra" ? 1.3f : 1f;
            int n = Mathf.RoundToInt((map.particles == "rain" ? 5000 : 1400) * q);
            if (n <= 0) return;
            float X = (float)map.size[0] + 10, Z = (float)map.size[1] + 10;

            var go = new GameObject("Ambient Particles"); go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 13, 0);              // the TS volume: y -2 .. 28
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            // a particle crosses the 30 m box at fall x (0.6 .. 1.4); the slow drifts live long and sway
            float speed = Mathf.Abs(fall);
            float life = Mathf.Clamp(30f / Mathf.Max(0.1f, speed), 6f, 40f);
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.duration = life;
            main.startLifetime = life;
            main.startSpeed = 0;
            main.startSize = size * 0.74f * (map.particles == "rain" ? 1 : 1.4f);
            var c = Conv.Hex(col);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(c.r, c.g, c.b, 0.5f), new Color(c.r, c.g, c.b, 1f));
            main.maxParticles = n;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission; em.rateOverTime = n / life;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(X * 2, 30, Z * 2);
            // fall (or rise) at 0.6 .. 1.4 of the speed
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0, 0); vel.z = new ParticleSystem.MinMaxCurve(0, 0);
            vel.y = new ParticleSystem.MinMaxCurve(-fall * 0.6f, -fall * 1.4f);
            // the sway: sin(t 0.7 + seed) x sway - a slow noise of that amplitude
            var noise = ps.noise; noise.enabled = sway > 0.15f;
            noise.strength = sway * 0.6f; noise.frequency = 0.12f; noise.scrollSpeed = 0.1f; noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Low;
            // fade in and out at the ends of a life (the TS wraps them round the box instead)
            var col2 = ps.colorOverLifetime; col2.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(1, 0.9f), new GradientAlphaKey(0, 1) });
            col2.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            bool normal = map.particles == "petals" || map.particles == "dust";
            var shader = Shader.Find(normal ? "ZU/FxAlpha" : "ZU/FxAdditive");
            var m = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", Dot()); m.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (map.particles == "rain") { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
        }
    }
}
