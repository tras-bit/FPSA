using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kordon.Core.Combat;

namespace Kordon.Core.Loadout
{
    /// <summary>One line of the pre-round magazine plan: "N magazines of type X".</summary>
    [Serializable]
    public struct MagazinePlanEntry : IEquatable<MagazinePlanEntry>
    {
        public string MagazineId;
        public int Count;

        public MagazinePlanEntry(string magazineId, int count)
        {
            MagazineId = magazineId;
            Count = count;
        }

        public bool Equals(MagazinePlanEntry other) => MagazineId == other.MagazineId && Count == other.Count;
        public override bool Equals(object obj) => obj is MagazinePlanEntry other && Equals(other);
        public override int GetHashCode() => (MagazineId?.GetHashCode() ?? 0) * 397 ^ Count;
        public override string ToString() => $"{Count}x {MagazineId}";
    }

    /// <summary>
    /// The kit an operator takes into a match.  Chosen on the loadout screen before
    /// the round starts and then locked for the whole match: no weapon swaps, no new
    /// magazines mid-round (see docs/03_MAGAZINE_SYSTEM.md).
    /// </summary>
    [Serializable]
    public sealed class LoadoutSelection
    {
        public string PrimaryWeaponId = string.Empty;
        public SightKind PrimarySight = SightKind.IronSight;
        public bool PrimarySuppressed = false;
        public List<MagazinePlanEntry> PrimaryMagazines = new List<MagazinePlanEntry>();

        public string SecondaryWeaponId = string.Empty;
        public SightKind SecondarySight = SightKind.IronSight;
        public bool SecondarySuppressed = false;
        public List<MagazinePlanEntry> SecondaryMagazines = new List<MagazinePlanEntry>();

        public string GrenadeId = string.Empty;
        public int GrenadeCount = 0;

        public string ArmorKitId = "standard";

        /// <summary>Server assigned once the kit is locked; used to detect tampering.</summary>
        public string LockToken = string.Empty;

        public LoadoutSelection Clone()
        {
            LoadoutSelection copy = (LoadoutSelection)MemberwiseClone();
            copy.PrimaryMagazines = new List<MagazinePlanEntry>(PrimaryMagazines);
            copy.SecondaryMagazines = new List<MagazinePlanEntry>(SecondaryMagazines);
            return copy;
        }

        public int TotalMagazines()
        {
            int total = 0;
            foreach (MagazinePlanEntry entry in PrimaryMagazines)
            {
                total += Math.Max(0, entry.Count);
            }

            foreach (MagazinePlanEntry entry in SecondaryMagazines)
            {
                total += Math.Max(0, entry.Count);
            }

            return total;
        }

        /// <summary>
        /// Stable content hash.  The server stores it when the kit is locked and
        /// rejects any later change, which is how "locked after match start" is
        /// enforced rather than merely asserted in the UI.
        /// </summary>
        public string ComputeHash()
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(PrimaryWeaponId).Append('|').Append((int)PrimarySight).Append('|')
              .Append(PrimarySuppressed ? 1 : 0).Append('|');
            AppendPlan(sb, PrimaryMagazines);
            sb.Append(SecondaryWeaponId).Append('|').Append((int)SecondarySight).Append('|')
              .Append(SecondarySuppressed ? 1 : 0).Append('|');
            AppendPlan(sb, SecondaryMagazines);
            sb.Append(GrenadeId).Append('|').Append(GrenadeCount).Append('|').Append(ArmorKitId);

            // FNV-1a, 64 bit: stable across runtimes, unlike GetHashCode.
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < sb.Length; i++)
                {
                    hash ^= sb[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private static void AppendPlan(StringBuilder sb, List<MagazinePlanEntry> plan)
        {
            List<MagazinePlanEntry> sorted = new List<MagazinePlanEntry>(plan);
            sorted.Sort((a, b) => string.CompareOrdinal(a.MagazineId, b.MagazineId));
            foreach (MagazinePlanEntry entry in sorted)
            {
                sb.Append(entry.MagazineId).Append(':').Append(entry.Count).Append(',');
            }

            sb.Append(';');
        }
    }

    /// <summary>Hard limits of the pre-round kit selection. Equal for every player.</summary>
    public static class LoadoutRules
    {
        public const int MaxPrimaryMagazines = 8;
        public const int MaxSecondaryMagazines = 4;
        public const int MaxTotalMagazines = 10;
        public const int MaxGrenades = 2;

        /// <summary>
        /// Total carried mass limit in kilograms: weapons, ammunition, armour and
        /// grenades.  Stops "take everything" from being the dominant choice.
        /// </summary>
        public const float MaxTotalMassKg = 34f;

        public const float MinTotalMassKg = 4f;

        /// <summary>One magazine must be inside the weapon when the match starts.</summary>
        public const bool RequireChamberedMagazine = true;
    }

    /// <summary>Result of validating a loadout selection.</summary>
    public sealed class LoadoutValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public float TotalMassKg { get; set; }
        public int TotalMagazines { get; set; }
        public int TotalRounds { get; set; }

        public void Fail(string reason) => Errors.Add(reason);

        public override string ToString() => IsValid
            ? string.Format(CultureInfo.InvariantCulture,
                "valid (mass {0:F1} kg, {1} magazines, {2} rounds)", TotalMassKg, TotalMagazines, TotalRounds)
            : "invalid: " + string.Join("; ", Errors.ToArray());
    }

    /// <summary>
    /// Validates a pre-round kit.  Runs on the client for instant feedback and on the
    /// server before locking, so a modified client cannot smuggle an illegal kit in.
    /// </summary>
    public static class LoadoutValidator
    {
        public static LoadoutValidationResult Validate(LoadoutSelection selection, Data.DefinitionLibrary library)
        {
            LoadoutValidationResult result = new LoadoutValidationResult();
            if (selection == null)
            {
                result.Fail("empty-selection");
                return result;
            }

            if (library == null)
            {
                result.Fail("no-definition-library");
                return result;
            }

            float mass = 0f;

            ValidateSlot(selection.PrimaryWeaponId, selection.PrimarySight, selection.PrimarySuppressed,
                selection.PrimaryMagazines, LoadoutRules.MaxPrimaryMagazines, library, result, "primary", ref mass);

            if (!string.IsNullOrEmpty(selection.SecondaryWeaponId))
            {
                ValidateSlot(selection.SecondaryWeaponId, selection.SecondarySight, selection.SecondarySuppressed,
                    selection.SecondaryMagazines, LoadoutRules.MaxSecondaryMagazines, library, result, "secondary",
                    ref mass);
            }

            if (selection.TotalMagazines() > LoadoutRules.MaxTotalMagazines)
            {
                result.Fail($"too-many-magazines-total({selection.TotalMagazines()}" +
                            $">{LoadoutRules.MaxTotalMagazines})");
            }

            if (!string.IsNullOrEmpty(selection.GrenadeId))
            {
                GrenadeDefinition grenade = library.GetGrenade(selection.GrenadeId);
                if (grenade == null)
                {
                    result.Fail($"unknown-grenade({selection.GrenadeId})");
                }
                else
                {
                    if (selection.GrenadeCount < 0 || selection.GrenadeCount > LoadoutRules.MaxGrenades)
                    {
                        result.Fail($"grenade-count-out-of-range({selection.GrenadeCount})");
                    }

                    mass += grenade.WeightKg * Math.Max(0, selection.GrenadeCount);
                }
            }
            else if (selection.GrenadeCount > 0)
            {
                result.Fail("grenade-count-without-type");
            }

            ArmorKitDefinition armor = library.GetArmorKit(selection.ArmorKitId);
            if (armor == null)
            {
                result.Fail($"unknown-armor-kit({selection.ArmorKitId})");
            }
            else
            {
                mass += armor.WeightKg;
            }

            result.TotalMassKg = mass;
            result.TotalMagazines = selection.TotalMagazines();

            if (mass > LoadoutRules.MaxTotalMassKg)
            {
                result.Fail(string.Format(CultureInfo.InvariantCulture,
                    "too-heavy({0:F1}kg>{1:F0}kg)", mass, LoadoutRules.MaxTotalMassKg));
            }

            if (mass < LoadoutRules.MinTotalMassKg)
            {
                result.Fail("kit-too-light");
            }

            if (string.IsNullOrEmpty(selection.PrimaryWeaponId))
            {
                result.Fail("no-primary-weapon");
            }

            return result;
        }

        private static void ValidateSlot(string weaponId, SightKind sight, bool suppressed,
            List<MagazinePlanEntry> plan, int maxMagazines, Data.DefinitionLibrary library,
            LoadoutValidationResult result, string slot, ref float mass)
        {
            if (string.IsNullOrEmpty(weaponId))
            {
                result.Fail($"{slot}-missing-weapon");
                return;
            }

            WeaponDefinition weapon = library.GetWeapon(weaponId);
            if (weapon == null)
            {
                result.Fail($"{slot}-unknown-weapon({weaponId})");
                return;
            }

            mass += weapon.WeightKg;

            if (!weapon.AvailableSights.Contains(sight))
            {
                result.Fail($"{slot}-sight-not-available({sight})");
            }

            if (suppressed && !weapon.CanBeSuppressed)
            {
                result.Fail($"{slot}-suppressor-not-available");
            }

            if (suppressed)
            {
                mass += 0.42f;
            }

            if (plan == null || plan.Count == 0)
            {
                result.Fail($"{slot}-no-magazines-planned");
                return;
            }

            int magazines = 0;
            foreach (MagazinePlanEntry entry in plan)
            {
                if (entry.Count <= 0)
                {
                    result.Fail($"{slot}-magazine-count-must-be-positive({entry.MagazineId})");
                    continue;
                }

                MagazineDefinition magazine = library.GetMagazine(entry.MagazineId);
                if (magazine == null)
                {
                    result.Fail($"{slot}-unknown-magazine({entry.MagazineId})");
                    continue;
                }

                if (!magazine.IsCompatibleWith(weapon))
                {
                    result.Fail($"{slot}-magazine-incompatible({entry.MagazineId}->{weaponId})");
                    continue;
                }

                Combat.BulletDefinition bullet = library.GetBullet(magazine.AmmoVariantId) ??
                                                 library.GetBullet(weapon.DefaultBulletId);
                if (bullet == null)
                {
                    result.Fail($"{slot}-unknown-ammunition({magazine.AmmoVariantId})");
                }

                magazines += entry.Count;
                mass += magazine.LoadedWeightKg * entry.Count;
                result.TotalRounds += magazine.Capacity * entry.Count;
            }

            if (magazines > maxMagazines)
            {
                result.Fail($"{slot}-too-many-magazines({magazines}>{maxMagazines})");
            }

            // The weapon must start the match with a magazine inside it.
            if (LoadoutRules.RequireChamberedMagazine && magazines < 1)
            {
                result.Fail($"{slot}-needs-chambered-magazine");
            }
        }
    }
}
