using System;
using Kordon.Core.Rules;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// The single hop between network callbacks and the game layer.  RPC receivers
    /// push here, the online session subscribes; nothing else touches these events,
    /// so there is exactly one path for replicated state into the UI.
    /// </summary>
    public static class NetworkSessionEvents
    {
        public static event Action<int, float> Welcome;
        public static event Action<string> Notice;
        public static event Action<Vector3, int> Respawn;
        public static event Action<string, int, int, bool> InspectResult;
        public static event Action<float, string> HitConfirm;
        public static event Action<string> KillFeed;
        public static event Action<int, ulong, string, int, float> MatchEnded;
        public static event Action<ulong[], int[], int[], string[], int[], MatchPhase> Scores;
        public static event Action<ulong[], Vector3[], float[], int[], bool[]> State;

        public static void RaiseWelcome(int killTarget, float serverTime) =>
            Welcome?.Invoke(killTarget, serverTime);

        public static void RaiseNotice(string message) => Notice?.Invoke(message);

        public static void RaiseRespawn(Vector3 position, int protectionSeconds) =>
            Respawn?.Invoke(position, protectionSeconds);

        public static void RaiseInspect(string magazineName, int rounds, int capacity,
            bool hasReserve) => InspectResult?.Invoke(magazineName, rounds, capacity, hasReserve);

        public static void RaiseHitConfirm(float damage, string zone) =>
            HitConfirm?.Invoke(damage, zone);

        public static void RaiseKillFeed(string line) => KillFeed?.Invoke(line);

        public static void RaiseMatchEnded(int reason, ulong winnerId, string winnerName,
            int winnerKills, float durationSeconds) =>
            MatchEnded?.Invoke(reason, winnerId, winnerName, winnerKills, durationSeconds);

        public static void RaiseScore(ulong[] ids, int[] kills, int[] deaths, string[] names,
            int[] pings, MatchPhase phase) =>
            Scores?.Invoke(ids, kills, deaths, names, pings, phase);

        public static void RaiseState(ulong[] ids, Vector3[] positions, float[] yaws,
            int[] health, bool[] dead) => State?.Invoke(ids, positions, yaws, health, dead);

        public static void Clear()
        {
            Welcome = null;
            Notice = null;
            Respawn = null;
            InspectResult = null;
            HitConfirm = null;
            KillFeed = null;
            MatchEnded = null;
            Scores = null;
            State = null;
        }
    }
}
