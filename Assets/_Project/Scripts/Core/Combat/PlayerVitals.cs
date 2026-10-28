using System;
using System.Collections.Generic;
using Kordon.Core.Utility;

namespace Kordon.Core.Combat
{
    /// <summary>How a character reacts to reaching zero health.</summary>
    public enum VitalsMode
    {
        /// <summary>Free-for-all: zero health is an immediate elimination.</summary>
        Lethal = 0,

        /// <summary>Co-op: zero health downs the operator, teammates can revive.</summary>
        DownAndRevive = 1
    }

    /// <summary>What killed / downed a character, for the kill feed and mission log.</summary>
    public enum DeathCause
    {
        None = 0,
        Gunfire = 1,
        BleedOut = 2,
        Explosion = 3,
        Fall = 4,
        Disconnect = 5
    }

    /// <summary>
    /// Health, per-zone damage, bleeding and the resulting mobility / aim penalties.
    /// Owned by the server; clients receive a replicated snapshot.
    /// </summary>
    [Serializable]
    public sealed class PlayerVitals
    {
        public const float MaxHealth = 100f;
        public const float MaxLimbDamage = 60f;
        public const float ReviveHealthFraction = 0.45f;

        private readonly Dictionary<HitZone, float> _zoneDamage = new Dictionary<HitZone, float>();

        public VitalsMode Mode { get; set; } = VitalsMode.Lethal;

        public float Health { get; private set; } = MaxHealth;
        public float BleedDamagePerSecond { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsDowned { get; private set; }
        public DeathCause LastCause { get; private set; } = DeathCause.None;
        public HitZone FatalZone { get; private set; } = HitZone.None;

        /// <summary>1 = unhindered, lower when legs/pelvis are damaged.</summary>
        public float MobilityMultiplier { get; private set; } = 1f;

        /// <summary>1 = steady, lower when arms/torso are damaged.</summary>
        public float AimStabilityMultiplier { get; private set; } = 1f;

        public SuppressionModel Suppression { get; } = new SuppressionModel();

        public bool IsIncapacitated => IsDead || IsDowned;

        public PlayerVitals(VitalsMode mode = VitalsMode.Lethal)
        {
            Mode = mode;
            Reset();
        }

        public void Reset()
        {
            Health = MaxHealth;
            BleedDamagePerSecond = 0f;
            IsDead = false;
            IsDowned = false;
            LastCause = DeathCause.None;
            FatalZone = HitZone.None;
            MobilityMultiplier = 1f;
            AimStabilityMultiplier = 1f;
            Suppression.Reset();
            _zoneDamage.Clear();
        }

        public float ZoneDamage(HitZone zone) => _zoneDamage.TryGetValue(zone, out float v) ? v : 0f;

        /// <summary>Apply a resolved damage result. Returns true when it changed the alive/downed state.</summary>
        public bool ApplyDamage(DamageResult result, DeathCause cause = DeathCause.Gunfire)
        {
            if (IsIncapacitated || !result.Accepted || result.Damage <= 0f && !result.Fatal)
            {
                return false;
            }

            if (result.ArmorDurabilityDamage > 0f && result.Zone.IsValid())
            {
                // armour durability lives in ArmorState, but vitals track the pain of absorbed hits
                Suppression.Add(DamageModel.SuppressionFromHit(result.Damage) * 0.5f);
            }

            Health = Math.Max(0f, Health - result.Damage);
            Suppression.Add(result.SuppressionAdded);

            if (result.BleedDamagePerSecond > 0f)
            {
                BleedDamagePerSecond = Math.Min(4.5f, BleedDamagePerSecond + result.BleedDamagePerSecond);
            }

            if (result.Zone.IsValid() && result.Zone != HitZone.Gear)
            {
                float accumulated = ZoneDamage(result.Zone) + result.Damage;
                _zoneDamage[result.Zone] = Math.Min(MaxLimbDamage * 2f, accumulated);
                RecomputePenalties();
            }

            if (result.Fatal || Health <= 0f)
            {
                FatalZone = result.Zone;
                LastCause = cause;
                if (Mode == VitalsMode.DownAndRevive && !IsDowned && cause == DeathCause.Gunfire)
                {
                    IsDowned = true;
                    BleedDamagePerSecond = Math.Max(BleedDamagePerSecond, 1.2f);
                    return true;
                }

                IsDead = true;
                return true;
            }

            return false;
        }

        /// <summary>Advance bleeding, suppression decay and bleed-out.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            Suppression.Tick(deltaTime);

            if (BleedDamagePerSecond > 0f && !IsDead)
            {
                Health = Math.Max(0f, Health - BleedDamagePerSecond * deltaTime);
                if (Health <= 0f)
                {
                    LastCause = DeathCause.BleedOut;
                    if (Mode == VitalsMode.DownAndRevive && !IsDowned)
                    {
                        IsDowned = true;
                    }
                    else
                    {
                        IsDead = true;
                    }
                }
            }

            if (IsDowned)
            {
                // downed operators bleed out unless revived
                Health = Math.Max(0f, Health - 1.5f * deltaTime);
                if (Health <= 0f)
                {
                    IsDowned = false;
                    IsDead = true;
                    LastCause = DeathCause.BleedOut;
                }
            }
        }

        public void StopBleeding() => BleedDamagePerSecond = 0f;

        public void Heal(float amount)
        {
            if (IsDead)
            {
                return;
            }

            Health = MathUtil.Clamp(Health + Math.Max(0f, amount), 0f, MaxHealth);
        }

        /// <summary>Co-op revive: brings a downed operator back with partial health.</summary>
        public bool Revive()
        {
            if (!IsDowned || IsDead)
            {
                return false;
            }

            IsDowned = false;
            Health = MaxHealth * ReviveHealthFraction;
            BleedDamagePerSecond = 0f;
            Suppression.Reset();
            return true;
        }

        private void RecomputePenalties()
        {
            float legDamage = ZoneDamage(HitZone.LeftLeg) + ZoneDamage(HitZone.RightLeg);
            float pelvisDamage = ZoneDamage(HitZone.Pelvis);
            float armDamage = ZoneDamage(HitZone.LeftArm) + ZoneDamage(HitZone.RightArm);
            float torsoDamage = ZoneDamage(HitZone.Thorax) + ZoneDamage(HitZone.Abdomen);

            float mobility = 1f
                             - MathUtil.Clamp01(legDamage / MaxLimbDamage) * 0.55f
                             - MathUtil.Clamp01(pelvisDamage / MaxLimbDamage) * 0.25f;
            float aim = 1f
                        - MathUtil.Clamp01(armDamage / MaxLimbDamage) * 0.5f
                        - MathUtil.Clamp01(torsoDamage / (MaxLimbDamage * 1.6f)) * 0.2f;

            MobilityMultiplier = MathUtil.Clamp(mobility, 0.35f, 1f);
            AimStabilityMultiplier = MathUtil.Clamp(aim, 0.4f, 1f);
        }
    }
}
