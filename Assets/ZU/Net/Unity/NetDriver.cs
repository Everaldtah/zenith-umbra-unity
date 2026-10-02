// The Unity side of the netcode: PlayerPrefs behind NetConfig (the node URL, the last connection test), WebRTC behind
// PeerLink (RtcPeer), and one DontDestroyOnLoad object that ticks the online / co-op sessions every frame and runs the
// WebRTC callbacks on the main thread. The front end uses NetDriver.Session / NetDriver.Coop; the match runner drives
// ZU.Net.NetMatch.Current.
using System;
using System.Collections.Concurrent;
using System.Linq;
using UnityEngine;
using ZU.Sim.Data;

namespace ZU.Net.Unity
{
    public sealed class NetDriver : MonoBehaviour
    {
        static NetDriver inst;
        static OnlineSession session;
        static CoopSession coop;
        readonly ConcurrentQueue<Action> posted = new ConcurrentQueue<Action>();
        static int mainThread = -1;

        /// <summary>the name other players see (the front end sets it from the player's profile)</summary>
        public static string PlayerName = "Vanguard";
        /// <summary>the profile card / level / best rank sent to the node (the front end sets it)</summary>
        public static Func<ProfileInfo> Profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            NetConfig.Load = k => PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : null;
            NetConfig.Save = (k, v) => { PlayerPrefs.SetString(k, v ?? ""); PlayerPrefs.Save(); };
            PeerLink.RtcFactory = ice => new RtcPeer(Instance, ice);
        }

        public static NetDriver Instance
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("ZU.NetDriver");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<NetDriver>();
                }
                return inst;
            }
        }

        /// <summary>the online PvP session (created on first use, connected to NetConfig.NodeUrl)</summary>
        public static OnlineSession Session
        {
            get
            {
                if (session == null)
                {
                    _ = Instance;
                    var maps = GameData.Current.Maps.Select(m => m.id).ToList();      // (the TS MAPS order: the node's seed picks the same map in every build)
                    session = new OnlineSession(PlayerName, maps, () => Profile?.Invoke() ?? new ProfileInfo());
                }
                return session;
            }
        }
        /// <summary>the campaign co-op session (created on first use)</summary>
        public static CoopSession Coop
        {
            get
            {
                if (coop == null) { _ = Instance; coop = new CoopSession(PlayerName, "desktop"); }
                return coop;
            }
        }
        /// <summary>drop the sessions (a node URL or name change takes effect with the next one)</summary>
        public static void CloseSessions()
        {
            session?.Close(); session = null;
            coop?.Close(); coop = null;
        }

        /// <summary>run on the main thread: at once when already on it (com.unity.webrtc delivers its callbacks there, once a
        /// frame - waiting for the next Update would add a frame to every message and inflate the measured RTT), else in
        /// the next Update</summary>
        public void Post(Action a)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainThread) { try { a(); } catch (Exception e) { Debug.LogException(e); } }
            else posted.Enqueue(a);
        }

        void Update()
        {
            while (posted.TryDequeue(out var a)) { try { a(); } catch (Exception e) { Debug.LogException(e); } }
            try { session?.Tick(); } catch (Exception e) { Debug.LogException(e); }
            try { coop?.Tick(); } catch (Exception e) { Debug.LogException(e); }
        }

        void OnApplicationQuit() => CloseSessions();
    }
}
