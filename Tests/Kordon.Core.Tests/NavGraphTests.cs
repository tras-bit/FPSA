using System.Collections.Generic;
using Kordon.Core.Nav;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The navigation graph is hand authored (no engine baking), so its structure,
    /// traversal costs and connectivity are verified here and again in CI for every
    /// graph that ships in Assets/StreamingAssets/Data/Nav.
    /// </summary>
    public class NavGraphTests
    {
        private static NavNode Node(int id, float x, float z, NavArea area = NavArea.Open, string marker = "") =>
            new NavNode
            {
                Id = id,
                Position = new KordonVec3(x, 0f, z),
                Area = area,
                Marker = marker
            };

        /// <summary>Yard -&gt; hall -&gt; roof, plus a service tunnel branch.</summary>
        private static NavGraph BuildMap()
        {
            NavGraph graph = new NavGraph { Id = "test_map" };
            graph.AddNode(Node(1, 0f, 0f, NavArea.Spawn, "spawn_south"));
            graph.AddNode(Node(2, 10f, 0f, NavArea.Exterior));
            graph.AddNode(Node(3, 20f, 0f, NavArea.Indoor, "kiln_hall"));
            graph.AddNode(Node(4, 10f, 10f, NavArea.Catwalk, "roof_access"));
            graph.AddNode(Node(5, 30f, 0f, NavArea.Extraction, "extraction_pad"));
            graph.AddNode(Node(6, 20f, -10f, NavArea.Tunnel));

            graph.AddLink(new NavLink { From = 1, To = 2 });
            graph.AddLink(new NavLink { From = 2, To = 3 });
            graph.AddLink(new NavLink { From = 3, To = 5 });
            graph.AddLink(new NavLink { From = 2, To = 4 });
            graph.AddLink(new NavLink { From = 3, To = 6 });
            graph.AddLink(new NavLink { From = 6, To = 5 });
            return graph;
        }

        [Fact]
        public void ConnectedGraph_ValidatesCleanly()
        {
            NavGraph graph = BuildMap();
            Assert.Equal(6, graph.Count);
            Assert.Equal(6, graph.Links.Count);
            Assert.Empty(graph.Validate());
        }

        [Fact]
        public void StructureProblems_AreReported()
        {
            NavGraph empty = new NavGraph { Id = "empty" };
            Assert.Contains("empty: graph has no nodes", empty.Validate());

            NavGraph dangling = BuildMap();
            dangling.AddLink(new NavLink { From = 3, To = 99 });
            Assert.Contains("test_map: link references unknown node 99", dangling.Validate());

            NavGraph negative = BuildMap();
            negative.AddLink(new NavLink { From = 1, To = 2, Cost = -5f });
            Assert.Contains("test_map: negative link cost 1->2", negative.Validate());

            NavGraph island = BuildMap();
            island.AddNode(Node(50, 200f, 200f, NavArea.Open));
            List<string> problems = island.Validate();
            Assert.Contains("test_map: node 50 is unreachable from node 1", problems);
        }

        [Fact]
        public void ShortestPath_IsFoundAndIncludesTheStartNode()
        {
            NavGraph graph = BuildMap();
            List<int> path = graph.FindPath(1, 5);
            Assert.Equal(new List<int> { 1, 2, 3, 5 }, path);
            Assert.Equal(30f, graph.PathLength(path), 3);

            Assert.Equal(new List<int> { 1 }, graph.FindPath(1, 1));
            Assert.Empty(graph.FindPath(1, 1234));
            Assert.Empty(graph.FindPath(1234, 1));
            Assert.Equal(0f, graph.PathLength(null), 3);
            Assert.Equal(0f, graph.PathLength(new List<int> { 1 }), 3);
        }

        [Fact]
        public void JumpsAndCrawls_CostExtraSoTheAiPrefersEasyRoutes()
        {
            NavGraph graph = new NavGraph { Id = "vault" };
            graph.AddNode(Node(1, 0f, 0f));
            graph.AddNode(Node(2, 6f, 0f));
            graph.AddNode(Node(3, 3f, 1f));
            graph.AddLink(new NavLink { From = 1, To = 2, RequiresJump = true, JumpHeightM = 1f });
            graph.AddLink(new NavLink { From = 1, To = 3 });
            graph.AddLink(new NavLink { From = 3, To = 2 });
            Assert.Empty(graph.Validate());

            // Direct route is 6 m but needs a vault: 6 + 1.4 + 0.5 = 7.9.
            // The detour is 3.162 + 3.162 = 6.32, so it wins.
            Assert.Equal(new List<int> { 1, 3, 2 }, graph.FindPath(1, 2));

            // Without the jump requirement the direct route is shorter.
            NavGraph flat = new NavGraph { Id = "flat" };
            flat.AddNode(Node(1, 0f, 0f));
            flat.AddNode(Node(2, 6f, 0f));
            flat.AddNode(Node(3, 3f, 1f));
            flat.AddLink(new NavLink { From = 1, To = 2 });
            flat.AddLink(new NavLink { From = 1, To = 3 });
            flat.AddLink(new NavLink { From = 3, To = 2 });
            Assert.Equal(new List<int> { 1, 2 }, flat.FindPath(1, 2));

            // Crawling costs less than vaulting but still more than walking.
            NavGraph crawl = new NavGraph { Id = "crawl" };
            crawl.AddNode(Node(1, 0f, 0f));
            crawl.AddNode(Node(2, 6f, 0f));
            crawl.AddNode(Node(3, 3f, 1.5f));
            crawl.AddLink(new NavLink { From = 1, To = 2, RequiresCrouch = true });
            crawl.AddLink(new NavLink { From = 1, To = 3 });
            crawl.AddLink(new NavLink { From = 3, To = 2 });
            Assert.Equal(new List<int> { 1, 2 }, crawl.FindPath(1, 2));
        }

        [Fact]
        public void OneWayLinks_AreNotTraversableBackwards()
        {
            NavGraph graph = new NavGraph { Id = "drop" };
            graph.AddNode(Node(1, 0f, 0f, NavArea.Catwalk));
            graph.AddNode(Node(2, 0f, 5f, NavArea.Exterior));
            graph.AddNode(Node(3, 0f, 10f, NavArea.Exterior));
            graph.AddLink(new NavLink { From = 1, To = 2, OneWay = true });
            graph.AddLink(new NavLink { From = 2, To = 3 });

            Assert.Equal(new List<int> { 1, 2, 3 }, graph.FindPath(1, 3));
            // Coming back up the one way drop is impossible, so 3 -> 1 has no path.
            Assert.Empty(graph.FindPath(3, 1));
            Assert.Equal(new List<int> { 3, 2 }, graph.FindPath(3, 2));
        }

        [Fact]
        public void Pathfinding_IsDeterministic()
        {
            NavGraph a = BuildMap();
            NavGraph b = BuildMap();
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(a.FindPath(1, 6), b.FindPath(1, 6));
                Assert.Equal(new List<int> { 1, 2, 3, 6 }, a.FindPath(1, 6));
            }
        }

        [Fact]
        public void Queries_FindNearestNodesMarkersAndAreas()
        {
            NavGraph graph = BuildMap();
            Assert.Equal(2, graph.NearestNode(new KordonVec3(9f, 0f, 1f)));
            Assert.Equal(4, graph.NearestNode(new KordonVec3(9f, 0f, 1f), NavArea.Catwalk));
            Assert.Equal(5, graph.NearestNode(new KordonVec3(9f, 0f, 1f), NavArea.Extraction));
            Assert.Equal(6, graph.NearestNode(new KordonVec3(9f, 0f, 1f), NavArea.Tunnel));
            Assert.Equal(-1, graph.NearestNode(new KordonVec3(9f, 0f, 1f), (NavArea)99));

            Assert.Equal(new List<int> { 5 }, graph.NodesWithMarker("extraction_pad"));
            Assert.Equal(new List<int> { 1 }, graph.NodesWithMarker("spawn_south"));
            Assert.Empty(graph.NodesWithMarker("nowhere"));
            Assert.Equal(new List<int> { 6 }, graph.NodesInArea(NavArea.Tunnel));
            Assert.Equal(1, graph.NodesInArea(NavArea.Exterior).Count);

            Assert.True(graph.TryGetNode(3, out NavNode hall));
            Assert.Equal("kiln_hall", hall.Marker);
            Assert.Equal(NavArea.Indoor, hall.Area);
            Assert.False(graph.TryGetNode(999, out _));
            Assert.Equal(-1, graph.IndexOf(999));
            Assert.True(graph.IndexOf(4) >= 0);
        }

        [Fact]
        public void GraphsLoadFromTheJsonTheMapToolWrites()
        {
            const string json = @"{
              ""id"": ""zavod7"",
              ""nodes"": [
                { ""id"": 1, ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 }, ""area"": ""Spawn"", ""marker"": ""spawn_a"" },
                { ""id"": 2, ""position"": { ""x"": 12, ""y"": 0, ""z"": 4 }, ""area"": ""Exterior"", ""cover"": 0.4 },
                { ""id"": 3, ""position"": { ""x"": 24, ""y"": 3, ""z"": 4 }, ""area"": ""Catwalk"", ""cover"": 0.8 }
              ],
              ""links"": [
                { ""from"": 1, ""to"": 2 },
                { ""from"": 2, ""to"": 3, ""jump"": true, ""jumpHeight"": 1.2, ""cost"": 0 },
                { ""from"": 3, ""to"": 1, ""crouch"": true, ""oneWay"": true }
              ]
            }";

            NavGraph graph = NavGraph.FromJson(json);
            Assert.Equal("zavod7", graph.Id);
            Assert.Equal(3, graph.Count);
            Assert.Equal(3, graph.Links.Count);
            Assert.Empty(graph.Validate());

            Assert.True(graph.TryGetNode(3, out NavNode catwalk));
            Assert.Equal(NavArea.Catwalk, catwalk.Area);
            Assert.Equal(0.8f, catwalk.CoverScore, 3);
            Assert.Equal(3f, catwalk.Position.Y, 3);

            NavLink vault = graph.Links[1];
            Assert.True(vault.RequiresJump);
            Assert.Equal(1.2f, vault.JumpHeightM, 3);
            Assert.False(vault.OneWay);
            Assert.True(graph.Links[2].RequiresCrouch);
            Assert.True(graph.Links[2].OneWay);

            Assert.Equal(new List<int> { 1, 2, 3 }, graph.FindPath(1, 3));
        }

        [Fact]
        public void ShippedGraphs_AreConnectedAndConsistent()
        {
            // Authored by the Blender map tool into Assets/StreamingAssets/Data/Nav and
            // loaded by GameDatabase; CI re-validates every graph that ships.
            Assert.All(TestData.Database.NavGraphs, pair => Assert.Empty(pair.Value.Validate()));
        }
    }
}
