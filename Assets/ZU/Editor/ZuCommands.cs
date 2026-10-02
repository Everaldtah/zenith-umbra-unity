// ZENITH//UMBRA's own editor commands for the Unity CLI / MCP (`unity command zu_*`, `unity list`): the project-specific
// tools an agent needs on top of the Pipeline package's generic ones.
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZU.Game;
using ZU.Sim.Data;

namespace ZU.EditorTools
{
    public static class ZuCommands
    {
        const string MatchScene = "Assets/ZU/Scenes/Match.unity";

        [CliCommand("zu_status", "ZENITH//UMBRA: game data, playable maps, heroes and the active scene")]
        public static string Status()
        {
            GameData.Current = null;
            var d = ZuData.Get();
            var maps = d.Maps.Where(m => !m.retired && m.id != "training").Select(m => m.id);
            return $"heroes {d.Heroes.Count} ({string.Join(",", d.Heroes.Select(h => h.id))}); maps {string.Join(",", maps)}; " +
                   $"scene {EditorSceneManager.GetActiveScene().path}; playing {EditorApplication.isPlaying}";
        }

        [CliCommand("zu_match_scene", "Create (or rebuild) the match scene for a map, a hero and a mode, save it, and make it the first build scene")]
        public static string MatchSceneCmd(
            [CliArg("map", "map id (hanabi, cloudstep, kagura, lantern, starfall, foundry, mile, gulch, training)")] string map = "hanabi",
            [CliArg("hero", "the local player's hero id; empty = spectate the bots")] string hero = "raijin",
            [CliArg("mode", "quickplay | competitive | skirmish | practice | aitest | training | spectate")] string mode = "quickplay",
            [CliArg("third", "third-person camera")] bool third = false,
            [CliArg("autopilot", "a bot drives the player's hero")] bool autopilot = false)
        {
            var d = ZuData.Get();
            if (!d.Map.ContainsKey(map)) return "unknown map " + map;
            if (!string.IsNullOrEmpty(hero) && d.Def(hero) == null) return "unknown hero " + hero;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>(); cam.AddComponent<AudioListener>();
            cam.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var go = new GameObject("Match");
            var r = go.AddComponent<MatchRunner>();
            r.mapId = map; r.playerHero = hero ?? ""; r.mode = mode; r.thirdPerson = third; r.autopilot = autopilot;
            System.IO.Directory.CreateDirectory("Assets/ZU/Scenes");
            EditorSceneManager.SaveScene(scene, MatchScene);
            var list = EditorBuildSettings.scenes.Where(s => s.path != MatchScene).ToList();
            list.Insert(0, new EditorBuildSettingsScene(MatchScene, true));
            EditorBuildSettings.scenes = list.ToArray();
            return $"saved {MatchScene}: map {map}, hero {(string.IsNullOrEmpty(hero) ? "(spectate)" : hero)}, mode {mode}, {(third ? "third" : "first")} person{(autopilot ? ", autopilot" : "")}";
        }
    }
}
