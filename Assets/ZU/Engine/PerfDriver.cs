// The engine's frame tick (the Unity side of zenith-umbra src/engine/EngineCore.ts's frame() / endRender(), engine
// core). The web Game called the engine at the top of its rAF loop and after its last draw; Unity owns the loop, so a
// hidden, scene-independent MonoBehaviour does it: Update at execution order -10000 (before any script reads
// Perf.FrameDt) runs the pacer, the timing capture, the dynamic resolution and the cap re-check; a second component at
// +10000 marks the end of the frame's scripted work for the fallback CPU measurement (the render submission's end
// comes from RenderPipelineManager.endContextRendering, subscribed in Perf.Init). Created automatically before the
// first scene loads; nothing to add to a scene. No per-frame allocation.
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
            if (inst != null) return;                      // (play mode without domain reload: the statics survive)
            var go = new GameObject("ZU.Engine.PerfDriver") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            inst = go.AddComponent<PerfDriver>();
            go.AddComponent<PerfDriverEnd>();
            Perf.Init();
        }

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
