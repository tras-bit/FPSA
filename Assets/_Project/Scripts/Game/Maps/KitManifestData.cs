using System.Collections.Generic;
using Kordon.Core.Serialization;

namespace Kordon.Game.Maps
{
    /// <summary>Runtime view of one kit module's authored metadata.</summary>
    public sealed class KitModuleData
    {
        public string Id = "";
        public bool BlocksMovement = true;
        public bool BlocksSight = true;
        public bool Walkable;
        public bool Climbable;
        public bool Destructible;
        public float Integrity = 100f;
        public float CoverHeight;
        public float WalkHeight;
        public string SurfaceKind = "Concrete";
        public string DominantSurface = "concrete_raw";
    }

    /// <summary>
    /// The kit manifest is the contract the Blender side wrote when it exported the
    /// modules: sizes, cover heights, sight and movement flags, destruction budgets
    /// and surface kinds.  The runtime never guesses any of it.
    /// </summary>
    public sealed class KitManifestData
    {
        private readonly Dictionary<string, KitModuleData> _modules =
            new Dictionary<string, KitModuleData>();

        public int ModuleCount => _modules.Count;

        public static KitManifestData FromJson(string json)
        {
            Json root = Json.Parse(json);
            KitManifestData data = new KitManifestData();
            Json modules = root["modules"];
            foreach (string key in modules.Keys())
            {
                Json item = modules[key];
                data._modules[key] = new KitModuleData
                {
                    Id = key,
                    BlocksMovement = item["blocksMovement"].AsBool(true),
                    BlocksSight = item["blocksSight"].AsBool(true),
                    Walkable = item["walkable"].AsBool(false),
                    Climbable = item["climbable"].AsBool(false),
                    Destructible = item["destructible"].AsBool(false),
                    Integrity = item["integrity"].AsFloat(100f),
                    CoverHeight = item["coverHeight"].AsFloat(0f),
                    WalkHeight = item["walkHeight"].AsFloat(0f),
                    SurfaceKind = item["surfaceKind"].AsString("Concrete"),
                    DominantSurface = item["dominantSurface"].AsString("concrete_raw"),
                };
            }

            return data;
        }

        public static KitManifestData Load() =>
            FromJson(ContentPaths.Read(ContentPaths.KitManifest));

        public bool TryGet(string moduleId, out KitModuleData module) =>
            _modules.TryGetValue(moduleId, out module);

        public bool BlocksMovement(string moduleId) =>
            TryGet(moduleId, out KitModuleData module) && module.BlocksMovement;

        public bool IsDestructible(string moduleId) =>
            TryGet(moduleId, out KitModuleData module) && module.Destructible;

        public float CoverHeight(string moduleId) =>
            TryGet(moduleId, out KitModuleData module) ? module.CoverHeight : 0f;
    }
}
