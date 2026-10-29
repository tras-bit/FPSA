using System;
using System.Collections.Generic;
using Kordon.Core.Combat;
using Kordon.Core.Loadout;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>
    /// Blast resolution for the local game (practice / offline).  The online match
    /// never calls this on a client: the server repeats exactly the same steps against
    /// its own bodies, using the very same <see cref="BlastModel"/>.
    /// </summary>
    public static class GrenadeBlast
    {
        private const int MaxTargets = 32;

        /// <summary>Evaluate one body against one detonation (distance, cover, plates).</summary>
        public static BlastResult Evaluate(GrenadeDefinition grenade, Vector3 explosionPoint,
            Vector3 bodyPosition, float eyeHeight, ArmorState armor, bool selfInflicted,
            Transform target = null)
        {
            Vector3 chest = bodyPosition + Vector3.up * Mathf.Max(0.9f, eyeHeight * 0.72f);
            float distance = Vector3.Distance(explosionPoint, chest);
            bool lineOfSight = HasLineOfSight(explosionPoint, chest, target);

            return new BlastModel().Resolve(new BlastRequest
            {
                Grenade = grenade,
                DistanceM = distance,
                HasLineOfSight = lineOfSight,
                CoverFactor = lineOfSight ? 0f : 1f,
                ArmorClass = BlastModel.AverageArmorClass(armor),
                SelfInflicted = selfInflicted,
            });
        }

        /// <summary>
        /// True when the blast has a clear path to the chest.  A ray that ends on the
        /// target's own body counts as clear: it is the target, not cover.
        /// </summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 start = from + Vector3.up * 0.08f;
            if (!Physics.Linecast(start, to, out RaycastHit hit))
            {
                return true;
            }

            if (hit.collider == null)
            {
                return true;
            }

            if (target != null &&
                (hit.collider.transform == target || hit.collider.transform.IsChildOf(target)))
            {
                // The ray ended on the target itself: that is the body, not cover.
                return true;
            }

            return false;
        }

        /// <summary>
        /// Apply a grenade to every body inside its effect radius.  Returns the number
        /// of bodies that felt something, and reports each one so the caller can log,
        /// score or animate it.
        /// </summary>
        public static int ApplyLocal(GrenadeDefinition grenade, Vector3 explosionPoint,
            PlayerController source, Action<PlayerController, BlastResult> onHit = null)
        {
            if (grenade == null)
            {
                return 0;
            }

            float radius = Mathf.Max(grenade.DamageRadiusM,
                Mathf.Max(grenade.SuppressionRadiusM, grenade.EffectRadiusM));
            if (radius <= 0.01f)
            {
                return 0;
            }

            Collider[] colliders = Physics.OverlapSphere(explosionPoint, radius + 1.5f);
            List<PlayerController> seen = new List<PlayerController>(MaxTargets);
            int affected = 0;

            for (int i = 0; i < colliders.Length && seen.Count < MaxTargets; i++)
            {
                PlayerController target = colliders[i] != null
                    ? colliders[i].GetComponentInParent<PlayerController>()
                    : null;
                if (target == null || target.IsDead || seen.Contains(target))
                {
                    continue;
                }

                seen.Add(target);
                BlastResult blast = Evaluate(grenade, explosionPoint, target.transform.position,
                    target.EyeHeight(), target.Armor, target == source, target.transform);
                if (!blast.HasEffect)
                {
                    continue;
                }

                DamageResult damage = blast.ToDamageResult();
                if (damage.Accepted)
                {
                    target.ApplyHit(damage, DeathCause.Explosion);
                }
                else if (blast.Suppression > 0f)
                {
                    // Behind cover: no fragments, but the blast still rattles you.
                    target.Vitals.Suppression.Add(blast.Suppression);
                }

                affected++;
                onHit?.Invoke(target, blast);
            }

            return affected;
        }
    }
}
