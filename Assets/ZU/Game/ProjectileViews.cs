// Projectiles: a pooled glowing sphere per live projectile, coloured by its owner's glow (prop projectiles and trails
// come with the effects pass).
using System.Collections.Generic;
using UnityEngine;
using ZU.Sim;

namespace ZU.Game
{
    public class ProjectileViews
    {
        readonly Transform root;
        readonly List<Transform> pool = new List<Transform>();
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        readonly Shader lit = Shader.Find("Universal Render Pipeline/Lit");

        public ProjectileViews(Transform parent) { root = new GameObject("Projectiles").transform; root.SetParent(parent, false); }

        Material Mat(string glow)
        {
            if (mats.TryGetValue(glow ?? "", out var m)) return m;
            var c = Conv.Hex(glow, Color.white);
            m = new Material(lit); m.SetColor("_BaseColor", c); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 4f);
            mats[glow ?? ""] = m;
            return m;
        }

        public void Sync(World w, float alpha)
        {
            int n = 0;
            foreach (var p in w.projs)
            {
                if (n >= pool.Count)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
                    Object.Destroy(s.GetComponent<Collider>()); s.SetParent(root, false); pool.Add(s);
                }
                var t = pool[n++];
                t.gameObject.SetActive(true);
                // ahead of the last step by the time not yet simulated (velocity is constant within a step)
                var v = Conv.U(p.vel) * (float)(alpha * MatchRunner.DT);
                t.position = Conv.U(p.pos) + v;
                t.localScale = Vector3.one * Mathf.Max(0.12f, (float)p.r * 1.6f);
                t.GetComponent<Renderer>().sharedMaterial = Mat(p.owner?.def.glow);
            }
            for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }
    }
}
