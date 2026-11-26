namespace TimeToExplore.Rendering
{
    /// <summary>
    /// The detail gate that decides how a body is drawn. A body has two
    /// regimes: far away it is a marker — the flat catalogue colour, with no
    /// texture fetch — and once it is close enough that its disc is readable it
    /// samples its albedo map. The switch is the body's <b>true angular
    /// radius</b>, before the minimum-apparent-size aid inflates a distant body
    /// into a click target: the aid keeps a far planet visible, but it must not
    /// earn it a texture. A body inflated by the aid therefore stays a flat
    /// marker.
    /// <para>
    /// The threshold is deliberately low. At 1600x900 with a 60-degree field of
    /// view, 0.025 rad is about a 31-pixel diameter, so the map is already
    /// loaded and sampling by the time the disc becomes legible. Raising it
    /// would make the hand-over from marker to textured sphere visible.
    /// </para>
    /// <para>
    /// Pure, with no Unity state, so the gate is unit-testable in edit mode.
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
    }
}
