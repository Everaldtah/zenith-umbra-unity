// The sound engineer's CLI (`unity command zu_audio_* --project-path <wt>`): record the master output to a WAV, read the
// mix, audition a sound at a distance and bearing, stress the master chain. A batch-mode Editor has no audio device, but
// AudioRenderer captures render offline, so captures and their measurements work there too (tools/audio/README.md).
using Unity.Pipeline.Commands;
using UnityEditor;
using ZU.Game.Audio;

namespace ZU.EditorTools
{
    public static class AudioCommands
    {
        [CliCommand("zu_audio_capture", "Play mode: record the next N seconds of game time of the master output (after the mix + master chain) to a 32-bit WAV; zu_audio_report reads the result")]
        public static string Capture(
            [CliArg("out", "WAV path, relative to the project")] string output = "Captures/audio/capture.wav",
            [CliArg("seconds", "game seconds to record")] float seconds = 20,
            [CliArg("stress", "also fire the stress burst (40 sounds in 0.25 s, 6 times) at the start")] bool stress = false)
        {
            if (!EditorApplication.isPlaying) return "enter play mode first (editor_play)";
            var r = AudioCapture.Begin(output, seconds);
            if (stress) AudioStress.Burst();
            return r;
        }

        [CliCommand("zu_audio_report", "The last audio capture's report (JSON: master peak / clips / clicks / limiter / loudness), or 'running'")]
        public static string Report() => AudioCapture.Running != null ? "running" : (AudioCapture.LastReport == "" ? "no capture yet" : AudioCapture.LastReport);

        [CliCommand("zu_audio_diag", "The live mix: ducks, voices per bus, voice-line filters, reverb, the master chain's meter")]
        public static string Diag() => AudioKit.Diag();

        [CliCommand("zu_audio_play", "Play mode: audition one sound id at a distance (m, 0 = your own) and bearing (deg, 0 = ahead, 90 = right)")]
        public static string Play(
            [CliArg("id", "sound bank id")] string id = "chaingun",
            [CliArg("dist", "metres from the listener; 0 = in your head")] float dist = 10,
            [CliArg("bearing", "degrees clockwise from where the listener faces")] float bearing = 0,
            [CliArg("rel", "who made it: enemy | ally | none")] string rel = "enemy")
        {
            if (!EditorApplication.isPlaying) return "enter play mode first (editor_play)";
            if (!AudioKit.Has(id)) return "unknown sound id " + id;
            AudioStress.Audition(id, dist, bearing, rel == "ally" ? Rel.Ally : rel == "none" ? Rel.None : Rel.Enemy);
            return $"{id} at {dist} m, {bearing} deg";
        }

        [CliCommand("zu_audio_stress", "Play mode: the overload burst (N weapon/impact/ability sounds within 0.25 s around the listener, repeated)")]
        public static string Stress(
            [CliArg("count", "sounds per burst")] int count = 40,
            [CliArg("bursts", "how many bursts, 0.6 s apart")] int bursts = 6)
        {
            if (!EditorApplication.isPlaying) return "enter play mode first (editor_play)";
            AudioStress.Burst(count, 0.25f, bursts);
            return $"stress: {bursts} bursts of {count}";
        }
    }
}
