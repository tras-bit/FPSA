using Kordon.Core.Utility;

namespace Kordon.Core.Maps
{
    /// <summary>
    /// The one conversion between authored map space (Blender, Z up) and the engine's
    /// space (Unity, Y up).
    ///
    /// The kit, the maps, the weapons and the operator bodies are all exported from
    /// Blender with the Unity preset (`axis_forward = -Z`, `axis_up = Y`), which means
    /// Unity receives a point (x, y, z) authored in Blender as:
    ///
    ///     (x, z, -y)
    ///
    /// i.e. Blender's up (+Z) becomes Unity's up (+Y), and Blender's front (-Y) becomes
    /// Unity's front (+Z). A rotation of psi about Blender's +Z becomes a rotation of
    /// psi about Unity's +Y - the axis conversion preserves the angle's sign because it
    /// is a rotation, not a reflection.
    ///
    /// The previous version of this mapping used (x, z, y): a *mirror*, not a rotation.
    /// Positions and yaws were mirrored together, but the module geometry comes from the
    /// FBX and is not mirrored, so every asymmetric module (stairs, doors, conveyors,
    /// gantries) ended up turned the wrong way relative to the gap it was meant to fill -
    /// which is what made the assembled map look like collapsed pieces instead of a yard.
    ///
    /// The maths lives in the engine free core so the conversion is covered by tests
    /// instead of being trusted.
    /// </summary>
    public static class MapOrientation
    {
        /// <summary>Authored (Blender) point to engine (Unity) point.</summary>
        public static KordonVec3 ToEngine(KordonVec3 authored) =>
            new KordonVec3(authored.X, authored.Z, -authored.Y);

        /// <summary>Engine (Unity) point back to the authored (Blender) space.</summary>
        public static KordonVec3 ToAuthored(KordonVec3 engine) =>
            new KordonVec3(engine.X, -engine.Z, engine.Y);

        /// <summary>
        /// Authored yaw (degrees about Blender +Z, from +X towards +Y) to engine yaw
        /// (degrees about Unity +Y, from +Z towards +X). The signed angle is unchanged.
        /// </summary>
        public static float YawToEngine(float authoredYawDegrees) => MathUtil.WrapAngle(authoredYawDegrees);

        /// <summary>Engine yaw back to authored yaw.</summary>
        public static float YawToAuthored(float engineYawDegrees) => MathUtil.WrapAngle(engineYawDegrees);

        /// <summary>
        /// Horizontal heading of an engine-space direction, in authored degrees, so the
        /// server can reason about a player's facing in the map's own convention.
        ///
        /// A placement's yaw is a rotation about the up axis: yaw 0 leaves a module as
        /// authored, so authored yaw 0 points along Blender +X, which is engine +X. A
        /// quarter turn takes it to authored +Y (north), which is engine -Z. The engine
        /// heading is therefore 90 degrees ahead of the authored one.
        /// </summary>
        public static float YawFromEngineDirection(float engineX, float engineZ) =>
            MathUtil.WrapAngle(MathUtil.Atan2Degrees(engineX, engineZ) - 90f);
    }
}
