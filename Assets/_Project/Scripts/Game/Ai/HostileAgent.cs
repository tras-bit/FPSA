using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Movement;
using Kordon.Core.Nav;
using Kordon.Core.Utility;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Ai
{
    /// <summary>Marks a body the AI can see and shoot; the director resolves the damage.</summary>
    public sealed class RemoteTargetMarker : MonoBehaviour
    {
        public ulong ClientId;
    }

    /// <summary>
    /// A militia hostile in the co-op operation.  It walks the authored nav graph,
    /// spots operators by line of sight, shoots on a human-ish cadence with the same
    /// Core damage model the players use, and dies from server resolved damage.  AI
    /// is server side only; clients see the decisions, never make them.
    /// </summary>
    public sealed class HostileAgent : MonoBehaviour
    {
        public NavGraph Graph;
        public Transform[] Targets;
        public DeterministicRandom Random;
        public float SightRangeM = 46f;
        public float FieldOfViewDeg = 118f;
        public float FireIntervalSeconds = 0.85f;
        public float Damage = 16f;
        public float Accuracy01 = 0.55f;
        public string BulletId = "762x39_ps";

        public bool IsDead { get; private set; }
        public float Health { get; private set; } = 100f;
        public Vector3 Position => transform.position;
        public string DisplayName = "Militia";

        /// <summary>Applies a hostile's resolved hit to whatever the director considers a target.</summary>
        public System.Func<Transform, DamageResult, bool> DamageApplier;

        public event System.Action<HostileAgent, Transform, DamageResult> ShotOperator;
        public event System.Action<HostileAgent> Died;

        private readonly List<int> _path = new List<int>();
        private int _pathIndex;
        private float _repathTimer;
        private float _fireTimer;
        private float _thinkTimer;
        private Vector3 _lastKnownTarget;
        private bool _alerted;
        private Transform _target;
        private Vector3 _patrolGoal;
        private Renderer[] _renderers;
        private Collider _collider;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _collider = GetComponentInChildren<Collider>();
        }

        public void Configure(NavGraph graph, Transform[] targets, DeterministicRandom random,
            int startNodeId)
        {
            Graph = graph;
            Targets = targets;
            Random = random ?? new DeterministicRandom(0xA11CEUL);
            if (graph != null && graph.TryGetNode(startNodeId, out NavNode node))
            {
                transform.position = MapSpace.ToUnity(node.Position) + new Vector3(0f, 0.05f, 0f);
                _patrolGoal = transform.position;
            }

            _fireTimer = _thinkTimer = 0f;
        }

        public bool ApplyDamage(DamageResult result)
        {
            if (IsDead || !result.Accepted)
            {
                return false;
            }

            Health -= result.Damage;
            if (Health <= 0f)
            {
                Kill();
                return true;
            }

            _alerted = true;
            return false;
        }

        private void Kill()
        {
            IsDead = true;
            Health = 0f;
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            foreach (Renderer renderer in _renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            Died?.Invoke(this);
            Destroy(gameObject, 6f);
        }

        private void Update()
        {
            if (IsDead || Graph == null)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            _thinkTimer -= dt;
            if (_thinkTimer <= 0f)
            {
                _thinkTimer = 0.35f;
                Think();
            }

            Move(dt);
            TryShoot(dt);
        }

        private void Think()
        {
            _target = null;
            float bestDistance = float.MaxValue;
            if (Targets != null)
            {
                foreach (Transform candidate in Targets)
                {
                    if (candidate == null || !candidate.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(transform.position, candidate.position);
                    if (distance > SightRangeM || distance >= bestDistance)
                    {
                        continue;
                    }

                    Vector3 toTarget = (candidate.position + Vector3.up * 1.2f) - EyePosition();
                    if (Vector3.Angle(transform.forward, toTarget) > FieldOfViewDeg * 0.5f &&
                        distance > 8f)
                    {
                        continue;
                    }

                    if (!HasLineOfSight(candidate))
                    {
                        continue;
                    }

                    _target = candidate;
                    bestDistance = distance;
                }
            }

            if (_target != null)
            {
                _alerted = true;
                _lastKnownTarget = _target.position;
                _patrolGoal = _lastKnownTarget;
                RequestPath(_lastKnownTarget);
                return;
            }

            if (!_alerted || Vector3.Distance(transform.position, _lastKnownTarget) < 3f)
            {
                PickPatrolGoal();
            }

            RequestPath(_patrolGoal);
        }

        private void PickPatrolGoal()
        {
            if (Graph.Count == 0)
            {
                return;
            }

            List<NavNode> nodes = new List<NavNode>(Graph.Nodes);
            NavNode node = nodes[Random.Range(0, nodes.Count)];
            _patrolGoal = MapSpace.ToUnity(node.Position);
        }

        private void RequestPath(Vector3 worldGoal)
        {
            if (Graph == null)
            {
                return;
            }

            int start = Graph.NearestNode(MapSpace.ToCore(transform.position));
            int goal = Graph.NearestNode(MapSpace.ToCore(worldGoal));
            if (start < 0 || goal < 0)
            {
                return;
            }

            List<int> path = Graph.FindPath(start, goal);
            if (path == null || path.Count == 0)
            {
                return;
            }

            _path.Clear();
            _path.AddRange(path);
            _pathIndex = 0;
        }

        private void Move(float dt)
        {
            if (_pathIndex >= _path.Count)
            {
                return;
            }

            if (!Graph.TryGetNode(_path[_pathIndex], out NavNode node))
            {
                _pathIndex = _path.Count;
                return;
            }

            Vector3 goal = MapSpace.ToUnity(node.Position);
            Vector3 flat = goal - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance < 0.75f)
            {
                _pathIndex++;
                return;
            }

            Vector3 direction = flat / distance;
            float speed = _alerted ? MovementProfile.WalkSpeed * 1.15f : MovementProfile.WalkSpeed * 0.8f;
            transform.position += direction * speed * dt;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), 240f * dt);
        }

        private void TryShoot(float dt)
        {
            _fireTimer -= dt;
            if (_target == null || _fireTimer > 0f)
            {
                return;
            }

            _fireTimer = FireIntervalSeconds * (0.8f + Random.Range(0f, 0.5f));
            if (!HasLineOfSight(_target))
            {
                return;
            }

            // Aim error is a real cone: the further and the less accurate the
            // shooter, the more often the round goes wide (and misses outright).
            float missChance = Mathf.Clamp01(1f - Accuracy01);
            if (Random.Range(0f, 1f) < missChance * 0.5f)
            {
                return;
            }

            HitZone zone = Random.Range(0f, 1f) < 0.14f ? HitZone.Head
                : Random.Range(0f, 1f) < 0.62f ? HitZone.Thorax : HitZone.Abdomen;
            DamageRequest request = DamageRequest.Direct(
                new BulletDefinition
                {
                    Id = BulletId,
                    BaseDamage = Damage,
                },
                zone, new ArmorState(null), Vector3.Distance(transform.position, _target.position), Random);
            DamageResult result = new DamageModel().Resolve(request);
            if (!result.Accepted)
            {
                return;
            }

            bool accepted = DamageApplier != null && DamageApplier(_target, result);
            ShotOperator?.Invoke(this, _target, result);
        }

        private bool HasLineOfSight(Transform target)
        {
            Vector3 origin = EyePosition();
            Vector3 destination = target.position + Vector3.up * 1.25f;
            Vector3 direction = destination - origin;
            float distance = direction.magnitude;
            RaycastHit hit;
            if (!Physics.Raycast(origin, direction.normalized, out hit, distance))
            {
                return true;
            }

            return hit.collider.GetComponentInParent<RemoteTargetMarker>() != null ||
                   hit.transform == target;
        }

        private Vector3 EyePosition() => transform.position + Vector3.up * 1.62f;
    }
}
