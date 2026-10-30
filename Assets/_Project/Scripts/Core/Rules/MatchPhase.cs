namespace Kordon.Core.Rules
{
    /// <summary>Life cycle of a match, driven entirely by the server.</summary>
    public enum MatchPhase
    {
        None = 0,

        /// <summary>Players are connecting; the match has not been configured yet.</summary>
        Lobby = 1,

        /// <summary>Loadout screen: choose weapons, sights and the magazine plan.</summary>
        Loadout = 2,

        /// <summary>Kits are locked, everyone is shown the final loadout, countdown runs.</summary>
        Countdown = 3,

        /// <summary>Match is live.</summary>
        Live = 4,

        /// <summary>Somebody reached the kill target or the time limit expired.</summary>
        Finished = 5,

        /// <summary>Server aborted the match (everybody left, fatal error).</summary>
        Aborted = 6
    }

    public static class MatchPhaseExtensions
    {
        /// <summary>Whether the loadout screen accepts changes in this phase.</summary>
        public static bool AllowsLoadoutChanges(this MatchPhase phase) =>
            phase == MatchPhase.Lobby || phase == MatchPhase.Loadout;

        public static bool IsPlayable(this MatchPhase phase) =>
            phase == MatchPhase.Live;

        public static bool IsTerminal(this MatchPhase phase) =>
            phase == MatchPhase.Finished || phase == MatchPhase.Aborted;
    }

    /// <summary>Why a match ended.</summary>
    public enum MatchEndReason
    {
        None = 0,
        KillTargetReached = 1,
        TimeLimitReached = 2,
        ServerAborted = 3,
        AllPlayersLeft = 4
    }

    /// <summary>Game mode. Free-for-all has no teams at all - every player is alone.</summary>
    public enum MatchMode
    {
        None = 0,

        /// <summary>20 independent operators, first to the kill target wins.</summary>
        FreeForAll = 1,

        /// <summary>Co-operative operation against AI (see Kordon.Core.Coop).</summary>
        CoOpOperation = 2,

        /// <summary>Offline training range with clearly labelled AI targets.</summary>
        PracticeRange = 3,

        /// <summary>
        /// Two teams, one charge, round based scoring and a cash economy between rounds.
        /// It has its own controller (BombMatchController) and its own rules (BombRules);
        /// the free-for-all controller is untouched by it.
        /// </summary>
        Demolition = 4
    }
}
