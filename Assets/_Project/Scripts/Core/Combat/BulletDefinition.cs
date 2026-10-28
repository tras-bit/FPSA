using System;

namespace Kordon.Core.Combat
{
    /// <summary>
    /// Ammunition characteristics.  Damage is resolved hitscan for normal
    /// bullets (<see cref="IsProjectile"/> == false); only grenade launcher /
    /// thrown grenade style warheads use a physical projectile, which is the only
    /// place the design allows simulated ballistics.
    /// </summary>
    [Serializable]
    public sealed class BulletDefinition
    {
        public string Id = "545x39_ps";
        public string DisplayName = "5.45x39 PS";
        public string Caliber = "5.45x39";

        /// <summary>Base flesh damage before zone, armour and distance modifiers.</summary>
        public float BaseDamage = 34f;

        /// <summary>Penetration capability in millimetres of RHA at 100 m (design unit).</summary>
        public float PenetrationValue = 28f;

        public float MuzzleVelocity = 880f;
        public float MassGrams = 3.4f;

        /// <summary>Distance falloff curve.</summary>
        public float OptimalRangeM = 35f;
        public float FalloffStartM = 60f;
        public float FalloffEndM = 180f;
        public float MinDamageMultiplier = 0.45f;

        /// <summary>Bullets that fragment after penetrating soft cover lose more damage.</summary>
        public float FragmentationChance = 0.15f;
        public float FragmentationDamageMultiplier = 1.35f;

        public bool IsProjectile = false;
        public float ProjectileSpeed = 0f;
        public float ProjectileGravity = 0f;

        /// <summary>Bleeding chance multiplier applied on non-fatal torso/limb hits.</summary>
        public float BleedChance = 0.25f;

        public BulletDefinition Clone() => (BulletDefinition)MemberwiseClone();

        /// <summary>Damage multiplier for a given travel distance, in metres.</summary>
        public float DamageMultiplierAtDistance(float distanceM)
        {
            if (distanceM <= OptimalRangeM)
            {
                return 1f;
            }

            if (distanceM >= FalloffEndM)
            {
                return MinDamageMultiplier;
            }

            if (distanceM <= FalloffStartM)
            {
                // gentle slope between optimal range and the start of falloff
                float t = (distanceM - OptimalRangeM) / Math.Max(0.001f, FalloffStartM - OptimalRangeM);
                return 1f - 0.1f * t;
            }

            float u = (distanceM - FalloffStartM) / Math.Max(0.001f, FalloffEndM - FalloffStartM);
            return Utility.MathUtil.Lerp(0.9f, MinDamageMultiplier, u);
        }
    }
}
