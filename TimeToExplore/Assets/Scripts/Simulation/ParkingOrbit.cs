using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The parking orbit the Go To plan ends in: a circular orbit in the body's
    /// equatorial plane, at a radius comfortably above the surface, comfortably
    /// inside the Hill sphere, and high enough that its own gravity stays well
    /// below the drive's acceleration. This is the "stable orbit" the arrival
    /// leg targets, and the state the ship is left in when the plan completes.
    /// </summary>
    public static class ParkingOrbit
    {
        /// <summary>Minimum altitude above the body's surface.</summary>
        public const double DefaultAltitudeM = 300_000.0;

        /// <summary>Minimum radius as a multiple of the body's radius.</summary>
        public const double RadiusFactor = 1.15;

        /// <summary>Fraction of the Hill radius that a parking orbit may not exceed.</summary>
        public const double HillFraction = 0.5;

        /// <summary>
        /// Ceiling on the parking orbit's gravity, as a fraction of the drive's
        /// acceleration. A quintic capture arc cannot command less thrust than
        /// the parking orbit's own gravity at its endpoint, so parking where
        /// `GM / r²` approaches the drive makes the capture unflyable however
        /// long it is given. Keeping the local gravity at 40% of the drive
        /// leaves room for the curve's shape term and the tracking feedback.
        /// See the deep-well section of the navigation document.
        /// </summary>
        public const double GravityFractionOfMaxAcceleration = 0.4;

        public static double RadiusM(BodyRuntime body) =>
            RadiusM(body, ShipState.DefaultMaxAccelerationMps2);

        /// <summary>
        /// Parking radius for a given design acceleration. Bodies whose default
        /// orbit would sit in gravity above 40% of the drive are pushed out
        /// until it does; the Hill sphere still caps the result, so a body that
        /// cannot satisfy both bounds simply yields a plan the planner reports
        /// as infeasible.
        /// </summary>
        public static double RadiusM(BodyRuntime body, double maxAccelerationMps2)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (body.RadiusM <= 0.0)
                throw new ArgumentException("Parking orbits need a body with a radius.", nameof(body));

            double radius = Math.Max(body.RadiusM * RadiusFactor, body.RadiusM + DefaultAltitudeM);

            if (maxAccelerationMps2 > 0.0 && body.Gm > 0.0)
            {
                double ceiling = GravityFractionOfMaxAcceleration * maxAccelerationMps2;
                double gravityRadius = Math.Sqrt(body.Gm / ceiling);
                if (gravityRadius > radius) radius = gravityRadius;
            }

            double hill = body.HillRadiusM;
            if (double.IsFinite(hill) && hill > 0.0)
                radius = Math.Min(radius, hill * HillFraction);

            // A body whose Hill sphere barely clears its own surface (Phobos)
            // cannot host a useful parking orbit; keep the orbit outside the
            // body and let the plan report whatever thrust that demands.
            return Math.Max(radius, body.RadiusM * 1.05);
        }

        /// <summary>Circular orbital speed at the parking radius.</summary>
        public static double SpeedMps(BodyRuntime body) =>
            SpeedMps(body, ShipState.DefaultMaxAccelerationMps2);

        public static double SpeedMps(BodyRuntime body, double maxAccelerationMps2) =>
            TwoBody.CircularSpeedMps(body.Gm, RadiusM(body, maxAccelerationMps2));

        /// <summary>
        /// Position and velocity of a circular orbit of the given radius in the
        /// body's equatorial plane, at a phase angle measured from the stored
        /// equatorial x axis. Prograde about the body's north pole.
        /// </summary>
        public static void StateRelativeTo(BodyRuntime body, double phaseRad, double radiusM,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            double speed = TwoBody.CircularSpeedMps(body.Gm, radiusM);
            double cos = Math.Cos(phaseRad);
            double sin = Math.Sin(phaseRad);

            Vector3d radial = body.EquatorX * cos + body.EquatorY * sin;
            Vector3d tangential = body.EquatorY * cos - body.EquatorX * sin;

            positionM = radial * radiusM;
            velocityMps = tangential * speed;
        }

        /// <summary>Circular orbit at the parking radius, relative to the body.</summary>
        public static void StateRelativeTo(BodyRuntime body, double phaseRad,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            StateRelativeTo(body, phaseRad, RadiusM(body), out positionM, out velocityMps);
        }

        /// <summary>Absolute state on a circular orbit of the given radius.</summary>
        public static void StateAtRadius(BodyRuntime body, Vector3d bodyPositionM,
            Vector3d bodyVelocityMps, double phaseRad, double radiusM,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            StateRelativeTo(body, phaseRad, radiusM, out Vector3d localPosition, out Vector3d localVelocity);
            positionM = bodyPositionM + localPosition;
            velocityMps = bodyVelocityMps + localVelocity;
        }

        /// <summary>Absolute state on the parking orbit.</summary>
        public static void StateAt(BodyRuntime body, Vector3d bodyPositionM, Vector3d bodyVelocityMps,
            double phaseRad, out Vector3d positionM, out Vector3d velocityMps)
        {
            StateAtRadius(body, bodyPositionM, bodyVelocityMps, phaseRad, RadiusM(body),
                out positionM, out velocityMps);
        }

        /// <summary>Absolute state on the parking orbit for a given design
        /// acceleration.</summary>
        public static void StateAt(BodyRuntime body, Vector3d bodyPositionM, Vector3d bodyVelocityMps,
            double phaseRad, double maxAccelerationMps2,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            StateAtRadius(body, bodyPositionM, bodyVelocityMps, phaseRad,
                RadiusM(body, maxAccelerationMps2), out positionM, out velocityMps);
        }

        /// <summary>Phase angle in the body's equatorial plane that a direction
        /// from the body points at. Used to choose an arrival point on the side
        /// the ship approaches from.</summary>
        public static double PhaseOfDirection(BodyRuntime body, Vector3d directionFromBody)
        {
            Vector3d direction = directionFromBody.Normalized;
            return Math.Atan2(Vector3d.Dot(direction, body.EquatorY),
                Vector3d.Dot(direction, body.EquatorX));
        }

        /// <summary>Orbital period of the parking orbit, in seconds.</summary>
        public static double PeriodSeconds(BodyRuntime body) =>
            PeriodSeconds(body, ShipState.DefaultMaxAccelerationMps2);

        public static double PeriodSeconds(BodyRuntime body, double maxAccelerationMps2) =>
            TwoBody.PeriodSeconds(body.Gm, RadiusM(body, maxAccelerationMps2));
    }
}
