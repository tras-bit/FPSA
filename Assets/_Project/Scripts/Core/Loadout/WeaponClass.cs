namespace Kordon.Core.Loadout
{
    /// <summary>Weapon archetypes. Weapons differ by range, tempo, weight and control, not just damage.</summary>
    public enum WeaponClass
    {
        None = 0,
        AssaultRifle = 1,
        BattleRifle = 2,
        SubMachineGun = 3,
        MarksmanRifle = 4,
        SniperRifle = 5,
        Shotgun = 6,
        Sidearm = 7,
        Grenade = 8,
        Melee = 9
    }

    /// <summary>Sight variants. Every sight is available to every player: they are options, not upgrades.</summary>
    public enum SightKind
    {
        IronSight = 0,
        RedDot = 1,
        Holographic = 2,
        Scope1p5x = 3,
        Scope4x = 4,
        Scope8x = 5
    }
}
