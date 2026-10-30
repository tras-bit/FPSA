using System;

namespace Kordon.Core.Weapons
{
    /// <summary>Handling state of the first person weapon, driven by ammo events.</summary>
    public enum ViewmodelState
    {
        Ready = 0,
        Aiming = 1,
        Sprinting = 2,
        ReloadOut = 3,
        ReloadIn = 4,
        Charging = 5,
        Inspecting = 6,
        Fumble = 7,
        Empty = 8,
    }

    /// <summary>Beats the ammo system reports; the animator turns them into motion.</summary>
    public enum ViewmodelEvent
    {
        MagazineOut = 0,
        MagazineIn = 1,
        BoltRelease = 2,
        InspectStart = 3,
        InspectEnd = 4,
        FumbleStart = 5,
        FumbleRecovered = 6,
        ReloadComplete = 7,
        OutOfAmmunition = 8,
    }

    /// <summary>Everything the animator reads about the player each frame.</summary>
    public struct ViewmodelInput
    {
        public bool Aiming;
        public bool Sprinting;
        public bool GrenadeOut;
        public bool Grounded;

        /// <summary>0 at a standstill, 1 at full sprint.</summary>
        public float Speed01;

        /// <summary>0 with steady breath, 1 at the end of a sprint.</summary>
        public float Breath01;

        /// <summary>Look rate in degrees per second, for the weapon lag.</summary>
        public float YawRate;
        public float PitchRate;

        /// <summary>Recoil impulse: 0..1, added once and consumed by the animator.</summary>
        public float RecoilImpulse;

        /// <summary>Landing impulse: 0..1, added on touchdown.</summary>
        public float LandingImpulse;
    }

    /// <summary>
    /// One frame of viewmodel motion, in metres and degrees, relative to the camera.
    /// </summary>
    public struct ViewmodelPose
    {
        public float X;
        public float Y;
        public float Z;
        public float Pitch;
        public float Yaw;
        public float Roll;

        /// <summary>Magazine offset from its seated position, in the weapon's space.</summary>
        public float MagazineX;
        public float MagazineY;
        public float MagazineZ;
        public float MagazinePitch;
        public float MagazineRoll;
        public bool MagazineVisible;

        /// <summary>
        /// 0 with the support hand on the fore-end, 1 with it on the magazine.
        /// The adapter slides the left hand along this: a reload where the hand
        /// stays on the handguard while the magazine falls out is the single most
        /// obvious tell that a game has no first person hands at all.
        /// </summary>
        public float SupportHandBlend;

        /// <summary>0 seated, 1 fully racked back.</summary>
        public float BoltTravel;

        /// <summary>How far the trigger is pulled, 0 to 1: snaps back on each shot.</summary>
        public float TriggerTravel;

        /// <summary>0 at the hip, 1 with the sights up: the camera uses this too.</summary>
        public float AimBlend;

        public ViewmodelState State;
    }

    /// <summary>Per weapon numbers the animator needs, measured from the art.</summary>
    public struct ViewmodelConfig
    {
        /// <summary>Distance from the grip to the top of the iron sights, metres.</summary>
        public float SightHeight;

        /// <summary>Overall length, metres: decides how far out the weapon is held.</summary>
        public float Length;

        /// <summary>How far the magazine travels when it leaves the well, metres.</summary>
        public float MagazineDrop;

        /// <summary>Seconds the empty magazine takes to clear the well.</summary>
        public float MagazineDropSeconds;

        /// <summary>Seconds the fresh magazine takes to travel in and seat.</summary>
        public float MagazineInsertSeconds;

        /// <summary>Where the magazine is held while it is being inspected.</summary>
        public float InspectHoldHeight;

        public static ViewmodelConfig Default => new ViewmodelConfig
        {
            SightHeight = 0.068f,
            Length = 0.9f,
            MagazineDrop = 0.20f,
            // 0.70 s of drop, half of it the lead-in before the magazine clears the
            // well: a reload is a beat, not a frame.
            MagazineDropSeconds = 0.70f,
            MagazineInsertSeconds = 0.30f,
            InspectHoldHeight = 0.10f,
        };
    }

    /// <summary>
    /// The procedural viewmodel animation that drives the first person weapon.
    ///
    /// This exists in the engine free core so it can be tested without Unity: the
    /// previous version smoothed a single position toward a target with a fixed
    /// lerp, which is why the weapon slid between poses and the magazine snapped.
    ///
    /// What it does, as layers, composed every frame:
    ///
    /// * <b>state pose</b> - hip, sights, sprint, reload, inspect, charge, fumble and
    ///   empty each have their own pose, and the transition is an exponential
    ///   approach with a per state rate (sights come up fast, a reload settles slow);
    /// * <b>sway</b> - the weapon lags behind the camera when the view turns, driven by
    ///   the look rate with a critically damped spring, so turning the view swings the
    ///   weapon a moment later and it returns to centre on its own;
    /// * <b>bob</b> - a figure eight tied to the footfall phase, scaled by speed and
    ///   damped while aiming;
    /// * <b>breathing</b> - a slow drift scaled by how hard the operator is breathing;
    /// * <b>recoil</b> - an impulse that kicks the weapon back and up, then a spring
    ///   with real overshoot brings it back down;
    /// * <b>landing</b> - a downward dip and roll when the feet hit the ground;
    /// * <b>magazine timeline</b> - the magazine dropping out of the well, and the
    ///   fresh one rising, overshooting and seating;
    /// * <b>bolt</b> - the charging handle travelling back and running home.
    ///
    /// Everything is deterministic: the same inputs and the same dt sequence produce
    /// exactly the same pose, which is what makes it testable.
    /// </summary>
    public sealed class ViewmodelAnimator
    {
        private readonly ViewmodelConfig _config;

        private ViewmodelState _state = ViewmodelState.Ready;
        private ViewmodelState _poseState = ViewmodelState.Ready;
        private float _stateTime;

        private float _x;
        private float _y;
        private float _z;
        private float _pitch;
        private float _yaw;
        private float _roll;
        private float _aimBlend;

        // lagged look rates, for the sway spring
        private float _swayYaw;
        private float _swayPitch;
        private float _swayYawVelocity;
        private float _swayPitchVelocity;

        // recoil and landing springs
        private float _recoil;
        private float _recoilVelocity;
        private float _recoilPitch;
        private float _recoilPitchVelocity;
        private float _landing;
        private float _trigger;
        private float _triggerTime;
        private float _landingVelocity;

        // bob and breathing phases, advanced by the animator so they are reproducible
        private float _bobPhase;
        private float _breathPhase;

        private bool _boltCharging;
        private float _boltTime;
        private float _boltTravel;

        private enum MagazineState
        {
            Seated,
            Dropping,
            Hidden,
            Inserting,
            Inspecting,
        }

        private MagazineState _magazine = MagazineState.Seated;
        private float _magazineTime;

        private ViewmodelPose _pose;

        public ViewmodelAnimator(ViewmodelConfig config)
        {
            _config = config;
        }

        public ViewmodelState State => _state;

        public ViewmodelPose Pose => _pose;

        /// <summary>True while the magazine is out of the weapon (in the hand or gone).</summary>
        public bool MagazineOut => _magazine == MagazineState.Dropping ||
                                   _magazine == MagazineState.Hidden ||
                                   _magazine == MagazineState.Inserting ||
                                   _magazine == MagazineState.Inspecting;

        // -------------------------------------------------------------- events

        /// <summary>A beat from the ammo system. Unknown beats are ignored.</summary>
        public void Notify(ViewmodelEvent kind)
        {
            switch (kind)
            {
                case ViewmodelEvent.MagazineOut:
                    _state = ViewmodelState.ReloadOut;
                    _stateTime = 0f;
                    _magazine = MagazineState.Dropping;
                    _magazineTime = 0f;
                    break;

                case ViewmodelEvent.MagazineIn:
                    _state = ViewmodelState.ReloadIn;
                    _stateTime = 0f;
                    _magazine = MagazineState.Inserting;
                    _magazineTime = 0f;
                    break;

                case ViewmodelEvent.BoltRelease:
                    _state = ViewmodelState.Charging;
                    _stateTime = 0f;
                    _boltCharging = true;
                    _boltTime = 0f;
                    break;

                case ViewmodelEvent.InspectStart:
                    _state = ViewmodelState.Inspecting;
                    _stateTime = 0f;
                    _magazine = MagazineState.Inspecting;
                    _magazineTime = 0f;
                    break;

                case ViewmodelEvent.InspectEnd:
                    _state = ViewmodelState.Ready;
                    _stateTime = 0f;
                    _magazine = MagazineState.Seated;
                    _magazineTime = 0f;
                    break;

                case ViewmodelEvent.FumbleStart:
                    _state = ViewmodelState.Fumble;
                    _stateTime = 0f;
                    break;

                case ViewmodelEvent.FumbleRecovered:
                case ViewmodelEvent.ReloadComplete:
                    _state = ViewmodelState.Ready;
                    _stateTime = 0f;
                    break;

                case ViewmodelEvent.OutOfAmmunition:
                    _state = ViewmodelState.Empty;
                    _stateTime = 0f;
                    break;
            }
        }

        /// <summary>Snaps the trigger back; it releases over the next few hundredths.</summary>
        public void PullTrigger()
        {
            _trigger = 1f;
            _triggerTime = 0f;
        }

        /// <summary>Adds a recoil impulse, clamped so a burst cannot stack to absurdity.</summary>
        public void AddRecoil(float impulse)
        {
            _recoil = Clamp01(_recoil + Math.Max(0f, impulse));
            _recoilPitch = Clamp01(_recoilPitch + Math.Max(0f, impulse) * 0.9f);
            _recoilVelocity += Math.Max(0f, impulse) * 2.4f;
            _recoilPitchVelocity += Math.Max(0f, impulse) * 3.1f;
        }

        // ---------------------------------------------------------------- tick

        public void Tick(float dt, ViewmodelInput input)
        {
            if (dt < 0f)
            {
                dt = 0f;
            }

            dt = Math.Min(dt, 0.1f);
            _stateTime += dt;

            if (input.RecoilImpulse > 0f)
            {
                AddRecoil(input.RecoilImpulse);
                PullTrigger();
            }

            if (input.LandingImpulse > 0f)
            {
                _landingVelocity -= Math.Max(0f, input.LandingImpulse) * 2.2f;
            }

            ResolveState(input);

            // ---- base pose for the state -----------------------------------
            float targetX = 0f, targetY = 0f, targetZ = 0f;
            float targetPitch = 0f, targetYaw = 0f, targetRoll = 0f;
            float rate = 12f;

            float lengthT = Clamp01(_config.Length / 0.95f);
            float hipX = Lerp(0.128f, 0.168f, lengthT);
            float hipY = Lerp(-0.132f, -0.198f, lengthT);
            float hipZ = Lerp(0.300f, 0.455f, lengthT);
            float aimZ = Lerp(0.215f, 0.335f, lengthT);

            switch (_poseState)
            {
                case ViewmodelState.Aiming:
                    targetX = 0f;
                    targetY = -_config.SightHeight + 0.004f;
                    targetZ = aimZ;
                    rate = 16f;
                    break;

                case ViewmodelState.Sprinting:
                    targetX = 0.105f;
                    targetY = -0.268f;
                    targetZ = 0.352f;
                    targetPitch = 14f;
                    targetYaw = -16f;
                    targetRoll = 24f;
                    rate = 9f;
                    break;

                case ViewmodelState.ReloadOut:
                case ViewmodelState.ReloadIn:
                    // The weapon is rolled towards the player and pulled in close, so
                    // the magazine well faces the camera and the support hand has
                    // somewhere to work.  A reload that happens out of frame is a
                    // reload the player cannot see, which is what makes it feel like
                    // the magazine teleported.
                    targetX = 0.104f;
                    targetY = -0.176f;
                    targetZ = 0.352f;
                    targetPitch = 20f;
                    targetYaw = 14f;
                    targetRoll = -46f;
                    rate = 14f;
                    break;

                case ViewmodelState.Inspecting:
                    targetX = 0.092f;
                    targetY = -0.206f;
                    targetZ = 0.408f;
                    targetPitch = 42f;
                    targetYaw = 12f;
                    targetRoll = -50f;
                    rate = 8f;
                    break;

                case ViewmodelState.Charging:
                    targetX = hipX;
                    targetY = hipY - 0.006f;
                    targetZ = hipZ - 0.02f;
                    targetPitch = 7f;
                    targetRoll = -9f;
                    rate = 13f;
                    break;

                case ViewmodelState.Fumble:
                    targetX = hipX + 0.012f;
                    targetY = hipY - 0.036f;
                    targetZ = hipZ - 0.03f;
                    targetPitch = -7f;
                    targetYaw = 11f;
                    targetRoll = 9f;
                    rate = 11f;
                    break;

                case ViewmodelState.Empty:
                    targetX = hipX;
                    targetY = hipY - 0.004f;
                    targetZ = hipZ;
                    targetPitch = 3f;
                    targetRoll = -4f;
                    rate = 10f;
                    break;

                default:
                    targetX = hipX;
                    targetY = hipY;
                    targetZ = hipZ;
                    rate = 12f;
                    break;
            }

            // While reloading or inspecting the weapon does not come to the sights;
            // trying to aim during those states only tightens the grip.
            float aimTarget = input.Aiming && (_poseState == ViewmodelState.Ready ||
                                               _poseState == ViewmodelState.Aiming)
                ? 1f
                : 0f;
            _aimBlend = Approach(_aimBlend, aimTarget, 13f, dt);

            if (aimTarget > 0.5f)
            {
                targetX = Lerp(targetX, 0f, _aimBlend);
                targetY = Lerp(targetY, -_config.SightHeight + 0.004f, _aimBlend);
                targetZ = Lerp(targetZ, aimZ, _aimBlend);
                targetPitch = Lerp(targetPitch, 0f, _aimBlend);
                targetYaw = Lerp(targetYaw, 0f, _aimBlend);
                targetRoll = Lerp(targetRoll, 0f, _aimBlend);
            }

            _x = Approach(_x, targetX, rate, dt);
            _y = Approach(_y, targetY, rate, dt);
            _z = Approach(_z, targetZ, rate, dt);
            _pitch = Approach(_pitch, targetPitch, rate, dt);
            _yaw = Approach(_yaw, targetYaw, rate, dt);
            _roll = Approach(_roll, targetRoll, rate, dt);

            // ---- sway: the weapon lags behind the camera -------------------
            float swayScale = Lerp(1f, 0.22f, _aimBlend);
            float swayTargetYaw = Clamp(input.YawRate * 0.0016f, -0.045f, 0.045f) * swayScale;
            float swayTargetPitch = Clamp(input.PitchRate * 0.0013f, -0.035f, 0.035f) * swayScale;
            Spring(ref _swayYaw, ref _swayYawVelocity, swayTargetYaw, 46f, 11f, dt);
            Spring(ref _swayPitch, ref _swayPitchVelocity, swayTargetPitch, 46f, 11f, dt);

            // ---- bob: figure eight from the footfall phase -----------------
            bool moving = input.Speed01 > 0.02f && input.Grounded;
            _bobPhase += dt * (6.6f + input.Speed01 * 4.4f) * (moving ? 1f : 0f);
            if (_bobPhase > 6.2831853f)
            {
                _bobPhase -= 6.2831853f;
            }

            float bobAmount = moving ? Clamp01(input.Speed01) : 0f;
            bobAmount *= Lerp(1f, 0.15f, _aimBlend);
            float bobX = (float)Math.Sin(_bobPhase) * 0.0125f * bobAmount;
            float bobY = (float)Math.Abs(Math.Sin(_bobPhase * 2.0)) * 0.0092f * bobAmount;
            float bobRoll = (float)Math.Sin(_bobPhase) * 0.9f * bobAmount;

            // ---- breathing -------------------------------------------------
            _breathPhase += dt * (1.15f + input.Breath01 * 1.6f);
            if (_breathPhase > 6.2831853f)
            {
                _breathPhase -= 6.2831853f;
            }

            float breathAmount = 0.35f + Clamp01(input.Breath01);
            float breathX = (float)Math.Sin(_breathPhase) * 0.0021f * breathAmount;
            float breathY = (float)Math.Sin(_breathPhase * 1.37f) * 0.0026f * breathAmount;
            float breathPitch = (float)Math.Sin(_breathPhase * 0.83f) * 0.45f * breathAmount;

            // ---- recoil and landing springs -------------------------------
            Spring(ref _recoil, ref _recoilVelocity, 0f, 150f, 17f, dt);
            Spring(ref _recoilPitch, ref _recoilPitchVelocity, 0f, 165f, 18f, dt);
            Spring(ref _landing, ref _landingVelocity, 0f, 120f, 15f, dt);

            // ---- trigger: pulled on the shot, held a beat, released ---------
            // The blade is held for a third of the pull and then let go: a trigger that
            // returns in the same frame it was pulled cannot be seen at all at 60 fps.
            if (_trigger > 0f)
            {
                _triggerTime += dt;
                const float holdSeconds = 0.030f;
                const float releaseSeconds = 0.070f;
                float release = Clamp01((_triggerTime - holdSeconds) / releaseSeconds);
                _trigger = 1f - release;
            }

            _pose.TriggerTravel = _trigger;

            // ---- magazine and bolt ----------------------------------------
            UpdateMagazine(dt);
            UpdateBolt(dt);

            // ---- compose ---------------------------------------------------
            float aimPitchDamp = Lerp(1f, 0.55f, _aimBlend);
            // sway uses the lagged spring, not its target, so the weapon tucks over a
            // couple of frames instead of tracking the mouse exactly
            _pose.X = _x + _swayYaw * 0.13f + bobX + breathX;
            // the landing dip runs through both the position and the muzzle, which is
            // what sells a heavy kit on touchdown
            _pose.Y = _y + _swayPitch * 0.10f + bobY + breathY + _landing * 0.16f;
            _pose.Z = _z + _recoil * 0.055f;
            _pose.Pitch = _pitch - _recoilPitch * 6.5f * aimPitchDamp + breathPitch
                          - _landing * 26f * aimPitchDamp;
            _pose.Yaw = _yaw + _swayYaw * 55f;
            _pose.Roll = _roll + bobRoll - _swayYaw * 30f;
            _pose.BoltTravel = _boltTravel;
            _pose.AimBlend = _aimBlend;
            _pose.State = _poseState;
            _pose.MagazineVisible = _magazine != MagazineState.Hidden;
            _pose.MagazineX = _magazineX;
            _pose.MagazineY = _magazineY;
            _pose.MagazineZ = _magazineZ;
            _pose.MagazinePitch = _magazinePitch;
            _pose.MagazineRoll = _magazineRoll;

            // the support hand leaves the fore-end for the magazine well the moment
            // the handling state starts and returns when the weapon is ready again
            float handTarget = _poseState == ViewmodelState.ReloadOut ||
                               _poseState == ViewmodelState.ReloadIn ||
                               _poseState == ViewmodelState.Inspecting
                ? 1f : 0f;
            _supportHandBlend = Approach(_supportHandBlend, handTarget, 11f, dt);
            _pose.SupportHandBlend = _supportHandBlend;
        }

        private float _supportHandBlend;

        private float _magazineX;
        private float _magazineY;
        private float _magazineZ;
        private float _magazinePitch;
        private float _magazineRoll;

        // ------------------------------------------------------------ helpers

        private void ResolveState(ViewmodelInput input)
        {
            // The handling states own the weapon until they finish; sprinting only
            // takes over when nothing else is happening.
            if (_state == ViewmodelState.Ready || _state == ViewmodelState.Aiming ||
                _state == ViewmodelState.Sprinting)
            {
                if (input.Sprinting && !input.Aiming)
                {
                    _state = ViewmodelState.Sprinting;
                }
                else if (input.Aiming)
                {
                    _state = ViewmodelState.Aiming;
                }
                else
                {
                    _state = ViewmodelState.Ready;
                }
            }

            _poseState = _state;
        }

        private void UpdateMagazine(float dt)
        {
            _magazineTime += dt;

            switch (_magazine)
            {
                case MagazineState.Dropping:
                {
                    // Half of the drop time is spent waiting: the weapon has to come
                    // round into the reload pose first.  Without that beat the magazine
                    // clears the well while it is still out of frame, which is exactly
                    // how a reload looks like nothing happened.
                    float lead = Math.Max(0.01f, _config.MagazineDropSeconds) * 0.5f;
                    float t = Clamp01((_magazineTime - lead) /
                                      Math.Max(0.01f, _config.MagazineDropSeconds));
                    float eased = t * t * t;
                    float spin = t * t;
                    _magazineX = 0.020f * eased;
                    _magazineY = -_config.MagazineDrop * eased;
                    _magazineZ = -0.026f * eased;
                    _magazinePitch = -46f * spin;
                    _magazineRoll = 22f * spin;
                    if (t >= 1f)
                    {
                        _magazine = MagazineState.Hidden;
                        _magazineTime = 0f;
                    }

                    break;
                }

                case MagazineState.Hidden:
                    _magazineX = 0f;
                    _magazineY = -_config.MagazineDrop;
                    _magazineZ = -0.01f;
                    break;

                case MagazineState.Inserting:
                {
                    // Up into the well fast, then a firm seat: the last 15% of the
                    // travel is a single beat that ends with the weapon taking the
                    // hit, which is the part that sells the change.
                    float t = Clamp01(_magazineTime / Math.Max(0.01f, _config.MagazineInsertSeconds));
                    float eased = t < 0.72f
                        ? EaseOutCubic(t / 0.72f) * 0.94f
                        : 0.94f + 0.06f * ((t - 0.72f) / 0.28f);
                    float seat = t < 0.80f ? 0f : (float)Math.Sin((t - 0.80f) / 0.20f * Math.PI);
                    _magazineX = 0.020f * (1f - eased);
                    _magazineY = -_config.MagazineDrop * (1f - eased) - seat * 0.004f;
                    _magazineZ = -0.026f * (1f - eased);
                    _magazinePitch = -46f * (1f - eased);
                    _magazineRoll = 22f * (1f - eased);
                    if (seat > 0f)
                    {
                        // the slap is felt by the whole weapon, not just the magazine
                        _recoilVelocity += seat * 0.010f;
                        _recoilPitchVelocity -= seat * 2.4f;
                    }
                    if (t >= 1f)
                    {
                        _magazine = MagazineState.Seated;
                        _magazineTime = 0f;
                        _magazineX = 0f;
                        _magazineY = 0f;
                        _magazineZ = 0f;
                        _magazinePitch = 0f;
                        _magazineRoll = 0f;
                    }

                    break;
                }

                case MagazineState.Inspecting:
                {
                    // Pulled out and turned towards the operator: this is the beat the
                    // HUD is allowed to show a round count on, so it has to look like
                    // the magazine is actually being looked at.
                    float t = Clamp01(_magazineTime / 0.28f);
                    float eased = EaseOutCubic(t);
                    float breathe = (float)Math.Sin(_magazineTime * 2.2f) * 0.004f;
                    _magazineX = 0.010f * eased;
                    _magazineY = (-_config.InspectHoldHeight + breathe) * eased;
                    _magazineZ = 0.020f * eased;
                    _magazinePitch = -34f * eased;
                    _magazineRoll = 62f * eased;
                    break;
                }

                default:
                    _magazineX = 0f;
                    _magazineY = 0f;
                    _magazineZ = 0f;
                    _magazinePitch = 0f;
                    _magazineRoll = 0f;
                    break;
            }
        }

        private void UpdateBolt(float dt)
        {
            if (_boltCharging)
            {
                _boltTime += dt;
                // rack back hard, hold a beat, run home
                float t = _boltTime;
                if (t < 0.09f)
                {
                    _boltTravel = t / 0.09f;
                }
                else if (t < 0.16f)
                {
                    _boltTravel = 1f;
                }
                else if (t < 0.26f)
                {
                    _boltTravel = 1f - (t - 0.16f) / 0.10f;
                }
                else
                {
                    _boltTravel = 0f;
                    _boltCharging = false;
                }

                _boltTravel = Clamp01(_boltTravel);
                return;
            }

            // firing cycles the carrier a little; it runs home by itself
            _boltTravel = Approach(_boltTravel, 0f, 26f, dt);
        }

        /// <summary>Called by the weapon when a round leaves, for the cycling bolt.</summary>
        public void AddBoltCycle(float amount = 0.55f)
        {
            _boltTravel = Clamp01(Math.Max(_boltTravel, amount));
        }

        private static float Approach(float value, float target, float rate, float dt)
        {
            // exponential approach: frame rate independent, never overshoots
            float factor = 1f - (float)Math.Exp(-rate * dt);
            return value + (target - value) * factor;
        }

        private static void Spring(ref float value, ref float velocity, float target,
            float stiffness, float damping, float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            // semi implicit Euler, sub stepped so a long frame cannot explode
            int steps = dt > 0.02f ? (int)Math.Ceiling(dt / 0.01f) : 1;
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acceleration = (target - value) * stiffness - velocity * damping;
                velocity += acceleration * step;
                value += velocity * step;
            }
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - Clamp01(t);
            return 1f - inverse * inverse * inverse;
        }

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;

        private static float Clamp01(float value) => Clamp(value, 0f, 1f);

        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    }
}
