using System;

namespace Kordon.Core.Combat
{
    /// <summary>
    /// Body zones used by the damage model.  Every character collider in the
    /// game is split into these zones (see Kordon.Game/Player/HitZoneColliderMap),
    /// and the server resolves every hit against them.
    /// </summary>
    public enum HitZone
    {
        None = 0,
        Head = 1,
        Neck = 2,
        Thorax = 3,
        Abdomen = 4,
        Pelvis = 5,
        LeftArm = 6,
        RightArm = 7,
        LeftLeg = 8,
        RightLeg = 9,

        /// <summary>Holstered gear, backpacks and pouches: absorbs damage, never lethal.</summary>
        Gear = 10
    }

    public static class HitZoneExtensions
    {
        /// <summary>Zones where a penetrating hit is immediately fatal ("danger zone").</summary>
        public static bool IsVitalZone(this HitZone zone) =>
            zone == HitZone.Head || zone == HitZone.Neck;

        public static bool IsLimb(this HitZone zone) =>
            zone == HitZone.LeftArm || zone == HitZone.RightArm ||
            zone == HitZone.LeftLeg || zone == HitZone.RightLeg;

        public static bool IsTorso(this HitZone zone) =>
            zone == HitZone.Thorax || zone == HitZone.Abdomen || zone == HitZone.Pelvis;

        public static bool IsValid(this HitZone zone) =>
            zone != HitZone.None && Enum.IsDefined(typeof(HitZone), zone);
    }
}
