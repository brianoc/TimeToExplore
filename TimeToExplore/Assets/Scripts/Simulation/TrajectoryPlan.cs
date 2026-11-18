using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// A complete Go To plan: an ordered chain of arcs from the ship's current
    /// state to a stable parking orbit around the target. A quintic chain is
    /// continuous in position, velocity and acceleration; a conic chain is
    /// continuous in position and acceleration and steps in velocity by its
    /// impulsive burns.
    /// </summary>
    public sealed class TrajectoryPlan
    {
        private readonly List<PlanArc> _arcs;

        public TrajectoryPlan(string targetId, double maxAccelerationMps2, List<PlanArc> arcs,
            ShipState departureState, ShipState arrivalState)
        {
            if (arcs == null || arcs.Count == 0)
                throw new ArgumentException("A plan needs at least one arc.", nameof(arcs));

            TargetId = targetId;
            MaxAccelerationMps2 = maxAccelerationMps2;
            _arcs = arcs;
            DepartureState = departureState;
            ArrivalState = arrivalState;
        }

        public string TargetId { get; }
        public double MaxAccelerationMps2 { get; }
        public IReadOnlyList<PlanArc> Arcs => _arcs;

        /// <summary>Reference state at the start of the plan.</summary>
        public ShipState DepartureState { get; }

        /// <summary>Reference state at the end of the plan, on the parking orbit.</summary>
        public ShipState ArrivalState { get; }

        public double DepartureJulianDate => _arcs[0].StartJulianDate;
        public double ArrivalJulianDate => _arcs[_arcs.Count - 1].EndJulianDate;

        public double FlightTimeSeconds => (ArrivalJulianDate - DepartureJulianDate) * JulianDate.SecondsPerDay;
        public double FlightTimeDays => FlightTimeSeconds / JulianDate.SecondsPerDay;

        public double PeakThrustMps2
        {
            get
            {
                double peak = 0.0;
                foreach (PlanArc arc in _arcs)
                    if (arc.PeakThrustMps2 > peak) peak = arc.PeakThrustMps2;
                return peak;
            }
        }

        public double PeakThrottle => MaxAccelerationMps2 > 0.0
            ? Math.Clamp(PeakThrustMps2 / MaxAccelerationMps2, 0.0, double.PositiveInfinity)
            : 0.0;

        public bool IsFeasible => string.IsNullOrEmpty(InfeasibleReason)
                                  && PeakThrustMps2 <= MaxAccelerationMps2 * (1.0 + 1e-6);

        /// <summary>Why the plan is not flyable, or null. A conic arrival that
        /// cannot be built reports the reason rather than a fake thrust.</summary>
        public string InfeasibleReason { get; internal set; }

        public double DeltaVMps
        {
            get
            {
                double total = 0.0;
                foreach (PlanArc arc in _arcs)
                    total += arc.DeltaVMps + arc.EntryImpulseMps;
                return total;
            }
        }

        /// <summary>Delta-v carried by impulsive burns at arc seams. For a conic
        /// capture these are the periapsis capture and the circularisation; the
        /// rest of the plan is the powered quintic's integrated thrust.</summary>
        public double ImpulseDeltaVMps
        {
            get
            {
                double total = 0.0;
                foreach (PlanArc arc in _arcs) total += arc.EntryImpulseMps;
                return total;
            }
        }

        public double MinimumClearanceM
        {
            get
            {
                double minimum = double.PositiveInfinity;
                foreach (PlanArc arc in _arcs)
                    if (arc.MinimumClearanceM < minimum) minimum = arc.MinimumClearanceM;
                return minimum;
            }
        }

        public PlanArc ArcAt(double julianDate)
        {
            foreach (PlanArc arc in _arcs)
                if (arc.Contains(julianDate)) return arc;

            return julianDate < _arcs[0].StartJulianDate ? _arcs[0] : _arcs[_arcs.Count - 1];
        }

        public PlanArc ArcForPhase(FlightPhase phase)
        {
            foreach (PlanArc arc in _arcs)
                if (arc.Phase == phase) return arc;
            return null;
        }

        public FlightPhase PhaseAt(double julianDate) => ArcAt(julianDate).Phase;

        public bool IsComplete(double julianDate) => julianDate >= ArrivalJulianDate;

        /// <summary>The next arc boundary after a date: the end of the arc the
        /// ship is on, then the following seams, then the arrival. Used by the
        /// flight session to land exactly on the arrival state and by the warp
        /// policy to pace the countdown. Infinite once the plan is complete.</summary>
        public double NextBoundaryJulianDate(double julianDate)
        {
            foreach (PlanArc arc in _arcs)
                if (arc.EndJulianDate > julianDate) return arc.EndJulianDate;
            return double.PositiveInfinity;
        }

        /// <summary>
        /// The point where the along-track thrust changes from accelerating the
        /// ship to decelerating it: the top of the arc, and what the HUD marks
        /// as the flip. The quintic turns over smoothly rather than switching,
        /// so this is the zero of `v · (a_ref − g)`, interpolated between
        /// samples. NaN when the plan never decelerates.
        /// </summary>
        public double FindFlipJulianDate(IGravityField gravity, int samplesPerArc = 32)
        {
            if (gravity == null) throw new ArgumentNullException(nameof(gravity));
            if (samplesPerArc < 2) throw new ArgumentOutOfRangeException(nameof(samplesPerArc));

            bool seenPositive = false;
            double previousAlong = 0.0;
            double previousJulianDate = double.NaN;

            foreach (PlanArc arc in _arcs)
            {
                for (int i = 0; i <= samplesPerArc; i++)
                {
                    double u = (double)i / samplesPerArc;
                    double julianDate = arc.StartJulianDate
                                        + arc.Path.SecondsAt(u) / JulianDate.SecondsPerDay;

                    Vector3d position = arc.Path.PositionAt(u);
                    Vector3d velocity = arc.Path.VelocityAt(u);
                    Vector3d required = arc.Path.AccelerationAt(u);

                    gravity.Prepare(position, julianDate);
                    Vector3d thrust = required - gravity.AccelerationAt(position, julianDate);
                    double along = Vector3d.Dot(velocity, thrust);

                    if (seenPositive && previousAlong > 0.0 && along < 0.0)
                    {
                        double t = previousAlong / (previousAlong - along);
                        return previousJulianDate + t * (julianDate - previousJulianDate);
                    }

                    if (!seenPositive && along > 0.0) seenPositive = true;

                    previousAlong = along;
                    previousJulianDate = julianDate;
                }
            }

            return double.NaN;
        }


        public Vector3d ReferencePositionAt(double julianDate)
        {
            if (julianDate <= DepartureJulianDate) return _arcs[0].Path.PositionAt(0.0);
            if (julianDate >= ArrivalJulianDate) return _arcs[_arcs.Count - 1].Path.PositionAt(1.0);
            return ArcAt(julianDate).ReferencePositionAt(julianDate);
        }

        public Vector3d ReferenceVelocityAt(double julianDate)
        {
            if (julianDate <= DepartureJulianDate) return _arcs[0].Path.VelocityAt(0.0);
            if (julianDate >= ArrivalJulianDate) return _arcs[_arcs.Count - 1].Path.VelocityAt(1.0);
            return ArcAt(julianDate).ReferenceVelocityAt(julianDate);
        }

        public Vector3d ReferenceAccelerationAt(double julianDate)
        {
            if (julianDate <= DepartureJulianDate) return _arcs[0].Path.AccelerationAt(0.0);
            if (julianDate >= ArrivalJulianDate) return _arcs[_arcs.Count - 1].Path.AccelerationAt(1.0);
            return ArcAt(julianDate).ReferenceAccelerationAt(julianDate);
        }

        /// <summary>No thrust once the plan is complete: the ship is on the
        /// parking orbit and gravity does the rest.</summary>
        public ThrustCommand ThrustAt(double julianDate, IGravityField gravity)
        {
            if (IsComplete(julianDate)) return ThrustCommand.None;
            return ArcAt(julianDate).ThrustAt(julianDate, gravity);
        }

        /// <summary>Integrator step hint for the next arc boundary.</summary>
        public double SuggestedMaxStepSeconds
        {
            get
            {
                double step = double.PositiveInfinity;
                foreach (PlanArc arc in _arcs)
                    if (arc.SuggestedMaxStepSeconds < step) step = arc.SuggestedMaxStepSeconds;
                return step;
            }
        }

        /// <summary>Sample the reference path into a polyline for the renderer.
        /// Each arc contributes <paramref name="samplesPerArc"/> segments and the
        /// last point is the arrival state.</summary>
        public void SamplePath(int samplesPerArc, List<Vector3d> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (samplesPerArc < 1) throw new ArgumentOutOfRangeException(nameof(samplesPerArc));

            output.Clear();
            for (int a = 0; a < _arcs.Count; a++)
            {
                PlanArc arc = _arcs[a];
                for (int i = 0; i <= samplesPerArc; i++)
                {
                    if (a > 0 && i == 0) continue; // shared seam
                    output.Add(arc.Path.PositionAt((double)i / samplesPerArc));
                }
            }
        }

        public override string ToString() =>
            $"to {TargetId}: {FlightTimeDays:F2} d, {PeakThrustMps2:F2} m/s² peak, " +
            $"{_arcs.Count} arcs, {DeltaVMps / 1000.0:F0} km/s";
    }
}
