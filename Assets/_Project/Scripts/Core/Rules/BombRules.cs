using System;

namespace Kordon.Core.Rules
{
    /// <summary>Which side of a demolition round a player is on.</summary>
    public enum Team
    {
        None = 0,

        /// <summary>Carries the charge, wins the round by detonating it or by wiping the defenders.</summary>
        Attackers = 1,

        /// <summary>Defends the sites, wins by defusing, by wiping the attackers or by running the clock out.</summary>
        Defenders = 2
    }

    /// <summary>Phase of a single round. Freeze time is the buying window.</summary>
    public enum RoundPhase
    {
        None = 0,

        /// <summary>Buy window: nobody moves, the buy menu is open, the clock has not started.</summary>
        Freeze = 1,

        /// <summary>Live round, the charge is not planted.</summary>
        Live = 2,

        /// <summary>The charge is armed and its own clock is running.</summary>
        Planted = 3,

        /// <summary>The round is decided; scores have been paid out, the next round has not started.</summary>
        RoundOver = 4,

        /// <summary>The match is decided.</summary>
        MatchOver = 5
    }

    /// <summary>Why the round ended.</summary>
    public enum RoundEndReason
    {
        None = 0,

        /// <summary>The charge went off: attackers take the round.</summary>
        BombExploded = 1,

        /// <summary>A defender cut the wire: defenders take the round.</summary>
        BombDefused = 2,

        /// <summary>Every attacker is dead before the plant: defenders take the round.</summary>
        AttackersEliminated = 3,

        /// <summary>Every defender is dead: attackers take the round.</summary>
        DefendersEliminated = 4,

        /// <summary>Round clock ran out with no plant: defenders take the round.</summary>
        TimeExpired = 5
    }

    /// <summary>
    /// Rules for the demolition mode: two teams, one charge, round based scoring.
    ///
    /// This is a separate rule set from the free-for-all controller on purpose. The
    /// free-for-all mode has no teams, no rounds and no respawn during a round, and
    /// bending it into a two-team objective mode would have put both rule sets at risk
    /// of drifting; the two controllers share the data (weapons, maps, damage) but not
    /// the state machine.
    /// </summary>
    [Serializable]
    public sealed class BombRules
    {
        public MatchMode Mode = MatchMode.Demolition;

        /// <summary>5 v 5: the classic demolition size, and the map is authored for it.</summary>
        public int MaxPlayers = 10;

        public int TeamSize = 5;

        /// <summary>First side to this many rounds takes the match (MR12: 13 of 24).</summary>
        public int RoundsToWin = 13;

        /// <summary>Rounds in the first half before the sides swap.</summary>
        public int RoundsPerHalf = 12;

        /// <summary>Freeze time at the top of a round: buying is allowed, movement is not.</summary>
        public float FreezeSeconds = 15f;

        /// <summary>Live round clock. The charge is not planted yet.</summary>
        public float RoundSeconds = 115f;

        /// <summary>Charge clock once it is armed. Short enough that a plant has to be defended.</summary>
        public float BombSeconds = 40f;

        /// <summary>How long the planter holds the interact key.</summary>
        public float PlantSeconds = 3.2f;

        /// <summary>Defuse without a kit. With a kit, half of it.</summary>
        public float DefuseSeconds = 10f;
        public float DefuseKitSeconds = 5f;

        /// <summary>Buying is only allowed during this many seconds from the start of the round.</summary>
        public float BuySeconds = 20f;

        /// <summary>Radius of a buy zone around the team's spawn, in metres.</summary>
        public float BuyZoneRadius = 14f;

        /// <summary>How long the round result is shown before the next round starts.</summary>
        public float RoundOverSeconds = 6f;

        /// <summary>Cash everybody starts the match with.</summary>
        public int StartingMoney = 800;

        /// <summary>Cash ceiling. Money is not cosmetic: it is the mode's progression.</summary>
        public int MaxMoney = 16000;

        /// <summary>Bots are for practice only, exactly as in the free-for-all rules.</summary>
        public bool AllowBots = false;

        /// <summary>Radius in which a plant is legal, around the site centre.</summary>
        public float SiteRadius = 9f;

        public BombRules Clone() => (BombRules)MemberwiseClone();

        public static BombRules Default() => new BombRules();

        /// <summary>Practice preset: bots may fill the other side, but they are labelled bots.</summary>
        public static BombRules Practice()
        {
            BombRules rules = Default();
            rules.AllowBots = true;
            rules.TeamSize = 5;
            rules.MaxPlayers = 10;
            return rules;
        }

        /// <summary>Sanity check so a bad config cannot silently break a round.</summary>
        public string Validate()
        {
            if (Mode != MatchMode.Demolition)
            {
                return "demolition-rules-carries-another-mode";
            }

            if (TeamSize < 1 || TeamSize > 16 || MaxPlayers != TeamSize * 2)
            {
                return "demolition-slots-must-match-two-teams";
            }

            if (RoundsToWin < 1 || RoundsToWin * 2 - 1 < RoundsPerHalf)
            {
                return "demolition-round-count-is-inconsistent";
            }

            if (FreezeSeconds < 0f || RoundSeconds <= 0f || BombSeconds <= 0f)
            {
                return "demolition-negative-clock";
            }

            if (PlantSeconds <= 0f || DefuseSeconds <= PlantSeconds)
            {
                // A defuse that is faster than a plant would make every retake trivially
                // winnable, which is the opposite of what the mode is for.
                return "demolition-defuse-faster-than-plant";
            }

            if (DefuseKitSeconds <= 0f || DefuseKitSeconds > DefuseSeconds)
            {
                return "demolition-kit-must-be-faster";
            }

            if (StartingMoney < 0 || MaxMoney < StartingMoney)
            {
                return "demolition-money-out-of-range";
            }

            if (SiteRadius <= 0f || BuyZoneRadius <= 0f)
            {
                return "demolition-radius-must-be-positive";
            }

            return null;
        }
    }
}
