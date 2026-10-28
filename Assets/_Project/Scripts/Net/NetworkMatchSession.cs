using System;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Rules;
using Kordon.Game.Maps;
using Kordon.Game.Match;
using Kordon.Game.Player;
using Kordon.Game.Ui;
using Kordon.Game.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// The online Free-for-All session: brings up the transport, the map, the
    /// server director and the local operator, then feeds replicated state into the
    /// HUD.  Implements IMatchConnector so GameFlow and every screen stay
    /// transport agnostic.  Clients send intent at 20 Hz (position, aim, trigger);
    /// the server decides every outcome and the client renders what it is told.
    /// </summary>
    public sealed class NetworkMatchSession : MonoBehaviour, IMatchConnector
    {
        public event Action<string> Status;
        public event Action<MatchResults> MatchEnded;

        private const float FrameInterval = 0.05f;

        private KordonNetwork _network;
        private NetworkMatchDirector _director;
        private GameDatabase _database;
        private BuiltMap _map;
        private LocalOperator _local;
        private LoadoutSelection _loadout;
        private bool _host;
        private bool _joined;
        private float _frameTimer;
        private float _joinRetryTimer;
        private readonly List<ScoreRow> _rows = new List<ScoreRow>();
        private readonly Dictionary<ulong, RemoteOperatorView> _remote =
            new Dictionary<ulong, RemoteOperatorView>();
        private Transform _remoteRoot;

        public KordonNetwork Network => _network;

        public void Begin(string address, int port, bool host, string regionId,
            LoadoutSelection loadout, string displayName)
        {
            _database = GameDatabase.Load(ContentPaths.DataRoot);
            _loadout = loadout;
            _host = host;

            GameObject networkObject = new GameObject("Network");
            networkObject.transform.SetParent(transform, false);
            _network = networkObject.AddComponent<KordonNetwork>();
            _network.StatusChanged += message => Status?.Invoke(message);

            BuildWorld();
            BuildLocalOperator();
            SubscribeEvents();

            if (host)
            {
                // The host is the server: bring the transport up first so the
                // director can be spawned as a networked object.
                if (!_network.StartHost(
                        _database.Net.DefaultRegion != null &&
                        !string.IsNullOrEmpty(_database.Net.DefaultRegion.Address)
                            ? _database.Net.DefaultRegion.Address
                            : "0.0.0.0", port))
                {
                    Status?.Invoke("could not start the host");
                    return;
                }

                SpawnDirector();
            }
            else if (!_network.StartClient(address, port, regionId))
            {
                Status?.Invoke("could not start the client");
            }
        }

        private void BuildWorld()
        {
            MapLayout layout = MapLayout.FromJson(ContentPaths.Read(
                ContentPaths.MapLayout("ffa_zavod7")));
            GameObject root = new GameObject("OnlineWorld");
            root.transform.SetParent(transform, false);
            _map = MapBuilder.Build(layout, KitManifestData.Load(), root.transform);

            GameObject remoteRoot = new GameObject("RemoteOperators");
            remoteRoot.transform.SetParent(transform, false);
            _remoteRoot = remoteRoot.transform;
        }

        private void BuildLocalOperator()
        {
            GameObject root = new GameObject("LocalOperatorRoot");
            root.transform.SetParent(transform, false);
            _local = LocalOperatorFactory.Build(root.transform, this, _database.Definitions,
                _loadout, 0xD00D5EEDUL, true);
            _local.Weapons.ShotFired += OnLocalShot;
            _local.Weapons.AmmoEventRaised += OnAmmoEvent;
            _local.Hud.SetObjective("FREE-FOR-ALL - first operator to 50 kills wins");
            _local.Body.Respawn(SpawnPoint(), 0f);
        }

        private Vector3 SpawnPoint()
        {
            if (_map == null || _map.SpawnPoints.Count == 0)
            {
                return new Vector3(0f, 1.2f, 0f);
            }

            return _map.SpawnPoints[0].position + new Vector3(0f, 0.1f, 0f);
        }

        private void SpawnDirector()
        {
            GameObject directorObject = new GameObject("MatchDirector");
            directorObject.transform.SetParent(transform, false);
            NetworkObject networkObject = directorObject.AddComponent<NetworkObject>();
            _director = directorObject.AddComponent<NetworkMatchDirector>();
            networkObject.Spawn();
            _director.Configure(_database, _map,
                new Kordon.Core.Utility.DeterministicRandom(0xBEEF5EEDUL));
            _director.MatchEnded += results => MatchEnded?.Invoke(results);
            _director.Notice += message => Status?.Invoke(message);
        }

        private void SubscribeEvents()
        {
            NetworkSessionEvents.Welcome += OnWelcome;
            NetworkSessionEvents.Notice += OnNotice;
            NetworkSessionEvents.Respawn += OnRespawn;
            NetworkSessionEvents.InspectResult += OnInspectResult;
            NetworkSessionEvents.HitConfirm += OnHitConfirm;
            NetworkSessionEvents.KillFeed += OnKillFeed;
            NetworkSessionEvents.MatchEnded += OnMatchEnded;
            NetworkSessionEvents.Scores += OnScores;
            NetworkSessionEvents.State += OnState;
        }

        private void Update()
        {
            _network?.Tick();
            if (_local == null || _network == null || !_network.IsRunning)
            {
                return;
            }

            EnsureDirector();
            if (!_joined)
            {
                return;
            }

            _frameTimer -= Time.deltaTime;
            if (_frameTimer <= 0f)
            {
                _frameTimer = FrameInterval;
                SendFrame();
            }

            RefreshHud();
        }

        /// <summary>
        /// The director is a networked object spawned by the server: a client finds
        /// it after replication and then announces its kit.
        /// </summary>
        private void EnsureDirector()
        {
            if (_director == null)
            {
                _director = FindObjectOfType<NetworkMatchDirector>();
                if (_director == null)
                {
                    return;
                }

                _director.MatchEnded += results => MatchEnded?.Invoke(results);
                _director.Notice += message => Status?.Invoke(message);
            }

            if (_joined || !_network.Manager.IsConnectedClient)
            {
                return;
            }

            _joinRetryTimer -= Time.deltaTime;
            if (_joinRetryTimer > 0f)
            {
                return;
            }

            _joinRetryTimer = 1f;
            SaveJoin();
        }

        private void SaveJoin()
        {
            if (_director == null || _loadout == null)
            {
                return;
            }

            _joined = true;
            _director.JoinServerRpc(PlayerProfile.Name, _loadout.PrimaryWeaponId,
                _loadout.PrimaryMagazines.Count > 0 ? _loadout.PrimaryMagazines[0].MagazineId : "",
                _loadout.PrimaryMagazines.Count > 0 ? _loadout.PrimaryMagazines[0].Count : 0,
                _loadout.SecondaryWeaponId,
                _loadout.SecondaryMagazines.Count > 0 ? _loadout.SecondaryMagazines[0].MagazineId : "",
                _loadout.SecondaryMagazines.Count > 0 ? _loadout.SecondaryMagazines[0].Count : 0,
                _loadout.ArmorKitId, _loadout.GrenadeId, _loadout.GrenadeCount);
        }

        private void SendFrame()
        {
            if (_director == null)
            {
                return;
            }

            Vector3 position = _local.Body.transform.position;
            float yaw = -_local.Body.transform.eulerAngles.y;
            float pitch = _local.Rig != null ? _local.Rig.Pitch : 0f;
            _director.FrameServerRpc(position, yaw, pitch, _local.Body.Sprinting,
                _local.Body.Stance != Stance.Stand);
        }

        private void OnLocalShot(ShotInfo shot)
        {
            _director?.ShotServerRpc(shot.Origin, shot.Direction);
        }

        private void OnAmmoEvent(AmmoEvent ammoEvent)
        {
            if (ammoEvent.Kind == AmmoEventKind.InspectReadable)
            {
                _director?.InspectServerRpc();
            }
        }

        private void RefreshHud()
        {
            if (_local?.Hud == null)
            {
                return;
            }

            _local.Hud.SetHealth(_local.Body.Vitals.Health / PlayerVitals.MaxHealth);
            _local.Hud.SetScoreboard(_rows);
            _local.Hud.SetMatchLine("ONLINE FFA   kills " + LocalKills() + " / 50   ping " +
                                    _network.LastPingMs + " ms   " + _rows.Count + " players");
        }

        private int LocalKills()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsLocal)
                {
                    return _rows[i].Kills;
                }
            }

            return 0;
        }

        // ------------------------------------------------------------- callbacks

        private void OnWelcome(int killTarget, float serverTime)
        {
            Status?.Invoke("joined - first to " + killTarget + " kills wins");
        }

        private void OnNotice(string message)
        {
            Status?.Invoke(message);
            _local?.Hud.Toast(message);
        }

        private void OnRespawn(Vector3 position, int protectionSeconds)
        {
            _local?.Body.Respawn(position, 0f);
            _local?.Weapons.Loadout?.OnRespawnResupply();
            if (_local != null && protectionSeconds > 0)
            {
                _local.Hud.SetObjective("spawn protection " + protectionSeconds + " s");
            }
        }

        private void OnInspectResult(string magazineName, int rounds, int capacity, bool hasReserve)
        {
            // The HUD's inspect readout is driven by the local kit state; the server
            // answer exists so a tampered client cannot display a count it does not own.
            _local?.Hud.Toast(magazineName + " confirmed by server");
        }

        private void OnHitConfirm(float damage, string zone) => _local?.Hud.HitMarker();

        private void OnKillFeed(string line) => _local?.Hud.Toast(line);

        private void OnScores(ulong[] ids, int[] kills, int[] deaths, string[] names, int[] pings,
            MatchPhase phase)
        {
            ulong localId = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : 0UL;
            _rows.Clear();
            for (int i = 0; i < ids.Length; i++)
            {
                _rows.Add(new ScoreRow
                {
                    Name = names[i],
                    Kills = kills[i],
                    Deaths = deaths[i],
                    Ping = pings[i],
                    IsLocal = ids[i] == localId,
                    IsBot = false,
                });
            }
        }

        private void OnState(ulong[] ids, Vector3[] positions, float[] yaws, int[] health, bool[] dead)
        {
            ulong localId = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : 0UL;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == localId)
                {
                    // The local body is locally simulated; only death is enforced
                    // from the server (the client may not declare itself alive).
                    if (dead[i] && !_local.Body.IsDead)
                    {
                        _local.Body.Vitals.ApplyDamage(new DamageResult
                        {
                            Accepted = true,
                            Damage = 999f,
                            Fatal = true,
                            Zone = HitZone.Thorax,
                        }, DeathCause.Gunfire);
                        _local.Hud.Toast("you were eliminated");
                    }

                    continue;
                }

                if (!_remote.TryGetValue(ids[i], out RemoteOperatorView view))
                {
                    view = RemoteOperatorView.Create(_remoteRoot, ids[i], "operator", true);
                    _remote[ids[i]] = view;
                }

                view.Apply(positions[i], yaws[i], dead[i], false);
            }
        }

        private void OnMatchEnded(int reason, ulong winnerId, string winnerName, int winnerKills,
            float durationSeconds)
        {
            MatchResults results = new MatchResults
            {
                Reason = (MatchEndReason)reason,
                WinnerId = winnerId,
                WinnerName = winnerName,
                WinnerKills = winnerKills,
                DurationSeconds = durationSeconds,
                Standings = ToRows(),
            };
            MatchEnded?.Invoke(results);
        }

        private List<ScoreboardRow> ToRows()
        {
            List<ScoreboardRow> rows = new List<ScoreboardRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                rows.Add(new ScoreboardRow
                {
                    DisplayName = _rows[i].Name,
                    Kills = _rows[i].Kills,
                    Deaths = _rows[i].Deaths,
                    PingMs = _rows[i].Ping,
                    Rank = i + 1,
                });
            }

            return rows;
        }

        public void Shutdown()
        {
            NetworkSessionEvents.Clear();
            _network?.Shutdown();
            Destroy(gameObject);
        }

        private void OnDestroy() => NetworkSessionEvents.Clear();
    }
}
