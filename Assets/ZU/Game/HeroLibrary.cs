// The runtime catalogue of hero visuals (Resources/ZUHeroLibrary.asset, maintained by the editor commands): each hero's
// prefab, and the shared humanoid AnimatorController (per-hero AnimatorOverrideControllers swap in weapon-specific clips).
// The prefabs are LAZY references: loading the library (the menu does, first thing) loads no hero; each prefab - its
// meshes and 4K textures - loads the first time something asks for it. Plain references made the menu load every hero
// in the game (2.7 GB of textures) before it could show anything.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ZU.Game
{
    [CreateAssetMenu(menuName = "ZU/Hero Library")]
    public class HeroLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string id;
            /// <summary>the old eager reference, kept only so an unmigrated asset still works (zu_lazy_library moves it)</summary>
            [SerializeField, FormerlySerializedAs("prefab")] GameObject legacyPrefab;
            [SerializeField] LazyLoadReference<GameObject> prefabRef;
            public RuntimeAnimatorController controller;
            /// <summary>the hero's prefab - loaded on first use</summary>
            public GameObject prefab
            {
                get => legacyPrefab != null ? legacyPrefab : prefabRef.isSet ? prefabRef.asset : null;
                set { prefabRef = value; legacyPrefab = null; }
            }
            /// <summary>whether a prefab is set, without loading it</summary>
            public bool Has => legacyPrefab != null || (prefabRef.isSet && !prefabRef.isBroken);
            /// <summary>Editor migration: the eager reference becomes the lazy one</summary>
            public bool MakeLazy() { if (legacyPrefab == null) return false; prefabRef = legacyPrefab; legacyPrefab = null; return true; }
        }
        public RuntimeAnimatorController baseController;
        public List<Entry> heroes = new List<Entry>();

        static HeroLibrary cached;
        public static HeroLibrary Get()
        {
            if (cached != null) return cached;
            float t0 = Time.realtimeSinceStartup;
            cached = Resources.Load<HeroLibrary>("ZUHeroLibrary");
            Debug.Log($"[ZU] hero library loaded in {(Time.realtimeSinceStartup - t0) * 1000:0} ms ({cached?.heroes.Count ?? 0} heroes, lazy)");
            return cached;
        }

        public Entry Find(string id) => heroes.Find(e => e.id == id && e.Has);
        public void Set(string id, GameObject prefab, RuntimeAnimatorController controller = null)
        {
            var e = heroes.Find(x => x.id == id);
            if (e == null) heroes.Add(e = new Entry { id = id });
            e.prefab = prefab; if (controller != null) e.controller = controller;
        }
    }
}
