using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kordon.Core.Ammo;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The mandatory magazine system: tap R reloads, holding R extracts and inspects the
    /// magazine (and only then shows an exact count), no permanent round counter exists,
    /// the strip is white / yellow / red, and there is no resupply inside a life.
    /// </summary>
    public class MagazineSystemTests
    {
        private static readonly DefinitionLibrary Library = TestData.Database.Definitions;

        private static AmmoKitOptions DeterministicOptions(ResupplyRule resupply = ResupplyRule.OnRespawnOnly) =>
            new AmmoKitOptions
            {
                AllowFumbles = false,
                HoldToInspectSeconds = 0.28f,
                Resupply = resupply
            };

        private static AmmoKit CreateRifleKit(int magazines, AmmoKitOptions options = null,
            string magazineId = "mag_545_30_ps")
        {
            WeaponDefinition weapon = Library.GetWeapon("vk74");
            Assert.NotNull(weapon);
            List<MagazinePlanEntry> plan = new List<MagazinePlanEntry> { new MagazinePlanEntry(magazineId, magazines) };
            return new AmmoKit(weapon, plan, Library, options ?? DeterministicOptions(),
                new DeterministicRandom(0xABCD));
        }

        private static void FireRounds(AmmoKit kit, int count, ref float now)
        {
            for (int i = 0; i < count; i++)
            {
                // Never ask the kit to fire faster than the weapon allows: the rate
                // limit is a hard rule, not a suggestion.
                now += Math.Max(0.2f, kit.Weapon.SecondsPerShot + 0.05f);
                FireAttempt attempt = kit.TryFire(now);
                Assert.True(attempt.Fired, $"shot {i} denied: {attempt.Denial}");
            }
        }

        private static List<AmmoEventKind> Drain(AmmoKit kit)
        {
            List<AmmoEvent> events = new List<AmmoEvent>();
            kit.DrainEvents(events);
            return events.Select(e => e.Kind).ToList();
        }

        [Fact]
        public void FirstMagazineStartsChamberedAndTheRestGoToThePouch()
        {
            AmmoKit kit = CreateRifleKit(5);
            Assert.Equal(5, kit.MagazineCount);
            Assert.NotNull(kit.Chambered);
            Assert.Equal(30, kit.Chambered.Rounds);
            Assert.Equal(4, kit.Pouch.Count);
            Assert.True(kit.RoundChambered);
            Assert.Equal(MagazineUiState.Full, kit.Chambered.UiState);
        }

        [Fact]
        public void TapR_ReloadsAndReturnsThePartialMagazineToThePouch()
        {
            AmmoKit kit = CreateRifleKit(5);
            float now = 10f;
            FireRounds(kit, 10, ref now);
            Assert.Equal(20, kit.Chambered.Rounds);
            Assert.Equal(MagazineUiState.Partial, kit.Chambered.UiState);

            kit.PressReloadKey(now);
            Assert.Equal(AmmoInteraction.PendingDecision, kit.State);
            kit.ReleaseReloadKey(now + 0.05f);

            Assert.Equal(AmmoInteraction.Reloading, kit.State);
            Assert.Equal(ReloadType.Tactical, kit.CurrentReloadType);
            Assert.Equal(now + 0.05f + 2.35f, kit.StateEndsAt, 3);

            // Firing is refused for the whole reload, never silently.
            FireAttempt denied = kit.TryFire(now + 0.2f);
            Assert.False(denied.Fired);
            Assert.Equal(FireDenial.Reloading, denied.Denial);

            List<AmmoEventKind> mid = Drain(kit);
            kit.Update(now + 0.05f + 2.35f * 0.70f + 0.01f);
            Assert.Equal(30, kit.Chambered.Rounds);
            Assert.Contains(AmmoEventKind.MagazineIn, Drain(kit));

            kit.Update(now + 0.05f + 2.35f + 0.01f);
            Assert.Equal(AmmoInteraction.Idle, kit.State);
            Assert.True(kit.RoundChambered);

            // The partly spent magazine is kept, not deleted: rounds are never invented.
            Assert.Single(kit.Pouch.Where(m => m.Rounds == 20));
            Assert.Equal(1, kit.ReloadCount);
            // 150 issued - 10 fired = 140, split as one full magazine in the weapon,
            // three full ones in the pouch and the returned partial.
            Assert.Equal(140, kit.TotalRoundsServerOnly);

            FireAttempt after = kit.TryFire(now + 5f);
            Assert.True(after.Fired);
            Assert.Equal(29, after.RoundsLeftInMagazine);
        }

        [Fact]
        public void DryReload_IsSlowerAndEndsWithABoltRelease()
        {
            AmmoKit kit = CreateRifleKit(3);
            float now = 0f;
            FireRounds(kit, 30, ref now);
            Assert.Equal(0, kit.Chambered.Rounds);
            Assert.False(kit.RoundChambered);
            Assert.Contains(AmmoEventKind.LastRound, Drain(kit));

            FireAttempt empty = kit.TryFire(now + 1f);
            Assert.False(empty.Fired);
            Assert.Equal(FireDenial.WeaponEmpty, empty.Denial);
            Assert.Contains(AmmoEventKind.DryFireClick, Drain(kit));

            kit.PressReloadKey(now + 2f);
            kit.ReleaseReloadKey(now + 2.04f);
            Assert.Equal(ReloadType.Dry, kit.CurrentReloadType);
            Assert.Equal(now + 2.04f + 2.95f, kit.StateEndsAt, 3);

            Drain(kit);
            kit.Update(now + 2.04f + 2.95f + 0.01f);
            List<AmmoEventKind> events = Drain(kit);
            Assert.Contains(AmmoEventKind.BoltRelease, events);
            Assert.Contains(AmmoEventKind.ReloadComplete, events);
            Assert.Equal(30, kit.Chambered.Rounds);
            Assert.True(kit.RoundChambered);
            Assert.Single(kit.Pouch.Where(m => m.UiState == MagazineUiState.Empty));
        }

        [Fact]
        public void HoldR_ExtractsAndInspectsTheMagazineAndShowsTheExactCount()
        {
            AmmoKit kit = CreateRifleKit(4);
            float now = 5f;
            FireRounds(kit, 7, ref now);
            Assert.Equal(23, kit.Chambered.Rounds);

            // Before the inspection the exact number is nowhere to be found.
            AmmoHudSnapshot hidden = kit.BuildHudSnapshot(now);
            Assert.Null(hidden.InspectedRounds);
            Assert.Null(hidden.InspectedCapacity);
            Assert.False(hidden.MayShowExactCount);
            Assert.All(hidden.Slots, slot => Assert.False(slot.ShowsExactCount));

            kit.PressReloadKey(now);
            Assert.Equal(AmmoInteraction.PendingDecision, kit.State);
            Assert.False(kit.MayShowExactCount);

            // Held past the threshold: the magazine comes out for inspection.
            kit.Update(now + 0.30f);
            Assert.Equal(AmmoInteraction.Inspecting, kit.State);
            Assert.True(kit.MayShowExactCount);
            Assert.Equal(23, kit.InspectedRoundCount);
            Assert.Equal("30-rnd 5.45 box (PS)", kit.InspectedMagazineName);
            Assert.Equal(1, kit.InspectCount);

            // You cannot fire with the magazine in your hand.
            FireAttempt denied = kit.TryFire(now + 0.35f);
            Assert.False(denied.Fired);
            Assert.Equal(FireDenial.Inspecting, denied.Denial);

            AmmoHudSnapshot shown = kit.BuildHudSnapshot(now + 0.5f);
            Assert.True(shown.IsInspecting);
            Assert.Equal(23, shown.InspectedRounds);
            Assert.Equal(30, shown.InspectedCapacity);
            Assert.True(shown.Slots[0].ShowsExactCount);
            Assert.Equal(23, shown.Slots[0].ExactRounds);
            Assert.False(shown.Slots[1].ShowsExactCount);

            // Inspection changes nothing about the ammunition.
            Assert.Equal(23, kit.Chambered.Rounds);
            Assert.Equal(3, kit.Pouch.Count);

            kit.ReleaseReloadKey(now + 1.4f);
            Assert.Equal(AmmoInteraction.ReturningFromInspect, kit.State);
            Assert.False(kit.MayShowExactCount);
            Assert.Equal(FireDenial.Inspecting, kit.TryFire(now + 1.5f).Denial);

            kit.Update(now + 1.4f + 0.85f + 0.01f);
            Assert.Equal(AmmoInteraction.Idle, kit.State);
            Assert.True(kit.TryFire(now + 3f).Fired);
        }

        [Fact]
        public void InspectReadableEvent_FiresPartWayThroughTheInspection()
        {
            AmmoKit kit = CreateRifleKit(2);
            kit.PressReloadKey(0f);
            kit.Update(0.30f);
            Assert.Equal(AmmoInteraction.Inspecting, kit.State);
            Drain(kit);

            kit.Update(0.30f + 1.15f * 0.45f + 0.01f);
            Assert.Contains(AmmoEventKind.InspectReadable, Drain(kit));
        }

        [Fact]
        public void HudSnapshot_NeverExposesATotalRoundCount()
        {
            // Contract check: the HUD type has no member that could render "187 rounds".
            foreach (MemberInfo member in typeof(AmmoHudSnapshot)
                         .GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                string name = member.Name;
                if (name == "MayShowExactCount" || name == "get_MayShowExactCount")
                {
                    continue;
                }

                Assert.DoesNotContain("Total", name);
                // "HasReserveAmmunition" is a yes/no flag and is allowed; a reserve
                // round count would not be.
                Assert.DoesNotContain("ReserveRounds", name);
                Assert.DoesNotContain("ReserveCount", name);
                Assert.DoesNotContain("AmmoCount", name);
                Assert.DoesNotContain("RoundCount", name);
                Assert.DoesNotContain("RoundsRemaining", name);
            }

            // The only exact numbers are the inspection pair, and they stay null.
            FieldInfo rounds = typeof(AmmoHudSnapshot).GetField("InspectedRounds");
            FieldInfo capacity = typeof(AmmoHudSnapshot).GetField("InspectedCapacity");
            Assert.NotNull(rounds);
            Assert.NotNull(capacity);
            Assert.Equal(typeof(int?), rounds.FieldType);
            Assert.Equal(typeof(int?), capacity.FieldType);
        }

        [Fact]
        public void MagazineStrip_UsesWhiteYellowRedForFullPartialEmpty()
        {
            AmmoKit kit = CreateRifleKit(3);
            Assert.Equal("full", kit.BuildHudSnapshot(0f).Slots[0].ColorKey);

            float now = 0f;
            FireRounds(kit, 5, ref now);
            Assert.Equal("partial", kit.BuildHudSnapshot(now).Slots[0].ColorKey);

            FireRounds(kit, 25, ref now);
            Assert.Equal("empty", kit.BuildHudSnapshot(now).Slots[0].ColorKey);
            Assert.True(kit.BuildHudSnapshot(now).WeaponIsEmpty);
        }

        [Fact]
        public void ExtendedMagazine_TakesLongerToSeat()
        {
            float ReloadDuration(string magazineId)
            {
                AmmoKit kit = CreateRifleKit(3, magazineId: magazineId);
                float now = 0f;
                FireRounds(kit, 5, ref now);
                kit.PressReloadKey(now);
                kit.ReleaseReloadKey(now + 0.05f);
                return kit.StateEndsAt - kit.StateStartedAt;
            }

            Assert.Equal(2.35f, ReloadDuration("mag_545_30_ps"), 3);
            Assert.Equal(2.35f * 1.28f, ReloadDuration("mag_545_45_ps"), 3);
            Assert.True(ReloadDuration("mag_545_45_ps") > ReloadDuration("mag_545_30_ps"));
        }

        [Fact]
        public void TubeFedShotgun_ReloadsAtTheDocumentedMultiplier()
        {
            WeaponDefinition shotgun = Library.GetWeapon("k870");
            AmmoKit kit = new AmmoKit(shotgun,
                new List<MagazinePlanEntry> { new MagazinePlanEntry("tube_12ga_6_buck", 3) },
                Library, DeterministicOptions(), new DeterministicRandom(9));

            Assert.Equal(6, kit.Chambered.Rounds);
            float now = 0f;
            FireRounds(kit, 2, ref now);
            kit.PressReloadKey(now);
            kit.ReleaseReloadKey(now + 0.03f);
            Assert.Equal(now + 0.03f + 4.6f * 1.9f, kit.StateEndsAt, 2);
        }

        [Fact]
        public void FumbledReload_AddsAVisibleHandlingErrorInsteadOfARandomMisfire()
        {
            AmmoKitOptions options = DeterministicOptions();
            options.AllowFumbles = true;
            WeaponDefinition rifle = Library.GetWeapon("tr30");
            int fumbles = 0;
            float extendedDelays = 0f;

            for (int i = 0; i < 200; i++)
            {
                AmmoKit kit = new AmmoKit(rifle,
                    new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_762_45_drum", 3) },
                    Library, options, new DeterministicRandom((ulong)(1000 + i)));
                float now = 0f;
                FireRounds(kit, 4, ref now);
                kit.PressReloadKey(now);
                kit.ReleaseReloadKey(now + 0.04f);
                float duration = kit.StateEndsAt - kit.StateStartedAt;
                if (kit.FumbleCount > 0)
                {
                    fumbles++;
                    extendedDelays += duration;
                    Assert.Equal(2.5f * 1.45f + 0.9f, duration, 2);
                }
                else
                {
                    Assert.Equal(2.5f * 1.45f, duration, 3);
                }
            }

            // Drum magazines fumble about 3.5% of the time; the failure is always shown
            // as extra handling time, never as a weapon that refuses to fire.
            Assert.InRange(fumbles, 1, 40);
            Assert.True(extendedDelays > 0f);
        }

        [Fact]
        public void RateOfFire_RefusesTriggerPullsThatAreTooFast()
        {
            AmmoKit kit = CreateRifleKit(3);
            Assert.True(kit.TryFire(1f).Fired);
            FireAttempt tooFast = kit.TryFire(1.02f);
            Assert.False(tooFast.Fired);
            Assert.Equal(FireDenial.RateOfFire, tooFast.Denial);
            Assert.True(kit.TryFire(1.2f).Fired);
        }

        [Fact]
        public void Sprinting_RefusesFire()
        {
            AmmoKit kit = CreateRifleKit(3);
            FireAttempt denied = kit.TryFire(1f, sprinting: true);
            Assert.False(denied.Fired);
            Assert.Equal(FireDenial.Sprinting, denied.Denial);
        }

        [Fact]
        public void MatchState_CanRefuseFire()
        {
            AmmoKit kit = CreateRifleKit(3);
            Assert.Equal(FireDenial.NotAllowedByMatch, kit.TryFire(1f, matchAllows: false).Denial);
        }

        [Fact]
        public void NoResupplyDuringALife_UnderEitherRule()
        {
            AmmoKit forbidden = CreateRifleKit(3, DeterministicOptions(ResupplyRule.NeverWithinRound));
            float now = 0f;
            FireRounds(forbidden, 12, ref now);
            Assert.False(forbidden.ReissueFullMagazines());
            Assert.Equal(18, forbidden.Chambered.Rounds);

            AmmoKit defaultRule = CreateRifleKit(3);
            float now2 = now;
            FireRounds(defaultRule, 12, ref now2);
            Assert.Equal(18, defaultRule.Chambered.Rounds);

            // The same kit, re-issued at respawn: the types and counts never change,
            // only the round counts are restored.
            Assert.True(defaultRule.ReissueFullMagazines());
            Assert.Equal(30, defaultRule.Chambered.Rounds);
            Assert.Equal(3, defaultRule.MagazineCount);
            Assert.All(defaultRule.Pouch, m => Assert.Equal(MagazineUiState.Full, m.UiState));

            // Suppressing resupply mid-round sticks.
            defaultRule.SuppressResupply();
            float now3 = now2;
            FireRounds(defaultRule, 5, ref now3);
            Assert.False(defaultRule.ReissueFullMagazines());
            Assert.Equal(25, defaultRule.Chambered.Rounds);
        }

        [Fact]
        public void OutOfAmmunition_IsAnnouncedRatherThanSilent()
        {
            AmmoKit kit = CreateRifleKit(1);
            float now = 0f;
            FireRounds(kit, 30, ref now);
            Assert.False(kit.HasReserveAmmunition);
            Assert.False(kit.BeginReload(now + 0.5f));
            Assert.Contains(AmmoEventKind.OutOfAmmunition, Drain(kit));
            Assert.Equal(AmmoInteraction.Idle, kit.State);
        }

        [Fact]
        public void LoadoutKit_SealsTheChosenKitAndRefusesEveryLaterChange()
        {
            LoadoutSelection selection = TestLoadouts.ValidRifleLoadout();
            LoadoutKit kit = new LoadoutKit(selection, Library, new LoadoutKitOptions(),
                new DeterministicRandom(3));
            Assert.Equal(ActiveSlot.Primary, kit.Active);
            Assert.Equal(5, kit.Primary.MagazineCount);
            Assert.Equal(2, kit.Secondary.MagazineCount);
            Assert.Equal(2, kit.GrenadesRemaining);

            string hash = kit.Seal();
            Assert.True(kit.IsSealed);
            Assert.Equal(selection.ComputeHash(), hash);

            LoadoutSelection smuggled = TestLoadouts.ValidSmgLoadout();
            Assert.False(kit.TryModify(smuggled, out string reason));
            Assert.Equal("kit-locked-after-match-start", reason);
            Assert.Equal("vk74", kit.ActiveWeapon.Id);
        }

        [Fact]
        public void LoadoutKit_SwitchesSlotsThrowsGrenadesAndForbidsResupply()
        {
            LoadoutKit kit = new LoadoutKit(TestLoadouts.ValidRifleLoadout(), Library,
                new LoadoutKitOptions(), new DeterministicRandom(4));
            kit.Seal();

            Assert.False(kit.TrySwitchSlot(ActiveSlot.Primary, 0f));
            Assert.True(kit.TrySwitchSlot(ActiveSlot.Secondary, 1f));
            Assert.Equal("p9", kit.ActiveWeapon.Id);
            Assert.False(kit.TrySwitchSlot(ActiveSlot.Primary, 1.1f));
            Assert.True(kit.TrySwitchSlot(ActiveSlot.Primary, 1.1f + 0.35f));

            Assert.True(kit.TrySwitchSlot(ActiveSlot.Grenade, 5f));
            Assert.True(kit.TryThrowGrenade(5.1f));
            Assert.True(kit.TryThrowGrenade(5.2f));
            Assert.Equal(0, kit.GrenadesRemaining);
            Assert.Equal(ActiveSlot.Primary, kit.Active);
            Assert.False(kit.TryThrowGrenade(5.3f));

            float now = 0f;
            for (int i = 0; i < 8; i++)
            {
                now += 0.2f;
                kit.Primary.TryFire(now);
            }

            kit.ForbidResupply();
            Assert.False(kit.OnRespawnResupply());
            Assert.Equal(22, kit.Primary.Chambered.Rounds);
        }

        [Fact]
        public void LoadoutKit_ResuppliesOnlyOnRespawnWithTheDefaultRule()
        {
            LoadoutKit kit = new LoadoutKit(TestLoadouts.ValidRifleLoadout(), Library,
                new LoadoutKitOptions(), new DeterministicRandom(5));
            kit.Seal();
            float now = 0f;
            for (int i = 0; i < 15; i++)
            {
                now += 0.2f;
                kit.Primary.TryFire(now);
            }

            Assert.Equal(15, kit.Primary.Chambered.Rounds);
            Assert.True(kit.OnRespawnResupply());
            Assert.Equal(30, kit.Primary.Chambered.Rounds);
            Assert.Equal(15, kit.Secondary.Chambered.Rounds);
        }

        [Fact]
        public void LoadoutKit_CarriesMassAndMobilityFromTheChosenGear()
        {
            LoadoutKit heavy = new LoadoutKit(new LoadoutSelection
            {
                PrimaryWeaponId = "sr338",
                PrimarySight = SightKind.Scope8x,
                PrimaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_338_5", 5) },
                SecondaryWeaponId = "p9",
                SecondarySight = SightKind.IronSight,
                SecondaryMagazines = new List<MagazinePlanEntry> { new MagazinePlanEntry("mag_9x19_15_pst", 2) },
                GrenadeId = "frag_f1",
                GrenadeCount = 1,
                ArmorKitId = "heavy"
            }, Library, new LoadoutKitOptions(), new DeterministicRandom(6));

            LoadoutKit light = new LoadoutKit(TestLoadouts.ValidSmgLoadout(), Library,
                new LoadoutKitOptions(), new DeterministicRandom(6));

            Assert.True(heavy.TotalMassKg > light.TotalMassKg);
            Assert.True(heavy.MobilityMultiplier < light.MobilityMultiplier);
            Assert.True(heavy.GearNoiseMultiplier > light.GearNoiseMultiplier);
        }
    }
}
