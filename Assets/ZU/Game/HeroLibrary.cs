// The runtime catalogue of hero visuals (Resources/ZUHeroLibrary.asset, maintained by the editor commands): each hero's
// prefab, and the shared humanoid AnimatorController (per-hero AnimatorOverrideControllers swap in weapon-specific clips).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZU.Game
{
    [CreateAssetMenu(menuName = "ZU/Hero Library")]
    public class HeroLibrary : ScriptableObject
    {
        [Serializable] public class Entry { public string id; public GameObject prefab; public RuntimeAnimatorController controller; }
        public RuntimeAnimatorController baseController;
        public List<Entry> heroes = new List<Entry>();

        static HeroLibrary cached;
        public static HeroLibrary Get() => cached != null ? cached : cached = Resources.Load<HeroLibrary>("ZUHeroLibrary");

        public Entry Find(string id) => heroes.Find(e => e.id == id && e.prefab != null);
        public void Set(string id, GameObject prefab, RuntimeAnimatorController controller = null)
        {
            var e = heroes.Find(x => x.id == id);
            if (e == null) heroes.Add(e = new Entry { id = id });
            e.prefab = prefab; if (controller != null) e.controller = controller;
        }
    }
}
