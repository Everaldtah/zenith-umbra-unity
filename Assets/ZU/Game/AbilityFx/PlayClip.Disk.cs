// A PlayClip on disk: "ZUPC", a version, the header as JSON (readable alone, fast, for lists), then the body gzipped.
// The body is written from the types themselves: every public number / flag / text / vector field of Actor (and of its
// input, animation cues and barrier), its statuses, its forced move and its shields; every such field of Proj; the effect
// and sound events. References become ids (a hero's def by id, an owner or an event's actor by actor id). The file names
// its columns, so a field added later is simply not in an old file and a field removed is skipped; a file of another
// version, a damaged file or a hero this build doesn't know makes Load return null - never an exception.
// Numbers are stored as 32-bit floats (a replay doesn't need more; frame and event times stay 64-bit).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Game.Fx
{
    public sealed partial class PlayClip
    {
        public const int VERSION = 1;
        static readonly byte[] MAGIC = { (byte)'Z', (byte)'U', (byte)'P', (byte)'C' };

        /// <summary>loaded from disk: the events' actors are ids until a replay maps them onto its heroes (KillCam)</summary>
        [JsonIgnore] public Dictionary<SimEvent, (int actor, int target)> refs;

        sealed class Head
        {
            public string map, mode, heroId, heroName, team, category, summary, playerName; public string[] lines;
            public double score, t0, t1; public int focusId; public bool potg, mine; public long at;
        }

        // ---------------------------------------------------------------------------------------- columns (by reflection)
        enum Kind : byte { Num, Flt, Int, Bool, Str, Vec, VecN, NumN, BoolN, IntN }
        sealed class Col { public string path; public Kind kind; public FieldInfo outer, field; }

        static bool KindOf(Type t, out Kind k)
        {
            k = Kind.Num;
            if (t == typeof(double)) k = Kind.Num; else if (t == typeof(float)) k = Kind.Flt; else if (t == typeof(int)) k = Kind.Int;
            else if (t == typeof(bool)) k = Kind.Bool; else if (t == typeof(string)) k = Kind.Str; else if (t == typeof(V3)) k = Kind.Vec;
            else if (t == typeof(V3?)) k = Kind.VecN; else if (t == typeof(double?)) k = Kind.NumN; else if (t == typeof(bool?)) k = Kind.BoolN;
            else if (t == typeof(int?)) k = Kind.IntN;
            else return false;
            return true;
        }
        static List<Col> ColsOf(Type type, params string[] nested)
        {
            var cols = new List<Col>();
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
            foreach (var f in type.GetFields(F).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (f.Name == "id" && type == typeof(Actor)) continue;                 // (written first, by hand)
                if (KindOf(f.FieldType, out var k)) cols.Add(new Col { path = f.Name, kind = k, field = f });
                else if (Array.IndexOf(nested, f.Name) >= 0)
                    foreach (var g in f.FieldType.GetFields(F).OrderBy(g => g.Name, StringComparer.Ordinal))
                        if (KindOf(g.FieldType, out var k2)) cols.Add(new Col { path = f.Name + "." + g.Name, kind = k2, outer = f, field = g });
            }
            return cols;
        }
        static List<Col> actorCols, projCols;
        static List<Col> ActorCols => actorCols ??= ColsOf(typeof(Actor), "input", "anim", "barrier");
        static List<Col> ProjCols => projCols ??= ColsOf(typeof(Proj));

        static void Put(BinaryWriter w, Kind k, object v)
        {
            switch (k)
            {
                case Kind.Num: w.Write((float)(double)v); break;
                case Kind.Flt: w.Write((float)v); break;
                case Kind.Int: w.Write((int)v); break;
                case Kind.Bool: w.Write((bool)v); break;
                case Kind.Str: w.Write(v != null); if (v != null) w.Write((string)v); break;
                case Kind.Vec: { var p = (V3)v; w.Write((float)p.x); w.Write((float)p.y); w.Write((float)p.z); break; }
                case Kind.VecN: w.Write(v != null); if (v != null) { var p = (V3)v; w.Write((float)p.x); w.Write((float)p.y); w.Write((float)p.z); } break;
                case Kind.NumN: w.Write(v != null); if (v != null) w.Write((float)(double)v); break;
                case Kind.BoolN: w.Write(v != null); if (v != null) w.Write((bool)v); break;
                case Kind.IntN: w.Write(v != null); if (v != null) w.Write((int)v); break;
            }
        }
        static object Get(BinaryReader r, Kind k)
        {
            switch (k)
            {
                case Kind.Num: return (double)r.ReadSingle();
                case Kind.Flt: return r.ReadSingle();
                case Kind.Int: return r.ReadInt32();
                case Kind.Bool: return r.ReadBoolean();
                case Kind.Str: return r.ReadBoolean() ? r.ReadString() : null;
                case Kind.Vec: return new V3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                case Kind.VecN: return r.ReadBoolean() ? (object)(V3?)new V3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) : null;
                case Kind.NumN: return r.ReadBoolean() ? (object)(double?)r.ReadSingle() : null;
                case Kind.BoolN: return r.ReadBoolean() ? (object)(bool?)r.ReadBoolean() : null;
                case Kind.IntN: return r.ReadBoolean() ? (object)(int?)r.ReadInt32() : null;
            }
            return null;
        }
        static void WriteSchema(BinaryWriter w, List<Col> cols) { w.Write(cols.Count); foreach (var c in cols) { w.Write(c.path); w.Write((byte)c.kind); } }
        /// <summary>the file's columns matched to this build's (null: a column the build no longer has - read and dropped)</summary>
        static List<(Kind kind, Col col)> ReadSchema(BinaryReader r, List<Col> now)
        {
            int n = r.ReadInt32(); var list = new List<(Kind, Col)>(n);
            for (int i = 0; i < n; i++)
            {
                string path = r.ReadString(); var k = (Kind)r.ReadByte();
                list.Add((k, now.FirstOrDefault(c => c.path == path && c.kind == k)));
            }
            return list;
        }
        static void WriteCols(BinaryWriter w, List<Col> cols, object o)
        {
            foreach (var c in cols) Put(w, c.kind, c.field.GetValue(c.outer != null ? c.outer.GetValue(o) : o));
        }
        static void ReadCols(BinaryReader r, List<(Kind kind, Col col)> cols, object o)
        {
            foreach (var (k, c) in cols)
            {
                var v = Get(r, k);
                if (c == null || o == null) continue;
                c.field.SetValue(c.outer != null ? c.outer.GetValue(o) : o, v);
            }
        }
        static void WriteDict(BinaryWriter w, Dictionary<string, double> d) { w.Write(d.Count); foreach (var kv in d) { w.Write(kv.Key); w.Write((float)kv.Value); } }
        static Dictionary<string, double> ReadDict(BinaryReader r) { int n = r.ReadInt32(); var d = new Dictionary<string, double>(n); for (int i = 0; i < n; i++) d[r.ReadString()] = r.ReadSingle(); return d; }

        // ---------------------------------------------------------------------------------------- save
        /// <summary>write the clip (header + body) to `path`, replacing what is there; sets `this.path`</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            {
                using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, true))
                {
                    w.Write(MAGIC); w.Write(VERSION);
                    w.Write(JsonConvert.SerializeObject(new Head { map = map, mode = mode, heroId = heroId, heroName = heroName, team = team, category = category, summary = summary,
                        playerName = playerName, lines = lines, score = score, t0 = t0, t1 = t1, focusId = focusId, potg = potg, mine = mine, at = at }));
                }
                using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
                using (var w = new BinaryWriter(gz, System.Text.Encoding.UTF8))
                {
                    WriteSchema(w, ActorCols); WriteSchema(w, ProjCols);
                    w.Write(frames.Count);
                    foreach (var f in frames)
                    {
                        w.Write(f.t); w.Write(f.actors.Count);
                        foreach (var a in f.actors.Values)
                        {
                            w.Write(a.id); w.Write(a.def.id); w.Write(a.baseDef != null ? a.baseDef.id : a.def.id); w.Write(a.team ?? ""); w.Write(a.owner != null ? a.owner.id : 0);
                            w.Write(a.beamTarget != null ? a.beamTarget.id : 0);
                            WriteCols(w, ActorCols, a);
                            WriteDict(w, a.st); WriteDict(w, a.sv);
                            var fo = a.forced; w.Write(fo != null);
                            if (fo != null) { w.Write(fo.kind ?? ""); w.Write((float)fo.until); w.Write((float)fo.vx); w.Write((float)fo.vy); w.Write((float)fo.vz); w.Write(fo.ignoreGravity); w.Write(fo.scaled); }
                            w.Write(a.shields.Count);
                            foreach (var s in a.shields) { w.Write(s.kind ?? ""); w.Write((float)s.amt); w.Write((float)s.until); }
                        }
                        w.Write(f.projs.Count);
                        foreach (var p in f.projs) { w.Write(p.owner != null ? p.owner.id : 0); WriteCols(w, ProjCols, p); }
                    }
                    w.Write(events.Count);
                    foreach (var (t, e) in events)
                    {
                        w.Write(t);
                        int actor = 0, target = 0;
                        if (refs != null && refs.TryGetValue(e, out var ids)) { actor = ids.actor; target = ids.target; }
                        if (e is FxEvent x)
                        {
                            w.Write((byte)0); w.Write(x.kind ?? ""); Put(w, Kind.Vec, x.pos); Put(w, Kind.VecN, x.to); Put(w, Kind.VecN, x.n);
                            Put(w, Kind.NumN, x.r); Put(w, Kind.NumN, x.dur); Put(w, Kind.NumN, x.side); Put(w, Kind.Str, x.color); Put(w, Kind.Str, x.mat);
                            w.Write(x.actor != null ? x.actor.id : actor); w.Write(x.target != null ? x.target.id : target);
                        }
                        else if (e is SfxEvent s)
                        {
                            w.Write((byte)1); w.Write(s.id ?? ""); Put(w, Kind.VecN, s.pos); Put(w, Kind.NumN, s.vol); w.Write(s.actor != null ? s.actor.id : actor);
                        }
                        else w.Write((byte)255);
                    }
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            this.path = path;
        }

        // ---------------------------------------------------------------------------------------- load
        static PlayClip ReadHead(BinaryReader r, string path)
        {
            var m = r.ReadBytes(4);
            if (m.Length != 4 || m[0] != MAGIC[0] || m[1] != MAGIC[1] || m[2] != MAGIC[2] || m[3] != MAGIC[3] || r.ReadInt32() != VERSION) return null;
            var h = JsonConvert.DeserializeObject<Head>(r.ReadString());
            if (h == null) return null;
            return new PlayClip { map = h.map, mode = h.mode, heroId = h.heroId, heroName = h.heroName, team = h.team, category = h.category, summary = h.summary, playerName = h.playerName,
                lines = h.lines, score = h.score, t0 = h.t0, t1 = h.t1, focusId = h.focusId, potg = h.potg, mine = h.mine, at = h.at, path = path };
        }

        /// <summary>the header alone (no frames): for lists. null: not a clip of this version, or unreadable</summary>
        public static PlayClip LoadHeader(string path)
        {
            try { using (var fs = File.OpenRead(path)) using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8)) return ReadHead(r, path); }
            catch (Exception) { return null; }
        }

        /// <summary>the whole clip. null: not a clip of this version, damaged, or it needs a hero this build doesn't have</summary>
        public static PlayClip Load(string path)
        {
            try
            {
                var data = GameData.Current;
                if (data == null) return null;
                using (var fs = File.OpenRead(path))
                {
                    PlayClip c;
                    using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8, true)) c = ReadHead(r, path);
                    if (c == null) return null;
                    c.refs = new Dictionary<SimEvent, (int, int)>();
                    using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                    using (var r = new BinaryReader(gz, System.Text.Encoding.UTF8))
                    {
                        var aCols = ReadSchema(r, ActorCols); var pCols = ReadSchema(r, ProjCols);
                        int nf = r.ReadInt32();
                        for (int i = 0; i < nf; i++)
                        {
                            var f = new Frame { t = r.ReadDouble() };
                            int na = r.ReadInt32(); f.actors = new Dictionary<int, Actor>(na);
                            var owners = new List<(Actor a, int owner, int beam)>();
                            for (int k = 0; k < na; k++)
                            {
                                int id = r.ReadInt32(); string defId = r.ReadString(), baseId = r.ReadString(), team = r.ReadString(); int owner = r.ReadInt32(), beam = r.ReadInt32();
                                var def = data.Def(defId);
                                Actor a = def != null ? new Actor(def, team, id) : null;    // (an unknown hero: its record is read and dropped)
                                ReadCols(r, aCols, a);
                                var st = ReadDict(r); var sv = ReadDict(r);
                                Forced fo = null;
                                if (r.ReadBoolean()) fo = new Forced { kind = r.ReadString(), until = r.ReadSingle(), vx = r.ReadSingle(), vy = r.ReadSingle(), vz = r.ReadSingle(), ignoreGravity = r.ReadBoolean(), scaled = r.ReadBoolean() };
                                int ns = r.ReadInt32(); var sh = new List<Shield>(ns);
                                for (int s = 0; s < ns; s++) sh.Add(new Shield { kind = r.ReadString(), amt = r.ReadSingle(), until = r.ReadSingle() });
                                if (a == null) continue;
                                a.def = def; a.baseDef = data.Def(baseId) ?? def; a.team = team;
                                a.st = st; a.sv = sv; a.forced = fo; a.shields = sh; a.controller = null;
                                f.actors[id] = a;
                                if (owner != 0 || beam != 0) owners.Add((a, owner, beam));
                            }
                            foreach (var (a, owner, beam) in owners)
                            {
                                a.owner = owner != 0 && f.actors.TryGetValue(owner, out var o) ? o : null;
                                a.beamTarget = beam != 0 && f.actors.TryGetValue(beam, out var b) ? b : null;
                            }
                            int np = r.ReadInt32(); f.projs = new List<Proj>(np);
                            for (int k = 0; k < np; k++)
                            {
                                int owner = r.ReadInt32(); var p = new Proj();
                                ReadCols(r, pCols, p);
                                p.owner = f.actors.TryGetValue(owner, out var o) ? o : null;
                                f.projs.Add(p);
                            }
                            c.frames.Add(f);
                        }
                        int ne = r.ReadInt32();
                        for (int i = 0; i < ne; i++)
                        {
                            double t = r.ReadDouble(); byte kind = r.ReadByte();
                            if (kind == 0)
                            {
                                string k = r.ReadString(); var pos = (V3)Get(r, Kind.Vec);
                                var o = new FxOpts { to = (V3?)Get(r, Kind.VecN), n = (V3?)Get(r, Kind.VecN), r = (double?)Get(r, Kind.NumN), dur = (double?)Get(r, Kind.NumN), side = (double?)Get(r, Kind.NumN),
                                    color = (string)Get(r, Kind.Str), mat = (string)Get(r, Kind.Str) };
                                var e = new FxEvent(k, pos, o);
                                c.refs[e] = (r.ReadInt32(), r.ReadInt32());
                                c.events.Add((t, e));
                            }
                            else if (kind == 1)
                            {
                                var e = new SfxEvent { id = r.ReadString(), pos = (V3?)Get(r, Kind.VecN), vol = (double?)Get(r, Kind.NumN) };
                                c.refs[e] = (r.ReadInt32(), 0);
                                c.events.Add((t, e));
                            }
                        }
                    }
                    return c.HasBody ? c : null;
                }
            }
            catch (Exception) { return null; }
        }
    }
}
