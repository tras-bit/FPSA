using System;
using System.Collections.Generic;

namespace Kordon.Core.Combat
{
    /// <summary>Protection level of one body zone inside an armour kit.</summary>
    [Serializable]
    public struct ZoneArmor
    {
        public HitZone Zone;

        /// <summary>0 = unarmoured, 6 = heaviest plate. Maps to a penetration threshold table.</summary>
        public int ArmorClass;

        /// <summary>Probability that a hit on this zone is actually covered by the plate (0..1).</summary>
        public float Coverage;

        /// <summary>Plate hit points; when exhausted the zone becomes unarmoured.</summary>
        public float Durability;

        public ZoneArmor(HitZone zone, int armorClass, float coverage, float durability)
        {
            Zone = zone;
            ArmorClass = armorClass;
            Coverage = coverage;
            Durability = durability;
        }
    }

    /// <summary>
    /// A wearable armour kit.  Kits are cosmetic/functional equalisers: every kit
    /// is available to every player from the first match, there is no progression
    /// gate and no paid advantage (see docs/01_GAME_DESIGN.md).
    /// </summary>
    [Serializable]
    public sealed class ArmorKitDefinition
    {
        public string Id = "standard";
        public string DisplayName = "Standard Plate Carrier";
        public float WeightKg = 6.5f;

        /// <summary>Movement speed multiplier while worn (heavier kits slow you down).</summary>
        public float MobilityMultiplier = 0.96f;

        /// <summary>Footstep noise multiplier (plates and pouches rattle).</summary>
        public float NoiseMultiplier = 1.1f;

        public List<ZoneArmor> Zones = new List<ZoneArmor>();

        public ArmorKitDefinition Clone()
        {
            ArmorKitDefinition copy = (ArmorKitDefinition)MemberwiseClone();
            copy.Zones = new List<ZoneArmor>(Zones);
            return copy;
        }

        public ZoneArmor GetZone(HitZone zone)
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                if (Zones[i].Zone == zone)
                {
                    return Zones[i];
                }
            }

            return new ZoneArmor(zone, 0, 0f, 0f);
        }

        public static ArmorKitDefinition Light() => new ArmorKitDefinition
        {
            Id = "light",
            DisplayName = "Light Chest Rig",
            WeightKg = 3.2f,
            MobilityMultiplier = 1f,
            NoiseMultiplier = 1f,
            Zones = new List<ZoneArmor>
            {
                new ZoneArmor(HitZone.Thorax, 2, 0.75f, 45f),
                new ZoneArmor(HitZone.Abdomen, 2, 0.6f, 35f)
            }
        };

        public static ArmorKitDefinition Standard() => new ArmorKitDefinition
        {
            Id = "standard",
            DisplayName = "Standard Plate Carrier",
            WeightKg = 6.5f,
            MobilityMultiplier = 0.96f,
            NoiseMultiplier = 1.1f,
            Zones = new List<ZoneArmor>
            {
                new ZoneArmor(HitZone.Thorax, 3, 0.9f, 70f),
                new ZoneArmor(HitZone.Abdomen, 3, 0.75f, 55f),
                new ZoneArmor(HitZone.Pelvis, 2, 0.5f, 35f)
            }
        };

        public static ArmorKitDefinition Heavy() => new ArmorKitDefinition
        {
            Id = "heavy",
            DisplayName = "Assault Armour Set",
            WeightKg = 11.4f,
            MobilityMultiplier = 0.9f,
            NoiseMultiplier = 1.25f,
            Zones = new List<ZoneArmor>
            {
                new ZoneArmor(HitZone.Head, 3, 0.85f, 60f),
                new ZoneArmor(HitZone.Neck, 2, 0.4f, 25f),
                new ZoneArmor(HitZone.Thorax, 4, 0.95f, 95f),
                new ZoneArmor(HitZone.Abdomen, 4, 0.85f, 80f),
                new ZoneArmor(HitZone.Pelvis, 3, 0.6f, 45f)
            }
        };
    }

    /// <summary>
    /// Live armour state carried by a character: plate durability degrades with
    /// every absorbed hit, and an exhausted plate stops protecting its zone.
    /// </summary>
    [Serializable]
    public sealed class ArmorState
    {
        private readonly Dictionary<HitZone, ZoneArmor> _zones = new Dictionary<HitZone, ZoneArmor>();

        public ArmorKitDefinition Kit { get; private set; }

        public ArmorState(ArmorKitDefinition kit)
        {
            Kit = kit ?? new ArmorKitDefinition();
            foreach (ZoneArmor zone in Kit.Zones)
            {
                _zones[zone.Zone] = zone;
            }
        }

        public bool HasCoverage(HitZone zone) => _zones.ContainsKey(zone) && _zones[zone].ArmorClass > 0;

        public int ArmorClass(HitZone zone) => _zones.TryGetValue(zone, out ZoneArmor a) ? a.ArmorClass : 0;

        public float Coverage(HitZone zone) => _zones.TryGetValue(zone, out ZoneArmor a) ? a.Coverage : 0f;

        public float Durability(HitZone zone) => _zones.TryGetValue(zone, out ZoneArmor a) ? a.Durability : 0f;

        public bool IsExhausted(HitZone zone) =>
            _zones.TryGetValue(zone, out ZoneArmor a) && a.ArmorClass > 0 && a.Durability <= 0f;

        public void ApplyDurabilityDamage(HitZone zone, float amount)
        {
            if (!_zones.TryGetValue(zone, out ZoneArmor a))
            {
                return;
            }

            a.Durability = Math.Max(0f, a.Durability - amount);
            _zones[zone] = a;
        }

        public void Reset()
        {
            _zones.Clear();
            foreach (ZoneArmor zone in Kit.Zones)
            {
                _zones[zone.Zone] = zone;
            }
        }
    }
}
