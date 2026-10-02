// Headless entry point for the simulation: smoke tests now, parity scenarios as the port lands.
using System;
using System.IO;
using ZU.Sim;
using ZU.Sim.Data;

namespace ZU.SimTest
{
    static class Program
    {
        static int Main(string[] args)
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            var data = GameData.Load(Path.Combine(root, "Assets/ZU/Data"));
            var cmd = args.Length > 0 ? args[0] : "smoke";
            switch (cmd)
            {
                case "smoke": return Smoke(data);
                default: Console.Error.WriteLine("unknown command " + cmd); return 2;
            }
        }

        static int Smoke(GameData data)
        {
            Console.WriteLine($"heroes {data.Heroes.Count}, maps {data.Maps.Count}");
            int fails = 0;
            foreach (var m in data.Maps)
            {
                var lvl = new BoxLevel(m);
                var sp = m.spawns["zenith"];
                var g = lvl.GroundAt(sp[0], sp[1], 1);
                var p = new V3(sp[0], g, sp[1]);
                lvl.Collide(ref p, 0.45, 1.8);
                var los = lvl.LineOfSight(new V3(sp[0], g + 1.6, sp[1]), new V3(m.point[0], m.point[1] + 1, m.point[2]));
                Console.WriteLine($"  {m.id,-10} boxes {m.boxes.Count,4}  spawn ground {g,6:0.00}  spawn->point LOS {los}");
                if (double.IsNegativeInfinity(g)) { fails++; Console.WriteLine("    FAIL: zenith spawn has no ground"); }
            }
            Console.WriteLine(fails == 0 ? "smoke OK" : $"smoke FAILED ({fails})");
            return fails == 0 ? 0 : 1;
        }
    }
}
