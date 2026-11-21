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
    /// thinner, which is natural perspective until the far half of an orbit or
    /// a route drops below a pixel and vanishes. <see cref="SampleWorldWidth"/>
    /// therefore gives every vertex its own width: perspective is unchanged
    /// until a vertex would fall below <see cref="DistanceFloorPixels"/>, and
    /// the floor holds it there. The <see cref="LineRenderer.widthCurve"/>
    /// carries those per-vertex widths.
    /// </summary>
    public static class LineWidth
    {
        /// <summary>
        /// Apparent width of the orbit and plan lines, in screen pixels. Three
        /// rather than one and a half: on a high-DPI display a 1.5 px world
        /// line reads as a sub-pixel thread, and the extra pixel also lets the
        /// shader's coverage feather leave a full-strength core instead of a
        /// soft, half-transparent one.
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
        /// Apparent orbit-line width in the local view, in screen pixels. It is
        /// the shared 3 px rule, unchanged.
        /// </summary>
        public const float OrbitLocalPixels = Pixels;

        /// <summary>
        /// Apparent orbit-line width in the solar system view, in screen pixels.
        /// The planets are what the map is read by, so their orbits are drawn
        /// as a bold stroke there instead of the local fine line.
        /// </summary>
        public const float OrbitMapPixels = 10f;

        /// <summary>
        /// Orbit-line width in pixels for a blend weight: 3 px in the local
        /// view, 10 px in the solar system view, linear in between. The blend
        /// uses the same weight as the camera pose, so the lines thicken as the
        /// zoom tilts toward the map rather than popping at the map threshold.
        /// The colour does not blend; it belongs to the body.
        /// </summary>
        public static float OrbitPixels(float mapBlendWeight) =>
            Mathf.Lerp(OrbitLocalPixels, OrbitMapPixels, Mathf.Clamp01(mapBlendWeight));

        /// <summary>
        /// Render-unit width that covers <paramref name="pixels"/> pixels at a
        /// view depth of <paramref name="depthUnits"/>.
        ///
        /// One pixel subtends <c>2·tan(fov/2)/screenHeight</c> of the depth, so
        /// a line of <c>p</c> pixels is that fraction of the depth times `p`.
        /// The old `0.0015 × distance` was a 60°/1080p approximation of a
        /// 1.5 px line; this form keeps the rule correct at other FOVs and
        /// resolutions and no longer needs a world-unit clamp that breaks at
        /// the map ceiling.
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
        /// still thins the line with distance, but a vertex is never given less
        /// than the width that draws <see cref="DistanceFloorPixels"/>, so the
        /// far arc stays visible instead of fading to a sub-pixel thread. The
        /// floor is capped by the line's own look-at width, so a line thinner
        /// than the floor is not inflated.
        /// </summary>
        public static float SampleWorldWidth(float lookAtWorldWidth, float lookAtPixels,
            float sampleDepthUnits, float fieldOfViewDegrees, float screenHeightPixels,
            float floorPixels = DistanceFloorPixels)
        {
            if (!(lookAtWorldWidth > 0f) || float.IsInfinity(lookAtWorldWidth))
                return Mathf.Max(lookAtWorldWidth, 0f);
            if (!(sampleDepthUnits > 0f) || float.IsInfinity(sampleDepthUnits))
                return lookAtWorldWidth;

            float floor = Mathf.Clamp(floorPixels, 0f, Mathf.Max(lookAtPixels, 0f));
            if (floor <= 0f) return lookAtWorldWidth;

            float floorWidth = WorldWidth(sampleDepthUnits, fieldOfViewDegrees,
                screenHeightPixels, floor);
            return Mathf.Max(lookAtWorldWidth, floorWidth);
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
