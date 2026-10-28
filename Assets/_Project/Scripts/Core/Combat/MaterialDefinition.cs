using System;

namespace Kordon.Core.Combat
{
    /// <summary>
    /// Physical description of a surface, used for penetration, impact FX, impact
    /// audio and footstep audio.  Values are authored in
    /// Assets/_Project/Data/Definitions/materials.json.
    /// </summary>
    [Serializable]
    public sealed class MaterialDefinition
    {
        public MaterialKind Kind;
        public string Id = "unknown";
        public string DisplayName = "Unknown";

        /// <summary>Millimetres of equivalent rolled homogeneous armour that stop a reference bullet.</summary>
        public float PenetrationResistance = 1f;

        /// <summary>Reference thickness in millimetres for a "typical" instance of this material in the level.</summary>
        public float ReferenceThicknessMm = 100f;

        /// <summary>Chance per shot that a bullet is deflected instead of penetrating, at 0 degrees from normal.</summary>
        public float BaseRicochetChance = 0.05f;

        /// <summary>Angle (degrees from the surface normal) above which ricochet chance rises sharply.</summary>
        public float RicochetAngleThresholdDeg = 55f;

        /// <summary>Damage kept by a bullet that does get through, 0..1.</summary>
        public float ResidualDamageFactor = 0.55f;

        /// <summary>Whether shooting through it is allowed at all (glass shatters, steel may not be penetrable).</summary>
        public bool Penetrable = true;

        /// <summary>Whether the surface can be destroyed by gunfire (limited tactical destruction).</summary>
        public bool Destructible = false;

        /// <summary>Structural integrity for destructible surfaces.</summary>
        public float DestructibleIntegrity = 100f;

        /// <summary>Footstep/impact audio surface id (see Assets/_Project/Data/Definitions/audio_surfaces.json).</summary>
        public string AudioSurfaceId = "concrete";

        /// <summary>Footstep loudness multiplier (1 = reference concrete).</summary>
        public float NoiseMultiplier = 1f;

        public MaterialDefinition Clone() => (MaterialDefinition)MemberwiseClone();

        public static MaterialDefinition Default(MaterialKind kind) => new MaterialDefinition
        {
            Kind = kind,
            Id = kind.ToString().ToLowerInvariant(),
            DisplayName = kind.ToString(),
            PenetrationResistance = DefaultResistance(kind),
            ReferenceThicknessMm = DefaultThickness(kind),
            BaseRicochetChance = DefaultRicochet(kind),
            ResidualDamageFactor = kind == MaterialKind.Flesh ? 1f : 0.55f,
            AudioSurfaceId = DefaultAudioSurface(kind),
            NoiseMultiplier = DefaultNoise(kind)
        };

        private static float DefaultResistance(MaterialKind kind)
        {
            switch (kind)
            {
                case MaterialKind.Flesh: return 0.05f;
                case MaterialKind.Cloth: return 0.1f;
                case MaterialKind.Glass: return 0.4f;
                case MaterialKind.Wood: return 1.2f;
                case MaterialKind.Drywall: return 0.9f;
                case MaterialKind.Sand: return 2.4f;
                case MaterialKind.Gravel: return 2.0f;
                case MaterialKind.Concrete: return 4.5f;
                case MaterialKind.Brick: return 3.6f;
                case MaterialKind.SheetMetal: return 2.2f;
                case MaterialKind.Steel: return 6.0f;
                case MaterialKind.HardenedSteel: return 11.0f;
                case MaterialKind.Rubber: return 1.4f;
                case MaterialKind.Water: return 0.8f;
                case MaterialKind.Ceramic: return 5.0f;
                default: return 1f;
            }
        }

        private static float DefaultThickness(MaterialKind kind)
        {
            switch (kind)
            {
                case MaterialKind.Glass: return 6f;
                case MaterialKind.SheetMetal: return 3f;
                case MaterialKind.Wood: return 25f;
                case MaterialKind.Drywall: return 15f;
                case MaterialKind.Concrete: return 200f;
                case MaterialKind.Brick: return 120f;
                case MaterialKind.Steel: return 12f;
                case MaterialKind.HardenedSteel: return 10f;
                default: return 50f;
            }
        }

        private static float DefaultRicochet(MaterialKind kind)
        {
            switch (kind)
            {
                case MaterialKind.Steel: return 0.45f;
                case MaterialKind.HardenedSteel: return 0.6f;
                case MaterialKind.Concrete: return 0.2f;
                case MaterialKind.Brick: return 0.18f;
                case MaterialKind.SheetMetal: return 0.25f;
                case MaterialKind.Water: return 0.5f;
                default: return 0.05f;
            }
        }

        private static string DefaultAudioSurface(MaterialKind kind)
        {
            switch (kind)
            {
                case MaterialKind.Glass: return "glass";
                case MaterialKind.Wood: return "wood";
                case MaterialKind.SheetMetal:
                case MaterialKind.Steel:
                case MaterialKind.HardenedSteel: return "metal";
                case MaterialKind.Sand: return "sand";
                case MaterialKind.Gravel: return "gravel";
                case MaterialKind.Water: return "water";
                case MaterialKind.Flesh: return "flesh";
                case MaterialKind.Cloth: return "cloth";
                case MaterialKind.Rubber: return "rubber";
                default: return "concrete";
            }
        }

        private static float DefaultNoise(MaterialKind kind)
        {
            switch (kind)
            {
                case MaterialKind.SheetMetal:
                case MaterialKind.Steel:
                case MaterialKind.HardenedSteel: return 1.35f;
                case MaterialKind.Gravel: return 1.25f;
                case MaterialKind.Water: return 1.3f;
                case MaterialKind.Wood: return 1.1f;
                case MaterialKind.Sand: return 0.7f;
                case MaterialKind.Cloth: return 0.45f;
                case MaterialKind.Rubber: return 0.6f;
                default: return 1f;
            }
        }
    }
}
