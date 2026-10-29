using System;
using Kordon.Core.Loadout;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>One flight step of a thrown grenade.  Pure data, no engine types.</summary>
    public struct GrenadeFlightState
    {
        public KordonVec3 Position;
        public KordonVec3 Velocity;
        public float FuseRemaining;
        public bool Resting;
        public float Age;

        public bool FuseExpired => FuseRemaining <= 0f;
        public float Speed => Velocity.Magnitude;

        public GrenadeFlightState At(KordonVec3 position)
        {
            GrenadeFlightState copy = this;
            copy.Position = position;
            return copy;
        }
    }

    /// <summary>
    /// Deterministic throw physics: a ballistic arc, a bounce with energy loss and a
    /// timed fuze.  Both the offline game and the authoritative server run this exact
    /// code, so a grenade lands where it was thrown regardless of frame rate, and a
    /// client cannot invent a curved throw or an instant detonation.
    ///
    /// Collision itself stays with the engine: the world knows its own geometry, this
    /// model only needs the impact normal (and reports the impact speed back so the
    /// presentation layer can play the right bounce sound).
    /// </summary>
    public static class GrenadeTrajectory
    {
        public const float GravityMS2 = 9.81f;

        /// <summary>Fixed integration step; servers accumulate time and run whole steps.</summary>
        public const float FixedStep = 1f / 60f;

        /// <summary>Minimum time between two throws from the same operator (server rule).</summary>
        public const float MinThrowIntervalSeconds = 0.45f;

        /// <summary>Hard ceiling on any thrown grenade. Server side anti-cheat re-checks it.</summary>
        public const float MaxThrowSpeedMS = 26f;

        /// <summary>Below this speed the grenade is considered to have come to rest.</summary>
        public const float RestSpeedMS = 0.85f;

        public const float DefaultRestitution = 0.38f;
        public const float DefaultFriction = 0.66f;

        /// <summary>A throw is never perfectly flat: a small upward bias makes it usable.</summary>
        public const float DefaultUpwardBias = 0.14f;

        /// <summary>Throw direction = aim direction + upward bias, normalised.</summary>
        public static KordonVec3 LaunchVelocity(float throwSpeed, KordonVec3 aimDirection,
            float upwardBias = DefaultUpwardBias)
        {
            KordonVec3 aim = aimDirection.Magnitude > 0.0001f ? aimDirection.Normalized() : KordonVec3.Forward;
            float speed = MathUtil.Clamp(throwSpeed, 0f, MaxThrowSpeedMS);
            KordonVec3 direction = new KordonVec3(aim.X, aim.Y + MathUtil.Clamp(upwardBias, 0f, 1f), aim.Z);
            if (direction.Magnitude <= 0.0001f)
            {
                direction = aim;
            }

            return direction.Normalized() * speed;
        }

        /// <summary>
        /// Remaining fuze after cooking.  Holding a cookable grenade burns the fuze
        /// down, but never below <see cref="GrenadeDefinition.CookableMinFuse"/> - a
        /// grenade that explodes in the hand is a rule the design does not want.
        /// </summary>
        public static float CookedFuse(GrenadeDefinition grenade, float cookSeconds)
        {
            if (grenade == null)
            {
                return 0f;
            }

            if (!grenade.AllowCooking)
            {
                return Math.Max(0f, grenade.FuseSeconds);
            }

            float cooked = Math.Max(0f, cookSeconds);
            float floor = Math.Max(0.05f, grenade.CookableMinFuse);
            return MathUtil.Clamp(grenade.FuseSeconds - cooked, floor, Math.Max(floor, grenade.FuseSeconds));
        }

        /// <summary>Start a flight from the operator's hand.</summary>
        public static GrenadeFlightState Launch(GrenadeDefinition grenade, KordonVec3 origin,
            KordonVec3 aimDirection, float cookSeconds)
        {
            return new GrenadeFlightState
            {
                Position = origin,
                Velocity = LaunchVelocity(grenade != null ? grenade.ThrowSpeed : 12f, aimDirection),
                FuseRemaining = CookedFuse(grenade, cookSeconds),
                Resting = false,
                Age = 0f,
            };
        }

        /// <summary>Gravity, drag and fuze.  Position is integrated after the velocity update.</summary>
        public static void Integrate(ref GrenadeFlightState state, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // The fuze keeps burning on the ground: a resting grenade still explodes.
            state.FuseRemaining = Math.Max(0f, state.FuseRemaining - deltaTime);
            state.Age += deltaTime;

            if (state.Resting)
            {
                state.Velocity = KordonVec3.Zero;
                return;
            }

            KordonVec3 velocity = state.Velocity;
            velocity.Y -= GravityMS2 * deltaTime;

            // Light air drag keeps long throws from flying forever without changing the arc.
            velocity *= Math.Max(0f, 1f - 0.06f * deltaTime);

            state.Velocity = velocity;
            state.Position += velocity * deltaTime;
        }

        /// <summary>
        /// Reflect the flight off a surface.  Returns true when the grenade bounced,
        /// false when it came to rest on that surface (then <see cref="GrenadeFlightState.Resting"/> is set).
        /// </summary>
        public static bool ResolveImpact(ref GrenadeFlightState state, KordonVec3 surfaceNormal,
            out float impactSpeed, float restitution = DefaultRestitution,
            float friction = DefaultFriction)
        {
            impactSpeed = 0f;
            KordonVec3 normal = surfaceNormal.Magnitude > 0.0001f
                ? surfaceNormal.Normalized()
                : KordonVec3.Up;

            float normalSpeed = KordonVec3.Dot(state.Velocity, normal);
            if (normalSpeed >= 0f)
            {
                // Already separating: nothing to bounce off.
                return false;
            }

            impactSpeed = -normalSpeed;
            KordonVec3 velocity = state.Velocity;
            KordonVec3 normalComponent = normal * normalSpeed;
            KordonVec3 tangent = velocity - normalComponent;

            float bounce = MathUtil.Clamp(restitution, 0f, 0.95f);
            float slide = MathUtil.Clamp(1f - friction, 0f, 1f);
            KordonVec3 reflected = tangent * slide - normalComponent * bounce;

            if (reflected.Magnitude < RestSpeedMS || impactSpeed < RestSpeedMS)
            {
                state.Velocity = KordonVec3.Zero;
                state.Resting = true;
                return false;
            }

            state.Velocity = reflected;
            state.Resting = false;
            return true;
        }

        /// <summary>
        /// Advance a flight in whole fixed steps and report a detonation position when
        /// the fuze burns out.  Collision is resolved by the caller through
        /// <see cref="ResolveImpact"/>; this helper exists for the server, which only
        /// needs "did it go off, and where".
        /// </summary>
        public static bool TryDetonate(ref GrenadeFlightState state, float deltaTime,
            out KordonVec3 detonationPosition)
        {
            detonationPosition = state.Position;
            float remaining = Math.Max(0f, deltaTime);
            while (remaining > 0f)
            {
                float step = Math.Min(FixedStep, remaining);
                Integrate(ref state, step);
                remaining -= step;
                if (state.FuseExpired)
                {
                    detonationPosition = state.Position;
                    return true;
                }
            }

            return state.FuseExpired;
        }

        /// <summary>True when the flight state is physically plausible for a thrown grenade.</summary>
        public static bool IsPlausible(GrenadeFlightState state)
        {
            if (state.Speed > MaxThrowSpeedMS + 1.5f)
            {
                return false;
            }

            if (float.IsNaN(state.Position.X) || float.IsNaN(state.Position.Y) || float.IsNaN(state.Position.Z))
            {
                return false;
            }

            return state.Position.Y > -40f && state.Position.Y < 900f;
        }
    }
}
