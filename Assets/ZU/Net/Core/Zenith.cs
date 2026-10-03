// Launched from the Zenith.net launcher (port of zenith-umbra src/net/zenith.ts + desktop/main.cjs zenithQuery, d2f85ac):
// the launcher starts the game with --zenith-user=<name#tag> [--zenith-party=<id:seq> --zenith-role=host|member]. The
// player's tag is the default name; the party id lets launcher party members find each other's Starfall co-op squad (the
// party host hosts, everyone else joins the squad whose presence carries the same party id).
using System;
using System.Text.RegularExpressions;

namespace ZU.Net
{
    public static class Zenith
    {
        /// <summary>started from the launcher (a --zenith-user switch)</summary>
        public static bool Launched { get; private set; }
        /// <summary>the launcher account's name#tag (the default player name), or null</summary>
        public static string User { get; private set; }
        /// <summary>the launcher party id, or null (not in a party / malformed)</summary>
        public static string Party { get; private set; }
        /// <summary>this player leads the launcher party (hosts the squad)</summary>
        public static bool Host { get; private set; }

        static readonly Regex SWITCH = new Regex(@"^--zenith-(user|party|role)=(.{1,120})$");
        static readonly Regex PARTY = new Regex(@"^[\w:-]{8,80}$");

        static Zenith() => Parse(Environment.GetCommandLineArgs());

        /// <summary>read the launcher switches (the process's own at start-up; tests pass their own)</summary>
        public static void Parse(string[] args)
        {
            string user = null, party = null, role = null;
            foreach (var a in args ?? Array.Empty<string>())
            {
                var m = SWITCH.Match(a ?? "");
                if (!m.Success) continue;
                var v = m.Groups[2].Value;
                switch (m.Groups[1].Value) { case "user": user = v; break; case "party": party = v; break; default: role = v; break; }
            }
            Launched = !string.IsNullOrEmpty(user);
            User = Launched ? (user.Length > 20 ? user.Substring(0, 20) : user) : null;
            Party = Launched && party != null && PARTY.IsMatch(party) ? party : null;
            Host = Launched && role == "host";
        }
    }
}
