using System;
using Kordon.Core.Combat;
using Kordon.Core.Utility;

namespace Kordon.Core.Movement
{
    /// <summary>Kind of sound event emitted into the AI hearing system.</summary>
    public enum NoiseKind
    {
        None = 0,
        Footstep = 1,
        Landing = 2,
        Jump = 3,
        WeaponHandling = 4,
        Reload = 5,
        MagazineInspect = 6,
        Door = 7,
        BulletImpact = 8,
        Gunshot = 9,
        Grenade = 10,
        Explosion = 11,
        Voice = 12,
        GlassBreak = 13,
        Machinery = 14
    }

    /// <summary>One audible event. The AI hearing system uses Position + RadiusM.</summary>
    public struct NoiseEvent
    {
        public NoiseKind Kind;
        public KordonVec3 Position;

        /// <summary>Distance in metres at which an AI can still hear this event.</summary>
        public float RadiusM;

        /// <summary>0..1 perceived loudness at the listener (computed by the receiver).</summary>
        public float Loudness;

        /// <summary>Audio surface id used to pick the right sample bank (concrete, metal, gravel ...).</summary>
        public string SurfaceId;

        public bool IsSilent => RadiusM <= 0.001f;

        public float LoudnessAt(KordonVec3 listener)
        {
            float distance = KordonVec3.Distance(Position, listener);
            if (distance >= RadiusM)
            {
                return 0f;
            }

            return MathUtil.Clamp01(1f - distance / RadiusM);
        }
    }

    /// <summary>
    /// Audibility model.  There is no silent movement in this game: every footstep,
    /// reload and handling action emits a radius that depends on surface, posture,
    /// gait and how much gear the operator carries.
    /// </summary>
    public static class NoiseModel
    {
        public const float WalkRadius = 9.5f;
        public const float SprintRadius = 23f;
        public const float CrouchWalkRadius = 4.6f;
        public const float ProneCrawlRadius = 2.2f;
        public const float ReloadRadius = 6.5f;
        public const float InspectRadius = 4.2f;
        public const float GunshotRadius = 120f;
        public const float SuppressedGunshotRadius = 42f;
        public const float ImpactRadius = 24f;
        public const float ExplosionRadius = 90f;
        public const float VoiceLocalRadius = 28f;

        /// <summary>Surface loudness multipliers keyed by audio surface id.</summary>
        public static float SurfaceMultiplier(string surfaceId)
        {
            switch (surfaceId)
            {
                case "metal": return 1.4f;
                case "grating": return 1.5f;
                case "water": return 1.3f;
                case "gravel": return 1.22f;
                case "wood": return 1.1f;
                case "glass": return 1.15f;
                case "sand": return 0.7f;
                case "cloth": return 0.5f;
                case "rubber": return 0.62f;
                case "concrete": return 1f;
                default: return 1f;
            }
        }

        public static float GaitRadius(Gait gait)
        {
            switch (gait)
            {
                case Gait.Sprint: return SprintRadius;
                case Gait.Walk: return WalkRadius;
                case Gait.CrouchWalk: return CrouchWalkRadius;
                case Gait.ProneCrawl: return ProneCrawlRadius;
                default: return 0f;
            }
        }

        /// <summary>
        /// Footstep audibility radius.  Gear mass and armour make you louder: a heavy
        /// assault kit cannot move as quietly as a light one.
        /// </summary>
        public static float FootstepRadius(Gait gait, string surfaceId, float gearMassKg,
            float armorNoiseMultiplier = 1f, float referenceGearMassKg = 14f)
        {
            float baseRadius = GaitRadius(gait);
            if (baseRadius <= 0f)
            {
                return 0f;
            }

            float mass = 1f + Math.Max(0f, gearMassKg - referenceGearMassKg) * 0.035f;
            mass = MathUtil.Clamp(mass, 0.85f, 1.6f);
            return baseRadius * SurfaceMultiplier(surfaceId) * mass *
                   MathUtil.Clamp(armorNoiseMultiplier, 0.6f, 2f);
        }

        /// <summary>Seconds between footsteps for the current gait and speed.</summary>
        public static float FootstepInterval(Gait gait, float speedMps)
        {
            float stride;
            switch (gait)
            {
                case Gait.Sprint: stride = 1.55f; break;
                case Gait.Walk: stride = 1.25f; break;
                case Gait.CrouchWalk: stride = 0.85f; break;
                case Gait.ProneCrawl: stride = 0.75f; break;
                default: return float.MaxValue;
            }

            return stride / Math.Max(0.25f, speedMps);
        }

        public static float LandingRadius(float fallHeightM)
        {
            if (fallHeightM <= 0.4f)
            {
                return 0f;
            }

            return MathUtil.Clamp(fallHeightM * 5.5f, 3f, 26f);
        }

        public static float WeaponHandlingRadius(float gearMassKg) =>
            MathUtil.Clamp(3.5f + gearMassKg * 0.12f, 3f, 7f);

        public static NoiseEvent MakeFootstep(Gait gait, string surfaceId, KordonVec3 position,
            float gearMassKg, float armorNoiseMultiplier) => new NoiseEvent
        {
            Kind = NoiseKind.Footstep,
            Position = position,
            RadiusM = FootstepRadius(gait, surfaceId, gearMassKg, armorNoiseMultiplier),
            SurfaceId = surfaceId,
            Loudness = 1f
        };

        public static NoiseEvent MakeGunshot(KordonVec3 position, bool suppressed, string surfaceId = null) =>
            new NoiseEvent
            {
                Kind = NoiseKind.Gunshot,
                Position = position,
                RadiusM = suppressed ? SuppressedGunshotRadius : GunshotRadius,
                SurfaceId = surfaceId,
                Loudness = suppressed ? 0.55f : 1f
            };

        public static NoiseEvent MakeImpact(KordonVec3 position, string surfaceId) => new NoiseEvent
        {
            Kind = NoiseKind.BulletImpact,
            Position = position,
            RadiusM = ImpactRadius * SurfaceMultiplier(surfaceId),
            SurfaceId = surfaceId,
            Loudness = 0.7f
        };
    }
}
