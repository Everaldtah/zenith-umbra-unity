// TEMPORARY: the cross-module contract for modules not ported yet. Each stub has the final C# signature of its TS
// function; when the real port lands (Weapons.cs, Abilities*.cs, Puppets.cs, Susanoo.cs, Stadium.cs) its stub is
// deleted from here. Nothing here may stay once the port is done (tools/simtest checks: `grep PENDING` must be empty).
using System;
using ZU.Sim.Data;

namespace ZU.Sim
{
    public class Stadium
    {
        public bool frozen;                               // PENDING: stadium.ts
        public void Update(double dt) { }
    }
}
