using System;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Ballistics;
using Kordon.Core.AntiCheat;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Rules;
using Kordon.Core.Serialization;
using Kordon.Core.Utility;
using Kordon.Game.Maps;
using Kordon.Game.Match;
using Kordon.Game.Player;
using Kordon.Game.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// Server authoritative Free-for-All.  Clients send intents only - where they
    /// are, where they aim, that they pulled the trigger, that they want to respawn
    /// or inspect their magazine.  This object decides everything that counts:
    /// fire rate, hit registration (its own raycast against its own colliders),
    /// damage, armour, ammo, deaths, kills, respawn timing and the 50 kill win.
    /// Every rejection goes through the shared validation rules and the violation
    /// log, so a cheating client is caught rather than believed.
    /// </summary>
    public sealed class NetworkMatchDirector : NetworkBehaviour
    {
        public const float SnapshotInterval = 0.1f;

        public FfaMatchController Match { get; private set; }
        public bool Finished { get; private set; }

        private sealed class ServerPlayer
        {
            public ulong ClientId;
            public string Name;
            public LoadoutKit Kit;
            public PlayerVitals Vitals;
            public ArmorState Armor;
            public Vector3 Position;
            public float Yaw;
            public float LastFrameAt;
            public float LastShotAt = float.NegativeInfinity;
            public float LastGrenadeAt = float.NegativeInfinity;
            public bool LoadoutAccepted;
        }

        private readonly Dictionary<ulong, RemoteOperatorView> _bodies =
            new Dictionary<ulong, RemoteOperatorView>();

        private readonly Dictionary<ulong, ServerPlayer> _players =
            new Dictionary<ulong, ServerPlayer>();
        private readonly ViolationLog _violations = new ViolationLog();
        private readonly Dictionary<ulong, float> _joinedAt = new Dictionary<ulong, float>();

        // Thrown equipment in flight. The server owns every one of them: it steps the
        // Core flight model, sweeps for collisions itself and resolves the blast.
        private readonly Dictionary<int, GrenadeProjectile> _grenades =
            new Dictionary<int, GrenadeProjectile>();
        private readonly Dictionary<int, ulong> _grenadeOwners = new Dictionary<int, ulong>();
        private int _grenadeCounter;

        private GameDatabase _database;
        private BuiltMap _map;
        private DeterministicRandom _random;
        private float _start;
        private float _snapshotTimer;
        private float Now => Time.time - _start;

        public event Action<MatchResults> MatchEnded;
        public event Action<string> Notice;

        public void Configure(GameDatabase database, BuiltMap map, DeterministicRandom random)
        {
            _database = database;
            _map = map;
            _random = random;
            _start = Time.time;
            MatchRulesConfig rules = database.MatchRules.Clone();
            rules.AllowBots = false;
            Match = new FfaMatchController(rules, random);
            Match.BeginLoadoutPhase(0f);
        }

        private void Update()
        {
            if (!IsServer || Match == null || Finished)
            {
                return;
            }

            float now = Now;
            Match.Tick(now);

            // Phase machine: loadout -> countdown -> live, driven by the roster.
            if (Match.Phase == MatchPhase.Loadout &&
                (Match.CountdownRemaining > 0f || ReadyCount() >= Match.Config.MinPlayersToStartLive))
            {
                if (Match.BeginCountdown(now))
                {
                    BroadcastNotice("all kits locked - match starting");
                }
            }

            if (Match.Phase == MatchPhase.Countdown && Match.CountdownRemaining <= 0f)
            {
                Match.BeginLive(now);
                BroadcastNotice("match live - first to " + Match.Config.KillTarget + " kills");
            }

            if (Match.Phase == MatchPhase.Live)
            {
                foreach (ServerPlayer player in _players.Values)
                {
                    if (player.Vitals.IsDead && Match.IsRespawnReady(player.ClientId, now))
                    {
                        Respawn(player, now);
                    }
                }
            }

            _snapshotTimer -= Time.deltaTime;
            if (_snapshotTimer <= 0f)
            {
                _snapshotTimer = SnapshotInterval;
                PushScoreSnapshot();
            }

            if (Match.Phase == MatchPhase.Finished && !Finished)
            {
                Finished = true;
                MatchResults results = Match.BuildResults();
                EndMatchClientRpc((int)results.Reason, results.WinnerId, results.WinnerName,
                    results.WinnerKills, results.DurationSeconds);
                MatchEnded?.Invoke(results);
            }
        }

        private int ReadyCount()
        {
            int ready = 0;
            foreach (ServerPlayer player in _players.Values)
            {
                if (player.LoadoutAccepted)
                {
                    ready++;
                }
            }

            return ready;
        }

        // ------------------------------------------------------------- join/leave

        /// <summary>
        /// Client announces itself.  The kit is rebuilt and validated on the server
        /// from primitive fields, so a modified client cannot smuggle extra
        /// magazines, a weapon it did not earn or a different armour class.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void JoinServerRpc(string displayName, string primaryId, string primaryMagId,
            int primaryMagCount, string secondaryId, string secondaryMagId, int secondaryMagCount,
            string armorId, string grenadeId, int grenadeCount, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            LoadoutSelection selection = new LoadoutSelection
            {
                PrimaryWeaponId = primaryId,
                SecondaryWeaponId = secondaryId,
                ArmorKitId = armorId,
                GrenadeId = grenadeId,
                GrenadeCount = Mathf.Clamp(grenadeCount, 0, 2),
            };
            if (!string.IsNullOrEmpty(primaryMagId) && primaryMagCount > 0)
            {
                selection.PrimaryMagazines.Add(new MagazinePlanEntry(primaryMagId,
                    Mathf.Clamp(primaryMagCount, 0, 8)));
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
                RejectClientRpc("kit rejected: " + string.Join("; ", validation.Errors.ToArray()),
                    Target(clientId));
                return;
            }

            JoinResult join = Match.TryJoin(clientId, PlayerProfile.Sanitize(displayName), false, Now);
            if (!join.IsAccepted)
            {
                RejectClientRpc("cannot join: " + join.Reason, Target(clientId));
                NetworkManager.Singleton.DisconnectClient(clientId);
                return;
            }

            LoadoutLockResult locked = Match.SubmitLoadout(clientId, selection,
                _database.Definitions, Now);
            if (!locked.Accepted)
            {
                RejectClientRpc("kit locked: " + locked.Reason, Target(clientId));
                return;
            }

            ServerPlayer player = new ServerPlayer
            {
                ClientId = clientId,
                Name = PlayerProfile.Sanitize(displayName),
                Kit = new LoadoutKit(selection, _database.Definitions, null,
                    new DeterministicRandom(0x51E5DUL ^ clientId)),
                Vitals = new PlayerVitals(),
                Armor = new ArmorState(_database.Definitions.GetArmorKit(
                    string.IsNullOrEmpty(armorId) ? "standard" : armorId)),
                Position = SpawnPosition((int)(clientId % 20UL)),
                Yaw = 0f,
                LoadoutAccepted = true,
            };
            player.Kit.Seal();
            _players[clientId] = player;
            RemoteOperatorView body = RemoteOperatorView.Create(transform, clientId, player.Name, false);
            body.Apply(player.Position, player.Yaw, false, true);
            _bodies[clientId] = body;
            _joinedAt[clientId] = Now;

            WelcomeClientRpc((int)Match.Config.KillTarget, Now, Target(clientId));
            PushScoreSnapshot();
        }

        public void HandleDisconnect(ulong clientId)
        {
            if (!IsServer || Match == null)
            {
                return;
            }

            // The match keeps the slot warm for the reconnect grace window.
            Match.MarkDisconnected(clientId, Now);
            Notice?.Invoke("operator " + clientId + " lost connection");
        }

        public void HandleReconnect(ulong clientId, string token)
        {
            if (!IsServer || Match == null)
            {
                return;
            }

            Match.TryReconnect(clientId, token, Now);
        }

        // ------------------------------------------------------------ client input

        /// <summary>Position/aim frames, validated against the movement speed limits.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void FrameServerRpc(Vector3 position, float yaw, float pitch, bool sprinting,
            bool crouching, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_players.TryGetValue(clientId, out ServerPlayer player))
            {
                return;
            }

            float now = Now;
            float delta = player.LastFrameAt <= 0f ? 0.1f : Mathf.Max(0.001f, now - player.LastFrameAt);
            player.LastFrameAt = now;

            ValidationVerdict viewAngles = ServerValidators.ValidateViewAngles(pitch);
            if (!viewAngles.Allowed)
            {
                RecordViolation(clientId, viewAngles);
                return;
            }

            float distance = Vector3.Distance(player.Position, position);
            Stance stance = crouching ? Stance.Crouch : Stance.Stand;
            ValidationVerdict movement = ServerValidators.ValidateMovement(distance, delta,
                MovementProfile.SprintSpeed * 1.35f, stance, sprinting);
            if (!movement.Allowed)
            {
                // The accepted position is what the snapshot keeps sending, so a
                // teleporting client is dragged back on its next state update.
                RecordViolation(clientId, movement);
                return;
            }

            player.Position = position;
            player.Yaw = yaw;
            if (_bodies.TryGetValue(clientId, out RemoteOperatorView body))
            {
                body.Apply(position, yaw, false, true);
            }
        }

        /// <summary>The shot claim. The server re-derives the ray, the spread and the damage.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ShotServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_players.TryGetValue(clientId, out ServerPlayer shooter) || shooter.Vitals.IsDead)
            {
                return;
            }

            WeaponDefinition weapon = shooter.Kit.ActiveWeapon;
            if (weapon == null || Match.Phase != MatchPhase.Live)
            {
                return;
            }

            float now = Now;
            ValidationVerdict rate = ServerValidators.ValidateFireRate(weapon,
                FireMode.Auto, now - shooter.LastShotAt);
            if (!rate.Allowed)
            {
                RecordViolation(clientId, rate);
                return;
            }

            FireAttempt attempt = shooter.Kit.ActiveAmmoKit.TryFire(now);
            if (!attempt.Fired)
            {
                return;
            }

            shooter.LastShotAt = now;
            Match.NotifyShotFired(clientId, now);

            RaycastHit hit;
            if (!Physics.Raycast(origin, direction.normalized, out hit,
                    weapon.MaxEffectiveRangeM * 4f))
            {
                return;
            }

            RemoteOperatorView victimView = hit.collider.GetComponentInParent<RemoteOperatorView>();
            if (victimView == null || victimView.ClientId == clientId)
            {
                return;
            }

            if (!_players.TryGetValue(victimView.ClientId, out ServerPlayer victim) ||
                victim.Vitals.IsDead)
            {
                return;
            }

            HitZone zone = ZoneFromHit(victim, hit.point);
            MagazineDefinition magazine = shooter.Kit.ActiveAmmoKit.Chambered != null
                ? _database.Definitions.GetMagazine(shooter.Kit.ActiveAmmoKit.Chambered.DefinitionId)
                : null;
            BulletDefinition bullet = magazine != null
                ? _database.Definitions.ResolveBullet(weapon, magazine)
                : _database.Definitions.GetBullet(weapon.DefaultBulletId);

            DamageRequest request = DamageRequest.Direct(bullet, zone, victim.Armor,
                Vector3.Distance(origin, hit.point), _random);
            DamageResult result = new DamageModel().Resolve(request);
            if (!result.Accepted)
            {
                return;
            }

            bool died = victim.Vitals.ApplyDamage(result, DeathCause.Gunfire);
            Match.NotifyHitLanded(clientId);
            Match.RegisterDamage(clientId, victim.ClientId, result.Damage, zone, Now);

            HitConfirmClientRpc(clientId, result.Damage, zone.ToString(), Target(clientId));

            if (died)
            {
                if (_bodies.TryGetValue(victim.ClientId, out RemoteOperatorView deadBody))
                {
                    deadBody.Apply(victim.Position, victim.Yaw, true, true);
                }

                KillRegistrationResult kill = Match.RegisterKill(clientId, victim.ClientId,
                    weapon.Id, zone, (hit.point - origin).magnitude, Now);
                if (kill.IsAccepted)
                {
                    KillFeedClientRpc(shooter.Name, weapon.DisplayName, victim.Name,
                        zone == HitZone.Head);
                }
            }
        }

        private static HitZone ZoneFromHit(ServerPlayer victim, Vector3 point)
        {
            float height = point.y - victim.Position.y;
            if (height >= 1.5f)
            {
                return HitZone.Head;
            }

            return height >= 0.85f ? HitZone.Thorax : HitZone.Abdomen;
        }

        /// <summary>
        /// The throw claim.  The client says where it threw from, in which direction and
        /// with how much fuze left; the server checks all three against its own copy of
        /// the operator and then owns the flight and the blast.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void GrenadeThrowServerRpc(Vector3 origin, Vector3 direction, float fuseSeconds,
            ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_players.TryGetValue(clientId, out ServerPlayer thrower) || thrower.Vitals.IsDead)
            {
                return;
            }

            if (Match.Phase != MatchPhase.Live || thrower.Kit.Grenade == null)
            {
                return;
            }

            GrenadeDefinition grenade = thrower.Kit.Grenade;
            Vector3 hand = thrower.Position + Vector3.up * 1.2f;
            float now = Now;
            if (Vector3.Distance(origin, hand) > 2.5f)
            {
                RecordViolation(clientId, ValidationVerdict.Reject(ViolationKind.InvalidState,
                    "grenade-origin", 1.5f));
                return;
            }

            if (float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z) ||
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
            _grenadeOwners[id] = clientId;
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
            _grenadeOwners.TryGetValue(projectile.Id, out ulong throwerId);
            _grenadeOwners.Remove(projectile.Id);

            GrenadeDefinition grenade = projectile.Definition;
            ResolveBlast(grenade, position, throwerId);
            if (grenade != null && grenade.Kind == GrenadeKind.Smoke)
            {
                // The server keeps the cloud in the same field the militia AI reads.
                SmokeScreen.Add(position + Vector3.up * 0.4f, grenade.EffectRadiusM * 0.85f,
                    grenade.EffectDurationSeconds);
            }
            GrenadeExplodedClientRpc(projectile.Id, position, grenade != null ? grenade.Id : string.Empty);
        }

        /// <summary>
        /// Server blast: the same Core model the offline game uses, applied to the
        /// server's bodies.  Kills are registered through the match controller, so a
        /// grenade can only ever score what the server itself observed.
        /// </summary>
        private void ResolveBlast(GrenadeDefinition grenade, Vector3 position, ulong throwerId)
        {
            if (grenade == null)
            {
                return;
            }

            float radius = Mathf.Max(grenade.DamageRadiusM,
                Mathf.Max(grenade.SuppressionRadiusM, grenade.EffectRadiusM));
            BlastModel model = new BlastModel();
            List<ServerPlayer> victims = new List<ServerPlayer>(_players.Values);

            for (int i = 0; i < victims.Count; i++)
            {
                ServerPlayer victim = victims[i];
                Vector3 chest = victim.Position + Vector3.up * 1.3f;
                float distance = Vector3.Distance(position, chest);
                if (distance > radius)
                {
                    continue;
                }

                bool lineOfSight = GrenadeBlast.HasLineOfSight(position, chest,
                    _bodies.TryGetValue(victim.ClientId, out RemoteOperatorView body)
                        ? body.transform
                        : null);

                BlastResult blast = model.Resolve(new BlastRequest
                {
                    Grenade = grenade,
                    DistanceM = distance,
                    HasLineOfSight = lineOfSight,
                    CoverFactor = lineOfSight ? 0f : 1f,
                    ArmorClass = BlastModel.AverageArmorClass(victim.Armor),
                    SelfInflicted = victim.ClientId == throwerId,
                });

                if (!blast.HasEffect)
                {
                    continue;
                }

                DamageResult damage = blast.ToDamageResult();
                if (!damage.Accepted)
                {
                    victim.Vitals.Suppression.Add(blast.Suppression);
                    continue;
                }

                bool died = victim.Vitals.ApplyDamage(damage, DeathCause.Explosion);
                if (!died)
                {
                    continue;
                }

                if (_bodies.TryGetValue(victim.ClientId, out RemoteOperatorView deadBody))
                {
                    deadBody.Apply(victim.Position, victim.Yaw, true, true);
                }

                if (victim.ClientId == throwerId)
                {
                    // Your own grenade never scores, but it does kill you.
                    Match.RegisterUncreditedDeath(victim.ClientId, DeathCause.Explosion, Now);
                    continue;
                }

                KillRegistrationResult kill = Match.RegisterKill(throwerId, victim.ClientId,
                    grenade.Id, HitZone.Thorax, distance, Now);
                if (!kill.IsAccepted)
                {
                    continue;
                }

                Match.NotifyHitLanded(throwerId);
                string throwerName = _players.TryGetValue(throwerId, out ServerPlayer killer)
                    ? killer.Name
                    : "operator";
                KillFeedClientRpc(throwerName, grenade.DisplayName, victim.Name, false);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void RespawnRequestServerRpc(ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_players.TryGetValue(clientId, out ServerPlayer player))
            {
                return;
            }

            if (Match.IsRespawnReady(clientId, Now))
            {
                Respawn(player, Now);
            }
        }

        /// <summary>Hold R answers: the exact round count goes only to the owner, only on inspect.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void InspectServerRpc(ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            if (!_players.TryGetValue(clientId, out ServerPlayer player))
            {
                return;
            }

            AmmoHudSnapshot snapshot = player.Kit.BuildHudSnapshot(Now);
            if (!snapshot.MayShowExactCount)
            {
                return;
            }

            InspectResultClientRpc(snapshot.InspectedMagazineName,
                snapshot.InspectedRounds ?? 0, snapshot.InspectedCapacity ?? 0,
                snapshot.HasReserveAmmunition, Target(clientId));
        }

        [ServerRpc(RequireOwnership = false)]
        public void LeaveServerRpc(ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            Match.TryLeave(clientId, Now);
        }

        private void Respawn(ServerPlayer player, float now)
        {
            RespawnResult respawn = Match.RequestRespawn(player.ClientId, -1, now);
            if (!respawn.IsAccepted)
            {
                return;
            }

            Vector3 spawn = SpawnPosition(respawn.SpawnPointIndex);
            player.Position = spawn;
            player.Vitals.Reset();
            if (_bodies.TryGetValue(player.ClientId, out RemoteOperatorView body))
            {
                body.Apply(spawn, 0f, false, true);
            }
            player.Kit.OnRespawnResupply();
            RespawnClientRpc(spawn, (int)(respawn.SpawnProtectionUntil - now), Target(player.ClientId));
        }

        private Vector3 SpawnPosition(int index)
        {
            if (_map == null || _map.SpawnPoints.Count == 0)
            {
                return new Vector3(0f, 1.2f, 0f);
            }

            Transform spawn = _map.SpawnPoints[Math.Abs(index) % _map.SpawnPoints.Count];
            return spawn.position + new Vector3(0f, 0.1f, 0f);
        }

        private void RecordViolation(ulong clientId, ValidationVerdict verdict)
        {
            _violations.Record(new Violation(verdict.Kind, clientId, verdict.Detail, Now,
                verdict.Severity));
            if (_violations.ShouldKick(clientId))
            {
                ViolationKickClientRpc(Target(clientId));
                NetworkManager.Singleton.DisconnectClient(clientId);
            }
        }

        // ---------------------------------------------------------------- rpcs out

        private static ClientRpcParams Target(ulong clientId) => new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
        };

        [ClientRpc]
        private void WelcomeClientRpc(int killTarget, float serverTime, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseWelcome(killTarget, serverTime);
        }

        [ClientRpc]
        private void RejectClientRpc(string reason, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseNotice(reason);
        }

        [ClientRpc]
        private void RespawnClientRpc(Vector3 position, int protectionSeconds,
            ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseRespawn(position, protectionSeconds);
        }

        [ClientRpc]
        private void InspectResultClientRpc(string magazineName, int rounds, int capacity,
            bool hasReserve, ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseInspect(magazineName, rounds, capacity, hasReserve);
        }

        [ClientRpc]
        private void HitConfirmClientRpc(ulong shooterId, float damage, string zone,
            ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseHitConfirm(damage, zone);
        }

        [ClientRpc]
        private void KillFeedClientRpc(string killer, string weapon, string victim, bool headshot)
        {
            NetworkSessionEvents.RaiseKillFeed(killer + "  [" + weapon + "]" +
                                               (headshot ? "  HEAD" : string.Empty) + "  " + victim);
        }

        [ClientRpc]
        private void ViolationKickClientRpc(ClientRpcParams parameters = default)
        {
            NetworkSessionEvents.RaiseNotice("removed by the server: rule violations");
        }

        [ClientRpc]
        private void EndMatchClientRpc(int reason, ulong winnerId, string winnerName, int winnerKills,
            float durationSeconds)
        {
            NetworkSessionEvents.RaiseMatchEnded(reason, winnerId, winnerName, winnerKills,
                durationSeconds);
        }

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

        private void PushScoreSnapshot()
        {
            if (!IsServer || Match == null)
            {
                return;
            }

            List<ScoreboardRow> rows = Match.BuildScoreboard();
            ulong[] ids = new ulong[rows.Count];
            int[] kills = new int[rows.Count];
            int[] deaths = new int[rows.Count];
            string[] names = new string[rows.Count];
            int[] pings = new int[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                ids[i] = rows[i].ClientId;
                kills[i] = rows[i].Kills;
                deaths[i] = rows[i].Deaths;
                names[i] = rows[i].DisplayName;
                pings[i] = rows[i].PingMs;
            }

            // Names travel as one length prefixed string: NGO's RPC code generator
            // cannot serialize a string[] and fails the ILPP build (see NameListWire).
            ScoreSnapshotClientRpc(ids, kills, deaths, NameListWire.Encode(names), pings,
                (int)Match.Phase);
            PushStateSnapshot();
        }

        /// <summary>
        /// One state packet per tick: who is where, how hurt and whether they are down.
        /// Positions are the ones the server accepted, so the server is always the
        /// truth about where everybody stands.
        /// </summary>
        private void PushStateSnapshot()
        {
            int count = _players.Count;
            ulong[] ids = new ulong[count];
            Vector3[] positions = new Vector3[count];
            float[] yaws = new float[count];
            int[] health = new int[count];
            bool[] dead = new bool[count];
            int index = 0;
            foreach (ServerPlayer player in _players.Values)
            {
                ids[index] = player.ClientId;
                positions[index] = player.Position;
                yaws[index] = player.Yaw;
                health[index] = Mathf.RoundToInt(player.Vitals.Health);
                dead[index] = player.Vitals.IsDead;
                index++;
            }

            StateSnapshotClientRpc(ids, positions, yaws, health, dead);
        }

        [ClientRpc]
        private void StateSnapshotClientRpc(ulong[] ids, Vector3[] positions, float[] yaws,
            int[] health, bool[] dead)
        {
            NetworkSessionEvents.RaiseState(ids, positions, yaws, health, dead);
        }

        [ClientRpc]
        private void ScoreSnapshotClientRpc(ulong[] ids, int[] kills, int[] deaths, string names,
            int[] pings, int phase)
        {
            string[] decoded = NameListWire.Decode(names, ids != null ? ids.Length : 0);
            NetworkSessionEvents.RaiseScore(ids, kills, deaths, decoded, pings,
                (MatchPhase)phase);
        }

        private void BroadcastNotice(string message)
        {
            NoticeClientRpc(message);
        }

        [ClientRpc]
        private void NoticeClientRpc(string message)
        {
            NetworkSessionEvents.RaiseNotice(message);
        }
    }
}
