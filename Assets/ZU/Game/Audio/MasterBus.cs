// The master output chain on the listener (MasterChain: glue compressor, true-peak limiter, safety clamp, output meter).
// A script with OnAudioFilterRead on the AudioListener's GameObject processes the whole mix on its way to the device, so
// AudioKit puts one of these on whichever listener is live (menu camera, match camera, editor captures) - nothing in a
// fight can clip the output again. Optional raw capture of the processed output for offline listening and the audio QA
// (AudioCapture writes it to a WAV).
using UnityEngine;

namespace ZU.Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class MasterBus : MonoBehaviour
    {
        public readonly MasterChain Chain = new MasterChain();
        /// <summary>the bus on the live listener (null until AudioKit finds one)</summary>
        public static MasterBus Live { get; private set; }
        /// <summary>when set, every processed block is copied here (audio thread) - AudioCapture</summary>
        internal static System.Action<float[], int> Tap;
        int rate;

        void OnEnable()
        {
            rate = AudioSettings.outputSampleRate;
            Chain.Prepare(rate);
            Live = this;
            AudioSettings.OnAudioConfigurationChanged += Reconfigured;
        }
        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= Reconfigured;
            if (Live == this) Live = null;
        }
        void Reconfigured(bool deviceWasChanged) { rate = AudioSettings.outputSampleRate; Chain.Prepare(rate); }

        void OnAudioFilterRead(float[] data, int channels)
        {
            Chain.Process(data, channels);
            Tap?.Invoke(data, channels);
        }

        /// <summary>the bus on this listener, added if missing</summary>
        public static MasterBus On(AudioListener l)
        {
            if (l == null) return null;
            var b = l.GetComponent<MasterBus>();
            if (b == null) b = l.gameObject.AddComponent<MasterBus>();
            Live = b;
            return b;
        }
    }
}
