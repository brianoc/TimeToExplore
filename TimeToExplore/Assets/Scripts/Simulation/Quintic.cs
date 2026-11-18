using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// A degree-five Hermite curve that matches position, velocity, and
    /// acceleration at both ends. Six conditions per axis are exactly what a
    /// quintic can satisfy, which is why the planner uses it: the arc starts
    /// and ends in free fall (its endpoint acceleration is set to local
    /// gravity), so the commanded thrust is zero at both ends and the
    /// accelerate/decelerate profile emerges from the curve rather than from a
    /// bang-bang guess.
    ///
    /// The parameter u runs 0..1 over the arc; derivatives are with respect to
    /// real time, so <see cref="VelocityAt"/> is m/s and
    /// <see cref="AccelerationAt"/> is m/s².
    /// </summary>
    public struct Quintic : IArcPath
    {
        private readonly Vector3d _a0;
        private readonly Vector3d _a1;
        private readonly Vector3d _a2;
        private readonly Vector3d _a3;
        private readonly Vector3d _a4;
        private readonly Vector3d _a5;
        private readonly double _durationSeconds;

        public double DurationSeconds => _durationSeconds;

        private Quintic(Vector3d a0, Vector3d a1, Vector3d a2, Vector3d a3, Vector3d a4,
            Vector3d a5, double durationSeconds)
        {
            _a0 = a0;
            _a1 = a1;
            _a2 = a2;
            _a3 = a3;
            _a4 = a4;
            _a5 = a5;
            _durationSeconds = durationSeconds;
        }

        /// <summary>
        /// Build the arc from the state at its start to the state at its end.
        /// Endpoint accelerations are usually the local gravity vector, which
        /// makes the commanded thrust zero at the seams.
        /// </summary>
        public static Quintic Create(
            Vector3d startPositionM, Vector3d startVelocityMps, Vector3d startAccelerationMps2,
            Vector3d endPositionM, Vector3d endVelocityMps, Vector3d endAccelerationMps2,
            double durationSeconds)
        {
            if (!(durationSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds),
                    "A quintic arc needs a positive duration.");

            double t = durationSeconds;
            double t2 = t * t;

            // Coefficients of the normalised curve q(u) = p(uT).
            Vector3d a0 = startPositionM;
            Vector3d a1 = startVelocityMps * t;
            Vector3d a2 = startAccelerationMps2 * (0.5 * t2);

            Vector3d d0 = endPositionM - a0 - a1 - a2;
            Vector3d d1 = endVelocityMps * t - a1 - a2 * 2.0;
            Vector3d d2 = endAccelerationMps2 * t2 - a2 * 2.0;

            // Solves the three remaining boundary conditions; see the derivation
            // in the flight-model document.
            Vector3d a5 = (d2 + d0 * 12.0 - d1 * 6.0) * 0.5;
            Vector3d a4 = d1 - d0 * 3.0 - a5 * 2.0;
            Vector3d a3 = d0 - a4 - a5;

            return new Quintic(a0, a1, a2, a3, a4, a5, durationSeconds);
        }

        /// <summary>An arc that starts and ends at rest, for reference profiles
        /// and tests.</summary>
        public static Quintic RestToRest(Vector3d startPositionM, Vector3d endPositionM, double durationSeconds)
        {
            return Create(startPositionM, Vector3d.Zero, Vector3d.Zero,
                endPositionM, Vector3d.Zero, Vector3d.Zero, durationSeconds);
        }

        public Vector3d PositionAt(double u)
        {
            return _a0 + u * (_a1 + u * (_a2 + u * (_a3 + u * (_a4 + u * _a5))));
        }

        public Vector3d VelocityAt(double u)
        {
            Vector3d derivative = _a1 + u * (_a2 * 2.0
                + u * (_a3 * 3.0 + u * (_a4 * 4.0 + u * (_a5 * 5.0))));
            return derivative / DurationSeconds;
        }

        public Vector3d AccelerationAt(double u)
        {
            Vector3d second = _a2 * 2.0
                              + u * (_a3 * 6.0 + u * (_a4 * 12.0 + u * (_a5 * 20.0)));
            return second / (DurationSeconds * DurationSeconds);
        }

        /// <summary>Time in seconds from the arc's start for a parameter u.</summary>
        public double SecondsAt(double u) => u * DurationSeconds;

        /// <summary>Parameter u for a time offset in seconds.</summary>
        public double ParameterAt(double seconds) =>
            DurationSeconds > 0.0 ? seconds / DurationSeconds : 0.0;

        public double ParameterAtSeconds(double seconds) => ParameterAt(seconds);

        /// <summary>Largest acceleration magnitude on the curve, ignoring
        /// gravity. Used by tests and by the planner's feasibility estimate.</summary>
        public double PeakAccelerationMagnitude(int samples = 256)
        {
            if (samples < 2) throw new ArgumentOutOfRangeException(nameof(samples));
            double peak = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double magnitude = AccelerationAt((double)i / samples).Magnitude;
                if (magnitude > peak) peak = magnitude;
            }

            return peak;
        }

        /// <summary>
        /// Analytic peak of a rest-to-rest quintic: 10u³ − 15u⁴ + 6u⁵ has its
        /// largest second derivative of 5√3/3 ≈ 5.7735 at u = (3 ∓ √3)/6.
        /// </summary>
        public const double RestToRestPeakFactor = 5.773502691896258;
    }
}
