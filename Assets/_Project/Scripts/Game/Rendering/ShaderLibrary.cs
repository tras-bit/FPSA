using UnityEngine;

namespace Kordon.Game.Rendering
{
    /// <summary>
    /// Shader lookup that still works in a player build.
    ///
    /// <see cref="Shader.Find"/> only sees shaders that made it into the build. A
    /// shader that no material asset references is stripped, so in a player it
    /// comes back null - which for this game would silently drop the bodycam pass
    /// and the grenade effects, the two things that must never be missing. Every
    /// Kordon shader therefore lives under a Resources folder
    /// (<c>Assets/_Project/Art/Resources/Kordon/Shaders</c>) and is loaded by path
    /// first, with <see cref="Shader.Find"/> kept only as the editor-side fallback.
    /// </summary>
    public static class ShaderLibrary
    {
        public const string BodycamResource = "Kordon/Shaders/KordonBodycam";
        public const string BodycamName = "Hidden/Kordon/Bodycam";
        public const string GrenadeFxResource = "Kordon/Shaders/KordonGrenadeFx";
        public const string GrenadeFxName = "Kordon/GrenadeFx";

        /// <summary>Bodycam post process (barrel warp, chroma, grain, vignette).</summary>
        public static Shader Bodycam()
        {
            return Load(BodycamResource, BodycamName);
        }

        /// <summary>Grenade detonation billboards: flash, smoke, sparks.</summary>
        public static Shader GrenadeFx()
        {
            return Load(GrenadeFxResource, GrenadeFxName);
        }

        /// <summary>Resources first (build safe), then the in-editor registry.</summary>
        public static Shader Load(string resourcePath, string shaderName)
        {
            Shader shader = Resources.Load<Shader>(resourcePath);
            if (shader == null)
            {
                shader = Shader.Find(shaderName);
            }

            if (shader == null)
            {
                Debug.LogWarning("[kordon] shader not found: " + shaderName +
                                 " (expected Resources/" + resourcePath + ")");
            }

            return shader;
        }
    }
}
