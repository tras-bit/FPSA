using System;
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
            if (IsDedicatedServer())
            {
                // The server has its own bootstrap (Kordon.Net.DedicatedServerBootstrap):
                // no menu, no UI, no local operator in this process.
                return;
            }

            if (UnityEngine.Object.FindObjectOfType<GameFlow>() != null)
            {
                return;
            }

            GameObject go = new GameObject("GameFlow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameFlow>();
        }

        /// <summary>Mirror of the Net launch flag, so the menu never comes up on a server.</summary>
        private static bool IsDedicatedServer()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], "-kordon-server", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
