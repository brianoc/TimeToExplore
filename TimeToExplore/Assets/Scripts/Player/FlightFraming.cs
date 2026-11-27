using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The chase pose used while a flight is under way: behind the ship along
    /// the route, 20° above it and 30° to one side, at a close fixed distance so
    /// the ship is the subject rather than a speck in the route's overview. The
    /// pose is chosen once, when the flight begins; the ship is the render
    /// origin, so holding the angles is enough to follow it, and a right-drag
    /// hands the view back to the player.
    ///
    /// Plain C#, so the angles are verified without a camera.
    /// </summary>
    public static class FlightFraming
    {
        /// <summary>The camera sits this many degrees above the route.</summary>
        public const float ElevationDegrees = 20f;

        /// <summary>The camera sits this many degrees off the route's heading.</summary>
        public const float SideDegrees = 30f;

        /// <summary>Follow distance: 20,000 km, close enough that the ship is
        /// the subject and the departing body still reads.</summary>
        public const float DistanceUnits = 0.02f;

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
