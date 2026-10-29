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
        private const string ResourceRoot = "Kordon/Materials/" + SurfaceNaming.MaterialPrefix;
        private static readonly Dictionary<string, Material> Cache =
            new Dictionary<string, Material>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

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
                if (material == null && Warned.Add(surface))
                {
                    Debug.LogWarning("[kordon] no material for surface '" + surface + "'");
                }
            }

            return material;
        }

        /// <summary>Imported material name ("KRD_steel_rust") to surface id ("steel_rust").</summary>
        public static string SurfaceFromImportedName(string materialName) =>
            SurfaceNaming.FromImportedMaterial(materialName);

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

                    string surface = SurfaceFromImportedName(slots[i].name);
                    if (string.IsNullOrEmpty(surface))
                    {
                        continue;
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
        /// <summary>The map the local player is in, so the fall guard can find it.</summary>
        public static BuiltMap Active { get; internal set; }

        public MapLayout Layout;
        public NavGraph Nav;
        public KitManifestData Kit;
        public readonly List<Transform> SpawnPoints = new List<Transform>();
        public readonly List<GameObject> Destructibles = new List<GameObject>();
        public readonly Dictionary<string, List<GameObject>> ByTag =
            new Dictionary<string, List<GameObject>>();

        /// <summary>Y (Unity up) below which a player has left the map and is respawned.</summary>
        public float KillFloorY = -25f;

        /// <summary>Map extent in Unity space, for the boundary walls and spawn checks.</summary>
        public Bounds WorldBounds;

        public int ShellsPlaced;
        public int ShellsMissing;
        public int ModulesPlaced;
        public int ModulesMissing;
        public int CollidersAdded;

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>World space point of a spawn index, clamped to the built map.</summary>
        public Vector3 SpawnPosition(int index)
        {
            if (SpawnPoints.Count == 0)
            {
                return new Vector3(0f, 2f, 0f);
            }

            return SpawnPoints[Mathf.Abs(index) % SpawnPoints.Count].position;
        }
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
                    built.ShellsMissing++;
                    Debug.LogError("[kordon] missing shell " + shell.Id + " for " + layout.Id);
                    continue;
                }

                bool solid = shell.BlocksMovement || shell.Walkable;
                Place(prefab, shell.Position, shell.YawDegrees, root.transform, shell.Id,
                      solid, built, "shell", shell.Walkable);
                built.ShellsPlaced++;
            }

            int index = 0;
            foreach (MapPlacement placement in layout.Placements)
            {
                GameObject prefab = Resources.Load<GameObject>(KitResourceRoot + placement.Module);
                if (prefab == null)
                {
                    built.ModulesMissing++;
                    Debug.LogError("[kordon] missing kit module " + placement.Module);
                    continue;
                }

                bool blocks = kit.BlocksMovement(placement.Module);
                GameObject instance = Place(prefab, placement.Position, placement.YawDegrees,
                                            root.transform, placement.Module + "_" + index,
                                            blocks, built, placement.Tag, false);
                built.ModulesPlaced++;
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
            AddWorldSafety(layout, built, root.transform);
            BuiltMap.Active = built;
            Debug.Log(string.Format(
                "[kordon] map {0} built: {1}/{2} shells, {3}/{4} modules, {5} colliders, " +
                "{6} spawn points, kill floor y={7:0.0}",
                layout.Id, built.ShellsPlaced, built.ShellsPlaced + built.ShellsMissing,
                built.ModulesPlaced, built.ModulesPlaced + built.ModulesMissing,
                built.CollidersAdded, built.SpawnPoints.Count, built.KillFloorY));
            return built;
        }

        /// <summary>
        /// Keeps players inside the map: four invisible walls around the layout bounds
        /// plus a recorded kill floor.  Before this, a player who walked off the apron
        /// fell forever, because the terrain is the only floor and the map has open
        /// edges away from the fences.
        /// </summary>
        private static void AddWorldSafety(MapLayout layout, BuiltMap built, Transform parent)
        {
            Bounds2D bounds = layout.Bounds;
            const float wallHeight = 40f;
            const float thickness = 2f;
            const float margin = 2f;

            float minX = bounds.MinX - margin;
            float maxX = bounds.MaxX + margin;
            float minY = bounds.MinY - margin;
            float maxY = bounds.MaxY + margin;
            float middleZ = wallHeight * 0.5f;

            GameObject walls = new GameObject("WorldBoundary");
            walls.transform.SetParent(parent, false);
            int index = 0;
            AddWall(walls.transform, "North", new Vector3((minX + maxX) * 0.5f, middleZ, maxY),
                    new Vector3(maxX - minX + thickness * 2f, wallHeight, thickness), ref index);
            AddWall(walls.transform, "South", new Vector3((minX + maxX) * 0.5f, middleZ, minY),
                    new Vector3(maxX - minX + thickness * 2f, wallHeight, thickness), ref index);
            AddWall(walls.transform, "East", new Vector3(maxX, middleZ, (minY + maxY) * 0.5f),
                    new Vector3(thickness, wallHeight, maxY - minY + thickness * 2f), ref index);
            AddWall(walls.transform, "West", new Vector3(minX, middleZ, (minY + maxY) * 0.5f),
                    new Vector3(thickness, wallHeight, maxY - minY + thickness * 2f), ref index);

            built.CollidersAdded += index;
            built.WorldBounds = new Bounds(
                new Vector3((minX + maxX) * 0.5f, 6f, (minY + maxY) * 0.5f),
                new Vector3(maxX - minX, wallHeight, maxY - minY));
            built.KillFloorY = -8f;
        }

        private static void AddWall(Transform parent, string name, Vector3 center, Vector3 size,
                                    ref int index)
        {
            GameObject wall = new GameObject("Wall_" + name);
            wall.transform.SetParent(parent, false);
            wall.transform.position = center;
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = size;
            index++;
        }

        private static GameObject Place(GameObject prefab, KordonVec3 position, float yaw,
                                        Transform parent, string name, bool blocksMovement,
                                        BuiltMap built, string tag, bool walkable)
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
                    built.CollidersAdded++;
                }
            }

            // A walkable shell with no collider is the bug that let players fall out
            // of the world: say so loudly instead of shipping an invisible hole.
            if (walkable && instance.GetComponentInChildren<Collider>(true) == null)
            {
                Debug.LogError("[kordon] walkable shell '" + name + "' has no collider - " +
                               "the player would fall through it");
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
