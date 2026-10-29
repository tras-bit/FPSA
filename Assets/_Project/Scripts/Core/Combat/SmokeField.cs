using System;
using System.Collections.Generic;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>One smoke cloud in the world, with the moment it stops existing.</summary>
    public struct SmokeCloud
    {
        public KordonVec3 Center;
        public float RadiusM;
        public float ExpiresAt;

        public bool IsActive(float now) => now < ExpiresAt && RadiusM > 0.01f;
    }

    /// <summary>
    /// The smoke volume of a match.  Smoke is not decoration: a cloud is a real
    /// occlusion volume, so a hostile AI cannot see through it and neither can the
    /// player.  The geometry is a plain segment-versus-sphere test, which keeps the
    /// rule deterministic and cheap enough to run for every perception tick.
    ///
    /// Clouds are keyed by time, never by engine state, so the server and the
    /// offline game agree on when a cloud is gone.
    /// </summary>
    public sealed class SmokeField
    {
        private readonly List<SmokeCloud> _clouds = new List<SmokeCloud>();

        public IReadOnlyList<SmokeCloud> Clouds => _clouds;

        public int Count => _clouds.Count;

        public void Add(KordonVec3 center, float radiusM, float expiresAt)
        {
            if (radiusM <= 0.01f)
            {
                return;
            }

            _clouds.Add(new SmokeCloud
            {
                Center = center,
                RadiusM = radiusM,
                ExpiresAt = expiresAt,
            });
        }

        /// <summary>Drop clouds whose lifetime ran out. Call once per tick, not per query.</summary>
        public void Prune(float now)
        {
            for (int i = _clouds.Count - 1; i >= 0; i--)
            {
                if (!_clouds[i].IsActive(now))
                {
                    _clouds.RemoveAt(i);
                }
            }
        }

        public void Clear() => _clouds.Clear();

        /// <summary>True when any active cloud sits on the line between the two points.</summary>
        public bool BlocksSegment(KordonVec3 from, KordonVec3 to, float now)
        {
            for (int i = 0; i < _clouds.Count; i++)
            {
                SmokeCloud cloud = _clouds[i];
                if (!cloud.IsActive(now))
                {
                    continue;
                }

                if (SegmentIntersectsSphere(from, to, cloud.Center, cloud.RadiusM))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Closest distance from a point to a line segment.</summary>
        public static float DistanceToSegment(KordonVec3 point, KordonVec3 a, KordonVec3 b)
        {
            KordonVec3 ab = b - a;
            float lengthSquared = ab.SqrMagnitude;
            if (lengthSquared <= 1e-8f)
            {
                return KordonVec3.Distance(point, a);
            }

            float t = MathUtil.Clamp01(KordonVec3.Dot(point - a, ab) / lengthSquared);
            KordonVec3 closest = a + ab * t;
            return KordonVec3.Distance(point, closest);
        }

        public static bool SegmentIntersectsSphere(KordonVec3 a, KordonVec3 b, KordonVec3 center,
            float radius)
        {
            if (radius <= 0f)
            {
                return false;
            }

            // A cloud that already swallowed an eye blinds that eye: the segment test
            // alone would report "no hit" for a point inside the sphere.
            if (KordonVec3.Distance(a, center) <= radius ||
                KordonVec3.Distance(b, center) <= radius)
            {
                return true;
            }

            return DistanceToSegment(center, a, b) <= radius;
        }
    }
}
