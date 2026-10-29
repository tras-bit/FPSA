using System;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>What a grenade does to one body when it goes off.</summary>
    public struct BlastRequest
    {
        public GrenadeDefinition Grenade;

        /// <summary>Distance from the detonation to the target's chest, in metres.</summary>
        public float DistanceM;

        /// <summary>True when nothing solid stands between the fragments and the target.</summary>
        public bool HasLineOfSight;

        /// <summary>0 = fully exposed, 1 = behind solid cover that still lets the blast through.</summary>
        public float CoverFactor;

        /// <summary>Average armour class of the torso plates (0..6); 0 for an unarmoured operator.</summary>
        public float ArmorClass;

        /// <summary>Thrown by the same operator that is being hit (own grenade).</summary>
        public bool SelfInflicted;

        public static BlastRequest Exposed(GrenadeDefinition grenade, float distanceM)
        {
            return new BlastRequest
            {
                Grenade = grenade,
                DistanceM = distanceM,
                HasLineOfSight = true,
                CoverFactor = 0f,
                ArmorClass = 0f,
            };
        }
    }

    /// <summary>Outcome of one blast against one target.</summary>
    public struct BlastResult
    {
        public bool Accepted;
        public string GrenadeId;
        public GrenadeKind Kind;
        public float DistanceM;
        public float Damage;
        public bool Fatal;
        public float Suppression;
        public bool DamageBlocked;
        public bool Blinded;
        public float BlindSeconds;
        public float EffectRadiusM;
        public float EffectSeconds;
        public bool BlocksVision;
        public bool SelfInflicted;

        public bool HasEffect => Damage > 0f || Suppression > 0f || Blinded || BlocksVision;

        /// <summary>Feeds the same <see cref="PlayerVitals"/> path bullets use.</summary>
        public DamageResult ToDamageResult()
        {
            return new DamageResult
            {
                Accepted = Accepted && (Damage > 0f || Fatal),
                Damage = Damage,
                Fatal = Fatal,
                Zone = HitZone.Thorax,
                SuppressionAdded = Suppression,
                BleedDamagePerSecond = Damage >= 20f ? 0.35f : 0f,
                BlockedByArmor = DamageBlocked || Damage < 1f,
            };
        }
    }

    /// <summary>
    /// Fragmentation, smoke and flash rules.  Blast damage is deterministic and
    /// readable: a quadratic falloff from the lethal radius to the damage radius,
    /// hard cover saves your life entirely, armour only softens fragments, and the
    /// flash only blinds what it can actually see.  Nothing here is random.
    /// </summary>
    public sealed class BlastModel
    {
        /// <summary>How much a full set of torso plates removes from fragment damage.</summary>
        public const float MaxArmorReduction = 0.35f;

        /// <summary>Cover that still lets the pressure wave through: damage is scaled by this floor.</summary>
        public const float CoverFloor = 0.12f;

        /// <summary>Suppression survives walls, but only this much of it.</summary>
        public const float ShieldedSuppressionFraction = 0.5f;

        public BlastResult Resolve(BlastRequest request)
        {
            BlastResult result = new BlastResult
            {
                GrenadeId = request.Grenade?.Id,
                Kind = request.Grenade?.Kind ?? GrenadeKind.None,
                DistanceM = Math.Max(0f, request.DistanceM),
                SelfInflicted = request.SelfInflicted,
            };

            if (request.Grenade == null)
            {
                return result;
            }

            result.Accepted = true;
            float distance = Math.Max(0f, request.DistanceM);
            float cover = MathUtil.Clamp01(request.CoverFactor);
            bool shielded = request.Grenade.LineOfSightRequired && !request.HasLineOfSight;

            // ---------------------------------------------------------- fragments
            if (request.Grenade.Kind == GrenadeKind.Fragmentation)
            {
                float raw = request.Grenade.DamageAtDistance(distance);
                if (raw > 0f)
                {
                    if (shielded)
                    {
                        result.DamageBlocked = true;
                        raw = 0f;
                    }
                    else
                    {
                        float exposure = MathUtil.Lerp(1f, CoverFloor, cover);
                        float armor = MathUtil.Clamp01(request.ArmorClass / 6f) * MaxArmorReduction;
                        raw *= exposure * (1f - armor);
                    }
                }

                result.Damage = Math.Max(0f, raw);
                result.Fatal = result.Damage >= DamageModel.BaseHealth && !shielded &&
                               distance <= request.Grenade.LethalRadiusM;
            }

            // --------------------------------------------------------- suppression
            float suppressionRadius = request.Grenade.SuppressionRadiusM;
            if (suppressionRadius > 0.01f && distance < suppressionRadius)
            {
                float t = 1f - distance / suppressionRadius;
                float suppression = MathUtil.Clamp01(t) * 0.85f;
                suppression *= MathUtil.Lerp(1f, CoverFloor, cover);
                if (shielded)
                {
                    suppression *= ShieldedSuppressionFraction;
                }

                result.Suppression = MathUtil.Clamp01(suppression);
            }

            // ------------------------------------------------------- smoke / flash
            result.EffectRadiusM = request.Grenade.EffectRadiusM;
            result.EffectSeconds = request.Grenade.EffectDurationSeconds;
            if (request.Grenade.Kind == GrenadeKind.Smoke && distance <= request.Grenade.EffectRadiusM)
            {
                result.BlocksVision = true;
                result.EffectSeconds = request.Grenade.EffectDurationSeconds;
            }

            if (request.Grenade.Kind == GrenadeKind.Flashbang &&
                distance <= request.Grenade.EffectRadiusM && !shielded)
            {
                float closeness = 1f - MathUtil.Clamp01(distance / Math.Max(0.5f, request.Grenade.EffectRadiusM));
                result.Blinded = true;
                result.BlindSeconds = MathUtil.Clamp(
                    request.Grenade.EffectDurationSeconds * MathUtil.Lerp(0.25f, 1f, closeness),
                    0.35f, request.Grenade.EffectDurationSeconds);
            }

            return result;
        }

        /// <summary>Average torso armour class of a kit, used for fragment mitigation.</summary>
        public static float AverageArmorClass(ArmorState armor)
        {
            if (armor == null || armor.Kit == null)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;
            foreach (ZoneArmor zone in armor.Kit.Zones)
            {
                if (!zone.Zone.IsTorso())
                {
                    continue;
                }

                sum += zone.ArmorClass;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }
    }
}
