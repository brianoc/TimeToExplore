using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// The detail gates that decide how a body is drawn. A body has two
    /// regimes: far away it is a marker — the flat catalogue colour, with no
    /// texture fetch and no atmosphere shell — and once it is close enough that
    /// its disc is readable it samples its albedo map and gains a rim-glow
    /// atmosphere. Each switch is the body's <b>true angular radius</b>, before
    /// the minimum-apparent-size aid inflates a distant body into a click
    /// target: the aid keeps a far planet visible, but it must not earn it
    /// detail. A body inflated by the aid therefore stays a flat marker.
    /// <para>
    /// The albedo threshold is deliberately low. At 1600x900 with a 60-degree
    /// field of view, 0.025 rad is about a 31-pixel diameter, so the map is
    /// already loaded and sampling by the time the disc becomes legible.
    /// Raising it would make the hand-over from marker to textured sphere
    /// visible. The atmosphere threshold is higher, about a 90-pixel diameter,
    /// because the rim is a fine detail that reads as a smudge when the disc is
    /// small.
    /// </para>
    /// <para>
    /// Pure, with no Unity state, so the gates are unit-testable in edit mode.
    /// </para>
    /// </summary>
    public static class BodyAppearance
    {
        /// <summary>
        /// True angular radius, in radians, at which a body starts drawing its
        /// albedo map. Below it the body is a flat marker.
        /// </summary>
        public const float AlbedoMinAngularRadius = 0.025f;

        /// <summary>
        /// Whether a body of the given true angular radius should sample its
        /// albedo map. <paramref name="angularRadius"/> is radius over distance
        /// in the render frame, not the inflated marker radius.
        /// </summary>
        public static bool UsesAlbedo(float angularRadius) =>
            angularRadius >= AlbedoMinAngularRadius;

        /// <summary>
        /// True angular radius, in radians, at which a body with an atmosphere
        /// starts drawing its rim shell. About a 90-pixel diameter at 1600x900
        /// with a 60-degree field of view.
        /// </summary>
        public const float AtmosphereMinAngularRadius = 0.06f;

        /// <summary>
        /// The smallest shell scale the renderer will build. A guard only: a
        /// real atmosphere height always clears it by a wide margin.
        /// </summary>
        public const float MinAtmosphereScale = 1.001f;

        /// <summary>
        /// The largest shell scale the renderer will build, so a bad catalogue
        /// height cannot swallow the near field. A guard only.
        /// </summary>
        public const float MaxAtmosphereScale = 1.08f;

        /// <summary>
        /// Whether a body of the given true angular radius should draw its
        /// atmosphere shell. <paramref name="angularRadius"/> is radius over
        /// distance in the render frame, not the inflated marker radius.
        /// </summary>
        public static bool UsesAtmosphere(float angularRadius) =>
            angularRadius >= AtmosphereMinAngularRadius;

        /// <summary>
        /// The shell's scale as a multiple of the body's radius, built from the
        /// catalogue's <b>real</b> atmosphere height rather than an art-inflated
        /// one: <c>1 + heightM / bodyRadiusM</c>, both in metres. Earth's 100 km
        /// on 6,371 km gives about 1.0157. The clamp is only a guard against bad
        /// data; it is not there to make a thin atmosphere look thicker.
        /// </summary>
        public static float AtmosphereScale(double bodyRadiusM, double heightM)
        {
            if (bodyRadiusM <= 0.0 || heightM <= 0.0) return 1f;

            double scale = 1.0 + heightM / bodyRadiusM;
            return Mathf.Clamp((float)scale, MinAtmosphereScale, MaxAtmosphereScale);
        }
    }
}
