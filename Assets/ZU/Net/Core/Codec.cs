// Fast Link wire format: compact binary packets for the per-tick traffic (host snapshots, client inputs, pings). Port of
// zenith-umbra src/net/codec.ts - byte for byte (little-endian, JS rounding and clamping), so a packet from either
// build reads the same in the other. Positions stay float32 (cm-exact on these maps); velocities, angles and vitals are
// quantised; everything that changes rarely (hero, statuses, animation cues, scores, zones, the objective) travels as
// JSON "cold state" inside the snapshot, and only until the client has acknowledged it (see FastSync).
using System;
using System.Collections.Generic;
using System.Text;

namespace ZU.Net
{
    public static class K
    {
        public const byte PK_SNAP = 1, PK_INPUT = 2, PK_PING = 3, PK_PONG = 4;

        /// <summary>JS Math.round (half up, toward +infinity); NaN -> 0 as a DataView store would make it</summary>
        public static double Round(double v) => double.IsNaN(v) ? 0 : Math.Floor(v + 0.5);
        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        // ---------------------------------------------------------------- quantisation
        const double TAU = Math.PI * 2;
        public static int QYaw(double a) => (int)Round(((a % TAU + TAU) % TAU) / TAU * 65535);
        public static double DqYaw(int q) { double a = q / 65535.0 * TAU; return a > Math.PI ? a - TAU : a; }
        /// <summary>shortest-way angle lerp</summary>
        public static double LerpAngle(double a, double b, double k)
        {
            double d = b - a; while (d > Math.PI) d -= TAU; while (d < -Math.PI) d += TAU;
            return a + d * k;
        }
        /// <summary>sequence numbers wrap at 16 bits: is a newer than b?</summary>
        public static bool SeqNewer(int a, int b) => ((a - b) & 0xffff) != 0 && ((a - b) & 0xffff) < 0x8000;

        // ---------------------------------------------------------------- the actor's hot row (every snapshot)
        public const int AF_ALIVE = 1, AF_GROUNDED = 2, AF_FLYING = 4, AF_BARRIER = 8, AF_BEAM = 16, AF_FLAME = 32, AF_CHARGING = 64, AF_BOSS = 128;
        public const int ACTOR_BYTES = 36;
        public static void WriteActor(Writer w, HotActor a) =>
            w.U16(a.id).F32(a.x).F32(a.y).F32(a.z).I16(a.vx * 100).I16(a.vy * 100).I16(a.vz * 100)
             .U16(QYaw(a.yaw)).I16(a.pitch * 10000).U16(a.hp).U16(a.armor).U16(a.shield).U16(a.bhp).U8(a.flags).U8(a.scale * 50).U16(a.beam);
        public static HotActor ReadActor(Reader r) => new HotActor
        {
            id = r.U16(), x = r.F32(), y = r.F32(), z = r.F32(), vx = r.I16() / 100.0, vy = r.I16() / 100.0, vz = r.I16() / 100.0,
            yaw = DqYaw(r.U16()), pitch = r.I16() / 10000.0, hp = r.U16(), armor = r.U16(), shield = r.U16(), bhp = r.U16(), flags = r.U8(), scale = r.U8() / 50.0, beam = r.U16(),
        };

        public static void WriteProj(Writer w, HotProj p) => w.U32(p.id).F32(p.x).F32(p.y).F32(p.z).I16(p.vx * 50).I16(p.vy * 50).I16(p.vz * 50);
        public static HotProj ReadProj(Reader r) => new HotProj { id = r.U32(), x = r.F32(), y = r.F32(), z = r.F32(), vx = r.I16() / 50.0, vy = r.I16() / 50.0, vz = r.I16() / 50.0 };

        // ---------------------------------------------------------------- packets
        /// <summary>SNAP: u8 type, u16 seq, f32 time (s), u16 the last input seq the host applied, u8 tier, u16 host ms (for the
        /// client's clock filter), then the shared hot block, then the peer's cold JSON ("" = nothing new)</summary>
        public static void WriteSnapHeader(Writer w, SnapHeader h) => w.U8(PK_SNAP).U16(h.seq).F32(h.time).U16(h.ackInput).U8(h.tier).U16((long)h.hostMs & 0xffff);
        public static SnapHeader ReadSnapHeader(Reader r) { r.U8(); return new SnapHeader { seq = r.U16(), time = r.F32(), ackInput = r.U16(), tier = r.U8(), hostMs = r.U16() }; }

        /// <summary>the part of a snapshot every peer gets: actors and projectiles</summary>
        public static void WriteHot(Writer w, IList<HotActor> actors, IList<HotProj> projs)
        {
            int na = Math.Min(255, actors.Count); w.U8(na); for (int i = 0; i < na; i++) WriteActor(w, actors[i]);
            int np = Math.Min(1024, projs.Count); w.U16(np); for (int i = 0; i < np; i++) WriteProj(w, projs[i]);
        }
        public static (List<HotActor> actors, List<HotProj> projs) ReadHot(Reader r)
        {
            var actors = new List<HotActor>(); for (int i = r.U8(); i > 0; i--) actors.Add(ReadActor(r));
            var projs = new List<HotProj>(); for (int i = r.U16(); i > 0; i--) projs.Add(ReadProj(r));
            return (actors, projs);
        }

        /// <summary>INPUT (client -> host, ~30-60 Hz, unreliable)</summary>
        public static readonly string[] IN_BITS = { "jump", "jumpHeld", "descend", "fire", "alt", "a1", "a2", "ult", "reload", "melee", "swoop", "grind" };
        /// <summary>buttons whose presses must never be lost (a counter per button: the host latches every new press for one step)</summary>
        public static readonly string[] IN_EDGES = { "jump", "fire", "alt", "a1", "a2", "ult", "reload", "melee", "swoop" };
        public static void WriteInput(Writer w, InputPacket p)
        {
            w.U8(PK_INPUT).U16(p.seq).U16(p.ackSnap).F32(p.yaw).F32(p.pitch).I8(p.mx * 100).I8(p.mz * 100).U16(p.held);
            for (int i = 0; i < IN_EDGES.Length; i++) w.U8(p.presses != null && i < p.presses.Length ? p.presses[i] : 0);
            w.U16(p.viewMs);
        }
        public static InputPacket ReadInput(Reader r)
        {
            r.U8();
            var p = new InputPacket { seq = r.U16(), ackSnap = r.U16(), yaw = r.F32(), pitch = r.F32(), mx = r.I8() / 100.0, mz = r.I8() / 100.0, held = r.U16(), presses = new int[IN_EDGES.Length] };
            for (int i = 0; i < IN_EDGES.Length; i++) p.presses[i] = r.U8();
            p.viewMs = r.U16();
            return p;
        }

        /// <summary>PING / PONG (both ways, 4 Hz, unreliable): u8 type, u16 seq</summary>
        public static byte[] PingPacket(byte type, int seq) => new Writer(4).U8(type).U16(seq).Done();

        // ---------------------------------------------------------------- base64 (binary through the node's JSON relay)
        public static string ToB64(byte[] u) => Convert.ToBase64String(u);
        public static byte[] FromB64(string s) => Convert.FromBase64String(s);
    }

    public struct HotActor
    {
        public int id; public double x, y, z, vx, vy, vz, yaw, pitch, hp, armor, shield, bhp; public int flags; public double scale; public int beam;
    }
    public struct HotProj { public long id; public double x, y, z, vx, vy, vz; }
    public struct SnapHeader { public int seq; public double time; public int ackInput; public int tier; public double hostMs; }
    public class InputPacket { public int seq, ackSnap; public double yaw, pitch, mx, mz; public int held; public int[] presses; public int viewMs; }

    public class Writer
    {
        byte[] buf; public int n;
        public Writer(int size = 2048) { buf = new byte[size]; }
        void Room(int k)
        {
            if (n + k <= buf.Length) return;
            int size = buf.Length * 2; while (size < n + k) size *= 2;
            Array.Resize(ref buf, size);
        }
        public Writer Reset() { n = 0; return this; }
        /// <summary>JS `v &amp; 0xff`: truncated, not rounded</summary>
        public Writer U8(double v) { Room(1); buf[n++] = (byte)(ToUint32(v) & 0xff); return this; }
        public Writer I8(double v) { Room(1); buf[n++] = (byte)(sbyte)Math.Max(-128, Math.Min(127, K.Round(v))); return this; }
        public Writer U16(double v) { Room(2); int x = (int)Math.Max(0, Math.Min(0xffff, K.Round(v))); buf[n] = (byte)x; buf[n + 1] = (byte)(x >> 8); n += 2; return this; }
        public Writer I16(double v) { Room(2); int x = (short)Math.Max(-32768, Math.Min(32767, K.Round(v))); buf[n] = (byte)x; buf[n + 1] = (byte)(x >> 8); n += 2; return this; }
        /// <summary>JS `v >>> 0`: the value modulo 2^32</summary>
        public Writer U32(double v) { Room(4); Put32(ToUint32(v)); return this; }
        /// <summary>JS ToUint32: NaN / infinities -> 0, truncated toward zero, modulo 2^32</summary>
        static uint ToUint32(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            double t = Math.Truncate(v) % 4294967296.0;
            return (uint)(t < 0 ? t + 4294967296.0 : t);
        }
        public Writer F32(double v) { Room(4); Put32((uint)BitConverter.SingleToInt32Bits((float)v)); return this; }
        void Put32(uint x) { buf[n] = (byte)x; buf[n + 1] = (byte)(x >> 8); buf[n + 2] = (byte)(x >> 16); buf[n + 3] = (byte)(x >> 24); n += 4; }
        public Writer Bytes(byte[] b, int len = -1) { if (len < 0) len = b.Length; Room(len); Buffer.BlockCopy(b, 0, buf, n, len); n += len; return this; }
        /// <summary>utf-8 with a u32 length (cold JSON can pass 64 KB on a match's first snapshot)</summary>
        public Writer Str(string s) { var b = Encoding.UTF8.GetBytes(s ?? ""); U32(b.Length); return Bytes(b); }
        /// <summary>a copy of what was written</summary>
        public byte[] Done() { var o = new byte[n]; Buffer.BlockCopy(buf, 0, o, 0, n); return o; }
        public byte[] Raw => buf;
    }

    public class Reader
    {
        readonly byte[] u; public int n;
        public Reader(byte[] u) { this.u = u; }
        public int Left => u.Length - n;
        void Need(int k) { if (n + k > u.Length) throw new IndexOutOfRangeException("packet too short"); }
        public int U8() { Need(1); return u[n++]; }
        public int I8() { Need(1); return (sbyte)u[n++]; }
        public int U16() { Need(2); int v = u[n] | (u[n + 1] << 8); n += 2; return v; }
        public int I16() { Need(2); int v = (short)(u[n] | (u[n + 1] << 8)); n += 2; return v; }
        public long U32() { Need(4); uint v = (uint)(u[n] | (u[n + 1] << 8) | (u[n + 2] << 16) | (u[n + 3] << 24)); n += 4; return v; }
        public double F32() { Need(4); int v = u[n] | (u[n + 1] << 8) | (u[n + 2] << 16) | (u[n + 3] << 24); n += 4; return BitConverter.Int32BitsToSingle(v); }
        public string Str() { int len = (int)U32(); Need(len); var s = Encoding.UTF8.GetString(u, n, len); n += len; return s; }
    }
}
