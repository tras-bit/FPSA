using Kordon.Game.Match;
using UnityEngine;

namespace Kordon.Game.Runtime
{
    /// <summary>
    /// Entry point. KORDON builds itself from code (content, UI, world), so any
    /// scene - including the empty boot scene the build script creates - starts the
    /// game: the flow object is created once and survives scene loads.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindObjectOfType<GameFlow>() != null)
            {
                return;
            }

            GameObject go = new GameObject("GameFlow");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameFlow>();
        }
    }
}
