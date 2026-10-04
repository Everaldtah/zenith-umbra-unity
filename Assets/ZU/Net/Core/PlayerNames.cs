// Who plays a hero in an online match (the user, 2026-10-04: until enough people are online the AI fills most seats - "there
// can be 2 real players online with usernames x, y and the remaining bot players ... will be bot 1, bot 2 etc"). A seat a
// person took shows that person's username; every seat the AI fills shows "Bot 1", "Bot 2", ... counted through both teams
// (Zenith's first, then Umbra's, in roster order), the same on every machine: the names come from the start message's
// seats and the heroes in the world, which the host and every client share. A player who drops out keeps their name on the
// hero the AI takes over, so the numbers never shift mid-match.
// Outside online PvP (offline modes, the co-op campaign) there are no names: Of() is null and the UI shows the hero's.
using System.Collections.Generic;
using System.Linq;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.Net
{
    public static class PlayerNames
    {
        /// <summary>the name of the n-th seat the AI fills (1-based)</summary>
        public static string Bot(int n) => "Bot " + n;

        /// <summary>a username as a label can show it: no rich-text tags from a typed name</summary>
        public static string Safe(string name) => string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim().Replace('<', '‹').Replace('>', '›');

        static NetMatch forMatch; static World forWorld; static int forCount;
        static readonly Dictionary<Actor, string> names = new Dictionary<Actor, string>();

        /// <summary>the username of whoever plays this hero in the online match being played, or "Bot N" for a seat the AI
        /// fills; a summon answers for its summoner. null: not an online match, or not a hero (a training robot)</summary>
        public static string Of(Actor a)
        {
            var nm = NetMatch.Current;
            var w = nm?.Built?.world;
            if (nm == null || nm.Start == null || w == null || a == null) return null;
            if (a.IsSummon) a = a.owner ?? a;
            if (a.isRobot) return null;
            int count = 0;
            foreach (var x in w.actors) if (!x.isRobot) count++;
            if (forMatch != nm || forWorld != w || forCount != count) Build(nm, w, count);
            return names.TryGetValue(a, out var s) ? s : null;
        }

        /// <summary>the username or "Bot N" online, the hero's name everywhere else</summary>
        public static string OrHero(Actor a) => Of(a) ?? a.def.name;

        static void Build(NetMatch nm, World w, int count)
        {
            names.Clear(); forMatch = nm; forWorld = w; forCount = count;
            foreach (var kv in Assign(nm.Start.seats, w)) names[kv.Key] = kv.Value;
        }

        /// <summary>the names of a match's heroes from its seats (the start message's): a seat's hero - the peer that plays it,
        /// or the side and hero it picked - takes the seat's username, every other hero the next Bot number</summary>
        public static Dictionary<Actor, string> Assign(List<Seat> seats, World w)
        {
            var res = new Dictionary<Actor, string>();
            var roster = GameData.Current.Heroes;
            int n = 0;
            foreach (var team in new[] { "zenith", "umbra" })
                foreach (var a in w.actors.Where(x => !x.isRobot && x.team == team).OrderBy(x => roster.FindIndex(h => h.id == (x.baseDef ?? x.def).id)))
                {
                    string hero = (a.baseDef ?? a.def).id;
                    var seat = (!string.IsNullOrEmpty(a.netId) ? seats.FirstOrDefault(x => x.id == a.netId) : null) ?? seats.FirstOrDefault(x => x.team == team && x.hero == hero);
                    res[a] = seat != null ? Safe(seat.name) : Bot(++n);
                }
            return res;
        }
    }
}
