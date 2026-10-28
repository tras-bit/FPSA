using System;
using Kordon.Core.Ammo;

namespace Kordon.Core.Rules
{
    /// <summary>
    /// Server side rule set for the main PvP mode.  Values come from
    /// Assets/_Project/Data/Definitions/match_rules.json and are replicated to clients
    /// so the UI can show the objective; the server is the only authority.
    /// </summary>
    [Serializable]
    public sealed class MatchRulesConfig
    {
        public MatchMode Mode = MatchMode.FreeForAll;

        /// <summary>Hard slot limit of the free-for-all match: 20 human operators.</summary>
        public int MaxPlayers = 20;

        /// <summary>Kills a single player has to reach, personally, to win the match.</summary>
        public int KillTarget = 50;

        public int MinPlayersToStartLive = 2;

        public float LoadoutPhaseSeconds = 45f;
        public float CountdownSeconds = 8f;

        /// <summary>0 = no time limit; the match ends when somebody reaches KillTarget.</summary>
        public float TimeLimitSeconds = 0f;

        public float RespawnDelaySeconds = 5f;
        public float SpawnProtectionSeconds = 2.5f;

        /// <summary>Grace period during which a dropped player keeps their score and slot.</summary>
        public float ReconnectGraceSeconds = 90f;

        /// <summary>
        /// Bots are for the offline practice range only.  A networked release match never
        /// fills empty slots with bots and never presents a bot as a human player.
        /// </summary>
        public bool AllowBots = false;
        public int MaxBotsInPractice = 19;

        /// <summary>Anti-cheat: no human can exceed this many eliminations per minute.</summary>
        public float MaxKillsPerMinute = 24f;

        /// <summary>Anti-cheat: minimum seconds between two registered eliminations.</summary>
        public float MinSecondsBetweenKills = 0.55f;

        public ResupplyRule Resupply = ResupplyRule.OnRespawnOnly;

        /// <summary>Score a kill for damaging but not finishing? No: only the finishing blow counts.</summary>
        public bool CreditAssists = false;

        public MatchRulesConfig Clone() => (MatchRulesConfig)MemberwiseClone();

        /// <summary>Default free-for-all rules shipped with the game.</summary>
        public static MatchRulesConfig DefaultFreeForAll() => new MatchRulesConfig
        {
            Mode = MatchMode.FreeForAll,
            MaxPlayers = 20,
            KillTarget = 50,
            MinPlayersToStartLive = 2,
            LoadoutPhaseSeconds = 45f,
            CountdownSeconds = 8f,
            TimeLimitSeconds = 0f,
            RespawnDelaySeconds = 5f,
            SpawnProtectionSeconds = 2.5f,
            ReconnectGraceSeconds = 90f,
            AllowBots = false
        };

        /// <summary>Offline practice range: same rules, bots allowed, lower kill target for quick loops.</summary>
        public static MatchRulesConfig PracticeRange()
        {
            MatchRulesConfig config = DefaultFreeForAll();
            config.Mode = MatchMode.PracticeRange;
            config.AllowBots = true;
            config.KillTarget = 15;
            config.MinPlayersToStartLive = 1;
            return config;
        }

        /// <summary>Sanity check so a bad config file cannot silently break the match.</summary>
        public string Validate()
        {
            if (MaxPlayers < 1 || MaxPlayers > 64)
            {
                return "max-players-out-of-range";
            }

            if (KillTarget < 1)
            {
                return "kill-target-must-be-positive";
            }

            if (Mode == MatchMode.FreeForAll && MaxPlayers != 20)
            {
                // The released free-for-all mode is specified for exactly 20 slots.
                return "ffa-requires-20-slots";
            }

            if (Mode == MatchMode.FreeForAll && KillTarget != 50)
            {
                return "ffa-requires-50-kills";
            }

            if (RespawnDelaySeconds < 0f)
            {
                return "negative-respawn-delay";
            }

            if (ReconnectGraceSeconds < 0f)
            {
                return "negative-reconnect-grace";
            }

            return null;
        }
    }
}
