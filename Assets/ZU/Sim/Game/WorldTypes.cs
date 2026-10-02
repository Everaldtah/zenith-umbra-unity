// Types the World and every ability module share (TS: the interfaces at the top of src/game/World.ts).
using System.Collections.Generic;

namespace ZU.Sim
{
    public class Proj
    {
        public int id; public Actor owner; public string team; public V3 pos, vel;
        public double dmg, splash; public bool heal;
        public string fx; public double life, r, grav; public string special; public double crit;
        public HashSet<int> hits = new HashSet<int>(); public bool pierce; public double? homing; public double born;
        /// <summary>a ricochet shuriken (Hayate): walls left to skip off; the perimeter it hunts in; the enemy it hunts
        /// now (actor id); how many bounces so far (Fx draws the water while seekTgt or a bounce is set)</summary>
        public double? bounce, seek; public int? seekTgt; public double? bounced;
        /// <summary>drawn as this prop (WeaponDef.mesh), spinning at `spin` rad/s about its flat axis</summary>
        public string mesh; public double? spin;
    }

    /// <summary>optional fields for World.SpawnProj (TS: Partial&lt;Proj&gt;)</summary>
    public class ProjOpts
    {
        public double? dmg, splash, life, r, grav, crit, homing, bounce, seek, spin;
        public bool? heal, pierce; public string fx, special, mesh;
    }

    public class Zone
    {
        public int id; public string kind; public Actor owner; public string team;
        public double x, y, z, r, born, until, next;
        /// <summary>TS `data?: any`: per-kind values</summary>
        public Dictionary<string, object> data;
    }

    /// <summary>World.Damage options (TS: the `o` object)</summary>
    public class DmgOpts
    {
        public bool crit; public string kind; public double? shieldMult; public string ability; public bool noLifesteal, wound;
    }

    public class Timer { public double at; public System.Action fn; }

    public class HealthPack { public double x, y, z; public bool big; public double readyAt; }

    public class PointState
    {
        public string owner, capTeam; public double capture; public bool contested;
        public Dictionary<string, double> progress = new Dictionary<string, double> { ["zenith"] = 0, ["umbra"] = 0 };
        public double unlockAt = 8, r = 6;
    }

    public class ControlState
    {
        public int round = 1; public Dictionary<string, int> wins = new Dictionary<string, int> { ["zenith"] = 0, ["umbra"] = 0 };
        public string phase = "fight"; public double phaseEnd; public bool overtime;
    }

    public class PushState
    {
        public double d; public Dictionary<string, double> best = new Dictionary<string, double> { ["zenith"] = 0, ["umbra"] = 0 };
        public string owner; public bool contested; public double unlockAt = 15, half; public V3 pos; public int checkpoint; public bool overtime;
    }

    public class WorldStats
    {
        public int counters; public Dictionary<string, int> casts = new Dictionary<string, int>(), sfx = new Dictionary<string, int>(), fx = new Dictionary<string, int>();
    }

    /// <summary>prevIn: last tick's buttons, for edge detection (World.pressed)</summary>
    public class PrevInput { public bool a1, a2, ult, alt, jump, fire, melee, swoop, descend; }

    /// <summary>a path finder: the classic arenas' grid (BoxNav) or a Unity NavMesh on the detailed maps</summary>
    public interface INav
    {
        List<V3> Find(V3 from, V3 to, int maxIter = 60000);
        /// <summary>the nearest walkable point to p within maxR (TS: nav.center(nav.nearest(p, maxR)))</summary>
        V3? NearestPoint(V3 p, double maxR);
    }
    public interface IDirector { void Update(double dt); void OnKill(Actor a, Actor src); V3? Waypoint(); }
}
