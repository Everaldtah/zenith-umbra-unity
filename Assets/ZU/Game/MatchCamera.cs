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
            var off = Quaternion.Euler(28, orbit, 0) * new Vector3(0, 0, -42);
            transform.position = p + off;
            transform.LookAt(p + Vector3.up * 2);
        }
    }
}
