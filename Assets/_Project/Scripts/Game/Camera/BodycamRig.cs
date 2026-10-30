using Kordon.Core.Movement;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using Kordon.Game.Rendering;
using UnityEngine;

namespace Kordon.Game.Camera
{
    /// <summary>
    /// The bodycam. Permanently on: inertial position lag, procedural sway from the
    /// breath model, recoil spring, landing dip, suppression shake and the post
    /// process pass (barrel distortion, chromatic aberration, vignette, sensor
    /// grain, exposure flutter).  There is deliberately no toggle anywhere - the
    /// design rule is that the cam feel is part of the game, not an option.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodycamRig : MonoBehaviour
    {
        public PlayerController Body;

        [Header("Look")]
        public float Sensitivity = 2.4f;
        public float PitchLimitDegrees = 85f;

        [Header("Inertia")]
        public float PositionLag = 9f;
        public float RotationLag = 22f;
        public float SwayAmplitude = 1f;

        public bool IsAiming { get; set; }
        public float AimFov = 60f;
        public float HipFov = 68f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }

        private UnityEngine.Camera _camera;
        private Material _postMaterial;
        private Vector3 _positionVelocity;
        private Vector3 _recoilAngles;
        private Vector3 _recoilVelocity;
        private float _roll;
        private float _dip;
        private float _dipVelocity;
        private float _suppression;
        private float _grainTime;
        private float _fovCurrent;

        private void Awake()
        {
            _camera = GetComponent<UnityEngine.Camera>();
            if (_camera == null)
            {
                _camera = gameObject.AddComponent<UnityEngine.Camera>();
            }

            _fovCurrent = HipFov;
            Shader shader = ShaderLibrary.Bodycam();
            if (shader != null)
            {
                _postMaterial = new Material(shader);
            }
        }

        private void Start()
        {
            if (Body != null)
            {
                transform.position = Body.CameraAnchor.position;
                Yaw = MapSpace.YawDegrees(Body.transform.forward);
                _roll = 0f;
            }
        }

        /// <summary>Weapon recoil feeds angular impulse into the cam spring.</summary>
        public void AddRecoil(float pitchDegrees, float yawDegrees)
        {
            _recoilAngles += new Vector3(-pitchDegrees, yawDegrees, 0f);
        }

        public void AddLandingDip(float fallHeight) =>
            _dipVelocity -= Mathf.Clamp(fallHeight, 0f, 6f) * 0.35f;

        public void SetSuppression(float suppression01) => _suppression = Mathf.Clamp01(suppression01);

        private void LateUpdate()
        {
            if (Body == null)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);

            // An unlocked cursor belongs to a menu or the shop, so the head must hold still
            // instead of swinging with the pointer.
            Vector2 look = Cursor.lockState == CursorLockMode.Locked
                ? Kordon.Game.Input.KordonInput.LookDelta() * Sensitivity
                : Vector2.zero;
            Yaw += look.x * 0.1f;
            Pitch = Mathf.Clamp(Pitch - look.y * 0.1f, -PitchLimitDegrees, PitchLimitDegrees);

            // Recoil spring: kicks up, recovers back to zero (the recovery is the
            // visible "control" the recoil model scores).
            _recoilAngles = SpringBack(_recoilAngles, ref _recoilVelocity, dt, 14f, 0.09f);

            float targetRoll = -Body.Lean * 7f;
            _roll = Mathf.MoveTowards(_roll, targetRoll, dt * 90f);

            _dip = SpringScalar(_dip, ref _dipVelocity, dt, 90f, 12f);

            Quaternion targetRotation = Quaternion.Euler(
                Pitch + _recoilAngles.x + ShakePitch(),
                Yaw + _recoilAngles.y + ShakeYaw(),
                _roll + _recoilAngles.z);

            transform.rotation = SlerpTowards(transform.rotation, targetRotation, RotationLag, dt);

            Vector3 eyeTarget = Body.CameraAnchor.position + SwayOffset();
            transform.position = CriticalDamp(transform.position, eyeTarget, ref _positionVelocity,
                                              PositionLag, dt);

            float targetFov = IsAiming ? AimFov : HipFov;
            targetFov += Body.Sprinting ? 5f : 0f;
            _fovCurrent = Mathf.MoveTowards(_fovCurrent, targetFov, dt * 140f);
            _camera.fieldOfView = _fovCurrent + _dip * 1.5f;
        }

        // ------------------------------------------------------------- procedural

        private Vector3 SwayOffset()
        {
            float intensity = Body.Stamina.BreathIntensity + _suppression * 0.35f;
            float degrees = Body.Breath.SwayDegrees(Body.Stance, intensity, IsAiming) * SwayAmplitude;
            Kordon.Core.Utility.KordonVec3 sway =
                Body.Breath.SampleSway(Time.time, intensity, degrees);
            Vector3 offset = MapSpace.ToUnity(sway) * 0.02f;
            offset.y += _dip * 0.05f;
            return offset;
        }

        private float ShakePitch() =>
            _suppression > 0.35f ? Mathf.Sin(Time.time * 31f) * 0.12f * _suppression : 0f;

        private float ShakeYaw() =>
            _suppression > 0.35f ? Mathf.Cos(Time.time * 27f) * 0.12f * _suppression : 0f;

        private static Vector3 SpringBack(Vector3 value, ref Vector3 velocity, float dt,
                                            float stiffness, float damping)
        {
            Vector3 acceleration = value * -stiffness - velocity * damping;
            velocity += acceleration * dt;
            return value + velocity * dt;
        }

        private static float SpringScalar(float value, ref float velocity, float dt,
                                          float stiffness, float damping)
        {
            float acceleration = value * -stiffness - velocity * damping;
            velocity += acceleration * dt;
            return value + velocity * dt;
        }

        private static Vector3 CriticalDamp(Vector3 current, Vector3 target, ref Vector3 velocity,
                                            float omega, float dt)
        {
            Vector3 delta = current - target;
            Vector3 exponential = new Vector3(
                Mathf.Exp(-omega * dt), Mathf.Exp(-omega * dt), Mathf.Exp(-omega * dt));
            Vector3 next = target + (delta + (velocity + delta * omega) * dt) * exponential.x;
            velocity = (next - current) / dt;
            return next;
        }

        private static Quaternion SlerpTowards(Quaternion current, Quaternion target,
                                                float rate, float dt) =>
            Quaternion.Slerp(current, target, Mathf.Clamp01(rate * dt));

        // ------------------------------------------------------------- post pass

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_postMaterial == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            _grainTime += Time.deltaTime;
            _postMaterial.SetFloat("_Time2", _grainTime);
            _postMaterial.SetFloat("_Suppression", _suppression);
            _postMaterial.SetFloat("_Breath", Body != null ? Body.Stamina.BreathIntensity : 0f);
            _postMaterial.SetFloat("_Sprint", Body != null && Body.Sprinting ? 1f : 0f);
            Graphics.Blit(source, destination, _postMaterial);
        }
    }
}
