using System.Collections.Generic;
using Kordon.Core.Serialization;
using Kordon.Core.Utility;

namespace Kordon.Game.Maps
{
    /// <summary>One placed kit module in a map layout JSON.</summary>
    public struct MapPlacement
    {
        public string Module;
        public KordonVec3 Position;
        public float YawDegrees;
        public string Tag;
    }

    /// <summary>A map specific shell mesh (terrain, roof structure) loaded by id.</summary>
    public struct MapShell
    {
        public string Id;
        public KordonVec3 Position;
        public float YawDegrees;
        public bool BlocksSight;
        public bool BlocksMovement;
    }

    public struct MapSpawn
    {
        public string Id;
        public string Zone;
        public KordonVec3 Position;
        public float YawDegrees;
        public string Marker;
    }

    public struct MapAudioZone
    {
        public string Id;
        public string Kind;
        public string Reverb;
        public float Height;
        public Bounds2D Region;
    }

    /// <summary>Axis aligned rectangle in map space (metres, Blender X/Y).</summary>
    public struct Bounds2D
    {
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public bool Contains(float x, float y) =>
            x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }

    public struct MapLandmark
    {
        public string Id;
        public string Label;
        public KordonVec3 Position;
        public float Height;
    }

    /// <summary>
    /// The authored layout of one map, exactly as Tools/blender/build_maps.py wrote
    /// it. Nothing here is hand placed in the Unity editor: the layout JSON is the
    /// single source of truth, so the runtime build and the editor bake cannot drift.
    /// </summary>
    public sealed class MapLayout
    {
        public string Id = "";
        public string Name = "";
        public string Scene = "";
        public string Mode = "";
        public string NavGraphId = "";
        public Bounds2D Bounds;
        public readonly List<MapShell> Shells = new List<MapShell>();
        public readonly List<MapPlacement> Placements = new List<MapPlacement>();
        public readonly List<MapSpawn> Spawns = new List<MapSpawn>();
        public readonly List<MapAudioZone> AudioZones = new List<MapAudioZone>();
        public readonly List<MapLandmark> Landmarks = new List<MapLandmark>();
        public int MaxPlayers;
        public int KillTarget;

        public static MapLayout FromJson(string json)
        {
            Json root = Json.Parse(json);
            MapLayout layout = new MapLayout
            {
                Id = root["id"].AsString("map"),
                Name = root["name"].AsString(""),
                Scene = root["scene"].AsString(""),
                Mode = root["mode"].AsString(""),
                NavGraphId = root["navGraphId"].AsString(root["id"].AsString("map")),
            };

            Json bounds = root["bounds"];
            layout.Bounds = new Bounds2D
            {
                MinX = bounds["min"][0].AsFloat(),
                MinY = bounds["min"][1].AsFloat(),
                MaxX = bounds["max"][0].AsFloat(),
                MaxY = bounds["max"][1].AsFloat(),
            };

            foreach (Json item in root["shells"].Items())
            {
                layout.Shells.Add(new MapShell
                {
                    Id = item["id"].AsString(""),
                    Position = ReadVec3(item["position"]),
                    YawDegrees = item["yaw"].AsFloat(0f),
                    BlocksSight = item["blocksSight"].AsBool(true),
                    BlocksMovement = item["blocksMovement"].AsBool(true),
                });
            }

            foreach (Json item in root["placements"].Items())
            {
                layout.Placements.Add(new MapPlacement
                {
                    Module = item["module"].AsString(""),
                    Position = ReadVec3(item["position"]),
                    YawDegrees = item["yaw"].AsFloat(0f),
                    Tag = item["tag"].AsString(""),
                });
            }

            foreach (Json item in root["spawns"].Items())
            {
                layout.Spawns.Add(new MapSpawn
                {
                    Id = item["id"].AsString(""),
                    Zone = item["zone"].AsString(""),
                    Position = ReadVec3(item["position"]),
                    YawDegrees = item["yaw"].AsFloat(0f),
                    Marker = item["marker"].AsString(""),
                });
            }

            foreach (Json item in root["audioZones"].Items())
            {
                Json region = item["region"];
                layout.AudioZones.Add(new MapAudioZone
                {
                    Id = item["id"].AsString(""),
                    Kind = item["kind"].AsString("Outdoor"),
                    Reverb = item["reverb"].AsString("yard"),
                    Height = item["height"].AsFloat(20f),
                    Region = new Bounds2D
                    {
                        MinX = region[0].AsFloat(),
                        MinY = region[1].AsFloat(),
                        MaxX = region[2].AsFloat(),
                        MaxY = region[3].AsFloat(),
                    },
                });
            }

            foreach (Json item in root["landmarks"].Items())
            {
                layout.Landmarks.Add(new MapLandmark
                {
                    Id = item["id"].AsString(""),
                    Label = item["label"].AsString(""),
                    Position = ReadVec3(item["position"]),
                    Height = item["height"].AsFloat(0f),
                });
            }

            Json rules = root["rules"];
            layout.MaxPlayers = rules["maxPlayers"].AsInt(20);
            layout.KillTarget = rules["killTarget"].AsInt(50);
            return layout;
        }

        private static KordonVec3 ReadVec3(Json array)
        {
            return new KordonVec3(array[0].AsFloat(), array[1].AsFloat(), array[2].AsFloat());
        }
    }
}
