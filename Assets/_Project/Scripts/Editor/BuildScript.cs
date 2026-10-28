using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kordon.EditorTools
{
    /// <summary>
    /// Player build.  KORDON builds itself from code at runtime, so the only scene
    /// it needs is an empty boot scene - which this script creates, adds to the
    /// build list and builds from.  Nothing here depends on hand authored scene or
    /// prefab YAML, which is also why the project can be opened in a fresh editor
    /// and immediately play.
    /// </summary>
    public static class BuildScript
    {
        private const string SceneFolder = "Assets/_Project/Scenes";
        private const string BootScene = SceneFolder + "/Boot.unity";
        private const string BuildFolder = "Builds/Windows";

        [MenuItem("Kordon/Build/Windows 64-bit player")]
        public static void BuildWindows()
        {
            string report = BuildPlayer(BuildTarget.StandaloneWindows64, BuildFolder);
            Debug.Log("[kordon] " + report);
        }

        [MenuItem("Kordon/Build/Linux 64-bit player (dedicated server)")]
        public static void BuildLinux()
        {
            string report = BuildPlayer(BuildTarget.StandaloneLinux64, "Builds/LinuxServer");
            Debug.Log("[kordon] " + report);
        }

        [MenuItem("Kordon/Open boot scene (press Play)")]
        public static void OpenBootScene()
        {
            EnsureBootScene();
            EditorSceneManager.OpenScene(BootScene);
        }

        /// <summary>Creates the empty boot scene if it does not exist and returns its path.</summary>
        public static string EnsureBootScene()
        {
            Directory.CreateDirectory(SceneFolder);
            if (!File.Exists(BootScene))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, BootScene);
                AssetDatabase.Refresh();
            }

            return BootScene;
        }

        private static string BuildPlayer(BuildTarget target, string outputFolder)
        {
            string scene = EnsureBootScene();
            Directory.CreateDirectory(outputFolder);

            PlayerSettings.productName = "KORDON";
            PlayerSettings.companyName = "Tras Bit";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,
                "com.trasbit.kordon");
            PlayerSettings.colorSpace = ColorSpace.Linear;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outputFolder,
                    target == BuildTarget.StandaloneWindows64
                        ? "Kordon.exe"
                        : "KordonServer.x86_64"),
                target = target,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            return string.Format("{0} build: {1} ({2} errors, {3} bytes)",
                target, summary.result, summary.totalErrors, summary.totalSize);
        }

        /// <summary>Dedicated server entry: run with -kordon-server to skip the menu.</summary>
        [MenuItem("Kordon/Run dedicated server here")]
        public static void StartDedicatedServer()
        {
            string[] args = Environment.GetCommandLineArgs();
            Debug.Log("[kordon] start with: Kordon.exe -kordon-server -serverAddress <bind> -serverPort 27015");
            args.ToList().ForEach(argument => Debug.Log("  arg: " + argument));
        }
    }
}
