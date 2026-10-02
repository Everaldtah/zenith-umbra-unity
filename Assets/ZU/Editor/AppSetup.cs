// The desktop app "Zenith Umbra Unity": `zu_app_setup` builds the main-menu scene, puts the build scenes in order
// (Menu, then Match) and sets the player up (name, company, version, icon, windowed fullscreen, keeps running when
// unfocused). The player build itself goes through the Pipeline's async `build` command (tools/build_app.sh).
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZU.Game.UI;

namespace ZU.EditorTools
{
    public static class AppSetup
    {
        public const string MenuScene = "Assets/ZU/Scenes/Menu.unity", MatchScene = "Assets/ZU/Scenes/Match.unity";

        [CliCommand("zu_app_setup", "Build the main-menu scene, order the build scenes (Menu, Match) and set the player up for the desktop app")]
        public static string Setup([CliArg("version", "the app version")] string version = "0.1.0")
        {
            if (!File.Exists(MatchScene)) return "no match scene: run zu_match_scene first";
            // the menu scene: a camera with post-processing and the MainMenu
            var active = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>(); cam.AddComponent<AudioListener>();
            cam.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            new GameObject("Menu").AddComponent<MainMenu>();
            Directory.CreateDirectory("Assets/ZU/Scenes");
            EditorSceneManager.SaveScene(scene, MenuScene);
            OrderScenes();
            if (!string.IsNullOrEmpty(active) && active != MenuScene) EditorSceneManager.OpenScene(active);

            PlayerSettings.productName = "Zenith Umbra Unity";
            PlayerSettings.companyName = "Everaldtah";
            PlayerSettings.bundleVersion = version;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ZU/Art/App/icon.png");
            if (icon != null) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            return $"saved {MenuScene}; build scenes: {string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path)))}; " +
                   $"player '{PlayerSettings.productName}' {version} by {PlayerSettings.companyName}, icon {(icon != null ? "set" : "missing")}";
        }

        /// <summary>Menu first (the app starts there), Match second, nothing else</summary>
        public static void OrderScenes()
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            if (File.Exists(MenuScene)) list.Add(new EditorBuildSettingsScene(MenuScene, true));
            if (File.Exists(MatchScene)) list.Add(new EditorBuildSettingsScene(MatchScene, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
