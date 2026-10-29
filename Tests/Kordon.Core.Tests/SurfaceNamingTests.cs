using Kordon.Core.Utility;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The material contract between the Blender kit and the Unity materials.
    /// This is not cosmetic: if the imported name is not normalised, every model
    /// silently keeps its untextured imported material and the whole map renders
    /// flat grey, with nothing in the console to say why.
    /// </summary>
    public sealed class SurfaceNamingTests
    {
        [Fact]
        public void BlenderPrefixIsRemoved()
        {
            Assert.Equal("steel_rust", SurfaceNaming.FromImportedMaterial("KRD_steel_rust"));
            Assert.Equal("concrete_raw", SurfaceNaming.FromImportedMaterial("KRD_concrete_raw"));
        }

        [Fact]
        public void UnityInstanceSuffixIsRemoved()
        {
            Assert.Equal("steel_rust", SurfaceNaming.FromImportedMaterial("KRD_steel_rust (Instance)"));
            Assert.Equal("brick", SurfaceNaming.FromImportedMaterial("KRD_brick (Clone)"));
        }

        [Fact]
        public void BareSurfaceNamePassesThrough()
        {
            Assert.Equal("gravel", SurfaceNaming.FromImportedMaterial("gravel"));
            Assert.Equal("gravel", SurfaceNaming.FromImportedMaterial("  gravel  "));
        }

        [Fact]
        public void NamesAreCaseInsensitive()
        {
            Assert.Equal("steel_rust", SurfaceNaming.FromImportedMaterial("KRD_Steel_Rust"));
            Assert.Equal("steel_rust", SurfaceNaming.FromImportedMaterial("STEEL_RUST"));
        }

        [Fact]
        public void EmptyAndNullNamesHaveNoSurface()
        {
            Assert.Null(SurfaceNaming.FromImportedMaterial(null));
            Assert.Null(SurfaceNaming.FromImportedMaterial(""));
            Assert.Null(SurfaceNaming.FromImportedMaterial("   "));
            Assert.Null(SurfaceNaming.FromImportedMaterial("KRD_"));
        }

        [Fact]
        public void ResourcePathMatchesAuthoredMaterialNames()
        {
            Assert.Equal("Kordon/Materials/M_steel_rust",
                SurfaceNaming.ResourcePath("steel_rust"));
            Assert.Null(SurfaceNaming.ResourcePath(null));
            Assert.Null(SurfaceNaming.ResourcePath(""));
        }
    }
}
