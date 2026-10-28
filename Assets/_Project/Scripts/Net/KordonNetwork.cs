using System;
using System.Collections.Generic;
using Kordon.Core.Config;
using Kordon.Core.Loadout;
using Kordon.Game.Match;
using Kordon.Game.Ui;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// Transport owner.  Builds a Netcode for GameObjects NetworkManager in code,
    /// configures the Unity transport straight from net_config.json (address, port,
    /// tick rate, reconnect grace), and implements the connection lifecycle:
    /// connect, ping sampling, reconnect after a brief drop, leave.
    ///
    /// Nothing here contains a hard coded address: the host binds to what the
    /// configuration says and the client only ever connects to what the player or
    /// the region configuration supplied.  Loopback is refused unless the
    /// configuration explicitly allows development loopback.
    /// </summary>
    public sealed class KordonNetwork : MonoBehaviour
    {
        public const int ProtocolVersion = 1;

        public NetConfig Config { get; private set; }
        public NetworkRole Role { get; private set; } = NetworkRole.Offline;
        public bool IsRunning => _manager != null && _manager.IsListening;
        public int LastPingMs { get; private set; }
        public string Status { get; private set; } = "offline";
        public int ReconnectAttempts { get; private set; }

        public event Action<string> StatusChanged;
        public event Action<ulong> ClientJoined;
        public event Action<ulong> ClientLeft;

        private Unity.Netcode.NetworkManager _manager;
        private Unity.Netcode.Transports.UTP.UnityTransport _transport;
        private string _address;
        private int _port;
        private bool _hostMode;
        private float _nextReconnectAt;
        private float _pingTimer;
        private readonly List<ulong> _knownClients = new List<ulong>();

        public Unity.Netcode.NetworkManager Manager => _manager;

        /// <summary>Creates the manager and transport. Called once, before any Start*.</summary>
        public void Prepare(NetConfig config)
        {
            if (_manager != null)
            {
                return;
            }

            Config = config ?? NetConfig.Default();

            GameObject managerObject = new GameObject("KordonNetwork");
            managerObject.transform.SetParent(transform, false);
            _manager = managerObject.AddComponent<Unity.Netcode.NetworkManager>();
            _transport = managerObject.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            _manager.NetworkConfig.NetworkTransport = _transport;
            _manager.NetworkConfig.EnableSceneManagement = false;
            _manager.OnClientConnectedCallback += OnConnected;
            _manager.OnClientDisconnectCallback += OnDisconnected;
        }

        public bool StartHost(string bindAddress, int port)
        {
            Prepare(Config);
            _address = string.IsNullOrEmpty(bindAddress) ? "0.0.0.0" : bindAddress;
            _port = port > 0 ? port : Config.ServerPort;
            _hostMode = true;
            _transport.SetConnectionData(_address, (ushort)_port, _address);
            bool started = _manager.StartHost();
            Role = started ? NetworkRole.Host : NetworkRole.Offline;
            SetStatus(started
                ? "hosting " + _address + ":" + _port + " (" + Config.ServerTickRate + " Hz)"
                : "host failed to start");
            return started;
        }

        public bool StartDedicatedServer(string bindAddress, int port)
        {
            Prepare(Config);
            _address = string.IsNullOrEmpty(bindAddress) ? "0.0.0.0" : bindAddress;
            _port = port > 0 ? port : Config.ServerPort;
            _hostMode = true;
            _transport.SetConnectionData(_address, (ushort)_port, _address);
            bool started = _manager.StartServer();
            Role = started ? NetworkRole.DedicatedServer : NetworkRole.Offline;
            SetStatus(started ? "dedicated server on " + _address + ":" + _port : "server failed");
            return started;
        }

        /// <summary>Client connect. The address must come from the player or region config.</summary>
        public bool StartClient(string address, int port, string regionId)
        {
            Prepare(Config);
            if (string.IsNullOrEmpty(address))
            {
                SetStatus("no server address given");
                return false;
            }

            ClientConnectionRequest request = new ClientConnectionRequest
            {
                Address = address,
                Port = port > 0 ? port : Config.ServerPort,
                PlayerName = PlayerProfile.Name,
                RegionId = regionId ?? Config.DefaultRegionId,
            };
            ConnectionValidation validation = Config.ValidateClientRequest(request);
            if (!validation.Allowed)
            {
                SetStatus(validation.Reason);
                return false;
            }

            _address = request.Address;
            _port = request.Port;
            _hostMode = false;
            ReconnectAttempts = 0;
            _transport.SetConnectionData(_address, (ushort)_port);
            bool started = _manager.StartClient();
            Role = started ? NetworkRole.Client : NetworkRole.Offline;
            SetStatus(started ? "connecting to " + _address + ":" + _port : "could not start client");
            return started;
        }

        /// <summary>
        /// Retries a dropped connection within the server's grace window.  The
        /// server keeps the player's slot for ReconnectGraceSeconds, so a short
        /// network hiccup does not cost the match.
        /// </summary>
        public void Tick()
        {
            if (!IsRunning)
            {
                return;
            }

            _pingTimer -= Time.deltaTime;
            if (_pingTimer <= 0f)
            {
                _pingTimer = 1f;
                LastPingMs = SamplePing();
            }

            if (Role == NetworkRole.Client && !_manager.IsConnectedClient &&
                ReconnectAttempts < Config.MaxReconnectAttempts &&
                Time.time >= _nextReconnectAt)
            {
                ReconnectAttempts++;
                _nextReconnectAt = Time.time + Config.ReconnectRetryDelaySeconds;
                SetStatus("reconnecting (" + ReconnectAttempts + "/" +
                          Config.MaxReconnectAttempts + ")");
                _manager.Shutdown();
                _manager.StartClient();
            }
        }

        private int SamplePing()
        {
            if (_manager == null || !IsRunning)
            {
                return 0;
            }

            ulong peer = _manager.IsServer
                ? (_manager.LocalClientId == 0UL && _knownClients.Count > 0
                    ? _knownClients[0]
                    : _manager.LocalClientId)
                : _manager.NetworkConfig.NetworkTransport != null
                    ? _manager.NetworkConfig.NetworkTransport.ServerClientId
                    : 0UL;
            ulong rtt = _manager.NetworkConfig.NetworkTransport != null
                ? _manager.NetworkConfig.NetworkTransport.GetCurrentRtt(peer)
                : 0UL;
            return (int)rtt;
        }

        private void OnConnected(ulong clientId)
        {
            if (!_knownClients.Contains(clientId))
            {
                _knownClients.Add(clientId);
            }

            ReconnectAttempts = 0;
            SetStatus("connected (client " + clientId + ")");
            ClientJoined?.Invoke(clientId);
        }

        private void OnDisconnected(ulong clientId)
        {
            bool wasLocalClient = !_hostMode && clientId == _manager.LocalClientId;
            _knownClients.Remove(clientId);
            ClientLeft?.Invoke(clientId);

            if (Role == NetworkRole.Client)
            {
                _nextReconnectAt = Time.time + Config.ReconnectRetryDelaySeconds;
                SetStatus(wasLocalClient
                    ? "connection lost - reconnecting"
                    : "a player left");
                return;
            }

            SetStatus("player " + clientId + " disconnected");
        }

        public void Kick(ulong clientId, string reason)
        {
            SetStatus("disconnecting " + clientId + ": " + reason);
            _manager?.DisconnectClient(clientId);
        }

        public void Shutdown()
        {
            if (_manager != null)
            {
                _manager.OnClientConnectedCallback -= OnConnected;
                _manager.OnClientDisconnectCallback -= OnDisconnected;
                _manager.Shutdown();
            }

            _knownClients.Clear();
            Role = NetworkRole.Offline;
            SetStatus("offline");
        }

        private void SetStatus(string status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        private void OnDestroy() => Shutdown();
    }

    public enum NetworkRole
    {
        Offline = 0,
        Host = 1,
        DedicatedServer = 2,
        Client = 3,
    }
}
