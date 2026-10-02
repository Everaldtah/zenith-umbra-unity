// JSON shapes shared with the TS build: simulation events ({ t, ...fields } with actors as ids - TS FastSync packEvent),
// the input bits, and JS-style rounding of the numbers that go into cold state.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using ZU.Sim;

namespace ZU.Net
{
    public static class Wire
    {
        public static double R1(double v) => K.Round(v * 10) / 10;
        public static double R2(double v) => K.Round(v * 100) / 100;
        public static long Rn(double v) => (long)K.Round(v);
        public static JObject V(V3 p) => new JObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z };
        public static V3 V(JToken t) => t is JObject o ? new V3((double?)o["x"] ?? 0, (double?)o["y"] ?? 0, (double?)o["z"] ?? 0) : new V3(0, 0, 0);

        // ---------------------------------------------------------------- events
        static readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
        static Dictionary<string, Type> byTag;
        static FieldInfo[] Fields(Type t)
        {
            if (!fields.TryGetValue(t, out var f)) fields[t] = f = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            return f;
        }
        /// <summary>{ t, every field that is set } with actors as their ids (TS packEvent: `{ ...e }`, actor/target/src/tgt -> id)</summary>
        public static JObject Pack(SimEvent e)
        {
            var o = new JObject { ["t"] = e.T };
            foreach (var f in Fields(e.GetType()))
            {
                var v = f.GetValue(e);
                if (v == null) continue;
                switch (v)
                {
                    case Actor a: o[f.Name] = a.id; break;
                    case V3 p: o[f.Name] = V(p); break;
                    case bool b: o[f.Name] = b; break;
                    case string s: o[f.Name] = s; break;
                    case double d: o[f.Name] = d; break;
                    case int i: o[f.Name] = i; break;
                    default: try { o[f.Name] = JToken.FromObject(v); } catch { /* not wire data */ } break;
                }
            }
            return o;
        }
        /// <summary>an event from the host, its actor ids resolved (null when it names an actor this client doesn't know yet -
        /// its first snapshot is still on the way - or a kind this build doesn't have)</summary>
        public static SimEvent Unpack(JObject o, Func<int, Actor> actor)
        {
            if (byTag == null)
            {
                byTag = new Dictionary<string, Type>();
                foreach (var t in typeof(SimEvent).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(SimEvent).IsAssignableFrom(t)))
                    byTag[((SimEvent)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t)).T] = t;
            }
            if (!byTag.TryGetValue((string)o["t"] ?? "", out var type)) return null;
            var e = (SimEvent)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            foreach (var f in Fields(type))
            {
                var tok = o[f.Name];
                if (tok == null || tok.Type == JTokenType.Null) continue;
                var ft = f.FieldType;
                try
                {
                    if (ft == typeof(Actor)) { var a = actor((int)tok); if (a == null) return null; f.SetValue(e, a); }
                    else if (ft == typeof(V3) || ft == typeof(V3?)) f.SetValue(e, V(tok));
                    else f.SetValue(e, tok.ToObject(ft));
                }
                catch { /* a field this build reads differently */ }
            }
            return e;
        }

        // ---------------------------------------------------------------- input buttons by their TS names
        public static bool Btn(SimInput i, string k)
        {
            switch (k)
            {
                case "jump": return i.jump; case "jumpHeld": return i.jumpHeld; case "descend": return i.descend;
                case "fire": return i.fire; case "alt": return i.alt; case "a1": return i.a1; case "a2": return i.a2;
                case "ult": return i.ult; case "reload": return i.reload; case "melee": return i.melee; case "swoop": return i.swoop;
                case "grind": return i.grind == true;
            }
            return false;
        }
        public static void SetBtn(SimInput i, string k, bool v)
        {
            switch (k)
            {
                case "jump": i.jump = v; break; case "jumpHeld": i.jumpHeld = v; break; case "descend": i.descend = v; break;
                case "fire": i.fire = v; break; case "alt": i.alt = v; break; case "a1": i.a1 = v; break; case "a2": i.a2 = v; break;
                case "ult": i.ult = v; break; case "reload": i.reload = v; break; case "melee": i.melee = v; break; case "swoop": i.swoop = v; break;
                case "grind": i.grind = v; break;
            }
        }
    }
}
