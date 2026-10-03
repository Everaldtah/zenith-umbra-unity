// ZU > Sound Lab: listen to the game's sounds the way a player hears them and watch the master meter while you do.
// Enter play mode (the menu or a match), pick a sound, play it in your head or out in the world at a distance and bearing,
// throw the overload burst at the mix, record a capture - with the master chain's meter live: output peak, loudness, how
// hard the limiter is working, clipped samples and clicks (both must stay 0).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZU.Game.Audio;

namespace ZU.EditorTools
{
    public sealed class SoundLabWindow : EditorWindow
    {
        string search = "", cat = "all";
        float dist = 10, bearing = 0;
        Vector2 scroll;
        List<(string id, string cat)> ids;
        static readonly string[] Cats = { "all", "weapon", "impact", "ability", "move", "step", "feedback", "loop", "amb" };
        static readonly float[] Dists = { 0, 5, 20, 50 };

        [MenuItem("ZU/Sound Lab")]
        static void Open() => GetWindow<SoundLabWindow>("ZU Sound Lab");

        void OnInspectorUpdate() { if (EditorApplication.isPlaying) Repaint(); }

        void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode (the menu or a match) to audition sounds through the game's own mix.", MessageType.Info);
                if (GUILayout.Button("Enter Play Mode")) EditorApplication.isPlaying = true;
                return;
            }
            ids ??= AudioStress.Ids();
            Meter();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Overload burst (40 x 6)")) AudioStress.Burst();
                if (GUILayout.Button("Capture 20 s")) Debug.Log(AudioCapture.Begin($"Captures/audio/lab_{System.DateTime.Now:HHmmss}.wav", 20));
                if (GUILayout.Button("Reset meter")) MasterBus.Live?.Chain.ResetMeter();
                if (GUILayout.Button("Stop all")) AudioKit.StopAll();
            }
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                int c = System.Array.IndexOf(Cats, cat); c = EditorGUILayout.Popup(c < 0 ? 0 : c, Cats, GUILayout.Width(90)); cat = Cats[c];
            }
            bearing = EditorGUILayout.Slider("Bearing (deg, 90 = right)", bearing, -180, 180);
            dist = EditorGUILayout.Slider("Custom distance (m)", dist, 0, 100);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var (id, kind) in ids)
            {
                if (cat != "all" && kind != cat) continue;
                if (search.Length > 0 && id.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(id, GUILayout.Width(170));
                    EditorGUILayout.LabelField(kind, EditorStyles.miniLabel, GUILayout.Width(60));
                    foreach (var d in Dists)
                        if (GUILayout.Button(d == 0 ? "own" : d + " m", EditorStyles.miniButton, GUILayout.Width(44))) AudioStress.Audition(id, d, bearing);
                    if (GUILayout.Button(dist.ToString("0") + " m", EditorStyles.miniButton, GUILayout.Width(44))) AudioStress.Audition(id, dist, bearing);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void Meter()
        {
            var b = MasterBus.Live;
            if (b == null) { EditorGUILayout.HelpBox("No master bus yet (no AudioListener found).", MessageType.Warning); return; }
            var m = b.Chain;
            Bar("Out peak", Db(m.Peak), -1);
            Bar("In peak", Db(m.PeakIn), 0);
            Bar("Loudness M", m.MomentaryLufs, -14);
            var r = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(r, $"limiter {m.ReductionDb:0.0} dB (max {m.MaxReductionDb:0.0})   voices {AudioKit.Voices}   max M {m.MaxMomentaryLufs:0.0} LUFS   " +
                                    $"clips in {m.ClipsIn} / OUT {m.ClipsOut}   clicks {m.Clicks}");
        }

        static void Bar(string label, float db, float mark)
        {
            var r = EditorGUILayout.GetControlRect(false, 16);
            float k = Mathf.InverseLerp(-60, 0, db);
            EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
            var fill = new Rect(r.x, r.y, r.width * k, r.height);
            EditorGUI.DrawRect(fill, db > mark ? new Color(0.9f, 0.3f, 0.2f) : db > mark - 6 ? new Color(0.9f, 0.75f, 0.2f) : new Color(0.3f, 0.8f, 0.4f));
            float mx = r.x + r.width * Mathf.InverseLerp(-60, 0, mark);
            EditorGUI.DrawRect(new Rect(mx, r.y, 1, r.height), Color.white);
            EditorGUI.LabelField(r, $" {label}  {db:0.0}", EditorStyles.whiteMiniLabel);
        }

        static float Db(float x) => 20f * Mathf.Log10(Mathf.Max(1e-6f, x));
    }
}
