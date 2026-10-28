using Kordon.Core.Settings;
using System;
using System.Collections.Generic;
using Kordon.Core.Ammo;
using Kordon.Core.Ballistics;
using Kordon.Core.Combat;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Core.Utility;
using Kordon.Game.Camera;
using Kordon.Game.Input;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>Everything a shot needs to carry: direction, shooter state, target zone.</summary>
    public struct ShotInfo
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public string WeaponId;
        public string BulletId;
        public bool Penetrated;
        public int ShotIndex;
    }

    /// <summary>
    /// Runs the authored kit: the Core LoadoutKit owns magazines, reloads and the
    /// inspect interaction, the Recoil/Spread models own the shot, and this component
    /// only wires them to input, the bodycam and the world.  Server authoritative
    /// resolution lives in Kordon.Net and reuses the very same Core calls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponController : MonoBehaviour
    {
        public PlayerController Body;
        public BodycamRig Rig;
        public WeaponViewmodel Viewmodel;
        public Transform Muzzle;

        public LoadoutKit Loadout { get; private set; }
        public DeterministicRandom Random { get; private set; } = new DeterministicRandom(0xC0FFEEUL);

        public float Now { get; private set; }
        public bool IsAiming => _aimBlend > 0.6f;
        public float AimBlend => _aimBlend;
        public float GearMassKg => Loadout != null ? Loadout.TotalMassKg : 9f;

        public bool IsSealed => Loadout != null && Loadout.IsSealed;

        /// <summary>
        /// When true the local hitscan is presentation only: the server resolves the
        /// hit and the client waits for its confirmation. Set by the online session.
        /// </summary>
        public bool ServerAuthoritativeHits;
        public float MobilityMultiplier => Loadout != null ? Loadout.MobilityMultiplier : 1f;

        public event Action<AmmoHudSnapshot> HudChanged;
        public event Action<ShotInfo> ShotFired;
        public event Action<FireDenial> ShotDenied;
        public event Action<AmmoEvent> AmmoEventRaised;
        public event Action<PlayerController, DamageResult> PlayerHit;

        private readonly RecoilSolver _recoil = new RecoilSolver();
        private readonly SpreadModel _spread = new SpreadModel();
        private readonly List<AmmoEvent> _events = new List<AmmoEvent>();

        private float _aimBlend;
        private float _timeSinceSprint;
        private float _timeSinceShot = float.MaxValue;
        private FireMode _fireMode = FireMode.Auto;
        private float _handlingPenalty;
        public float HandlingPenalty => _handlingPenalty;

        public WeaponDefinition ActiveWeapon => Loadout != null ? Loadout.ActiveWeapon : null;

        public AmmoKit ActiveKit => Loadout != null ? Loadout.ActiveAmmoKit : null;

        public DefinitionLibrary Library => ActiveKit != null ? ActiveKit.Library : null;

        public void Equip(LoadoutSelection selection, DefinitionLibrary library, ulong seed)
        {
            Loadout = new LoadoutKit(selection, library, null, new DeterministicRandom(seed ^ 0xA5A5UL));
            Loadout.Seal();
            if (Body != null)
            {
                Body.Armor = Loadout.Armor;
            }
            Random = new DeterministicRandom(seed);
            _fireMode = ActiveWeapon != null ? ActiveWeapon.DefaultFireMode : FireMode.Auto;
        }

        private void Update()
        {
            if (Loadout == null || Body == null)
            {
                return;
            }

            Now = Time.time;
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);

            UpdateSlotSwitch();
            UpdateAim(dt);
            UpdateReloadKey();

            Loadout.Tick(Now);
            _events.Clear();
            Loadout.DrainEvents(_events);
            foreach (AmmoEvent ammoEvent in _events)
            {
                Viewmodel?.OnAmmoEvent(ammoEvent);
                AmmoEventRaised?.Invoke(ammoEvent);
            }

            _recoil.Tick(dt, RecoilProfileFor());
            _spread.Tick(dt, SpreadProfileFor());
            _timeSinceShot += dt;
            _timeSinceSprint = Body.Sprinting ? 0f : _timeSinceSprint + dt;

            UpdateFiring();
            HudChanged?.Invoke(Loadout.BuildHudSnapshot(Now));
        }

        // ---------------------------------------------------------------- input

        private void UpdateSlotSwitch()
        {
            ActiveSlot want = Loadout.Active;
            if (KeyInput.Pressed(KeyCode.Alpha1))
            {
                want = ActiveSlot.Primary;
            }
            else if (KeyInput.Pressed(KeyCode.Alpha2))
            {
                want = ActiveSlot.Secondary;
            }
            else if (KeyInput.Pressed(KeyCode.Alpha3))
            {
                want = ActiveSlot.Grenade;
            }

            if (want != Loadout.Active)
            {
                Loadout.TrySwitchSlot(want, Now);
                _aimBlend = 0f;
            }
        }

        private void UpdateAim(float dt)
        {
            bool wantAim = KordonInput.AimHeld && Loadout.Active != ActiveSlot.Grenade;
            WeaponDefinition weapon = ActiveWeapon;
            float up = weapon != null ? weapon.AimDownSightSeconds : 0.25f;
            float down = weapon != null ? weapon.AimOutSeconds : 0.2f;
            _aimBlend = wantAim
                ? Mathf.MoveTowards(_aimBlend, 1f, dt / Mathf.Max(0.05f, up))
                : Mathf.MoveTowards(_aimBlend, 0f, dt / Mathf.Max(0.05f, down));
            if (Rig != null)
            {
                Rig.IsAiming = IsAiming;
                Rig.AimFov = FovForSight(Loadout.Selection.PrimarySight);
            }
        }

        public static float FovForSight(SightKind sight)
        {
            switch (sight)
            {
                case SightKind.RedDot: return 62f;
                case SightKind.Holographic: return 60f;
                case SightKind.Scope1p5x: return 42f;
                case SightKind.Scope4x: return 16f;
                case SightKind.Scope8x: return 8f;
                default: return 58f;
            }
        }

        private void UpdateReloadKey()
        {
            AmmoKit kit = ActiveKit;
            if (kit == null)
            {
                return;
            }

            if (KordonInput.Pressed(InputAction.Reload))
            {
                kit.PressReloadKey(Now);
            }

            if (KordonInput.Released(InputAction.Reload))
            {
                kit.ReleaseReloadKey(Now);
            }
        }

        // ---------------------------------------------------------------- fire

        private void UpdateFiring()
        {
            AmmoKit kit = ActiveKit;
            if (kit == null)
            {
                return;
            }

            bool trigger = _fireMode == FireMode.Auto ? KordonInput.FireHeld : KordonInput.FirePressed;
            if (!trigger)
            {
                return;
            }

            WeaponDefinition weapon = ActiveWeapon;
            if (weapon == null)
            {
                return;
            }

            if (_timeSinceSprint < weapon.SprintToFireSeconds)
            {
                ShotDenied?.Invoke(FireDenial.Sprinting);
                return;
            }

            if (_timeSinceShot < weapon.SecondsPerShot * 0.98f)
            {
                return;
            }

            FireAttempt attempt = kit.TryFire(Now, Body.Sprinting);
            if (!attempt.Fired)
            {
                ShotDenied?.Invoke(attempt.Denial);
                return;
            }

            _timeSinceShot = 0f;
            ResolveShot(weapon);
        }

        private void ResolveShot(WeaponDefinition weapon)
        {
            ShotContext context = BuildContext();
            RecoilProfile recoilProfile = RecoilProfileFor();
            RecoilShot shot = _recoil.Fire(recoilProfile, context, Random, Now);
            _spread.AddBloom(SpreadProfileFor());

            Rig?.AddRecoil(shot.PitchDeg, shot.YawDeg);
            Viewmodel?.Kick(shot.VisualKick);

            float cone = _spread.ComputeCone(SpreadProfileFor(), context);
            KordonVec3 forwardCore = MapSpace.ToCore(Rig != null ? Rig.transform.forward : transform.forward);
            KordonVec3 direction = SpreadModel.SampleDirection(forwardCore, cone, Random);
            Vector3 unityDirection = MapSpace.ToUnity(direction).normalized;
            Vector3 origin = Rig != null ? Rig.transform.position : transform.position;

            ShotFired?.Invoke(new ShotInfo
            {
                Origin = origin,
                Direction = unityDirection,
                WeaponId = weapon.Id,
                BulletId = weapon.DefaultBulletId,
                ShotIndex = shot.ShotIndex,
            });

            if (!ServerAuthoritativeHits)
            {
                ResolveHitscan(weapon, origin, unityDirection);
            }
        }

        private void ResolveHitscan(WeaponDefinition weapon, Vector3 origin, Vector3 direction)
        {
            RaycastHit hit;
            float range = weapon.MaxEffectiveRangeM * 6f;
            if (!Physics.Raycast(origin, direction, out hit, range))
            {
                return;
            }

            PlayerController target = hit.collider.GetComponentInParent<PlayerController>();
            if (target == null || target == Body || target.IsDead)
            {
                return;
            }

            MagazineDefinition magazine = ChamberedMagazine();
            BulletDefinition bullet = magazine != null
                ? Library.ResolveBullet(weapon, magazine)
                : Library.GetBullet(weapon.DefaultBulletId);
            DamageRequest request = DamageRequest.Direct(
                bullet, ZoneFromHit(target, hit.point), target.Armor,
                (hit.point - origin).magnitude, Random);
            DamageResult result = new DamageModel().Resolve(request);
            if (result.Accepted)
            {
                target.ApplyHit(result);
                PlayerHit?.Invoke(target, result);
            }
        }

        private MagazineDefinition ChamberedMagazine()
        {
            AmmoKit kit = ActiveKit;
            if (kit == null || kit.Chambered == null)
            {
                return null;
            }

            return Library.GetMagazine(kit.Chambered.DefinitionId);
        }

        public static HitZone ZoneFromHit(PlayerController target, Vector3 unityPoint)
        {
            float height = unityPoint.y - target.transform.position.y;
            float eye = target.EyeHeight();
            if (height >= eye - 0.12f)
            {
                return HitZone.Head;
            }

            if (height >= eye * 0.52f)
            {
                return HitZone.Thorax;
            }

            if (height >= eye * 0.32f)
            {
                return HitZone.Abdomen;
            }

            return unityPoint.x >= target.transform.position.x ? HitZone.RightLeg : HitZone.LeftLeg;
        }

        // ------------------------------------------------------------- profiles

        public ShotContext BuildContext() => new ShotContext
        {
            Stance = Body.Stance,
            IsAiming = IsAiming,
            IsGrounded = Body.Grounded,
            IsLeaning = Mathf.Abs(Body.Lean) > 0.25f,
            Speed = Body.Speed,
            Stamina01 = Body.Stamina.Normalized,
            Suppression01 = Body.Vitals.Suppression.Value,
            AimStability = Mathf.Clamp01(1f - Body.WeaponControlPenalty - _handlingPenalty),
            TimeSinceLanding = 1f,
        };

        private RecoilProfile RecoilProfileFor()
        {
            WeaponDefinition weapon = ActiveWeapon;
            return weapon != null && weapon.Recoil != null ? weapon.Recoil : new RecoilProfile();
        }

        private SpreadProfile SpreadProfileFor()
        {
            WeaponDefinition weapon = ActiveWeapon;
            return weapon != null && weapon.Spread != null ? weapon.Spread : new SpreadProfile();
        }
    }

    /// <summary>
    /// Unity's Input is only touched by KordonInput; this tiny helper exists so the
    /// weapon layer can read digit keys without repeating the alias dance.
    /// </summary>
    internal static class KeyInput
    {
        public static bool Pressed(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
    }
}
