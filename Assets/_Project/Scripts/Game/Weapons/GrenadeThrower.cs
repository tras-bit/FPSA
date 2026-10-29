using System;
using Kordon.Core.Ammo;
using Kordon.Core.Combat;
using Kordon.Core.Loadout;
using Kordon.Game.Audio;
using Kordon.Game.Camera;
using Kordon.Game.Input;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>
    /// Grenade handling: select the grenade slot (3), hold the throw key to cook a
    /// cookable grenade, release to throw.  The fuze keeps burning while the grenade
    /// is in the air, so cooking is a real risk - but the Core model never lets it
    /// drop below the grenade's own safe minimum, so you cannot commit suicide by
    /// holding the button too long.
    ///
    /// Offline the throw is simulated and resolved here.  Online the same throw is
    /// turned into a server intent (<see cref="NetworkThrow"/>) and the server runs
    /// the exact same Core physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrenadeThrower : MonoBehaviour
    {
        public WeaponController Weapons;
        public PlayerController Body;
        public BodycamRig Rig;
        public AudioDirector Audio;

        /// <summary>Minimum time between two throws, mirroring the server rule.</summary>
        public const float ThrowCooldownSeconds = GrenadeTrajectory.MinThrowIntervalSeconds;

        /// <summary>
        /// Returns true when the throw has been handed to the authoritative server
        /// (the caller must not resolve it locally).
        /// </summary>
        public Func<GrenadeDefinition, Vector3, Vector3, float, bool> NetworkThrow;

        /// <summary>Raised after a blast resolved locally, for scoring and feedback.</summary>
        public event Action<PlayerController, BlastResult> BlastResolved;

        public event Action<GrenadeDefinition, Vector3, Vector3> Thrown;

        public bool IsCooking { get; private set; }
        public float CookSeconds { get; private set; }
        public float LastThrowTime { get; private set; } = float.NegativeInfinity;

        private GrenadeProjectile _live;
        private Transform _projectileRoot;
        private float _cookStartedAt;

        public GrenadeDefinition HeldGrenade =>
            Weapons != null && Weapons.Loadout != null ? Weapons.Loadout.Grenade : null;

        public int Remaining =>
            Weapons != null && Weapons.Loadout != null ? Weapons.Loadout.GrenadesRemaining : 0;

        private void Update()
        {
            if (Weapons == null || Body == null || Weapons.Loadout == null)
            {
                return;
            }

            if (Body.IsDead || Weapons.Loadout.Active != ActiveSlot.Grenade || Remaining <= 0)
            {
                CancelCook();
                return;
            }

            GrenadeDefinition grenade = HeldGrenade;
            if (grenade == null)
            {
                CancelCook();
                return;
            }

            if (!IsCooking && KordonInput.FirePressed)
            {
                BeginCook(grenade);
            }

            if (!IsCooking)
            {
                return;
            }

            CookSeconds = Mathf.Max(0f, Time.time - _cookStartedAt);

            if (KordonInput.FireReleased || CookSeconds >= grenade.FuseSeconds)
            {
                Throw(grenade);
            }
        }

        private void BeginCook(GrenadeDefinition grenade)
        {
            // Smoke and flash are not cookable: they leave the hand immediately.
            if (!grenade.AllowCooking)
            {
                Throw(grenade);
                return;
            }

            IsCooking = true;
            CookSeconds = 0f;
            _cookStartedAt = Time.time;
            Audio?.PlayGrenadePin(Body.transform.position);
        }

        private void CancelCook()
        {
            IsCooking = false;
            CookSeconds = 0f;
        }

        /// <summary>Throw the held grenade along the bodycam's aim.</summary>
        public bool Throw(GrenadeDefinition grenade)
        {
            if (grenade == null || Weapons == null || Weapons.Loadout == null)
            {
                CancelCook();
                return false;
            }

            float now = Time.time;
            if (now - LastThrowTime < ThrowCooldownSeconds)
            {
                CancelCook();
                return false;
            }

            if (!Weapons.Loadout.TryThrowGrenade(now))
            {
                CancelCook();
                return false;
            }

            float cook = Mathf.Max(0f, Time.time - _cookStartedAt);
            float fuse = GrenadeTrajectory.CookedFuse(grenade, IsCooking ? cook : 0f);
            Vector3 origin = MuzzlePoint();
            Vector3 direction = Rig != null ? Rig.transform.forward : Body.transform.forward;
            LastThrowTime = now;
            CancelCook();

            Audio?.PlayGrenadePin(origin);
            Thrown?.Invoke(grenade, origin, direction);

            bool handedToServer = NetworkThrow != null && NetworkThrow(grenade, origin, direction, fuse);
            if (handedToServer)
            {
                return true;
            }

            _live = GrenadeProjectile.Spawn(ProjectileRoot(), grenade, origin, direction, cook,
                true, Audio);
            if (_live != null)
            {
                _live.Detonated += OnLocalDetonation;
            }

            return true;
        }

        private void OnLocalDetonation(GrenadeProjectile projectile, Vector3 position)
        {
            if (projectile != null)
            {
                projectile.Detonated -= OnLocalDetonation;
            }

            if (_live == projectile)
            {
                _live = null;
            }

            GrenadeDefinition grenade = projectile != null ? projectile.Definition : null;
            if (grenade == null)
            {
                return;
            }

            GrenadeEffects.Detonate(ProjectileRoot(), grenade, position, Audio);
            int affected = GrenadeBlast.ApplyLocal(grenade, position, Body,
                (target, blast) => BlastResolved?.Invoke(target, blast));

            if (Rig != null)
            {
                float distance = Vector3.Distance(Rig.transform.position, position);
                if (distance < grenade.SuppressionRadiusM)
                {
                    float shake = Mathf.Max(0.4f,
                        1f - distance / Mathf.Max(1f, grenade.SuppressionRadiusM));
                    Rig.SetSuppression(shake);
                }
            }

            if (affected == 0)
            {
                Debug.Log("[kordon] blast at " + position + " hit nothing");
            }
        }

        private Vector3 MuzzlePoint()
        {
            Transform source = Rig != null ? Rig.transform : Body.transform;
            return source.position + source.forward * 0.42f + Vector3.up * -0.06f;
        }

        private Transform ProjectileRoot()
        {
            if (_projectileRoot != null)
            {
                return _projectileRoot;
            }

            GameObject root = new GameObject("Grenades");
            root.transform.SetParent(transform.parent, false);
            _projectileRoot = root.transform;
            return _projectileRoot;
        }
    }
}
