using System;

namespace Kordon.Core.Loadout
{
    public enum GrenadeKind
    {
        None = 0,
        Fragmentation = 1,
        Smoke = 2,
        Flashbang = 3
    }

    /// <summary>
    /// Thrown equipment.  Grenades are physical projectiles because their game rule
    /// (arc, bounce, timed fuze, area effect) cannot be expressed as a hitscan ray.
    /// </summary>
    [Serializable]
    public sealed class GrenadeDefinition
    {
        public string Id = "frag_f1";
        public string DisplayName = "F-1 Oskolok";
        public GrenadeKind Kind = GrenadeKind.Fragmentation;
        public float WeightKg = 0.6f;
        public float ThrowSpeed = 14f;
        public float FuseSeconds = 3.4f;
        public float CookableMinFuse = 0.8f;

        /// <summary>Fragmentation damage model.</summary>
        public float DamageAtCenter = 115f;
        public float LethalRadiusM = 4.5f;
        public float DamageRadiusM = 13f;
        public float SuppressionRadiusM = 22f;

        /// <summary>How much cover reduces fragment damage (concrete walls save lives).</summary>
        public bool LineOfSightRequired = true;

        /// <summary>Smoke/flash parameters.</summary>
        public float EffectRadiusM = 7f;
        public float EffectDurationSeconds = 22f;

        /// <summary>Cook time before throw (holding the grenade shortens the remaining fuze).</summary>
        public bool AllowCooking = true;

        public float DamageAtDistance(float distanceM)
        {
            if (distanceM <= LethalRadiusM)
            {
                return DamageAtCenter;
            }

            if (distanceM >= DamageRadiusM)
            {
                return 0f;
            }

            float t = (distanceM - LethalRadiusM) / Math.Max(0.01f, DamageRadiusM - LethalRadiusM);
            return DamageAtCenter * (1f - t) * (1f - t);
        }

        public GrenadeDefinition Clone() => (GrenadeDefinition)MemberwiseClone();
    }
}
