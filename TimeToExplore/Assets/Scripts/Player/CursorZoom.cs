using System;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The camera's off-centre look-at point: a wheel notch anchors the point
    /// under the pointer on the plane through the look-at point, and the rig
    /// solves the look-at from that anchor every frame so the point stays under
    /// the pointer through the eased zoom.
    ///
    /// Plain C# with no scene dependency, so the projection and the anchor
    /// round trip are verified in EditMode. The anchor is held in
    /// ship-relative render units, exactly like the camera offset, and the
    /// result is capped so a corner anchor at the outer zoom cannot lose the
    /// ship by an unbounded amount.
    /// </summary>
    public static class CursorZoom
    {
        /// <summary>One astronomical unit in metres (IAU 2012 definition).</summary>
        public const double AstronomicalUnitM = 1.495978707e11;

        /// <summary>
        /// How far the look-at point may move from the ship: 60 AU. The zoom
        /// ceiling is 80 AU, so the cap keeps a corner anchor meaningful while
        /// bounding the render-space offset. `Space` is the explicit way home.
        /// </summary>
        public const double MaxLookAtOffsetM = 60.0 * AstronomicalUnitM;

        /// <summary>
        /// Normalised device coordinates for a pointer, with −1 to 1 spanning
        /// the screen. A screen with no extent maps to the centre, so a batch
        /// run without a view cannot produce a NaN.
        /// </summary>
        public static Vector2 PointerNdc(Vector2 pointerPixels, Vector2 screenPixels)
        {
            if (!(screenPixels.x > 0f) || !(screenPixels.y > 0f)) return Vector2.zero;

            return new Vector2(
                2f * pointerPixels.x / screenPixels.x - 1f,
                2f * pointerPixels.y / screenPixels.y - 1f);
        }

        /// <summary>
        /// The point under a screen position on the plane through the look-at
        /// point, perpendicular to the view, in render units relative to the
        /// ship. This is the pivot a cursor-anchored zoom pins.
        /// </summary>
        public static Vector3 CursorPointOnLookAtPlane(Vector3 lookAtUnits,
            Quaternion rotation, float distanceUnits, Vector2 pointerNdc,
            float fieldOfViewDegrees, float aspect)
        {
            return lookAtUnits + LookAtPlaneOffset(rotation, distanceUnits, pointerNdc,
                fieldOfViewDegrees, aspect);
        }

        /// <summary>
        /// The look-at offset that keeps <paramref name="anchorUnits"/> under
        /// the same screen position at a new distance, at the look-at plane's
        /// depth. Applied every frame of a zoom tween, it pins the point the
        /// player put the cursor on.
        /// </summary>
        public static Vector3 AnchoredLookAtUnits(Vector3 anchorUnits, Quaternion rotation,
            float distanceUnits, Vector2 pointerNdc, float fieldOfViewDegrees, float aspect)
        {
            return anchorUnits - LookAtPlaneOffset(rotation, distanceUnits, pointerNdc,
                fieldOfViewDegrees, aspect);
        }

        /// <summary>Clamp a look-at offset to <see cref="MaxLookAtOffsetM"/>.</summary>
        public static Vector3d ClampLookAtOffsetM(Vector3d offsetM)
        {
            double magnitude = offsetM.Magnitude;
            if (!(magnitude > MaxLookAtOffsetM)) return offsetM;
            return offsetM * (MaxLookAtOffsetM / magnitude);
        }

        /// <summary>
        /// The in-plane offset a ray at <paramref name="pointerNdc"/> has on
        /// the look-at plane, in render units. The vertical half-tangent is the
        /// field of view's; the horizontal one carries the aspect ratio.
        /// </summary>
        private static Vector3 LookAtPlaneOffset(Quaternion rotation, float distanceUnits,
            Vector2 pointerNdc, float fieldOfViewDegrees, float aspect)
        {
            float halfHeight = Mathf.Tan(0.5f * fieldOfViewDegrees * Mathf.Deg2Rad);
            float halfWidth = halfHeight * Mathf.Max(aspect, 0f);

            return (rotation * Vector3.right) * (distanceUnits * pointerNdc.x * halfWidth)
                   + (rotation * Vector3.up) * (distanceUnits * pointerNdc.y * halfHeight);
        }
    }
}
