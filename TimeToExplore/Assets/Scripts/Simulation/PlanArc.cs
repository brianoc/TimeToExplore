using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    public enum FlightPhase
    {
        /// <summary>The short powered break out of the parking orbit: a quintic
        /// that raises the ship onto its planetocentric escape conic.</summary>
        Departure,
        /// <summary>The ballistic planetocentric climb from the parking radius to
        /// the departure handover ceiling: a Keplerian conic about the departure
        /// body, with the drive off. A long trip uses the minimum-energy
        /// parabolic climb; a shorter one a hyperbolic excess speed.</summary>
        Escape,
        /// <summary>Heliocentric transfer between spheres of influence.</summary>
        Cruise,
        /// <summary>The planetocentric approach: the Keplerian fall about the
        /// target from the handover band's ceiling down to where the brake
        /// begins. It is the part of the final approach a heliocentric conic
        /// cannot describe ([the powered-leg floor](/simulation/powered-leg-floor.md)).</summary>
        Approach,
        /// <summary>A Keplerian coast, such as the inbound transfer hyperbola.</summary>
        Coast,
        /// <summary>Dropping from the target's sphere of influence into the
        /// parking orbit. A conic capture uses this for the transfer ellipse;
        /// the quintic fallback uses it for the whole powered capture.</summary>
        Capture,
        /// <summary>Coasting on the parking orbit after circularisation.</summary>
        Parking
    }

    /// <summary>
    /// One leg of a planned trip, in heliocentric coordinates. The planned
    /// thrust at a time is the path's acceleration minus the gravity at the
    /// path's own position: a feed-forward command that reproduces the path
    /// exactly when the ship starts on it.
    ///
    /// A path can be a powered quintic or a Keplerian <see cref="ConicArc"/>.
    /// A conic has no thrust along its two-body reference; the full field adds
    /// a small third-body correction. An arc may begin with an impulsive burn:
    /// <see cref="EntryImpulseVectorMps"/> is the velocity change from the
    /// previous arc's end, applied when the flight crosses the seam.
    /// </summary>
    public sealed class PlanArc
    {
        public PlanArc(FlightPhase phase, string referenceBodyId, double startJulianDate,
            double endJulianDate, IArcPath path)
        {
            if (!(endJulianDate > startJulianDate))
                throw new ArgumentOutOfRangeException(nameof(endJulianDate));

            Phase = phase;
            ReferenceBodyId = referenceBodyId;
            StartJulianDate = startJulianDate;
            EndJulianDate = endJulianDate;
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public FlightPhase Phase { get; }
        public string ReferenceBodyId { get; }
        public double StartJulianDate { get; }
        public double EndJulianDate { get; }
        public IArcPath Path { get; }

        /// <summary>Velocity change at the arc's start, in the heliocentric
        /// frame. Zero for a seam that is continuous in velocity.</summary>
        public Vector3d EntryImpulseVectorMps { get; internal set; }

        /// <summary>Magnitude of <see cref="EntryImpulseVectorMps"/>, in m/s.</summary>
        public double EntryImpulseMps => EntryImpulseVectorMps.Magnitude;

        /// <summary>True when the arc is a Keplerian coast rather than a powered
        /// quintic.</summary>
        public bool IsCoast => Path is ConicArc;

        /// <summary>True when the arc is a fixed-thrust finite burn rather than
        /// a smooth quintic or a coast.</summary>
        public bool IsFiniteBurn => Path is FiniteBurnArc;

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
            double seconds = (julianDate - StartJulianDate) * JulianDate.SecondsPerDay;
            if (!(seconds > 0.0)) return 0.0;
            return Math.Clamp(Path.ParameterAtSeconds(seconds), 0.0, 1.0);
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
