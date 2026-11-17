using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// The minimum-apparent-size aid's pixel rule. The local view keeps the
    /// original 4.5 px minimum; the map raises it to 6 px so every planet and
    /// station is a comfortable click target. The change follows the map blend
    /// weight, so the marker grows smoothly as the view enters the map band
    /// rather than popping at a threshold.
    ///
    /// Pure C# so the rule and its angular conversion are tested without a
    /// camera or a scene.
    /// </summary>
    public static class MarkerSizing
    {
        /// <summary>The local view's minimum marker radius, in pixels.</summary>
        public const float LocalMinimumPixels = 4.5f;

        /// <summary>The map's minimum marker radius, in pixels.</summary>
        public const float MapMinimumPixels = 6f;

        /// <summary>
        /// Marker radius in pixels for a blend weight: 4.5 px local, 6 px map,
        /// linear in between.
        /// </summary>
        public static float MinimumPixels(float mapBlendWeight) =>
            Mathf.Lerp(LocalMinimumPixels, MapMinimumPixels, Mathf.Clamp01(mapBlendWeight));

        /// <summary>
        /// Angular radius that one marker radius covers, given the camera's
        /// vertical field of view and the screen height. This is the value
        /// <see cref="BodyView"/> compares against a body's angular radius.
        /// </summary>
        public static float MinimumAngularSize(float minimumPixels, float fieldOfViewDegrees,
            int screenHeightPixels) =>
            fieldOfViewDegrees * Mathf.Deg2Rad * minimumPixels / Mathf.Max(1, screenHeightPixels);
    }
}
