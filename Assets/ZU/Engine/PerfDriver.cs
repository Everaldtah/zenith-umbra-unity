// The engine's frame tick (the Unity side of zenith-umbra src/engine/EngineCore.ts's frame() / endRender(), engine
// core). The web Game called the engine at the top of its rAF loop and after its last draw; Unity owns the loop, so a
// hidden, scene-independent MonoBehaviour does it: Update at execution order -10000 (before any script reads
// Perf.FrameDt) runs the pacer, the timing capture, the dynamic resolution and the cap re-check; a second component at
// +10000 marks the end of the frame's scripted work for the fallback CPU measurement (the render submission's end
// comes from RenderPipelineManager.endContextRendering, subscribed in Perf.Init). Created automatically before the
// first scene loads; nothing to add to a scene. No per-frame allocation. Hidden but NOT DontSave: a DontSave object can
// outlive play mode in the Editor, and with Enter Play Mode's domain reload off the next play would then keep (or skip
// booting over) a stale driver - so play-mode exit destroys it, OnDestroy clears inst, and Perf.ResetRun (SubsystemRegistration)
// clears it too before Boot runs again.
using UnityEngine;

namespace ZU.Engine
{
    [DefaultExecutionOrder(-10000)]
    internal sealed class PerfDriver : MonoBehaviour
    {
        static PerfDriver inst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // play mode without a domain reload: the static survives; a driver destroyed with the last play reads null
            // through Unity's == (its native object is gone) - then it is booted afresh
            if (inst != null && inst.gameObject != null) return;
            inst = null;
            var go = new GameObject("ZU.Engine.PerfDriver") { hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable };
            DontDestroyOnLoad(go);
            inst = go.AddComponent<PerfDriver>();
            go.AddComponent<PerfDriverEnd>();
            Perf.Init();
        }

        /// <summary>a new run (Perf.ResetRun): forget the previous play's driver (destroyed with its play mode)</summary>
        internal static void ResetRun() { inst = null; }

        void Update() => Perf.Frame();

        void OnDestroy() { if (inst == this) inst = null; }
    }

    /// <summary>the end of the frame's scripted work (after every other LateUpdate)</summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class PerfDriverEnd : MonoBehaviour
    {
        void LateUpdate() => Perf.EndScripts();
    }
}
