// Where the simulation's events go each step: the kill feed and announcements for the HUD now; the effects and audio
// systems subscribe here as they are built.
using System;
using System.Collections.Generic;
using ZU.Sim;

namespace ZU.Game
{
    public static class EventSink
    {
        public struct Line { public string text; public string color; public float until; }
        public static readonly List<Line> feed = new List<Line>();
        public static event Action<MatchRunner, SimEvent> OnEvent;

        public static void Handle(MatchRunner r, SimEvent e)
        {
            float now = UnityEngine.Time.time;
            switch (e)
            {
                case KillEvent k:
                    Add(k.src != null ? $"{k.src.def.name}  >  {k.tgt.def.name}" : $"{k.tgt.def.name} fell", k.tgt.team == "zenith" ? "#ff3b5c" : "#5cc8ff", now, 5);
                    break;
                case MsgEvent m: Add(m.text, m.color ?? "#ffffff", now, 4); break;
                case CounterEvent c: Add($"COUNTER  {c.text}", "#ffe28a", now, 4); break;
            }
            OnEvent?.Invoke(r, e);
        }

        static void Add(string text, string color, float now, float secs)
        {
            feed.Add(new Line { text = text, color = color, until = now + secs });
            if (feed.Count > 8) feed.RemoveAt(0);
        }
    }
}
