// Game data, mirrored field for field from the TypeScript source (zenith-umbra src/data/*.ts) and loaded from the JSON
// that tools/export_data.mjs writes. Names stay camelCase-compatible through Newtonsoft's default (case-insensitive) binding.
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ZU.Sim.Data
{
    /// <summary>A weapon (kind set) or an ability (id set): TS types `WeaponDef | AbilityDef` share the hero slots.</summary>
    public class SlotDef
    {
        // ---- WeaponDef
        public string kind;            // projectile | hitscan | melee | beam | charge
        public string name;
        public double damage, rate, range;
        public double? speed, splash, spread, reload, delay, burstGap, spin, seek;
        public int? pellets, ammo, burst, bounce;
        public bool heal, sweep;
        public string mesh, note, sfx, fx;
        // ---- AbilityDef
        public string id, key, desc, counter;
        public double cooldown;
        public bool hold;
        public double charge;           // ult only

        [JsonIgnore] public bool IsAbility => id != null;
        [JsonIgnore] public bool IsWeapon => id == null && kind != null;
    }

    public class Passive { public string name, desc; }
    public class Pilot { public string id, name, bio; }

    public class HeroDef
    {
        public string id, name, title, team, role, subrole, frame, rival;
        public double hp, armor, speed, height, radius;
        public string color, glow;
        public SlotDef primary, secondary, ability1, ability2, ult;
        public Passive passive;
        public string lore, inspiration;
        public double[] voice;
        public Pilot pilot;
        public double? jets;
        public bool gunProp, dualGuns, full, summoned;
        public string model, holo;
        public double? scale;
        // campaign bosses (BossDef extends HeroDef)
        public string[] attacks;
        public double hpPerPlayer;
        public string summon, weak;
    }

    public class Skin
    {
        public string id, name, rarity, primary, accent, neutral, patternColor, model;
        public double metal, glow, pattern;
    }

    public class PowerInfo { public string id, name, desc; }

    public class HeroesFile
    {
        public List<HeroDef> heroes, robots;
        /// <summary>mech id -> its pilot on foot (TS PILOTS)</summary>
        public Dictionary<string, HeroDef> pilots;
        public Dictionary<string, Dictionary<string, string>> teams;
        public Dictionary<string, List<Skin>> skins;
        public Dictionary<string, List<PowerInfo>> powers;
    }

    // ------------------------------------------------------------------ maps (src/data/maps.ts)
    public class Box
    {
        public double x, z, w, d, h;
        public double? y;
        public string mat, ramp;      // ramp: x+ | x- | z+ | z-
    }
    public class Prop { public string id; public double x, z; public double? y, rot, s, solid; }
    public class Pad { public double x, z, vx, vy, vz; public double? y; }
    public class Pack { public double x, z; public double? y; public bool big; }
    public class Sun { public string color; public double intensity; public double[] dir; }

    public class MapDef
    {
        public string id, name, story;
        public string[] heroes;
        public double[] size;                 // half extents X, Z
        public List<Box> floors, boxes, decor;
        public List<Prop> props;
        public List<Pad> pads;
        public List<Pack> packs;
        public Dictionary<string, double[]> spawns;   // zenith / umbra -> [x, z]
        public double[] point;                // capture point x, y, z
        public Sun sun;
        public object[] ambient;             // [sky colour, ground colour, intensity]
        public object[] fog;                 // [colour, near, far]
        public string tint, particles, objective, payload;
        public double killY;
        public List<double[]> path;           // push route
        public bool full, retired;
        public double? water, payloadYaw;
        public double[] bloom;
    }
    public class MapsFile { public List<MapDef> maps; }

    // ------------------------------------------------------------------ campaign (src/campaign/data.ts)
    public class Encounter { public double[] at; public double r; public List<List<object[]>> waves; }
    public class CampaignFile
    {
        public List<HeroDef> enemies, bosses;
        public List<Newtonsoft.Json.Linq.JObject> levels;   // ported with Director.cs
        public string[] heroes;
    }
}
