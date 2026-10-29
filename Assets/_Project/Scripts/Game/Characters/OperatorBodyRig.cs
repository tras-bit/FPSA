using Kordon.Core.Characters;
using UnityEngine;

namespace Kordon.Game.Characters
{
    /// <summary>
    /// Animates an authored operator body.
    ///
    /// Tools/blender/build_operators.py exports the body as rigid parts parented to
    /// empties on the hip, knee, shoulder and elbow centres, so the body walks by
    /// turning eight transforms - no armature, no skinned animation clip, and nothing
    /// that needs an editor to bake.  The maths lives in Kordon.Core.Characters.BodyGait
    /// and is covered by the core test suite; this component only reads velocity from
    /// the scene and applies the result.
    ///
    /// The rig is a pure view: it never touches position, yaw or health, which stay
    /// server state.  If the art is missing it silently does nothing, so a missing
    /// import cannot throw every frame of a match.
    /// </summary>
    public sealed class OperatorBodyRig : MonoBehaviour
    {
        public const string BodyNode = "Body";
        public const string HipLeftNode = "HipL";
        public const string HipRightNode = "HipR";
        public const string KneeLeftNode = "KneeL";
        public const string KneeRightNode = "KneeR";
        public const string ShoulderLeftNode = "ShoulderL";
        public const string ShoulderRightNode = "ShoulderR";
        public const string ElbowLeftNode = "ElbowL";
        public const string ElbowRightNode = "ElbowR";

        /// <summary>Speed above which the body is considered to be sprinting, m/s.</summary>
        public float SprintSpeedMps = 6.2f;

        private readonly BodyGait _gait = new BodyGait();

        private Transform _hipLeft;
        private Transform _hipRight;
        private Transform _kneeLeft;
        private Transform _kneeRight;
        private Transform _shoulderLeft;
        private Transform _shoulderRight;
        private Transform _elbowLeft;
        private Transform _elbowRight;

        private Transform _body;
        private Quaternion _restBodyRotation = Quaternion.identity;
        private float _aimPitchDegrees;
        private float _yawRateDegreesPerSecond;
        private float _lastYaw;
        private bool _hasLastYaw;

        public void SetAim(float pitchDegrees) => _aimPitchDegrees = pitchDegrees;

        private Quaternion _hipLeftRest = Quaternion.identity;
        private Quaternion _hipRightRest = Quaternion.identity;
        private Quaternion _kneeLeftRest = Quaternion.identity;
        private Quaternion _kneeRightRest = Quaternion.identity;
        private Quaternion _shoulderLeftRest = Quaternion.identity;
        private Quaternion _shoulderRightRest = Quaternion.identity;
        private Quaternion _elbowLeftRest = Quaternion.identity;
        private Quaternion _elbowRightRest = Quaternion.identity;

        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private Vector3 _velocity;
        private float _speed;

        private bool _dead;
        private float _deathTime;
        private float _breathTime;
        private Vector3 _downLocal = Vector3.down;
        private Vector3 _restLocalPosition;
        private Quaternion _restLocalRotation;

        public bool IsBound => _hipLeft != null && _kneeLeft != null;
        public bool IsDead => _dead;
        public float SpeedMps => _speed;

        /// <summary>
        /// Bind the rig to an instantiated body.  Returns null when the object is not an
        /// authored body (the fallback capsule), so callers can stay simple.
        /// </summary>
        public static OperatorBodyRig Bind(GameObject body)
        {
            if (body == null)
            {
                return null;
            }

            Transform hipLeft = FindNode(body.transform, HipLeftNode);
            if (hipLeft == null)
            {
                return null;
            }

            OperatorBodyRig rig = body.GetComponent<OperatorBodyRig>();
            if (rig == null)
            {
                rig = body.AddComponent<OperatorBodyRig>();
            }

            rig.Capture(body.transform);
            return rig;
        }

        private void Capture(Transform root)
        {
            _hipLeft = FindNode(root, HipLeftNode);
            _hipRight = FindNode(root, HipRightNode);
            _kneeLeft = FindNode(root, KneeLeftNode);
            _kneeRight = FindNode(root, KneeRightNode);
            _shoulderLeft = FindNode(root, ShoulderLeftNode);
            _shoulderRight = FindNode(root, ShoulderRightNode);
            _elbowLeft = FindNode(root, ElbowLeftNode);
            _elbowRight = FindNode(root, ElbowRightNode);
            // The rest rotations are kept, never assumed: if a future export bakes the
            // Z-up to Y-up conversion into each node, the rig still turns the joint
            // instead of tearing the limb off its own orientation.
            _body = FindNode(root, BodyNode);
            _restBodyRotation = RestOf(_body);
            _hipLeftRest = RestOf(_hipLeft);
            _hipRightRest = RestOf(_hipRight);
            _kneeLeftRest = RestOf(_kneeLeft);
            _kneeRightRest = RestOf(_kneeRight);
            _shoulderLeftRest = RestOf(_shoulderLeft);
            _shoulderRightRest = RestOf(_shoulderRight);
            _elbowLeftRest = RestOf(_elbowLeft);
            _elbowRightRest = RestOf(_elbowRight);
            _gait.SprintSpeedMps = Mathf.Max(1f, SprintSpeedMps);
            _gait.Reset();
            _restLocalPosition = transform.localPosition;
            _restLocalRotation = transform.localRotation;
            _downLocal = transform.parent != null
                ? transform.parent.InverseTransformDirection(Vector3.down)
                : Vector3.down;
            _lastPosition = transform.position;
            _hasLastPosition = true;
        }

        private static Quaternion RestOf(Transform node) =>
            node != null ? node.localRotation : Quaternion.identity;

        /// <summary>Depth first search for an exported node; the FBX keeps its names.</summary>
        private static Transform FindNode(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindNode(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>Start the death fall.  Safe to call more than once.</summary>
        public void PlayDeath()
        {
            if (_dead)
            {
                return;
            }

            _dead = true;
            _deathTime = 0f;
            // The corpse is left where it fell: the server respawn point is elsewhere and
            // a body that keeps walking after it dies is the clearest possible bug.
            _speed = 0f;
            _velocity = Vector3.zero;
            _hasLastPosition = false;
        }

        /// <summary>Bring the body back up when its owner respawns.</summary>
        public void ResetPose()
        {
            _dead = false;
            _deathTime = 0f;
            _gait.Reset();
            _speed = 0f;
            _velocity = Vector3.zero;
            _hasLastPosition = false;
            transform.localPosition = _restLocalPosition;
            transform.localRotation = _restLocalRotation;
            if (_body != null)
            {
                _body.localRotation = _restBodyRotation;
            }

            ApplyPose(default(GaitPose));
        }

        /// <summary>Feed the body's own movement; both the network view and the AI move the transform.</summary>
        public void Drive(Vector3 position, float deltaTime)
        {
            if (!IsBound)
            {
                return;
            }

            float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
            if (!_hasLastPosition)
            {
                _lastPosition = position;
                _hasLastPosition = true;
                _velocity = Vector3.zero;
                return;
            }

            if (dt <= 0f)
            {
                return;
            }

            float yaw = transform.eulerAngles.y;
            if (_hasLastYaw && dt > 0f)
            {
                float deltaYaw = Mathf.DeltaAngle(_lastYaw, yaw);
                _yawRateDegreesPerSecond = deltaYaw / dt;
            }

            _lastYaw = yaw;
            _hasLastYaw = true;

            Vector3 delta = position - _lastPosition;
            _lastPosition = position;
            _velocity = delta / dt;
            _velocity.y = 0f;
            _speed = Mathf.Clamp(_velocity.magnitude, 0f, 20f);
        }

        private void LateUpdate()
        {
            if (!IsBound)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            if (_dead)
            {
                _deathTime += dt;
                _gait.Fall(_deathTime, out float pitch, out float drop);
                transform.localRotation = _restLocalRotation * Quaternion.AngleAxis(pitch, Vector3.right);
                transform.localPosition = _restLocalPosition + _downLocal * drop;
                return;
            }

            // Planar speed of the body as the scene moved it this frame.  Anything
            // slower than a shuffle counts as standing: see BodyGait.StandSpeedThreshold.
            if (_speed < _gait.StandSpeedThreshold)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, dt * 3f);
            }

            _breathTime += dt;
            float breath = 0.5f + 0.5f * Mathf.Sin(_breathTime * 1.45f);
            float crouch = 0f;

            _gait.Advance(_speed, dt);
            GaitPose pose = _gait.Sample(_gait.SmoothedSpeedMps, crouch, breath);
            ApplyPose(pose);

            // The body leans: into a turn, into a sprint, and slightly towards where it
            // is aiming.  A torso that never leaves vertical is what makes a walking
            // body read as a model being slid across the floor.
            float lean = pose.RollDegrees +
                         _gait.TurnLeanDegrees(_yawRateDegreesPerSecond);
            transform.localRotation = _restLocalRotation *
                Quaternion.AngleAxis(lean, Vector3.forward);
            _body.localRotation = _restBodyRotation * Quaternion.AngleAxis(
                -_gait.UpperBodyPitchDegrees(_aimPitchDegrees, _speed / _gait.SprintSpeedMps,
                    crouch), Vector3.right);
        }

        /// <summary>
        /// Turn the pose into local rotations of the eight joint nodes.
        ///
        /// The bodies face Blender +Y, which is Unity +Z, so a leg swinging forward is a
        /// negative rotation about the node's local X (the maths says "positive is
        /// forward", the adapter owns the sign).  Knees only ever fold backwards.
        /// </summary>
        private void ApplyPose(GaitPose pose)
        {
            SetJoint(_hipLeft, _hipLeftRest, -pose.ThighLeftDegrees);
            SetJoint(_hipRight, _hipRightRest, -pose.ThighRightDegrees);
            SetJoint(_kneeLeft, _kneeLeftRest, pose.KneeLeftDegrees);
            SetJoint(_kneeRight, _kneeRightRest, pose.KneeRightDegrees);
            SetJoint(_shoulderLeft, _shoulderLeftRest, -pose.UpperArmLeftDegrees);
            SetJoint(_shoulderRight, _shoulderRightRest, -pose.UpperArmRightDegrees);
            SetJoint(_elbowLeft, _elbowLeftRest, -pose.ElbowLeftDegrees);
            SetJoint(_elbowRight, _elbowRightRest, -pose.ElbowRightDegrees);
        }

        private static void SetJoint(Transform joint, Quaternion rest, float degrees)
        {
            if (joint == null)
            {
                return;
            }

            joint.localRotation = rest * Quaternion.AngleAxis(degrees, Vector3.right);
        }
    }
}
