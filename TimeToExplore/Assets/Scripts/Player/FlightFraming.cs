using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The chase pose used while a flight is under way: behind the ship along
    /// the route, 20° above it and 30° to one side. The angle is chosen once,
    /// when the flight begins; the ship is the render origin, so holding it is
    /// enough to follow the ship, and a right-drag hands the view back.
    ///
    /// The distance starts close, so the ship and the departing body read, and
    /// grows with the simulated distance one frame covers. At high warp a fixed
    /// 20,000 km view is crossed many times per frame, which reads as the route
    /// line jumping; keeping the frame's travel to a small fraction of the view
    /// turns that into a smooth slide. The ship marker keeps its flight size, so
    /// pulling back does not turn the ship into a speck.
    ///
    /// Plain C#, so the angles and the distance rule are verified without a
    /// camera.
    /// </summary>
    public static class FlightFraming
    {
        /// <summary>The camera sits this many degrees above the route.</summary>
        public const float ElevationDegrees = 20f;

        /// <summary>The camera sits this many degrees off the route's heading.</summary>
        public const float SideDegrees = 30f;

        /// <summary>Closest follow distance: 20,000 km, close enough that the
        /// ship and the departing body read.</summary>
        public const float DistanceUnits = 20f;

        /// <summary>Fraction of the view the ship may cross in one frame before
        /// the camera pulls back. Eight per cent is a fast but smooth slide.</summary>
        public const float MotionFraction = 0.08f;

        /// <summary>Farthest the chase pulls back: 5 million km.</summary>
        public const float MaxFollowDistanceUnits = 5_000f;

        /// <summary>Frame rate the per-frame travel is measured at.</summary>
        public const double ReferenceFrameRate = 60.0;

        /// <summary>
        /// The follow distance for a ship that covers <paramref name="stepPerFrameM"/>
        /// in one frame, so the route slides by no more than
        /// <see cref="MotionFraction"/> of the view. Never closer than
        /// <see cref="DistanceUnits"/> and never farther than
        /// <see cref="MaxFollowDistanceUnits"/>.
        /// </summary>
        public static float FollowDistanceUnits(double stepPerFrameM,
            float unitsPerMeter)
        {
            if (!(stepPerFrameM > 0.0) || !(unitsPerMeter > 0f)) return DistanceUnits;

            double neededM = stepPerFrameM / MotionFraction;
            return Mathf.Clamp((float)(neededM * unitsPerMeter),
                DistanceUnits, MaxFollowDistanceUnits);
        }

        /// <summary>
        /// The orbit angles for the chase view of a given travel direction.
        /// Returns false when the direction is degenerate, leaving the current
        /// pose alone.
        /// </summary>
        public static bool Pose(Vector3d flightDirection,
            out float yawDegrees, out float pitchDegrees)
        {
            yawDegrees = 0f;
            pitchDegrees = OrbitCamera.DefaultPitchDegrees;
            if (flightDirection.SqrMagnitude < 1e-12) return false;

            Vector3 forward = flightDirection.Normalized.ToUnity();

            // A frame around the route. Ecliptic north is already Unity up; a
            // near-polar route falls back to the world right axis so the cross
            // product never collapses.
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;

            // Look along the route, turned to the side and dropped below it. The
            // camera sits opposite this direction, so it is above the ship and
            // off its quarter.
            float side = SideDegrees * Mathf.Deg2Rad;
            float drop = ElevationDegrees * Mathf.Deg2Rad;
            Vector3 look = Mathf.Cos(drop) * (Mathf.Cos(side) * forward
                                              + Mathf.Sin(side) * right)
                           - Mathf.Sin(drop) * up;

            OrbitCamera.AnglesForLookDirection(Vector3d.FromUnity(look),
                out yawDegrees, out pitchDegrees);
            return true;
        }
    }
}
