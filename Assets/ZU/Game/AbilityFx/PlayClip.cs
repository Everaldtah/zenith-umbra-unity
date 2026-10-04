// A cut of a match as the views drew it: the recorder's frames (frozen copies of every hero and projectile, 30 a second)
// and the effect / sound events of a stretch of sim time, with a header that says what it is (the kill cam's replay, the
// Play of the Game, a player's best play). KillCam.Cut makes one from the live record; KillCam.Play replays it through the
// hero views; Save / Load keep it on disk (PlayClip.Disk.cs).
using System.Collections.Generic;
using ZU.Sim;

namespace ZU.Game.Fx
{
    public sealed partial class PlayClip
    {
        // ---- header: plain data, filled by whoever cut the clip (the play detector, the highlights library)
        public string map, mode, heroId, heroName, team, category, summary, playerName;
        public string[] lines;
        public double score, t0, t1;
        /// <summary>the hero the lens follows (its actor id in the clip's frames)</summary>
        public int focusId;
        public bool potg, mine;
        /// <summary>when it was cut (unix ms)</summary>
        public long at;
        /// <summary>the file it was loaded from / saved to (not stored in the file)</summary>
        public string path;

        // ---- body: what the views replay
        /// <summary>one recorded moment: every hero with a view as it was (by actor id), every projectile</summary>
        public sealed class Frame { public double t; public Dictionary<int, Actor> actors; public List<Proj> projs; }
        public List<Frame> frames = new List<Frame>();
        /// <summary>the effect and sound events of the stretch, by sim time</summary>
        public List<(double t, SimEvent e)> events = new List<(double, SimEvent)>();

        public double Seconds => t1 - t0;
        public bool HasBody => frames != null && frames.Count > 1;
    }
}
