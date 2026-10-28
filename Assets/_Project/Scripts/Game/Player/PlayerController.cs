using System;
using Kordon.Core.Combat;
using Kordon.Core.Movement;
using Kordon.Core.Utility;
using Kordon.Game.Input;
using Kordon.Game.Maps;
using UnityEngine;

namespace Kordon.Game.Player
{
    /// <summary>
    /// The local body: stance, lean, sprint and stamina, inertial movement from the
    /// deterministic MovementModel, footstep and landing noise on the authored
    /// surface contract, eye height and vitals.  Nothing here decides rules - every
    /// number comes from Kordon.Core so server and client simulate identically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerController : MonoBehaviour
    {
        /// <summary>Child transform the bodycam rig hangs from (eye position).</summary>
        public Transform CameraAnchor;

        /// <summary>Set by the match bootstrap; may be null on the practice range.</summary>
        public Kordon.Game.Weapons.WeaponController Weapons;

        public MovementModel Movement { get; private set; }
        public StaminaModel Stamina { get; } = new StaminaModel();
        public BreathModel Breath { get; } = new BreathModel();
        public PlayerVitals Vitals { get; } = new PlayerVitals();

        /// <summary>Armor state of the sealed loadout; server assigns on spawn.</summary>
        public ArmorState Armor { get; set; } = new ArmorState(null);

        public Stance Stance { get; private set; } = Stance.Stand;
        public float StanceBlend { get; private set; } = 1f;
        public float Lean { get; private set; }
        public Gait Gait { get; private set; } = Gait.Idle;
        public bool Sprinting { get; private set; }
        public bool Grounded { get; private set; } = true;
        public KordonVec3 Velocity { get; private set; }
        public float Speed => Velocity.Magnitude;
        public float WeaponControlPenalty { get; private set; }
        public string GroundSurface { get; private set; } = "concrete_raw";
        public float GearMassKg => Weapons != null ? Weapons.GearMassKg : 9f;

        /// <summary>Raised for the audio director and, on servers, for listeners/AI.</summary>
        public event Action<NoiseEvent> NoiseEmitted;

        /// <summary>Raised when the body touches down after a fall.</summary>
        public event Action<float> Landed;

        private CharacterController _controller;
        private Stance _pendingStance = Stance.Stand;
        private float _stanceTimer;
        private float _stanceDuration = 0.28f;
        private float _footstepTimer;
        private float _bobPhase;
        private float _fallStart;
        private float _eyeFrom = MovementProfile.StandEyeHeight;
        private bool _wasGrounded = true;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_controller == null)
            {
                _controller = gameObject.AddComponent<CharacterController>();
                _controller.height = 1.82f;
                _controller.radius = 0.34f;
                _controller.center = new Vector3(0f, 0.91f, 0f);
            }

            Movement = new MovementModel();
            if (CameraAnchor == null)
            {
                GameObject anchor = new GameObject("CameraAnchor");
                anchor.transform.SetParent(transform, false);
                CameraAnchor = anchor.transform;
            }
        }

        private void Update()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            if (dt <= 0f)
            {
                return;
            }

            UpdateStance();
            UpdateLean(dt);

            bool aiming = Weapons != null && Weapons.IsAiming;
            MovementIntent intent = new MovementIntent
            {
                Forward = KordonInput.MoveAxes().y,
                Right = KordonInput.MoveAxes().x,
                WantsSprint = KordonInput.Held(InputAction.Sprint),
                WantsJump = KordonInput.Pressed(InputAction.Jump),
                IsAiming = aiming,
                Stance = Stance,
            };

            KordonVec3 wish = WishDirection(intent);
            bool canSprint = Stamina.CanSprint && !aiming && Stance == Stance.Stand;

            MovementStep step = Movement.Integrate(
                Velocity, wish, intent, Grounded, dt,
                mobilityMultiplier: Weapons != null ? Weapons.MobilityMultiplier : 1f,
                gearMassKg: GearMassKg,
                armorMobility: 1f,
                canSprint: canSprint,
                stanceTransition01: StanceBlend);

            Stamina.Tick(dt, step.Sprinting, aiming, GearMassKg);
            Velocity = step.Velocity;
            Gait = step.Gait;
            Sprinting = step.Sprinting;
            WeaponControlPenalty = step.WeaponControlPenalty;

            _controller.Move(MapSpace.ToUnity(Velocity) * dt);

            UpdateGroundContact(dt);
            UpdateFootsteps(dt);
            UpdateAnchor(dt);
            Vitals.Tick(dt);
        }

        // ---------------------------------------------------------------- stance

        private void UpdateStance()
        {
            if (KordonInput.Pressed(InputAction.Prone))
            {
                _pendingStance = _pendingStance == Stance.Prone ? Stance.Stand : Stance.Prone;
            }
            else if (KordonInput.Held(InputAction.Crouch))
            {
                _pendingStance = _pendingStance == Stance.Prone ? Stance.Prone : Stance.Crouch;
            }
            else if (_pendingStance == Stance.Crouch)
            {
                _pendingStance = Stance.Stand;
            }

            if (_pendingStance != Stance)
            {
                _stanceTimer = 0f;
                _stanceDuration = _pendingStance == Stance.Prone || Stance == Stance.Prone
                    ? MovementProfile.ProneTransitionSeconds
                    : MovementProfile.StanceTransitionSeconds;
                _eyeFrom = EyeHeight();
                Stance = _pendingStance;
            }

            _stanceTimer = Mathf.Min(_stanceTimer + Time.deltaTime, _stanceDuration);
            StanceBlend = _stanceDuration <= 0f ? 1f : _stanceTimer / _stanceDuration;
        }

        private void UpdateLean(float dt)
        {
            float target = (KordonInput.Held(InputAction.LeanRight) ? 1f : 0f) -
                           (KordonInput.Held(InputAction.LeanLeft) ? 1f : 0f);
            Lean = Mathf.MoveTowards(Lean, target, dt / Mathf.Max(0.05f, MovementProfile.LeanTransitionSeconds));
        }

        // ---------------------------------------------------------------- world

        private KordonVec3 WishDirection(MovementIntent intent)
        {
            if (intent.InputMagnitude <= 0.001f)
            {
                return KordonVec3.Zero;
            }

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward = forward.normalized;
            right = right.normalized;

            KordonVec3 coreForward = MapSpace.ToCore(forward);
            KordonVec3 coreRight = MapSpace.ToCore(right);
            KordonVec3 wish = new KordonVec3(
                coreForward.X * intent.Forward + coreRight.X * intent.Right,
                coreForward.Y * intent.Forward + coreRight.Y * intent.Right,
                0f);
            float length = wish.Magnitude;
            return length > 0.0001f
                ? new KordonVec3(wish.X / length * intent.InputMagnitude,
                                 wish.Y / length * intent.InputMagnitude, 0f)
                : KordonVec3.Zero;
        }

        private void UpdateGroundContact(float dt)
        {
            bool grounded = _controller.isGrounded;
            if (grounded && !_wasGrounded)
            {
                float fallHeight = _fallStart - transform.position.y;
                if (fallHeight > 0.5f)
                {
                    float damage = MovementModel.FallDamage(fallHeight);
                    if (damage > 0f)
                    {
                        Vitals.ApplyDamage(new DamageResult
                        {
                            Accepted = true,
                            Damage = damage,
                            Zone = HitZone.LeftLeg,
                        }, DeathCause.Fall);
                    }

                    NoiseEmitted?.Invoke(new NoiseEvent
                    {
                        Kind = NoiseKind.Landing,
                        Position = MapSpace.ToCore(transform.position),
                        RadiusM = NoiseModel.LandingRadius(fallHeight),
                        SurfaceId = GroundSurface,
                    });
                }

                Landed?.Invoke(fallHeight);
            }

            if (!grounded && _wasGrounded)
            {
                _fallStart = transform.position.y;
            }

            _wasGrounded = grounded;
            Grounded = grounded;

            if (grounded)
            {
                GroundSurface = SurfaceProbe.Below(_controller);
            }
        }

        private void UpdateFootsteps(float dt)
        {
            if (!Grounded || Gait == Gait.Idle)
            {
                _footstepTimer = 0f;
                return;
            }

            float interval = NoiseModel.FootstepInterval(Gait, Speed);
            _footstepTimer += dt;
            if (_footstepTimer < interval)
            {
                return;
            }

            _footstepTimer = 0f;
            NoiseEmitted?.Invoke(NoiseModel.MakeFootstep(
                Gait, GroundSurface, MapSpace.ToCore(transform.position), GearMassKg, 1f));
        }

        private void UpdateAnchor(float dt)
        {
            float eye = EyeHeight();
            _bobPhase += dt * (2f + Speed * 1.35f);
            float bob = Gait == Gait.Idle ? 0f : Mathf.Sin(_bobPhase * 2f) * 0.018f * Mathf.Clamp01(Speed / 4f);
            CameraAnchor.localPosition = new Vector3(Lean * 0.16f, eye + bob, Lean * 0.04f);
        }

        public float EyeHeight() => Mathf.Lerp(_eyeFrom, EyeHeightFor(Stance), StanceBlend);

        public static float EyeHeightFor(Stance stance) =>
            stance == Stance.Stand ? MovementProfile.StandEyeHeight :
            stance == Stance.Crouch ? MovementProfile.CrouchEyeHeight :
            MovementProfile.ProneEyeHeight;

        // ----------------------------------------------------------------- hits

        /// <summary>Server (or local practice) applies an authoritative damage result.</summary>
        public bool ApplyHit(DamageResult result, DeathCause cause = DeathCause.Gunfire) =>
            Vitals.ApplyDamage(result, cause);

        public bool IsDead => Vitals.IsDead;

        public void Respawn(Vector3 unityPosition, float yawDegrees)
        {
            transform.position = unityPosition;
            transform.rotation = MapSpace.ToUnityYaw(yawDegrees);
            Velocity = KordonVec3.Zero;
            Vitals.Reset();
            Stamina.Reset();
            Stance = Stance.Stand;
            _pendingStance = Stance.Stand;
            Lean = 0f;
        }
    }

    /// <summary>
    /// Reads the authored surface id of whatever the player stands on, using the
    /// same material-name contract MapBuilder installs (M_&lt;surface&gt;), so
    /// footsteps and impacts always match the visible ground.
    /// </summary>
    public static class SurfaceProbe
    {
        public static string Below(CharacterController controller)
        {
            Vector3 origin = controller.transform.position + new Vector3(0f, 0.35f, 0f);
            RaycastHit hit;
            if (!Physics.Raycast(origin, Vector3.down, out hit, 2.6f))
            {
                return "concrete_raw";
            }

            return FromCollider(hit.collider);
        }

        public static string FromCollider(Collider collider)
        {
            if (collider == null)
            {
                return "concrete_raw";
            }

            Renderer renderer = collider.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null)
            {
                return "concrete_raw";
            }

            string surface = renderer.sharedMaterial.name;
            if (surface.StartsWith("M_", StringComparison.Ordinal))
            {
                surface = surface.Substring(2);
            }

            int space = surface.IndexOf(' ');
            if (space > 0)
            {
                surface = surface.Substring(0, space);
            }

            return surface;
        }
    }
}
