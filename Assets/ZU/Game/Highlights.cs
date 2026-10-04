// The highlights library: the local player's best play of every match, kept as replay clips (Fx.PlayClip: the frames the
// hero views draw, not video), listed in Career > History, watched in the engine, and saved as a video file on request.
// Overwatch works the same way: its highlights are replay data re-rendered by the game, and "record" renders one to a
// video file at a chosen resolution, whatever the graphics settings (up to 4K at 60 frames a second).
//
// The user's rules (2026-10-04): a highlight stays 48 hours and is then erased by itself; at most 10 are kept (the oldest
// makes room for a new one). A video the player saved is theirs: it goes to Videos\ZENITH UMBRA\Highlights and is never
// erased by the game.
//
// The video: the clip replays with Time.captureFramerate set, so every frame is one sixtieth of a second however long
// it takes to render; each frame the match camera is rendered once more into a texture of the chosen size (1080p, 1440p
// or 4K - not the window's) and the pixels go to the encoder. With ffmpeg on the machine (next to the game or on the
// PATH) that is an H.264 MP4; without it, a Motion-JPEG AVI written by the game itself, which every player opens.
// There is no sound in the file in this version.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using ZU.Game.Fx;

namespace ZU.Game
{
    /// <summary>one highlight as the list shows it (the clip's header; the body is read only to watch or save it)</summary>
    public sealed class HighlightInfo
    {
        public string path, map, mode, heroId, heroName, category, summary;
        public bool potg;
        /// <summary>when it was made (unix ms)</summary>
        public long at;
        public double seconds, score;
    }

    public static class Highlights
    {
        public const int MAX = 10;
        public static readonly TimeSpan LIFE = TimeSpan.FromHours(48);
        const string EXT = ".zuclip";

        /// <summary>where the clips are kept</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, "Highlights");
        /// <summary>where saved videos go</summary>
        public static string VideoFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ZENITH UMBRA", "Highlights");

        public static bool Exporting { get; private set; }
        public static float ExportProgress { get; private set; }
        public static string LastExportPath { get; private set; }
        public static string LastExportError { get; private set; }
        /// <summary>a highlight is on the screen (watched or being saved)</summary>
        public static bool Watching { get; private set; }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ------------------------------------------------------------------------------------------------ the library
        /// <summary>the highlights, newest first. Erases what is older than 48 hours on the way.</summary>
        public static IReadOnlyList<HighlightInfo> List()
        {
            var list = new List<HighlightInfo>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                long oldest = Now - (long)LIFE.TotalMilliseconds;
                foreach (var f in Directory.GetFiles(Folder, "*" + EXT))
                {
                    PlayClip c = null;
                    try { c = PlayClip.LoadHeader(f); } catch (Exception) { }
                    // (unreadable = written by another version: it cannot be watched, so it does not stay)
                    if (c == null || c.at < oldest) { TryDelete(f); continue; }
                    list.Add(new HighlightInfo
                    {
                        path = f, map = c.map, mode = c.mode, heroId = c.heroId, heroName = c.heroName, category = c.category, summary = c.summary,
                        potg = c.potg, at = c.at, seconds = c.Seconds, score = c.score,
                    });
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[ZU] highlights: " + e.Message); }
            return list.OrderByDescending(h => h.at).ToList();
        }

        /// <summary>keep this clip (the end of a match). The oldest makes room when there are ten.</summary>
        public static void Add(PlayClip clip)
        {
            if (clip == null || !clip.HasBody) return;
            try
            {
                Directory.CreateDirectory(Folder);
                if (clip.at <= 0) clip.at = Now;
                string name = $"{DateTimeOffset.FromUnixTimeMilliseconds(clip.at).ToLocalTime():yyyyMMdd_HHmmss}_{Safe(clip.heroId)}{EXT}";
                clip.Save(Path.Combine(Folder, name));
                var all = List();                                              // (also erases the expired ones)
                foreach (var h in all.Skip(MAX)) TryDelete(h.path);
                UnityEngine.Debug.Log($"[ZU] highlight kept: {clip.heroName} - {(clip.potg ? "PLAY OF THE GAME, " : "")}{clip.category} - {clip.summary} ({clip.Seconds:0.0} s, {Math.Min(all.Count, MAX)} of {MAX})");
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[ZU] highlight not kept: " + e.Message); }
        }

        public static void Delete(HighlightInfo h) { if (h != null) TryDelete(h.path); }

        static void TryDelete(string f) { try { if (!string.IsNullOrEmpty(f) && File.Exists(f)) File.Delete(f); } catch (Exception) { } }
        static string Safe(string s) => new string((s ?? "play").Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());

        public static void OpenFolder()
        {
            try { Directory.CreateDirectory(VideoFolder); Process.Start(new ProcessStartInfo { FileName = VideoFolder, UseShellExecute = true }); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[ZU] highlights folder: " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------ watching
        /// <summary>replay this highlight in the engine; `done` when it ends or is left (Esc)</summary>
        public static void Watch(HighlightInfo h, Action done) => Run(h, 0, done);

        /// <summary>save this highlight as a video file of this height (1080, 1440 or 2160); the replay is shown while it is
        /// written and the game returns to where `Watch` would</summary>
        public static void Export(HighlightInfo h, int height) => Run(h, height, null);

        static void Run(HighlightInfo h, int height, Action done)
        {
            if (h == null || Watching) { done?.Invoke(); return; }
            PlayClip clip = null;
            try { clip = PlayClip.Load(h.path); } catch (Exception e) { UnityEngine.Debug.LogWarning("[ZU] highlight: " + e.Message); }
            if (clip == null || !clip.HasBody)
            {
                if (height > 0) LastExportError = "this highlight can no longer be read";
                done?.Invoke();
                return;
            }
            HighlightPlayer.Begin(clip, height, done);
        }

        // ------------------------------------------------------------------------------------------------ the player / recorder
        /// <summary>lives for one watch or one export: starts the replay (KillCam.Watch boots the clip's map), draws the
        /// banner, takes Esc, and when a height is given renders every frame to the encoder</summary>
        sealed class HighlightPlayer : MonoBehaviour
        {
            const int FPS = 60;
            PlayClip clip; int height; Action done;
            bool ended; IEncoder enc; string file; int frames; RenderTexture rt; Texture2D tex; int oldCapture;
            GUIStyle label; Texture2D px;

            public static void Begin(PlayClip clip, int height, Action done)
            {
                var go = new GameObject("ZU Highlight");
                DontDestroyOnLoad(go);
                var p = go.AddComponent<HighlightPlayer>();
                p.clip = clip; p.height = height; p.done = done;
                Watching = true;
                if (height > 0) { Exporting = true; ExportProgress = 0; LastExportError = null; LastExportPath = null; }
                KillCam.Watch(clip, p.OnReplayEnd);
                if (height > 0) p.StartCoroutine(p.Record());
            }

            void OnReplayEnd() { ended = true; }

            void Update()
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (!ended && kb != null && kb.escapeKey.wasPressedThisFrame) { KillCam.Stop(); ended = true; if (Exporting) LastExportError = "cancelled"; }
                if (ended && enc == null) Close();
            }

            IEnumerator Record()
            {
                // wait for the replay to be on the screen (the map loads first)
                float t0 = Time.realtimeSinceStartup;
                while (!ended && !(KillCam.Playing && Camera.main != null))
                {
                    if (Time.realtimeSinceStartup - t0 > 60) { LastExportError = "the replay did not start"; ended = true; }
                    yield return null;
                }
                if (ended) yield break;
                int w = Mathf.RoundToInt(height * 16f / 9f) & ~1, h = height & ~1;
                try
                {
                    Directory.CreateDirectory(VideoFolder);
                    string stem = Path.Combine(VideoFolder, $"{DateTimeOffset.FromUnixTimeMilliseconds(clip.at).ToLocalTime():yyyy-MM-dd_HH-mm}_{Safe(clip.heroName)}_{Safe((clip.potg ? "PlayOfTheGame" : clip.category) ?? "play")}_{h}p");
                    string ff = FfmpegPath();
                    if (ff != null) { file = stem + ".mp4"; enc = new FfmpegEncoder(ff, file, w, h, FPS); }
                    else { file = stem + ".avi"; enc = new AviEncoder(file, w, h, FPS); }
                }
                catch (Exception e) { LastExportError = e.Message; enc = null; KillCam.Stop(); ended = true; yield break; }
                rt = new RenderTexture(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = true });
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                oldCapture = Time.captureFramerate; Time.captureFramerate = FPS;
                var eof = new WaitForEndOfFrame();
                while (!ended)
                {
                    yield return eof;
                    var cam = Camera.main;
                    if (cam == null || !KillCam.Playing) continue;
                    try
                    {
                        var req = new RenderPipeline.StandardRequest { destination = rt };
                        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                        else { var t = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = t; }
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                        RenderTexture.active = prev;
                        enc.Frame(tex);
                        frames++;
                        ExportProgress = clip.Seconds > 0 ? Mathf.Clamp01((float)(KillCam.ClipTime / clip.Seconds)) : 0;
                    }
                    catch (Exception e) { LastExportError = e.Message; KillCam.Stop(); ended = true; }
                }
                Time.captureFramerate = oldCapture;
                try { enc.Close(); } catch (Exception e) { LastExportError ??= e.Message; }
                enc = null;
                if (LastExportError == null && frames > 0 && File.Exists(file))
                {
                    LastExportPath = file; ExportProgress = 1;
                    UnityEngine.Debug.Log($"[ZU] highlight saved: {file} ({frames} frames, {w}x{h}, {new FileInfo(file).Length / 1048576.0:0.0} MB)");
                }
                else
                {
                    LastExportError ??= "no frame was written";
                    try { if (file != null && File.Exists(file)) File.Delete(file); } catch (Exception) { }
                    UnityEngine.Debug.LogWarning("[ZU] highlight not saved: " + LastExportError);
                }
            }

            void Close()
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (tex != null) Destroy(tex);
                Exporting = false; Watching = false;
                // back to where the highlight was opened: the caller's route (Career > History), then the menu scene if the
                // replay's match scene is still up. A saved video has no caller waiting: it returns to Career > History too.
                var d = done ?? UI.Toolkit.MenuView.BackToCareer; done = null;
                Destroy(gameObject);
                d?.Invoke();
                if (FindFirstObjectByType<MatchRunner>() != null) UI.MatchSettings.BackToMenu();
            }

            void OnGUI()
            {
                float W = Screen.width, H = Screen.height, u = H / 1080f;
                if (label == null)
                {
                    px = new Texture2D(1, 1); px.SetPixel(0, 0, Color.white); px.Apply();
                    label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
                    var f = Resources.Load<Font>("ZUUI/Fonts/Rajdhani-700"); if (f != null) label.font = f;
                }
                label.fontSize = Mathf.RoundToInt(24 * u);
                var gold = new Color(1f, 0.84f, 0.42f);
                void Box(Rect rc, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(rc, px); GUI.color = o; }
                Box(new Rect(40 * u, 40 * u, 700 * u, 104 * u), new Color(0.02f, 0.03f, 0.06f, 0.72f));
                Box(new Rect(40 * u, 40 * u, 6 * u, 104 * u), gold);
                label.normal.textColor = gold;
                GUI.Label(new Rect(64 * u, 46 * u, 660 * u, 32 * u), (clip.potg ? "PLAY OF THE GAME" : "HIGHLIGHT") + "  ·  " + (clip.heroName ?? "").ToUpperInvariant(), label);
                label.normal.textColor = new Color(0.9f, 0.92f, 0.97f);
                GUI.Label(new Rect(64 * u, 78 * u, 660 * u, 32 * u), $"{clip.category}  ·  {clip.summary}", label);
                label.normal.textColor = new Color(gold.r, gold.g, gold.b, 0.9f);
                GUI.Label(new Rect(64 * u, 108 * u, 660 * u, 32 * u), Exporting ? $"SAVING VIDEO  {Mathf.RoundToInt(ExportProgress * 100)} %   ·   ESC cancel" : "ESC  back", label);
                float prog = clip.Seconds > 0 ? Mathf.Clamp01((float)(KillCam.ClipTime / clip.Seconds)) : 0;
                Box(new Rect(0, H - 6 * u, W, 6 * u), new Color(0, 0, 0, 0.5f));
                Box(new Rect(0, H - 6 * u, W * prog, 6 * u), gold);
            }
        }

        // ------------------------------------------------------------------------------------------------ encoders
        interface IEncoder { void Frame(Texture2D rgb24); void Close(); }

        /// <summary>ffmpeg next to the game's exe, or on the PATH (null: none)</summary>
        static string FfmpegPath()
        {
            try
            {
                var dirs = new List<string> { Path.Combine(Application.dataPath, ".."), Application.streamingAssetsPath };
                dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
                foreach (var d in dirs)
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    string f = Path.Combine(d.Trim(), "ffmpeg.exe");
                    if (File.Exists(f)) return f;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>raw frames down ffmpeg's standard input, H.264 in an MP4 out</summary>
        sealed class FfmpegEncoder : IEncoder
        {
            readonly Process p; readonly Stream stdin;
            public FfmpegEncoder(string exe, string file, int w, int h, int fps)
            {
                p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    // (a texture's rows run bottom to top: vflip; yuv420p + faststart so every player and phone opens it)
                    Arguments = $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgb24 -s {w}x{h} -r {fps} -i - -vf vflip -c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p -movflags +faststart \"{file}\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                });
                stdin = p.StandardInput.BaseStream;
            }
            public void Frame(Texture2D t) { var d = t.GetRawTextureData(); stdin.Write(d, 0, d.Length); }
            public void Close()
            {
                stdin.Flush(); stdin.Close();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch (Exception) { } throw new IOException("ffmpeg did not finish"); }
                if (p.ExitCode != 0) throw new IOException("ffmpeg failed (exit code " + p.ExitCode + ")");
            }
        }

        /// <summary>a Motion-JPEG AVI written here: one JPEG a frame in a RIFF container with an index. No dependency; large
        /// files (about 1 MB a frame at 1080p), which any player opens.</summary>
        sealed class AviEncoder : IEncoder
        {
            readonly FileStream f; readonly BinaryWriter w; readonly int width, height, fps;
            readonly List<(uint off, uint len)> index = new List<(uint, uint)>();
            long moviStart; uint maxFrame;
            public AviEncoder(string file, int width, int height, int fps)
            {
                this.width = width; this.height = height; this.fps = fps;
                f = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
                w = new BinaryWriter(f);
                Header(0);
            }
            static void FourCC(BinaryWriter w, string s) { w.Write((byte)s[0]); w.Write((byte)s[1]); w.Write((byte)s[2]); w.Write((byte)s[3]); }
            void Header(uint frames)
            {
                f.Position = 0;
                FourCC(w, "RIFF"); w.Write((uint)0); FourCC(w, "AVI ");
                FourCC(w, "LIST"); w.Write((uint)(4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40)); FourCC(w, "hdrl");
                FourCC(w, "avih"); w.Write((uint)56);
                w.Write((uint)(1000000 / fps)); w.Write((uint)(maxFrame * fps)); w.Write((uint)0); w.Write((uint)0x10);      // µs a frame, bytes a second, padding, flags (has index)
                w.Write(frames); w.Write((uint)0); w.Write((uint)1); w.Write(maxFrame);                                    // frames, initial frames, streams, buffer
                w.Write((uint)width); w.Write((uint)height); w.Write((uint)0); w.Write((uint)0); w.Write((uint)0); w.Write((uint)0);
                FourCC(w, "LIST"); w.Write((uint)(4 + 8 + 56 + 8 + 40)); FourCC(w, "strl");
                FourCC(w, "strh"); w.Write((uint)56);
                FourCC(w, "vids"); FourCC(w, "MJPG"); w.Write((uint)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((uint)0);
                w.Write((uint)1); w.Write((uint)fps); w.Write((uint)0); w.Write(frames);                                    // scale, rate, start, length
                w.Write(maxFrame); w.Write(unchecked((uint)-1)); w.Write((uint)0);                                          // buffer, quality, sample size
                w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
                FourCC(w, "strf"); w.Write((uint)40);
                w.Write((uint)40); w.Write(width); w.Write(height); w.Write((ushort)1); w.Write((ushort)24); FourCC(w, "MJPG");
                w.Write((uint)(width * height * 3)); w.Write(0); w.Write(0); w.Write((uint)0); w.Write((uint)0);
                FourCC(w, "LIST"); w.Write((uint)0); FourCC(w, "movi");
                moviStart = f.Position - 4;                                                                                  // (index offsets count from the 'movi' tag)
            }
            public void Frame(Texture2D t)
            {
                var jpg = t.EncodeToJPG(92);
                if (f.Position + jpg.Length + (long)index.Count * 16 + 1024 > 0xF0000000L) throw new IOException("the video is too large for an AVI file: save it at a lower quality");
                index.Add(((uint)(f.Position - moviStart), (uint)jpg.Length));
                FourCC(w, "00dc"); w.Write((uint)jpg.Length); w.Write(jpg);
                if ((jpg.Length & 1) == 1) w.Write((byte)0);
                maxFrame = Math.Max(maxFrame, (uint)jpg.Length);
            }
            public void Close()
            {
                long moviEnd = f.Position;
                FourCC(w, "idx1"); w.Write((uint)(index.Count * 16));
                foreach (var (off, len) in index) { FourCC(w, "00dc"); w.Write((uint)0x10); w.Write(off); w.Write(len); }
                long end = f.Position;
                Header((uint)index.Count);
                f.Position = moviStart - 4; w.Write((uint)(moviEnd - moviStart));
                f.Position = 4; w.Write((uint)(end - 8));
                w.Flush(); f.Close();
            }
        }
    }
}
