using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Builds a Go To plan: an escape arc out of the departure body's sphere of
    /// influence, a heliocentric cruise arc, and a capture arc that ends on the
    /// target's parking orbit. Every arc is a quintic that matches its
    /// neighbours in position, velocity, and acceleration, so the whole plan is
    /// one smooth trajectory with zero commanded thrust at the seams.
    ///
    /// The solver is a search, not an optimal-control solve. It allocates time
    /// between the arcs in proportion to each arc's kinematic minimum
    /// 2√(d/a), then grows the whole plan until the measured peak thrust fits
    /// the drive. The result is a smooth trajectory 20-40% slower than the
    /// bang-bang optimum, which is the right trade for a first cut: it is
    /// deterministic, always feasible for the inner system, and its endpoint is
    /// exact.
    ///
    /// Known limit: one quintic cannot thread a gravity well whose parking-orbit
    /// gravity approaches the drive's own acceleration. Jupiter's parking orbit
    /// sits at 19 m/s², and the capture arc cannot be flown there. Capture arcs
    /// must become transfer ellipses (or a shaped spiral of several arcs) before
    /// the outer giants are reachable. See the navigation document.
    /// </summary>
    public sealed class TripPlanner
    {
        private const double SecondsPerDay = JulianDate.SecondsPerDay;

        /// <summary>Shortest arc the planner will emit, in seconds. Below this an
        /// arc is treated as a seam rather than a flight phase.</summary>
        public const double MinimumArcSeconds = 1.0;

        /// <summary>How far the nav computer lets a player stretch a trip beyond
        /// the fastest feasible plan. Three times the minimum roughly divides
        /// the geometric peak thrust by nine, which is enough to see the
        /// time/thrust trade without turning the plan into a drift.</summary>
        public const double MaxDurationScale = 3.0;

        private readonly SolarSystemModel _model;
        private readonly GravityField _gravity;

        public TripPlanner(SolarSystemModel model, GravityField gravity)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
        }

        /// <summary>Design acceleration the plan must stay inside.</summary>
        public double MaxAccelerationMps2 { get; set; } = ShipState.DefaultMaxAccelerationMps2;

        /// <summary>Fraction of the design acceleration the planner aims for, so
        /// the executed trajectory keeps a little authority in reserve.</summary>
        public double ThrustMargin { get; set; } = 0.97;

        /// <summary>Inbound speed at the target's sphere of influence. Shapes
        /// how much of the deceleration happens in the capture arc.</summary>
        public double EntrySpeedMps { get; set; } = 5_000.0;

        /// <summary>Outbound speed at the departure body's sphere of influence.</summary>
        public double EscapeSpeedMps { get; set; } = 1_000.0;

        /// <summary>Points sampled per arc to measure peak thrust and clearance.</summary>
        public int ArcSamples { get; set; } = 64;

        public int MaxIterations { get; set; } = 16;

        /// <summary>Relative flight-time error that ends the moving-target loop.</summary>
        public double ConvergenceTolerance { get; set; } = 0.005;

        /// <summary>Rotates the arrival point around the target's equator.</summary>
        public double ParkingPhaseOffsetRad { get; set; }

        /// <summary>Optional diagnostic sink for the planning loop.</summary>
        public Action<string> Trace { get; set; }

        public TrajectoryPlan Plan(ShipState ship, string targetId)
        {
            BodyRuntime target = RequireTarget(targetId);

            _model.ComputeState(target, ship.JulianDate, out Vector3d targetPosition, out _);
            double distance = Math.Max(Vector3d.Distance(targetPosition, ship.PositionM),
                ParkingOrbit.RadiusM(target, MaxAccelerationMps2));

            double total = 1.1 * 2.0 * Math.Sqrt(distance / MaxAccelerationMps2);
            TrajectoryPlan plan = null;
            double required = total;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                BuildResult result = Build(ship, target, total);
                plan = result.Plan;
                required = result.RequiredSeconds;

                Trace?.Invoke($"iter {iteration}: total {total / SecondsPerDay:F3} d, " +
                              $"required {required / SecondsPerDay:F3} d, " +
                              $"peak {plan.PeakThrustMps2:F3}, feasible {plan.IsFeasible}");

                if (Math.Abs(required - total) <= ConvergenceTolerance * total && plan.IsFeasible)
                    return plan;

                total = 0.5 * (total + required);
            }

            // A longer arc with the same endpoints needs less thrust, so a final
            // build at the duration the last pass asked for is feasible whenever
            // the limit is set by the curve's shape rather than by a gravity well.
            if (!plan.IsFeasible || required > total)
                plan = Build(ship, target, Math.Max(required, total)).Plan;

            return plan;
        }

        /// <summary>
        /// Build a plan at an explicit flight time instead of solving for the
        /// shortest feasible one. The arcs are allocated so the last one ends at
        /// <paramref name="durationSeconds"/> after departure; the target is
        /// still where the ephemeris puts it on that date, so the path is
        /// re-solved rather than rescaled.
        ///
        /// The one exception is the arcs' own kinematic floor: a request shorter
        /// than the combined 2√(d/a) of the legs cannot shrink them further, and
        /// the plan comes out longer than asked. The nav computer's minimum
        /// feasible time is above that floor, so a slider value is exact.
        ///
        /// A duration at or above the plan's minimum is normally feasible, but
        /// the arrival geometry moves with the date, so the caller must still
        /// read <see cref="TrajectoryPlan.IsFeasible"/>. Durations below the
        /// minimum are accepted and reported as infeasible rather than silently
        /// stretched: the panel can then explain why the drive cannot fly them.
        /// </summary>
        public TrajectoryPlan PlanAtDuration(ShipState ship, string targetId,
            double durationSeconds)
        {
            BodyRuntime target = RequireTarget(targetId);
            if (!(durationSeconds > 0.0) || !double.IsFinite(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds),
                    "A pinned flight time must be finite and positive.");

            return Build(ship, target, durationSeconds).Plan;
        }

        private BodyRuntime RequireTarget(string targetId)
        {
            BodyRuntime target = _model.Get(targetId);
            if (target == null)
                throw new ArgumentException($"No body '{targetId}' in the catalogue.", nameof(targetId));
            if (target.Gm <= 0.0)
                throw new ArgumentException(
                    $"'{targetId}' has no gravitational parameter to orbit.", nameof(targetId));
            if (!(MaxAccelerationMps2 > 0.0))
                throw new InvalidOperationException("MaxAccelerationMps2 must be positive.");

            return target;
        }

        private BuildResult Build(ShipState ship, BodyRuntime target, double totalSeconds)
        {
            double t0 = ship.JulianDate;
            double t1 = t0 + totalSeconds / SecondsPerDay;
            double targetSoi = SphereOfInfluence.RadiusM(target);

            _model.ComputeState(target, t0, out Vector3d targetAtDeparture, out _);
            bool insideTarget = Vector3d.Distance(ship.PositionM, targetAtDeparture) < targetSoi;

            BodyRuntime departureBody = null;
            double departureSoi = 0.0;
            if (!insideTarget)
            {
                foreach (BodyRuntime planet in _model.Sun.Children)
                {
                    if (planet == target || planet == target.Parent) continue;

                    double soi = SphereOfInfluence.RadiusM(planet);
                    if (!double.IsFinite(soi)) continue;

                    _model.ComputeState(planet, t0, out Vector3d planetPosition, out _);
                    if (Vector3d.Distance(ship.PositionM, planetPosition) < soi)
                    {
                        departureBody = planet;
                        departureSoi = soi;
                        break;
                    }
                }
            }

            ArrivalGeometry arrival = ComputeArrival(target, ship, t1, targetSoi, insideTarget);

            double escapeSeconds;
            double cruiseSeconds;
            double captureSeconds;

            Vector3d cruiseStartPosition = ship.PositionM;
            Vector3d cruiseStartVelocity = ship.VelocityMps;
            Vector3d exitPosition = Vector3d.Zero;
            Vector3d exitVelocity = Vector3d.Zero;
            double escapeEndJd = t0;

            double captureBase = BaseDuration(
                Vector3d.Distance(arrival.EntryPosition, arrival.ParkingPosition));

            if (insideTarget)
            {
                escapeSeconds = 0.0;
                cruiseSeconds = 0.0;
                captureSeconds = totalSeconds;
            }
            else if (departureBody != null)
            {
                _model.ComputeState(departureBody, t0,
                    out Vector3d anchorPosition, out Vector3d anchorVelocity);

                // Leave along the ship's own relative motion, not along the
                // target direction. Pointing the escape at the target couples
                // the escape length to the arrival date: when the target is
                // behind the departure body the ship has to chase that body
                // around its orbit, and the whole plan grows without bound.
                Vector3d escapeDirection = (ship.VelocityMps - anchorVelocity).Normalized;
                if (escapeDirection.SqrMagnitude < 0.5)
                    escapeDirection = (ship.PositionM - anchorPosition).Normalized;
                if (escapeDirection.SqrMagnitude < 0.5)
                    escapeDirection = new Vector3d(1.0, 0.0, 0.0);

                Vector3d provisionalExit = anchorPosition + escapeDirection * departureSoi;
                double escapeBase = BaseDuration(Vector3d.Distance(ship.PositionM, provisionalExit));
                double cruiseBase = BaseDuration(
                    Vector3d.Distance(provisionalExit, arrival.EntryPosition));

                double scale = Math.Max(1.0,
                    totalSeconds / (escapeBase + cruiseBase + captureBase));
                escapeSeconds = escapeBase * scale;
                cruiseSeconds = cruiseBase * scale;
                captureSeconds = captureBase * scale;

                escapeEndJd = t0 + escapeSeconds / SecondsPerDay;
                _model.ComputeState(departureBody, escapeEndJd,
                    out anchorPosition, out anchorVelocity);

                exitPosition = anchorPosition + escapeDirection * departureSoi;
                exitVelocity = anchorVelocity + escapeDirection * EscapeSpeedMps;
                cruiseStartPosition = exitPosition;
                cruiseStartVelocity = exitVelocity;
            }
            else
            {
                double cruiseBase = BaseDuration(
                    Vector3d.Distance(ship.PositionM, arrival.EntryPosition));

                double scale = Math.Max(1.0, totalSeconds / (cruiseBase + captureBase));
                escapeSeconds = 0.0;
                cruiseSeconds = cruiseBase * scale;
                captureSeconds = captureBase * scale;
            }

            double cruiseEndJd = escapeEndJd + cruiseSeconds / SecondsPerDay;
            double provisionalArrivalJd = cruiseEndJd + captureSeconds / SecondsPerDay;

            // Rebuild the arrival geometry at the true arrival date, so the final
            // state is exactly on the parking orbit at the instant the plan ends.
            arrival = ComputeArrival(target, ship, provisionalArrivalJd, targetSoi, insideTarget);
            double arrivalJd = provisionalArrivalJd;

            var arcs = new List<PlanArc>(3);

            if (insideTarget)
            {
                arcs.Add(MakeArc(FlightPhase.Capture, target.Id, t0, arrivalJd,
                    ship.PositionM, ship.VelocityMps,
                    arrival.ParkingPosition, arrival.ParkingVelocity));
            }
            else
            {
                if (departureBody != null && escapeSeconds > MinimumArcSeconds)
                {
                    arcs.Add(MakeArc(FlightPhase.Departure, departureBody.Id, t0, escapeEndJd,
                        ship.PositionM, ship.VelocityMps, exitPosition, exitVelocity));
                }

                if (cruiseSeconds > MinimumArcSeconds)
                {
                    arcs.Add(MakeArc(FlightPhase.Cruise, target.Id, escapeEndJd, cruiseEndJd,
                        cruiseStartPosition, cruiseStartVelocity,
                        arrival.EntryPosition, arrival.EntryVelocity));
                }

                arcs.Add(MakeArc(FlightPhase.Capture, target.Id, cruiseEndJd, arrivalJd,
                    arrival.EntryPosition, arrival.EntryVelocity,
                    arrival.ParkingPosition, arrival.ParkingVelocity));
            }

            double limit = MaxAccelerationMps2 * ThrustMargin;
            double worst = 1.0;
            foreach (PlanArc arc in arcs)
            {
                if (arc.PeakThrustMps2 <= 0.0) continue;
                worst = Math.Max(worst, arc.PeakThrustMps2 / limit);
            }

            // Grow quickly towards the feasible duration; shrink gently, because
            // the arc durations have a kinematic floor.
            double actualSeconds = (arrivalJd - t0) * SecondsPerDay;
            double requiredSeconds = Math.Max(totalSeconds, actualSeconds)
                                     * Math.Clamp(Math.Sqrt(worst), 0.85, 3.0);

            var arrivalState = new ShipState(arrivalJd,
                arrival.ParkingPosition, arrival.ParkingVelocity,
                ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2, arcs, ship, arrivalState);

            return new BuildResult(plan, requiredSeconds);
        }

        /// <summary>Arrival geometry at one instant: the sphere-of-influence
        /// entry and the parking-orbit state the plan must finish in.</summary>
        private ArrivalGeometry ComputeArrival(BodyRuntime target, ShipState ship, double arrivalJd,
            double targetSoi, bool insideTarget)
        {
            _model.ComputeState(target, arrivalJd,
                out Vector3d targetPosition, out Vector3d targetVelocity);

            Vector3d approach = (ship.PositionM - targetPosition).Normalized;
            if (approach.SqrMagnitude < 0.5) approach = new Vector3d(1.0, 0.0, 0.0);

            Vector3d entryPosition = targetPosition + approach * targetSoi;
            Vector3d entryVelocity = targetVelocity - approach * EntrySpeedMps;

            Vector3d phaseDirection = insideTarget
                ? ship.PositionM - targetPosition
                : entryPosition - targetPosition;
            double phase = ParkingOrbit.PhaseOfDirection(target, phaseDirection)
                           + ParkingPhaseOffsetRad;

            ParkingOrbit.StateAt(target, targetPosition, targetVelocity, phase, MaxAccelerationMps2,
                out Vector3d parkingPosition, out Vector3d parkingVelocity);

            if (insideTarget)
            {
                entryPosition = ship.PositionM;
                entryVelocity = ship.VelocityMps;
            }

            return new ArrivalGeometry(entryPosition, entryVelocity, parkingPosition, parkingVelocity);
        }

        private PlanArc MakeArc(FlightPhase phase, string bodyId, double startJd, double endJd,
            Vector3d startPosition, Vector3d startVelocity,
            Vector3d endPosition, Vector3d endVelocity)
        {
            _gravity.Prepare(startPosition, startJd);
            Vector3d startGravity = _gravity.AccelerationAt(startPosition, startJd);

            _gravity.Prepare(endPosition, endJd);
            Vector3d endGravity = _gravity.AccelerationAt(endPosition, endJd);

            double durationSeconds = (endJd - startJd) * SecondsPerDay;
            Quintic path = Quintic.Create(startPosition, startVelocity, startGravity,
                endPosition, endVelocity, endGravity, durationSeconds);

            var arc = new PlanArc(phase, bodyId, startJd, endJd, path);
            Measure(arc);
            return arc;
        }

        private void Measure(PlanArc arc)
        {
            int samples = Math.Max(8, ArcSamples);
            double peak = 0.0;
            double deltaV = 0.0;
            double clearance = double.PositiveInfinity;
            string nearest = null;
            double previousMagnitude = 0.0;
            double stepSeconds = arc.DurationSeconds / samples;

            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                Vector3d position = arc.Path.PositionAt(u);
                Vector3d required = arc.Path.AccelerationAt(u);
                double julianDate = arc.StartJulianDate + u * (arc.EndJulianDate - arc.StartJulianDate);

                _gravity.Prepare(position, julianDate);
                Vector3d localGravity = _gravity.AccelerationAt(position, julianDate);
                double magnitude = (required - localGravity).Magnitude;

                if (magnitude > peak) peak = magnitude;
                if (_gravity.NearestClearanceM < clearance)
                {
                    clearance = _gravity.NearestClearanceM;
                    nearest = _gravity.NearestBodyId;
                }

                if (i > 0) deltaV += 0.5 * (previousMagnitude + magnitude) * stepSeconds;
                previousMagnitude = magnitude;
            }

            arc.PeakThrustMps2 = peak;
            arc.DeltaVMps = deltaV;
            arc.MinimumClearanceM = clearance;
            arc.NearestBodyId = nearest;
        }

        private double BaseDuration(double distanceM)
        {
            double distance = Math.Max(distanceM, 1.0);
            double kinematic = 2.0 * Math.Sqrt(distance / MaxAccelerationMps2);
            return Math.Max(kinematic, MinimumArcSeconds);
        }

        private readonly struct ArrivalGeometry
        {
            public ArrivalGeometry(Vector3d entryPosition, Vector3d entryVelocity,
                Vector3d parkingPosition, Vector3d parkingVelocity)
            {
                EntryPosition = entryPosition;
                EntryVelocity = entryVelocity;
                ParkingPosition = parkingPosition;
                ParkingVelocity = parkingVelocity;
            }

            public Vector3d EntryPosition { get; }
            public Vector3d EntryVelocity { get; }
            public Vector3d ParkingPosition { get; }
            public Vector3d ParkingVelocity { get; }
        }

        private readonly struct BuildResult
        {
            public BuildResult(TrajectoryPlan plan, double requiredSeconds)
            {
                Plan = plan;
                RequiredSeconds = requiredSeconds;
            }

            public TrajectoryPlan Plan { get; }
            public double RequiredSeconds { get; }
        }
    }
}
