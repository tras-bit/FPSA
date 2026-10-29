using Kordon.Core.Ammo;

namespace Kordon.Core.Ui
{
    /// <summary>
    /// What the HUD is allowed to reveal about ammunition, in one testable place.
    ///
    /// The rule the game is built on: the operator does not know what is in a pouch.
    /// Nothing ammo-like is on screen while the weapon is in the hands - no round
    /// count, no magazine icons, no "4 MAG" - until a magazine is physically out of
    /// the weapon and in front of the operator's eyes.  Then, and only then, the
    /// inspection shows the exact count and the pouch is on screen.
    /// </summary>
    public static class HudAmmoPolicy
    {
        /// <summary>True while a magazine has been pulled and is being looked at.</summary>
        public static bool ShowPouch(AmmoHudSnapshot snapshot) =>
            snapshot != null && snapshot.IsInspecting;

        /// <summary>True only for the magazine in the operator's hand.</summary>
        public static bool ShowExactCount(AmmoHudSnapshot snapshot) =>
            snapshot != null && snapshot.MayShowExactCount;

        /// <summary>Magazine icons appear with the pouch, never before it.</summary>
        public static int VisibleSlots(AmmoHudSnapshot snapshot) =>
            ShowPouch(snapshot) && snapshot.Slots != null ? snapshot.Slots.Count : 0;

        /// <summary>
        /// What to print next to a weapon on the loadout card.  Empty string means
        /// "say nothing": the player has to check the magazine to learn anything.
        /// </summary>
        public static string PouchSummary(AmmoHudSnapshot snapshot) =>
            ShowPouch(snapshot) && snapshot.Slots != null
                ? snapshot.Slots.Count + " MAG"
                : string.Empty;
    }
}
