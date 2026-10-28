// Unity Netcode for GameObjects 2.2.0 stub surface for the compile check.
// Only the members KORDON actually uses are declared, but each one carries the
// real signature from com.unity.netcode.gameobjects@2.2.0, so the network layer
// is type checked in CI even though Unity itself cannot run there.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Netcode
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute
    {
        public bool RequireOwnership = true;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute
    {
    }

    public struct ServerRpcReceiveParams
    {
        public ulong SenderClientId;
    }

    public class ServerRpcParams
    {
        public ServerRpcReceiveParams Receive = new ServerRpcReceiveParams();
    }

    public struct ClientRpcSendParams
    {
        public IReadOnlyList<ulong> TargetClientIds;
    }

    public class ClientRpcParams
    {
        public ClientRpcSendParams Send = new ClientRpcSendParams();
    }

    public abstract class NetworkBehaviour : MonoBehaviour
    {
        public NetworkObject NetworkObject => GetComponent<NetworkObject>();
        public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        public bool IsClient => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        public bool IsOwner => NetworkObject != null && NetworkManager.Singleton != null &&
                               NetworkObject.OwnerClientId == NetworkManager.Singleton.LocalClientId;
        public bool IsSpawned => NetworkObject != null && NetworkObject.IsSpawned;
        public ulong OwnerClientId => NetworkObject != null ? NetworkObject.OwnerClientId : 0UL;

        public virtual void OnNetworkSpawn()
        {
        }

        public virtual void OnNetworkDespawn()
        {
        }
    }

    public sealed class NetworkObject : MonoBehaviour
    {
        public ulong OwnerClientId { get; internal set; }
        public bool IsSpawned { get; internal set; }
        public bool IsPlayerObject { get; internal set; }

        public void Spawn(bool destroyWithScene = false)
        {
        }

        public void SpawnAsPlayerObject(ulong clientId, bool destroyWithScene = false)
        {
        }

        public void SpawnWithOwnership(ulong clientId, bool destroyWithScene = false)
        {
        }

        public void Despawn(bool destroy = true)
        {
        }
    }

    public enum NetworkVariableReadPermission
    {
        Everyone = 0,
        Owner = 1,
    }

    public enum NetworkVariableWritePermission
    {
        Server = 0,
        Owner = 1,
    }

    public class NetworkVariable<T>
    {
        public delegate void OnValueChangedDelegate(T previousValue, T newValue);

        public NetworkVariable(T value = default,
            NetworkVariableReadPermission readPerm = NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission writePerm = NetworkVariableWritePermission.Server)
        {
            Value = value;
        }

        public T Value { get; set; }

        public OnValueChangedDelegate OnValueChanged;
    }

    public struct ConnectionApprovalResponse
    {
        public bool Approved;
        public string Reason;
        public bool CreatePlayerObject;
        public bool Pending;
    }

    public class NetworkTransport : MonoBehaviour
    {
        public ulong ServerClientId => 0UL;

        public ulong GetCurrentRtt(ulong clientId) => 0UL;

        public void Shutdown()
        {
        }
    }

    public class NetworkConfig
    {
        public NetworkTransport NetworkTransport { get; set; }
        public bool EnableSceneManagement { get; set; } = true;
        public bool ForceSamePrefabs { get; set; } = true;
        public int ClientConnectionBufferTimeout { get; set; } = 10;
    }

    public sealed class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Singleton { get; private set; }

        public NetworkConfig NetworkConfig { get; } = new NetworkConfig();
        public bool IsServer { get; private set; }
        public bool IsClient { get; private set; }
        public bool IsHost => IsServer && IsClient;
        public bool IsListening { get; private set; }
        public bool IsConnectedClient { get; private set; }
        public ulong LocalClientId { get; private set; }

        public event Action<ulong> OnClientConnectedCallback;
        public event Action<ulong> OnClientDisconnectCallback;

        public bool StartHost() => StartServer();

        public bool StartServer()
        {
            Singleton = this;
            IsServer = true;
            IsListening = true;
            return true;
        }

        public bool StartClient()
        {
            Singleton = this;
            IsClient = true;
            IsListening = true;
            IsConnectedClient = true;
            return true;
        }

        public void Shutdown(bool discardMessageQueue = false)
        {
            IsServer = false;
            IsClient = false;
            IsListening = false;
        }

        public void DisconnectClient(ulong clientId)
        {
        }

        public bool AddNetworkPrefab(GameObject prefab) => true;

        internal void RaiseConnected(ulong clientId) => OnClientConnectedCallback?.Invoke(clientId);

        internal void RaiseDisconnected(ulong clientId) => OnClientDisconnectCallback?.Invoke(clientId);
    }
}

namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : NetworkTransport
    {
        public string ConnectionData { get; set; }

        public void SetConnectionData(string ipv4Address, ushort port, string listenAddress = null)
        {
        }

        public void SetConnectionData(string ipv4Address, ushort port)
        {
        }

        public void SetDebugSimulatorParameters(int packetDelay, int packetJitter, int dropRate)
        {
        }
    }
}
