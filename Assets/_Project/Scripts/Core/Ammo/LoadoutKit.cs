using System;
using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;

namespace Kordon.Core.Ammo
{
    /// <summary>Which weapon is currently in the operator's hands.</summary>
    public enum ActiveSlot
    {
        Primary = 0,
        Secondary = 1,
        Grenade = 2
    }

    /// <summary>
    /// The whole kit an operator carries into a match: primary weapon, sidearm,
    /// grenades, armour and the magazine plan of each weapon.
    ///
    /// The kit is built once from a validated <see cref="LoadoutSelection"/> and then
    /// locked for the entire match (<see cref="Seal"/>); attempting to change weapons,
    /// sights or magazines afterwards is rejected.  This is the server side half of the
    /// "no loadout changes mid-round" rule.
    /// </summary>
    [Serializable]
    public sealed class LoadoutKit
    {
        private readonly DefinitionLibrary _library;

        public LoadoutSelection Selection { get; private set; }
        public AmmoKit Primary { get; private set; }
        public AmmoKit Secondary { get; private set; }
        public ArmorState Armor { get; private set; }
        public ActiveSlot Active { get; private set; } = ActiveSlot.Primary;
        public bool IsSealed { get; private set; }
        public string SealedHash { get; private set; } = string.Empty;
        public int GrenadesRemaining { get; private set; }
        public GrenadeDefinition Grenade { get; private set; }
        public float TotalMassKg { get; private set; }
        public float LastSlotSwitchTime { get; private set; } = float.NegativeInfinity;

        public LoadoutKitOptions Options { get; }

        public LoadoutKit(LoadoutSelection selection, DefinitionLibrary library,
            LoadoutKitOptions options = null, DeterministicRandom random = null)
        {
            _library = library;
            Options = options ?? new LoadoutKitOptions();
            Build(selection, random);
        }

        private void Build(LoadoutSelection selection, DeterministicRandom random)
        {
            Selection = selection;
            AmmoKitOptions ammoOptions = new AmmoKitOptions
            {
                HoldToInspectSeconds = Options.HoldToInspectSeconds,
                KeepEmptyMagazines = Options.KeepEmptyMagazines,
                ReturnPartialMagazines = Options.ReturnPartialMagazines,
                PickPolicy = Options.PickPolicy,
                AllowFumbles = Options.AllowFumbles,
                Resupply = Options.Resupply
            };

            WeaponDefinition primary = _library?.GetWeapon(selection?.PrimaryWeaponId);
            if (primary != null)
            {
                Primary = new AmmoKit(primary, selection.PrimaryMagazines, _library, ammoOptions, random);
            }

            WeaponDefinition secondary = _library?.GetWeapon(selection?.SecondaryWeaponId);
            if (secondary != null)
            {
                Secondary = new AmmoKit(secondary, selection.SecondaryMagazines, _library, ammoOptions, random);
            }

            ArmorKitDefinition armorKit = _library?.GetArmorKit(selection?.ArmorKitId) ?? new ArmorKitDefinition();
            Armor = new ArmorState(armorKit);

            Grenade = _library?.GetGrenade(selection?.GrenadeId);
            GrenadesRemaining = Grenade != null ? Math.Max(0, selection.GrenadeCount) : 0;

            LoadoutValidationResult validation = LoadoutValidator.Validate(selection, _library);
            TotalMassKg = validation.TotalMassKg;
        }

        /// <summary>Lock the kit for the rest of the match.</summary>
        public string Seal()
        {
            IsSealed = true;
            SealedHash = Selection?.ComputeHash() ?? string.Empty;
            return SealedHash;
        }

        /// <summary>
        /// Attempt to change the kit after it has been sealed.  Always refused once the
        /// match is live, which is what the design requires.
        /// </summary>
        public bool TryModify(LoadoutSelection newSelection, out string reason)
        {
            if (IsSealed)
            {
                reason = "kit-locked-after-match-start";
                return false;
            }

            LoadoutValidationResult validation = LoadoutValidator.Validate(newSelection, _library);
            if (!validation.IsValid)
            {
                reason = string.Join(";", validation.Errors.ToArray());
                return false;
            }

            Build(newSelection, new DeterministicRandom((ulong)DateTime.UtcNow.Ticks));
            reason = null;
            return true;
        }

        public AmmoKit ActiveAmmoKit
        {
            get
            {
                switch (Active)
                {
                    case ActiveSlot.Secondary: return Secondary;
                    default: return Primary;
                }
            }
        }

        public WeaponDefinition ActiveWeapon => ActiveAmmoKit?.Weapon;

        public bool TrySwitchSlot(ActiveSlot slot, float now)
        {
            if (slot == Active)
            {
                return false;
            }

            if (now - LastSlotSwitchTime < Options.SlotSwitchCooldownSeconds)
            {
                return false;
            }

            switch (slot)
            {
                case ActiveSlot.Primary when Primary == null: return false;
                case ActiveSlot.Secondary when Secondary == null: return false;
                case ActiveSlot.Grenade when GrenadesRemaining <= 0: return false;
            }

            ActiveAmmoKit?.CancelInteraction();
            Active = slot;
            LastSlotSwitchTime = now;
            return true;
        }

        /// <summary>Throw the equipped grenade. Grenades are physical projectiles.</summary>
        public bool TryThrowGrenade(float now)
        {
            if (Active != ActiveSlot.Grenade || GrenadesRemaining <= 0 || Grenade == null)
            {
                return false;
            }

            GrenadesRemaining--;
            if (GrenadesRemaining <= 0)
            {
                Active = Primary != null ? ActiveSlot.Primary : ActiveSlot.Secondary;
            }

            return true;
        }

        public void Tick(float now)
        {
            Primary?.Tick(now);
            Secondary?.Tick(now);
        }

        public void DrainEvents(List<AmmoEvent> into)
        {
            Primary?.DrainEvents(into);
            Secondary?.DrainEvents(into);
        }

        /// <summary>
        /// Respawn resupply.  Follows the configured <see cref="ResupplyRule"/>: with the
        /// default rule the locked kit is re-issued with full magazines, but never during
        /// a life - there are no ammo crates, no ground pickups and no ammo sharing.
        /// </summary>
        public bool OnRespawnResupply()
        {
            if (Options.Resupply == ResupplyRule.NeverWithinRound)
            {
                return false;
            }

            bool changed = false;
            changed |= Primary != null && Primary.ReissueFullMagazines();
            changed |= Secondary != null && Secondary.ReissueFullMagazines();
            Armor?.Reset();

            // Thrown equipment is part of the sealed kit too: respawning reissues the
            // grenades the operator picked, never more than that.
            int issued = Grenade != null ? Math.Max(0, Selection?.GrenadeCount ?? 0) : 0;
            if (GrenadesRemaining != issued)
            {
                GrenadesRemaining = issued;
                changed = true;
            }

            return changed;
        }

        /// <summary>Explicitly forbid any resupply for the rest of the round.</summary>
        public void ForbidResupply()
        {
            Options.Resupply = ResupplyRule.NeverWithinRound;
            Primary?.SuppressResupply();
            Secondary?.SuppressResupply();
        }

        public AmmoHudSnapshot BuildHudSnapshot(float now)
        {
            AmmoKit kit = ActiveAmmoKit ?? Primary;
            if (kit == null)
            {
                return new AmmoHudSnapshot();
            }

            AmmoHudSnapshot snapshot = kit.BuildHudSnapshot(now, GrenadesRemaining);
            return snapshot;
        }

        /// <summary>Mobility and noise contribution of everything currently carried.</summary>
        public float GearNoiseMultiplier => Armor?.Kit != null ? Armor.Kit.NoiseMultiplier : 1f;

        public float MobilityMultiplier
        {
            get
            {
                float weapon = ActiveWeapon?.MobilityMultiplier ?? 1f;
                float armor = Armor?.Kit != null ? Armor.Kit.MobilityMultiplier : 1f;
                return MathUtil.Clamp(weapon * armor, 0.5f, 1.1f);
            }
        }
    }

    /// <summary>Tunables for a kit instance.</summary>
    [Serializable]
    public sealed class LoadoutKitOptions
    {
        public float HoldToInspectSeconds = 0.28f;
        public bool KeepEmptyMagazines = true;
        public bool ReturnPartialMagazines = true;
        public MagazinePickPolicy PickPolicy = MagazinePickPolicy.NextNonEmpty;
        public bool AllowFumbles = true;
        public ResupplyRule Resupply = ResupplyRule.OnRespawnOnly;
        public float SlotSwitchCooldownSeconds = 0.35f;
    }
}
