using System;
using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>Mirrors Assets/Resources/Sky/bright_stars.json.</summary>
    [Serializable]
    public sealed class BrightStarFile
    {
        public string sourceNote;
        public string epoch;
        public float magnitudeLimit;
        public BrightStar[] stars;
    }

    /// <summary>
    /// One catalogued star: its catalogue position for a reader, and its
    /// direction in the game's spun ecliptic frame for the renderer.
    /// </summary>
    [Serializable]
    public sealed class BrightStar
    {
        public int hr;
        public string bayer;
        public float ra;
        public float dec;
        public float magnitude;
        public float bv;
        public float x;
        public float y;
        public float z;
    }

    /// <summary>
    /// Turns the bright-star catalogue into the lookup texture the skybox
    /// shader reads, so the real stars — and with them the constellations —
    /// sit where they belong instead of being another random field.
    ///
    /// The texture is one texel per sky cell, in the same equirectangular
    /// mapping as the Milky Way band. A texel carries
    ///
    /// * red and green: where the star sits inside its cell, as a fraction;
    /// * blue: its V magnitude rebased so 0 is an empty cell and 1 is the
    ///   faintest catalogued star;
    /// * alpha: its B-V colour index, normalised over the catalogue's range.
    ///
    /// The red and green channels are fractions rather than absolute positions
    /// on purpose: an absolute longitude at fifteen bits would quantise to a
    /// third of a screen pixel, while a fraction of a cell lands within a
    /// hundredth of one.
    ///
    /// The format is half float rather than RGBA32 because these channels are
    /// data, not colour: a float texture is never colour-managed or resampled
    /// into eight bits, so a magnitude and a sub-cell offset arrive at the
    /// shader exactly as written, whatever colour space the project renders in.
    ///
    /// The shader tests the nine cells around the pixel. Anything further away
    /// cannot be within a star's radius, because the widest star is under half
    /// a cell across.
    /// </summary>
    public static class StarCatalogue
    {
        /// <summary>Resources path of the committed catalogue.</summary>
        public const string Resource = "Sky/bright_stars";

        /// <summary>Cells around the sky. A cell is 0.35 degrees.</summary>
        public const int Columns = 1024;

        /// <summary>Cell rows, half the columns as in the band's mapping.</summary>
        public const int Rows = 512;

        /// <summary>The magnitude limit the generator used; blue channel 0.</summary>
        public const float MagnitudeLimit = 4f;

        /// <summary>
        /// Magnitudes the blue channel spans, from <see cref="MagnitudeLimit"/>
        /// up to brightness 1. The catalogue's brightest entry is Sirius at
        /// -1.46, so this covers it with a little headroom.
        /// </summary>
        public const float MagnitudeSpan = 6f;

        /// <summary>
        /// B-V range the alpha channel spans, matching the generator's `warmth`
        /// window: blue-white at -0.4, amber at 2.0.
        /// </summary>
        public const float BvMinimum = -0.4f;

        /// <summary>Width of the B-V range in <see cref="BvMinimum"/>'s scale.</summary>
        public const float BvSpan = 2.4f;

        /// <summary>Parse the committed catalogue; null text is an empty sky.</summary>
        public static BrightStarFile Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new BrightStarFile();
            return JsonUtility.FromJson<BrightStarFile>(json) ?? new BrightStarFile();
        }

        /// <summary>
        /// Build the lookup texture. An empty or missing catalogue gives a black
        /// map, which the shader reads as a sky without catalogue stars.
        /// </summary>
        public static Texture2D Build(BrightStarFile file)
        {
            var texture = new Texture2D(Columns, Rows, TextureFormat.RGBAHalf, false, true)
            {
                name = "Bright star map",
                filterMode = FilterMode.Point,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };

            var pixels = new Color[Columns * Rows];
            BrightStar[] stars = file?.stars;
            if (stars != null)
            {
                foreach (BrightStar star in stars)
                {
                    Vector3 direction = new Vector3(star.x, star.y, star.z);
                    if (direction.sqrMagnitude < 1e-8f) continue;

                    Vector2 uv = SkyUv(direction);
                    int column = Mathf.Clamp(Mathf.FloorToInt(uv.x * Columns), 0, Columns - 1);
                    int row = Mathf.Clamp(Mathf.FloorToInt(uv.y * Rows), 0, Rows - 1);
                    int cell = row * Columns + column;

                    float brightness = BrightnessCode(star.magnitude);
                    // Two catalogue entries can share a cell; the brighter one
                    // wins, so the map stays deterministic whatever the order.
                    if (pixels[cell].b >= brightness) continue;

                    float fractionalU = Mathf.Clamp01(uv.x * Columns - column);
                    float fractionalV = Mathf.Clamp01(uv.y * Rows - row);
                    pixels[cell] = new Color(
                        fractionalU,
                        fractionalV,
                        brightness,
                        ColourCode(star.bv));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false);
            return texture;
        }

        /// <summary>
        /// Direction to the equirectangular uv the band and the shader share:
        /// <c>u = atan2(z, x) / 2pi + 0.5</c>, <c>v = asin(y) / pi + 0.5</c>.
        /// </summary>
        public static Vector2 SkyUv(Vector3 direction)
        {
            Vector3 d = direction.normalized;
            float u = Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) + 0.5f;
            float v = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f;
            return new Vector2(u, v);
        }

        /// <summary>
        /// Magnitude to the blue channel, 0..1. A catalogued star never reaches
        /// zero, because zero is reserved for an empty cell.
        /// </summary>
        public static float BrightnessCode(float magnitude)
        {
            return Mathf.Clamp((MagnitudeLimit - magnitude) / MagnitudeSpan, 1f / 255f, 1f);
        }

        /// <summary>B-V index to the alpha channel, 0..1.</summary>
        public static float ColourCode(float bv)
        {
            return Mathf.Clamp01((bv - BvMinimum) / BvSpan);
        }
    }
}
