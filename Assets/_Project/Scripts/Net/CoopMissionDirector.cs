using System;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Ballistics;
using Kordon.Core.AntiCheat;
using Kordon.Core.Combat;
using Kordon.Core.Coop;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Nav;
using Kordon.Core.Utility;
using Kordon.Game.Ai;
using Kordon.Game.Maps;
using Kordon.Game.Match;
using Kordon.Game.Player;
using Kordon.Game.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// Server authority for Operation Silent Kiln.  The mission chain, the hostile
    /// budget, objective progress, hold timers, the alarm and the evacuation are all
    /// decided here by the Core MissionController; clients send intents (frames,
    /// shots, "I am interacting") and render the mission state they are sent.
    /// </summary>
    public sealed class CoopMissionDirector : NetworkBehaviour
    {
        private const float SnapshotInterval = 0.1f;

        public MissionController Mission { get; private set; }
        public BuiltMap Map { get; private set; }

        private sealed class ServerOperator
        {
            public ulong ClientId;
            public string Name;
            public LoadoutKit Kit;
            public PlayerVitals Vitals;
            public ArmorState Armor;
            public RemoteOperatorView Body;
            public Vector3 Position;
            public float Yaw;
            public float LastFrameAt;
            public float LastShotAt = float.NegativeInfinity;
            public float LastGrenadeAt = float.NegativeInfinity;
        }

        private readonly Dictionary<ulong, ServerOperator> _operators =
            new Dictionary<ulong, ServerOperator>();
        private readonly List<HostileAgent> _hostiles = new List<HostileAgent>();
        private readonly Dictionary<string, Vector3> _objectivePoints =
            new Dictionary<string, Vector3>();
        private readonly Dictionary<string, float> _progress =
            new Dictionary<string, float>();
        private readonly ViolationLog _violations = new ViolationLog();

        private GameDatabase _database;
        private MissionDefinition _definition;
        private NavGraph _graph;
        private DeterministicRandom _random;
        private float _start;
        private float _snapshotTimer;
        private float _spawnTimer;
        private int _hostilesSpawned;
        private bool _extractionCalled;
        private bool _finished;
        private int _hostilesRemoved;
        private Transform _hostileRoot;

        // Thrown equipment in flight, stepped by the same Core model as the offline game.
        private readonly Dictionary<int, GrenadeProjectile> _grenades =
            new Dictionary<int, GrenadeProjectile>();
        private int _grenadeCounter;

        public event Action<MissionOutcome> MissionEnded;
        public event Action<string> Notice;

        private float Now => Time.time - _start;

        public void Configure(GameDatabase database, BuiltMap map)
        {
            _database = database;
            Map = map;
            _random = new DeterministicRandom(0x0C00F5EEDUL);
            _start = Time.time;
            _definition = database.GetMission("silent_kiln");
            if (_definition == null)
            {
                Notice?.Invoke("mission definition missing");
                return;
            }

            Mission = new MissionController(_definition);
            _graph = Map != null && Map.Nav != null
                ? Map.Nav
                : database.GetNavGraph(Map != null ? Map.Layout.NavGraphId : "coop_silent_kiln");
            ResolveObjectivePoints();

            GameObject root = new GameObject("Hostiles");
            _hostileRoot = root.transform;
            _spawnTimer = 3f;
        }

        private void ResolveObjectivePoints()
        {
            _objectivePoints.Clear();
            foreach (ObjectiveDefinition objective in _definition.Objectives)
            {
                string marker = MarkerFor(objective.Id);
                Vector3 point = ResolveMarker(marker);
                _objectivePoints[objective.Id] = point;
            }
        }

        /// <summary>The layout JSON pairs every objective id with the nav marker that anchors it.</summary>
        private string MarkerFor(string objectiveId)
        {
            switch (objectiveId)
            {
                case "reach_yard": return "nav_rail_spur";
                case "breach_gate": return "nav_yard_gate";
                case "sabotage_fuel_line": return "nav_fuel_line";
                case "retrieve_intel": return "nav_control_room";
                case "hold_kiln_hall": return "nav_kiln_hall";
                case "reach_extraction": return "nav_extraction_pad";
                case "extract": return "nav_extraction_pad";
                default: return string.Empty;
            }
        }

        private Vector3 ResolveMarker(string marker)
        {
            if (_graph == null || string.IsNullOrEmpty(marker))
            {
                return Vector3.zero;
            }

            List<int> nodes = _graph.NodesWithMarker(marker);
            if (nodes.Count == 0)
            {
                return Vector3.zero;
            }

            _graph.TryGetNode(nodes[0], out NavNode node);
            return MapSpace.ToUnity(node.Position);
        }

        // ------------------------------------------------------------------ join

        [ServerRpc(RequireOwnership = false)]
        public void JoinServerRpc(string displayName, string primaryId, string magId, int magCount,
            string secondaryId, string secondaryMagId, int secondaryMagCount, string armorId,
            string grenadeId, int grenadeCount, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (Mission == null)
            {
                return;
            }

            LoadoutSelection selection = new LoadoutSelection
            {
                PrimaryWeaponId = primaryId,
                SecondaryWeaponId = secondaryId,
                ArmorKitId = armorId,
                GrenadeId = grenadeId,
                GrenadeCount = Mathf.Clamp(grenadeCount, 0, 2),
            };
            if (!string.IsNullOrEmpty(magId) && magCount > 0)
            {
                selection.PrimaryMagazines.Add(new MagazinePlanEntry(magId, Mathf.Clamp(magCount, 0, 8)));
            }

            if (!string.IsNullOrEmpty(secondaryMagId) && secondaryMagCount > 0)
            {
                selection.SecondaryMagazines.Add(new MagazinePlanEntry(secondaryMagId,
                    Mathf.Clamp(secondaryMagCount, 0, 4)));
            }

            LoadoutValidationResult validation =
                LoadoutValidator.Validate(selection, _database.Definitions);
            if (!validation.IsValid)
            {
                NoticeClientRpc("kit rejected: " + string.Join("; ", validation.Errors.ToArray()),
                    Target(clientId));
                return;
            }

            if (Mission.ActiveOperatorCount >= _definition.MaxPlayers)
            {
                NoticeClientRpc("operation is full (" + _definition.MaxPlayers + " operators)",
                    Target(clientId));
                NetworkManager.Singleton.DisconnectClient(clientId);
                return;
            }

            if (!Mission.AddOperator(clientId, PlayerProfile.Sanitize(displayName), Now))
            {
                NoticeClientRpc("could not join the operation", Target(clientId));
                return;
            }

            ServerOperator op = new ServerOperator
            {
                ClientId = clientId,
                Name = PlayerProfile.Sanitize(displayName),
                Kit = new LoadoutKit(selection, _database.Definitions, null,
                    new DeterministicRandom(0xC0DEUL ^ clientId)),
                Vitals = new PlayerVitals(),
                Armor = new ArmorState(_database.Definitions.GetArmorKit(
                    string.IsNullOrEmpty(armorId) ? "standard" : armorId)),
                Position = InsertionPoint(clientId),
            };
            op.Kit.Seal();
            op.Body = RemoteOperatorView.Create(transform, clientId, op.Name, false);
            op.Body.Apply(op.Position, 0f, false, true);
            _operators[clientId] = op;

            WelcomeClientRpc(_definition.Name, _definition.BriefingTitle, _definition.BriefingText,
                Now, Target(clientId));
            PushMissionState();
        }

        private Vector3 InsertionPoint(ulong clientId)
        {
            if (Map == null || Map.SpawnPoints.Count == 0)
            {
                return new Vector3(0f, 1.2f, 0f);
            }

            int index = (int)(clientId % (ulong)Map.SpawnPoints.Count);
            return Map.SpawnPoints[index].position + new Vector3(0f, 0.1f, 0f);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReadyServerRpc(ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (Mission == null)
            {
                return;
            }

            if (Mission.Phase == MissionPhase.Briefing)
            {
                Mission.AcknowledgeBriefing(clientId, Now);
                int ready = 0;
                foreach (OperatorState op in Mission.Operators)
                {
                    if (op.AcknowledgedBriefing)
                    {
                        ready++;
                    }
                }

                if (ready >= Mission.ActiveOperatorCount && ready >= _definition.MinPlayers)
                {
                    Mission.BeginInfiltration(Now);
                    NoticeClientRpc("infiltration started - move to the rail spur");
                }
            }
        }

        // ------------------------------------------------------------ client input

        [ServerRpc(RequireOwnership = false)]
        public void FrameServerRpc(Vector3 position, float yaw, float pitch, bool sprinting,
            bool crouching, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_operators.TryGetValue(clientId, out ServerOperator op))
            {
                return;
            }

            float delta = op.LastFrameAt <= 0f ? 0.1f : Mathf.Max(0.001f, Now - op.LastFrameAt);
            op.LastFrameAt = Now;

            if (!ServerValidators.ValidateViewAngles(pitch).Allowed)
            {
                return;
            }

            float distance = Vector3.Distance(op.Position, position);
            ValidationVerdict movement = ServerValidators.ValidateMovement(distance, delta,
                MovementProfile.SprintSpeed * 1.35f,
                crouching ? Stance.Crouch : Stance.Stand, sprinting);
            if (!movement.Allowed)
            {
                _violations.Record(new Violation(movement.Kind, clientId, movement.Detail, Now,
                    movement.Severity));
                return;
            }

            op.Position = position;
            op.Yaw = yaw;
            op.Body?.Apply(position, yaw, false, true);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ShotServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_operators.TryGetValue(clientId, out ServerOperator shooter) || shooter.Vitals.IsDead)
            {
                return;
            }

            WeaponDefinition weapon = shooter.Kit.ActiveWeapon;
            if (weapon == null || Mission == null ||
                Mission.Phase != MissionPhase.ObjectivesActive &&
                Mission.Phase != MissionPhase.ExtractionCalled)
            {
                return;
            }

            float now = Now;
            if (!ServerValidators.ValidateFireRate(weapon, FireMode.Auto, now - shooter.LastShotAt).Allowed)
            {
                return;
            }

            if (!shooter.Kit.ActiveAmmoKit.TryFire(now).Fired)
            {
                return;
            }

            shooter.LastShotAt = now;

            RaycastHit hit;
            if (!Physics.Raycast(origin, direction.normalized, out hit, weapon.MaxEffectiveRangeM * 4f))
            {
                return;
            }

            HostileAgent hostile = hit.collider.GetComponentInParent<HostileAgent>();
            if (hostile == null || hostile.IsDead)
            {
                return;
            }

            HitZone zone = hit.point.y - hostile.Position.y >= 1.5f ? HitZone.Head
                : hit.point.y - hostile.Position.y >= 0.85f ? HitZone.Thorax : HitZone.Abdomen;
            MagazineDefinition magazine = shooter.Kit.ActiveAmmoKit.Chambered != null
                ? _database.Definitions.GetMagazine(shooter.Kit.ActiveAmmoKit.Chambered.DefinitionId)
                : null;
            BulletDefinition bullet = magazine != null
                ? _database.Definitions.ResolveBullet(weapon, magazine)
                : _database.Definitions.GetBullet(weapon.DefaultBulletId);

            DamageRequest request = DamageRequest.Direct(bullet, zone, new ArmorState(null),
                Vector3.Distance(origin, hit.point), _random);
            DamageResult result = new DamageModel().Resolve(request);
            bool killed = hostile.ApplyDamage(result);
            HitConfirmClientRpc(result.Damage, zone.ToString(), Target(clientId));
            if (killed)
            {
                _hostilesRemoved++;
                KillFeedClientRpc(shooter.Name + "  [" + weapon.DisplayName + "]  " + hostile.DisplayName);
            }
        }

        /// <summary>
        /// Grenade throw in the operation.  Operators and militia stand in the same
        /// blast: the server resolves both sides with the Core BlastModel.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void GrenadeThrowServerRpc(Vector3 origin, Vector3 direction, float fuseSeconds,
            ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_operators.TryGetValue(clientId, out ServerOperator thrower) || thrower.Vitals.IsDead)
            {
                return;
            }

            if (Mission == null || thrower.Kit.Grenade == null ||
                Mission.Phase != MissionPhase.ObjectivesActive &&
                Mission.Phase != MissionPhase.ExtractionCalled)
            {
                return;
            }

            GrenadeDefinition grenade = thrower.Kit.Grenade;
            Vector3 hand = thrower.Position + Vector3.up * 1.2f;
            float now = Now;
            if (Vector3.Distance(origin, hand) > 2.5f ||
                float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z) ||
                direction.sqrMagnitude < 0.01f)
            {
                return;
            }

            if (now - thrower.LastGrenadeAt < GrenadeTrajectory.MinThrowIntervalSeconds)
            {
                return;
            }

            float minFuse = Mathf.Max(0.05f, grenade.CookableMinFuse);
            float maxFuse = Mathf.Max(minFuse, grenade.FuseSeconds);
            float fuse = Mathf.Clamp(fuseSeconds, minFuse, maxFuse);

            if (!thrower.Kit.TryThrowGrenade(now))
            {
                return;
            }

            thrower.LastGrenadeAt = now;

            GrenadeFlightState state = new GrenadeFlightState
            {
                Position = MapSpace.ToCore(origin),
                Velocity = GrenadeTrajectory.LaunchVelocity(grenade.ThrowSpeed,
                    MapSpace.ToCore(direction.normalized)),
                FuseRemaining = fuse,
            };

            int id = ++_grenadeCounter;
            GrenadeProjectile projectile = GrenadeProjectile.SpawnWithState(transform, grenade, state,
                false, null, id);
            if (projectile == null)
            {
                return;
            }

            _grenades[id] = projectile;
            projectile.Detonated += OnGrenadeDetonated;
            GrenadeSpawnedClientRpc(id, origin, MapSpace.ToUnity(state.Velocity), fuse, grenade.Id);
        }

        private void OnGrenadeDetonated(GrenadeProjectile projectile, Vector3 position)
        {
            if (projectile == null)
            {
                return;
            }

            projectile.Detonated -= OnGrenadeDetonated;
            _grenades.Remove(projectile.Id);
            GrenadeDefinition grenade = projectile.Definition;
            ResolveBlast(grenade, position, projectile.Id);
            if (grenade != null && grenade.Kind == GrenadeKind.Smoke)
            {
                // The server keeps the cloud in the same field the militia AI reads.
                SmokeScreen.Add(position + Vector3.up * 0.4f, grenade.EffectRadiusM * 0.85f,
                    grenade.EffectDurationSeconds);
            }
            GrenadeExplodedClientRpc(projectile.Id, position,
                grenade != null ? grenade.Id : string.Empty);
        }

        /// <summary>Fragments, smoke and flash against every operator and every militia body.</summary>
        private void ResolveBlast(GrenadeDefinition grenade, Vector3 position, int grenadeId)
        {
            if (grenade == null)
            {
                return;
            }

            float radius = Mathf.Max(grenade.DamageRadiusM,
                Mathf.Max(grenade.SuppressionRadiusM, grenade.EffectRadiusM));
            BlastModel model = new BlastModel();

            List<HostileAgent> hostiles = new List<HostileAgent>(_hostiles);
            for (int i = 0; i < hostiles.Count; i++)
            {
                HostileAgent hostile = hostiles[i];
                if (hostile == null || hostile.IsDead)
                {
                    continue;
                }

                Vector3 chest = hostile.Position + Vector3.up * 1.1f;
                float distance = Vector3.Distance(position, chest);
                if (distance > radius)
                {
                    continue;
                }

                BlastResult blast = model.Resolve(new BlastRequest
                {
                    Grenade = grenade,
                    DistanceM = distance,
                    HasLineOfSight = GrenadeBlast.HasLineOfSight(position, chest, hostile.transform),
                    CoverFactor = 0f,
                    ArmorClass = 0f,
                });

                DamageResult damage = blast.ToDamageResult();
                if (damage.Accepted)
                {
                    hostile.ApplyDamage(damage);
                }
            }

            List<ServerOperator> operators = new List<ServerOperator>(_operators.Values);
            for (int i = 0; i < operators.Count; i++)
            {
                ServerOperator op = operators[i];
                if (op.Vitals.IsDead)
                {
                    continue;
                }

                Vector3 chest = op.Position + Vector3.up * 1.2f;
                float distance = Vector3.Distance(position, chest);
                if (distance > radius)
                {
                    continue;
                }

                BlastResult blast = model.Resolve(new BlastRequest
                {
                    Grenade = grenade,
                    DistanceM = distance,
                    HasLineOfSight = GrenadeBlast.HasLineOfSight(position, chest,
                        op.Body != null ? op.Body.transform : null),
                    CoverFactor = 0f,
                    ArmorClass = BlastModel.AverageArmorClass(op.Armor),
                });

                if (!blast.HasEffect)
                {
                    continue;
                }

                DamageResult damage = blast.ToDamageResult();
                if (!damage.Accepted)
                {
                    op.Vitals.Suppression.Add(blast.Suppression);
                    continue;
                }

                if (op.Vitals.ApplyDamage(damage, DeathCause.Explosion))
                {
                    NoticeClientRpc("an operator was caught in the blast", new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = null },
                    });
                }
            }
        }

        /// <summary>F on an objective point: breach, sabotage, intel, extraction.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void InteractServerRpc(ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (Mission == null || !_operators.TryGetValue(clientId, out ServerOperator op))
            {
                return;
            }

            ObjectiveDefinition active = Mission.FirstActiveObjective();
            if (active == null)
            {
                return;
            }

            if (!_objectivePoints.TryGetValue(active.Id, out Vector3 point))
            {
                return;
            }

            if (Vector3.Distance(op.Position, point) > active.InteractionRadiusM + 1.5f)
            {
                NoticeClientRpc("too far from " + active.Title, Target(clientId));
                return;
            }

            if (active.Id == "extract" || active.Id == "reach_extraction")
            {
                if (Mission.ExtractionAvailable())
                {
                    if (Mission.Phase != MissionPhase.ExtractionCalled)
                    {
                        Mission.CallExtraction(Now);
                        NoticeClientRpc("extraction inbound - hold the pad",
                            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });
                    }
                    else
                    {
                        Mission.BeginBoarding(Now);
                        NoticeClientRpc("boarding started - stay on the pad");
                    }
                }

                return;
            }

            if (active.Kind == ObjectiveKind.Hold)
            {
                NoticeClientRpc("hold the area - the timer runs while you are inside", Target(clientId));
                return;
            }

            if (Mission.ProgressObjective(active.Id, 1, clientId, Now))
            {
                NoticeClientRpc("objective complete: " + active.Title,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = null } });
            }
        }

        // ------------------------------------------------------------------ tick

        private void Update()
        {
            if (!IsServer || Mission == null || _definition == null || _finished)
            {
                return;
            }

            float now = Now;
            Mission.Tick(now);
            UpdateHoldObjectives(now, Time.deltaTime);
            UpdateExtraction(now);
            UpdateHostiles(now);
            UpdateOperatorDeath(now);

            _snapshotTimer -= Time.deltaTime;
            if (_snapshotTimer <= 0f)
            {
                _snapshotTimer = SnapshotInterval;
                PushMissionState();
            }

            if (Finished(now))
            {
                _finished = true;
                MissionOutcome outcome = new MissionOutcome
                {
                    Success = Mission.Phase == MissionPhase.Completed,
                    Name = _definition.Name,
                    Reason = Mission.FailReason.ToString(),
                    HostilesRemoved = _hostilesRemoved,
                    DurationSeconds = now,
                    Debrief = _definition.DebriefText,
                };
                MissionEnded?.Invoke(outcome);
                MissionEndedClientRpc(outcome.Success, outcome.Reason, outcome.HostilesRemoved,
                    outcome.DurationSeconds, outcome.Debrief);
            }
        }

        private bool Finished(float now) =>
            Mission.Phase == MissionPhase.Completed || Mission.Phase == MissionPhase.Failed;

        private void UpdateHoldObjectives(float now, float deltaTime)
        {
            ObjectiveDefinition active = Mission.FirstActiveObjective();
            if (active == null || active.Kind != ObjectiveKind.Hold || active.HoldSeconds <= 0f)
            {
                return;
            }

            if (!_objectivePoints.TryGetValue(active.Id, out Vector3 point))
            {
                return;
            }

            bool occupied = false;
            foreach (ServerOperator op in _operators.Values)
            {
                if (!op.Vitals.IsDead && Vector3.Distance(op.Position, point) <=
                    active.InteractionRadiusM + 3f)
                {
                    occupied = true;
                    break;
                }
            }

            // The Core controller tracks the hold timer; the director only reports
            // whether an operator is actually standing in the area.
            Mission.TickHoldObjective(active.Id, occupied, deltaTime, now);
            ObjectiveState state = Mission.GetObjective(active.Id);
            _progress[active.Id] = state != null ? state.HoldTimer : 0f;
        }

        private void UpdateExtraction(float now)
        {
            if (Mission.Phase != MissionPhase.ExtractionBoarding)
            {
                return;
            }

            if (!_objectivePoints.TryGetValue("extract", out Vector3 pad))
            {
                pad = _objectivePoints.TryGetValue("reach_extraction", out Vector3 alt)
                    ? alt
                    : Vector3.zero;
            }

            int aboard = 0;
            int alive = 0;
            foreach (ServerOperator op in _operators.Values)
            {
                if (op.Vitals.IsDead)
                {
                    continue;
                }

                alive++;
                if (Vector3.Distance(op.Position, pad) <= 9f)
                {
                    Mission.BoardOperator(op.ClientId, now);
                    aboard++;
                }
            }

            bool windowOver = Mission.BoardingTimeRemaining <= 0f &&
                              !float.IsNaN(Mission.BoardingStartedAt);
            if (alive > 0 && (aboard >= alive || windowOver))
            {
                Mission.Complete(now);
            }
        }

        private void UpdateHostiles(float now)
        {
            if (Mission.Phase != MissionPhase.ObjectivesActive &&
                Mission.Phase != MissionPhase.ExtractionCalled)
            {
                return;
            }

            _hostiles.RemoveAll(hostile => hostile == null || hostile.IsDead);
            int alive = 0;
            foreach (HostileAgent hostile in _hostiles)
            {
                if (!hostile.IsDead)
                {
                    alive++;
                }
            }

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer > 0f || alive >= _definition.MaxSimultaneousHostiles ||
                _hostilesSpawned >= _definition.TotalHostileBudget)
            {
                return;
            }

            _spawnTimer = 4.5f;
            SpawnHostile(now);
        }

        private void SpawnHostile(float now)
        {
            if (_graph == null || _graph.Count == 0)
            {
                return;
            }

            List<NavNode> candidates = new List<NavNode>(_graph.Nodes);
            if (candidates.Count == 0)
            {
                return;
            }

            NavNode node = candidates[_random.Range(0, candidates.Count)];
            Vector3 position = MapSpace.ToUnity(node.Position);
            foreach (ServerOperator op in _operators.Values)
            {
                if (Vector3.Distance(position, op.Position) < 26f)
                {
                    return;
                }
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Hostile_" + (++_hostilesSpawned);
            go.transform.SetParent(_hostileRoot, false);
            go.transform.localScale = new Vector3(0.6f, 0.9f, 0.45f);
            Material body = SurfaceMaterialLibrary.ForSurface("steel_rust");
            if (body != null)
            {
                go.GetComponent<Renderer>().sharedMaterial = body;
            }

            HostileAgent agent = go.AddComponent<HostileAgent>();
            agent.DisplayName = "Militia " + _hostilesSpawned;
            List<Transform> targets = new List<Transform>();
            foreach (ServerOperator op in _operators.Values)
            {
                if (op.Body != null)
                {
                    targets.Add(op.Body.transform);
                }
            }

            agent.Configure(_graph, targets.ToArray(), _random, node.Id);
            agent.DamageApplier = ApplyHostileHit;
            agent.Died += _ => _hostilesRemoved++;
            _hostiles.Add(agent);
            Mission.NoteHostileSpawned(1);
        }

        /// <summary>Hostile rounds land on the server's copy of the operator, then replicate.</summary>
        private bool ApplyHostileHit(Transform target, DamageResult result)
        {
            foreach (ServerOperator op in _operators.Values)
            {
                if (op.Body != null && op.Body.transform == target)
                {
                    bool died = op.Vitals.ApplyDamage(result, DeathCause.Gunfire);
                    if (died)
                    {
                        NoticeClientRpc("an operator is down", new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams { TargetClientIds = null },
                        });
                    }

                    return true;
                }
            }

            return false;
        }

        private void UpdateOperatorDeath(float now)
        {
            foreach (ServerOperator op in _operators.Values)
            {
                if (op.Vitals.IsDead)
                {
                    continue;
                }

                if (op.Vitals.IsDowned)
                {
                    Mission.DownOperator(op.ClientId, now);
                }
            }
        }

        // ------------------------------------------------------------- snapshots

        [ClientRpc]
        private void GrenadeSpawnedClientRpc(int id, Vector3 position, Vector3 velocity,
            float fuseSeconds, string grenadeId, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseGrenadeSpawned(id, position, velocity, fuseSeconds, grenadeId);
        }

        [ClientRpc]
        private void GrenadeExplodedClientRpc(int id, Vector3 position, string grenadeId,
            ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseGrenadeExploded(id, position, grenadeId);
        }

        private static ClientRpcParams Target(ulong clientId) => new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
        };

        private void PushMissionState()
        {
            if (!IsServer || Mission == null)
            {
                return;
            }

            ObjectiveDefinition active = Mission.FirstActiveObjective();
            string title = active != null ? active.Title
                : Mission.Phase == MissionPhase.Completed ? "extracted"
                : "no active objective";
            float progress = active != null && _progress.TryGetValue(active.Id, out float held)
                ? held
                : 0f;
            float hold = active != null ? active.HoldSeconds : 0f;

            int count = _operators.Count;
            ulong[] ids = new ulong[count];
            Vector3[] positions = new Vector3[count];
            float[] yaws = new float[count];
            int[] health = new int[count];
            bool[] dead = new bool[count];
            int index = 0;
            foreach (ServerOperator op in _operators.Values)
            {
                ids[index] = op.ClientId;
                positions[index] = op.Position;
                yaws[index] = op.Yaw;
                health[index] = Mathf.RoundToInt(op.Vitals.Health);
                dead[index] = op.Vitals.IsDead;
                index++;
            }

            int hostileCount = _hostiles.Count;
            Vector3[] hostiles = new Vector3[hostileCount];
            bool[] hostileDead = new bool[hostileCount];
            for (int i = 0; i < hostileCount; i++)
            {
                hostiles[i] = _hostiles[i] != null ? _hostiles[i].Position : Vector3.zero;
                hostileDead[i] = _hostiles[i] == null || _hostiles[i].IsDead;
            }

            MissionStateClientRpc((int)Mission.Phase, title,
                active != null ? active.Description : string.Empty, progress, hold,
                Mission.ExtractionAvailable(), (int)Mission.FailReason,
                Mission.MissionTimeRemaining, ids, positions, yaws, health, dead,
                hostiles, hostileDead);
        }

        [ClientRpc]
        private void MissionStateClientRpc(int phase, string title, string description,
            float progress, float holdSeconds, bool extractionAvailable, int failReason,
            float timeRemaining, ulong[] ids, Vector3[] positions, float[] yaws, int[] health,
            bool[] dead, Vector3[] hostiles, bool[] hostileDead)
        {
            NetworkSessionEvents.RaiseMission((MissionPhase)phase, title, description, progress,
                holdSeconds, extractionAvailable, failReason, timeRemaining);
            NetworkSessionEvents.RaiseState(ids, positions, yaws, health, dead);
            NetworkSessionEvents.RaiseHostiles(hostiles, hostileDead);
        }

        [ClientRpc]
        private void WelcomeClientRpc(string missionName, string briefingTitle, string briefingText,
            float serverTime, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseBriefing(missionName, briefingTitle, briefingText);
        }

        [ClientRpc]
        private void NoticeClientRpc(string message, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseNotice(message);
        }

        [ClientRpc]
        private void HitConfirmClientRpc(float damage, string zone, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseHitConfirm(damage, zone);
        }

        [ClientRpc]
        private void KillFeedClientRpc(string line)
        {
            NetworkSessionEvents.RaiseKillFeed(line);
        }

        [ClientRpc]
        private void MissionEndedClientRpc(bool success, string reason, int hostilesRemoved,
            float durationSeconds, string debrief)
        {
            NetworkSessionEvents.RaiseMissionEnded(success, reason, hostilesRemoved,
                durationSeconds, debrief);
        }
    }

    /// <summary>Result of a co-op operation, as shown on the results screen.</summary>
    public struct MissionOutcome
    {
        public bool Success;
        public string Name;
        public string Reason;
        public int HostilesRemoved;
        public float DurationSeconds;
        public string Debrief;
    }
}
