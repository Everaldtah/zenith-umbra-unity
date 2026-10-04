// The match camera: first person at the player's eye (Overwatch's Normal mode), or a third-person shoulder camera that
// keeps off walls; with no player it orbits the fight (spectate / AI matches).
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public class MatchCamera : MonoBehaviour
    {
        Camera cam;
        float orbit;
        // the spectate orbit: how far out and how steeply it looks down right now (eased), see Place
        float specDist = SpecDist, specPitch = SpecPitches[0];
        const float SpecDist = 42, SpecClear = 0.6f;
        static readonly float[] SpecPitches = { 28, 40, 52, 64, 76 };

        public static MatchCamera Ensure(MatchRunner r)
        {
            var c = Camera.main;
            if (c == null) { var go = new GameObject("Main Camera") { tag = "MainCamera" }; c = go.AddComponent<Camera>(); go.AddComponent<AudioListener>(); }
            var mc = c.GetComponent<MatchCamera>(); if (mc == null) mc = c.gameObject.AddComponent<MatchCamera>();
            mc.cam = c;
            c.fieldOfView = 78; c.nearClipPlane = 0.05f; c.farClipPlane = 2500;
            return mc;
        }

        public void Sync(MatchRunner r)
        {
            Place(r);
            // Options: the field of view (the TS camera takes 0.75 of it as the vertical angle; a zoomed sight 38), eased in
            float fov = r.Player != null && r.Player.Sv("zoom") != 0 ? 38 : (float)UI.Toolkit.ZuSettings.Current.fov * 0.75f;
            cam.fieldOfView += (fov - cam.fieldOfView) * Mathf.Min(1, Time.deltaTime * 12);
            // the effects' camera shake (slams, stomps, explosions near you): a small random turn, fading with the budget,
            // times Options > Accessibility > Camera Shake
            float k = (Fx.MatchFx.Current != null ? Fx.MatchFx.Current.Shake : 0) * (float)UI.Toolkit.ZuSettings.Current.access.cameraShake;
            if (k > 0.002f) transform.rotation *= Quaternion.Euler((Random.value - 0.5f) * k * 6, (Random.value - 0.5f) * k * 6, 0);
        }

        void Place(MatchRunner r)
        {
            var me = r.Player;
            if (me != null)
            {
                var feet = r.DrawPos(me);
                var eyeH = (float)(me.Eye.y - me.pos.y);
                var rot = Conv.Aim(me.input.yaw, me.input.pitch);
                if (!r.thirdPerson)
                {
                    transform.SetPositionAndRotation(feet + Vector3.up * eyeH, rot);
                    return;
                }
                // third person: over the right shoulder, pulled in where a wall would come between
                var pivot = feet + Vector3.up * eyeH;
                var want = pivot + rot * new Vector3(0.6f, 0.25f, -3.2f * Mathf.Max(1, (float)me.scale));
                var dir = want - pivot;
                if (Physics.SphereCast(pivot, 0.2f, dir.normalized, out var hit, dir.magnitude)) want = pivot + dir.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
                transform.SetPositionAndRotation(want, rot);
                return;
            }
            // spectate: a slow orbit around the objective
            var w = r.World;
            var p = w.rules == "push" ? Conv.U(w.push.pos) : Conv.U(w.map.point[0], w.map.point[1], w.map.point[2]);
            orbit += Time.deltaTime * 6;
            // ...kept out of the level. The orbit was a fixed 42 m at 28 degrees; in a canyon (Iron Gulch) or under a roof that
            // point is inside the rock and the spectator stared at a wall. From the objective outwards, the lowest of five
            // elevations with a clear line wins; when none is clear, the one that gets furthest out; the view then closes
            // in to where the level begins. Elevation and distance are eased, and the eased view is checked once more.
            var hub = p + Vector3.up * 3;
            float bestPitch = SpecPitches[0], bestDist = -1;
            foreach (float pitch in SpecPitches)
            {
                float d = SpecFree(hub, pitch, SpecDist);
                if (d > bestDist + 0.5f) { bestDist = d; bestPitch = pitch; }
                if (d >= SpecDist - 0.5f) break;
            }
            float dt = Time.deltaTime, slow = 1 - Mathf.Exp(-dt * 2.5f), fast = 1 - Mathf.Exp(-dt * 10);
            specPitch = Mathf.Lerp(specPitch, bestPitch, slow);
            specDist = Mathf.Lerp(specDist, bestDist, bestDist < specDist ? fast : slow);       // in quickly, out gently
            float dist = Mathf.Max(4, Mathf.Min(specDist, SpecFree(hub, specPitch, specDist)));
            transform.position = hub + Quaternion.Euler(specPitch, orbit, 0) * Vector3.back * dist;
            transform.LookAt(p + Vector3.up * 2);
        }

        /// <summary>how far the spectate camera can go from the pivot at this elevation before the level is in the way</summary>
        float SpecFree(Vector3 pivot, float pitch, float max)
        {
            var dir = Quaternion.Euler(pitch, orbit, 0) * Vector3.back;
            return Physics.SphereCast(pivot, SpecClear, dir, out var hit, max, ~0, QueryTriggerInteraction.Ignore) ? Mathf.Max(0, hit.distance - 0.3f) : max;
        }
    }
}
