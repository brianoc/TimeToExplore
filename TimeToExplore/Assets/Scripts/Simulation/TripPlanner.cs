using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>How the planner brings the ship into the parking orbit.</summary>
    public enum CaptureStyle
    {
        /// <summary>The continuous-thrust quintic capture. The working default:
        /// fast, and constrained by the parking orbit's gravity ceiling.</summary>
        Quintic,
        /// <summary>A Keplerian inbound hyperbola, an impulsive periapsis
        /// capture, a transfer ellipse, and a circularisation burn. Slower in
        /// flight time, much cheaper in delta-v, and free of the ceiling.</summary>
        Conic,
        /// <summary>The same coasted hyperbola and transfer ellipse, but the
        /// long capture is a single finite burn at the player's chosen
        /// acceleration (<see cref="BurnLevelG"/>). A terminal impulse trims the
        /// burn's end state onto the exact transfer ellipse, and the
        /// circularisation is the conic style's impulse. At zero level it
        /// degenerates to <see cref="Conic"/>.</summary>
        FiniteBurn
    }

    /// <summary>How the planner crosses between the spheres of influence.</summary>
    public enum CruiseStyle
    {
        /// <summary>A continuously powered quintic. The working default: it is
        /// fast, and it is what every existing recipe emits.</summary>
        Powered,
        /// <summary>A zero-revolution prograde Lambert coast around the Sun,
        /// chosen to arrive on time and handed off at both ends. The departure
        /// quintic is retargeted so its endpoint velocity is the Lambert
        /// departure velocity, and the arrival is the conic or hybrid capture
        /// chain; the ship coasts with the drive off between them
        /// ([Burn–coast–burn trips](../simulation/burn-coast-burn.md)).</summary>
        Coast
    }

    /// <summary>
    /// Builds a Go To plan: an escape arc out of the departure body's sphere of
    /// influence, a heliocentric cruise arc, and a capture that ends on the
    /// target's parking orbit. The default capture is a quintic that matches its
    /// neighbours in position, velocity, and acceleration. The alternative
    /// conic capture is a coasting hyperbola and transfer ellipse with
    /// impulsive burns, which parks lower and spends less delta-v but takes
    /// months from the outer spheres of influence
    /// ([ADR-014](../decisions/adr-014-keplerian-coast.md)).
    ///
    /// The solver is a search, not an optimal-control solve. It allocates time
    /// between the arcs in proportion to each arc's kinematic minimum
    /// 2√(d/a), then grows the whole plan until the measured peak thrust fits
    /// the drive. The result is a smooth trajectory 20-40% slower than the
    /// bang-bang optimum, which is the right trade for a first cut: it is
    /// deterministic, always feasible for the inner system, and its endpoint is
    /// exact.
    ///
    /// The quintic capture has a known limit: it cannot thread a gravity well
    /// whose parking-orbit gravity approaches the drive's own acceleration,
    /// because the curve must arrive with that acceleration. Jupiter's
    /// surface-radius orbit sits at 19 m/s², so the default path parks at the
    /// gravity ceiling. The conic capture removes that limit by coasting; it
    /// parks lower and spends less delta-v, at the cost of time. See the
    /// navigation document.
    ///
    /// <see cref="CruiseStyle"/> chooses how the ship crosses: the default
    /// powered quintic, or a zero-revolution Lambert conic with the drive off.
    /// The coast style reuses the conic or hybrid capture chain and replaces
    /// the powered cruise with a heliocentric conic, so its cost is the break
    /// out of the departure well, the arrival handover, and the capture, and
    /// its time is chosen rather than minimised
    /// ([Burn–coast–burn trips](../simulation/burn-coast-burn.md)).
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

        /// <summary>Inbound speed at the target's sphere of influence. For the
        /// quintic it shapes the deceleration; for the conic it is the
        /// hyperbolic excess speed of the arrival hyperbola.</summary>
        public double EntrySpeedMps { get; set; } = 5_000.0;

        /// <summary>Outbound speed at the departure body's sphere of influence.
        /// It shapes the powered departure only: <see cref="CruiseStyle.Coast"/>
        /// takes its departure velocity from the Lambert solve instead, so this
        /// placeholder never drives the coast.</summary>
        public double EscapeSpeedMps { get; set; } = 1_000.0;

        /// <summary>Points sampled per arc to measure peak thrust and clearance.</summary>
        public int ArcSamples { get; set; } = 64;

        public int MaxIterations { get; set; } = 16;

        /// <summary>Relative flight-time error that ends the moving-target loop.</summary>
        public double ConvergenceTolerance { get; set; } = 0.005;

        /// <summary>Rotates the arrival point around the target's equator.</summary>
        public double ParkingPhaseOffsetRad { get; set; }

        /// <summary>Which arrival model the planner builds. The quintic capture
        /// is the working default; the conic capture is the cheap, slow
        /// alternative and is verified end to end by the suite. Under
        /// <see cref="CruiseStyle.Coast"/> the quintic capture is not available:
        /// the capture is always conic, or hybrid for
        /// <see cref="CaptureStyle.FiniteBurn"/>.</summary>
        public CaptureStyle ArrivalStyle { get; set; } = CaptureStyle.Quintic;

        /// <summary>Which cruise the planner builds. <see cref="CruiseStyle.Powered"/>
        /// is the default and reproduces every existing recipe; the
        /// <see cref="CruiseStyle.Coast"/> value sends the ship on a heliocentric
        /// Lambert conic with the drive off.</summary>
        public CruiseStyle CruiseStyle { get; set; } = CruiseStyle.Powered;

        /// <summary>Design acceleration of a fixed-thrust burn. 0.3 g is the
        /// working level: low enough to be a real burn rather than an impulse,
        /// high enough that the capture still fits inside a low orbit. The
        /// player-facing level is <see cref="BurnLevelG"/>; this is the default
        /// before the drive's own limit is applied.</summary>
        public const double DefaultBurnLevelG = 0.3;

        /// <summary>Standard gravity, the scale of the player's burn-level
        /// control. 1 g is 9.80665 m/s².</summary>
        public const double StandardGravityMps2 = 9.80665;

        /// <summary>Highest burn level the player can choose.</summary>
        public const double MaxBurnLevelG = 1.0;

        private double _burnLevelG = DefaultBurnLevelG;

        /// <summary>Player-facing fixed-burn level in g, from 0 to
        /// <see cref="MaxBurnLevelG"/>. Zero selects the pure conic arrival, so
        /// the style has no finite-burn arc.</summary>
        public double BurnLevelG
        {
            get => _burnLevelG;
            set => _burnLevelG = Math.Clamp(value, 0.0, MaxBurnLevelG);
        }

        /// <summary>Acceleration the <see cref="CaptureStyle.FiniteBurn"/> burn
        /// commands: the chosen level, clamped to the drive's own thrust
        /// margin. Zero when the level is zero.</summary>
        public double FixedBurnAccelerationMps2 =>
            Math.Min(BurnLevelG * StandardGravityMps2, MaxAccelerationMps2 * ThrustMargin);

        /// <summary>True when the plan ends on the conic capture chain rather
        /// than the quintic capture: every non-quintic arrival, and every coast
        /// cruise, which cannot feed a quintic capture.</summary>
        private bool UsesConicCapture =>
            ArrivalStyle == CaptureStyle.Conic
            || ArrivalStyle == CaptureStyle.FiniteBurn
            || CruiseStyle == CruiseStyle.Coast;

        /// <summary>Optional diagnostic sink for the planning loop.</summary>
        public Action<string> Trace { get; set; }

        public TrajectoryPlan Plan(ShipState ship, string targetId)
        {
            BodyRuntime target = RequireTarget(targetId);

            _model.ComputeState(target, ship.JulianDate, out Vector3d targetPosition, out _);
            double parkingRadius = UsesConicCapture
                ? ParkingOrbit.ConicRadiusM(target)
                : ParkingOrbit.RadiusM(target, MaxAccelerationMps2);
            double distance = Math.Max(Vector3d.Distance(targetPosition, ship.PositionM),
                parkingRadius);

            double total = 1.1 * 2.0 * Math.Sqrt(distance / MaxAccelerationMps2);
            if (UsesConicCapture)
                total += EstimateConicCaptureSeconds(target);
            TrajectoryPlan plan = null;
            double required = total;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                BuildResult result = Build(ship, target, total);
                plan = result.Plan;
                required = result.RequiredSeconds;

                if (!string.IsNullOrEmpty(plan.InfeasibleReason))
                    return plan;

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
            if (CruiseStyle == CruiseStyle.Coast)
                return BuildCoast(ship, target, totalSeconds);

            return ArrivalStyle == CaptureStyle.Conic || ArrivalStyle == CaptureStyle.FiniteBurn
                ? BuildConic(ship, target, totalSeconds)
                : BuildQuintic(ship, target, totalSeconds);
        }

        private BuildResult BuildQuintic(ShipState ship, BodyRuntime target, double totalSeconds)
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

        /// <summary>
        /// Build the cheap arrival: a powered departure and cruise, then a
        /// planetocentric hyperbola, an impulsive periapsis capture, a transfer
        /// ellipse, and a circularisation burn. The capture's duration is fixed
        /// by the target's gravity and the chosen radii, so the cruise absorbs
        /// the requested time; a request shorter than the capture plus the
        /// kinematic floor comes out longer rather than inventing a faster
        /// conic. A trip that starts inside the target's sphere of influence has
        /// no entry hyperbola and keeps the quintic capture.
        /// </summary>
        private BuildResult BuildConic(ShipState ship, BodyRuntime target, double totalSeconds)
        {
            double t0 = ship.JulianDate;
            double targetSoi = SphereOfInfluence.RadiusM(target);

            _model.ComputeState(target, t0, out Vector3d targetAtDeparture, out _);
            bool insideTarget = Vector3d.Distance(ship.PositionM, targetAtDeparture) < targetSoi;
            if (insideTarget) return BuildQuintic(ship, target, totalSeconds);

            BodyRuntime departureBody = null;
            double departureSoi = 0.0;
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

            Vector3d escapeDirection = Vector3d.Zero;
            Vector3d provisionalExit = ship.PositionM;
            double escapeBase = 0.0;
            if (departureBody != null)
            {
                _model.ComputeState(departureBody, t0,
                    out Vector3d anchorPosition, out Vector3d anchorVelocity);

                escapeDirection = (ship.VelocityMps - anchorVelocity).Normalized;
                if (escapeDirection.SqrMagnitude < 0.5)
                    escapeDirection = (ship.PositionM - anchorPosition).Normalized;
                if (escapeDirection.SqrMagnitude < 0.5)
                    escapeDirection = new Vector3d(1.0, 0.0, 0.0);

                provisionalExit = anchorPosition + escapeDirection * departureSoi;
                escapeBase = BaseDuration(Vector3d.Distance(ship.PositionM, provisionalExit));
            }

            double arrivalJd = t0 + totalSeconds / SecondsPerDay;
            double captureSeconds = 0.0;
            double escapeSeconds = 0.0;
            double cruiseSeconds = 0.0;
            TransferConic chain = null;

            for (int pass = 0; pass < 5; pass++)
            {
                chain = BuildCaptureShape(ship, target, targetSoi, arrivalJd);
                if (!chain.IsValid)
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "conic capture: " + chain.InvalidReason);

                captureSeconds = chain.TotalSeconds;
                double entryJd = arrivalJd - captureSeconds / SecondsPerDay;
                _model.ComputeState(target, entryJd,
                    out Vector3d targetAtEntry, out Vector3d targetAtEntryVelocity);
                Vector3d entryPosition = targetAtEntry + chain.EntryLocalPositionM;

                double available = totalSeconds - captureSeconds;
                if (departureBody != null)
                {
                    double cruiseBase = BaseDuration(
                        Vector3d.Distance(provisionalExit, entryPosition));
                    double scale = Math.Max(1.0, available / (escapeBase + cruiseBase));
                    escapeSeconds = escapeBase * scale;
                    cruiseSeconds = cruiseBase * scale;
                }
                else
                {
                    double cruiseBase = BaseDuration(
                        Vector3d.Distance(ship.PositionM, entryPosition));
                    double scale = Math.Max(1.0, available / cruiseBase);
                    escapeSeconds = 0.0;
                    cruiseSeconds = cruiseBase * scale;
                }

                double nextArrival = t0 + (escapeSeconds + cruiseSeconds + captureSeconds)
                                        / SecondsPerDay;
                if (Math.Abs(nextArrival - arrivalJd) <= 1e-8) break;
                arrivalJd = nextArrival;
            }

            // Rebuild the shape at the settled arrival, so the plane and the
            // approach azimuth belong to the date the plan actually reaches.
            chain = BuildCaptureShape(ship, target, targetSoi, arrivalJd);
            if (!chain.IsValid)
                return InfeasiblePlan(ship, target, totalSeconds,
                    "conic capture: " + chain.InvalidReason);

            captureSeconds = chain.TotalSeconds;
            double cruiseEndJd = arrivalJd - captureSeconds / SecondsPerDay;
            double escapeEndJd = t0 + escapeSeconds / SecondsPerDay;
            if (departureBody != null)
                cruiseEndJd = Math.Max(cruiseEndJd, escapeEndJd + MinimumArcSeconds / SecondsPerDay);
            cruiseSeconds = (cruiseEndJd - escapeEndJd) * SecondsPerDay;

            Vector3d cruiseStartPosition = ship.PositionM;
            Vector3d cruiseStartVelocity = ship.VelocityMps;
            if (departureBody != null)
            {
                _model.ComputeState(departureBody, escapeEndJd,
                    out Vector3d anchorPosition, out Vector3d anchorVelocity);
                cruiseStartPosition = anchorPosition + escapeDirection * departureSoi;
                cruiseStartVelocity = anchorVelocity + escapeDirection * EscapeSpeedMps;
            }

            _model.ComputeState(target, cruiseEndJd,
                out Vector3d targetAtEntryFinal, out Vector3d targetAtEntryVelocityFinal);
            Vector3d entryPositionFinal = targetAtEntryFinal + chain.EntryLocalPositionM;
            Vector3d entryVelocityFinal = targetAtEntryVelocityFinal + chain.EntryLocalVelocityMps;
            Vector3d entryAcceleration = _model.ComputeAcceleration(target, cruiseEndJd)
                                          + chain.EntryLocalAccelerationMps2;

            var arcs = new List<PlanArc>(6);

            if (departureBody != null && escapeSeconds > MinimumArcSeconds)
            {
                arcs.Add(MakeArc(FlightPhase.Departure, departureBody.Id, t0, escapeEndJd,
                    ship.PositionM, ship.VelocityMps, cruiseStartPosition, cruiseStartVelocity));
            }

            if (cruiseSeconds > MinimumArcSeconds)
            {
                arcs.Add(MakeArc(FlightPhase.Cruise, target.Id, escapeEndJd, cruiseEndJd,
                    cruiseStartPosition, cruiseStartVelocity,
                    entryPositionFinal, entryVelocityFinal,
                    endAcceleration: entryAcceleration));
            }

            double parkingEndJd;
            ConicArc parking;
            double finiteBurnAcceleration = 0.0;

            if (ArrivalStyle == CaptureStyle.FiniteBurn
                && FixedBurnAccelerationMps2 > 0.0)
            {
                if (!AppendHybridCapture(arcs, target, cruiseEndJd, chain, Vector3d.Zero,
                        out parkingEndJd, out parking, out string reason,
                        out finiteBurnAcceleration))
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "hybrid capture: " + reason);
                }
            }
            else
            {
                AppendConicCapture(arcs, target, cruiseEndJd, chain, Vector3d.Zero,
                    out parkingEndJd, out parking);
            }

            double limit = MaxAccelerationMps2 * ThrustMargin;
            double worst = 1.0;
            foreach (PlanArc arc in arcs)
            {
                if (arc.PeakThrustMps2 <= 0.0) continue;
                worst = Math.Max(worst, arc.PeakThrustMps2 / limit);
            }

            double actualSeconds = (parkingEndJd - t0) * SecondsPerDay;
            double requiredSeconds = Math.Max(totalSeconds, actualSeconds)
                                     * Math.Clamp(Math.Sqrt(worst), 0.85, 3.0);

            Vector3d parkingPosition = parking.PositionAt(1.0);
            Vector3d parkingVelocity = parking.VelocityAt(1.0);
            var arrivalState = new ShipState(parkingEndJd,
                parkingPosition, parkingVelocity, ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2, arcs, ship, arrivalState);
            plan.FiniteBurnAccelerationMps2 = finiteBurnAcceleration;

            return new BuildResult(plan, requiredSeconds);
        }

        /// <summary>
        /// Build the coast cruise: a quintic break out of the departure sphere
        /// of influence retargeted so its endpoint velocity is the Lambert
        /// departure velocity, a heliocentric zero-revolution conic coast about
        /// the Sun, and the conic or hybrid capture chain. The capture geometry
        /// sets the entry point and the coast's time of flight; the solver then
        /// chooses the coast that lands there.
        ///
        /// The departure takes a share of the trip in proportion to its
        /// kinematic floor, because the Lambert break velocity is a real
        /// velocity change that a fixed few-hour escape cannot supply; whatever
        /// is left after the capture and the departure is coast. A trip time
        /// too short to leave the coast a phase comes out longer than asked
        /// rather than inventing a faster conic, the same rule as the conic
        /// arrival.
        ///
        /// Only one branch exists: the prograde, short-way, zero-revolution
        /// transfer. A date whose transfer angle is 180° or more, or whose
        /// transfer plane contains the ecliptic pole, has no coast. That is an
        /// infeasible trip time, reported with the geometry that rejected it,
        /// not a planner fault and not a long-way fallback.
        /// </summary>
        private BuildResult BuildCoast(ShipState ship, BodyRuntime target, double totalSeconds)
        {
            double t0 = ship.JulianDate;
            double targetSoi = SphereOfInfluence.RadiusM(target);

            _model.ComputeState(target, t0, out Vector3d targetAtDeparture, out _);
            bool insideTarget = Vector3d.Distance(ship.PositionM, targetAtDeparture) < targetSoi;
            if (insideTarget) return BuildQuintic(ship, target, totalSeconds);

            BodyRuntime departureBody = null;
            double departureSoi = 0.0;
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

            Vector3d escapeDirection = Vector3d.Zero;
            Vector3d provisionalExit = ship.PositionM;
            double escapeBase = 0.0;
            if (departureBody != null)
            {
                _model.ComputeState(departureBody, t0,
                    out Vector3d anchorPosition, out Vector3d anchorVelocity);

                escapeDirection = (ship.VelocityMps - anchorVelocity).Normalized;
                if (escapeDirection.SqrMagnitude < 0.5)
                    escapeDirection = (ship.PositionM - anchorPosition).Normalized;
                if (escapeDirection.SqrMagnitude < 0.5)
                    escapeDirection = new Vector3d(1.0, 0.0, 0.0);

                provisionalExit = anchorPosition + escapeDirection * departureSoi;
                escapeBase = BaseDuration(Vector3d.Distance(ship.PositionM, provisionalExit));
            }

            double arrivalJd = t0 + totalSeconds / SecondsPerDay;
            double captureSeconds = 0.0;
            double escapeSeconds = 0.0;
            double cruiseSeconds = 0.0;
            TransferConic chain = null;

            // Same five-pass arrival loop as the conic capture: the capture
            // shape fixes the entry date, the departure and the coast split the
            // time that is left in proportion to their kinematic floors, and
            // the arrival date is re-derived until it settles. The departure
            // needs the share because the coast asks for a real break velocity,
            // which a fixed few-hour escape cannot supply.
            for (int pass = 0; pass < 5; pass++)
            {
                chain = BuildCaptureShape(ship, target, targetSoi, arrivalJd);
                if (!chain.IsValid)
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "conic capture: " + chain.InvalidReason);

                captureSeconds = chain.TotalSeconds;
                double passEntryJd = arrivalJd - captureSeconds / SecondsPerDay;
                _model.ComputeState(target, passEntryJd,
                    out Vector3d passTargetAtEntry, out _);
                Vector3d passEntryPosition = passTargetAtEntry + chain.EntryLocalPositionM;

                double available = totalSeconds - captureSeconds;
                if (departureBody != null)
                {
                    double cruiseBase = BaseDuration(
                        Vector3d.Distance(provisionalExit, passEntryPosition));
                    double scale = Math.Max(1.0, available / (escapeBase + cruiseBase));
                    escapeSeconds = escapeBase * scale;
                    cruiseSeconds = cruiseBase * scale;
                }
                else
                {
                    escapeSeconds = 0.0;
                    cruiseSeconds = Math.Max(available, MinimumArcSeconds);
                }

                double nextArrival = t0
                                     + (escapeSeconds + cruiseSeconds + captureSeconds)
                                     / SecondsPerDay;
                if (Math.Abs(nextArrival - arrivalJd) <= 1e-8) break;
                arrivalJd = nextArrival;
            }

            // Rebuild the shape at the settled arrival, so the plane and the
            // approach azimuth belong to the date the plan actually reaches.
            chain = BuildCaptureShape(ship, target, targetSoi, arrivalJd);
            if (!chain.IsValid)
                return InfeasiblePlan(ship, target, totalSeconds,
                    "conic capture: " + chain.InvalidReason);

            captureSeconds = chain.TotalSeconds;
            double entryJd = arrivalJd - captureSeconds / SecondsPerDay;
            _model.ComputeState(target, entryJd,
                out Vector3d targetAtEntry, out _);
            Vector3d entryPosition = targetAtEntry + chain.EntryLocalPositionM;

            double escapeEndJd = t0 + escapeSeconds / SecondsPerDay;
            Vector3d exitPosition = ship.PositionM;
            if (departureBody != null)
            {
                _model.ComputeState(departureBody, escapeEndJd,
                    out Vector3d anchorPosition, out _);
                exitPosition = anchorPosition + escapeDirection * departureSoi;
            }

            double cruiseEndJd = entryJd;
            cruiseSeconds = (cruiseEndJd - escapeEndJd) * SecondsPerDay;
            if (!(cruiseSeconds > MinimumArcSeconds))
                return InfeasiblePlan(ship, target, totalSeconds,
                    "coast: the trip time leaves no room for the transfer");

            LambertSolution coast = LambertSolver.SolveShortWay(
                exitPosition, entryPosition, cruiseSeconds, _model.Sun.Gm);
            if (!coast.HasSolution)
                return InfeasiblePlan(ship, target, totalSeconds, CoastReason(coast));

            _model.ComputeState(_model.Sun, escapeEndJd,
                out Vector3d sunAtEscape, out Vector3d sunVelocityAtEscape);
            ConicArc heliocentricCoast = ConicArc.Create(_model, _model.Sun,
                escapeEndJd, cruiseSeconds,
                exitPosition - sunAtEscape, coast.DepartureVelocityMps - sunVelocityAtEscape);

            // The coast starts where the departure quintic ends, so the ship
            // never moves at t0. Without a departure body there is no arc to
            // break out with, and the velocity change is an impulsive start.
            var arcs = new List<PlanArc>(6);
            Vector3d startImpulse = Vector3d.Zero;
            if (departureBody != null)
            {
                arcs.Add(MakeArc(FlightPhase.Departure, departureBody.Id, t0, escapeEndJd,
                    ship.PositionM, ship.VelocityMps, exitPosition,
                    coast.DepartureVelocityMps,
                    endAcceleration: heliocentricCoast.AccelerationAt(0.0)));
            }
            else
            {
                startImpulse = coast.DepartureVelocityMps - ship.VelocityMps;
            }

            arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, escapeEndJd, cruiseEndJd,
                heliocentricCoast, startImpulse));

            // The coast arrives with the Lambert arrival velocity, which need
            // not equal the capture chain's entry velocity. The trim is the
            // arrival handover impulse: stage B replaces it with the brake burn.
            _model.ComputeState(target, cruiseEndJd,
                out _, out Vector3d targetAtEntryFinal);
            Vector3d chainEntryVelocity = targetAtEntryFinal + chain.EntryLocalVelocityMps;
            Vector3d arrivalTrim = chainEntryVelocity - heliocentricCoast.VelocityAt(1.0);

            double parkingEndJd;
            ConicArc parking;
            double finiteBurnAcceleration = 0.0;

            if (ArrivalStyle == CaptureStyle.FiniteBurn
                && FixedBurnAccelerationMps2 > 0.0)
            {
                if (!AppendHybridCapture(arcs, target, cruiseEndJd, chain, arrivalTrim,
                        out parkingEndJd, out parking, out string reason,
                        out finiteBurnAcceleration))
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "hybrid capture: " + reason);
                }
            }
            else
            {
                AppendConicCapture(arcs, target, cruiseEndJd, chain, arrivalTrim,
                    out parkingEndJd, out parking);
            }

            double limit = MaxAccelerationMps2 * ThrustMargin;
            double worst = 1.0;
            foreach (PlanArc arc in arcs)
            {
                if (arc.PeakThrustMps2 <= 0.0) continue;
                worst = Math.Max(worst, arc.PeakThrustMps2 / limit);
            }

            double actualSeconds = (parkingEndJd - t0) * SecondsPerDay;
            double requiredSeconds = Math.Max(totalSeconds, actualSeconds)
                                     * Math.Clamp(Math.Sqrt(worst), 0.85, 3.0);

            Vector3d parkingPosition = parking.PositionAt(1.0);
            Vector3d parkingVelocity = parking.VelocityAt(1.0);
            var arrivalState = new ShipState(parkingEndJd,
                parkingPosition, parkingVelocity, ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2, arcs, ship, arrivalState);
            plan.FiniteBurnAccelerationMps2 = finiteBurnAcceleration;

            return new BuildResult(plan, requiredSeconds);
        }

        /// <summary>Why a Lambert coast is unavailable, with the geometry that
        /// rejected it. The transfer angle is reported even on a rejection.</summary>
        private static string CoastReason(LambertSolution coast) =>
            $"coast: no prograde short-way Lambert transfer at this trip time " +
            $"(transfer angle {coast.TransferAngleRad * 180.0 / Math.PI:F1} deg); " +
            $"the coast needs a transfer short of 180 deg";

        /// <summary>
        /// Append the conic capture: the inbound hyperbola from the sphere of
        /// influence to periapsis, the impulsive capture onto the transfer
        /// ellipse, and the transfer and circularisation coasts. The inbound arc
        /// starts at the chain's entry state at
        /// <paramref name="cruiseEndJd"/>; <paramref name="inboundImpulseMps"/>
        /// is the velocity change the preceding arc needs to hand over exactly,
        /// and is zero when that arc already ends on the chain's entry velocity.
        /// </summary>
        private void AppendConicCapture(List<PlanArc> arcs, BodyRuntime target,
            double cruiseEndJd, TransferConic chain, Vector3d inboundImpulseMps,
            out double parkingEndJd, out ConicArc parking)
        {
            double periapsisJd = cruiseEndJd + chain.InboundSeconds / SecondsPerDay;
            double apoapsisJd = periapsisJd + chain.TransferSeconds / SecondsPerDay;
            parkingEndJd = apoapsisJd + chain.ParkingSeconds / SecondsPerDay;

            ConicArc inbound = ConicArc.Create(_model, target, cruiseEndJd,
                chain.InboundSeconds, chain.EntryLocalPositionM, chain.EntryLocalVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Coast, target.Id, cruiseEndJd, periapsisJd,
                inbound, inboundImpulseMps));

            ConicArc transfer = ConicArc.Create(_model, target, periapsisJd,
                chain.TransferSeconds, chain.PeriapsisLocalPositionM,
                chain.TransferPeriapsisVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Capture, target.Id, periapsisJd, apoapsisJd,
                transfer, chain.CaptureBurnVectorMps));

            parking = ConicArc.Create(_model, target, apoapsisJd,
                chain.ParkingSeconds, chain.ApoapsisLocalPositionM,
                chain.CircularApoapsisVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Parking, target.Id, apoapsisJd, parkingEndJd,
                parking, chain.CircularisationBurnVectorMps));
        }

        /// <summary>
        /// Build the hybrid arrival: a coast to the burn start, one retrograde
        /// finite burn to the injection radius, the terminal impulse that puts
        /// the ship on the exact transfer ellipse, the transfer coast, and the
        /// conic circularisation and parking coast. The start offset and burn
        /// duration come from <see cref="HybridCapture"/>; the transfer is then
        /// rebuilt from the plan's own burn end, so the seams are exact.
        /// </summary>
        private bool AppendHybridCapture(List<PlanArc> arcs, BodyRuntime target,
            double cruiseEndJd, TransferConic chain, Vector3d inboundImpulseMps,
            out double parkingEndJd, out ConicArc parking, out string reason,
            out double accelerationMps2)
        {
            parkingEndJd = cruiseEndJd;
            parking = null;
            reason = null;

            accelerationMps2 = FixedBurnAccelerationMps2;
            if (!(accelerationMps2 > 0.0))
            {
                reason = "the drive has no thrust margin for a fixed burn";
                return false;
            }

            double parkingRadius = ParkingOrbit.ConicRadiusM(target);
            double periapsisRadius = ParkingOrbit.PeriapsisRadiusM(target);
            if (!(parkingRadius > periapsisRadius))
            {
                reason = "the parking radius is below the capture periapsis";
                return false;
            }

            ConicArc fullInbound = ConicArc.Create(_model, target, cruiseEndJd,
                chain.InboundSeconds, chain.EntryLocalPositionM,
                chain.EntryLocalVelocityMps);

            HybridCapture capture = HybridCapture.Build(target, fullInbound,
                parkingRadius, periapsisRadius, accelerationMps2);
            if (!capture.IsValid)
            {
                reason = capture.InvalidReason;
                return false;
            }

            double inboundCoastSeconds = capture.InboundSeconds + capture.StartOffsetSeconds;
            if (!(inboundCoastSeconds > MinimumArcSeconds))
            {
                reason = "the fixed burn would start inside the inbound entry";
                return false;
            }

            double burnStartJd = cruiseEndJd + inboundCoastSeconds / SecondsPerDay;
            double burnEndJd = burnStartJd + capture.BurnSeconds / SecondsPerDay;

            ConicArc inbound = ConicArc.Create(_model, target, cruiseEndJd,
                inboundCoastSeconds, chain.EntryLocalPositionM,
                chain.EntryLocalVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Coast, target.Id, cruiseEndJd, burnStartJd,
                inbound, inboundImpulseMps));

            FiniteBurnArc captureBurn = FiniteBurnArc.Create(_model, target, burnStartJd,
                capture.BurnSeconds, capture.BurnStartLocalPositionM,
                capture.BurnStartLocalVelocityMps, accelerationMps2, retrograde: true);
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Capture, target.Id, burnStartJd,
                burnEndJd, captureBurn));

            // Recompute the trim from the plan's own burn end, not the solver's
            // sample, so the transfer actually starts where the flown arc ends.
            if (!HybridCapture.TryTrim(target, captureBurn.EndLocalPositionM,
                    captureBurn.EndLocalVelocityMps, parkingRadius,
                    out Vector3d trim, out Vector3d transferVelocity,
                    out double transferSeconds))
            {
                reason = "the fixed burn's end state has no transfer ellipse";
                return false;
            }

            if (!(transferSeconds > MinimumArcSeconds))
            {
                reason = "the fixed burn leaves no transfer coast to apoapsis";
                return false;
            }

            ConicArc transfer = ConicArc.Create(_model, target, burnEndJd,
                transferSeconds, captureBurn.EndLocalPositionM, transferVelocity);
            double transferEndJd = burnEndJd + transferSeconds / SecondsPerDay;
            arcs.Add(MakeConicArc(FlightPhase.Coast, target.Id, burnEndJd,
                transferEndJd, transfer, trim));

            // The transfer ends at its apoapsis; use the arc's own end state so
            // the circularisation impulse matches the drawn coast.
            transfer.LocalStateAt(1.0, out Vector3d apoapsisPositionFlown,
                out Vector3d apoapsisVelocityFlown);
            double apoapsisRadius = apoapsisPositionFlown.Magnitude;
            Vector3d circularVelocity = apoapsisVelocityFlown.Normalized
                                        * Math.Sqrt(target.Gm / apoapsisRadius);
            Vector3d circularisationFlown = circularVelocity - apoapsisVelocityFlown;

            double parkingSeconds = Math.Clamp(
                TwoBody.PeriodSeconds(target.Gm, apoapsisRadius) / 360.0, 1.0, 60.0);
            parking = ConicArc.Create(_model, target, transferEndJd,
                parkingSeconds, apoapsisPositionFlown, circularVelocity);
            parkingEndJd = transferEndJd + parkingSeconds / SecondsPerDay;
            arcs.Add(MakeConicArc(FlightPhase.Parking, target.Id, transferEndJd,
                parkingEndJd, parking, circularisationFlown));

            return true;
        }

        private double EstimateConicCaptureSeconds(BodyRuntime target)
        {
            TransferConic chain = TransferConic.Build(target, new Vector3d(1.0, 0.0, 0.0),
                SphereOfInfluence.RadiusM(target), EntrySpeedMps,
                ParkingOrbit.ConicRadiusM(target), ParkingOrbit.PeriapsisRadiusM(target));
            return chain.IsValid ? chain.TotalSeconds : 0.0;
        }

        private TransferConic BuildCaptureShape(ShipState ship, BodyRuntime target,
            double targetSoi, double arrivalJd)
        {
            _model.ComputeState(target, arrivalJd, out Vector3d targetPosition, out _);
            Vector3d approach = (ship.PositionM - targetPosition).Normalized;
            if (approach.SqrMagnitude < 0.5) approach = new Vector3d(1.0, 0.0, 0.0);

            return TransferConic.Build(target, approach, targetSoi, EntrySpeedMps,
                ParkingOrbit.ConicRadiusM(target), ParkingOrbit.PeriapsisRadiusM(target));
        }

        private BuildResult InfeasiblePlan(ShipState ship, BodyRuntime target,
            double totalSeconds, string reason)
        {
            double end = ship.JulianDate + MinimumArcSeconds / SecondsPerDay;
            PlanArc dummy = MakeArc(FlightPhase.Capture, target.Id, ship.JulianDate, end,
                ship.PositionM, ship.VelocityMps, ship.PositionM, ship.VelocityMps);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2,
                new List<PlanArc> { dummy }, ship, ship)
            {
                InfeasibleReason = reason
            };
            return new BuildResult(plan, totalSeconds);
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
            Vector3d endPosition, Vector3d endVelocity,
            Vector3d? startAcceleration = null, Vector3d? endAcceleration = null)
        {
            Vector3d startGravity;
            if (startAcceleration.HasValue)
            {
                startGravity = startAcceleration.Value;
            }
            else
            {
                _gravity.Prepare(startPosition, startJd);
                startGravity = _gravity.AccelerationAt(startPosition, startJd);
            }

            Vector3d endGravity;
            if (endAcceleration.HasValue)
            {
                endGravity = endAcceleration.Value;
            }
            else
            {
                _gravity.Prepare(endPosition, endJd);
                endGravity = _gravity.AccelerationAt(endPosition, endJd);
            }

            double durationSeconds = (endJd - startJd) * SecondsPerDay;
            Quintic path = Quintic.Create(startPosition, startVelocity, startGravity,
                endPosition, endVelocity, endGravity, durationSeconds);

            var arc = new PlanArc(phase, bodyId, startJd, endJd, path);
            Measure(arc);
            return arc;
        }

        private PlanArc MakeConicArc(FlightPhase phase, string bodyId, double startJd,
            double endJd, ConicArc path, Vector3d entryImpulseMps)
        {
            var arc = new PlanArc(phase, bodyId, startJd, endJd, path)
            {
                EntryImpulseVectorMps = entryImpulseMps
            };
            Measure(arc);
            return arc;
        }

        private PlanArc MakeFiniteBurnArc(FlightPhase phase, string bodyId,
            double startJd, double endJd, FiniteBurnArc path)
        {
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
            double previousSeconds = 0.0;

            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                Vector3d position = arc.Path.PositionAt(u);
                Vector3d required = arc.Path.AccelerationAt(u);
                double seconds = arc.Path.SecondsAt(u);
                double julianDate = arc.StartJulianDate + seconds / JulianDate.SecondsPerDay;

                _gravity.Prepare(position, julianDate);
                Vector3d localGravity = _gravity.AccelerationAt(position, julianDate);
                double magnitude = (required - localGravity).Magnitude;

                if (magnitude > peak) peak = magnitude;
                if (_gravity.NearestClearanceM < clearance)
                {
                    clearance = _gravity.NearestClearanceM;
                    nearest = _gravity.NearestBodyId;
                }

                if (i > 0)
                    deltaV += 0.5 * (previousMagnitude + magnitude)
                              * (seconds - previousSeconds);
                previousMagnitude = magnitude;
                previousSeconds = seconds;
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
