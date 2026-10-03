// What the simulation tells the presentation layer each tick (TS: World.ts `GameEvent` union). Views, audio and the HUD
// only read state and drain World.events - exactly as the TS renderer does.
namespace ZU.Sim
{
    public abstract class SimEvent { public abstract string T { get; } }

    public sealed class SfxEvent : SimEvent { public override string T => "sfx"; public string id; public V3? pos; public double? vol; public Actor actor; }

    /// <summary>optional fields of an fx event (TS Partial&lt;Extract&lt;GameEvent, { t: 'fx' }&gt;&gt;)</summary>
    public class FxOpts { public V3? to, n; public double? r, dur, side; public string color, mat; public Actor actor, target; }
    public sealed class FxEvent : SimEvent
    {
        public override string T => "fx";
        public string kind; public V3 pos; public V3? to, n; public double? r, dur, side; public string color, mat; public Actor actor, target;
        public FxEvent(string kind, V3 pos, FxOpts o)
        {
            this.kind = kind; this.pos = pos;
            if (o != null) { to = o.to; n = o.n; r = o.r; dur = o.dur; side = o.side; color = o.color; mat = o.mat; actor = o.actor; target = o.target; }
        }
    }

    public sealed class DmgEvent : SimEvent { public override string T => "dmg"; public Actor src, tgt; public double amt; public bool crit, heal; public V3 pos; public string kind; }
    public sealed class KillEvent : SimEvent { public override string T => "kill"; public Actor src, tgt; }
    public sealed class DemechEvent : SimEvent { public override string T => "demech"; public Actor src, tgt; }
    public sealed class CastEvent : SimEvent { public override string T => "cast"; public Actor actor; public string id, name; }
    public sealed class CounterEvent : SimEvent { public override string T => "counter"; public Actor actor, target; public string text; }
    public sealed class MsgEvent : SimEvent { public override string T => "msg"; public string text, color; }
}
