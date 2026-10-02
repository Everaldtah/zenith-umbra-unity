// The front end's choices that outlive a scene (the TS Menu object lives for the whole session; here the Menu and Match
// scenes come and go): the queue and role picked, the AI Quick Match difficulty and map choice, and where the menu
// opens when a match hands back to it (the title, hero select after CHANGE HERO, matchmaking after QUEUE AGAIN).
using System.Collections.Generic;
using System.Linq;
using ZU.Sim.Data;

namespace ZU.Game.UI.Toolkit
{
    public static class MenuState
    {
        /// <summary>quickplay | competitive | practice, or null (the other modes)</summary>
        public static string Queue;
        /// <summary>tank | damage | support | flex</summary>
        public static string Role = "flex";
        public static double Diff = 0.62;
        public static string MapChoice = "random";
        /// <summary>the screen the menu opens on next: "title" | "heroes" | "find" | "campaign"</summary>
        public static string ReturnTo = "title";
        public static readonly Dictionary<string, string> QUEUE_NAME = new Dictionary<string, string> { ["quickplay"] = "QUICK PLAY", ["competitive"] = "COMPETITIVE", ["practice"] = "AI QUICK MATCH" };
        public static readonly Dictionary<string, string> ROLE_NAME = new Dictionary<string, string> { ["tank"] = "TANK", ["damage"] = "DAMAGE", ["support"] = "SUPPORT", ["flex"] = "FLEX" };
        /// <summary>role queue's role -> the heroes' role field</summary>
        public static readonly Dictionary<string, string> ROLE_HERO = new Dictionary<string, string> { ["tank"] = "tank", ["damage"] = "dps", ["support"] = "support" };
        public static readonly Dictionary<string, string> TEAM_NAME = new Dictionary<string, string> { ["zenith"] = "Zenith Vanguard", ["umbra"] = "Umbra Syndicate" };

        /// <summary>the desktop edition's playlist (TS PLAY_MAPS): every map but the training grounds and the retired arenas</summary>
        public static List<MapDef> PlayMaps(GameData d) => d.Maps.Where(m => m.id != "training" && !m.retired).ToList();
        /// <summary>the playable roster (TS rosterFor(FULL)): every hero, no summons</summary>
        public static List<HeroDef> Roster(GameData d) => d.Heroes.Where(h => !h.summoned).ToList();
        public static MapDef RandomMap(GameData d) { var l = PlayMaps(d); return l[UnityEngine.Random.Range(0, l.Count)]; }
    }
}
