using System;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The opening view: a wide shot of Earth and the Moon from above the
    /// ecliptic, with the ship at Gateway between them.
    ///
    /// The pair sets the shot. The camera sits on the perpendicular to the
    /// Earth–Moon line, tilted 20° above the ecliptic, on the sunward side so
    /// both discs are lit. The look-at is the midpoint of the pair, and the
    /// distance is the nearest one that keeps both inside the frame with a
    /// wider margin than a route fit, so the opening reads as a system view
    /// rather than a body framed at its own radius.
    ///
    /// Plain C# over double-precision ecliptic metres, so the angle, the side,
    /// and the fit are verified in EditMode without a scene.
    /// </summary>
    public static class OpeningFraming
    {
        /// <summary>Camera elevation above the ecliptic, in degrees.</summary>
        public const float PitchDegrees = 20f;

        /// <summary>
        /// Breathing room beyond the pair, as a multiple of the projected
        /// half-width. Larger than the route fit's 1.12 so the two discs sit
        /// clear of the HUD's side panels and the frame edge.
        /// </summary>
        public const float FitMargin = 1.25f;

        /// <summary>
        /// The opening frame for the Earth–Moon pair. The side of the pair is
        /// chosen toward the Sun, so the camera looks away from it and both
        /// bodies show a lit face; when the Sun lies along the pair, the side
        /// nearest the player's current yaw is kept.
        /// </summary>
        public static RouteFraming.Frame Compute(
            Vector3d shipPositionM, Vector3d earthPositionM, Vector3d moonPositionM,
            Vector3d sunPositionM, float unitsPerMeter, float fieldOfViewDegrees,
            float aspect, float preferredYawDegrees, float maxDistanceUnits)
        {
            Vector3d chord = moonPositionM - earthPositionM;
            Vector3d across = new Vector3d(-chord.y, chord.x, 0.0).Normalized;
            if (across.SqrMagnitude < 1e-12) return default;

            Vector3d toSun = sunPositionM - shipPositionM;
            double sunSide = Vector3d.Dot(across, toSun);
            if (sunSide < 0.0)
            {
                across = -across;
            }
            else if (sunSide == 0.0)
            {
                Vector3d current = Vector3d.FromUnity(
                    OrbitCamera.Direction(preferredYawDegrees, 0f));
                if (Vector3d.Dot(across, current) < 0.0) across = -across;
            }

            double pitchRadians = PitchDegrees * Math.PI / 180.0;
            Vector3d offsetEcliptic = across * Math.Cos(pitchRadians)
                                      + Vector3d.Up * Math.Sin(pitchRadians);

            // The camera looks along the opposite of its offset from the
            // look-at point, so the orbit angles come from that direction.
            OrbitCamera.AnglesForLookDirection(-offsetEcliptic,
                out float yawDegrees, out float pitchDegrees);

            return RouteFraming.ComputeForAngles(
                new[] { earthPositionM, moonPositionM }, shipPositionM,
                shipPositionM, unitsPerMeter, fieldOfViewDegrees, aspect,
                yawDegrees, pitchDegrees, maxDistanceUnits, FitMargin);
        }
    }
}
