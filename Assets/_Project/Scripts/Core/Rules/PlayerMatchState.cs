using System;
using Kordon.Core.Combat;

namespace Kordon.Core.Rules
{
    /// <summary>Server side record of one participant in a match.</summary>
    [Serializable]
    public sealed class PlayerMatchState
    {
        public ulong ClientId;
        public string DisplayName = string.Empty;

        /// <summary>Practice bots are always flagged; the UI shows them as BOTS and they never join a network match.</summary>
        public bool IsBot;

        /// <summary>Reconnect token issued at join; required to reclaim the slot after a drop.</summary>
        public string SessionToken = string.Empty;

        public bool IsConnected = true;
        public bool LeftPermanently;
        public float JoinTime;
        public float DisconnectTime = float.NaN;

        public int Kills;
        public int Deaths;
        public float DamageDealt;
        public float DamageTaken;
        public int ShotsFired;
        public int HitsLanded;
        public float LongestKillM;
        public int Headshots;
        public int PingMs;

        public bool IsAlive;
        public float RespawnAvailableAt = float.MaxValue;
        public float SpawnProtectionUntil;
        public int SpawnPointIndex = -1;

        public string LoadoutHash = string.Empty;
        public bool LoadoutLocked;
        public string SelectedLoadoutJson = string.Empty;

        public bool IsWinner;
        public DeathCause LastDeathCause = DeathCause.None;
        public HitZone LastFatalZone = HitZone.None;
        public ulong LastKilledBy;

        public float Accuracy => ShotsFired <= 0 ? 0f : (float)HitsLanded / ShotsFired;

        public bool IsInSpawnProtection(float now) => now < SpawnProtectionUntil;

        public bool IsInReconnectGrace(float now, MatchRulesConfig config)
        {
            if (IsConnected || LeftPermanently || float.IsNaN(DisconnectTime))
            {
                return false;
            }

            return now - DisconnectTime <= config.ReconnectGraceSeconds;
        }

        /// <summary>Scoreboard ordering: kills, then damage, then fewer deaths.</summary>
        public int CompareTo(PlayerMatchState other)
        {
            if (other == null)
            {
                return 1;
            }

            int byKills = other.Kills.CompareTo(Kills);
            if (byKills != 0)
            {
                return byKills;
            }

            int byDamage = other.DamageDealt.CompareTo(DamageDealt);
            if (byDamage != 0)
            {
                return byDamage;
            }

            int byDeaths = Deaths.CompareTo(other.Deaths);
            if (byDeaths != 0)
            {
                return byDeaths;
            }

            return string.CompareOrdinal(DisplayName, other.DisplayName);
        }

        public PlayerMatchState Clone()
        {
            PlayerMatchState copy = (PlayerMatchState)MemberwiseClone();
            return copy;
        }
    }

    /// <summary>One kill feed line, replicated to every client.</summary>
    [Serializable]
    public struct KillFeedEntry
    {
        public ulong KillerId;
        public string KillerName;
        public ulong VictimId;
        public string VictimName;
        public string WeaponId;
        public HitZone Zone;
        public float DistanceM;
        public float ServerTime;
        public bool KillerIsBot;
        public bool VictimIsBot;
        public bool WasHeadshot;

        public KillFeedEntry(ulong killerId, string killerName, ulong victimId, string victimName,
            string weaponId, HitZone zone, float distanceM, float serverTime, bool killerIsBot, bool victimIsBot)
        {
            KillerId = killerId;
            KillerName = killerName;
            VictimId = victimId;
            VictimName = victimName;
            WeaponId = weaponId;
            Zone = zone;
            DistanceM = distanceM;
            ServerTime = serverTime;
            KillerIsBot = killerIsBot;
            VictimIsBot = victimIsBot;
            WasHeadshot = zone.IsVitalZone();
        }
    }
}
