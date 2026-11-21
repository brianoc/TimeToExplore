using UnityEngine;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// One apparent-width rule for every world-space line in the scene. A line
    /// is specified in screen pixels and converted to render units at its view
    /// depth, so it keeps the same thickness at any zoom, resolution, or field
    /// of view. That is the property the old orbit-sized local rule lacked: it
    /// was only correct at the one distance where the whole orbit was framed,
    /// and turned a planet's orbit into a ribbon as soon as the camera moved
    /// inside it.
    ///
    /// The base width is in render units at <em>one</em> depth. Used alone, that
    /// makes geometry nearer the camera thicker and geometry farther away
    /// thinner. Natural perspective is right for a gently sloping orbit, but it
    /// breaks at both ends: the far half of an orbit or a route drops below a
    /// pixel and vanishes, while a route that passes close to the camera grows
    /// into a screen-filling ribbon. <see cref="SampleWorldWidth"/> therefore
    /// gives every vertex its own width and bounds the apparent result: no
    /// vertex draws thinner than <see cref="DistanceFloorPixels"/> or thicker
    /// than the line's declared pixel width. The
    /// <see cref="LineRenderer.widthCurve"/> carries those per-vertex widths.
    /// </summary>
    public static class LineWidth
    {
        /// <summary>
        /// Apparent line width in screen pixels, shared by orbit and plan
        /// lines. Three rather than one and a half: on a high-DPI display a
        /// 1.5 px world line reads as a sub-pixel thread, and the extra pixel
        /// also lets the shader's coverage feather leave a full-strength core
        /// instead of a soft, half-transparent one.
        /// </summary>
        public const float Pixels = 3f;

        /// <summary>
        /// Apparent width a distant line holds, in screen pixels. Perspective
        /// still thins a line until it reaches this width; below it the line
        /// would start to disappear, so the floor keeps the far half of an
        /// orbit or a route readable without turning it into a bold stroke.
        /// </summary>
        public const float DistanceFloorPixels = 2f;

        /// <summary>
        /// Render-unit width that covers <paramref name="pixels"/> pixels at a
        /// view depth of <paramref name="depthUnits"/>.
        ///
        /// One pixel subtends <c>2·tan(fov/2)/screenHeight</c> of the depth, so
        /// a line of <c>p</c> pixels is that fraction of the depth times `p`.
        /// The old `0.0015 × distance` was a 60°/1080p approximation of a
        /// 1.5 px line; this form keeps the rule correct at other FOVs and
        /// resolutions and no longer needs a world-unit clamp that breaks at
        /// the outer zoom ceiling.
        /// </summary>
        public static float WorldWidth(float depthUnits, float fieldOfViewDegrees,
            float screenHeightPixels, float pixels = Pixels)
        {
            if (!(depthUnits > 0f) || float.IsInfinity(depthUnits)) depthUnits = 0f;
            if (!(pixels > 0f) || float.IsInfinity(pixels)) pixels = 0f;

            float halfHeightFactor = Mathf.Tan(0.5f * fieldOfViewDegrees * Mathf.Deg2Rad);
            float screenHeight = Mathf.Max(1f, screenHeightPixels);
            return 2f * depthUnits * halfHeightFactor / screenHeight * pixels;
        }

        /// <summary>
        /// Width for a camera and the depth of the line. A missing camera uses
        /// the scene's 60° FOV; the viewport height still comes from the screen
        /// so the rule stays resolution-independent.
        /// </summary>
        public static float WorldWidth(Camera camera, float depthUnits)
        {
            float fieldOfView = camera != null ? camera.fieldOfView : 60f;
            return WorldWidth(depthUnits, fieldOfView, Screen.height);
        }

        /// <summary>
        /// Per-vertex width for a line whose width at the look-at depth is
        /// <paramref name="lookAtWorldWidth"/> (which draws
        /// <paramref name="lookAtPixels"/> screen pixels there). Perspective
        /// still narrows the line with distance, but the apparent width is
        /// bounded at both ends.
        ///
        /// * A vertex is never given less than the width that draws
        ///   <see cref="DistanceFloorPixels"/>, so the far arc stays visible
        ///   instead of fading to a sub-pixel thread.
        /// * A vertex is never given more than the width that draws
        ///   <paramref name="lookAtPixels"/> at its own depth, so a route that
        ///   passes close to the camera cannot turn into a screen-filling
        ///   ribbon. Without that ceiling, the old constant world width drew
        ///   the near end at <c>lookAtPixels × lookAtDepth / depth</c>, which is
        ///   unbounded as the depth goes to zero.
        ///
        /// The floor is capped by the line's own look-at width, so a line
        /// thinner than the floor is neither inflated nor thickened.
        /// </summary>
        public static float SampleWorldWidth(float lookAtWorldWidth, float lookAtPixels,
            float sampleDepthUnits, float fieldOfViewDegrees, float screenHeightPixels,
            float floorPixels = DistanceFloorPixels)
        {
            if (!(lookAtWorldWidth > 0f) || float.IsInfinity(lookAtWorldWidth))
                return Mathf.Max(lookAtWorldWidth, 0f);
            if (!(lookAtPixels > 0f) || float.IsInfinity(lookAtPixels))
                return lookAtWorldWidth;

            // A vertex at or behind the camera has no well-defined apparent
            // width. Returning the look-at width here used to draw it at the
            // full world size, which is exactly the ribbon this rule exists to
            // prevent; a zero width tapers the line at the camera plane.
            if (!(sampleDepthUnits > 0f) || float.IsInfinity(sampleDepthUnits))
                return 0f;

            float floor = Mathf.Clamp(floorPixels, 0f, lookAtPixels);
            float pixels = PixelsAt(lookAtWorldWidth, sampleDepthUnits,
                fieldOfViewDegrees, screenHeightPixels);
            pixels = Mathf.Clamp(pixels, floor, lookAtPixels);
            return WorldWidth(sampleDepthUnits, fieldOfViewDegrees, screenHeightPixels, pixels);
        }

        /// <summary>
        /// The apparent width in pixels of a world width at a depth. Used by
        /// tests and by the HUD to express the rule in its own terms.
        /// </summary>
        public static float PixelsAt(float worldWidth, float depthUnits,
            float fieldOfViewDegrees, float screenHeightPixels)
        {
            float halfHeightFactor = Mathf.Tan(0.5f * fieldOfViewDegrees * Mathf.Deg2Rad);
            float screenHeight = Mathf.Max(1f, screenHeightPixels);
            float depth = Mathf.Max(depthUnits, 1e-6f);
            return worldWidth * screenHeight / (2f * depth * halfHeightFactor);
        }
    }
}
