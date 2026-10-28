namespace Kordon.Core.Combat
{
    /// <summary>
    /// Surface/material taxonomy shared by penetration, impact effects, impact
    /// audio and footstep audio.  Each entry maps to a <see cref="MaterialDefinition"/>
    /// loaded from Data/Definitions/materials.json.
    /// </summary>
    public enum MaterialKind
    {
        Unknown = 0,
        Flesh = 1,
        Cloth = 2,
        Glass = 3,
        Wood = 4,
        Drywall = 5,
        Sand = 6,
        Gravel = 7,
        Concrete = 8,
        Brick = 9,
        SheetMetal = 10,
        Steel = 11,
        HardenedSteel = 12,
        Rubber = 13,
        Water = 14,
        Ceramic = 15
    }
}
