// Animation level of detail (port of zenith-umbra src/engine/AnimBudget.ts, engine core), after Unreal's Update Rate
// Optimisation / Animation Budget Allocator that Fortnite runs on every character: a hero's skeleton is re-posed at a rate
// that follows how much of the screen it covers. Near or big on screen = every frame; small = 45 or 30 updates a second;
// off screen = 24 (its shadow still moves). On a frame a view sits out, only its root follows the simulation (HeroView
// re-applies the last procedural root pose at the current draw position), so the body never lags its hitbox - just the
// limbs re-pose a little less often where nobody can see the difference. The view gets the real time since its last update
// when it does run (ClipLayer's inertialization and the gait phase integrate it; ProcAnimator clamps at 50 ms, the slowest
// tier here is 42 ms). Updates are staggered so far heroes don't all land on the same frame. The engine switch turns it
// off too (-zu-engine=0 -> Perf.Enabled false = every view every frame, the TS `anim.enabled = this.on`), for A/B checks.
using UnityEngine;

namespace ZU.Engine
{
    public sealed class AnimBudget
    {
        /// <summary>projected hero height (CSS px) at which a view gets every frame / 45 Hz; below that 30 Hz</summary>
        public const float FULL_PX = 110f, MID_PX = 50f;
        /// <summary>the reduced update intervals (s): on screen but mid-sized, far, off screen</summary>
        public const float MID = 1f / 45, FAR = 1f / 30, HIDDEN = 1f / 24;

        public static readonly AnimBudget Shared = new AnimBudget();

        public bool enabled = true;
        /// <summary>Governor knob: >1 raises the pixel thresholds (more views on the reduced rates when the main thread is the
        /// bottleneck); 1 = the TS</summary>
        public float tierScale = 1f;
        /// <summary>this frame so far: views updated / held</summary>
        public int Updated, Held;
        /// <summary>the previous complete frame (for the HUD)</summary>
        public int LastUpdated, LastHeld;

        /// <summary>per view: how much time has gone by since its skeleton last ran; the caller keeps one (TS WeakMap slot)</summary>
        public sealed class Slot { internal float acc; }

        // the camera of the frame being drawn (last frame's pose is close enough), as the TS Frustum / cam / pxPerM
        readonly Plane[] planes = new Plane[6];
        Vector3 camPos;
        float pxPerM = 600;
        bool hasCam;
        int seq, frame = -1;

        /// <summary>a new view's slot, starting at a spread of phases so the reduced-rate ones don't all update together</summary>
        public Slot NewSlot() => new Slot { acc = (seq++ % 4) * FAR / 4 };

        /// <summary>once a frame, before the views: the camera that will show them. Step calls it itself with Camera.main
        /// on the first view of a new frame, so a driver only needs it to pick another camera.</summary>
        public void Begin(Camera cam)
        {
            frame = Time.frameCount;
            LastUpdated = Updated; LastHeld = Held;
            Updated = Held = 0;
            hasCam = cam != null;
            if (!hasCam) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);     // (the non-allocating overload)
            camPos = cam.transform.position;
            // the thresholds are CSS px (the TS measures against window.innerHeight); a Windows player's Screen.height is
            // device px, and its DPR is dpi / 96 - so a 150 % desktop at 1440 px tall counts as the 960 CSS px the web build saw
            float viewportH = Screen.dpi > 0 ? Screen.height * 96f / Screen.dpi : Screen.height;
            pxPerM = viewportH / 2 / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);     // (fieldOfView is the vertical one)
        }

        /// <summary>
        /// The dt to pose this view with this frame, or -1 to hold its pose. Never held while the engine is off
        /// (Perf.Enabled false, -zu-engine=0): the TS tied anim.enabled to the engine switch.
        /// </summary>
        /// <param name="feet">where the body is drawn (its root, at the feet)</param>
        /// <param name="height">the hero's height in metres (scale included)</param>
        /// <param name="always">this view must run every frame (own hero, bosses, the dead / ragdolls, holograms, forced moves)</param>
        public float Step(Slot s, Vector3 feet, float height, float dt, bool always)
        {
            if (frame != Time.frameCount) Begin(Camera.main);
            if (dt <= 0) { Updated++; return dt; }                   // paused: nothing to accumulate
            s.acc += dt;
            if (!enabled || !Perf.Enabled || always || s.acc + 1e-3f >= Interval(feet, height))
            {
                // everything since its last update (a long frame still passes through whole, as before)
                float d = Mathf.Min(s.acc, Mathf.Max(dt, 0.05f));
                s.acc = 0;
                Updated++;
                return d;
            }
            Held++;
            return -1;
        }

        /// <summary>the minimum time between this hero's skeleton updates (0 = every frame); no camera = every frame</summary>
        public float Interval(Vector3 feet, float height)
        {
            if (!hasCam) return 0;
            float h = Mathf.Max(0.5f, height);
            var c = feet; c.y += h / 2;
            float r = h * 0.75f + 0.5f;
            // the frustum planes face inward: a sphere wholly behind any one of them is off screen
            for (int i = 0; i < 6; i++) if (planes[i].GetDistanceToPoint(c) < -r) return HIDDEN;
            float dist = Mathf.Max(0.1f, Vector3.Distance(camPos, c));
            float px = h * pxPerM / dist;
            return px >= FULL_PX * tierScale ? 0 : px >= MID_PX * tierScale ? MID : FAR;
        }
    }
}
