using System;
using System.Collections.Generic;

namespace Kordon.Core.Loadout
{
    /// <summary>Physical feeding device type. Affects reload animation and speed.</summary>
    public enum MagazineKind
    {
        Box = 0,
        Extended = 1,
        Drum = 2,
        Tube = 3,
        Cylinder = 4,
        Internal = 5
    }

    /// <summary>
    /// A magazine the player can put into the pre-round plan.  Magazines differ by
    /// capacity (size), by the ammunition variant they carry (type) and by the
    /// handling cost they impose - which is what makes the pre-round choice a real
    /// tactical decision instead of a number to maximise.
    /// </summary>
    [Serializable]
    public sealed class MagazineDefinition
    {
        public string Id = "mag_545_30";
        public string DisplayName = "30-rnd 5.45 box";
        public string Caliber = "5.45x39";

        /// <summary>Bullet definition id loaded in this magazine (ammunition variant).</summary>
        public string AmmoVariantId = "545x39_ps";

        public int Capacity = 30;
        public MagazineKind Kind = MagazineKind.Box;

        /// <summary>Mass of the loaded magazine in kilograms.</summary>
        public float LoadedWeightKg = 0.72f;

        /// <summary>Mass of the empty magazine in kilograms (what you keep after it is shot dry).</summary>
        public float EmptyWeightKg = 0.24f;

        /// <summary>Reload time multiplier: drums are slow, short boxes are quick.</summary>
        public float ReloadTimeMultiplier = 1f;

        /// <summary>Handling penalty applied while the weapon carries this magazine (aim sway, ADS).</summary>
        public float HandlingPenalty = 0f;

        /// <summary>Reliability: chance per reload of a fumble that needs a second, visible handling attempt.</summary>
        public float FumbleChance = 0.01f;

        /// <summary>Weapon classes this magazine can be planned for.</summary>
        public List<WeaponClass> AllowedClasses = new List<WeaponClass>();

        /// <summary>Weapon ids this magazine fits (empty = any weapon of an allowed class with matching caliber).</summary>
        public List<string> AllowedWeaponIds = new List<string>();

        public bool IsCompatibleWith(WeaponDefinition weapon)
        {
            if (weapon == null)
            {
                return false;
            }

            if (AllowedWeaponIds.Count > 0)
            {
                for (int i = 0; i < AllowedWeaponIds.Count; i++)
                {
                    if (AllowedWeaponIds[i] == weapon.Id)
                    {
                        return true;
                    }
                }

                return false;
            }

            if (AllowedClasses.Count > 0 && !AllowedClasses.Contains(weapon.Class))
            {
                return false;
            }

            return string.IsNullOrEmpty(Caliber) || Caliber == weapon.Caliber;
        }

        public MagazineDefinition Clone()
        {
            MagazineDefinition copy = (MagazineDefinition)MemberwiseClone();
            copy.AllowedClasses = new List<WeaponClass>(AllowedClasses);
            copy.AllowedWeaponIds = new List<string>(AllowedWeaponIds);
            return copy;
        }
    }
}
