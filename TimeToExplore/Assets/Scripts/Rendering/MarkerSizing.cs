using System;
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
        /// Distance multiple at which the local marker aid has faded to true
        /// scale. It matches the map pose's factor-of-3 blend band, so a body
        /// and the camera hand over to the map over the same neighbourhood.
        /// </summary>
        public const double NeighbourhoodFadeRatio = 3.0;

        /// <summary>
        /// Marker radius in pixels for a blend weight: 4.5 px local, 6 px map,
        /// linear in between.
        /// </summary>
        public static float MinimumPixels(float mapBlendWeight) =>
            Mathf.Lerp(LocalMinimumPixels, MapMinimumPixels, Mathf.Clamp01(mapBlendWeight));

        /// <summary>
        /// How much of the minimum-apparent-size aid a body receives. The local
        /// view only aids its neighbourhood: full strength at the local scale,
        /// easing to true scale by <see cref="NeighbourhoodFadeRatio"/>, so a
        /// planet an AU away is the sub-pixel dot it physically is. The map
        /// restores the aid in full, because finding distant bodies is the
        /// map's job.
        ///
        /// The neighbourhood is the larger of the dominant body's SOI radius
        /// and the camera distance: zooming out widens what counts as local, so
        /// the aid does not vanish while the local camera still frames a wide
        /// view. <paramref name="localRadiusM"/> at or below zero, or infinite,
        /// disables the fade and returns full aid, which keeps callers that do
        /// not know the dominant body on the old behaviour.
        ///
        /// Pure, like the rest of the aid, so the falloff is tested without a
        /// camera or a scene.
        /// </summary>
        /// <param name="cameraDistanceM">Camera distance from the ship, in metres.</param>
        /// <param name="bodyDistanceM">Body distance from the ship, in metres.</param>
        /// <param name="localRadiusM">Dominant body SOI radius, in metres.</param>
        /// <param name="mapBlendWeight">0 local, 1 map.</param>
        public static float AidStrength(double cameraDistanceM, double bodyDistanceM,
            double localRadiusM, float mapBlendWeight)
        {
            float mapWeight = Mathf.Clamp01(mapBlendWeight);
            if (mapWeight >= 1f) return 1f;

            if (!(localRadiusM > 0.0) || double.IsInfinity(localRadiusM)) return 1f;
            if (double.IsNaN(bodyDistanceM) || bodyDistanceM <= 0.0) return 1f;

            double cameraM = double.IsFinite(cameraDistanceM) && cameraDistanceM > 0.0
                ? cameraDistanceM
                : 0.0;
            double neighbourhoodM = Math.Max(localRadiusM, cameraM);
            if (!(neighbourhoodM > 0.0)) return 1f;

            double ratio = bodyDistanceM / neighbourhoodM;

            // The same log-distance band and smoothstep as MapView's blend, with
            // the aid on the inside instead of the map. Exact at the endpoints
            // so a body at the SOI edge is not nudged by floating-point noise.
            double x = ratio <= 1.0
                ? 0.0
                : ratio >= NeighbourhoodFadeRatio
                    ? 1.0
                    : Math.Log(ratio) / Math.Log(NeighbourhoodFadeRatio);

            float localStrength = (float)(1.0 - x * x * (3.0 - 2.0 * x));
            return Mathf.Lerp(localStrength, 1f, mapWeight);
        }

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
