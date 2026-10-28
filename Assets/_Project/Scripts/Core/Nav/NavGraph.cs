using System;
using System.Collections.Generic;
using Kordon.Core.Serialization;
using Kordon.Core.Utility;

namespace Kordon.Core.Nav
{
    /// <summary>Area tag used by AI cover selection and by path cost tuning.</summary>
    public enum NavArea
    {
        Open = 0,
        Cover = 1,
        Indoor = 2,
        Elevated = 3,
        Tunnel = 4,
        Doorway = 5,
        Exterior = 6,
        Catwalk = 7,
        Objective = 8,
        Extraction = 9,
        Spawn = 10
    }

    /// <summary>One walkable position in the hand authored navigation graph.</summary>
    [Serializable]
    public struct NavNode
    {
        public int Id;
        public KordonVec3 Position;
        public NavArea Area;

        /// <summary>0 = exposed, 1 = fully covered. Used by AI when picking a firing position.</summary>
        public float CoverScore;

        /// <summary>Optional marker name (objective id, spawn group, door name).</summary>
        public string Marker;
    }

    /// <summary>A connection between two nodes, with a traversal cost and capability flags.</summary>
    [Serializable]
    public struct NavLink
    {
        public int From;
        public int To;
        public float Cost;
        public bool RequiresCrouch;
        public bool RequiresJump;
        public float JumpHeightM;
        public bool OneWay;
    }

    /// <summary>
    /// Hand authored navigation graph.  The project deliberately does not use Unity's
    /// NavMesh: the graph is exported by the Blender tooling together with the level
    /// layout (see Tools/blender/build_map_zavod7.py), which means AI navigation works
    /// without any editor baking step and can be unit tested.
    /// </summary>
    [Serializable]
    public sealed class NavGraph
    {
        private readonly List<NavNode> _nodes = new List<NavNode>();
        private readonly Dictionary<int, int> _indexById = new Dictionary<int, int>();
        private List<int>[] _adjacency;

        public string Id = "nav";
        public IReadOnlyList<NavNode> Nodes => _nodes;
        public List<NavLink> Links { get; } = new List<NavLink>();

        public int Count => _nodes.Count;

        public int AddNode(NavNode node)
        {
            _indexById[node.Id] = _nodes.Count;
            _nodes.Add(node);
            _adjacency = null;
            return _nodes.Count - 1;
        }

        public void AddLink(NavLink link)
        {
            Links.Add(link);
            _adjacency = null;
        }

        public bool TryGetNode(int id, out NavNode node)
        {
            if (_indexById.TryGetValue(id, out int index))
            {
                node = _nodes[index];
                return true;
            }

            node = default;
            return false;
        }

        public int IndexOf(int id) => _indexById.TryGetValue(id, out int index) ? index : -1;

        private void BuildAdjacency()
        {
            if (_adjacency != null)
            {
                return;
            }

            _adjacency = new List<int>[_nodes.Count];
            for (int i = 0; i < _adjacency.Length; i++)
            {
                _adjacency[i] = new List<int>();
            }

            for (int linkIndex = 0; linkIndex < Links.Count; linkIndex++)
            {
                NavLink link = Links[linkIndex];
                int fromIndex = IndexOf(link.From);
                int toIndex = IndexOf(link.To);
                if (fromIndex < 0 || toIndex < 0)
                {
                    continue;
                }

                _adjacency[fromIndex].Add(linkIndex);
                if (!link.OneWay)
                {
                    _adjacency[toIndex].Add(linkIndex);
                }
            }
        }

        public int NearestNode(KordonVec3 position, NavArea? areaFilter = null)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (areaFilter.HasValue && _nodes[i].Area != areaFilter.Value)
                {
                    continue;
                }

                float sqr = KordonVec3.SqrDistance(_nodes[i].Position, position);
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = _nodes[i].Id;
                }
            }

            return best;
        }

        public List<int> NodesWithMarker(string marker)
        {
            List<int> ids = new List<int>();
            foreach (NavNode node in _nodes)
            {
                if (node.Marker == marker)
                {
                    ids.Add(node.Id);
                }
            }

            return ids;
        }

        public List<int> NodesInArea(NavArea area)
        {
            List<int> ids = new List<int>();
            foreach (NavNode node in _nodes)
            {
                if (node.Area == area)
                {
                    ids.Add(node.Id);
                }
            }

            return ids;
        }

        /// <summary>Deterministic A* over the graph. Returns node ids, start included.</summary>
        public List<int> FindPath(int startId, int goalId)
        {
            List<int> result = new List<int>();
            BuildAdjacency();
            int startIndex = IndexOf(startId);
            int goalIndex = IndexOf(goalId);
            if (startIndex < 0 || goalIndex < 0)
            {
                return result;
            }

            if (startIndex == goalIndex)
            {
                result.Add(startId);
                return result;
            }

            int n = _nodes.Count;
            float[] gScore = new float[n];
            float[] fScore = new float[n];
            int[] cameFrom = new int[n];
            bool[] closed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                gScore[i] = float.MaxValue;
                fScore[i] = float.MaxValue;
                cameFrom[i] = -1;
            }

            gScore[startIndex] = 0f;
            fScore[startIndex] = Heuristic(_nodes[startIndex].Position, _nodes[goalIndex].Position);

            BinaryHeap open = new BinaryHeap(n * 2);
            open.Push(startIndex, fScore[startIndex]);

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current])
                {
                    continue;
                }

                closed[current] = true;
                if (current == goalIndex)
                {
                    break;
                }

                foreach (int linkIndex in _adjacency[current])
                {
                    NavLink link = Links[linkIndex];
                    bool forward = link.From == _nodes[current].Id;
                    if (!forward && link.OneWay)
                    {
                        continue;
                    }

                    int neighbourIndex = IndexOf(forward ? link.To : link.From);
                    if (neighbourIndex < 0 || closed[neighbourIndex])
                    {
                        continue;
                    }

                    float cost = link.Cost;
                    if (cost <= 0f)
                    {
                        cost = KordonVec3.Distance(_nodes[current].Position, _nodes[neighbourIndex].Position);
                    }

                    // Traversal capabilities cost extra: vaulting and crawling are slower.
                    if (link.RequiresJump)
                    {
                        cost += 1.4f + link.JumpHeightM * 0.5f;
                    }

                    if (link.RequiresCrouch)
                    {
                        cost += 0.6f;
                    }

                    float tentative = gScore[current] + cost;
                    if (tentative < gScore[neighbourIndex])
                    {
                        gScore[neighbourIndex] = tentative;
                        cameFrom[neighbourIndex] = current;
                        fScore[neighbourIndex] = tentative +
                                               Heuristic(_nodes[neighbourIndex].Position,
                                                   _nodes[goalIndex].Position);
                        open.Push(neighbourIndex, fScore[neighbourIndex]);
                    }
                }
            }

            if (cameFrom[goalIndex] < 0 && goalIndex != startIndex)
            {
                return result; // unreachable
            }

            int cursor = goalIndex;
            while (cursor >= 0)
            {
                result.Add(_nodes[cursor].Id);
                cursor = cameFrom[cursor];
            }

            result.Reverse();
            return result;
        }

        public float PathLength(List<int> nodeIds)
        {
            if (nodeIds == null || nodeIds.Count < 2)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 1; i < nodeIds.Count; i++)
            {
                if (TryGetNode(nodeIds[i - 1], out NavNode a) && TryGetNode(nodeIds[i], out NavNode b))
                {
                    total += KordonVec3.Distance(a.Position, b.Position);
                }
            }

            return total;
        }

        private static float Heuristic(KordonVec3 a, KordonVec3 b) => KordonVec3.Distance(a, b);

        /// <summary>
        /// Structural validation used by CI: every link must reference existing nodes and
        /// the graph must be fully connected, otherwise AI (and players) could be trapped.
        /// </summary>
        public List<string> Validate()
        {
            List<string> errors = new List<string>();
            if (_nodes.Count == 0)
            {
                errors.Add($"{Id}: graph has no nodes");
                return errors;
            }

            foreach (NavLink link in Links)
            {
                if (IndexOf(link.From) < 0)
                {
                    errors.Add($"{Id}: link references unknown node {link.From}");
                }

                if (IndexOf(link.To) < 0)
                {
                    errors.Add($"{Id}: link references unknown node {link.To}");
                }

                if (link.Cost < 0f)
                {
                    errors.Add($"{Id}: negative link cost {link.From}->{link.To}");
                }
            }

            // Reachability from node 0 over undirected edges.
            BuildAdjacency();
            bool[] seen = new bool[_nodes.Count];
            Stack<int> stack = new Stack<int>();
            stack.Push(0);
            seen[0] = true;
            int visited = 0;
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                visited++;
                foreach (int linkIndex in _adjacency[current])
                {
                    NavLink link = Links[linkIndex];
                    int neighbour = IndexOf(link.From == _nodes[current].Id ? link.To : link.From);
                    if (neighbour >= 0 && !seen[neighbour])
                    {
                        seen[neighbour] = true;
                        stack.Push(neighbour);
                    }
                }
            }

            if (visited != _nodes.Count)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (!seen[i])
                    {
                        errors.Add($"{Id}: node {_nodes[i].Id} is unreachable from node {_nodes[0].Id}");
                    }
                }
            }

            return errors;
        }

        public static NavGraph FromJson(string json)
        {
            Json root = Json.Parse(json);
            NavGraph graph = new NavGraph { Id = root["id"].AsString("nav") };

            foreach (Json item in root["nodes"].Items())
            {
                NavNode node = new NavNode
                {
                    Id = item["id"].AsInt(),
                    Position = new KordonVec3(
                        item["position"]["x"].AsFloat(),
                        item["position"]["y"].AsFloat(),
                        item["position"]["z"].AsFloat()),
                    Area = item["area"].AsEnum(NavArea.Open),
                    CoverScore = item["cover"].AsFloat(0f),
                    Marker = item["marker"].AsString(string.Empty)
                };
                graph.AddNode(node);
            }

            foreach (Json item in root["links"].Items())
            {
                graph.AddLink(new NavLink
                {
                    From = item["from"].AsInt(),
                    To = item["to"].AsInt(),
                    Cost = item["cost"].AsFloat(0f),
                    RequiresCrouch = item["crouch"].AsBool(false),
                    RequiresJump = item["jump"].AsBool(false),
                    JumpHeightM = item["jumpHeight"].AsFloat(0f),
                    OneWay = item["oneWay"].AsBool(false)
                });
            }

            return graph;
        }
    }

    /// <summary>Minimal binary heap for A*, ordered by (fScore, nodeIndex) for determinism.</summary>
    internal sealed class BinaryHeap
    {
        private readonly int[] _items;
        private readonly float[] _priorities;
        private int _count;

        public BinaryHeap(int capacity)
        {
            _items = new int[Math.Max(4, capacity)];
            _priorities = new float[Math.Max(4, capacity)];
        }

        public int Count => _count;

        public void Push(int item, float priority)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
                Array.Resize(ref _priorities, _priorities.Length * 2);
            }

            int index = _count++;
            _items[index] = item;
            _priorities[index] = priority;

            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(index, parent))
                {
                    break;
                }

                Swap(index, parent);
                index = parent;
            }
        }

        public int Pop()
        {
            int result = _items[0];
            _count--;
            if (_count > 0)
            {
                _items[0] = _items[_count];
                _priorities[0] = _priorities[_count];
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;
                    if (left < _count && Less(left, smallest))
                    {
                        smallest = left;
                    }

                    if (right < _count && Less(right, smallest))
                    {
                        smallest = right;
                    }

                    if (smallest == index)
                    {
                        break;
                    }

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            return result;
        }

        private bool Less(int a, int b)
        {
            if (Math.Abs(_priorities[a] - _priorities[b]) > 1e-6f)
            {
                return _priorities[a] < _priorities[b];
            }

            return _items[a] < _items[b];
        }

        private void Swap(int a, int b)
        {
            int tmpItem = _items[a];
            _items[a] = _items[b];
            _items[b] = tmpItem;
            float tmpPriority = _priorities[a];
            _priorities[a] = _priorities[b];
            _priorities[b] = tmpPriority;
        }
    }
}
