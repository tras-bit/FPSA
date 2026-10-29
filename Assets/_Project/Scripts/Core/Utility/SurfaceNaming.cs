using System;

namespace Kordon.Core.Utility
{
    /// <summary>
    /// How an authored surface becomes the material the game loads.
    ///
    /// The Blender kit exports every material with a <c>KRD_</c> prefix
    /// (<c>KRD_steel_rust</c>) so exported art can never be confused with an
    /// outside library. Unity appends <c> (Instance)</c> to runtime copies. The
    /// authored materials in the project are named <c>M_steel_rust</c>, so the
    /// import name has to be normalised before the lookup - getting this wrong is
    /// silent: every model keeps its untextured imported material and the map
    /// looks grey.
    /// </summary>
    public static class SurfaceNaming
    {
        /// <summary>Prefix Blender puts on every exported material.</summary>
        public const string ImportedPrefix = "KRD_";

        /// <summary>Prefix of the authored Unity materials (<c>M_steel_rust</c>).</summary>
        public const string MaterialPrefix = "M_";

        /// <summary>
        /// Imported material name to surface id: <c>KRD_steel_rust</c> and
        /// <c>KRD_steel_rust (Instance)</c> both become <c>steel_rust</c>.
        /// Returns null when there is no usable name.
        /// </summary>
        public static string FromImportedMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return null;
            }

            string surface = materialName.Trim();

            // Unity appends " (Instance)" / " (Clone)" to runtime copies.
            int space = surface.IndexOf(' ');
            if (space > 0)
            {
                surface = surface.Substring(0, space);
            }

            if (surface.StartsWith(ImportedPrefix, StringComparison.Ordinal))
            {
                surface = surface.Substring(ImportedPrefix.Length);
            }

            surface = surface.ToLowerInvariant();
            return surface.Length == 0 ? null : surface;
        }

        /// <summary>Surface id to the authored material's resource path.</summary>
        public static string ResourcePath(string surface)
        {
            if (string.IsNullOrEmpty(surface))
            {
                return null;
            }

            return "Kordon/Materials/" + MaterialPrefix + surface;
        }
    }
}
