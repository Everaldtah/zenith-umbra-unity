// TEMPORARY (evera-6e): stands in for evera-a0's second sha (PlayClip.Disk.cs: Save / Load / LoadHeader, and KillCam.Watch)
// so the highlights library and its UI compile. DELETE this file when that sha is merged.
namespace ZU.Game.Fx
{
    public sealed partial class PlayClip
    {
        public void Save(string file) { }
        public static PlayClip Load(string file) => null;
        public static PlayClip LoadHeader(string file) => null;
    }
    public sealed partial class KillCam
    {
        public static void Watch(PlayClip c, System.Action done) => done?.Invoke();
    }
}
