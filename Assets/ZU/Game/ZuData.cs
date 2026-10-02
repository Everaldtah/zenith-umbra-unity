// Loads the exported game data (Resources/ZUData/*.json) once, for the editor and the player alike.
using UnityEngine;
using ZU.Sim.Data;

namespace ZU.Game
{
    public static class ZuData
    {
        public static GameData Get()
        {
            if (GameData.Current != null) return GameData.Current;
            string T(string n) { var a = Resources.Load<TextAsset>("ZUData/" + n); if (a == null) throw new System.Exception("missing Resources/ZUData/" + n + ".json"); return a.text; }
            return GameData.FromJson(T("heroes"), T("maps"), T("campaign"), T("rules"));
        }
    }
}
