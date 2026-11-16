using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    public enum FlightPhase
    {
        /// <summary>Climbing out of the departure body's sphere of influence.</summary>
        Departure,
        /// <summary>Heliocentric transfer between spheres of influence.</summary>
        Cruise,
        /// <summary>Dropping from the target's sphere of influence into the
        /// parking orbit.</summary>
        Capture
    }

    /// <summary>
    /// One quintic leg of a planned trip, in heliocentric coordinates. The
    /// planned thrust at a time is the curve's acceleration minus the gravity
    /// at the curve's own position: a feed-forward command that reproduces the
    /// curve exactly when the ship starts on it.
    /// </summary>
    public sealed class PlanArc
    {
        public PlanArc(FlightPhase phase, string referenceBodyId, double startJulianDate,
            double endJulianDate, Quintic path)
        {
            if (!(endJulianDate > startJulianDate))
                throw new ArgumentOutOfRangeException(nameof(endJulianDate));

            Phase = phase;
            ReferenceBodyId = referenceBodyId;
            StartJulianDate = startJulianDate;
            EndJulianDate = endJulianDate;
            Path = path;
        }

        public FlightPhase Phase { get; }
        public string ReferenceBodyId { get; }
        public double StartJulianDate { get; }
        public double EndJulianDate { get; }
        public Quintic Path { get; }

        /// <summary>Largest commanded thrust on the arc, including the gravity
        /// feed-forward. Set by the planner when it measures the arc.</summary>
        public double PeakThrustMps2 { get; internal set; }

        /// <summary>Integrated |thrust| dt over the arc, in m/s. This is the
        /// delta-v the plan spends, which the fuel model will charge for.</summary>
        public double DeltaVMps { get; internal set; }

        /// <summary>Closest approach to any body's surface along the arc.
        /// Negative means the reference curve passes through a body.</summary>
        public double MinimumClearanceM { get; internal set; } = double.PositiveInfinity;

        public string NearestBodyId { get; internal set; }

        public double DurationSeconds => (EndJulianDate - StartJulianDate) * JulianDate.SecondsPerDay;

        /// <summary>Step hint for the integrator: about 250 steps per arc,
        /// clipped to a range that is useful at both solar and orbital scale.</summary>
        public double SuggestedMaxStepSeconds =>
            Math.Clamp(DurationSeconds / 250.0, 0.5, 600.0);

        public bool Contains(double julianDate) =>
            julianDate >= StartJulianDate && julianDate < EndJulianDate;

        public double ParameterAt(double julianDate)
        {
            double span = EndJulianDate - StartJulianDate;
            if (span <= 0.0) return 0.0;
            return Math.Clamp((julianDate - StartJulianDate) / span, 0.0, 1.0);
        }

        public Vector3d ReferencePositionAt(double julianDate) =>
            Path.PositionAt(ParameterAt(julianDate));

        public Vector3d ReferenceVelocityAt(double julianDate) =>
            Path.VelocityAt(ParameterAt(julianDate));

        public Vector3d ReferenceAccelerationAt(double julianDate) =>
            Path.AccelerationAt(ParameterAt(julianDate));

        /// <summary>
        /// Feed-forward thrust: the acceleration the curve needs minus the
        /// gravity the ship will feel there. Gravity is sampled at the
        /// reference position, not the actual one, because this is the command
        /// that reproduces the curve.
        /// </summary>
        public ThrustCommand ThrustAt(double julianDate, IGravityField gravity)
        {
            double u = ParameterAt(julianDate);
            Vector3d position = Path.PositionAt(u);
            Vector3d required = Path.AccelerationAt(u);
            Vector3d localGravity = gravity.AccelerationAt(position, julianDate);
            return new ThrustCommand(required - localGravity);
        }

        public override string ToString() =>
            $"{Phase} {DurationSeconds / 3600.0:F2} h, peak {PeakThrustMps2:F2} m/s²";
    }
}
