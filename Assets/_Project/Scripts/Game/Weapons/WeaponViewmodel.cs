using Kordon.Core.Ammo;
using Kordon.Core.Movement;
using Kordon.Game.Camera;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>Handling state of the first person weapon, driven by ammo events.</summary>
    public enum HandlingState
    {
        Ready,
        Sprinting,
        Aiming,
        ReloadMagOut,
        ReloadMagIn,
        Charging,
        Inspecting,
        Fumble,
        Empty,
    }

    /// <summary>
    /// First person weapon presentation.  The pose graph is procedural on purpose:
    /// every transition length comes from the weapon definition (reload tactical
    /// vs dry, inspect, charge), every state change is announced by a real ammo
    /// event from the Core AmmoKit, and the magazine prop is hidden and shown at
    /// the exact MagazineOut / MagazineIn beats, so what you see is what the
    /// magazine system did - no desynced canned clips, no random misfire theatre.
    /// Weapon meshes attach under <see cref="WeaponRoot"/> in the art pass.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponViewmodel : MonoBehaviour
    {
        public PlayerController Body;
        public BodycamRig Rig;

        /// <summary>Child of the bodycam; weapon art parents under it.</summary>
        public Transform WeaponRoot;

        /// <summary>Detachable magazine prop, toggled by handling beats.</summary>
        public Transform MagazineProp;

        /// <summary>Bolt/charging handle prop, cycled on BoltRelease.</summary>
        public Transform BoltProp;

        public HandlingState State { get; private set; } = HandlingState.Ready;

        private Vector3 _position;
        private Vector3 _positionTarget;
        private Quaternion _rotation = Quaternion.identity;
        private Quaternion _rotationTarget = Quaternion.identity;
        private float _kick;
        private float _stateTime;
        private float _bobPhase;
        private Vector3 _boltLocal;
        private Vector3 _magLocal;

        private static readonly Vector3 PoseReady = new Vector3(0.16f, -0.19f, 0.46f);
        private static readonly Vector3 PoseAim = new Vector3(0f, -0.135f, 0.38f);
        private static readonly Vector3 PoseSprint = new Vector3(0.11f, -0.26f, 0.36f);
        private static readonly Vector3 PoseMagOut = new Vector3(0.13f, -0.24f, 0.40f);
        private static readonly Vector3 PoseInspect = new Vector3(0.10f, -0.22f, 0.42f);

        private void Awake()
        {
            if (WeaponRoot == null)
            {
                GameObject root = new GameObject("WeaponRoot");
                root.transform.SetParent(transform, false);
                WeaponRoot = root.transform;
            }

            _position = PoseReady;
            WeaponRoot.localPosition = _position;
            if (MagazineProp != null)
            {
                _magLocal = MagazineProp.localPosition;
            }

            if (BoltProp != null)
            {
                _boltLocal = BoltProp.localPosition;
            }
        }

        public void OnAmmoEvent(AmmoEvent ammoEvent)
        {
            switch (ammoEvent.Kind)
            {
                case AmmoEventKind.MagazineOut:
                    State = HandlingState.ReloadMagOut;
                    if (MagazineProp != null)
                    {
                        MagazineProp.gameObject.SetActive(false);
                    }

                    break;
                case AmmoEventKind.MagazineIn:
                    State = HandlingState.ReloadMagIn;
                    if (MagazineProp != null)
                    {
                        MagazineProp.gameObject.SetActive(true);
                        MagazineProp.localPosition = _magLocal;
                    }

                    break;
                case AmmoEventKind.BoltRelease:
                case AmmoEventKind.ChamberRound:
                    State = HandlingState.Charging;
                    break;
                case AmmoEventKind.InspectStart:
                    State = HandlingState.Inspecting;
                    if (MagazineProp != null)
                    {
                        MagazineProp.gameObject.SetActive(false);
                    }

                    break;
                case AmmoEventKind.InspectEnd:
                    if (MagazineProp != null && State == HandlingState.Inspecting)
                    {
                        MagazineProp.gameObject.SetActive(true);
                    }

                    State = HandlingState.Ready;
                    break;
                case AmmoEventKind.FumbleStart:
                    State = HandlingState.Fumble;
                    break;
                case AmmoEventKind.FumbleRecovered:
                case AmmoEventKind.ReloadComplete:
                    State = HandlingState.Ready;
                    break;
                case AmmoEventKind.OutOfAmmunition:
                    State = HandlingState.Empty;
                    break;
            }

            _stateTime = 0f;
        }

        public void Kick(float visualKick) => _kick = Mathf.Clamp01(_kick + visualKick);

        private void LateUpdate()
        {
            if (Body == null)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            _stateTime += dt;

            UpdatePose(dt);
            UpdateProps();
        }

        private void UpdatePose(float dt)
        {
            bool aiming = Rig != null && Rig.IsAiming;
            Vector3 pose;
            Quaternion tilt = Quaternion.identity;

            if (Body.Sprinting && !aiming)
            {
                State = State == HandlingState.Ready ? HandlingState.Sprinting : State;
                pose = PoseSprint;
                tilt = Quaternion.Euler(12f, -14f, 22f);
            }
            else
            {
                if (State == HandlingState.Sprinting)
                {
                    State = HandlingState.Ready;
                }

                switch (State)
                {
                    case HandlingState.Aiming:
                    default:
                        pose = aiming ? PoseAim : PoseReady;
                        break;
                    case HandlingState.ReloadMagOut:
                    case HandlingState.ReloadMagIn:
                        pose = PoseMagOut;
                        tilt = Quaternion.Euler(28f, 0f, -34f);
                        break;
                    case HandlingState.Inspecting:
                        pose = PoseInspect;
                        tilt = Quaternion.Euler(46f, 8f, -52f);
                        break;
                    case HandlingState.Charging:
                        pose = PoseReady;
                        tilt = Quaternion.Euler(6f, 0f, -8f);
                        break;
                    case HandlingState.Fumble:
                        pose = PoseReady + new Vector3(0f, -0.03f, -0.02f);
                        tilt = Quaternion.Euler(-6f, 10f, 8f);
                        break;
                    case HandlingState.Empty:
                        pose = PoseReady;
                        tilt = Quaternion.Euler(3f, 0f, -4f);
                        break;
                }
            }

            // Gait bob and breath sway ride on top of the handling pose.
            _bobPhase += dt * (3f + Body.Speed * 1.6f);
            float bobAmount = Body.Gait == Gait.Idle ? 0f : Mathf.Clamp01(Body.Speed / 5f);
            pose += new Vector3(
                Mathf.Sin(_bobPhase) * 0.012f * bobAmount,
                Mathf.Abs(Mathf.Sin(_bobPhase * 2f)) * 0.009f * bobAmount,
                Mathf.Cos(_bobPhase * 0.5f) * 0.006f * bobAmount);

            float breath = Body.Stamina.BreathIntensity;
            pose += new Vector3(Mathf.Sin(Time.time * 1.9f) * 0.0022f * breath,
                                Mathf.Sin(Time.time * 2.6f) * 0.0026f * breath, 0f);

            // Recoil kick: shove back and up, spring home.
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 7f);
            pose += new Vector3(0f, _kick * 0.015f, _kick * 0.06f);
            tilt = Quaternion.Euler(tilt.eulerAngles.x - _kick * 5f, tilt.eulerAngles.y,
                                    tilt.eulerAngles.z);

            _positionTarget = pose;
            _rotationTarget = tilt;
            _position = Vector3.Lerp(_position, _positionTarget, Mathf.Clamp01(dt * 18f));
            _rotation = Quaternion.Slerp(_rotation, _rotationTarget, Mathf.Clamp01(dt * 16f));

            WeaponRoot.localPosition = _position;
            WeaponRoot.localRotation = _rotation;
        }

        private void UpdateProps()
        {
            if (BoltProp == null)
            {
                return;
            }

            Vector3 target = _boltLocal;
            if (State == HandlingState.Charging && _stateTime < 0.22f)
            {
                target = _boltLocal + new Vector3(0f, 0f, -0.05f);
            }

            BoltProp.localPosition = Vector3.Lerp(BoltProp.localPosition, target,
                                                  Mathf.Clamp01(Time.deltaTime * 22f));
        }
    }
}
