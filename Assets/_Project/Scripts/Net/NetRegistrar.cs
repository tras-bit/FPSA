using Kordon.Game.Match;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// Registers the online session with the game layer.  The Net assembly depends
    /// on Game (not the other way round), so the connector is injected from here as
    /// soon as the flow object exists - GameFlow and every screen stay transport
    /// agnostic and the assemblies keep a clean, one way dependency.
    /// </summary>
    public sealed class NetRegistrar : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<NetRegistrar>() != null)
            {
                return;
            }

            GameObject go = new GameObject("NetRegistrar");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<NetRegistrar>();
        }

        private void Update()
        {
            GameFlow flow = GameFlow.Instance;
            if (flow == null)
            {
                return;
            }

            if (flow.ConnectorFactory == null)
            {
                flow.ConnectorFactory = CreateSession;
            }

            Destroy(gameObject);
        }

        private static IMatchConnector CreateSession()
        {
            GameObject go = new GameObject("NetworkSession");
            Object.DontDestroyOnLoad(go);
            return go.AddComponent<NetworkMatchSession>();
        }
    }
}
