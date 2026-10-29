using System.Collections.Generic;
using UnityEngine;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// The HUD's small icons, generated at runtime instead of loaded from disk.
    ///
    /// The first HUD drew a magazine as a 44 x 15 px coloured bar, which reads as a
    /// progress bar, not as a magazine. These are actual silhouettes - a curved box
    /// magazine, a round grenade, a rounded panel - rasterised once into small
    /// textures when first asked for and cached, so the match pays nothing after the
    /// first frame and the project ships no extra image files.
    ///
    /// Colours come from the caller (UiBuilder.MagazineColor for the pocket states),
    /// so the white full / yellow partial / red empty rule stays in one place.
    /// </summary>
    internal static class HudIcons
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        private static Sprite Cached(string key, System.Func<Sprite> build)
        {
            if (Cache.TryGetValue(key, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }

            sprite = build();
            Cache[key] = sprite;
            return sprite;
        }

        private static Sprite ToSprite(Texture2D texture, Vector4 border)
        {
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
        }

        private static Texture2D NewTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        /// <summary>
        /// Nine sliced rounded panel: a 1 px border around a fill, used for the loadout
        /// card, the key chips and the active row highlight so they stretch cleanly.
        /// </summary>
        public static Sprite Rounded(Color fill, Color border)
        {
            string key = "round:" + Key(fill) + ":" + Key(border);
            return Cached(key, () =>
            {
                const int size = 48;
                const float radius = 11f;
                Texture2D texture = NewTexture(size, size);
                Color[] pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distance = RoundedDistance(x + 0.5f, y + 0.5f, size, size, radius);
                        if (distance > 1.4f)
                        {
                            pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                            continue;
                        }

                        Color colour = distance > -0.6f ? border : fill;
                        colour.a *= Mathf.Clamp01(1.4f - Mathf.Abs(distance));
                        pixels[y * size + x] = colour;
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply();
                return ToSprite(texture, new Vector4(14f, 14f, 14f, 14f));
            });
        }

        /// <summary>
        /// A curved box magazine seen flat: body, three ribs, a floor plate and the feed
        /// lips.  A chambered magazine carries a bright round at the top, which is what
        /// the old "|" character was trying to say.
        /// </summary>
        public static Sprite Magazine(Color body, bool chambered)
        {
            string key = "mag:" + Key(body) + (chambered ? ":chambered" : ":plain");
            return Cached(key, () =>
            {
                const int width = 26;
                const int height = 46;
                Texture2D texture = NewTexture(width, height);
                Color[] pixels = new Color[width * height];
                Color dark = Shade(body, 0.62f);
                Color rib = Shade(body, 0.84f);

                for (int y = 0; y < height; y++)
                {
                    // Unity textures are bottom up: y = 0 is the floor plate, the top of
                    // the texture is the feed end of the magazine.
                    float t = Mathf.Clamp01((y - 3f) / (height - 7f));
                    float lean = Mathf.Pow(1f - t, 2f) * 4.5f;
                    float halfWidth = Mathf.Lerp(8.6f, 10f, t);
                    float centre = width * 0.5f + lean * 0.6f;
                    float topRound = y > height - 6
                        ? (y - (height - 6)) / 3f * 1.6f
                        : 0f;

                    for (int x = 0; x < width; x++)
                    {
                        float dx = Mathf.Abs(x + 0.5f - centre);
                        bool inside = y >= 3 && y <= height - 3 && dx <= halfWidth - topRound;
                        if (!inside)
                        {
                            pixels[y * width + x] = new Color(0f, 0f, 0f, 0f);
                            continue;
                        }

                        Color colour = body;
                        if (y <= 5)
                        {
                            colour = dark; // floor plate
                        }
                        else if (y >= height - 7)
                        {
                            colour = dark; // feed lips
                        }
                        else if (Mathf.Abs(y - 15f) < 1.1f || Mathf.Abs(y - 24f) < 1.1f ||
                                 Mathf.Abs(y - 33f) < 1.1f)
                        {
                            colour = rib;
                        }

                        colour.a = 1f;
                        pixels[y * width + x] = colour;
                    }
                }

                if (chambered)
                {
                    Color round = new Color(1f, 0.87f, 0.62f, 1f);
                    int centreX = width / 2;
                    for (int y = height - 6; y <= height - 3; y++)
                    {
                        for (int x = centreX - 2; x <= centreX + 1; x++)
                        {
                            pixels[y * width + x] = round;
                        }
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply();
                return ToSprite(texture, Vector4.zero);
            });
        }

        /// <summary>A round icon with a ring: the grenade, and any other disc marker.</summary>
        public static Sprite Disc(Color fill, Color ring)
        {
            string key = "disc:" + Key(fill) + ":" + Key(ring);
            return Cached(key, () =>
            {
                const int size = 32;
                Texture2D texture = NewTexture(size, size);
                Color[] pixels = new Color[size * size];
                float centre = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - centre;
                        float dy = y + 0.5f - centre;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float outer = size * 0.5f - 2f;
                        if (distance > outer + 1f)
                        {
                            pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                            continue;
                        }

                        Color colour = distance > outer - 3f ? ring : fill;
                        colour.a = Mathf.Clamp01(outer + 1f - distance);
                        pixels[y * size + x] = colour;
                    }
                }

                texture.SetPixels(pixels);
                texture.Apply();
                return ToSprite(texture, Vector4.zero);
            });
        }

        private static float RoundedDistance(float x, float y, int width, int height, float radius)
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float dx = Mathf.Abs(x - halfWidth) - (halfWidth - radius - 0.5f);
            float dy = Mathf.Abs(y - halfHeight) - (halfHeight - radius - 0.5f);
            float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                       Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
            float inside = Mathf.Min(Mathf.Max(dx, dy), 0f);
            return outside + inside - radius;
        }

        private static Color Shade(Color colour, float factor) =>
            new Color(colour.r * factor, colour.g * factor, colour.b * factor, colour.a);

        private static string Key(Color colour) =>
            Mathf.RoundToInt(colour.r * 255f) + "," + Mathf.RoundToInt(colour.g * 255f) + "," +
            Mathf.RoundToInt(colour.b * 255f) + "," + Mathf.RoundToInt(colour.a * 255f);
    }
}
