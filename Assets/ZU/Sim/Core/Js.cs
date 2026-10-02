// JavaScript semantics the port depends on: a seedable Math.random, Math.round, a stable sort.
using System;
using System.Collections.Generic;

namespace ZU.Sim
{
    /// <summary>
    /// The simulation's only source of randomness (TS: Math.random). mulberry32 - the parity harness installs the very
    /// same generator as Math.random in the TS sim, so a seeded scenario draws identical numbers on both sides.
    /// </summary>
    public static class Rng
    {
        static uint state = 0x9E3779B9;
        static bool seeded;
        public static void Seed(uint seed) { state = seed; seeded = true; }
        /// <summary>unseeded play: seed from the clock once</summary>
        static void Ensure() { if (!seeded) { state = (uint)Environment.TickCount; seeded = true; } }
        public static double Random()
        {
            Ensure();
            unchecked
            {
                state += 0x6D2B79F5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return ((t ^ (t >> 14)) >> 0) / 4294967296.0;
            }
        }
    }

    public static class JsMath
    {
        /// <summary>JS Math.round: halves round toward +infinity (-2.5 -> -2)</summary>
        public static double Round(double x) => Math.Floor(x + 0.5);
        public static double Max(params double[] v) { double m = double.NegativeInfinity; foreach (var x in v) { if (double.IsNaN(x)) return double.NaN; if (x > m) m = x; } return m; }
        public static double Min(params double[] v) { double m = double.PositiveInfinity; foreach (var x in v) { if (double.IsNaN(x)) return double.NaN; if (x < m) m = x; } return m; }
    }

    public static class JsSort
    {
        /// <summary>Array.prototype.sort with a comparator: stable (V8 TimSort), in place</summary>
        public static List<T> Sort<T>(List<T> list, Comparison<T> cmp)
        {
            var idx = new List<(T v, int i)>(list.Count);
            for (int i = 0; i < list.Count; i++) idx.Add((list[i], i));
            idx.Sort((a, b) => { var c = Sign(cmp(a.v, b.v)); return c != 0 ? c : a.i.CompareTo(b.i); });
            for (int i = 0; i < list.Count; i++) list[i] = idx[i].v;
            return list;
        }
        /// <summary>comparators that return doubles (TS `(p, q) => p.d - q.d`)</summary>
        public static List<T> SortBy<T>(List<T> list, Func<T, T, double> cmp) => Sort(list, (a, b) => Sign(cmp(a, b)));
        static int Sign(double d) => d > 0 ? 1 : d < 0 ? -1 : 0;
    }
}
