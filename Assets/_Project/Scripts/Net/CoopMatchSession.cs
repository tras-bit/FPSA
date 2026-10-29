using Kordon.Core.Settings;
using System;
using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Coop;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Rules;
using Kordon.Game.Ai;
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
    /// Client (and host) side of Operation Silent Kiln: brings up the transport, the
    /// Kiln map, the local operator and the mission HUD, then renders replicated
    /// mission state, operator positions and hostile positions.  The briefing is a
    /// real gate: nobody infiltrates until every operator acknowledges it.
    /// </summary>
    public sealed class CoopMatchSession : MonoBehaviour, IMatchConnector
    {
        public event Action<string> Status;
        public event Action<MatchResults> MatchEnded;

        public string ModeLabel => "CO-OP";

        private const float FrameInterval = 0.05f;

        private KordonNetwork _network;
        private CoopMissionDirector _director;
        private GameDatabase _database;
        private BuiltMap _map;
        private LocalOperator _local;
        private LoadoutSelection _loadout;
        private bool _host;
        private bool _joined;
        private bool _ready;
        private float _frameTimer;
        private float _joinTimer;
        private string _objectiveTitle = "";
        private float _holdProgress;
        private float _holdSeconds;
        private Transform _hostileRoot;
        private Transform _remoteRoot;
        private ClientGrenadeView _grenadeView;
        private readonly Dictionary<int, RemoteOperatorView> _hostileViews =
            new Dictionary<int, RemoteOperatorView>();
        private readonly Dictionary<ulong, RemoteOperatorView> _remote =
            new Dictionary<ulong, RemoteOperatorView>();

        public void Begin(string address, int port, bool host, string regionId,
            LoadoutSelection loadout, string displayName)
        {
            _database = GameDatabase.Load(ContentPaths.DataRoot);
            _loadout = loadout;
            _host = host;

            GameObject netObject = new GameObject("Network");
            netObject.transform.SetParent(transform, false);
            _network = netObject.AddComponent<KordonNetwork>();
            _network.StatusChanged += message => Status?.Invoke(message);

            BuildWorld();
            BuildLocalOperator();
            Subscribe();

            if (host)
            {
                if (!_network.StartHost(_database.Net.DefaultRegion != null &&
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
                ContentPaths.MapLayout("coop_silent_kiln")));
            GameObject root = new GameObject("KilnWorld");
            root.transform.SetParent(transform, false);
            _map = MapBuilder.Build(layout, KitManifestData.Load(), root.transform);

            GameObject remoteRoot = new GameObject("RemoteOperators");
            remoteRoot.transform.SetParent(transform, false);
            _remoteRoot = remoteRoot.transform;

            GameObject hostiles = new GameObject("HostileViews");
            hostiles.transform.SetParent(transform, false);
            _hostileRoot = hostiles.transform;
        }

        private void BuildLocalOperator()
        {
            GameObject root = new GameObject("LocalOperatorRoot");
            root.transform.SetParent(transform, false);
            _local = LocalOperatorFactory.Build(root.transform, this, _database.Definitions,
                _loadout, 0xC00B5EEDUL, true, _map != null ? _map.Layout : null, true);
            _local.Weapons.ShotFired += shot => _director?.ShotServerRpc(shot.Origin, shot.Direction);

            // Grenades are server owned: the operation resolves them against operators
            // and militia alike, so the client only sends the intent.
            _local.Grenades.NetworkThrow = OnLocalGrenadeThrow;
            GameObject grenadeView = new GameObject("GrenadeView");
            grenadeView.transform.SetParent(transform, false);
            _grenadeView = grenadeView.AddComponent<ClientGrenadeView>();
            _grenadeView.Configure(_database.Definitions, _local);

            _local.Hud.SetObjective("awaiting briefing");
            _local.Body.Respawn(InsertionPoint(), 0f);
        }

        private bool OnLocalGrenadeThrow(GrenadeDefinition grenade, Vector3 origin, Vector3 direction,
            float fuseSeconds)
        {
            _director?.GrenadeThrowServerRpc(origin, direction, fuseSeconds);
            return true;
        }

        private Vector3 InsertionPoint() =>
            _map != null && _map.SpawnPoints.Count > 0
                ? _map.SpawnPoints[0].position + new Vector3(0f, 0.1f, 0f)
                : new Vector3(0f, 1.2f, 0f);

        private void SpawnDirector()
        {
            GameObject go = new GameObject("CoopDirector");
            go.transform.SetParent(transform, false);
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            _director = go.AddComponent<CoopMissionDirector>();
            networkObject.Spawn();
            _director.Configure(_database, _map);
            _director.Notice += message => Status?.Invoke(message);
        }

        private void Subscribe()
        {
            NetworkSessionEvents.Briefing += OnBriefing;
            NetworkSessionEvents.Notice += OnNotice;
            NetworkSessionEvents.State += OnState;
            NetworkSessionEvents.Hostiles += OnHostiles;
            NetworkSessionEvents.Mission += OnMission;
            NetworkSessionEvents.MissionEnded += OnMissionEnded;
            NetworkSessionEvents.HitConfirm += (damage, zone) => _local?.Hud.HitMarker();
            NetworkSessionEvents.KillFeed += line => _local?.Hud.Toast(line);
        }

        private void Update()
        {
            _network?.Tick();
            if (_local == null || _network == null || !_network.IsRunning)
            {
                return;
            }

            EnsureDirector();

            if (_local.Hud != null)
            {
                _local.Hud.SetHealth(_local.Body.Vitals.Health / PlayerVitals.MaxHealth);
                string line = string.IsNullOrEmpty(_objectiveTitle)
                    ? "operation silent kiln"
                    : _objectiveTitle;
                if (_holdSeconds > 0f)
                {
                    line += "   [" + Mathf.RoundToInt(Mathf.Min(_holdProgress, _holdSeconds)) +
                            " / " + Mathf.RoundToInt(_holdSeconds) + " s]";
                }

                _local.Hud.SetObjective(line);
                _local.Hud.SetMatchLine("CO-OP   ping " + _network.LastPingMs + " ms   " +
                                        (_ready ? "ready" : "awaiting briefing"));
            }

            if (!_joined)
            {
                return;
            }

            _frameTimer -= Time.deltaTime;
            if (_frameTimer > 0f)
            {
                return;
            }

            _frameTimer = FrameInterval;
            _director?.FrameServerRpc(_local.Body.transform.position,
                -_local.Body.transform.eulerAngles.y, _local.Rig != null ? _local.Rig.Pitch : 0f,
                _local.Body.Sprinting, _local.Body.Stance != Stance.Stand);

            if (GameInput.UsePressed && !_ready)
            {
                _ready = true;
                _director?.ReadyServerRpc();
            }
            else if (GameInput.UsePressed)
            {
                _director?.InteractServerRpc();
            }
        }

        private void EnsureDirector()
        {
            if (_director == null)
            {
                _director = FindObjectOfType<CoopMissionDirector>();
                if (_director == null)
                {
                    return;
                }

                _director.Notice += message => Status?.Invoke(message);
            }

            if (_joined || !_network.Manager.IsConnectedClient)
            {
                return;
            }

            _joinTimer -= Time.deltaTime;
            if (_joinTimer > 0f)
            {
                return;
            }

            _joinTimer = 1f;
            _joined = true;
            _director.JoinServerRpc(PlayerProfile.Name, _loadout.PrimaryWeaponId,
                _loadout.PrimaryMagazines.Count > 0 ? _loadout.PrimaryMagazines[0].MagazineId : "",
                _loadout.PrimaryMagazines.Count > 0 ? _loadout.PrimaryMagazines[0].Count : 0,
                _loadout.SecondaryWeaponId,
                _loadout.SecondaryMagazines.Count > 0 ? _loadout.SecondaryMagazines[0].MagazineId : "",
                _loadout.SecondaryMagazines.Count > 0 ? _loadout.SecondaryMagazines[0].Count : 0,
                _loadout.ArmorKitId, _loadout.GrenadeId, _loadout.GrenadeCount);
        }

        // ------------------------------------------------------------- callbacks

        private void OnBriefing(string missionName, string title, string text)
        {
            Status?.Invoke("briefing: " + title);
            _local?.Hud.SetObjective("BRIEFING - " + title + " (press F to acknowledge)");
            _local?.Hud.SetSubtitle(text);
        }

        private void OnNotice(string message)
        {
            Status?.Invoke(message);
            _local?.Hud.Toast(message);
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
                    if (dead[i] && !_local.Body.IsDead)
                    {
                        _local.Body.Vitals.ApplyDamage(new DamageResult
                        {
                            Accepted = true,
                            Damage = 999f,
                            Fatal = true,
                            Zone = HitZone.Thorax,
                        }, DeathCause.Gunfire);
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

        private void OnHostiles(Vector3[] positions, bool[] dead)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (!_hostileViews.TryGetValue(i, out RemoteOperatorView view))
                {
                    view = RemoteOperatorView.Create(_hostileRoot, 9000UL + (ulong)i, "Militia", true);
                    _hostileViews[i] = view;
                }

                view.gameObject.SetActive(!dead[i]);
                if (!dead[i])
                {
                    view.Apply(positions[i], 0f, false, false);
                }
            }
        }

        private void OnMission(MissionPhase phase, string title, string description, float progress,
            float holdSeconds, bool extractionAvailable, int failReason, float timeRemaining)
        {
            _objectiveTitle = title;
            _holdProgress = progress;
            _holdSeconds = holdSeconds;
            if (_local?.Hud != null)
            {
                _local.Hud.SetSubtitle(description);
                if (phase == MissionPhase.ObjectivesActive && extractionAvailable)
                {
                    _local.Hud.SetSubtitle(description + "\nextraction available at the pad");
                }
            }
        }

        private void OnMissionEnded(bool success, string reason, int hostilesRemoved,
            float durationSeconds, string debrief)
        {
            MatchResults results = new MatchResults
            {
                Reason = success ? MatchEndReason.KillTargetReached : MatchEndReason.ServerAborted,
                WinnerName = success ? "OPERATION COMPLETE" : "OPERATION FAILED",
                WinnerKills = hostilesRemoved,
                DurationSeconds = durationSeconds,
            };
            results.Standings = new List<ScoreboardRow>
            {
                new ScoreboardRow { DisplayName = debrief, Rank = 1 },
            };
            MatchEnded?.Invoke(results);
        }

        public void Shutdown()
        {
            _grenadeView?.Clear();
            NetworkSessionEvents.Clear();
            _network?.Shutdown();
            Destroy(gameObject);
        }

        private void OnDestroy() => NetworkSessionEvents.Clear();
    }

    /// <summary>Use/acknowledge input, read once per frame for the co-op session.</summary>
    internal static class GameInput
    {
        public static bool UsePressed =>
            Kordon.Game.Input.KordonInput.Pressed(Kordon.Core.Settings.InputAction.Interact);
    }
}
