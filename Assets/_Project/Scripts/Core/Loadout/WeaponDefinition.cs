using System;
using System.Collections.Generic;
using Kordon.Core.Ballistics;
using Kordon.Core.Combat;

namespace Kordon.Core.Loadout
{
    /// <summary>
    /// Complete mechanical description of a weapon.  Authored in
    /// Assets/_Project/Data/Definitions/weapons.json and validated by
    /// <see cref="LoadoutValidator"/>; the same object is used by the server to
    /// resolve shots and by the client to animate the viewmodel.
    /// </summary>
    [Serializable]
    public sealed class WeaponDefinition
    {
        public string Id = "vk74";
        public string DisplayName = "VK-74 Klyuch";
        public string Manufacturer = "Veransk Ordnance Works";
        public string Description = string.Empty;
        public WeaponClass Class = WeaponClass.AssaultRifle;
        public string Caliber = "5.45x39";

        /// <summary>Default ammunition when the magazine does not override it.</summary>
        public string DefaultBulletId = "545x39_ps";

        public List<FireMode> FireModes = new List<FireMode> { FireMode.Semi, FireMode.Auto };
        public FireMode DefaultFireMode = FireMode.Auto;
        public int BurstCount = 3;

        /// <summary>Cyclic rate; the server uses this to validate fire rate (anti-cheat).</summary>
        public float RoundsPerMinute = 600f;

        /// <summary>Seconds between two shots - derived, used by validators.</summary>
        public float SecondsPerShot => RoundsPerMinute <= 0f ? 1f : 60f / RoundsPerMinute;

        public int PelletsPerShot = 1;

        /// <summary>Mass in kilograms; drives movement speed, stamina drain and noise.</summary>
        public float WeightKg = 3.6f;

        /// <summary>Movement speed multiplier while this weapon is out.</summary>
        public float MobilityMultiplier = 1f;

        public float AimDownSightSeconds = 0.28f;
        public float AimOutSeconds = 0.22f;

        /// <summary>Delay between stopping a sprint and being able to shoot.</summary>
        public float SprintToFireSeconds = 0.42f;

        /// <summary>Tactical reload (magazine still has rounds): out, in, no bolt release.</summary>
        public float ReloadTacticalSeconds = 2.35f;

        /// <summary>Dry reload (chamber empty): out, in, bolt release.</summary>
        public float ReloadDrySeconds = 2.95f;

        /// <summary>Chamber a round after inspecting/handling with an empty chamber.</summary>
        public float ChargeSeconds = 0.55f;

        /// <summary>Time to pull the magazine out and look at it (hold R).</summary>
        public float InspectSeconds = 1.15f;
        public float InspectReturnSeconds = 0.85f;

        public float MaxEffectiveRangeM = 120f;
        public float MuzzleNoiseDb = 158f;
        public bool CanBeSuppressed = true;

        /// <summary>Weapon sway multiplier while aiming (heavier weapons drift more).</summary>
        public float SwayMultiplier = 1f;

        public RecoilProfile Recoil = new RecoilProfile();
        public SpreadProfile Spread = new SpreadProfile();

        /// <summary>Sights that can be selected for this weapon in the loadout screen.</summary>
        public List<SightKind> AvailableSights = new List<SightKind> { SightKind.IronSight, SightKind.RedDot };
        public SightKind DefaultSight = SightKind.IronSight;

        /// <summary>Magnification for scope sights (zoom factor).</summary>
        public static float MagnificationFor(SightKind sight)
        {
            switch (sight)
            {
                case SightKind.Scope1p5x: return 1.5f;
                case SightKind.Scope4x: return 4f;
                case SightKind.Scope8x: return 8f;
                default: return 1f;
            }
        }

        /// <summary>Field of view used when aiming with a given sight (degrees, vertical).</summary>
        public float AimFieldOfView(SightKind sight, float hipFov)
        {
            float magnification = MagnificationFor(sight);
            if (magnification <= 1.001f)
            {
                // Iron sights and reflex optics keep a realistic, non-zoomed picture.
                return hipFov * 0.92f;
            }

            return hipFov / magnification;
        }

        /// <summary>Whether the primary fire uses hitscan (all normal bullets do).</summary>
        public bool IsHitscan => Class != WeaponClass.Grenade;

        /// <summary>Grenades are the only physical projectiles, justified by their game rule.</summary>
        public bool IsThrownProjectile => Class == WeaponClass.Grenade;

        public WeaponDefinition Clone()
        {
            WeaponDefinition copy = (WeaponDefinition)MemberwiseClone();
            copy.FireModes = new List<FireMode>(FireModes);
            copy.AvailableSights = new List<SightKind>(AvailableSights);
            copy.Recoil = Recoil != null ? Recoil.Clone() : new RecoilProfile();
            copy.Spread = Spread != null ? Spread.Clone() : new SpreadProfile();
            return copy;
        }

        public bool SupportsFireMode(FireMode mode) => FireModes.Contains(mode);

        public bool CanBurst => SupportsFireMode(FireMode.Burst) && BurstCount > 1;
    }
}
