using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Ui;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The rule the HUD obeys: nothing ammo-like is on screen until a magazine has
    /// been pulled out of the weapon.  The pouch, the magazine icons and the exact
    /// round count appear together, and only while the magazine is in front of the
    /// operator's eyes.
    /// </summary>
    public sealed class HudAmmoPolicyTests
    {
        private static AmmoHudSnapshot Snapshot(bool inspecting, int? rounds, bool mayShow)
        {
            AmmoHudSnapshot snapshot = new AmmoHudSnapshot
            {
                IsInspecting = inspecting,
                InspectedRounds = rounds,
                InspectedCapacity = rounds.HasValue ? 30 : (int?)null,
                InspectedMagazineName = "VK-74 30 round",
            };
            if (mayShow)
            {
                snapshot.IsInspecting = true;
            }

            snapshot.Slots.Add(new MagazineHudSlot("m1", MagazineUiState.Full, true));
            snapshot.Slots.Add(new MagazineHudSlot("m2", MagazineUiState.Partial, false));
            snapshot.Slots.Add(new MagazineHudSlot("m3", MagazineUiState.Empty, false));
            return snapshot;
        }

        [Fact]
        public void PouchIsHiddenWhileTheWeaponIsInHand()
        {
            AmmoHudSnapshot inHands = Snapshot(false, null, false);

            Assert.False(HudAmmoPolicy.ShowPouch(inHands));
            Assert.Equal(0, HudAmmoPolicy.VisibleSlots(inHands));
            Assert.Equal(string.Empty, HudAmmoPolicy.PouchSummary(inHands));
            Assert.False(HudAmmoPolicy.ShowExactCount(inHands));
        }

        [Fact]
        public void PouchAppearsWithTheInspectedMagazine()
        {
            AmmoHudSnapshot inspecting = Snapshot(true, 12, true);

            Assert.True(HudAmmoPolicy.ShowPouch(inspecting));
            Assert.Equal(3, HudAmmoPolicy.VisibleSlots(inspecting));
            Assert.Equal("3 MAG", HudAmmoPolicy.PouchSummary(inspecting));
            Assert.True(HudAmmoPolicy.ShowExactCount(inspecting));
        }

        [Fact]
        public void ExactCountRequiresAMagazineInHand()
        {
            AmmoHudSnapshot counting = Snapshot(false, 12, false);

            // An inspecting flag without a magazine in hand, or a round count without
            // an inspection, must never leak a number to the screen.
            counting.IsInspecting = false;
            Assert.False(HudAmmoPolicy.ShowExactCount(counting));
            Assert.False(HudAmmoPolicy.ShowPouch(counting));
        }

        [Fact]
        public void NullSnapshotShowsNothing()
        {
            Assert.False(HudAmmoPolicy.ShowPouch(null));
            Assert.False(HudAmmoPolicy.ShowExactCount(null));
            Assert.Equal(0, HudAmmoPolicy.VisibleSlots(null));
            Assert.Equal(string.Empty, HudAmmoPolicy.PouchSummary(null));
        }

        [Fact]
        public void PartialInspectionNeverPrintsARoundCount()
        {
            // IsInspecting but no rounds read yet: the pouch can be on screen, the
            // number cannot.
            AmmoHudSnapshot partial = Snapshot(true, null, false);

            Assert.True(HudAmmoPolicy.ShowPouch(partial));
            Assert.False(HudAmmoPolicy.ShowExactCount(partial));
        }
    }
}
