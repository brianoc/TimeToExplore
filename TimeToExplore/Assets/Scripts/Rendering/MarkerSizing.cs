using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// The minimum-apparent-size aid's pixel rule: every body is drawn at
    /// least 4.5 pixels across, so a distant planet stays a click target and a
    /// label anchor. True scale (`T`) bypasses it for inspection.
    ///
    /// Pure C# so the rule and its angular conversion are tested without a
    /// camera or a scene.
    /// </summary>
    public static class MarkerSizing
    {
        /// <summary>The minimum marker radius at every zoom, in pixels.</summary>
        public const float MinimumPixels = 4.5f;

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
