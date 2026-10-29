using Kordon.Core.Maps;
using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The authored map and the engine disagree about which axis is up, and the whole
    /// level is placed through one conversion.  It used to be a mirror, which turned
    /// every asymmetric module the wrong way; these tests pin the rotation down.
    /// </summary>
    public sealed class MapOrientationTests
    {
        [Fact]
        public void UpBecomesUpAndFrontBecomesFront()
        {
            // Blender up (+Z) is Unity up (+Y).
            KordonVec3 up = MapOrientation.ToEngine(new KordonVec3(0f, 0f, 1f));
            Assert.Equal(0f, up.X, 4);
            Assert.Equal(1f, up.Y, 4);
            Assert.Equal(0f, up.Z, 4);

            // Blender front (-Y) is Unity front (+Z): the operators and the weapons are
            // authored facing -Y, and they must face the camera side in the engine.
            KordonVec3 front = MapOrientation.ToEngine(new KordonVec3(0f, -1f, 0f));
            Assert.Equal(0f, front.X, 4);
            Assert.Equal(0f, front.Y, 4);
            Assert.Equal(1f, front.Z, 4);

            // Blender +X stays +X.
            KordonVec3 right = MapOrientation.ToEngine(new KordonVec3(1f, 0f, 0f));
            Assert.Equal(1f, right.X, 4);
        }

        [Fact]
        public void TheConversionIsARotationNotAMirror()
        {
            // A reflection would flip the handedness of a basis. Take two axes and their
            // cross product: a rotation preserves it, a mirror reverses it.
            KordonVec3 a = MapOrientation.ToEngine(new KordonVec3(1f, 0f, 0f));
            KordonVec3 b = MapOrientation.ToEngine(new KordonVec3(0f, 1f, 0f));
            KordonVec3 cross = new KordonVec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
            // x cross y is +z in Blender, and +z maps to +y in the engine.
            Assert.Equal(1f, cross.Y, 4);
            Assert.Equal(0f, cross.Z, 4);
        }

        [Fact]
        public void RoundTripReturnsTheAuthoredPoint()
        {
            KordonVec3 authored = new KordonVec3(12.5f, -31.25f, 7.75f);
            KordonVec3 engine = MapOrientation.ToEngine(authored);
            KordonVec3 back = MapOrientation.ToAuthored(engine);
            Assert.Equal(authored.X, back.X, 4);
            Assert.Equal(authored.Y, back.Y, 4);
            Assert.Equal(authored.Z, back.Z, 4);
        }

        [Fact]
        public void YawKeepsItsSignAndItsValue()
        {
            // A quarter turn about Blender up is a quarter turn about engine up, and the
            // heading reported back is the authored heading, not its mirror.
            Assert.Equal(90f, MapOrientation.YawToEngine(90f), 3);
            Assert.Equal(-45f, MapOrientation.YawToEngine(-45f), 3);

            // Authored yaw 0 points along Blender +X, which is engine +X.
            Assert.Equal(0f, MapOrientation.YawFromEngineDirection(1f, 0f), 3);
            // Authored yaw 90 points north (Blender +Y), which is engine -Z.
            Assert.Equal(90f, MapOrientation.YawFromEngineDirection(0f, -1f), 3);
            // ...and a quarter turn the other way is engine +Z.
            Assert.Equal(-90f, MapOrientation.YawFromEngineDirection(0f, 1f), 3);
        }
    }
}
