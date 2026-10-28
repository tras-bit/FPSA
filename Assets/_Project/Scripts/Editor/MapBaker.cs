using System.IO;
using Kordon.Game.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kordon.EditorTools
{
    /// <summary>
    /// Turns an authored layout JSON into a saved Unity scene.  It runs the very
    /// same MapBuilder the runtime uses, so a baked scene and a runtime built map
    /// cannot disagree: modules, colliders, materials and spawns all come from the
    /// layout plus the kit manifest, never from hand editing.
    /// </summary>
    public static class MapBaker
    {
        private const string SceneDirectory = "Assets/_Project/Scenes";

        [MenuItem("Kordon/Bake map/ffa_zavod7 (Zavod-7)")]
        public static void BakeZavod7() => Bake("ffa_zavod7");

        [MenuItem("Kordon/Bake map/coop_silent_kiln (Operation Silent Kiln)")]
        public static void BakeSilentKiln() => Bake("coop_silent_kiln");

        [MenuItem("Kordon/Bake map/Every map in StreamingAssets")]
        public static void BakeAll()
        {
            Bake("ffa_zavod7");
            Bake("coop_silent_kiln");
        }

        /// <summary>Builds mapId into a fresh empty scene and saves it. Returns the asset path.</summary>
        public static string Bake(string mapId)
        {
            MapLayout layout = MapLayout.FromJson(ContentPaths.Read(ContentPaths.MapLayout(mapId)));
            KitManifestData kit = KitManifestData.Load();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuiltMap built = MapBuilder.Build(layout, kit, null);
            MarkStatic(built.transform);
            EditorSceneManager.MarkSceneDirty(scene);

            Directory.CreateDirectory(SceneDirectory);
            string path = SceneDirectory + "/" + mapId + ".unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[kordon] baked " + path + ": " + layout.Placements.Count +
                      " placements, " + layout.Shells.Count + " shells, " +
                      layout.Spawns.Count + " spawns");
            return path;
        }

        private static void MarkStatic(Transform root)
        {
            foreach (Transform child in root)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    child.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                MarkStatic(child);
            }
        }
    }
}
