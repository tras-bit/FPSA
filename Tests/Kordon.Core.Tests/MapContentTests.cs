using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kordon.Core.Serialization;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The shipped map layouts are load bearing data, not decoration: 1.0.5 shipped a
    /// map whose ground shell was authored without a collider and whose spawns were
    /// therefore in mid air, and nothing in the suite noticed.
    ///
    /// MapBuilder now gives every walkable shell a collider (see MapBuilder.Place), and
    /// these tests pin the data contract that decision depends on: exactly one walkable
    /// ground shell per map, spawns inside the playable bounds and on the ground, and
    /// every placement pointing at a kit module the kit manifest actually exports.
    /// The geometric half of the audit (a 2 m grid of downward ray casts over the
    /// authored Blender scene) lives in Tools/blender/audit_map.py, because it needs
    /// real geometry.
    /// </summary>
    public sealed class MapContentTests
    {
        private static string MapsDirectory => Path.Combine(TestData.DataRoot, "Maps");

        public static IEnumerable<object[]> MapIds() =>
            Directory.GetFiles(MapsDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(id => id)
                .Select(id => new object[] { id });

        private static Json LoadMap(string mapId) =>
            Json.Parse(File.ReadAllText(Path.Combine(MapsDirectory, mapId + ".json")));

        [Fact]
        public void BothShippedMapsArePresent()
        {
            List<string> ids = Directory.GetFiles(MapsDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension).ToList();

            Assert.Contains("ffa_zavod7", ids);
            Assert.Contains("coop_silent_kiln", ids);
        }

        [Theory]
        [MemberData(nameof(MapIds))]
        public void EveryMapHasExactlyOneWalkableGroundShell(string mapId)
        {
            Json map = LoadMap(mapId);
            List<Json> walkable = map["shells"].Items()
                .Where(shell => shell["walkable"].AsBool(false)).ToList();

            Assert.Single(walkable);
            Assert.Equal("ground", walkable[0]["id"].AsString());

            // The ground must not be the only shell that blocks movement: if it were,
            // the map would consist of a floor and nothing to hide behind.
            Assert.True(map["shells"].Items().Count(shell => shell["blocksMovement"].AsBool(true)) >= 1,
                mapId + " has no shell that blocks movement");
        }

        [Theory]
        [MemberData(nameof(MapIds))]
        public void EverySpawnStandsInsideTheBoundsAndOnAFloorHeight(string mapId)
        {
            Json map = LoadMap(mapId);
            float minX = map["bounds"]["min"][0].AsFloat();
            float minY = map["bounds"]["min"][1].AsFloat();
            float maxX = map["bounds"]["max"][0].AsFloat();
            float maxY = map["bounds"]["max"][1].AsFloat();

            Assert.True(maxX - minX > 20f && maxY - minY > 20f, mapId + " bounds are too small");

            List<Json> spawns = map["spawns"].Items().ToList();
            Assert.NotEmpty(spawns);

            foreach (Json spawn in spawns)
            {
                string id = spawn["id"].AsString();
                float x = spawn["position"][0].AsFloat();
                float y = spawn["position"][1].AsFloat();
                float z = spawn["position"][2].AsFloat();

                Assert.True(x >= minX && x <= maxX, id + " is outside the x bounds");
                Assert.True(y >= minY && y <= maxY, id + " is outside the y bounds");
                // Spawn markers are authored on the floor: a marker far above it is the
                // signature of the 1.0.5 bug where bodies dropped out of the world.
                Assert.True(z >= -0.5f && z <= 1.0f, id + " is not at floor height (" + z + ")");
                Assert.False(string.IsNullOrEmpty(spawn["zone"].AsString()), id + " has no zone");
            }
        }

        [Theory]
        [MemberData(nameof(MapIds))]
        public void EveryPlacementUsesAModuleTheKitExports(string mapId)
        {
            Json map = LoadMap(mapId);
            Json kit = Json.Parse(File.ReadAllText(
                Path.Combine(TestData.DataRoot, "Art", "kit_manifest.json")));
            Assert.True(kit["modules"].Count > 0, "kit manifest is empty");

            HashSet<string> exported = new HashSet<string>(kit["modules"].Keys());
            List<Json> placements = map["placements"].Items().ToList();
            Assert.True(placements.Count > 100, mapId + " has too few kit placements");

            foreach (Json placement in placements)
            {
                string module = placement["module"].AsString();
                Assert.True(exported.Contains(module),
                    mapId + " places module '" + module + "', which the kit does not export");
                Assert.False(string.IsNullOrEmpty(placement["tag"].AsString()),
                    mapId + " places " + module + " without a tag");
            }
        }

        [Fact]
        public void FreeForAllMapCarriesTwentySpawnSlotsInDistinctZones()
        {
            Json map = LoadMap("ffa_zavod7");
            List<Json> spawns = map["spawns"].Items().ToList();

            Assert.Equal(20, spawns.Count);
            // Four zones of five slots: spawn selection is round robin over the zones so
            // an enemy cannot farm one corner of the map.
            Assert.Equal(4, spawns.Select(s => s["zone"].AsString()).Distinct().Count());
            Assert.All(spawns.GroupBy(s => s["zone"].AsString()), group => Assert.Equal(5, group.Count()));
            Assert.Equal(50, map["rules"]["killTarget"].AsInt(0));
            Assert.Equal(20, map["rules"]["maxPlayers"].AsInt(0));
            Assert.Equal("FreeForAll", map["mode"].AsString());
        }

        [Fact]
        public void CoopMapIsItsOwnLayoutAndCarriesAFourSlotTeam()
        {
            Json map = LoadMap("coop_silent_kiln");

            Assert.Equal("Cooperative", map["mode"].AsString());
            Assert.True(map["spawns"].Count >= 4, "a co-op operation needs four insertion slots");
            Assert.True(map["placements"].Count > 100, "the co-op map is a finished layout");
        }
    }
}
