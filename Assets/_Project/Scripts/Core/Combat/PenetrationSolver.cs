using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>Everything the solver needs to decide what a bullet does to a surface or a plate.</summary>
    public struct PenetrationQuery
    {
        public BulletDefinition Bullet;
        public MaterialDefinition Material;

        /// <summary>Actual thickness of the surface the bullet has to cross, in millimetres.</summary>
        public float ThicknessMm;

        /// <summary>0 = hitting the surface head on, 90 = perfectly oblique.</summary>
        public float AngleFromNormalDeg;

        /// <summary>Armour class behind the surface (0 for open flesh / no plate).</summary>
        public int ArmorClass;

        /// <summary>Armour coverage probability 0..1 (plates do not cover the whole zone).</summary>
        public float ArmorCoverage;

        public float TravelDistanceM;

        public static PenetrationQuery Flesh(BulletDefinition bullet, float distanceM) => new PenetrationQuery
        {
            Bullet = bullet,
            Material = MaterialDefinition.Default(MaterialKind.Flesh),
            ThicknessMm = 0f,
            AngleFromNormalDeg = 0f,
            ArmorClass = 0,
            ArmorCoverage = 0f,
            TravelDistanceM = distanceM
        };
    }

    /// <summary>Outcome of one penetration test.</summary>
    public struct PenetrationResult
    {
        public bool Penetrated;
        public bool Ricochet;
        public bool BlockedByArmor;

        /// <summary>Multiplier applied to the bullet's base damage after the barrier/plate.</summary>
        public float ResidualDamageMultiplier;

        /// <summary>Durability damage inflicted on the armour plate (0 when unarmoured).</summary>
        public float ArmorDurabilityDamage;

        /// <summary>Positive when the bullet had more penetration than the obstacle needed.</summary>
        public float PenetrationMargin;

        /// <summary>Angle of incidence actually used, degrees from normal.</summary>
        public float AngleFromNormalDeg;

        public static PenetrationResult Nothing() => new PenetrationResult { ResidualDamageMultiplier = 1f };
    }

    /// <summary>
    /// Material + angle + armour penetration model.  Pure maths so the server can
    /// run it authoritatively and the tests can pin the numbers down.
    ///
    /// Model summary (design units, not physics):
    ///   effectivePenetration = bullet.PenetrationValue * cos(angle)^AngleExponent
    ///   coverResistance      = ReferenceStopValue * material.PenetrationResistance
    ///                          * (thickness / material.ReferenceThicknessMm)
    ///   margin               = effectivePenetration - coverResistance
    ///   penetratesArmour     = margin >= ArmorClassThreshold[armorClass]
    ///
    /// ReferenceStopValue = 20 means "a 20 penetration bullet is exactly stopped by
    /// the reference thickness of a material with resistance 1.0".
    /// </summary>
    public static class PenetrationSolver
    {
        public const float ReferenceStopValue = 20f;
        public const float AngleExponent = 1.35f;
        public const float MaxAngleDeg = 85f;

        /// <summary>Penetration needed to defeat each armour class (index = armour class).</summary>
        public static readonly float[] ArmorClassThresholds =
        {
            0f,   // 0 - unarmoured
            8f,   // 1 - light vest
            16f,  // 2
            25f,  // 3
            35f,  // 4
            48f,  // 5
            62f   // 6 - heavy plate
        };

        /// <summary>Fraction of base damage transferred as blunt trauma when a plate stops a bullet.</summary>
        public static readonly float[] BluntDamageFactors =
        {
            1f, 0.45f, 0.32f, 0.22f, 0.16f, 0.12f, 0.09f
        };

        public static float CoverResistance(MaterialDefinition material, float thicknessMm)
        {
            if (material == null)
            {
                return 0f;
            }

            float reference = Math.Max(1f, material.ReferenceThicknessMm);
            float ratio = Math.Max(0f, thicknessMm) / reference;
            return ReferenceStopValue * material.PenetrationResistance * ratio;
        }

        public static float AngleFactor(float angleFromNormalDeg)
        {
            float clamped = MathUtil.Clamp(Math.Abs(angleFromNormalDeg), 0f, MaxAngleDeg);
            double cos = Math.Cos(clamped * MathUtil.Deg2Rad);
            return (float)Math.Pow(Math.Max(0.0, cos), AngleExponent);
        }

        public static float ArmorThreshold(int armorClass)
        {
            int index = MathUtil.Clamp(armorClass, 0, ArmorClassThresholds.Length - 1);
            return ArmorClassThresholds[index];
        }

        public static float BluntFactor(int armorClass)
        {
            int index = MathUtil.Clamp(armorClass, 0, BluntDamageFactors.Length - 1);
            return BluntDamageFactors[index];
        }

        public static float RicochetChance(MaterialDefinition material, float angleFromNormalDeg)
        {
            if (material == null)
            {
                return 0f;
            }

            // Flesh and cloth never send a round back; matching the guard in Solve() keeps
            // the helper honest for UI and debugging.
            if (material.Kind == MaterialKind.Flesh || material.Kind == MaterialKind.Cloth)
            {
                return 0f;
            }

            float angle = MathUtil.Clamp(Math.Abs(angleFromNormalDeg), 0f, 90f);
            float chance = material.BaseRicochetChance;
            if (angle > material.RicochetAngleThresholdDeg)
            {
                float t = MathUtil.InverseLerp(material.RicochetAngleThresholdDeg, 85f, angle);
                chance += t * (1f - material.BaseRicochetChance) * 0.85f;
            }

            return MathUtil.Clamp01(chance);
        }

        /// <summary>
        /// Resolve one barrier/plate interaction.  <paramref name="random"/> is only used for the
        /// ricochet and armour-coverage rolls, so results stay reproducible in tests.
        /// </summary>
        public static PenetrationResult Solve(PenetrationQuery query, DeterministicRandom random)
        {
            PenetrationResult result = PenetrationResult.Nothing();
            result.AngleFromNormalDeg = query.AngleFromNormalDeg;

            BulletDefinition bullet = query.Bullet;
            if (bullet == null)
            {
                result.Penetrated = false;
                result.ResidualDamageMultiplier = 0f;
                return result;
            }

            float effective = bullet.PenetrationValue * AngleFactor(query.AngleFromNormalDeg);
            MaterialDefinition material = query.Material ?? MaterialDefinition.Default(MaterialKind.Flesh);

            // ---- ricochet roll (only meaningful against hard, non-flesh surfaces)
            if (material.Kind != MaterialKind.Flesh && material.Kind != MaterialKind.Cloth)
            {
                float ricochetChance = RicochetChance(material, query.AngleFromNormalDeg);
                if (random != null && random.Chance(ricochetChance))
                {
                    result.Ricochet = true;
                    result.Penetrated = false;
                    result.ResidualDamageMultiplier = 0f;
                    result.PenetrationMargin = effective - CoverResistance(material, query.ThicknessMm);
                    return result;
                }
            }

            // ---- cover
            bool hasCover = material.Kind != MaterialKind.Flesh && query.ThicknessMm > 0f;
            if (hasCover)
            {
                if (!material.Penetrable)
                {
                    result.Penetrated = false;
                    result.ResidualDamageMultiplier = 0f;
                    result.PenetrationMargin = -CoverResistance(material, query.ThicknessMm);
                    return result;
                }

                float resistance = CoverResistance(material, query.ThicknessMm);
                float margin = effective - resistance;
                result.PenetrationMargin = margin;
                if (margin <= 0f)
                {
                    result.Penetrated = false;
                    result.ResidualDamageMultiplier = 0f;
                    return result;
                }

                // The deeper the barrier, the more the bullet is damaged/deformed.
                float loss = MathUtil.Clamp01(resistance / Math.Max(1f, effective));
                result.ResidualDamageMultiplier = material.ResidualDamageFactor * (1f - 0.45f * loss);
                effective = margin + ReferenceStopValue * 0f; // margin carries on to armour
            }

            // ---- armour plate behind the cover
            if (query.ArmorClass > 0 && query.ArmorCoverage > 0f)
            {
                bool covered = random == null || random.Chance(query.ArmorCoverage);
                if (covered)
                {
                    float threshold = ArmorThreshold(query.ArmorClass);
                    float availablePenetration = hasCover
                        ? Math.Max(0f, result.PenetrationMargin)
                        : effective;
                    result.PenetrationMargin = availablePenetration - threshold;

                    if (availablePenetration < threshold)
                    {
                        result.Penetrated = false;
                        result.BlockedByArmor = true;
                        result.ArmorDurabilityDamage = 6f + availablePenetration * 0.9f;
                        result.ResidualDamageMultiplier = BluntFactor(query.ArmorClass);
                        return result;
                    }

                    result.ArmorDurabilityDamage = 4f + (availablePenetration - threshold) * 0.6f;
                    float armorLoss = MathUtil.Clamp01(threshold / Math.Max(1f, availablePenetration));
                    result.ResidualDamageMultiplier *= 1f - 0.35f * armorLoss;
                }
            }

            result.Penetrated = true;
            result.ResidualDamageMultiplier = MathUtil.Clamp(result.ResidualDamageMultiplier, 0.05f, 1f);
            return result;
        }
    }
}
