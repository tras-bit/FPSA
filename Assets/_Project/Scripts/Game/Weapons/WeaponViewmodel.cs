using Kordon.Core.Ammo;
using Kordon.Core.Weapons;
using Kordon.Game.Camera;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Weapons
{
    /// <summary>
    /// First person weapon presentation.
    ///
    /// All of the motion lives in <see cref="ViewmodelAnimator"/> - the engine free
    /// core class that is unit tested against fixed inputs, because a spring that
    /// diverges or a magazine that never seats would be visible in every frame and
    /// cannot be seen in this build environment.  This component is the adapter:
    ///
    /// * it reads the player (aim state, sprint, speed, breath, look rate, landing)
    ///   and feeds it to the animator;
    /// * it forwards the ammo system's beats (<see cref="OnAmmoEvent"/>) so the pose
    ///   and the magazine follow what the magazine system actually did;
    /// * it loads the authored FBX per weapon and measures it, so every weapon aims
    ///   down its own sights and is held at its own distance;
    /// * it writes the resulting pose onto the transforms, with the magazine and the
    ///   charging handle as separate props.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponViewmodel : MonoBehaviour
    {
        public PlayerController Body;
        public BodycamRig Rig;

        /// <summary>Child of the bodycam; weapon art parents under it.</summary>
        public Transform WeaponRoot;

        /// <summary>Detachable magazine prop, animated by the handling timeline.</summary>
        public Transform MagazineProp;

        /// <summary>Bolt/charging handle prop, cycled on BoltRelease.</summary>
        public Transform BoltProp;

        /// <summary>
        /// Support hand node of the authored model.  It rides on the fore-end until
        /// a reload starts and then follows the magazine: a reload where the hand
        /// never leaves the handguard is the biggest tell that a game has no first
        /// person hands, so the hand is animated with the same magazine timeline.
        /// </summary>
        public Transform SupportHandProp;

        /// <summary>Shooting hand node, kept for anything that wants to react to recoil.</summary>
        public Transform ShootingHandProp;

        /// <summary>The authored FBX currently in the hands (null until the art is loaded).</summary>
        public GameObject ModelInstance => _modelInstance;

        /// <summary>The handling state the animator is in, for anything that wants to react.</summary>
        public ViewmodelState State => _animator != null ? _animator.State : ViewmodelState.Ready;

        /// <summary>0 at the hip, 1 with the sights up; the camera can use this for FOV.</summary>
        public float AimBlend => _animator != null ? _animator.Pose.AimBlend : 0f;

        private ViewmodelAnimator _animator;
        private ViewmodelConfig _config = ViewmodelConfig.Default;

        private Vector3 _magSeat;
        private Vector3 _boltSeat;
        private Vector3 _handSeat;
        private Transform _triggerProp;
        private Quaternion _triggerSeat = Quaternion.identity;

        private GameObject _modelInstance;
        private string _modelWeaponId;
        private float _sightHeight = 0.068f;
        private float _modelLength = 0.9f;

        // look rate, derived from the camera yaw/pitch between frames
        private float _lastYaw;
        private float _lastPitch;
        private bool _hasLookSample;
        private float _yawRate;
        private float _pitchRate;

        // landing impulse, taken from the player's touchdown event
        private float _pendingLanding;
        private bool _landedSubscribed;

        private void Awake()
        {
            if (WeaponRoot == null)
            {
                GameObject root = new GameObject("WeaponRoot");
                root.transform.SetParent(transform, false);
                WeaponRoot = root.transform;
            }

            _animator = new ViewmodelAnimator(_config);
            if (MagazineProp != null)
            {
                _magSeat = MagazineProp.localPosition;
            }

            if (BoltProp != null)
            {
                _boltSeat = BoltProp.localPosition;
            }
        }

        // Awake runs before the operator has assigned Body (it is set right after
        // AddComponent), so the player events are bound in Start.
        private void Start()
        {
            SubscribeToBody();
        }

        private void SubscribeToBody()
        {
            if (_landedSubscribed || Body == null)
            {
                return;
            }

            Body.Landed += OnLanded;
            _landedSubscribed = true;
        }

        // ------------------------------------------------------------- events

        /// <summary>Ammo system beat.  The pose and the magazine follow it exactly.</summary>
        public void OnAmmoEvent(AmmoEvent ammoEvent)
        {
            if (_animator == null)
            {
                return;
            }

            switch (ammoEvent.Kind)
            {
                case AmmoEventKind.MagazineOut:
                    _animator.Notify(ViewmodelEvent.MagazineOut);
                    break;
                case AmmoEventKind.MagazineIn:
                    _animator.Notify(ViewmodelEvent.MagazineIn);
                    break;
                case AmmoEventKind.BoltRelease:
                case AmmoEventKind.ChamberRound:
                    _animator.Notify(ViewmodelEvent.BoltRelease);
                    break;
                case AmmoEventKind.InspectStart:
                    _animator.Notify(ViewmodelEvent.InspectStart);
                    break;
                case AmmoEventKind.InspectEnd:
                    _animator.Notify(ViewmodelEvent.InspectEnd);
                    break;
                case AmmoEventKind.FumbleStart:
                    _animator.Notify(ViewmodelEvent.FumbleStart);
                    break;
                case AmmoEventKind.FumbleRecovered:
                    _animator.Notify(ViewmodelEvent.FumbleRecovered);
                    break;
                case AmmoEventKind.ReloadComplete:
                    _animator.Notify(ViewmodelEvent.ReloadComplete);
                    break;
                case AmmoEventKind.OutOfAmmunition:
                    _animator.Notify(ViewmodelEvent.OutOfAmmunition);
                    break;
            }
        }

        /// <summary>Recoil impulse from a shot, 0..1.</summary>
        public void Kick(float visualKick)
        {
            _animator?.AddRecoil(visualKick);
            _animator?.AddBoltCycle(0.35f + Mathf.Clamp01(visualKick) * 0.4f);
        }

        // --------------------------------------------------------------- tick

        private void LateUpdate()
        {
            if (Body == null || _animator == null)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            SubscribeToBody();
            EnsureModel();

            // Slot 3 puts the weapon away and the hand comes up; it comes back the
            // moment the grenade leaves the hand or the slot changes.
            bool grenadeOut = Body.Weapons != null && Body.Weapons.Loadout != null &&
                              Body.Weapons.Loadout.Active == ActiveSlot.Grenade;
            if (WeaponRoot != null && WeaponRoot.gameObject.activeSelf == grenadeOut)
            {
                WeaponRoot.gameObject.SetActive(!grenadeOut);
            }

            if (grenadeOut)
            {
                return;
            }

            SampleLook(dt);
            ViewmodelInput input = BuildInput();

            _animator.Tick(dt, input);
            Apply(_animator.Pose);
        }

        private void SampleLook(float dt)
        {
            if (Rig == null)
            {
                _yawRate = 0f;
                _pitchRate = 0f;
                return;
            }

            float yaw = Rig.Yaw;
            float pitch = Rig.Pitch;
            if (!_hasLookSample)
            {
                _lastYaw = yaw;
                _lastPitch = pitch;
                _hasLookSample = true;
                return;
            }

            if (dt > 0.0001f)
            {
                // shortest way round, so a wrap from 359 to 1 does not swing the weapon
                float deltaYaw = Mathf.DeltaAngle(_lastYaw, yaw);
                _yawRate = deltaYaw / dt;
                _pitchRate = (pitch - _lastPitch) / dt;
            }

            _lastYaw = yaw;
            _lastPitch = pitch;
        }

        /// <summary>Touchdown: the heavier the drop, the deeper the weapon dips.</summary>
        private void OnLanded(float fallHeight)
        {
            _pendingLanding = Mathf.Clamp01(fallHeight / 4f);
        }

        private ViewmodelInput BuildInput()
        {
            bool grounded = Body.Grounded;
            float landing = _pendingLanding;
            _pendingLanding = 0f;

            float speed01 = Mathf.Clamp01(Body.Speed / 5.2f);
            bool aiming = Rig != null && Rig.IsAiming;

            return new ViewmodelInput
            {
                Aiming = aiming,
                Sprinting = Body.Sprinting && !aiming,
                Grounded = grounded,
                Speed01 = speed01,
                Breath01 = Mathf.Clamp01(Body.Stamina.BreathIntensity),
                YawRate = _yawRate,
                PitchRate = _pitchRate,
                LandingImpulse = landing,
            };
        }

        private void Apply(ViewmodelPose pose)
        {
            if (WeaponRoot == null)
            {
                return;
            }

            // The charging handle is part of the joined weapon mesh, so the bolt beat
            // is read as the whole weapon being worked: pulled back and rolled a
            // little, which is what a rack actually looks like in the hands.
            float boltRoll = pose.BoltTravel * 4f;
            float boltPitch = pose.BoltTravel * -2f;

            WeaponRoot.localPosition = new Vector3(pose.X + pose.BoltTravel * 0.004f,
                                                   pose.Y - pose.BoltTravel * 0.002f,
                                                   pose.Z - pose.BoltTravel * 0.010f);
            WeaponRoot.localRotation = Quaternion.Euler(pose.Pitch + boltPitch,
                                                        pose.Yaw, pose.Roll + boltRoll);

            if (MagazineProp != null)
            {
                if (MagazineProp.gameObject.activeSelf != pose.MagazineVisible)
                {
                    MagazineProp.gameObject.SetActive(pose.MagazineVisible);
                }

                if (pose.MagazineVisible)
                {
                    // the drop is the animation's local Y, which maps onto the
                    // weapon's own down axis whatever pose the weapon is in
                    MagazineProp.localPosition = _magSeat +
                        new Vector3(pose.MagazineX, pose.MagazineY, pose.MagazineZ);
                    MagazineProp.localRotation = Quaternion.Euler(pose.MagazinePitch, 0f,
                        pose.MagazineRoll);
                }
            }

            if (BoltProp != null)
            {
                BoltProp.localPosition = _boltSeat + new Vector3(0f, 0f, -pose.BoltTravel * 0.05f);
            }

            if (_triggerProp != null)
            {
                // the blade pivots on its pin: it swings backwards into the guard, it
                // does not slide
                _triggerProp.localRotation = _triggerSeat *
                    Quaternion.Euler(-16f * Mathf.Clamp01(pose.TriggerTravel), 0f, 0f);
            }

            ApplySupportHand(pose);
        }

        /// <summary>
        /// Slide the support hand between the fore-end and the magazine well.
        ///
        /// The hand is authored on the weapon, so its seat is whatever the modeller
        /// placed it at.  During a magazine change it rides the same offset the
        /// magazine itself is given - down with the empty magazine, and back up with
        /// the fresh one - which is what makes the hand look like it is holding the
        /// magazine rather than floating near it.
        /// </summary>
        private void ApplySupportHand(ViewmodelPose pose)
        {
            if (SupportHandProp == null)
            {
                return;
            }

            float blend = Mathf.Clamp01(pose.SupportHandBlend);
            Vector3 travel = Vector3.zero;
            if (blend > 0.0001f)
            {
                // the hand travels a little less than the magazine: a hand holding a
                // magazine drops it half the way and rotates it, it does not follow
                // the magazine to the floor
                travel = new Vector3(pose.MagazineX * 0.85f,
                                     pose.MagazineY * 0.55f,
                                     pose.MagazineZ * 0.85f);
            }

            SupportHandProp.localPosition = _handSeat + travel * blend;
            SupportHandProp.localRotation = Quaternion.Euler(
                pose.MagazinePitch * 0.45f * blend, 0f, pose.MagazineRoll * 0.55f * blend);
        }

        // --------------------------------------------------------------- model

        /// <summary>Load the authored model for the active weapon, once per weapon change.</summary>
        private void EnsureModel()
        {
            string id = Body.Weapons != null && Body.Weapons.ActiveWeapon != null
                ? Body.Weapons.ActiveWeapon.Id
                : null;
            if (id == _modelWeaponId)
            {
                return;
            }

            _modelWeaponId = id;
            if (_modelInstance != null)
            {
                Destroy(_modelInstance);
                _modelInstance = null;
            }

            MagazineProp = null;
            _triggerProp = null;
            BoltProp = null;
            SupportHandProp = null;
            ShootingHandProp = null;
            _sightHeight = 0.068f;
            _modelLength = 0.9f;
            if (string.IsNullOrEmpty(id) || WeaponRoot == null)
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>("Kordon/Weapons/" + id);
            if (prefab == null)
            {
                Debug.LogWarning("[kordon] no viewmodel art for weapon '" + id + "'");
                return;
            }

            _modelInstance = Instantiate(prefab, WeaponRoot, false);
            _modelInstance.name = "Weapon_" + id;
            SurfaceMaterialLibrary.Apply(_modelInstance);

            Transform magazine = _modelInstance.transform.Find("Magazine");
            if (magazine != null)
            {
                MagazineProp = magazine;
                _magSeat = magazine.localPosition;
            }

            _triggerProp = _modelInstance.transform.Find("Trigger");
            if (_triggerProp != null)
            {
                _triggerSeat = _triggerProp.localRotation;
            }

            SupportHandProp = _modelInstance.transform.Find("HandLeft");
            if (SupportHandProp != null)
            {
                _handSeat = SupportHandProp.localPosition;
            }

            ShootingHandProp = _modelInstance.transform.Find("HandRight");

            MeasureModel(_modelInstance);

            // The animator needs the real numbers, or a pistol would be held at rifle
            // distance and the sights would aim above the camera axis.
            _config = new ViewmodelConfig
            {
                SightHeight = _sightHeight,
                Length = _modelLength,
                MagazineDrop = Mathf.Max(0.16f, _modelLength * 0.22f),
                MagazineDropSeconds = 0.70f,
                MagazineInsertSeconds = 0.30f,
                InspectHoldHeight = Mathf.Max(0.08f, _modelLength * 0.11f),
            };
            _animator = new ViewmodelAnimator(_config);
        }

        /// <summary>Read the sight height and the length straight from the mesh bounds.</summary>
        private void MeasureModel(GameObject model)
        {
            float top = 0f;
            float forward = 0f;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds bounds = filter.sharedMesh.bounds;
                // measure in the model's own space, so a mesh nested under an
                // imported armature root still reports the right height
                Vector3 centre = model.transform.InverseTransformPoint(
                    filter.transform.TransformPoint(bounds.center));
                top = Mathf.Max(top, centre.y + bounds.size.y * 0.5f);
                forward = Mathf.Max(forward, centre.z + bounds.size.z * 0.5f);
            }

            if (top > 0.01f)
            {
                _sightHeight = top;
            }

            if (forward > 0.05f)
            {
                _modelLength = forward;
            }
        }

        private void OnDestroy()
        {
            if (_landedSubscribed && Body != null)
            {
                Body.Landed -= OnLanded;
            }

            if (_modelInstance != null)
            {
                Destroy(_modelInstance);
            }
        }
    }
}
