using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Scalar two-body formulae used by the flight model: circular and escape
    /// speeds, specific energy, and the osculating elements of a state vector.
    /// Everything here is a pure function of position, velocity, and GM, so it
    /// can be checked against textbook values without a propagator.
    /// </summary>
    public static class TwoBody
    {
        public static double CircularSpeedMps(double gravitationalParameter, double radiusM)
        {
            if (gravitationalParameter <= 0.0 || radiusM <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(radiusM));
            return Math.Sqrt(gravitationalParameter / radiusM);
        }

        public static double EscapeSpeedMps(double gravitationalParameter, double radiusM)
        {
            return Math.Sqrt(2.0) * CircularSpeedMps(gravitationalParameter, radiusM);
        }

        /// <summary>Specific orbital energy, v²/2 − μ/r. Negative for a closed orbit.</summary>
        public static double SpecificEnergyJPerKg(Vector3d positionM, Vector3d velocityMps, double gravitationalParameter)
        {
            double r = positionM.Magnitude;
            if (r <= 0.0) throw new ArgumentOutOfRangeException(nameof(positionM));
            return 0.5 * velocityMps.SqrMagnitude - gravitationalParameter / r;
        }

        /// <summary>Semi-major axis from the vis-viva equation. Infinity at
        /// parabolic energy, negative for a hyperbola.</summary>
        public static double SemiMajorAxisM(Vector3d positionM, Vector3d velocityMps, double gravitationalParameter)
        {
            double energy = SpecificEnergyJPerKg(positionM, velocityMps, gravitationalParameter);
            double denominator = 2.0 * energy;
            if (Math.Abs(denominator) < 1e-18) return double.PositiveInfinity;
            return -gravitationalParameter / denominator;
        }

        /// <summary>Eccentricity from the eccentricity vector,
        /// e = (v × h)/μ − r̂. Zero for a circular orbit.</summary>
        public static double Eccentricity(Vector3d positionM, Vector3d velocityMps, double gravitationalParameter)
        {
            double r = positionM.Magnitude;
            if (r <= 0.0) throw new ArgumentOutOfRangeException(nameof(positionM));

            Vector3d h = Vector3d.Cross(positionM, velocityMps);
            Vector3d eccentricity = Vector3d.Cross(velocityMps, h) / gravitationalParameter
                                    - positionM / r;
            return eccentricity.Magnitude;
        }

        public static double PeriodSeconds(double gravitationalParameter, double semiMajorAxisM)
        {
            if (gravitationalParameter <= 0.0 || semiMajorAxisM <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(semiMajorAxisM));
            return OrbitMath.TwoPi * Math.Sqrt(
                semiMajorAxisM * semiMajorAxisM * semiMajorAxisM / gravitationalParameter);
        }

        /// <summary>Flight path angle in radians: the angle between velocity
        /// and the local horizontal. Zero on a circular orbit.</summary>
        public static double FlightPathAngleRad(Vector3d positionM, Vector3d velocityMps)
        {
            Vector3d radial = positionM.Normalized;
            double speed = velocityMps.Magnitude;
            if (speed <= 0.0) return 0.0;
            return Math.Asin(Math.Clamp(Vector3d.Dot(velocityMps, radial) / speed, -1.0, 1.0));
        }

        /// <summary>Inclination of a state vector relative to the ecliptic, in
        /// radians. The reference plane is the ecliptic (+z north).</summary>
        public static double InclinationRad(Vector3d positionM, Vector3d velocityMps)
        {
            Vector3d h = Vector3d.Cross(positionM, velocityMps);
            double magnitude = h.Magnitude;
            if (magnitude <= 0.0) return 0.0;
            return Math.Acos(Math.Clamp(h.z / magnitude, -1.0, 1.0));
        }
    }
}
