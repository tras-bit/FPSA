namespace Kordon.Core.Ammo
{
    /// <summary>
    /// Ammunition resupply policy for a match.  The design brief only requires that
    /// resupply between respawns inside the same round is forbidden; the exact
    /// respawn behaviour is a documented design decision
    /// (see docs/03_MAGAZINE_SYSTEM.md, "Resupply rules").
    /// </summary>
    public enum ResupplyRule
    {
        /// <summary>
        /// Default: you never regain ammunition during a life - no crates, no ground
        /// pickups, no ammo sharing.  When you respawn the locked kit is re-issued
        /// with the magazine types and counts you chose before the match.
        /// </summary>
        OnRespawnOnly = 0,

        /// <summary>
        /// Hardcore: the kit you planned is all the ammunition you get for the whole
        /// round, across every life.  Used by the co-op operation's default preset.
        /// </summary>
        NeverWithinRound = 1,

        /// <summary>Only a new round (rematch / next objective) re-issues the kit.</summary>
        BetweenRoundsOnly = 2
    }

    /// <summary>Tunable behaviour of the magazine state machine.</summary>
    public sealed class AmmoKitOptions
    {
        /// <summary>Seconds R must be held before a press turns into an inspection.</summary>
        public float HoldToInspectSeconds = 0.28f;

        /// <summary>Empty magazines stay in the pouch so the HUD can show them as red slots.</summary>
        public bool KeepEmptyMagazines = true;

        /// <summary>Partially spent magazines always return to the pouch (their rounds are never lost).</summary>
        public bool ReturnPartialMagazines = true;

        /// <summary>Which spare magazine is picked on reload.</summary>
        public MagazinePickPolicy PickPolicy = MagazinePickPolicy.NextNonEmpty;

        /// <summary>Fumbles are visible handling mistakes with a recovery animation, never silent misfires.</summary>
        public bool AllowFumbles = true;

        /// <summary>Resupply policy for the current match.</summary>
        public ResupplyRule Resupply = ResupplyRule.OnRespawnOnly;
    }

    /// <summary>How the next spare magazine is chosen when reloading.</summary>
    public enum MagazinePickPolicy
    {
        /// <summary>First magazine in pouch order that still has rounds (realistic, predictable).</summary>
        NextNonEmpty = 0,

        /// <summary>The fullest magazine in the pouch (consolidates ammunition).</summary>
        Fullest = 1
    }
}
