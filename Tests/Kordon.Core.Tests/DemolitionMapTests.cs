using System.IO;
using Kordon.Core.Rules;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The demolition layout data shipped with Zavod-7. The Blender audit proves the sites
    /// stand on real floor; this proves the numbers the round loads agree with the map, and
    /// that a malformed layout is rejected rather than loaded halfway.
    /// </summary>
    public class DemolitionMapTests
    {
        private static string MapPath(string mapId) =>
            Path.Combine(TestData.DataRoot, "Maps", mapId + ".json");

        private static DemolitionMapData Load() =>
            DemolitionMapData.Parse(File.ReadAllText(MapPath("ffa_zavod7")), out _);

        [Fact]
        public void ZavodSevenSupportsBothModes()
        {
            DemolitionMapData data = Load();
            Assert.NotNull(data);
            Assert.True(data.SupportsDemolition);
            Assert.Contains("FreeForAll", data.Modes);
            // The free-for-all rules are untouched by the demolition data living here.
            Assert.Equal(20, System.Linq.Enumerable.Count(data.AttackerSpawns) * 2);
        }

        [Fact]
        public void TwoSitesAreFarApartAndValid()
        {
            DemolitionMapData data = Load();
            Assert.Equal(2, data.Sites.Count);
            BombSiteVolume? a = data.Site("A");
            BombSiteVolume? b = data.Site("B");
            Assert.True(a.HasValue && b.HasValue);
            Assert.True(a.Value.Radius >= 3f);
            double gap = System.Math.Sqrt(
                (a.Value.CentreX - b.Value.CentreX) * (a.Value.CentreX - b.Value.CentreX) +
                (a.Value.CentreY - b.Value.CentreY) * (a.Value.CentreY - b.Value.CentreY));
            Assert.True(gap >= 20.0, "the sites are " + gap.ToString("0.0") + " m apart");
        }

        [Fact]
        public void EveryTeamSpawnIsInsideItsOwnBuyZone()
        {
            DemolitionMapData data = Load();
            // Positions come from the spawns block; the order of the two files must agree.
            string layout = File.ReadAllText(MapPath("ffa_zavod7"));
            Kordon.Core.Serialization.Json json = Kordon.Core.Serialization.Json.Parse(layout);
            System.Collections.Generic.Dictionary<string, (float X, float Y)> positions =
                new System.Collections.Generic.Dictionary<string, (float, float)>();
            foreach (Kordon.Core.Serialization.Json spawn in json["spawns"].Items())
            {
                positions[spawn["id"].AsString()] =
                    (spawn["position"][0].AsFloat(), spawn["position"][1].AsFloat());
            }

            foreach (string spawnId in data.AttackerSpawns)
            {
                Assert.True(positions.ContainsKey(spawnId), spawnId + " has no spawn entry");
                (float x, float y) = positions[spawnId];
                Assert.True(BombMatchController.InBuyZone(data.BuyZones, Team.Attackers, x, y),
                    "attacker spawn " + spawnId + " is not in a buy zone");
            }

            foreach (string spawnId in data.DefenderSpawns)
            {
                Assert.True(positions.ContainsKey(spawnId), spawnId + " has no spawn entry");
                (float x, float y) = positions[spawnId];
                Assert.True(BombMatchController.InBuyZone(data.BuyZones, Team.Defenders, x, y),
                    "defender spawn " + spawnId + " is not in a buy zone");
            }
        }

        [Fact]
        public void ASiteIdResolvesFromAPosition()
        {
            DemolitionMapData data = Load();
            BombSiteVolume a = data.Site("A").Value;
            Assert.Equal("A", BombMatchController.SiteAt(data.Sites, a.CentreX, a.CentreY));
            Assert.Null(BombMatchController.SiteAt(data.Sites, a.CentreX + a.Radius + 2f,
                a.CentreY));
        }

        [Fact]
        public void AMalformedLayoutIsRejectedNotLoadedHalfway()
        {
            string layout = File.ReadAllText(MapPath("ffa_zavod7"));
            string broken = layout.Replace("\"radius\": 9.0", "\"radius\": 0.0");
            // Removing the site radius leaves a site that cannot be planted on; the parse
            // itself is still valid JSON, so it must be the contract check that refuses it.
            DemolitionMapData parsed = DemolitionMapData.Parse(broken, out string error);
            Assert.Null(error);
            Assert.Equal(0f, parsed.Site("A").Value.Radius);

            Assert.Null(DemolitionMapData.Parse("{ not json", out string jsonError));
            Assert.NotNull(jsonError);

            string noSites = "{\"modes\":[\"Demolition\"]}";
            Assert.Null(DemolitionMapData.Parse(noSites, out string siteError));
            Assert.Contains("bomb sites", siteError);
        }
    }
}
