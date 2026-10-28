// UnityEditor stub surface for the compile check, same rules as UnityEngineStubs:
// real signatures only, additive over time.

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItem : Attribute
    {
        public MenuItem(string itemName)
        {
        }

        public MenuItem(string itemName, bool isValidateFunction)
        {
        }

        public MenuItem(string itemName, bool isValidateFunction, int priority)
        {
        }
    }

    [Flags]
    public enum StaticEditorFlags
    {
        None = 0,
        ContributeGI = 1,
        OccluderStatic = 2,
        OccludeeStatic = 4,
        BatchingStatic = 8,
        NavigationStatic = 16,
        OffMeshLinkGeneration = 32,
        ReflectionProbeStatic = 64,
    }

    public static class GameObjectUtility
    {
        public static void SetStaticEditorFlags(GameObject target, StaticEditorFlags flags)
        {
        }

        public static StaticEditorFlags GetStaticEditorFlags(GameObject target) =>
            StaticEditorFlags.None;
    }

    public static class EditorApplication
    {
        public static bool isPlaying;
        public static bool isCompiling;
        public static void RepaintHierarchyWindow()
        {
        }
    }

    public static class AssetDatabase
    {
        public static void Refresh()
        {
        }

        public static void SaveAssets()
        {
        }

        public static bool CreateFolder(string parent, string name) => true;
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static string GetAssetPath(UnityEngine.Object asset) => "";
        public static void CreateAsset(UnityEngine.Object asset, string path)
        {
        }
    }

    public static class Selection
    {
        public static GameObject activeGameObject;
    }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target)
        {
        }

        public static bool DisplayDialog(string title, string message, string ok) => true;
    }
}

namespace UnityEditor.SceneManagement
{
    using UnityEngine.SceneManagement;

    public enum NewSceneSetup
    {
        DefaultGameObjects = 0,
        EmptyScene = 1,
    }

    public enum NewSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    public static class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(Scene scene, string destinationPath,
            bool saveAsCopy = false) => true;
        public static void MarkSceneDirty(Scene scene)
        {
        }

        public static bool MarkAllScenesDirty() => true;
        public static Scene GetActiveScene() => default;
        public static int sceneCount => 1;
    }
}
