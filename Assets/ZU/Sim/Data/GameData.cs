// Loads the exported game data (Assets/ZU/Data/*.json, written by tools/export) into lookup tables - the C# side of
// HERO / PILOT_BY_ID / ROBOTS / MAP / ENEMIES / BOSSES in the TS source.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ZU.Sim.Data
{
    public class GameData
    {
        public static GameData Current;

        public List<HeroDef> Heroes, Pilots, Robots, Enemies, Bosses;
        public List<MapDef> Maps;
        public Dictionary<string, HeroDef> Hero, PilotById, Robot, Enemy, Boss;
        public Dictionary<string, MapDef> Map;
        public Dictionary<string, List<Skin>> Skins;
        public Dictionary<string, List<PowerInfo>> Powers;
        public Newtonsoft.Json.Linq.JObject Rules;
        public CampaignFile Campaign;

        /// <summary>parse the four JSON files; Unity passes their text (TextAssets), tools pass a folder</summary>
        public static GameData FromJson(string heroes, string maps, string campaign, string rules)
        {
            var h = JsonConvert.DeserializeObject<HeroesFile>(heroes);
            var m = JsonConvert.DeserializeObject<MapsFile>(maps);
            var c = JsonConvert.DeserializeObject<CampaignFile>(campaign);
            var d = new GameData
            {
                Heroes = h.heroes, Pilots = h.pilots, Robots = h.robots, Skins = h.skins, Powers = h.powers,
                Maps = m.maps, Campaign = c, Enemies = c.enemies, Bosses = c.bosses,
                Rules = Newtonsoft.Json.Linq.JObject.Parse(rules),
            };
            d.Hero = d.Heroes.ToDictionary(x => x.id);
            d.PilotById = d.Pilots.ToDictionary(x => x.id);
            d.Robot = d.Robots.ToDictionary(x => x.id);
            d.Enemy = d.Enemies.ToDictionary(x => x.id);
            d.Boss = d.Bosses.ToDictionary(x => x.id);
            d.Map = d.Maps.ToDictionary(x => x.id);
            Current = d;
            return d;
        }

        public static GameData Load(string dir) => FromJson(
            File.ReadAllText(Path.Combine(dir, "heroes.json")), File.ReadAllText(Path.Combine(dir, "maps.json")),
            File.ReadAllText(Path.Combine(dir, "campaign.json")), File.ReadAllText(Path.Combine(dir, "rules.json")));

        /// <summary>TS: HERO[id] ?? PILOT_BY_ID[id] ?? ROBOTS[id] ?? ENEMIES / BOSSES (campaign extraDefs)</summary>
        public HeroDef Def(string id) =>
            Hero.TryGetValue(id, out var x) ? x : PilotById.TryGetValue(id, out x) ? x : Robot.TryGetValue(id, out x) ? x
            : Enemy.TryGetValue(id, out x) ? x : Boss.TryGetValue(id, out x) ? x : null;
    }
}
