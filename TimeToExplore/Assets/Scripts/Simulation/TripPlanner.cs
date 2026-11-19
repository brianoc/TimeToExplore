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
        Coast,
        /// <summary>The full burn–coast–burn profile: a quintic break out of
        /// the departure sphere of influence, one prograde finite burn at the
        /// chosen acceleration (<see cref="BurnLevelG"/>), the Lambert coast,
        /// one retrograde finite brake onto the capture chain's entry state,
        /// and the conic or hybrid capture. At zero level it degenerates to
        /// <see cref="Coast"/> ([Burn–coast–burn trips](../simulation/burn-coast-burn.md)).</summary>
        BurnCoastBurn
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

        /// <summary>How far outside a departure sphere of influence the steady
        /// burn must begin, as a multiple of the boundary. The margin keeps the
        /// whole burn clear while the departure body moves under it.</summary>
        private const double DepartureBurnClearance = 1.02;

        /// <summary>Shortest coast the burn–coast–burn profile will leave between
        /// its two burns. A trip time that cannot fit one is stretched.</summary>
        private const double MinimumCoastSeconds = 3_600.0;

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
        /// Lambert conic with the drive off, and
        /// <see cref="CruiseStyle.BurnCoastBurn"/> bookends that coast with the
        /// departure burn and the arrival brake.</summary>
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
        /// or burn–coast–burn cruise, which cannot feed a quintic capture.</summary>
        private bool UsesConicCapture =>
            ArrivalStyle == CaptureStyle.Conic
            || ArrivalStyle == CaptureStyle.FiniteBurn
            || CruiseStyle == CruiseStyle.Coast
            || CruiseStyle == CruiseStyle.BurnCoastBurn;

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
                {
                    // A builder that knows the trip is too short says so with a
                    // longer requirement; grow the request before giving up on the
                    // geometry.
                    if (required > total)
                    {
                        total = 0.5 * (total + required);
                        continue;
                    }

                    return plan;
                }

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
            if (CruiseStyle == CruiseStyle.BurnCoastBurn)
                return BuildBurnCoastBurn(ship, target, totalSeconds);

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
        /// Build the burn–coast–burn cruise: the coast skeleton of
        /// <see cref="BuildCoast"/> with the two finite burns inserted. The
        /// departure burn starts on the departure body's clearance sphere and the
        /// brake ends on the capture chain's entry state; the brake is placed by
        /// integrating it **backwards** from the entry and the departure burn is
        /// opened out from its pinned start, so every seam is continuous in
        /// position and velocity and the arrival is exact.
        ///
        /// The break ends where the departure burn begins: on the clearance sphere
        /// <see cref="DepartureBurnClearance"/> times the departure sphere of
        /// influence, along the outbound escape ray, at the local escape speed
        /// along that ray. That is a design decision, not a derived state: the
        /// break's only job is to leave the parking orbit and reach escape, and the
        /// burn supplies everything above it
        /// ([Burn–coast–burn trips](../simulation/burn-coast-burn.md)). The ray is
        /// the outbound asymptote the crossing needs, not the ship's own relative
        /// velocity: a station's orbital direction has no relation to the transfer,
        /// and pinning the burn there cannot close. The break's duration is solved:
        /// it keeps the reference coast's departure share while that is gentle
        /// enough for the drive, and is lengthened until its peak thrust fits when
        /// the share is too short.
        ///
        /// The departure burn is the inverse of the old placement. The old chain
        /// solved the coast's start and integrated the burn backwards, so the burn's
        /// start landed wherever it landed (4.7 departure spheres at 8 days to
        /// Mercury). Here the start is pinned, the burn holds the escape ray, and
        /// its duration is solved with the coast: for a trial ray and duration the
        /// burn is integrated forward, the Lambert coast is solved from its end to
        /// the brake's start, and the mismatch between the burn's end velocity and
        /// the coast's departure velocity is driven to zero — three equations in
        /// the two ray angles and the duration. The burn holds the escape ray rather
        /// than following the heliocentric velocity because at the boundary the
        /// heliocentric velocity is the departure body's own orbital velocity; a
        /// velocity-following burn there points along the body's motion, not at the
        /// target. The escape ray is the burn's prograde direction in the departure
        /// body's frame.
        ///
        /// The brake spends the handover impulse the coast skeleton leaves at the
        /// target's sphere of influence. A burn along the instantaneous velocity
        /// can only change the speed, and the handover is not in general a pure
        /// speed change, so the brake's thrust direction is held fixed along the
        /// change the chain needs and the direction and duration are iterated until
        /// the burn begins exactly where the coast ends. The direction opposes the
        /// arrival velocity, which is what makes it a brake; a coast that arrives
        /// slower than the target's frame would need a prograde burn, and that trip
        /// time is rejected.
        /// </summary>
        private BuildResult BuildBurnCoastBurn(ShipState ship, BodyRuntime target,
            double totalSeconds)
        {
            double acceleration = FixedBurnAccelerationMps2;
            if (!(acceleration > 0.0))
                return BuildCoast(ship, target, totalSeconds);

            double t0 = ship.JulianDate;
            double targetSoi = SphereOfInfluence.RadiusM(target);
            int burnSamples = Math.Max(512, ArcSamples);
            int brakeSamples = FiniteBurnArc.MaximumSamples;

            _model.ComputeState(target, t0, out Vector3d targetAtDeparture, out _);
            if (Vector3d.Distance(ship.PositionM, targetAtDeparture) < targetSoi)
                return BuildQuintic(ship, target, totalSeconds);

            BodyRuntime departureBody = FindDepartureBody(ship, target, t0,
                out double departureSoi);
            if (departureBody == null)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the ship is not inside a departure sphere of " +
                    "influence to break out of");
            }

            // The coast skeleton supplies the capture chain, the handover the
            // brake spends, and a first guess at the outbound direction and the
            // burn's size.
            BuildResult reference = BuildCoast(ship, target, totalSeconds);
            if (!string.IsNullOrEmpty(reference.Plan.InfeasibleReason))
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: " + reference.Plan.InfeasibleReason);
            }

            PlanArc referenceCruise = null;
            foreach (PlanArc arc in reference.Plan.Arcs)
            {
                if (arc.Phase == FlightPhase.Cruise && arc.IsCoast)
                {
                    referenceCruise = arc;
                    break;
                }
            }

            if (referenceCruise == null)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the coast skeleton has no heliocentric arc");
            }

            // The capture chain is rebuilt at the reference coast's arrival, so the
            // two plans share one chain and one entry state.
            double entryJd = referenceCruise.EndJulianDate;
            TransferConic chain = BuildCaptureShape(ship, target, targetSoi, entryJd);
            if (!chain.IsValid)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "conic capture: " + chain.InvalidReason);
            }

            double arrivalJd = entryJd + chain.TotalSeconds / SecondsPerDay;
            chain = BuildCaptureShape(ship, target, targetSoi, arrivalJd);
            if (!chain.IsValid)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "conic capture: " + chain.InvalidReason);
            }

            double captureSeconds = chain.TotalSeconds;
            entryJd = arrivalJd - captureSeconds / SecondsPerDay;
            _model.ComputeState(target, entryJd,
                out Vector3d targetAtEntry, out Vector3d targetVelocityAtEntry);
            Vector3d entryPosition = targetAtEntry + chain.EntryLocalPositionM;
            Vector3d entryVelocity = targetVelocityAtEntry + chain.EntryLocalVelocityMps;

            double clearanceRadius = departureSoi * DepartureBurnClearance;
            double escapeSpeed = Math.Sqrt(2.0 * departureBody.Gm / clearanceRadius);

            _model.ComputeState(departureBody, referenceCruise.StartJulianDate,
                out _, out Vector3d departureAtExit);
            Vector3d escapeDirection =
                referenceCruise.Path.VelocityAt(0.0) - departureAtExit;
            if (escapeDirection.SqrMagnitude < 0.5)
            {
                _model.ComputeState(departureBody, t0,
                    out Vector3d departureAtStart, out _);
                escapeDirection = ship.PositionM - departureAtStart;
            }
            if (escapeDirection.SqrMagnitude < 0.5)
                escapeDirection = new Vector3d(1.0, 0.0, 0.0);
            escapeDirection = escapeDirection.Normalized;

            double burnSeconds = Math.Max(0.0,
                (referenceCruise.Path.VelocityAt(0.0) - departureAtExit).Magnitude
                - escapeSpeed) / acceleration;
            burnSeconds = Math.Max(burnSeconds, MinimumArcSeconds);

            double breakSeconds = Math.Max(
                (referenceCruise.StartJulianDate - t0) * SecondsPerDay,
                MinimumArcSeconds);

            double brakeSeconds = MinimumArcSeconds;
            Vector3d brakeDirection = -referenceCruise.Path.VelocityAt(1.0).Normalized;
            Vector3d brakeStartPosition = entryPosition;
            Vector3d brakeStartVelocity = entryVelocity;
            double brakeStartJd = entryJd - brakeSeconds / SecondsPerDay;

            Vector3d burnStartPosition = ship.PositionM;
            Vector3d burnStartVelocity = ship.VelocityMps;
            Vector3d coastStartPosition = Vector3d.Zero;
            Vector3d coastStartVelocity = Vector3d.Zero;
            Vector3d coastArrivalVelocity = Vector3d.Zero;
            double coastSeconds = 0.0;
            double burnStartJd = t0;
            double coastStartJd = t0;

            // The brake and the departure solve feed each other: the brake's start
            // is the coast's end, and the coast's start is the burn's end. Settle
            // both by repeating the reverse brake, the break's duration, and the
            // pinned departure solve.
            for (int pass = 0; pass < 12; pass++)
            {
                _model.ComputeState(_model.Sun, entryJd,
                    out Vector3d sunAtEntry, out Vector3d sunVelocityAtEntry);
                FiniteBurnArc.SimulateBackward(_model.Sun,
                    entryPosition - sunAtEntry, entryVelocity - sunVelocityAtEntry,
                    acceleration, retrograde: true, brakeSeconds, brakeSamples,
                    out Vector3d brakeLocalPosition, out Vector3d brakeLocalVelocity,
                    brakeDirection);
                brakeStartJd = entryJd - brakeSeconds / SecondsPerDay;
                _model.ComputeState(_model.Sun, brakeStartJd,
                    out Vector3d sunAtBrakeStart, out Vector3d sunVelocityAtBrakeStart);
                brakeStartPosition = sunAtBrakeStart + brakeLocalPosition;
                brakeStartVelocity = sunVelocityAtBrakeStart + brakeLocalVelocity;

                breakSeconds = SolveBreakSeconds(ship, departureBody, escapeDirection,
                    clearanceRadius, escapeSpeed, breakSeconds, brakeStartJd,
                    burnSeconds, brakeSeconds);

                burnStartJd = t0 + breakSeconds / SecondsPerDay;
                _model.ComputeState(departureBody, burnStartJd,
                    out Vector3d anchorPosition, out Vector3d anchorVelocity);
                burnStartPosition = anchorPosition + escapeDirection * clearanceRadius;
                burnStartVelocity = anchorVelocity + escapeDirection * escapeSpeed;

                if (!TrySolvePinnedDeparture(ship, departureBody, clearanceRadius,
                        escapeSpeed, acceleration, burnStartJd, brakeStartJd,
                        brakeStartPosition, ref escapeDirection, ref burnSeconds,
                        burnSamples, out coastStartPosition, out coastStartVelocity,
                        out coastArrivalVelocity, out coastSeconds, out string reason))
                {
                    return StretchedPlan(ship, target, totalSeconds, reason);
                }

                coastStartJd = burnStartJd + burnSeconds / SecondsPerDay;

                // The forward brake from the coast's arrival must reach the entry
                // state; the difference is the thrust the brake needs.
                Vector3d thrustDelta = brakeDirection * (acceleration * brakeSeconds);
                Vector3d gravityDelta = entryVelocity - brakeStartVelocity - thrustDelta;
                Vector3d required = entryVelocity - coastArrivalVelocity - gravityDelta;
                if (!(required.SqrMagnitude > 1e-18))
                    break;

                brakeSeconds = required.Magnitude / acceleration;
                brakeDirection = required.Normalized;
            }

            // Settle the break and the departure once more against the final ray,
            // so the break's duration, the burn's start state, and the coast all
            // belong to the arcs that are about to be built.
            for (int settle = 0; settle < 3; settle++)
            {
                breakSeconds = SolveBreakSeconds(ship, departureBody, escapeDirection,
                    clearanceRadius, escapeSpeed, breakSeconds, brakeStartJd,
                    burnSeconds, brakeSeconds);
                burnStartJd = t0 + breakSeconds / SecondsPerDay;
                if (!TrySolvePinnedDeparture(ship, departureBody, clearanceRadius,
                        escapeSpeed, acceleration, burnStartJd, brakeStartJd,
                        brakeStartPosition, ref escapeDirection, ref burnSeconds,
                        burnSamples, out coastStartPosition, out coastStartVelocity,
                        out coastArrivalVelocity, out coastSeconds, out string settleReason))
                {
                    return StretchedPlan(ship, target, totalSeconds, settleReason);
                }
            }

            if (!(coastSeconds > MinimumArcSeconds))
            {
                return StretchedPlan(ship, target, totalSeconds,
                    "burn-coast-burn: the trip time leaves no room for the coast " +
                    "between the burns");
            }

            _model.ComputeState(departureBody, burnStartJd,
                out Vector3d settledAnchorPosition, out Vector3d settledAnchorVelocity);
            burnStartPosition = settledAnchorPosition + escapeDirection * clearanceRadius;
            burnStartVelocity = settledAnchorVelocity + escapeDirection * escapeSpeed;
            coastStartJd = burnStartJd + burnSeconds / SecondsPerDay;

            // A brake decelerates. A coast that arrives slower than the target's
            // frame would need a prograde burn, which this profile does not have.
            if (Vector3d.Dot(brakeDirection, coastArrivalVelocity) >= 0.0)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the coast arrives slower than the target's " +
                    "frame, so the crossing needs an acceleration rather than a brake");
            }

            // The burn's final arc uses the solve's sample count, so its end state
            // is the state the coast was solved from; its end velocity is the
            // coast's departure velocity and its position is the Lambert start.
            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunForBurn, out Vector3d sunVelocityForBurn);
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                burnStartJd, burnSeconds, burnStartPosition - sunForBurn,
                burnStartVelocity - sunVelocityForBurn, acceleration,
                retrograde: false, samples: burnSamples,
                fixedThrustDirection: escapeDirection);

            // The brake is solved backwards from the entry, so its start is the
            // state a forward brake must leave from. The coast is then solved to
            // that start, and its departure velocity is corrected against the
            // conic propagator itself: the Lambert solver and the propagator agree
            // only to centimetres on a fast, long coast, and the seam must be
            // tighter than that. The correction moves the departure velocity by
            // metres per second at most, far inside the velocity seam tolerance.
            double brakeStartFinalJd = coastStartJd + coastSeconds / SecondsPerDay;
            _model.ComputeState(_model.Sun, coastStartJd,
                out Vector3d sunAtCoastStart, out Vector3d sunVelocityAtCoastStart);
            _model.ComputeState(_model.Sun, brakeStartFinalJd,
                out Vector3d sunAtBrakeArc, out Vector3d sunVelocityAtBrakeArc);

            LambertSolution coastSolution = LambertSolver.SolveShortWay(
                coastStartPosition, brakeStartPosition, coastSeconds, _model.Sun.Gm);
            if (!coastSolution.HasSolution)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    CoastReason(coastSolution));
            }

            Vector3d coastStartFinalVelocity = coastSolution.DepartureVelocityMps;
            Vector3d localTarget = brakeStartPosition - sunAtBrakeArc;
            if (!TryCorrectCoastDeparture(_model.Sun,
                    coastStartPosition - sunAtCoastStart,
                    coastStartFinalVelocity - sunVelocityAtCoastStart,
                    coastSeconds, localTarget,
                    out Vector3d correctedLocalVelocity))
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the coast cannot be joined to the brake's start");
            }

            coastStartFinalVelocity = correctedLocalVelocity + sunVelocityAtCoastStart;

            ConicArc coast = ConicArc.Create(_model, _model.Sun, coastStartJd, coastSeconds,
                coastStartPosition - sunAtCoastStart,
                coastStartFinalVelocity - sunVelocityAtCoastStart);

            FiniteBurnArc brake = FiniteBurnArc.Create(_model, _model.Sun,
                brakeStartFinalJd, brakeSeconds, brakeStartPosition - sunAtBrakeArc,
                coastSolution.ArrivalVelocityMps - sunVelocityAtBrakeArc, acceleration,
                retrograde: true, samples: brakeSamples,
                fixedThrustDirection: brakeDirection);

            var arcs = new List<PlanArc>(8);
            arcs.Add(MakeArc(FlightPhase.Departure, departureBody.Id, t0, burnStartJd,
                ship.PositionM, ship.VelocityMps, burnStartPosition, burnStartVelocity));
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Cruise, _model.Sun.Id,
                burnStartJd, coastStartJd, departureBurn));

            if (!DepartureBurnStaysOutside(arcs, departureBody, departureSoi))
            {
                return StretchedPlan(ship, target, totalSeconds,
                    "burn-coast-burn: the departure finite burn would cross the departure " +
                    "sphere of influence; the level is too low for this trip time");
            }

            arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, coastStartJd,
                brakeStartFinalJd, coast, Vector3d.Zero));

            if (brakeSeconds > 0.0)
            {
                arcs.Add(MakeFiniteBurnArc(FlightPhase.Capture, _model.Sun.Id,
                    brakeStartFinalJd, entryJd, brake));
            }

            double parkingEndJd;
            ConicArc parking;
            double finiteBurnAcceleration = acceleration;

            if (ArrivalStyle == CaptureStyle.FiniteBurn
                && FixedBurnAccelerationMps2 > 0.0)
            {
                if (!AppendHybridCapture(arcs, target, entryJd, chain, Vector3d.Zero,
                        out parkingEndJd, out parking, out string reason,
                        out finiteBurnAcceleration))
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "burn-coast-burn: hybrid capture: " + reason);
                }
            }
            else
            {
                AppendConicCapture(arcs, target, entryJd, chain, Vector3d.Zero,
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
        /// The break's duration. The reference coast's departure share is kept
        /// while the quintic's peak thrust fits the drive; when it does not, the
        /// duration is lengthened to the shortest one that fits, capped by the time
        /// the coast and the burns leave. The endpoint is the pinned clearance
        /// state, so the duration changes only how hard the break works, not where
        /// it ends.
        /// </summary>
        private double SolveBreakSeconds(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, double clearanceRadius, double escapeSpeed,
            double candidateSeconds, double brakeStartJd, double burnSeconds,
            double brakeSeconds)
        {
            double limit = MaxAccelerationMps2 * ThrustMargin;
            double available = (brakeStartJd - ship.JulianDate) * SecondsPerDay
                               - burnSeconds - brakeSeconds - MinimumCoastSeconds;
            double cap = Math.Max(MinimumArcSeconds, available);
            double candidate = Math.Clamp(candidateSeconds, MinimumArcSeconds, cap);

            if (BreakPeakMps2(ship, departureBody, direction, clearanceRadius,
                    escapeSpeed, candidate) <= limit)
            {
                return candidate;
            }

            if (!(available > candidate)
                || BreakPeakMps2(ship, departureBody, direction, clearanceRadius,
                    escapeSpeed, cap) > limit)
            {
                // The trip time itself is too short for a gentle break; the plan
                // comes out over-thrust and is reported as infeasible rather than
                // inventing time.
                return cap;
            }

            double low = candidate;
            double high = candidate;
            for (int step = 0; step < 24 && high < cap; step++)
            {
                high = Math.Min(high * 1.25, cap);
                if (BreakPeakMps2(ship, departureBody, direction, clearanceRadius,
                        escapeSpeed, high) <= limit)
                {
                    break;
                }
            }

            for (int iteration = 0; iteration < 20; iteration++)
            {
                double middle = 0.5 * (low + high);
                if (BreakPeakMps2(ship, departureBody, direction, clearanceRadius,
                        escapeSpeed, middle) <= limit)
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            return high;
        }

        /// <summary>The quintic break's peak commanded thrust for one duration.</summary>
        private double BreakPeakMps2(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, double clearanceRadius, double escapeSpeed,
            double seconds)
        {
            double endJd = ship.JulianDate + seconds / SecondsPerDay;
            _model.ComputeState(departureBody, endJd,
                out Vector3d anchorPosition, out Vector3d anchorVelocity);
            PlanArc arc = MakeArc(FlightPhase.Departure, departureBody.Id,
                ship.JulianDate, endJd, ship.PositionM, ship.VelocityMps,
                anchorPosition + direction * clearanceRadius,
                anchorVelocity + direction * escapeSpeed);
            return arc.PeakThrustMps2;
        }

        /// <summary>
        /// Solve the departure burn's escape ray and duration so that a burn
        /// integrated forward from the pinned clearance state ends on the Lambert
        /// coast's departure velocity. Three equations (the velocity mismatch) in
        /// three unknowns (two ray angles and the burn's duration), solved by a
        /// damped Newton with a finite-difference Jacobian. The coast runs from the
        /// burn's end to the brake's start in the time the trip leaves it.
        /// </summary>
        private bool TrySolvePinnedDeparture(ShipState ship, BodyRuntime departureBody,
            double clearanceRadius, double escapeSpeed, double acceleration,
            double burnStartJd, double brakeStartJd, Vector3d brakeStart,
            ref Vector3d direction, ref double burnSeconds, int samples,
            out Vector3d coastStartPosition, out Vector3d coastStartVelocity,
            out Vector3d coastArrivalVelocity, out double coastSeconds,
            out string reason)
        {
            coastStartPosition = Vector3d.Zero;
            coastStartVelocity = Vector3d.Zero;
            coastArrivalVelocity = Vector3d.Zero;
            coastSeconds = 0.0;
            reason = null;

            Vector3d axis = Math.Abs(direction.z) < 0.9
                ? new Vector3d(0.0, 0.0, 1.0)
                : new Vector3d(0.0, 1.0, 0.0);
            Vector3d tangent1 = Vector3d.Cross(direction, axis).Normalized;
            Vector3d tangent2 = Vector3d.Cross(direction, tangent1).Normalized;

            double angle1 = 0.0;
            double angle2 = 0.0;
            double seconds = burnSeconds;
            double best = double.PositiveInfinity;

            for (int iteration = 0; iteration < 12; iteration++)
            {
                if (!EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1, angle2, seconds, samples,
                        out Vector3d mismatch, out coastStartPosition,
                        out coastStartVelocity, out coastArrivalVelocity,
                        out coastSeconds))
                {
                    reason = "burn-coast-burn: the departure burn has no coast at " +
                             "this trip time";
                    return false;
                }

                double residual = mismatch.Magnitude;
                best = Math.Min(best, residual);
                if (residual <= 1e-6)
                {
                    direction = Ray(direction, tangent1, tangent2, angle1, angle2);
                    burnSeconds = seconds;
                    return true;
                }

                double angleStep = 1e-3;
                double timeStep = Math.Max(0.5, Math.Min(10.0, 0.001 * seconds));
                if (!EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1 + angleStep, angle2, seconds, samples,
                        out Vector3d mismatch1, out _, out _, out _, out _)
                    || !EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1 - angleStep, angle2, seconds, samples,
                        out Vector3d mismatch1Low, out _, out _, out _, out _)
                    || !EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1, angle2 + angleStep, seconds, samples,
                        out Vector3d mismatch2, out _, out _, out _, out _)
                    || !EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1, angle2 - angleStep, seconds, samples,
                        out Vector3d mismatch2Low, out _, out _, out _, out _)
                    || !EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1, angle2, seconds + timeStep, samples,
                        out Vector3d mismatch3, out _, out _, out _, out _)
                    || !EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, brakeStartJd, brakeStart,
                        angle1, angle2, seconds - timeStep, samples,
                        out Vector3d mismatch3Low, out _, out _, out _, out _))
                {
                    reason = "burn-coast-burn: the departure burn has no coast at " +
                             "this trip time";
                    return false;
                }

                var jacobian = new double[3, 3];
                var rhs = new double[3];
                for (int row = 0; row < 3; row++)
                {
                    jacobian[row, 0] = (Component(mismatch1, row)
                                        - Component(mismatch1Low, row))
                                       / (2.0 * angleStep);
                    jacobian[row, 1] = (Component(mismatch2, row)
                                        - Component(mismatch2Low, row))
                                       / (2.0 * angleStep);
                    jacobian[row, 2] = (Component(mismatch3, row)
                                        - Component(mismatch3Low, row))
                                       / (2.0 * timeStep);
                    rhs[row] = -Component(mismatch, row);
                }

                if (!SolveLinear(jacobian, rhs, out double[] step))
                {
                    reason = "burn-coast-burn: the departure burn's solve is " +
                             "singular at this trip time";
                    return false;
                }

                double scale = 1.0;
                double maxAngle = Math.Max(Math.Abs(step[0]), Math.Abs(step[1]));
                if (maxAngle > 0.4) scale = Math.Min(scale, 0.4 / maxAngle);
                if (Math.Abs(step[2]) > 0.25 * seconds)
                {
                    scale = Math.Min(scale, 0.25 * seconds / Math.Abs(step[2]));
                }

                // Backtrack if the step does not reduce the residual: the system
                // is not analytic at a degenerate coast and a full Newton step can
                // leave the basin.
                bool accepted = false;
                for (int attempt = 0; attempt < 6; attempt++, scale *= 0.5)
                {
                    double trialAngle1 = angle1 + scale * step[0];
                    double trialAngle2 = angle2 + scale * step[1];
                    double trialSeconds = seconds + scale * step[2];
                    if (!(trialSeconds > MinimumArcSeconds)) continue;
                    if (!EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, brakeStartJd, brakeStart,
                            trialAngle1, trialAngle2, trialSeconds, samples,
                            out Vector3d trial, out _, out _, out _, out _))
                    {
                        continue;
                    }

                    if (trial.Magnitude < residual)
                    {
                        angle1 = trialAngle1;
                        angle2 = trialAngle2;
                        seconds = trialSeconds;
                        accepted = true;
                        break;
                    }
                }

                if (!accepted)
                {
                    reason = "burn-coast-burn: the departure burn cannot be placed " +
                             $"at this trip time ({best:E3} m/s short of the coast)";
                    return false;
                }
            }

            reason = "burn-coast-burn: the departure burn did not settle on the " +
                     $"coast ({best:F3} m/s short)";
            return false;
        }

        private bool EvaluatePinnedDeparture(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, Vector3d tangent1, Vector3d tangent2,
            double clearanceRadius, double escapeSpeed, double acceleration,
            double burnStartJd, double brakeStartJd, Vector3d brakeStart,
            double angle1, double angle2, double seconds, int samples,
            out Vector3d mismatch, out Vector3d coastStartPosition,
            out Vector3d coastStartVelocity, out Vector3d coastArrivalVelocity,
            out double coastSeconds)
        {
            mismatch = Vector3d.Zero;
            coastStartPosition = Vector3d.Zero;
            coastStartVelocity = Vector3d.Zero;
            coastArrivalVelocity = Vector3d.Zero;
            coastSeconds = 0.0;

            if (!(seconds > MinimumArcSeconds)) return false;

            Vector3d ray = Ray(direction, tangent1, tangent2, angle1, angle2);
            _model.ComputeState(departureBody, burnStartJd,
                out Vector3d anchorPosition, out Vector3d anchorVelocity);
            Vector3d startPosition = anchorPosition + ray * clearanceRadius;
            Vector3d startVelocity = anchorVelocity + ray * escapeSpeed;

            double coastStartJd = burnStartJd + seconds / SecondsPerDay;
            coastSeconds = (brakeStartJd - coastStartJd) * SecondsPerDay;
            if (!(coastSeconds > MinimumArcSeconds)) return false;

            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunAtStart, out Vector3d sunVelocityAtStart);
            FiniteBurnArc burn = FiniteBurnArc.Create(_model, _model.Sun, burnStartJd,
                seconds, startPosition - sunAtStart, startVelocity - sunVelocityAtStart,
                acceleration, retrograde: false, samples: samples,
                fixedThrustDirection: ray);

            _model.ComputeState(_model.Sun, coastStartJd,
                out Vector3d sunAtEnd, out Vector3d sunVelocityAtEnd);
            coastStartPosition = burn.EndLocalPositionM + sunAtEnd;
            Vector3d burnEndVelocity = burn.EndLocalVelocityMps + sunVelocityAtEnd;

            LambertSolution coast = LambertSolver.SolveShortWay(
                coastStartPosition, brakeStart, coastSeconds, _model.Sun.Gm);
            if (!coast.HasSolution) return false;

            coastStartVelocity = coast.DepartureVelocityMps;
            coastArrivalVelocity = coast.ArrivalVelocityMps;
            mismatch = burnEndVelocity - coastStartVelocity;
            return true;
        }

        /// <summary>
        /// Correct a Lambert departure velocity until the two-body propagator
        /// itself lands on the required end position. The Lambert solve is a
        /// tolerance apart from the propagator, which is centimetres on a fast
        /// coast; this closes the coast's end seam to below a millimetre.
        /// </summary>
        private static bool TryCorrectCoastDeparture(BodyRuntime sun,
            Vector3d localStartPosition, Vector3d localStartVelocity,
            double seconds, Vector3d localTarget, out Vector3d correctedVelocity)
        {
            correctedVelocity = localStartVelocity;
            double tolerance = 1e-4;

            for (int iteration = 0; iteration < 8; iteration++)
            {
                KeplerPropagator.Propagate(localStartPosition, correctedVelocity,
                    sun.Gm, seconds, out Vector3d endPosition, out _);
                Vector3d error = localTarget - endPosition;
                if (error.Magnitude < tolerance) return true;

                var jacobian = new double[3, 3];
                var rhs = new double[3];
                double step = 1e-3;
                for (int column = 0; column < 3; column++)
                {
                    Vector3d probe = correctedVelocity;
                    if (column == 0) probe = new Vector3d(probe.x + step, probe.y, probe.z);
                    if (column == 1) probe = new Vector3d(probe.x, probe.y + step, probe.z);
                    if (column == 2) probe = new Vector3d(probe.x, probe.y, probe.z + step);
                    KeplerPropagator.Propagate(localStartPosition, probe, sun.Gm,
                        seconds, out Vector3d probeEnd, out _);
                    for (int row = 0; row < 3; row++)
                        jacobian[row, column] = (Component(probeEnd, row)
                                                 - Component(endPosition, row)) / step;
                }

                for (int row = 0; row < 3; row++) rhs[row] = Component(error, row);
                if (!SolveLinear(jacobian, rhs, out double[] correction)) return false;

                correctedVelocity = new Vector3d(
                    correctedVelocity.x + correction[0],
                    correctedVelocity.y + correction[1],
                    correctedVelocity.z + correction[2]);
            }

            return false;
        }

        private static double Component(Vector3d vector, int index) =>
            index == 0 ? vector.x : index == 1 ? vector.y : vector.z;

        private static Vector3d Ray(Vector3d direction, Vector3d tangent1,
            Vector3d tangent2, double angle1, double angle2) =>
            (direction + tangent1 * angle1 + tangent2 * angle2).Normalized;

        private static bool SolveLinear(double[,] matrix, double[] rhs, out double[] solution)
        {
            int size = rhs.Length;
            var work = new double[size, size + 1];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                    work[row, column] = matrix[row, column];
                work[row, size] = rhs[row];
            }

            for (int column = 0; column < size; column++)
            {
                int pivot = column;
                for (int row = column + 1; row < size; row++)
                {
                    if (Math.Abs(work[row, column]) > Math.Abs(work[pivot, column]))
                        pivot = row;
                }

                if (!(Math.Abs(work[pivot, column]) > 1e-18))
                {
                    solution = null;
                    return false;
                }

                if (pivot != column)
                {
                    for (int entry = column; entry <= size; entry++)
                    {
                        (work[column, entry], work[pivot, entry]) =
                            (work[pivot, entry], work[column, entry]);
                    }
                }

                for (int row = 0; row < size; row++)
                {
                    if (row == column) continue;
                    double factor = work[row, column] / work[column, column];
                    for (int entry = column; entry <= size; entry++)
                        work[row, entry] -= factor * work[column, entry];
                }
            }

            solution = new double[size];
            for (int row = 0; row < size; row++)
                solution[row] = work[row, size] / work[row, row];
            return true;
        }

        private bool DepartureBurnStaysOutside(List<PlanArc> arcs, BodyRuntime departureBody,
            double soiM)
        {
            int samples = Math.Max(8, ArcSamples);
            foreach (PlanArc arc in arcs)
            {
                if (!arc.IsFiniteBurn) continue;

                for (int sample = 0; sample <= samples; sample++)
                {
                    double u = (double)sample / samples;
                    double julianDate = arc.StartJulianDate
                                        + arc.Path.SecondsAt(u) / SecondsPerDay;
                    _model.ComputeState(departureBody, julianDate,
                        out Vector3d departurePosition, out _);
                    if (Vector3d.Distance(arc.Path.PositionAt(u), departurePosition)
                        < soiM * (1.0 - 1e-6))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private BodyRuntime FindDepartureBody(ShipState ship, BodyRuntime target,
            double julianDate, out double soiM)
        {
            soiM = 0.0;
            foreach (BodyRuntime planet in _model.Sun.Children)
            {
                if (planet == target || planet == target.Parent) continue;

                double soi = SphereOfInfluence.RadiusM(planet);
                if (!double.IsFinite(soi)) continue;

                _model.ComputeState(planet, julianDate, out Vector3d planetPosition, out _);
                if (Vector3d.Distance(ship.PositionM, planetPosition) < soi)
                {
                    soiM = soi;
                    return planet;
                }
            }

            return null;
        }

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

        /// <summary>An infeasible plan that also asks for a longer trip. The
        /// growing search in <see cref="Plan"/> reads the requirement, so a trip
        /// time that squeezes the arcs is retried longer instead of being reported
        /// as a geometry failure.</summary>
        private BuildResult StretchedPlan(ShipState ship, BodyRuntime target,
            double totalSeconds, string reason)
        {
            BuildResult result = InfeasiblePlan(ship, target, totalSeconds, reason);
            return new BuildResult(result.Plan, Math.Max(totalSeconds * 1.5,
                totalSeconds + MinimumCoastSeconds));
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
