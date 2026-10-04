// Round trip of a PlayClip through its disk format, headless: record a stretch of a bot match the way KillCam does, Save,
// Load, compare everything a view can read; then the failure cases (another version, a cut file, rubbish, no file).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using ZU.Game.Fx;
using ZU.Sim;
using ZU.Sim.Data;

static class P
{
    static int bad;
    static void Fail(string s) { if (++bad <= 40) Console.WriteLine("  FAIL " + s); }
    static void Check(bool ok, string s) { if (!ok) Fail(s); }

    static bool Same(object a, object b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a is double x) return (float)x == (float)(double)b || (double.IsNaN(x) && double.IsNaN((double)b));
        if (a is V3 v) { var u = (V3)b; return (float)v.x == (float)u.x && (float)v.y == (float)u.y && (float)v.z == (float)u.z; }
        return a.Equals(b);
    }
    static readonly HashSet<Type> PRIM = new HashSet<Type> { typeof(double), typeof(float), typeof(int), typeof(bool), typeof(string), typeof(V3), typeof(V3?), typeof(double?), typeof(bool?), typeof(int?) };
    static readonly HashSet<string> skipped = new HashSet<string>();
    static void Fields(object a, object b, string where, params string[] nested)
    {
        foreach (var f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (PRIM.Contains(f.FieldType)) { if (!Same(f.GetValue(a), f.GetValue(b))) Fail($"{where}.{f.Name}: {f.GetValue(a)} -> {f.GetValue(b)}"); }
            else if (Array.IndexOf(nested, f.Name) >= 0) Fields(f.GetValue(a), f.GetValue(b), where + "." + f.Name);
            else skipped.Add(a.GetType().Name + "." + f.Name + " (" + f.FieldType.Name + ")");
        }
    }
    static void Dict(Dictionary<string, double> a, Dictionary<string, double> b, string where)
    {
        Check(a.Count == b.Count, where + " count");
        foreach (var kv in a) Check(b.TryGetValue(kv.Key, out var v) && Same(kv.Value, v), where + "[" + kv.Key + "]");
    }

    static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "zu-cliptest");
        Directory.CreateDirectory(outDir);
        GameData.Load(Path.Combine(root, "Assets/ZU/Resources/ZUData"));
        Rng.Seed(12345);
        long total = 0; int clips = 0;
        foreach (var (map, mode, warm, secs) in new[] { ("hanabi", "aitest", 35.0, 12.0), ("mile", "aitest", 60.0, 12.0), ("gulch", "stadium", 50.0, 20.0) })
        {
            var match = Setup.CreateMatch(map, mode, null, 0.8);
            var w = match.world;
            const double DT = 1.0 / 120;
            while (w.time < warm) { w.Step(DT); w.events.Clear(); }
            var c = new PlayClip { map = map, mode = mode, heroId = w.actors[0].def.id, heroName = w.actors[0].def.name, team = w.actors[0].team, category = "TEST", summary = "a round trip \"quoted\" \u00e9",
                playerName = "tester", lines = new[] { "one", "two" }, score = 123.5, t0 = w.time, focusId = w.actors[0].id, potg = true, mine = true, at = 1791000000000 };
            double last = -1;
            while (w.time < warm + secs)
            {
                w.Step(DT);
                foreach (var e in w.events) if (e is FxEvent || e is SfxEvent) c.events.Add((w.time, e));
                w.events.Clear();
                if (w.time - last >= 1.0 / 30)
                {
                    last = w.time;
                    var f = new PlayClip.Frame { t = w.time, actors = new Dictionary<int, Actor>(), projs = new List<Proj>() };
                    foreach (var a in w.actors) { if (a.IsSummon && string.IsNullOrEmpty(a.def.model)) continue; f.actors[a.id] = a.Snapshot(); }
                    foreach (var p in w.projs) f.projs.Add(p.Clone());
                    c.frames.Add(f);
                }
            }
            c.t1 = w.time;
            string path = Path.Combine(outDir, map + ".zuclip");
            var sw = Stopwatch.StartNew(); c.Save(path); double saveMs = sw.Elapsed.TotalMilliseconds;
            long size = new FileInfo(path).Length; total += size; clips++;
            sw.Restart(); var h = PlayClip.LoadHeader(path); double headMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); var d = PlayClip.Load(path); double loadMs = sw.Elapsed.TotalMilliseconds;
            int before = bad;
            Console.WriteLine($"{map} {mode}: {c.Seconds:0.0} s, {c.frames.Count} frames, {c.frames.Average(f => f.actors.Count):0.0} heroes, {c.frames.Average(f => f.projs.Count):0.0} projectiles a frame, {c.events.Count} events" +
                              $" -> {size / 1024.0:0} KB ({size / 1024.0 / c.Seconds:0} KB/s); save {saveMs:0} ms, header {headMs:0.0} ms, load {loadMs:0} ms");
            if (h == null) { Fail("LoadHeader null"); continue; }
            if (d == null) { Fail("Load null"); continue; }
            foreach (var x in new[] { h, d })
            {
                Check(x.map == c.map && x.mode == c.mode && x.heroId == c.heroId && x.heroName == c.heroName && x.team == c.team && x.category == c.category && x.summary == c.summary && x.playerName == c.playerName, "header text");
                Check(x.lines != null && x.lines.SequenceEqual(c.lines), "header lines");
                Check(x.score == c.score && x.t0 == c.t0 && x.t1 == c.t1 && x.focusId == c.focusId && x.potg == c.potg && x.mine == c.mine && x.at == c.at && x.path == path, "header numbers");
            }
            Check(!h.HasBody, "header has a body");
            Check(d.frames.Count == c.frames.Count, "frame count");
            for (int i = 0; i < Math.Min(d.frames.Count, c.frames.Count); i++)
            {
                var f = c.frames[i]; var g = d.frames[i];
                Check(f.t == g.t, $"frame {i} t");
                Check(f.actors.Count == g.actors.Count, $"frame {i} actors {f.actors.Count} -> {g.actors.Count}");
                foreach (var kv in f.actors)
                {
                    if (!g.actors.TryGetValue(kv.Key, out var b)) { Fail($"frame {i} actor {kv.Key} ({kv.Value.def.id}) missing"); continue; }
                    var a = kv.Value; string where = $"f{i} {a.def.id}";
                    Check(b.id == a.id && b.def == a.def && b.baseDef == a.baseDef && b.team == a.team, where + " identity");
                    Check((a.owner?.id ?? 0) == (b.owner?.id ?? 0), where + " owner");
                    int beam = a.beamTarget != null && f.actors.ContainsKey(a.beamTarget.id) ? a.beamTarget.id : 0;
                    Check(beam == (b.beamTarget?.id ?? 0), where + " beamTarget");
                    Fields(a, b, where, "input", "anim", "barrier");
                    Dict(a.st, b.st, where + " st"); Dict(a.sv, b.sv, where + " sv");
                    Check((a.forced == null) == (b.forced == null), where + " forced");
                    if (a.forced != null && b.forced != null) Fields(a.forced, b.forced, where + " forced");
                    Check(a.shields.Count == b.shields.Count, where + " shields");
                    for (int s = 0; s < Math.Min(a.shields.Count, b.shields.Count); s++) Fields(a.shields[s], b.shields[s], where + " shield");
                }
                Check(f.projs.Count == g.projs.Count, $"frame {i} projs");
                for (int k = 0; k < Math.Min(f.projs.Count, g.projs.Count); k++)
                {
                    Fields(f.projs[k], g.projs[k], $"f{i} proj{k}");
                    int own = f.projs[k].owner != null && f.actors.ContainsKey(f.projs[k].owner.id) ? f.projs[k].owner.id : 0;
                    Check(own == (g.projs[k].owner?.id ?? 0), $"f{i} proj{k} owner");
                }
            }
            Check(d.events.Count == c.events.Count, "event count");
            for (int i = 0; i < Math.Min(d.events.Count, c.events.Count); i++)
            {
                var (t, e) = c.events[i]; var (u, g) = d.events[i];
                Check(t == u && e.GetType() == g.GetType(), $"event {i}");
                Fields(e, g, $"event {i}");
                var ids = d.refs[g];
                if (e is FxEvent x) Check(ids.actor == (x.actor?.id ?? 0) && ids.target == (x.target?.id ?? 0), $"event {i} actors");
                if (e is SfxEvent s) Check(ids.actor == (s.actor?.id ?? 0), $"event {i} actor");
            }
            // a loaded clip saved again is the same file
            string again = Path.Combine(outDir, map + ".again.zuclip");
            d.Save(again);
            Check(File.ReadAllBytes(again).SequenceEqual(File.ReadAllBytes(path)), "saved again: the file differs");
            Console.WriteLine(bad == before ? "  round trip ok" : $"  {bad - before} differences");

            // ---- what must come back null, never throw
            var bytes = File.ReadAllBytes(path);
            string t1 = Path.Combine(outDir, "bad.zuclip");
            var v2 = (byte[])bytes.Clone(); v2[4] = 99; File.WriteAllBytes(t1, v2);
            Check(PlayClip.Load(t1) == null && PlayClip.LoadHeader(t1) == null, "another version: not null");
            File.WriteAllBytes(t1, bytes.Take(bytes.Length / 2).ToArray());
            Check(PlayClip.Load(t1) == null, "cut in half: not null"); Check(PlayClip.LoadHeader(t1) != null, "cut in half: the header should still read");
            File.WriteAllBytes(t1, bytes.Take(9).ToArray());
            Check(PlayClip.Load(t1) == null && PlayClip.LoadHeader(t1) == null, "cut in the header: not null");
            var junk = (byte[])bytes.Clone(); var rnd = new Random(7); for (int i = bytes.Length / 3; i < bytes.Length; i += 97) junk[i] = (byte)rnd.Next(256);
            File.WriteAllBytes(t1, junk);
            PlayClip jl = null; try { jl = PlayClip.Load(t1); } catch (Exception ex) { Fail("damaged body threw " + ex.GetType().Name); }
            Console.WriteLine("  damaged body -> " + (jl == null ? "null" : "a clip (gzip let it through)"));
            File.WriteAllBytes(t1, new byte[] { 1, 2, 3 });
            Check(PlayClip.Load(t1) == null && PlayClip.LoadHeader(t1) == null, "rubbish: not null");
            File.WriteAllBytes(t1, new byte[0]);
            Check(PlayClip.Load(t1) == null && PlayClip.LoadHeader(t1) == null, "empty: not null");
            Check(PlayClip.Load(Path.Combine(outDir, "none.zuclip")) == null && PlayClip.LoadHeader(Path.Combine(outDir, "none.zuclip")) == null, "no file: not null");
        }
        Console.WriteLine("not stored (by design or to review): " + string.Join(", ", skipped.OrderBy(s => s)));
        Console.WriteLine(bad == 0 ? $"ALL OK - {clips} clips, {total / 1024.0 / clips:0} KB on average" : $"{bad} FAILURES");
        return bad == 0 ? 0 : 1;
    }
}
