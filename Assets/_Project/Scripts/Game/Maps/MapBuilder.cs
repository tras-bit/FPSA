using System.Collections.Generic;
using System.IO;
using Kordon.Core.Nav;
using Kordon.Core.Utility;
using UnityEngine;

namespace Kordon.Game.Maps
{
    /// <summary>
    /// Where authored content lives, and how Blender map space maps into Unity.
    ///
    /// The kit is exported with the Blender FBX settings axis_forward = -Z and
    /// axis_up = Y, which puts a Blender point (x, y, z) at Unity (x, z, y); a
    /// Blender yaw of +90 degrees (facing north, +Y) faces Unity +Z, so the Unity
    /// yaw is the negated Blender yaw about Y.  One place, one convention.
    /// </summary>
    public static class MapSpace
    {
        public static Vector3 ToUnity(KordonVec3 blender) =>
            new Vector3(blender.X, blender.Z, blender.Y);

        public static Quaternion ToUnityYaw(float yawDegrees) =>
            Quaternion.Euler(0f, -yawDegrees, 0f);

        /// <summary>Unity world direction/velocity back into authored map space.</summary>
        public static KordonVec3 ToCore(Vector3 unity) => new KordonVec3(unity.x, unity.z, unity.y);

        /// <summary>Horizontal heading in authored degrees (north = +Y = Unity +Z).</summary>
        public static float YawDegrees(Vector3 unityDirection) =>
            -Mathf.Atan2(unityDirection.x, unityDirection.z) * Mathf.Rad2Deg;
    }

    /// <summary>StreamingAssets layout, independent of the editor or a build target.</summary>
    public static class ContentPaths
    {
        public static string StreamingAssets => Application.streamingAssetsPath;

        public static string MapLayout(string mapId) =>
            Path.Combine(StreamingAssets, "Data", "Maps", mapId + ".json");

        public static string NavGraph(string mapId) =>
            Path.Combine(StreamingAssets, "Data", "Nav", mapId + ".json");

        public static string KitManifest =>
            Path.Combine(StreamingAssets, "Data", "Art", "kit_manifest.json");

        public static string Definitions => Path.Combine(StreamingAssets, "Data", "Definitions");

        /// <summary>Root expected by Kordon.Core.GameDatabase.Load (holds Definitions/Nav/Config).</summary>
        public static string DataRoot => Path.Combine(StreamingAssets, "Data");

        public static string Read(string path)
        {
            // StreamingAssets is a plain directory on every desktop target, which is
            // all KORDON ships for (Windows first); no UnityWebRequest needed.
            return File.ReadAllText(path);
        }
    }

    /// <summary>
    /// Resolves the authored surface name of a mesh slot to its generated material.
    /// Blender assigns one material per surface kind, the FBX carries those names,
    /// and this is the single lookup that turns them into Unity materials.
    /// </summary>
    public static class SurfaceMaterialLibrary
    {
        private const string ResourceRoot = "Kordon/Materials/M_";
        private static readonly Dictionary<string, Material> Cache =
            new Dictionary<string, Material>();

        public static Material ForSurface(string surface)
        {
            if (string.IsNullOrEmpty(surface))
            {
                return null;
            }

            if (!Cache.TryGetValue(surface, out Material material))
            {
                material = Resources.Load<Material>(ResourceRoot + surface);
                Cache[surface] = material;
                if (material == null)
                {
                    Debug.LogWarning("[kordon] no material for surface '" + surface + "'");
                }
            }

            return material;
        }

        /// <summary>Replace every imported slot material with the authored one.</summary>
        public static void Apply(GameObject instance)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null)
                    {
                        continue;
                    }

                    string surface = slots[i].name;
                    // Unity suffixes instantiated material names; imported ones are bare.
                    int space = surface.IndexOf(' ');
                    if (space > 0)
                    {
                        surface = surface.Substring(0, space);
                    }

                    Material authored = ForSurface(surface);
                    if (authored != null && authored != slots[i])
                    {
                        slots[i] = authored;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = slots;
                }
            }
        }
    }

    /// <summary>Everything MapBuilder produced for one map, for the match to query.</summary>
    public sealed class BuiltMap : MonoBehaviour
    {
        public MapLayout Layout;
        public NavGraph Nav;
        public KitManifestData Kit;
        public readonly List<Transform> SpawnPoints = new List<Transform>();
        public readonly List<GameObject> Destructibles = new List<GameObject>();
        public readonly Dictionary<string, List<GameObject>> ByTag =
            new Dictionary<string, List<GameObject>>();
    }

    /// <summary>
    /// Assembles a map at runtime from its layout JSON: shells and kit modules by
    /// Resources.Load (never hand authored prefabs), colliders from the kit
    /// manifest's movement flags, materials from the surface contract, spawns and
    /// the validated navigation graph.  The editor bake tool runs this same code,
    /// so what plays is what the baked scene contains.
    /// </summary>
    public static class MapBuilder
    {
        public const string KitResourceRoot = "Kordon/Kit/";
        public const string ShellResourceRoot = "Kordon/Maps/";

        public static BuiltMap Build(MapLayout layout, KitManifestData kit, Transform parent)
        {
            GameObject root = new GameObject("Map_" + layout.Id);
            root.transform.SetParent(parent, false);
            BuiltMap built = root.AddComponent<BuiltMap>();
            built.Layout = layout;
            built.Kit = kit;

            foreach (MapShell shell in layout.Shells)
            {
                GameObject prefab = Resources.Load<GameObject>(
                    ShellResourceRoot + layout.Id + "/" + shell.Id);
                if (prefab == null)
                {
                    Debug.LogError("[kordon] missing shell " + shell.Id + " for " + layout.Id);
                    continue;
                }

                Place(prefab, shell.Position, shell.YawDegrees, root.transform, shell.Id,
                      shell.BlocksMovement, built, "shell");
            }

            int index = 0;
            foreach (MapPlacement placement in layout.Placements)
            {
                GameObject prefab = Resources.Load<GameObject>(KitResourceRoot + placement.Module);
                if (prefab == null)
                {
                    Debug.LogError("[kordon] missing kit module " + placement.Module);
                    continue;
                }

                bool blocks = kit.BlocksMovement(placement.Module);
                GameObject instance = Place(prefab, placement.Position, placement.YawDegrees,
                                            root.transform, placement.Module + "_" + index,
                                            blocks, built, placement.Tag);
                index++;

                if (kit.IsDestructible(placement.Module))
                {
                    built.Destructibles.Add(instance);
                }
            }

            foreach (MapSpawn spawn in layout.Spawns)
            {
                GameObject point = new GameObject("Spawn_" + spawn.Id);
                point.transform.SetParent(root.transform, false);
                point.transform.position = MapSpace.ToUnity(spawn.Position);
                point.transform.rotation = MapSpace.ToUnityYaw(spawn.YawDegrees);
                point.tag = "Untagged";
                built.SpawnPoints.Add(point.transform);
            }

            string navJson = ContentPaths.Read(ContentPaths.NavGraph(layout.NavGraphId));
            NavGraph nav = NavGraph.FromJson(navJson);
            List<string> problems = nav.Validate();
            if (problems.Count > 0)
            {
                Debug.LogError("[kordon] nav graph " + layout.NavGraphId + " invalid: " +
                               string.Join("; ", problems));
            }

            built.Nav = nav;
            return built;
        }

        private static GameObject Place(GameObject prefab, KordonVec3 position, float yaw,
                                        Transform parent, string name, bool blocksMovement,
                                        BuiltMap built, string tag)
        {
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.position = MapSpace.ToUnity(position);
            instance.transform.rotation = MapSpace.ToUnityYaw(yaw);
            SurfaceMaterialLibrary.Apply(instance);

            if (blocksMovement && instance.GetComponent<Collider>() == null)
            {
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }

                    MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                }
            }

            if (!string.IsNullOrEmpty(tag))
            {
                if (!built.ByTag.TryGetValue(tag, out List<GameObject> list))
                {
                    list = new List<GameObject>();
                    built.ByTag[tag] = list;
                }

                list.Add(instance);
            }

            return instance;
        }
    }
}
