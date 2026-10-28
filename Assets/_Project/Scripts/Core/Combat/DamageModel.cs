using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>Tunable rules for a single body zone.</summary>
    [Serializable]
    public struct ZoneDamageRule
    {
        public HitZone Zone;
        public float DamageMultiplier;

        /// <summary>A penetrating hit here kills outright regardless of remaining health.</summary>
        public bool LethalWhenPenetrated;

        public float BleedChanceMultiplier;

        /// <summary>Mobility loss per point of accumulated damage to this zone (limbs only).</summary>
        public float MobilityDamageFactor;

        /// <summary>Aim stability loss per point of accumulated damage (arms / torso).</summary>
        public float AimDamageFactor;

        public ZoneDamageRule(HitZone zone, float multiplier, bool lethal, float bleed,
            float mobilityFactor = 0f, float aimFactor = 0f)
        {
            Zone = zone;
            DamageMultiplier = multiplier;
            LethalWhenPenetrated = lethal;
            BleedChanceMultiplier = bleed;
            MobilityDamageFactor = mobilityFactor;
            AimDamageFactor = aimFactor;
        }
    }

    /// <summary>One damage request, fully described by server known data.</summary>
    public struct DamageRequest
    {
        public BulletDefinition Bullet;
        public HitZone Zone;
        public ArmorState Armor;
        public float DistanceM;

        /// <summary>Angle between the bullet direction and the surface/body normal, degrees.</summary>
        public float AngleFromNormalDeg;

        /// <summary>Soft cover the bullet already crossed (null/none for a clean hit).</summary>
        public MaterialDefinition InterposedMaterial;
        public float InterposedThicknessMm;

        /// <summary>How suppressed the shooter is (affects nothing on damage, kept for telemetry).</summary>
        public float ShooterSuppression;

        public DeterministicRandom Random;

        public static DamageRequest Direct(BulletDefinition bullet, HitZone zone, ArmorState armor,
            float distanceM, DeterministicRandom random) => new DamageRequest
        {
            Bullet = bullet,
            Zone = zone,
            Armor = armor,
            DistanceM = distanceM,
            AngleFromNormalDeg = 0f,
            Random = random
        };
    }

    /// <summary>Outcome of a damage request.</summary>
    public struct DamageResult
    {
        public bool Accepted;
        public string RejectReason;
        public float Damage;
        public bool Fatal;
        public HitZone Zone;
        public bool PenetratedArmor;
        public bool BlockedByArmor;
        public bool Ricochet;
        public float ArmorDurabilityDamage;
        public float BleedDamagePerSecond;
        public float SuppressionAdded;
        public float MobilityPenalty;
        public float AimPenalty;
        public PenetrationResult Penetration;

        public static DamageResult Rejected(string reason) => new DamageResult
        {
            Accepted = false,
            RejectReason = reason ?? string.Empty
        };
    }

    /// <summary>
    /// Server authoritative damage resolution: distance falloff, zone multipliers,
    /// armour by zone, cover penetration by material and angle, bleeding, and the
    /// suppression a hit (or a near miss) applies to the victim.
    /// </summary>
    public sealed class DamageModel
    {
        public const float BaseHealth = 100f;

        /// <summary>Near misses within this radius build suppression instead of damage.</summary>
        public const float SuppressionRadiusM = 3.5f;

        private readonly ZoneDamageRule[] _zones;

        public DamageModel(ZoneDamageRule[] zones = null)
        {
            _zones = zones ?? DefaultZones();
        }

        public static ZoneDamageRule[] DefaultZones() => new[]
        {
            new ZoneDamageRule(HitZone.Head, 4.0f, true, 0f, 0f, 0.002f),
            new ZoneDamageRule(HitZone.Neck, 3.4f, true, 2.5f, 0f, 0.002f),
            new ZoneDamageRule(HitZone.Thorax, 1.0f, false, 1f, 0f, 0.0012f),
            new ZoneDamageRule(HitZone.Abdomen, 1.15f, false, 1.6f, 0.0006f, 0.0012f),
            new ZoneDamageRule(HitZone.Pelvis, 1.05f, false, 1.8f, 0.0025f, 0.001f),
            new ZoneDamageRule(HitZone.LeftArm, 0.72f, false, 1.1f, 0.0008f, 0.0032f),
            new ZoneDamageRule(HitZone.RightArm, 0.72f, false, 1.1f, 0.0008f, 0.0032f),
            new ZoneDamageRule(HitZone.LeftLeg, 0.68f, false, 1.2f, 0.0042f, 0f),
            new ZoneDamageRule(HitZone.RightLeg, 0.68f, false, 1.2f, 0.0042f, 0f),
            new ZoneDamageRule(HitZone.Gear, 0.25f, false, 0f, 0f, 0f)
        };

        public ZoneDamageRule RuleFor(HitZone zone)
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                if (_zones[i].Zone == zone)
                {
                    return _zones[i];
                }
            }

            return new ZoneDamageRule(zone, 1f, false, 1f);
        }

        public DamageResult Resolve(DamageRequest request)
        {
            if (request.Bullet == null)
            {
                return DamageResult.Rejected("no-bullet");
            }

            if (!request.Zone.IsValid())
            {
                return DamageResult.Rejected("invalid-zone");
            }

            if (request.DistanceM < 0f || float.IsNaN(request.DistanceM) || float.IsInfinity(request.DistanceM))
            {
                return DamageResult.Rejected("invalid-distance");
            }

            ZoneDamageRule rule = RuleFor(request.Zone);
            ArmorKitDefinition kit = request.Armor?.Kit;
            ZoneArmor plate = request.Armor == null
                ? new ZoneArmor(request.Zone, 0, 0f, 0f)
                : new ZoneArmor(request.Zone, request.Armor.ArmorClass(request.Zone),
                                request.Armor.Coverage(request.Zone),
                                request.Armor.Durability(request.Zone));

            // An exhausted plate stops protecting.
            int armorClass = plate.Durability <= 0f ? 0 : plate.ArmorClass;

            PenetrationQuery query = new PenetrationQuery
            {
                Bullet = request.Bullet,
                Material = request.InterposedMaterial ?? MaterialDefinition.Default(MaterialKind.Flesh),
                ThicknessMm = request.InterposedThicknessMm,
                AngleFromNormalDeg = request.AngleFromNormalDeg,
                ArmorClass = armorClass,
                ArmorCoverage = armorClass > 0 ? plate.Coverage : 0f,
                TravelDistanceM = request.DistanceM
            };

            PenetrationResult pen = PenetrationSolver.Solve(query, request.Random);

            DamageResult result = new DamageResult
            {
                Accepted = true,
                Zone = request.Zone,
                Penetration = pen,
                Ricochet = pen.Ricochet,
                ArmorDurabilityDamage = pen.ArmorDurabilityDamage,
                PenetratedArmor = pen.Penetrated && armorClass > 0,
                BlockedByArmor = pen.BlockedByArmor
            };

            if (pen.Ricochet)
            {
                result.Damage = 0f;
                result.SuppressionAdded = SuppressionFromNearMiss(0.6f);
                return result;
            }

            float distanceMultiplier = request.Bullet.DamageMultiplierAtDistance(request.DistanceM);
            float damage = request.Bullet.BaseDamage * distanceMultiplier * rule.DamageMultiplier
                           * pen.ResidualDamageMultiplier;

            if (request.Random != null && request.Random.Chance(request.Bullet.FragmentationChance) &&
                pen.Penetrated && request.Zone != HitZone.Gear)
            {
                damage *= request.Bullet.FragmentationDamageMultiplier;
            }

            // Vital zone + real penetration = immediately fatal, as specified.
            bool fatal = damage >= BaseHealth;
            if (rule.LethalWhenPenetrated && pen.Penetrated)
            {
                fatal = true;
            }

            result.Damage = Math.Max(0f, damage);
            result.Fatal = fatal;

            if (!fatal && request.Bullet.BleedChance > 0f && rule.BleedChanceMultiplier > 0f &&
                pen.Penetrated && request.Random != null)
            {
                float bleedChance = MathUtil.Clamp01(request.Bullet.BleedChance * rule.BleedChanceMultiplier);
                if (request.Random.Chance(bleedChance))
                {
                    result.BleedDamagePerSecond = 0.35f + 0.9f * rule.BleedChanceMultiplier * 0.2f;
                }
            }

            result.MobilityPenalty = rule.MobilityDamageFactor * result.Damage;
            result.AimPenalty = rule.AimDamageFactor * result.Damage;
            result.SuppressionAdded = SuppressionFromHit(result.Damage);
            return result;
        }

        /// <summary>Suppression produced by a hit on the victim.</summary>
        public static float SuppressionFromHit(float damage) => MathUtil.Clamp01(0.25f + damage * 0.006f);

        /// <summary>Suppression produced by a bullet passing close without hitting.</summary>
        public static float SuppressionFromNearMiss(float missDistanceM)
        {
            float t = 1f - MathUtil.Clamp01(missDistanceM / SuppressionRadiusM);
            return MathUtil.Clamp01(0.55f * t * t + 0.12f * t);
        }
    }
}
