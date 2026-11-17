using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// One orbit colour per planet, sampled from a single warm-to-cool ramp:
    /// Mercury is a warm orange and Neptune a cool purple, and the six planets
    /// between them step through the reds and magentas that join the two ends.
    /// The hue travels the short way round the wheel (orange → red → magenta →
    /// violet), not the long way through green and cyan, so every orbit stays in
    /// the requested warm-to-cool family.
    ///
    /// The planet's position in the ramp comes from its rank in orbital order,
    /// not from its semi-major axis: a linear radius would bunch Mercury to
    /// Mars into the first two percent of the ramp, because Jupiter and Neptune
    /// are tens of times further out. Even spacing gives all eight planets a
    /// colour a player can tell apart.
    ///
    /// The colour is a property of the body, not of the view: the local view and
    /// the solar system view draw the same hue at the same alpha. Only the width
    /// blends with the zoom (see <see cref="LineWidth.OrbitPixels"/>).
    ///
    /// Pure C#, so the ramp and the ordering are tested without a scene.
    /// </summary>
    public static class OrbitColours
    {
        /// <summary>Mercury's hue: a warm orange.</summary>
        public const float MercuryHueDegrees = 30f;

        /// <summary>Neptune's hue: a cool violet-purple.</summary>
        public const float NeptuneHueDegrees = 275f;

        /// <summary>Mercury's saturation and value: bright and warm.</summary>
        public const float MercurySaturation = 0.74f;
        public const float MercuryValue = 1f;

        /// <summary>Neptune's saturation and value: softer and deeper.</summary>
        public const float NeptuneSaturation = 0.58f;
        public const float NeptuneValue = 0.94f;

        /// <summary>
        /// Opacity shared by every planet orbit in both views. Half is enough
        /// for a 3 px local line to read over the sky and lets the 10 px map
        /// line layer without washing out the markers behind it.
        /// </summary>
        public const float PlanetAlpha = 0.5f;

        /// <summary>
        /// The ramp at <paramref name="t"/>: 0 is Mercury's warm orange, 1 is
        /// Neptune's cool purple. Values outside 0..1 clamp to the ends.
        /// </summary>
        public static Color Planet(float t)
        {
            t = Mathf.Clamp01(t);

            // Lerp toward Neptune's hue minus one turn so the path runs down
            // through the reds rather than up through yellow, green, and cyan.
            float hue = Mathf.Lerp(MercuryHueDegrees, NeptuneHueDegrees - 360f, t);
            float saturation = Mathf.Lerp(MercurySaturation, NeptuneSaturation, t);
            float value = Mathf.Lerp(MercuryValue, NeptuneValue, t);

            Color colour = Color.HSVToRGB(Mathf.Repeat(hue, 360f) / 360f, saturation, value);
            colour.a = PlanetAlpha;
            return colour;
        }

        /// <summary>
        /// The ramp sample for one planet of <paramref name="count"/> in orbital
        /// order. A single planet takes the warm end rather than dividing by
        /// zero.
        /// </summary>
        public static Color Planet(int index, int count) =>
            Planet(count <= 1 ? 0f : index / (float)(count - 1));

        /// <summary>
        /// Map every orbiting planet in <paramref name="bodies"/> to its ramp
        /// colour, assigned from Mercury outward. Moons, stations, asteroids,
        /// and the Sun are ignored. The sort is by semi-major axis rather than
        /// catalogue order, so the ramp survives a reordered catalogue.
        /// </summary>
        public static Dictionary<string, Color> ForPlanets(IEnumerable<BodyRuntime> bodies)
        {
            List<BodyRuntime> planets = new List<BodyRuntime>();

            if (bodies != null)
            {
                foreach (BodyRuntime body in bodies)
                {
                    if (body == null || !body.HasOrbit || body.Kind != "planet") continue;
                    planets.Add(body);
                }
            }

            planets.Sort((a, b) =>
                a.Elements.semiMajorAxisM.CompareTo(b.Elements.semiMajorAxisM));

            Dictionary<string, Color> colours = new Dictionary<string, Color>(planets.Count);
            for (int i = 0; i < planets.Count; i++)
                colours[planets[i].Id] = Planet(i, planets.Count);

            return colours;
        }
    }
}
