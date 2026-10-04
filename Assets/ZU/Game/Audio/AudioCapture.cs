// Record what the player hears - the master output after the whole mix and the master chain - into a WAV, for listening
// offline and for the audio QA (tools/audio/qa_capture.py measures the file: true peak, clipping, clicks, loudness).
// Uses Unity's AudioRenderer: game time steps a fixed 1/60 s per frame (Time.captureFramerate) and the audio renders exactly
// that much per frame, so a capture works the same in a batch-mode Editor (no audio device there) as in the player, at
// whatever speed the machine runs, and the same seed renders the same file.
using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace ZU.Game.Audio
{
    public sealed class AudioCapture : MonoBehaviour
    {
        /// <summary>the capture running now (null when idle)</summary>
        public static AudioCapture Running { get; private set; }
        /// <summary>the last finished capture's report (JSON), "" until one finishes</summary>
        public static string LastReport = "";

        string path; float seconds; int rate, ch, frames, maxVoices, prevCapture, emptyFrames;
        FileStream fs; BinaryWriter bw; long dataBytes;
        NativeArray<float> buf;

        /// <summary>start recording the next `secs` seconds of game time into a 32-bit float WAV at `wavPath`</summary>
        public static string Begin(string wavPath, float secs)
        {
            if (Running != null) return "a capture is already running -> " + Running.path;
            var go = new GameObject("ZU audio capture") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            var c = go.AddComponent<AudioCapture>();
            c.path = Path.GetFullPath(wavPath); c.seconds = Mathf.Max(0.5f, secs);
            c.rate = AudioSettings.outputSampleRate;
            c.ch = Channels(AudioSettings.speakerMode);
            Directory.CreateDirectory(Path.GetDirectoryName(c.path));
            c.fs = File.Create(c.path); c.bw = new BinaryWriter(c.fs);
            c.WriteHeader(0);
            c.prevCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            MasterBus.Live?.Chain.ResetMeter();
            if (!AudioRenderer.Start()) { c.Finish("AudioRenderer.Start failed"); return LastReport; }
            Running = c;
            return $"capturing {c.seconds:0.#} s at {c.rate} Hz x {c.ch} ch -> {c.path}";
        }

        static int Channels(AudioSpeakerMode m) => m switch
        {
            AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4, AudioSpeakerMode.Surround => 5,
            AudioSpeakerMode.Mode5point1 => 6, AudioSpeakerMode.Mode7point1 => 8, AudioSpeakerMode.Prologic => 2, _ => 2,
        };

        void LateUpdate()
        {
            if (Running != this) return;
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if (n > 0)
            {
                if (!buf.IsCreated || buf.Length < n * ch) { if (buf.IsCreated) buf.Dispose(); buf = new NativeArray<float>(n * ch * 2, Allocator.Persistent); }
                var view = buf.GetSubArray(0, n * ch);
                if (AudioRenderer.Render(view))
                {
                    for (int i = 0; i < view.Length; i++) bw.Write(view[i]);
                    dataBytes += view.Length * 4L;
                    frames += n;
                }
            }
            maxVoices = Math.Max(maxVoices, AudioKit.Voices);
            // a batch-mode Editor (no audio device) hands out no samples at all: give up after 2 s of game time, don't hang
            if (frames == 0 && n <= 0 && ++emptyFrames > 120) { Finish("AudioRenderer produced no samples (no audio device: a batch-mode Editor?)"); return; }
            if (frames >= seconds * rate) Finish(null);
        }

        void OnDestroy() { if (Running == this) Finish("capture object destroyed early"); }

        void Finish(string error)
        {
            if (Running == this) { AudioRenderer.Stop(); Running = null; }
            Time.captureFramerate = prevCapture;
            if (bw != null) { WriteHeader(dataBytes); bw.Flush(); bw.Dispose(); bw = null; fs = null; }
            if (buf.IsCreated) buf.Dispose();
            var m = MasterBus.Live?.Chain;
            LastReport = "{" +
                $"\"wav\":\"{path.Replace("\\", "/")}\",\"error\":{(error == null ? "null" : "\"" + error + "\"")},\"rate\":{rate},\"channels\":{ch}," +
                $"\"seconds\":{(rate > 0 ? frames / (double)rate : 0):0.###},\"maxVoices\":{maxVoices}," +
                (m == null ? "\"master\":null" :
                    $"\"master\":{{\"peakDb\":{Db(m.Peak):0.00},\"peakInDb\":{Db(m.PeakIn):0.00},\"maxMomentaryLufs\":{m.MaxMomentaryLufs:0.0},\"maxReductionDb\":{m.MaxReductionDb:0.00}," +
                    $"\"clipsIn\":{m.ClipsIn},\"clipsOut\":{m.ClipsOut},\"clicks\":{m.Clicks},\"framesOver\":{m.OverCeiling}}}") + "}";
            Debug.Log("[ZU] audio capture " + LastReport);
            if (this != null) Destroy(gameObject);
        }

        static float Db(float x) => 20f * Mathf.Log10(Mathf.Max(1e-6f, x));

        void WriteHeader(long data)
        {
            long at = fs.Position;
            fs.Position = 0;
            bw.Write("RIFF".ToCharArray()); bw.Write((int)(36 + data)); bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray()); bw.Write(16); bw.Write((short)3); bw.Write((short)ch); bw.Write(rate);
            bw.Write(rate * ch * 4); bw.Write((short)(ch * 4)); bw.Write((short)32);
            bw.Write("data".ToCharArray()); bw.Write((int)data);
            if (at > fs.Position) fs.Position = at;
        }
    }
}
