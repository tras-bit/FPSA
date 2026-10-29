using System;
using Kordon.Core.Config;
using Kordon.Core.Data;
using Kordon.Core.Rules;
using Kordon.Core.Utility;
using Kordon.Game.Maps;
using Unity.Netcode;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// The dedicated server: no menu, no local operator, no camera.  It builds the map
    /// (so hitscan, grenade bounces and nav have real colliders), brings the transport
    /// up on the configured port, spawns the authoritative match director and then
    /// only reports state.  Everything that decides the match is already server side,
    /// so this process is the whole back end - start it with:
    ///
    ///   Kordon.exe -kordon-server -serverAddress 0.0.0.0 -serverPort 27015
    /// </summary>
    public sealed class DedicatedServerHost : MonoBehaviour
    {
        public ServerLaunchOptions Options { get; private set; }
        public KordonNetwork Network { get; private set; }
        public NetworkMatchDirector Director { get; private set; }
        public BuiltMap Map { get; private set; }
        public bool Ready { get; private set; }

        private GameDatabase _database;
        private float _reportTimer = 10f;

        public void Begin(ServerLaunchOptions options)
        {
            Options = options ?? new ServerLaunchOptions();

            _database = GameDatabase.Load(ContentPaths.DataRoot);
            if (Options.MaxPlayers > 0)
            {
                Debug.Log("[kordon-server] max players requested: " + Options.MaxPlayers);
            }

            GameObject world = new GameObject("ServerWorld");
            world.transform.SetParent(transform, false);
            MapLayout layout = MapLayout.FromJson(
                ContentPaths.Read(ContentPaths.MapLayout(Options.MapId)));
            Map = MapBuilder.Build(layout, KitManifestData.Load(), world.transform);
            Debug.Log("[kordon-server] map '" + Options.MapId + "' built: " +
                      Map.SpawnPoints.Count + " spawns");

            GameObject networkObject = new GameObject("Network");
            networkObject.transform.SetParent(transform, false);
            Network = networkObject.AddComponent<KordonNetwork>();
            Network.StatusChanged += message => Debug.Log("[kordon-server] " + message);

            int port = Options.Port > 0 ? Options.Port : _database.Net.ServerPort;
            if (!Network.StartDedicatedServer(Options.BindAddress, port))
            {
                Debug.LogError("[kordon-server] transport failed to start on " +
                               Options.BindAddress + ":" + port);
                return;
            }

            GameObject directorObject = new GameObject("MatchDirector");
            directorObject.transform.SetParent(transform, false);
            NetworkObject networkId = directorObject.AddComponent<NetworkObject>();
            Director = directorObject.AddComponent<NetworkMatchDirector>();
            networkId.Spawn();
            Director.Configure(_database, Map, new DeterministicRandom(0xBEEF5EEDUL));
            Director.Notice += message => Debug.Log("[kordon-server] " + message);
            Director.MatchEnded += results => Debug.Log(string.Format(
                "[kordon-server] match over: {0} wins with {1} eliminations ({2} s)",
                results.WinnerName, results.WinnerKills, results.DurationSeconds));

            Ready = true;
            Debug.Log("[kordon-server] ready - " + Options.BindAddress + ":" + port +
                      "  (first to " + Director.Match.Config.KillTarget + " kills)");
            if (Options.HasUnknownArguments)
            {
                Debug.LogWarning("[kordon-server] unknown arguments: " +
                                 string.Join(" ", Options.UnknownArguments));
            }
        }

        private void Update()
        {
            Network?.Tick();
            if (!Ready || Director?.Match == null)
            {
                return;
            }

            _reportTimer -= Time.deltaTime;
            if (_reportTimer > 0f)
            {
                return;
            }

            _reportTimer = 15f;
            FfaMatchController match = Director.Match;
            Debug.Log(string.Format(
                "[kordon-server] {0}   operators {1}   target {2}   uptime {3:F0} s",
                match.Phase, match.Roster.Count, match.Config.KillTarget, Time.time));
        }

        private void OnApplicationQuit()
        {
            Network?.Shutdown();
        }
    }

    /// <summary>
    /// Starts the dedicated server before anything else when the process was launched
    /// with <c>-kordon-server</c>.  A normal player build is untouched: without the flag
    /// this does nothing at all.
    /// </summary>
    public static class DedicatedServerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            ServerLaunchOptions options =
                ServerLaunchOptions.Parse(Environment.GetCommandLineArgs());
            if (!options.Enabled)
            {
                return;
            }

            if (UnityEngine.Object.FindFirstObjectByType<DedicatedServerHost>() != null)
            {
                return;
            }

            GameObject host = new GameObject("KordonDedicatedServer");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<DedicatedServerHost>().Begin(options);
        }
    }
}
