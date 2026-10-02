// One hero / robot / summon on screen. The prototype body is a team-coloured capsule with a head and a visor showing the
// aim; the rigged hero models (Assets/ZU/Art/Heroes) take its place as they are imported.
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public class ActorView : MonoBehaviour, IActorView
    {
        Transform body, head, visor;
        Renderer[] rends;
        int actorId;

        public static ActorView Create(Actor a, Transform parent)
        {
            var go = new GameObject($"{a.def.id} #{a.id} ({a.team})");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<ActorView>();
            v.actorId = a.id;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var team = Conv.Hex(a.team == "zenith" ? "#5cc8ff" : "#ff3b5c");
            var col = Color.Lerp(Conv.Hex(a.def.color, team), team, 0.35f);
            Material M(Color c, float glow = 0)
            {
                var m = new Material(lit); m.SetColor("_BaseColor", c);
                if (glow > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * glow); }
                return m;
            }
            v.body = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
            v.body.SetParent(go.transform, false); Destroy(v.body.GetComponent<Collider>());
            v.head = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            v.head.SetParent(go.transform, false); Destroy(v.head.GetComponent<Collider>());
            v.visor = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            v.visor.SetParent(v.head, false); Destroy(v.visor.GetComponent<Collider>());
            v.body.GetComponent<Renderer>().sharedMaterial = M(col);
            v.head.GetComponent<Renderer>().sharedMaterial = M(Color.Lerp(col, Color.white, 0.3f));
            v.visor.GetComponent<Renderer>().sharedMaterial = M(Conv.Hex(a.def.glow, Color.white), 2.5f);
            v.visor.localPosition = new Vector3(0, 0.1f, 0.45f); v.visor.localScale = new Vector3(0.8f, 0.25f, 0.2f);
            v.rends = go.GetComponentsInChildren<Renderer>();
            return v;
        }

        public void Sync(IViewHost r, Actor a)
        {
            bool show = a.alive && !(a == r.Player && !r.ThirdPerson);
            if (a.Has("stealth", r.SimTime) && r.Player != null && a.team != r.Player.team && !a.Has("revealed", r.SimTime)) show = false;
            foreach (var x in rends) x.enabled = show;
            if (!a.alive) return;
            transform.position = r.DrawPos(a);
            transform.rotation = Conv.Yaw(a.yaw);
            float h = (float)a.Height, rad = (float)a.Radius;
            float headR = (float)World.HeadR(a);
            body.localPosition = new Vector3(0, (h - headR * 2) / 2, 0);
            body.localScale = new Vector3(rad * 2, (h - headR * 2) / 2, rad * 2);
            head.localPosition = new Vector3(0, h - headR * 1.1f, 0);
            head.localScale = Vector3.one * headR * 2;
            head.localRotation = Quaternion.Euler(Conv.PitchDeg(a.pitch), 0, 0);
        }
    }
}
