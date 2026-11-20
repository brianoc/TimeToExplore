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

        /// <summary>Shortest coast the burn–coast–burn profile will leave between
        /// its two burns. A trip time that cannot fit one is stretched.</summary>
        private const double MinimumCoastSeconds = 3_600.0;

        /// <summary>Velocity mismatch, in m/s, that ends the pinned departure
        /// solve. The design note promises fast-plan seams at or under 1e-5 m/s,
        /// so the solve aims there rather than at a tolerance no seam test asks
        /// for; the tighter 1e-6 target left narrow non-convergence holes at the
        /// fast end of the profile.</summary>
        private const double DepartureSolveToleranceMps = 1e-5;

        /// <summary>Newton iterations for the pinned departure solve. The fast
        /// end of the profile is close to degenerate and needs more iterations
        /// than the gentle trips; the loop exits as soon as it converges.</summary>
        private const int DepartureSolveIterations = 48;

        /// <summary>Burn samples used by the floor search's probes. The final
        /// plan always integrates its burns at the full
        /// <see cref="ArcSamples"/>-or-better resolution; a probe only has to
        /// decide whether a duration fits, and the certified build is the
        /// authority.</summary>
        private const int FastProbeBurnSamples = 128;

        /// <summary>Burn samples used inside the pinned departure solve's
        /// trial burns. This is the solve's resolution, not the reported arc's:
        /// the Newton iteration only has to place the burn's end velocity on the
        /// coast's departure, and 128 samples resolve the trial burn finely
        /// enough to converge to <see cref="DepartureSolveToleranceMps"/> at a
        /// quarter of the 512-sample cost. The plan's departure burn is still
        /// integrated at <see cref="ArcSamples"/>-or-better (512).</summary>
        private const int PinnedDepartureSamples = 128;

        /// <summary>Brake samples in the burn–coast–burn **certified** build. The
        /// brake is a slow crossing over hours, so 512 samples is a metre-scale
        /// integration step and the pin's velocity mismatch stays far inside the
        /// 1 km / 1 m/s chain gate; the automatic count would clamp at the old
        /// 2,048 for every measured brake. A fast probe keeps
        /// <see cref="FiniteBurnArc.MaximumSamples"/>: the probe is a search
        /// input, and its brake must not move with the build's cost trim.</summary>
        private const int BrakeSamples = 512;

        /// <summary>Burn samples used by a **full-field probe**'s pinned
        /// departure solve and its reported trial arcs. A probe is a search
        /// input, not an answer: 64 samples resolve the trial burn well enough
        /// to decide whether the chain closes, while the certified build that
        /// follows the search integrates at <see cref="PinnedDepartureSamples"/>
        /// and reports at 512 or better. A probe gets no 512-sample fallback;
        /// that retry protects a certified fixed-duration answer, not a search
        /// decision.</summary>
        private const int ProbeDepartureSamples = 64;

        /// <summary>Certificates the floor search may spend before it reports the
        /// last refusal. The fast probe's answer is certified at least once; a
        /// refused certificate seeds the probe search above itself and the next
        /// answer is certified again. Three is the bound that keeps a noisy
        /// near-floor basin from turning the search into a chain of certified
        /// builds.</summary>
        private const int MaxFloorCertificates = 3;

        /// <summary>Velocity mismatch, in m/s, at the brake's start that ends
        /// the pinned brake/departure fixed point. The mismatch is integrated
        /// over the brake, so 1e-6 m/s is at most about 0.3 m of entry position
        /// over the longest measured brake (82.7 h at the Uranus 0.3 g floor),
        /// far inside the 1 km chain gate. The fixed point's own interpolation
        /// resolution stalls at about 6.6e-7 m/s, so a 1e-7 target never
        /// converges and the loop runs its twelve-pass cap; 1e-6 reaches the
        /// same plan in eight passes.</summary>
        private const double BrakeConvergenceMps = 1e-6;

        /// <summary>Change in the brake's duration that ends the fixed point.</summary>
        private const double BrakeDurationToleranceSeconds = 1e-6;

        /// <summary>Change in the brake's unit thrust direction that ends the
        /// fixed point.</summary>
        private const double BrakeDirectionTolerance = 1e-10;

        /// <summary>Looser fixed-point stops for a floor probe. A probe only
        /// has to decide whether the chain closes at all; the certified build
        /// converges to the tolerances above.</summary>
        private const double FastBrakeConvergenceMps = 1e-4;
        private const double FastBrakeDurationToleranceSeconds = 1e-3;
        private const double FastBrakeDirectionTolerance = 1e-6;

        /// <summary>Outer brake/departure passes a floor probe may run. A probe
        /// stops on <see cref="FastBrakeConvergenceMps"/>; the slow near-floor
        /// fixed point needs the full twelve to reach the one-kilometre gate.</summary>
        private const int FastProbeOuterPasses = 12;

        /// <summary>Outer brake/departure passes a certified build may run. Most
        /// builds converge in a handful; the near-floor fixed point plateaus at
        /// about 1e-4 m/s — its own interpolation resolution, above
        /// <see cref="BrakeConvergenceMps"/> — and runs this cap.</summary>
        private const int DefaultOuterPasses = 12;

        /// <summary>Outer passes a certified build retries with when the
        /// twelve-pass cap leaves the brake unsettled and the chain gate refuses
        /// the plan. At the near-floor basin the pin is still converging at pass
        /// twelve (Jupiter 0.3 g at 13.07211 d: a 0.137 m/s brake residual reads
        /// as a 28 km chain miss), and the extra passes settle it to metres. The
        /// retry changes no tolerance and no gate: it only gives the same fixed
        /// point the passes it needs. A converged build never pays for it.</summary>
        private const int RetryOuterPasses = 24;

        /// <summary>Change in the solved departure ray and burn duration that
        /// ends the pinned fixed point.</summary>
        private const double RayDirectionTolerance = 1e-10;
        private const double BurnDurationToleranceSeconds = 1e-6;

        /// <summary>How close the floor search's bracket must be before it
        /// returns the feasible end, as an absolute floor and a fraction of the
        /// floor. The old search bisected to 30 s; the fast model costs enough
        /// per probe that a twentieth of a percent is the useful resolution,
        /// and the returned duration is still the shortest feasible plan the
        /// drive can fly.</summary>
        private const double FloorToleranceSeconds = 30.0;
        private const double FloorRelativeTolerance = 5e-4;

        /// <summary>How close the fast, departure-body-only break peak may come
        /// to the drive limit before the break search certifies the probe with
        /// the full gravity field. The departure body dominates the break, but
        /// the Sun and the other catalogue bodies still pull on the ship; inside
        /// this band the full field decides, so a fast estimate can never let an
        /// over-thrust break through. The band is much wider than the omitted
        /// term at the limit: at Mercury the Sun contributes about 0.04 m/s²
        /// against a 10 m/s² break peak, and less at the other departure
        /// bodies.</summary>
        public const double BreakScreenMargin = 0.05;

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

        /// <summary>Geometric refinement points added at each end of a conic arc
        /// when its thrust and delta-v are measured. A conic's parameter is
        /// linear in true anomaly, so the sample spacing in time collapses at one
        /// end of a long transfer; the delta-v is a time integral and its last
        /// uniform bin can span days of approach into the target's well.</summary>
        private const int ConicEdgeSamples = 16;

        /// <summary>Fraction of a conic arc's duration the edge refinement
        /// covers, halving towards each end.</summary>
        private const double ConicEdgeFraction = 0.01;

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
            {
                // The estimate is only the search's seed, but the burn–coast–burn
                // now enters at the handover floor instead of the sphere of
                // influence, so its capture's fall is minutes rather than months
                // and the seed should say so.
                double entryRadius = SphereOfInfluence.RadiusM(target);
                if (CruiseStyle == CruiseStyle.BurnCoastBurn && FixedBurnAccelerationMps2 > 0.0)
                {
                    HandoverBand band = HandoverBand.For(target, FixedBurnAccelerationMps2);
                    if (band.IsValid) entryRadius = ArrivalEntryRadiusM(band, target);
                }

                total += EstimateConicCaptureSeconds(target, entryRadius);
            }

            // The burn–coast–burn profile has a real floor that the generic
            // growing loop cannot see: the pinned break is gentle, so the fast
            // end is limited by the drive and the solver, not by the break's
            // clearance. Search for it instead of returning the loop's first
            // feasible plan.
            bool insideTarget = Vector3d.Distance(targetPosition, ship.PositionM)
                                < SphereOfInfluence.RadiusM(target);
            if (CruiseStyle == CruiseStyle.BurnCoastBurn
                && FixedBurnAccelerationMps2 > 0.0
                && !insideTarget)
            {
                return PlanBurnCoastBurnMinimum(ship, target, total);
            }

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

                    return Reported(plan);
                }

                Trace?.Invoke($"iter {iteration}: total {total / SecondsPerDay:F3} d, " +
                              $"required {required / SecondsPerDay:F3} d, " +
                              $"peak {plan.PeakThrustMps2:F3}, feasible {plan.IsFeasible}");

                if (Math.Abs(required - total) <= ConvergenceTolerance * total && plan.IsFeasible)
                    return Reported(plan);

                total = 0.5 * (total + required);
            }

            // A longer arc with the same endpoints needs less thrust, so a final
            // build at the duration the last pass asked for is feasible whenever
            // the limit is set by the curve's shape rather than by a gravity well.
            if (!plan.IsFeasible || required > total)
                plan = Build(ship, target, Math.Max(required, total)).Plan;

            return Reported(plan);
        }

        /// <summary>
        /// The burn–coast–burn profile's true floor: the shortest duration whose
        /// build fits the drive and whose chain closes. The search starts on the
        /// fast probe model (<see cref="Build"/>'s <c>fast</c> mode), which
        /// measures the break against its own central body and treats a fixed
        /// burn as its commanded acceleration. A bracket is found by probing a
        /// little above the planner's kinematic estimate and walking down in 15%
        /// steps to the first rejected request; the crossing is then bisected to
        /// <see cref="FloorRelativeTolerance"/>.
        ///
        /// The fast answer is never reported. It is rebuilt as a **certified**
        /// full-field plan; if the certificate refuses, the search continues
        /// upward with the **full-field probe** quality, whose burns integrate the
        /// summed field but whose solve and measurement are the cheap probe ones.
        /// The certificate is the probe search's low end, so its refusal is never
        /// rebuilt, and at most <see cref="MaxFloorCertificates"/> certificates are
        /// spent before the last refusal is reported honestly.
        /// </summary>
        private TrajectoryPlan PlanBurnCoastBurnMinimum(ShipState ship, BodyRuntime target,
            double estimateSeconds)
        {
            FloorSearch fast = SearchFloor(ship, target, estimateSeconds,
                fast: true, seedLow: 0.0, seedHigh: 0.0);
            if (fast.Plan == null)
                return Reported(fast.LastPlan);

            // The full field is the authority. The fast crossing is normally
            // feasible there, but the near-floor basin is a scatter, so a refused
            // certificate is the search's new low end rather than its answer.
            var tried = new Dictionary<double, BuildResult>();
            double candidate = fast.HighSeconds;
            TrajectoryPlan lastRefusal = null;

            for (int certificate = 0; certificate < MaxFloorCertificates; certificate++)
            {
                BuildResult certified = Build(ship, target, candidate);
                tried[candidate] = certified;
                lastRefusal = certified.Plan;

                bool feasible = IsFeasibleAt(certified.Plan, candidate);
                Trace?.Invoke($"certify {candidate / SecondsPerDay:F5} d: " +
                              $"feasible {feasible}, " +
                              $"time {certified.Plan.FlightTimeSeconds / SecondsPerDay:F5} d, " +
                              $"peak {certified.Plan.PeakThrustMps2:F3}, " +
                              $"reason {certified.Plan.InfeasibleReason}");
                if (feasible)
                    return Reported(certified.Plan);

                if (certificate == MaxFloorCertificates - 1)
                    break;

                // The fast or probe model was optimistic at this duration.
                // Continue above it with full-field probes; the certificate is
                // the seeded low, so the search reuses it instead of rebuilding.
                FloorSearch probe = SearchFloor(ship, target, estimateSeconds,
                    fast: false, seedLow: candidate, seedHigh: candidate * 1.2,
                    tried, probe: true);
                if (probe.Plan == null)
                    break;

                candidate = probe.HighSeconds;
            }

            return Reported(lastRefusal);
        }

        /// <summary>One bracketed search for the floor. <paramref name="fast"/>
        /// selects the two-body probe model; <paramref name="probe"/> keeps the
        /// full summed field for the burns but solves and measures at probe
        /// quality. The final answer is neither: the caller rebuilds it with
        /// <see cref="Build"/>'s default, certified mode. A feasible high and a
        /// rejected low are established with direct probes, then the bracket is
        /// bisected to a tolerance. The 0.2% retry keeps a near-floor
        /// non-convergence from becoming the advertised floor.
        /// <paramref name="tried"/> carries builds this call has already paid
        /// for, keyed by the requested duration, so a duration the caller
        /// certified before the search is never built again.</summary>
        private FloorSearch SearchFloor(ShipState ship, BodyRuntime target,
            double estimateSeconds, bool fast, double seedLow, double seedHigh,
            Dictionary<double, BuildResult> tried = null, bool probe = false)
        {
            double low = 0.0;
            double high = 0.0;
            TrajectoryPlan highPlan = null;
            TrajectoryPlan lastPlan = null;
            double lastRequest = double.NaN;
            bool seedRejected = false;

            FloorProbe Probe(double seconds)
            {
                bool reused = false;
                if (tried == null || !tried.TryGetValue(seconds, out BuildResult result))
                {
                    result = Build(ship, target, seconds, fast, probe);
                    if (tried != null) tried[seconds] = result;
                }
                else
                {
                    reused = true;
                }

                lastPlan = result.Plan;
                lastRequest = seconds;
                bool feasible = IsFeasibleAt(result.Plan, seconds);
                Trace?.Invoke($"floor {(fast ? "fast" : probe ? "probe" : "full")} " +
                              $"{seconds / SecondsPerDay:F5} d: feasible {feasible}, " +
                              $"time {result.Plan.FlightTimeSeconds / SecondsPerDay:F5} d, " +
                              $"peak {result.Plan.PeakThrustMps2:F3}, " +
                              $"reason {result.Plan.InfeasibleReason}" +
                              (reused ? " [cached]" : ""));
                return new FloorProbe(result.Plan, feasible);
            }

            // A seeded high (the fast search's feasible end) is the best hint
            // for the full-field retry; probe it first rather than starting over.
            if (seedHigh > MinimumArcSeconds && seedHigh > seedLow)
            {
                FloorProbe seeded = Probe(seedHigh);
                if (seeded.Feasible)
                {
                    high = seeded.Plan.FlightTimeSeconds;
                    highPlan = seeded.Plan;

                    if (seedLow > MinimumArcSeconds)
                    {
                        FloorProbe below = Probe(seedLow);
                        if (below.Feasible)
                        {
                            high = below.Plan.FlightTimeSeconds;
                            highPlan = below.Plan;
                        }
                        else
                        {
                            low = seedLow;
                        }
                    }
                }
                else
                {
                    low = seedHigh;
                    seedRejected = true;
                }
            }
            else
            {
                double estimateProbeSeconds = Math.Max(estimateSeconds * 1.2,
                    MinimumArcSeconds);
                FloorProbe estimate = Probe(estimateProbeSeconds);
                if (estimate.Feasible)
                {
                    high = estimate.Plan.FlightTimeSeconds;
                    highPlan = estimate.Plan;
                }
                else
                {
                    low = estimateProbeSeconds;
                }
            }

            // Grow to a feasible duration in 20% steps. A rejected seed is
            // retried at the 0.2% step first, because it is a near-floor bracket
            // and the next feasible duration is close. The
            // step count is generous because the estimate starts from the
            // capture chain's own duration: with the arrival entering at the
            // handover floor that is minutes rather than months, so the seed can
            // land well below the profile's real floor.
            for (int step = 0; step < 12 && highPlan == null; step++)
            {
                if (!(lastRequest == low))
                {
                    FloorProbe rejected = Probe(low);
                    if (rejected.Feasible)
                    {
                        high = rejected.Plan.FlightTimeSeconds;
                        highPlan = rejected.Plan;
                        break;
                    }
                }

                double next = seedRejected ? low * 1.002 : low * 1.2;
                next = Math.Min(next, low * 3.0);
                if (!(next > low)) break;
                seedRejected = false;

                FloorProbe growth = Probe(next);
                if (growth.Feasible)
                {
                    high = growth.Plan.FlightTimeSeconds;
                    highPlan = growth.Plan;
                    break;
                }

                low = next;
            }

            if (highPlan == null)
                return new FloorSearch(low, high, null, lastPlan);

            // A seeded full-field probe search starts well above the refused
            // certificate so it has a feasible anchor. The near-floor basin's
            // narrow holes are the search's own resolution, so retry a fifth of
            // a percent above the refused low before bracketing the whole seeded
            // interval: when that retry closes, the bracket is a few probes wide.
            if (probe && low > 0.0 && high > 0.0)
            {
                double near = low * 1.002;
                if (near < high)
                {
                    FloorProbe tightened = Probe(near);
                    if (tightened.Feasible && tightened.Plan.FlightTimeSeconds < high)
                    {
                        high = tightened.Plan.FlightTimeSeconds;
                        highPlan = tightened.Plan;
                    }
                }
            }

            // Walk down to a rejected request. The 15% step is the bracket the
            // old search used, bounded here; the first step below the crossing
            // is the low end. A full-field probe search that was seeded above a
            // refused certificate also walks down from its seeded high, but
            // stops at the seeded low: stepping below it would re-probe a
            // duration the certificate has already refused. The fast search's
            // walk is unchanged — its growth already found the low end.
            bool walkDown = !(low > 0.0) || probe;
            for (int step = 0; step < 8 && walkDown; step++)
            {
                double shorter = high * 0.85;
                if (!(shorter > MinimumArcSeconds)) break;
                if (probe && low > 0.0 && shorter <= low) break;

                FloorProbe walk = Probe(shorter);
                if (!walk.Feasible)
                {
                    low = shorter;
                    break;
                }

                high = walk.Plan.FlightTimeSeconds;
                highPlan = walk.Plan;
            }

            if (!(low > 0.0))
                return new FloorSearch(low, high, highPlan, lastPlan);

            // Bisect the bracket to a tolerance. A near-floor non-convergence
            // can make a flyable duration look rejected, so a failing probe is
            // retried a fifth of a percent higher before it lowers the bracket.
            double tolerance = Math.Max(FloorToleranceSeconds, high * FloorRelativeTolerance);
            for (int iteration = 0; iteration < 10 && high - low > tolerance; iteration++)
            {
                double middle = 0.5 * (low + high);
                FloorProbe middleProbe = Probe(middle);
                if (middleProbe.Feasible && middleProbe.Plan.FlightTimeSeconds < high)
                {
                    high = middleProbe.Plan.FlightTimeSeconds;
                    highPlan = middleProbe.Plan;
                    continue;
                }

                double retrySeconds = middle * 1.002;
                if (retrySeconds < high)
                {
                    FloorProbe retry = Probe(retrySeconds);
                    if (retry.Feasible && retry.Plan.FlightTimeSeconds < high)
                    {
                        high = retry.Plan.FlightTimeSeconds;
                        highPlan = retry.Plan;
                        continue;
                    }
                }

                low = middle;
            }

            return new FloorSearch(low, high, highPlan, lastPlan);
        }

        /// <summary>True when a build is a plan the drive can fly at the
        /// duration asked for. A request below the arcs' kinematic floor comes
        /// back longer, and that longer plan is not proof this duration fits.</summary>
        private static bool IsFeasibleAt(TrajectoryPlan plan, double seconds) =>
            plan.IsFeasible
            && plan.FlightTimeSeconds > 0.0
            && plan.FlightTimeSeconds <= seconds * (1.0 + 1e-3) + 1.0;

        /// <summary>Populate the thrust-limit reason on a plan before it leaves
        /// the planner. The build loop reads <see cref="TrajectoryPlan.IsFeasible"/>
        /// without a reason so an over-thrust trial still grows; only the plan the
        /// caller receives carries the cause.</summary>
        private static TrajectoryPlan Reported(TrajectoryPlan plan)
        {
            plan?.EnsureInfeasibleReason();
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

            return Reported(Build(ship, target, durationSeconds).Plan);
        }

        /// <summary>Diagnostic only: build one burn–coast–burn trip at the
        /// full-field **probe** quality the floor search uses. The planner never
        /// returns this plan — probes are search inputs, and the answer is
        /// rebuilt at certified quality — so this exists for the tests that prove
        /// the difference. A probe reports a fixed burn's commanded acceleration,
        /// a conic's zero thrust, and no clearance or measured delta-v.</summary>
        public TrajectoryPlan BuildProbeAtDuration(ShipState ship, string targetId,
            double durationSeconds)
        {
            if (CruiseStyle != CruiseStyle.BurnCoastBurn)
                throw new InvalidOperationException(
                    "Probe quality is the burn–coast–burn floor search's.");
            if (!(durationSeconds > 0.0) || !double.IsFinite(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds),
                    "A pinned flight time must be finite and positive.");

            BodyRuntime target = RequireTarget(targetId);
            return Reported(Build(ship, target, durationSeconds, fast: false, probe: true).Plan);
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

        private BuildResult Build(ShipState ship, BodyRuntime target, double totalSeconds,
            bool fast = false, bool probe = false)
        {
            if (CruiseStyle == CruiseStyle.Coast)
                return BuildCoast(ship, target, totalSeconds, fast, null, probe);
            if (CruiseStyle == CruiseStyle.BurnCoastBurn)
            {
                BuildResult result = BuildBurnCoastBurn(ship, target, totalSeconds,
                    fast, probe);
                if (!fast && !probe && !result.Plan.IsFeasible && result.PinStalled)
                {
                    // The near-floor brake/departure fixed point can stop at its
                    // default pass cap with a residual the chain gate reads as a
                    // position miss. Give the same fixed point the longer cap
                    // once; the tolerances and the gate are unchanged, so this is
                    // a solver retry, not a relaxed answer.
                    Trace?.Invoke($"pin retry at {totalSeconds / SecondsPerDay:F5} d: " +
                                  $"{DefaultOuterPasses} -> {RetryOuterPasses} passes, " +
                                  $"after {result.Plan.InfeasibleReason}");
                    result = BuildBurnCoastBurn(ship, target, totalSeconds, fast, probe,
                        RetryOuterPasses);
                }

                return result;
            }

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
            plan.CaptureEntryRadiusM = chain.EntryRadiusM;
            plan.CaptureInboundSeconds = chain.InboundSeconds;

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
        private BuildResult BuildCoast(ShipState ship, BodyRuntime target, double totalSeconds,
            bool fast = false, double? entryRadiusM = null, bool probe = false)
        {
            bool cheap = fast || probe;
            double t0 = ship.JulianDate;
            double targetSoi = SphereOfInfluence.RadiusM(target);

            // The coast has no brake, so it enters the target's sphere of
            // influence where the heliocentric coast ends. The burn–coast–burn
            // passes its own floor entry radius here so its reference coast lands
            // on the same entry state; the coast profile itself never does.
            double captureEntryRadius = entryRadiusM ?? targetSoi;

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
                chain = BuildCaptureShape(ship, target, captureEntryRadius, arrivalJd);
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
            chain = BuildCaptureShape(ship, target, captureEntryRadius, arrivalJd);
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
                    endAcceleration: heliocentricCoast.AccelerationAt(0.0),
                    fast: cheap));
            }
            else
            {
                startImpulse = coast.DepartureVelocityMps - ship.VelocityMps;
            }

            arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, escapeEndJd, cruiseEndJd,
                heliocentricCoast, startImpulse, cheap));

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
                        out finiteBurnAcceleration, fast, probe))
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "hybrid capture: " + reason);
                }
            }
            else
            {
                AppendConicCapture(arcs, target, cruiseEndJd, chain, arrivalTrim,
                    out parkingEndJd, out parking, cheap);
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
            plan.CaptureEntryRadiusM = chain.EntryRadiusM;
            plan.CaptureInboundSeconds = chain.InboundSeconds;

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
        /// The break ends where the departure burn begins: on the departure
        /// handover band's ceiling, <see cref="HandoverBand.CeilingSoiFraction"/>
        /// times the departure sphere of influence, along the outbound escape ray,
        /// at the local escape speed along that ray. That is a design decision, not
        /// a derived state: the break's only job is to leave the parking orbit and
        /// reach escape, and the burn supplies everything above it
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
            double totalSeconds, bool fast = false, bool probe = false,
            int outerPassesLimit = DefaultOuterPasses)
        {
            double acceleration = FixedBurnAccelerationMps2;
            if (!(acceleration > 0.0))
                return BuildCoast(ship, target, totalSeconds, fast, null, probe);

            double t0 = ship.JulianDate;
            double targetSoi = SphereOfInfluence.RadiusM(target);
            bool cheap = fast || probe;
            int burnSamples = fast ? FastProbeBurnSamples
                : probe ? ProbeDepartureSamples
                : Math.Max(512, ArcSamples);
            // The fast probe is a search input and keeps the brake resolution it
            // has always had; the certified build's brake is trimmed. A
            // full-field probe keeps the certified 512-sample brake: it is a
            // search input too, and the brake's closure is what decides a
            // near-floor duration.
            int brakeSamples = fast ? FiniteBurnArc.MaximumSamples : BrakeSamples;

            // The fast probe keeps the cheap two-body burn. A full-field probe
            // integrates the summed field like the plan it stands in for, so its
            // closure decision is the reported build's; only its solve and its
            // measurement are the cheap ones.
            IGravityField burnGravity = fast ? null : _gravity;

            // A probe solves at 64 samples and never falls back; the fallback
            // exists to protect a certified fixed-duration answer.
            int solveSamples = probe ? ProbeDepartureSamples : PinnedDepartureSamples;
            int fallbackSamples = cheap ? 0 : burnSamples;

            _model.ComputeState(target, t0, out Vector3d targetAtDeparture, out _);
            if (Vector3d.Distance(ship.PositionM, targetAtDeparture) < targetSoi)
                return BuildQuintic(ship, target, totalSeconds);

            // The arrival endpoint is the band's floor, raised to the radius the
            // chain captures into when the floor is deeper than it. The capture
            // chain is built from that radius instead of the sphere of influence:
            // the brake ends there, so the chain's inbound fall starts there.
            HandoverBand arrivalBand = HandoverBand.For(target, acceleration);
            if (!arrivalBand.IsValid)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: " + arrivalBand.InvalidReason);
            }

            double arrivalRadius = ArrivalEntryRadiusM(arrivalBand, target);

            BodyRuntime departureBody = FindDepartureBody(ship, target, t0);
            if (departureBody == null)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the ship is not inside a departure sphere of " +
                    "influence to break out of");
            }

            // The departure endpoint is the band's ceiling: the break's job is
            // escape, and the burn starts there and moves outward. The ceiling is
            // the same clearance sphere the profile has always used, so the
            // departure plan does not move.
            HandoverBand departureBand = HandoverBand.For(departureBody, acceleration);
            if (!departureBand.IsValid)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: " + departureBand.InvalidReason);
            }

            Trace?.Invoke($"handover band: departure {departureBand.DepartureRadiusM / 1000.0:F0} km " +
                          $"(ceiling), arrival {arrivalBand.ArrivalRadiusM / 1000.0:F0} km " +
                          $"(floor), chain entry {arrivalRadius / 1000.0:F0} km, " +
                          $"ceiling {arrivalBand.CeilingRadiusM / 1000.0:F0} km");

            // The coast skeleton supplies the capture chain, the handover the
            // brake spends, and a first guess at the outbound direction and the
            // burn's size. It is built at the floor entry radius too, so its
            // timing and its handover are the ones the burn–coast–burn flies; the
            // coast profile itself keeps the sphere of influence.
            BuildResult reference = BuildCoast(ship, target, totalSeconds, cheap,
                arrivalRadius, probe);
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

            // The capture chain enters at the arrival handover radius. The plan
            // still ends at the duration the caller asked for: the capture takes
            // only its own short fall, and the coast absorbs what the old
            // sphere-of-influence fall used to spend.
            double arrivalJd = t0 + totalSeconds / SecondsPerDay;
            TransferConic chain = BuildCaptureShape(ship, target, arrivalRadius, arrivalJd);
            if (!chain.IsValid)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "conic capture: " + chain.InvalidReason);
            }

            double captureSeconds = chain.TotalSeconds;
            double entryJd = arrivalJd - captureSeconds / SecondsPerDay;
            _model.ComputeState(target, entryJd,
                out Vector3d targetAtEntry, out Vector3d targetVelocityAtEntry);
            Vector3d entryPosition = targetAtEntry + chain.EntryLocalPositionM;
            Vector3d entryVelocity = targetVelocityAtEntry + chain.EntryLocalVelocityMps;

            double clearanceRadius = departureBand.DepartureRadiusM;
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

            // The approach leg. The brake's start state is the approach's end
            // state; running the target-centred conic that reaches it backwards
            // gives the ceiling state the heliocentric coast must arrive at.
            // When the brake's start is already outside the ceiling there is no
            // planetocentric region to model — the fast trips, where the brake
            // itself spans the sphere — and the coast runs to the brake's start
            // as it always did.
            double approachSeconds = 0.0;
            double approachStartJd = brakeStartJd;
            Vector3d approachStartPosition = entryPosition;
            Vector3d approachStartVelocity = entryVelocity;
            Vector3d approachStartLocalPosition = Vector3d.Zero;
            Vector3d approachStartLocalVelocity = Vector3d.Zero;
            bool hasApproach = false;

            // The coast's end: the approach's start on the ceiling when the leg
            // exists, otherwise the brake's start.
            double coastEndJd = brakeStartJd;
            Vector3d coastEndPosition = entryPosition;

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
            // pinned departure solve, and stop once the brake's own velocity
            // mismatch stops falling. A fast probe may stop at a looser tolerance
            // than the plan that is reported: the certified build is the
            // authority. A full-field probe keeps the certified tolerances — its
            // closure decision is meant to be the reported build's — but it is
            // still a probe, so it skips the settle passes.
            double brakeTolerance = fast ? FastBrakeConvergenceMps : BrakeConvergenceMps;
            double directionTolerance = fast ? FastBrakeDirectionTolerance
                : BrakeDirectionTolerance;
            double durationTolerance = fast ? FastBrakeDurationToleranceSeconds
                : BrakeDurationToleranceSeconds;

            double previousBurnSeconds = double.NaN;
            Vector3d previousRay = Vector3d.Zero;
            double brakeMismatch = double.PositiveInfinity;
            bool pinSettled = false;

            int outerPasses = fast ? FastProbeOuterPasses : outerPassesLimit;
            for (int pass = 0; pass < outerPasses; pass++)
            {
                _model.ComputeState(_model.Sun, entryJd,
                    out Vector3d sunAtEntry, out Vector3d sunVelocityAtEntry);
                Vector3d brakeLocalPosition;
                Vector3d brakeLocalVelocity;
                if (burnGravity == null)
                {
                    FiniteBurnArc.SimulateBackward(_model.Sun,
                        entryPosition - sunAtEntry, entryVelocity - sunVelocityAtEntry,
                        acceleration, retrograde: true, brakeSeconds, brakeSamples,
                        out brakeLocalPosition, out brakeLocalVelocity,
                        brakeDirection);
                }
                else
                {
                    FiniteBurnArc.SimulateBackward(_model, burnGravity, _model.Sun, entryJd,
                        entryPosition - sunAtEntry, entryVelocity - sunVelocityAtEntry,
                        acceleration, retrograde: true, brakeSeconds, brakeSamples,
                        out brakeLocalPosition, out brakeLocalVelocity,
                        brakeDirection);
                }

                brakeStartJd = entryJd - brakeSeconds / SecondsPerDay;
                _model.ComputeState(_model.Sun, brakeStartJd,
                    out Vector3d sunAtBrakeStart, out Vector3d sunVelocityAtBrakeStart);
                brakeStartPosition = sunAtBrakeStart + brakeLocalPosition;
                brakeStartVelocity = sunVelocityAtBrakeStart + brakeLocalVelocity;

                // The planetocentric approach: the target-centred conic that ends
                // at the brake's start and starts on the handover ceiling. The
                // heliocentric coast must arrive at that ceiling state, so the
                // deep part of the final approach is no longer a Sun-only conic.
                hasApproach = TryBuildApproach(target, arrivalBand.CeilingRadiusM,
                    brakeStartJd, brakeStartPosition, brakeStartVelocity,
                    out approachSeconds, out approachStartJd, out approachStartPosition,
                    out approachStartVelocity, out approachStartLocalPosition,
                    out approachStartLocalVelocity, out string approachReason);
                if (!hasApproach && !string.IsNullOrEmpty(approachReason))
                {
                    return StretchedPlan(ship, target, totalSeconds, approachReason);
                }

                // A free fall from the ceiling to a brake start this deep can take
                // longer than the whole trip: on the first pass the brake is a
                // second long, so its start is the chain's entry and the conic back
                // out to the ceiling is a near-parabolic 119 days. Start the fixed
                // point on the profile's original shape — the coast running to the
                // brake's start — and let the brake grow before the planetocentric
                // leg appears.
                if (pass == 0 || (hasApproach && approachStartJd <= t0))
                {
                    hasApproach = false;
                    approachSeconds = 0.0;
                    approachStartJd = brakeStartJd;
                    approachStartPosition = brakeStartPosition;
                    approachStartVelocity = brakeStartVelocity;
                    approachStartLocalPosition = Vector3d.Zero;
                    approachStartLocalVelocity = Vector3d.Zero;
                }

                coastEndJd = hasApproach ? approachStartJd : brakeStartJd;
                coastEndPosition = hasApproach ? approachStartPosition : brakeStartPosition;
                breakSeconds = SolveBreakSeconds(ship, departureBody, escapeDirection,
                    clearanceRadius, escapeSpeed, breakSeconds, coastEndJd,
                    burnSeconds, hasApproach ? 0.0 : brakeSeconds, cheap);

                burnStartJd = t0 + breakSeconds / SecondsPerDay;
                _model.ComputeState(departureBody, burnStartJd,
                    out Vector3d anchorPosition, out Vector3d anchorVelocity);
                burnStartPosition = anchorPosition + escapeDirection * clearanceRadius;
                burnStartVelocity = anchorVelocity + escapeDirection * escapeSpeed;

                if (!TrySolvePinnedDepartureWithFallback(ship, departureBody, clearanceRadius,
                        escapeSpeed, acceleration, burnStartJd, coastEndJd,
                        coastEndPosition, ref escapeDirection, ref burnSeconds,
                        solveSamples, fallbackSamples, burnGravity,
                        out coastStartPosition, out coastStartVelocity,
                        out coastArrivalVelocity, out coastSeconds, out string reason))
                {
                    return StretchedPlan(ship, target, totalSeconds, reason);
                }

                coastStartJd = burnStartJd + burnSeconds / SecondsPerDay;

                // The seam is the velocity the coast arrives with at the ceiling
                // (or at the brake's start without an approach) against the
                // velocity the planetocentric arc needs there. The brake's thrust
                // is adjusted by carrying the coast's arrival along that same
                // conic to the brake's start radius — a velocity change at the
                // ceiling is not a velocity change at the brake's start — and
                // then asking the burn for the change the chain's entry needs.
                Vector3d incoming = hasApproach ? approachStartVelocity : brakeStartVelocity;
                Vector3d seamVector = incoming - coastArrivalVelocity;
                brakeMismatch = seamVector.Magnitude;

                // The brake's thrust update. Without the approach it is the old
                // rule: the burn must begin with the velocity the coast arrives
                // with. With the approach the seam is at the ceiling, one fall
                // earlier, so the correction is the ceiling residual itself —
                // the free fall's gain is close to one, so adding the residual
                // to the thrust moves it by about its own size.
                Vector3d desiredStartVelocity = coastArrivalVelocity;
                Vector3d thrustDelta = brakeDirection * (acceleration * brakeSeconds);
                Vector3d gravityDelta = entryVelocity - brakeStartVelocity - thrustDelta;
                Vector3d required = hasApproach
                    ? thrustDelta + seamVector
                    : entryVelocity - desiredStartVelocity - gravityDelta;

                double nextBrakeSeconds = required.Magnitude / acceleration;
                Vector3d nextBrakeDirection = required.Normalized;

                bool converged = pass > 0
                    && brakeMismatch <= brakeTolerance
                    && Math.Abs(nextBrakeSeconds - brakeSeconds) <= durationTolerance
                    && (nextBrakeDirection - brakeDirection).Magnitude <= directionTolerance
                    && Math.Abs(burnSeconds - previousBurnSeconds) <= durationTolerance
                    && (escapeDirection - previousRay).Magnitude <= directionTolerance;

                Trace?.Invoke($"pin pass {pass}: brake {brakeSeconds:F3} s, " +
                    $"mismatch {brakeMismatch:E2} m/s, " +
                    $"burn {burnSeconds:F3} s, converged {converged}" +
                    (hasApproach ? $", approach {approachSeconds:F1} s" : ""));

                previousBurnSeconds = burnSeconds;
                previousRay = escapeDirection;
                brakeSeconds = nextBrakeSeconds;
                brakeDirection = nextBrakeDirection;
                if (converged)
                {
                    pinSettled = true;
                    break;
                }
            }

            // Settle the break and the departure against the brake the loop
            // finished with, so the arcs that are about to be built all belong
            // to the same ray. A converged loop needs one pass; a near-floor
            // probe whose fixed point is still drifting gets the old three.
            int settlePasses = cheap ? 0 : 3;
            for (int settle = 0; settle < settlePasses; settle++)
            {
                double previousSettleBurn = burnSeconds;
                Vector3d previousSettleRay = escapeDirection;

                breakSeconds = SolveBreakSeconds(ship, departureBody, escapeDirection,
                    clearanceRadius, escapeSpeed, breakSeconds, coastEndJd,
                    burnSeconds, hasApproach ? 0.0 : brakeSeconds, cheap);
                burnStartJd = t0 + breakSeconds / SecondsPerDay;
                if (!TrySolvePinnedDepartureWithFallback(ship, departureBody, clearanceRadius,
                        escapeSpeed, acceleration, burnStartJd, coastEndJd,
                        coastEndPosition, ref escapeDirection, ref burnSeconds,
                        solveSamples, fallbackSamples, burnGravity,
                        out coastStartPosition, out coastStartVelocity,
                        out coastArrivalVelocity, out coastSeconds, out string settleReason))
                {
                    return StretchedPlan(ship, target, totalSeconds, settleReason);
                }

                if (Math.Abs(burnSeconds - previousSettleBurn) <= durationTolerance
                    && (escapeDirection - previousSettleRay).Magnitude <= directionTolerance)
                {
                    break;
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
            if (Vector3d.Dot(brakeDirection, brakeStartVelocity) >= 0.0)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the coast arrives slower than the target's " +
                    "frame, so the crossing needs an acceleration rather than a brake");
            }

            // The reported burn integrates at the plan's resolution (512 or
            // ArcSamples), while the solve above integrated its trial burns at
            // PinnedDepartureSamples. Its end state is therefore not the state
            // the solve's coast ran from, so the coast is re-solved below from
            // where the reported burn actually ends: the position seam stays
            // exact and only the velocity seam carries the sample-count
            // difference (metres, not kilometres).
            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunForBurn, out Vector3d sunVelocityForBurn);
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                burnStartJd, burnSeconds, burnStartPosition - sunForBurn,
                burnStartVelocity - sunVelocityForBurn, acceleration,
                retrograde: false, samples: burnSamples,
                fixedThrustDirection: escapeDirection, gravity: burnGravity);

            // The coast is solved to the approach's start on the ceiling (or to
            // the brake's start when the brake begins outside the ceiling and
            // there is no planetocentric leg), and its departure velocity is
            // corrected against the conic propagator itself: the Lambert solver
            // and the propagator agree only to centimetres on a fast, long coast,
            // and the seam must be tighter than that. The correction moves the
            // departure velocity by metres per second at most, far inside the
            // velocity seam tolerance.
            double coastEndFinalJd = hasApproach ? approachStartJd : brakeStartJd;
            Vector3d coastEndFinalPosition = hasApproach
                ? approachStartPosition
                : brakeStartPosition;
            double coastFinalSeconds = (coastEndFinalJd - coastStartJd) * SecondsPerDay;
            _model.ComputeState(_model.Sun, coastStartJd,
                out Vector3d sunAtCoastStart, out Vector3d sunVelocityAtCoastStart);
            _model.ComputeState(_model.Sun, coastEndFinalJd,
                out Vector3d sunAtCoastEnd, out Vector3d sunVelocityAtCoastEnd);

            // The coast starts where the reported burn ends, not where the
            // solve's coarser trial burn ended.
            coastStartPosition = departureBurn.EndLocalPositionM + sunAtCoastStart;

            LambertSolution coastSolution = LambertSolver.SolveShortWay(
                coastStartPosition, coastEndFinalPosition, coastFinalSeconds, _model.Sun.Gm);
            if (!coastSolution.HasSolution)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    CoastReason(coastSolution));
            }

            Vector3d coastStartFinalVelocity = coastSolution.DepartureVelocityMps;
            Vector3d localTarget = coastEndFinalPosition - sunAtCoastEnd;
            if (!TryCorrectCoastDeparture(_model.Sun,
                    coastStartPosition - sunAtCoastStart,
                    coastStartFinalVelocity - sunVelocityAtCoastStart,
                    coastFinalSeconds, localTarget,
                    out Vector3d correctedLocalVelocity))
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "burn-coast-burn: the coast cannot be joined to the brake's start");
            }

            coastStartFinalVelocity = correctedLocalVelocity + sunVelocityAtCoastStart;

            ConicArc coast = ConicArc.Create(_model, _model.Sun, coastStartJd, coastFinalSeconds,
                coastStartPosition - sunAtCoastStart,
                coastStartFinalVelocity - sunVelocityAtCoastStart);

            // The approach is the planetocentric conic from the ceiling to the
            // brake's start, built from the ceiling state so its own two-body
            // elements are the ones the free fall flies.
            PlanArc approach = null;
            if (hasApproach)
            {
                ConicArc approachPath = ConicArc.Create(_model, target, approachStartJd,
                    approachSeconds, approachStartLocalPosition, approachStartLocalVelocity);
                approach = MakeConicArc(FlightPhase.Approach, target.Id, approachStartJd,
                    brakeStartJd, approachPath, Vector3d.Zero, cheap);
            }

            // The brake is the exact reverse of the backward solve, so it begins
            // on the approach's end state and lands on the chain's entry state.
            // Without an approach the start keeps the coast's own arrival velocity,
            // which is the state the fixed point drove the backward start to and
            // what the earlier profile flew.
            _model.ComputeState(_model.Sun, brakeStartJd,
                out Vector3d sunAtBrakeArc, out Vector3d sunVelocityAtBrakeArc);
            Vector3d brakeStartFinalVelocity = hasApproach
                ? brakeStartVelocity
                : coastSolution.ArrivalVelocityMps;
            FiniteBurnArc brake = FiniteBurnArc.Create(_model, _model.Sun,
                brakeStartJd, brakeSeconds, brakeStartPosition - sunAtBrakeArc,
                brakeStartFinalVelocity - sunVelocityAtBrakeArc, acceleration,
                retrograde: true, samples: brakeSamples,
                fixedThrustDirection: brakeDirection, gravity: burnGravity);

            var arcs = new List<PlanArc>(9);
            arcs.Add(MakeArc(FlightPhase.Departure, departureBody.Id, t0, burnStartJd,
                ship.PositionM, ship.VelocityMps, burnStartPosition, burnStartVelocity,
                fast: cheap));
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Cruise, _model.Sun.Id,
                burnStartJd, coastStartJd, departureBurn, cheap));

            arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, coastStartJd,
                coastEndFinalJd, coast, Vector3d.Zero, cheap));

            if (approach != null)
                arcs.Add(approach);

            if (brakeSeconds > 0.0)
            {
                arcs.Add(MakeFiniteBurnArc(FlightPhase.Capture, _model.Sun.Id,
                    brakeStartJd, entryJd, brake, cheap));
            }

            double parkingEndJd;
            ConicArc parking;
            double finiteBurnAcceleration = acceleration;

            if (ArrivalStyle == CaptureStyle.FiniteBurn
                && FixedBurnAccelerationMps2 > 0.0)
            {
                if (!AppendHybridCapture(arcs, target, entryJd, chain, Vector3d.Zero,
                        out parkingEndJd, out parking, out string reason,
                        out finiteBurnAcceleration, cheap, probe))
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "burn-coast-burn: hybrid capture: " + reason);
                }
            }
            else
            {
                AppendConicCapture(arcs, target, entryJd, chain, Vector3d.Zero,
                    out parkingEndJd, out parking, cheap);
            }

            // Every fixed burn must clear the body it is working near. The old
            // rule was that the departure burn stay outside the departure sphere
            // of influence; a burn may now cross a sphere of influence, because
            // it integrates the summed field, but it may not cut a body.
            if (!FixedBurnsClearBodies(arcs, departureBody, target, out string clearanceReason))
                return StretchedPlan(ship, target, totalSeconds, clearanceReason);

            double limit = MaxAccelerationMps2 * ThrustMargin;
            double worst = 1.0;
            foreach (PlanArc arc in arcs)
            {
                if (arc.PeakThrustMps2 <= 0.0) continue;
                worst = Math.Max(worst, arc.PeakThrustMps2 / limit);
            }

            // A plan inside the drive whose brake does not meet the capture
            // entry is not flyable, whatever its peak says. The near-floor trips
            // amplify the brake solve's mismatch, so report the trip time as too
            // short rather than handing the autopilot a broken chain. An
            // over-thrust plan is already rejected for its peak, so keep that
            // cause instead.
            if (worst <= 1.0 / ThrustMargin
                && !BurnChainIsClosed(arcs, out string seamReason))
            {
                // A chain miss after an unsettled pin is a solver artefact, not a
                // verdict on the trip time: the certified build retries the same
                // fixed point with the longer pass cap once (see Build).
                return StretchedPlan(ship, target, totalSeconds,
                    "burn-coast-burn: " + seamReason,
                    pinStalled: !fast && !probe && !pinSettled);
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
            plan.CaptureEntryRadiusM = chain.EntryRadiusM;
            plan.CaptureInboundSeconds = chain.InboundSeconds;

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
            double candidateSeconds, double coastEndJd, double burnSeconds,
            double reservedTailSeconds, bool cheap = false)
        {
            double limit = MaxAccelerationMps2 * ThrustMargin;
            // The reserved tail is the brake's duration when the coast ends at
            // the brake's start; the approach leg already takes the brake and its
            // own fall out of the span when it exists, so the caller passes zero.
            double available = (coastEndJd - ship.JulianDate) * SecondsPerDay
                               - burnSeconds - reservedTailSeconds - MinimumCoastSeconds;
            double cap = Math.Max(MinimumArcSeconds, available);
            double candidate = Math.Clamp(candidateSeconds, MinimumArcSeconds, cap);

            // A probe measures the break against its own central body and skips
            // the full-field certification; the certified build at the end of
            // the floor search decides with the field the plan reports.
            double Peak(double seconds) => cheap
                ? BreakPeakCentralBody(ship, departureBody, direction, clearanceRadius,
                    escapeSpeed, seconds)
                : BreakPeakMps2(ship, departureBody, direction, clearanceRadius,
                    escapeSpeed, seconds);

            if (Peak(candidate) <= limit)
                return candidate;

            if (!(available > candidate) || Peak(cap) > limit)
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
                if (Peak(high) <= limit)
                    break;
            }

            // The crossing is a smooth function of the break's duration, so a
            // tolerance-based bisection is enough; a probe stops a few steps
            // early because only the certified build's break is reported.
            double tolerance = cheap ? 1e-3 * high : 0.0;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                double middle = 0.5 * (low + high);
                if (tolerance > 0.0 && high - low <= tolerance)
                    break;
                if (Peak(middle) <= limit)
                    high = middle;
                else
                    low = middle;
            }

            return high;
        }

        /// <summary>The quintic break's peak commanded thrust for one duration.
        /// The search uses the departure body's gravity alone: the break's
        /// meaningful field is its own central body, and evaluating the full
        /// field at 64 samples per probe is what made the profile freeze the nav
        /// computer. The endpoint accelerations stay full-field, so the quintic
        /// is exactly the arc the final plan builds; only the along-arc gravity
        /// subtraction is reduced. Inside <see cref="BreakScreenMargin"/> of the
        /// drive limit the full field certifies the probe, because there the
        /// omitted Sun and third-body terms decide whether the break fits.</summary>
        private double BreakPeakMps2(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, double clearanceRadius, double escapeSpeed,
            double seconds)
        {
            double fast = BreakPeakCentralBody(ship, departureBody, direction,
                clearanceRadius, escapeSpeed, seconds);
            double limit = MaxAccelerationMps2 * ThrustMargin;
            if (fast <= limit * (1.0 - BreakScreenMargin))
                return fast;

            return BreakPeakFullField(ship, departureBody, direction,
                clearanceRadius, escapeSpeed, seconds);
        }

        /// <summary>The break peak with only the departure body's gravity. The
        /// quintic is built exactly as <see cref="MakeArc"/> builds the final
        /// break, so the reported and fast arcs share a shape; the samples
        /// subtract the central body instead of the full catalogue.</summary>
        private double BreakPeakCentralBody(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, double clearanceRadius, double escapeSpeed,
            double seconds)
        {
            double startJd = ship.JulianDate;
            double endJd = startJd + seconds / SecondsPerDay;
            _model.ComputeState(departureBody, endJd,
                out Vector3d anchorPosition, out Vector3d anchorVelocity);
            Vector3d endPosition = anchorPosition + direction * clearanceRadius;
            Vector3d endVelocity = anchorVelocity + direction * escapeSpeed;

            // Full-field endpoint accelerations keep this curve identical to the
            // final plan's break arc. Only two Prepare calls are paid per probe;
            // the samples evaluate one body's state, not the whole catalogue.
            _gravity.Prepare(ship.PositionM, startJd);
            Vector3d startGravity = _gravity.AccelerationAt(ship.PositionM, startJd);
            _gravity.Prepare(endPosition, endJd);
            Vector3d endGravity = _gravity.AccelerationAt(endPosition, endJd);

            Quintic path = Quintic.Create(ship.PositionM, ship.VelocityMps, startGravity,
                endPosition, endVelocity, endGravity, seconds);

            int samples = Math.Max(8, ArcSamples);
            double peak = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double magnitude = BreakThrustAt(path, departureBody, startJd,
                    (double)i / samples);
                if (magnitude > peak) peak = magnitude;
            }

            return peak;
        }

        /// <summary>Thrust the break quintic needs at a parameter, against the
        /// departure body's gravity alone.</summary>
        private double BreakThrustAt(Quintic path, BodyRuntime departureBody,
            double startJd, double u)
        {
            Vector3d position = path.PositionAt(u);
            double julianDate = startJd + path.SecondsAt(u) / SecondsPerDay;
            return (path.AccelerationAt(u)
                    - CentralBodyGravity(departureBody, position, julianDate)).Magnitude;
        }

        /// <summary>The fast peak of an already-built quintic, against its own
        /// reference body alone. The floor probes use this instead of the full
        /// <see cref="Measure"/>, so a probe pays one body's ephemeris per
        /// sample rather than the whole catalogue.</summary>
        private double BreakPeakCentralBodyAlongPath(IArcPath path, double startJd,
            BodyRuntime body)
        {
            int samples = Math.Max(8, ArcSamples);
            double peak = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                Vector3d position = path.PositionAt(u);
                double julianDate = startJd + path.SecondsAt(u) / SecondsPerDay;
                double magnitude = (path.AccelerationAt(u)
                                    - CentralBodyGravity(body, position, julianDate)).Magnitude;
                if (magnitude > peak) peak = magnitude;
            }

            return peak;
        }

        /// <summary>One body's softened point-mass acceleration at a position.
        /// Mirrors <see cref="GravityField"/>'s per-source term, so the fast break
        /// model is the full field minus every other body.</summary>
        private Vector3d CentralBodyGravity(BodyRuntime body, Vector3d positionM,
            double julianDate)
        {
            _model.ComputeState(body, julianDate,
                out Vector3d bodyPosition, out _);
            Vector3d delta = bodyPosition - positionM;
            double radius2 = Math.Max(delta.SqrMagnitude, body.RadiusM * body.RadiusM);
            double inverse = 1.0 / Math.Sqrt(radius2);
            return delta * (body.Gm * inverse * inverse * inverse);
        }

        /// <summary>The full-field break peak: the same quintic the plan reports,
        /// sampled through the complete field. The field is prepared once at the
        /// break's start, where the departure body and its moons are strongest,
        /// and that selected set is evaluated at every sample. The break only
        /// climbs outward, so no new body becomes significant along it; this is
        /// the field <see cref="Measure"/> sees, without recomputing every
        /// catalogue ephemeris at each sample. Used only inside the screen band,
        /// so the common path stays central-body-only.</summary>
        private double BreakPeakFullField(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, double clearanceRadius, double escapeSpeed,
            double seconds)
        {
            double startJd = ship.JulianDate;
            double endJd = startJd + seconds / SecondsPerDay;
            _model.ComputeState(departureBody, endJd,
                out Vector3d anchorPosition, out Vector3d anchorVelocity);
            Vector3d endPosition = anchorPosition + direction * clearanceRadius;
            Vector3d endVelocity = anchorVelocity + direction * escapeSpeed;

            // Endpoint accelerations exactly as MakeArc builds them, so the
            // certified curve and the plan's break arc share their shape.
            _gravity.Prepare(ship.PositionM, startJd);
            Vector3d startGravity = _gravity.AccelerationAt(ship.PositionM, startJd);
            _gravity.Prepare(endPosition, endJd);
            Vector3d endGravity = _gravity.AccelerationAt(endPosition, endJd);

            Quintic path = Quintic.Create(ship.PositionM, ship.VelocityMps, startGravity,
                endPosition, endVelocity, endGravity, seconds);

            return BreakPeakFullFieldAlongPath(path, startJd);
        }

        /// <summary>Full-field peak of an already-built break path. The field is
        /// prepared once at the path's start, where the departure body and its
        /// moons are strongest, and that selected set is evaluated at every
        /// sample; the outward break cannot make a new body significant.</summary>
        private double BreakPeakFullFieldAlongPath(IArcPath path, double startJd)
        {
            _gravity.Prepare(path.PositionAt(0.0), startJd);
            int samples = Math.Max(8, ArcSamples);
            double peak = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                Vector3d position = path.PositionAt(u);
                double julianDate = startJd + path.SecondsAt(u) / SecondsPerDay;
                double magnitude = (path.AccelerationAt(u)
                                    - _gravity.AccelerationAt(position, julianDate)).Magnitude;
                if (magnitude > peak) peak = magnitude;
            }

            return peak;
        }

        /// <summary>Diagnostic only: measure a returned plan's break arc with the
        /// departure body's gravity alone (the search's fast model), with the
        /// full field (the plan's reported measurement), and with the screen the
        /// break search applies. The planner never returns these numbers; the
        /// tests use them to bound the fast model and prove the 5% screen picks
        /// the full field when the fast peak is near the drive limit.</summary>
        public void MeasureBreakPeak(TrajectoryPlan plan, out double fastPeakMps2,
            out double fullPeakMps2, out double screenedPeakMps2)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            PlanArc arc = plan.Arcs[0];
            if (arc.Phase != FlightPhase.Departure || arc.IsCoast || arc.IsFiniteBurn)
                throw new ArgumentException(
                    "The plan's first arc is not a quintic break.", nameof(plan));

            BodyRuntime departureBody = _model.Get(arc.ReferenceBodyId)
                ?? throw new ArgumentException(
                    $"No body '{arc.ReferenceBodyId}' for the break arc.", nameof(plan));

            int samples = Math.Max(8, ArcSamples);
            double peak = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                Vector3d position = arc.Path.PositionAt(u);
                double julianDate = arc.StartJulianDate
                                    + arc.Path.SecondsAt(u) / SecondsPerDay;
                double magnitude = (arc.Path.AccelerationAt(u)
                                    - CentralBodyGravity(departureBody, position,
                                        julianDate)).Magnitude;
                if (magnitude > peak) peak = magnitude;
            }

            fastPeakMps2 = peak;
            fullPeakMps2 = arc.PeakThrustMps2;
            double limit = MaxAccelerationMps2 * ThrustMargin;
            screenedPeakMps2 = fastPeakMps2 <= limit * (1.0 - BreakScreenMargin)
                ? fastPeakMps2
                : fullPeakMps2;
        }

        /// <summary>Diagnostic only: re-measure a returned plan's break with the
        /// screen's one-prepare full-field sweep. The tests compare it with the
        /// plan's own <see cref="Measure"/> peak, which prepares the field at
        /// every sample.</summary>
        public double MeasureBreakPeakFullField(TrajectoryPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            PlanArc arc = plan.Arcs[0];
            if (arc.Phase != FlightPhase.Departure || arc.IsCoast || arc.IsFiniteBurn)
                throw new ArgumentException(
                    "The plan's first arc is not a quintic break.", nameof(plan));

            return BreakPeakFullFieldAlongPath(arc.Path, arc.StartJulianDate);
        }

        /// <summary>
        /// Solve the departure burn's escape ray and duration so that a burn
        /// integrated forward from the pinned clearance state ends on the Lambert
        /// coast's departure velocity. Three equations (the velocity mismatch) in
        /// three unknowns (two ray angles and the burn's duration), solved by a
        /// damped Newton with a finite-difference Jacobian. The coast runs from the
        /// burn's end to the coast's end in the time the trip leaves it.
        /// </summary>
        private bool TrySolvePinnedDeparture(ShipState ship, BodyRuntime departureBody,
            double clearanceRadius, double escapeSpeed, double acceleration,
            double burnStartJd, double coastEndJd, Vector3d coastEnd,
            ref Vector3d direction, ref double burnSeconds, int samples,
            IGravityField burnGravity,
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
            var jacobian = new double[3, 3];
            var rhs = new double[3];
            int jacobianAge = int.MaxValue;

            for (int iteration = 0; iteration < DepartureSolveIterations; iteration++)
            {
                if (!EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, clearanceRadius, escapeSpeed,
                        acceleration, burnStartJd, coastEndJd, coastEnd,
                        angle1, angle2, seconds, samples, burnGravity,
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
                if (residual <= DepartureSolveToleranceMps)
                {
                    direction = Ray(direction, tangent1, tangent2, angle1, angle2);
                    burnSeconds = seconds;
                    return true;
                }

                // A full finite-difference Jacobian costs six evaluations. The
                // curve is smooth and the Newton step is damped, so reusing it
                // for one extra iteration halves the solve's cost without
                // changing where it converges: the residual check above still
                // decides when the seam is good enough.
                if (jacobianAge >= 2)
                {
                    double angleStep = 1e-3;
                    double timeStep = Math.Max(0.5, Math.Min(10.0, 0.001 * seconds));
                    if (!EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            angle1 + angleStep, angle2, seconds, samples, burnGravity,
                            out Vector3d mismatch1, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            angle1 - angleStep, angle2, seconds, samples, burnGravity,
                            out Vector3d mismatch1Low, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            angle1, angle2 + angleStep, seconds, samples, burnGravity,
                            out Vector3d mismatch2, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            angle1, angle2 - angleStep, seconds, samples, burnGravity,
                            out Vector3d mismatch2Low, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            angle1, angle2, seconds + timeStep, samples, burnGravity,
                            out Vector3d mismatch3, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, clearanceRadius, escapeSpeed,
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            angle1, angle2, seconds - timeStep, samples, burnGravity,
                            out Vector3d mismatch3Low, out _, out _, out _, out _))
                    {
                        reason = "burn-coast-burn: the departure burn has no coast at " +
                                 "this trip time";
                        return false;
                    }

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
                    }

                    jacobianAge = 0;
                }
                else
                {
                    jacobianAge++;
                }

                for (int row = 0; row < 3; row++) rhs[row] = -Component(mismatch, row);

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
                            acceleration, burnStartJd, coastEndJd, coastEnd,
                            trialAngle1, trialAngle2, trialSeconds, samples, burnGravity,
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

        /// <summary>
        /// Solve the pinned departure at the cheap trial resolution, retrying once
        /// at the plan's resolution when the coarse solve stalls. The tolerance is
        /// identical at both; the retry exists because a runtime's last-bit libm
        /// difference can leave the coarse iteration a fraction above
        /// <see cref="DepartureSolveToleranceMps"/> — measured at Uranus 35 d at
        /// 1 g in Mono — and a fixed-duration anchor must not turn into a refusal
        /// because the build got cheaper. A fast probe has no fallback: it is the
        /// search's cheap decision, and the certified build after it is the
        /// authority.
        /// </summary>
        private bool TrySolvePinnedDepartureWithFallback(ShipState ship,
            BodyRuntime departureBody, double clearanceRadius, double escapeSpeed,
            double acceleration, double burnStartJd, double coastEndJd,
            Vector3d coastEnd, ref Vector3d direction, ref double burnSeconds,
            int samples, int fallbackSamples, IGravityField burnGravity,
            out Vector3d coastStartPosition, out Vector3d coastStartVelocity,
            out Vector3d coastArrivalVelocity, out double coastSeconds,
            out string reason)
        {
            if (TrySolvePinnedDeparture(ship, departureBody, clearanceRadius,
                    escapeSpeed, acceleration, burnStartJd, coastEndJd, coastEnd,
                    ref direction, ref burnSeconds, samples, burnGravity,
                    out coastStartPosition, out coastStartVelocity,
                    out coastArrivalVelocity, out coastSeconds, out reason))
            {
                return true;
            }

            if (fallbackSamples <= samples) return false;

            return TrySolvePinnedDeparture(ship, departureBody, clearanceRadius,
                escapeSpeed, acceleration, burnStartJd, coastEndJd, coastEnd,
                ref direction, ref burnSeconds, fallbackSamples, burnGravity,
                out coastStartPosition, out coastStartVelocity,
                out coastArrivalVelocity, out coastSeconds, out reason);
        }

        private bool EvaluatePinnedDeparture(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, Vector3d tangent1, Vector3d tangent2,
            double clearanceRadius, double escapeSpeed, double acceleration,
            double burnStartJd, double coastEndJd, Vector3d coastEnd,
            double angle1, double angle2, double seconds, int samples,
            IGravityField burnGravity,
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
            coastSeconds = (coastEndJd - coastStartJd) * SecondsPerDay;
            if (!(coastSeconds > MinimumArcSeconds)) return false;

            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunAtStart, out Vector3d sunVelocityAtStart);
            FiniteBurnArc burn = FiniteBurnArc.Create(_model, _model.Sun, burnStartJd,
                seconds, startPosition - sunAtStart, startVelocity - sunVelocityAtStart,
                acceleration, retrograde: false, samples: samples,
                fixedThrustDirection: ray, gravity: burnGravity);

            _model.ComputeState(_model.Sun, coastStartJd,
                out Vector3d sunAtEnd, out Vector3d sunVelocityAtEnd);
            coastStartPosition = burn.EndLocalPositionM + sunAtEnd;
            Vector3d burnEndVelocity = burn.EndLocalVelocityMps + sunVelocityAtEnd;

            LambertSolution coast = LambertSolver.SolveShortWay(
                coastStartPosition, coastEnd, coastSeconds, _model.Sun.Gm);
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
        /// coast; this closes the coast's end seam to about a millimetre.
        ///
        /// The tolerance is a millimetre rather than a tenth: a coast of weeks at
        /// 1.5 AU cannot resolve its end position below about 10⁻⁴ m, so a tighter
        /// target leaves the Newton cycle in its round-off and reports a failure
        /// that is not there. A millimetre is three orders below the seam gate and
        /// four below the plan's own closure gate.
        /// </summary>
        private static bool TryCorrectCoastDeparture(BodyRuntime sun,
            Vector3d localStartPosition, Vector3d localStartVelocity,
            double seconds, Vector3d localTarget, out Vector3d correctedVelocity)
        {
            correctedVelocity = localStartVelocity;
            double tolerance = 1e-3;

            for (int iteration = 0; iteration < 16; iteration++)
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

        /// <summary>
        /// True when every seam between adjacent arcs closes: position is shared
        /// and velocity differs only by the arc's declared impulse. This is a
        /// broken-chain gate, not the seam-quality specification; the solve aims
        /// at 1e-5 m/s, and a metre or a millimetre per second here means the
        /// brake missed the capture entry by orders of magnitude.
        /// </summary>
        private static bool BurnChainIsClosed(List<PlanArc> arcs, out string reason)
        {
            reason = null;
            for (int i = 0; i < arcs.Count - 1; i++)
            {
                PlanArc left = arcs[i];
                PlanArc right = arcs[i + 1];
                double positionStep = Vector3d.Distance(
                    left.Path.PositionAt(1.0), right.Path.PositionAt(0.0));
                double velocityStep = (left.Path.VelocityAt(1.0)
                                       + right.EntryImpulseVectorMps
                                       - right.Path.VelocityAt(0.0)).Magnitude;

                if (positionStep <= 1e3 && velocityStep <= 1.0) continue;

                string seam = left.Phase == FlightPhase.Capture
                              && right.Phase == FlightPhase.Coast
                    ? "the brake does not close on the capture entry"
                    : $"the {left.Phase} and {right.Phase} arcs do not meet";
                reason = $"{seam} at this trip time ({positionStep:F1} m, " +
                         $"{velocityStep:F1} m/s)";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether one fixed burn clears the body it is working near by
        /// <see cref="HandoverBand.MinimumClearanceAltitudeM"/>. The closest
        /// sampled radius from the body's centre is returned whether or not it
        /// clears, so a rejection can name how close the arc came. The sphere of
        /// influence is not consulted: a burn may cross it.
        /// </summary>
        public bool FixedBurnClearsBody(PlanArc arc, BodyRuntime body, out double closestRadiusM)
        {
            if (arc == null) throw new ArgumentNullException(nameof(arc));
            if (body == null) throw new ArgumentNullException(nameof(body));

            closestRadiusM = double.PositiveInfinity;
            int samples = Math.Max(8, ArcSamples);
            for (int sample = 0; sample <= samples; sample++)
            {
                double u = (double)sample / samples;
                double julianDate = arc.StartJulianDate
                                    + arc.Path.SecondsAt(u) / SecondsPerDay;
                _model.ComputeState(body, julianDate, out Vector3d bodyPosition, out _);
                double radius = Vector3d.Distance(arc.Path.PositionAt(u), bodyPosition);
                if (radius < closestRadiusM) closestRadiusM = radius;
            }

            double required = body.RadiusM + HandoverBand.MinimumClearanceAltitudeM;
            return closestRadiusM >= required * (1.0 - 1e-9);
        }

        /// <summary>
        /// The body-clearance gate on a plan's fixed burns: the departure burn is
        /// held clear of the departure body, and the arrival burns — the brake,
        /// then the hybrid capture's injection burn — are held clear of the
        /// target. The reason names the arc and how close it came.
        /// </summary>
        private bool FixedBurnsClearBodies(List<PlanArc> arcs, BodyRuntime departureBody,
            BodyRuntime target, out string reason)
        {
            reason = null;
            int arrivalBurns = 0;

            foreach (PlanArc arc in arcs)
            {
                if (!arc.IsFiniteBurn) continue;

                bool arrival = arc.Phase == FlightPhase.Capture;
                if (arrival) arrivalBurns++;
                BodyRuntime near = arrival ? target : departureBody;

                if (FixedBurnClearsBody(arc, near, out double closest)) continue;

                // The profile's arrival burns are the brake first and the
                // hybrid capture's injection burn second.
                string name = arrival
                    ? (arrivalBurns == 1 ? "brake" : "capture")
                    : "departure";
                double required = near.RadiusM + HandoverBand.MinimumClearanceAltitudeM;
                reason = $"burn-coast-burn: the {name} fixed burn passes " +
                         $"{closest / 1000.0:F0} km from {near.Name}'s centre, inside the " +
                         $"{required / 1000.0:F0} km body clearance " +
                         $"({near.RadiusM / 1000.0:F0} km radius + " +
                         $"{HandoverBand.MinimumClearanceAltitudeM / 1000.0:F0} km)";
                return false;
            }

            return true;
        }

        private BodyRuntime FindDepartureBody(ShipState ship, BodyRuntime target,
            double julianDate)
        {
            foreach (BodyRuntime planet in _model.Sun.Children)
            {
                if (planet == target || planet == target.Parent) continue;

                double soi = SphereOfInfluence.RadiusM(planet);
                if (!double.IsFinite(soi)) continue;

                _model.ComputeState(planet, julianDate, out Vector3d planetPosition, out _);
                if (Vector3d.Distance(ship.PositionM, planetPosition) < soi)
                    return planet;
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
            out double parkingEndJd, out ConicArc parking, bool fast = false)
        {
            double periapsisJd = cruiseEndJd + chain.InboundSeconds / SecondsPerDay;
            double apoapsisJd = periapsisJd + chain.TransferSeconds / SecondsPerDay;
            parkingEndJd = apoapsisJd + chain.ParkingSeconds / SecondsPerDay;

            ConicArc inbound = ConicArc.Create(_model, target, cruiseEndJd,
                chain.InboundSeconds, chain.EntryLocalPositionM, chain.EntryLocalVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Coast, target.Id, cruiseEndJd, periapsisJd,
                inbound, inboundImpulseMps, fast));

            ConicArc transfer = ConicArc.Create(_model, target, periapsisJd,
                chain.TransferSeconds, chain.PeriapsisLocalPositionM,
                chain.TransferPeriapsisVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Capture, target.Id, periapsisJd, apoapsisJd,
                transfer, chain.CaptureBurnVectorMps, fast));

            parking = ConicArc.Create(_model, target, apoapsisJd,
                chain.ParkingSeconds, chain.ApoapsisLocalPositionM,
                chain.CircularApoapsisVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Parking, target.Id, apoapsisJd, parkingEndJd,
                parking, chain.CircularisationBurnVectorMps, fast));
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
            out double accelerationMps2, bool fast = false, bool probe = false)
        {
            bool cheap = fast || probe;
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

            // The hybrid solver's own grid is hundreds of radius integrations, so
            // it stays the two-body search model; the burn arc the plan reports
            // below is rebuilt full-field and its trim is taken from that arc, so
            // the plan still flies what it reports. Measured at the epoch, the
            // full-field burn ends within 0.2 km of the two-body injection
            // radius, against the 5 km injection margin.
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
                inbound, inboundImpulseMps, cheap));

            FiniteBurnArc captureBurn = FiniteBurnArc.Create(_model, target, burnStartJd,
                capture.BurnSeconds, capture.BurnStartLocalPositionM,
                capture.BurnStartLocalVelocityMps, accelerationMps2, retrograde: true,
                gravity: fast && !probe ? null : _gravity);
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Capture, target.Id, burnStartJd,
                burnEndJd, captureBurn, cheap));

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
                transferEndJd, transfer, trim, cheap));

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
                parkingEndJd, parking, circularisationFlown, cheap));

            return true;
        }

        /// <summary>The capture chain's duration at one entry radius. Only the
        /// inbound fall moves with the radius: the periapsis burn, the transfer,
        /// and the parking coast are set by the entry speed and the radii.</summary>
        private double EstimateConicCaptureSeconds(BodyRuntime target, double entryRadiusM)
        {
            TransferConic chain = TransferConic.Build(target, new Vector3d(1.0, 0.0, 0.0),
                entryRadiusM, EntrySpeedMps,
                ParkingOrbit.ConicRadiusM(target), ParkingOrbit.PeriapsisRadiusM(target));
            return chain.IsValid ? chain.TotalSeconds : 0.0;
        }

        /// <summary>The radius the capture chain is handed over at.
        /// <paramref name="entryRadiusM"/> is the branch's entry radius: the
        /// sphere of influence for the coast, the arrival handover floor for the
        /// burn–coast–burn.</summary>
        private TransferConic BuildCaptureShape(ShipState ship, BodyRuntime target,
            double entryRadiusM, double arrivalJd)
        {
            _model.ComputeState(target, arrivalJd, out Vector3d targetPosition, out _);
            Vector3d approach = (ship.PositionM - targetPosition).Normalized;
            if (approach.SqrMagnitude < 0.5) approach = new Vector3d(1.0, 0.0, 0.0);

            return TransferConic.Build(target, approach, entryRadiusM, EntrySpeedMps,
                ParkingOrbit.ConicRadiusM(target), ParkingOrbit.PeriapsisRadiusM(target));
        }

        /// <summary>
        /// The burn–coast–burn's arrival handover radius: the band's floor, raised
        /// to the radius the capture chain captures into when the floor is deeper
        /// than that. The floor is a lower bound on the handover, not a promise
        /// the chain can start there: at 1 g on Mercury and Mars, and on several
        /// small moons, the clamped floor sits inside the conic periapsis. An
        /// entry at the parking radius is above the periapsis by construction and
        /// falls into the orbit the chain ends on
        /// ([the powered-leg floor](/simulation/powered-leg-floor.md#the-floor-is-clamped-to-the-body)).
        /// </summary>
        public static double ArrivalEntryRadiusM(HandoverBand band, BodyRuntime target)
        {
            if (band == null) throw new ArgumentNullException(nameof(band));
            if (target == null) throw new ArgumentNullException(nameof(target));
            return Math.Max(band.ArrivalRadiusM, ParkingOrbit.ConicRadiusM(target));
        }

        /// <summary>
        /// The planetocentric approach leg: the two-body conic about the target
        /// that ends at the brake's start state and starts on the handover band's
        /// ceiling. The conic is the free fall the coast's arrival would make, so
        /// it is built backwards from the brake's start — the state a forward
        /// brake must leave from — to the ceiling.
        ///
        /// Returns false, with no reason, when the brake's start is already
        /// outside the ceiling: the fast trips, whose brake spans the sphere, have
        /// no planetocentric region for a conic to describe, and the coast runs to
        /// the brake's start instead. Returns false with a reason when the brake's
        /// start is inside the ceiling but its conic cannot climb back out to it.
        /// </summary>
        private bool TryBuildApproach(BodyRuntime target, double ceilingRadiusM,
            double brakeStartJd, Vector3d brakeStartPosition, Vector3d brakeStartVelocity,
            out double approachSeconds, out double approachStartJd,
            out Vector3d approachStartPosition, out Vector3d approachStartVelocity,
            out Vector3d approachStartLocalPosition, out Vector3d approachStartLocalVelocity,
            out string reason)
        {
            approachSeconds = 0.0;
            approachStartJd = brakeStartJd;
            approachStartPosition = brakeStartPosition;
            approachStartVelocity = brakeStartVelocity;
            approachStartLocalPosition = Vector3d.Zero;
            approachStartLocalVelocity = Vector3d.Zero;
            reason = null;

            _model.ComputeState(target, brakeStartJd,
                out Vector3d targetPosition, out Vector3d targetVelocity);
            Vector3d localEndPosition = brakeStartPosition - targetPosition;
            Vector3d localEndVelocity = brakeStartVelocity - targetVelocity;
            if (localEndPosition.Magnitude >= ceilingRadiusM) return false;

            if (!TryInboundRadiusCrossing(target, localEndPosition, localEndVelocity,
                    ceilingRadiusM, out double seconds, out Vector3d ceilingLocalPosition,
                    out Vector3d ceilingLocalVelocity))
            {
                reason = "burn-coast-burn: the approach conic cannot reach the " +
                         "handover ceiling from the brake's start";
                return false;
            }

            // The crossing is before the brake's start when the arc is a fall, so
            // the signed time is negative. A crossing after it would mean the
            // brake starts outbound, past the approach's periapsis.
            if (!(seconds < -MinimumArcSeconds))
            {
                reason = "burn-coast-burn: the brake starts past the approach's " +
                         "periapsis, where the planetocentric arc is already outward";
                return false;
            }

            approachSeconds = -seconds;
            approachStartJd = brakeStartJd - approachSeconds / SecondsPerDay;
            _model.ComputeState(target, approachStartJd,
                out Vector3d targetAtStart, out Vector3d targetVelocityAtStart);
            approachStartLocalPosition = ceilingLocalPosition;
            approachStartLocalVelocity = ceilingLocalVelocity;
            approachStartPosition = targetAtStart + ceilingLocalPosition;
            approachStartVelocity = targetVelocityAtStart + ceilingLocalVelocity;
            return true;
        }

        /// <summary>
        /// The signed time from a body-relative state to where its two-body conic
        /// reaches <paramref name="radiusM"/> on the <b>inbound</b> branch.
        /// Negative when that point is before the state, positive when it is
        /// after. The approach uses it both ways: backwards from the brake's start
        /// to the ceiling, and forwards from the coast's arrival to the brake's
        /// start radius. The crossing is solved from the conic's own elements
        /// rather than by propagation, so the returned state is exactly on the
        /// radius and the seam lands where the arc's own time map puts it.
        /// </summary>
        private static bool TryInboundRadiusCrossing(BodyRuntime target, Vector3d localPositionM,
            Vector3d localVelocityMps, double radiusM, out double seconds,
            out Vector3d crossingLocalPositionM, out Vector3d crossingLocalVelocityMps)
        {
            seconds = 0.0;
            crossingLocalPositionM = localPositionM;
            crossingLocalVelocityMps = localVelocityMps;

            ConicElements elements;
            try
            {
                elements = ConicElements.FromState(localPositionM, localVelocityMps, target.Gm);
            }
            catch (Exception)
            {
                return false;
            }

            double eccentricity = elements.Eccentricity;
            if (!(eccentricity > 1e-6)) return false;

            double startAnomaly = elements.TrueAnomalyRad;
            if (!(startAnomaly < 0.0)) return false; // outbound: no inbound branch ahead

            double cosAnomaly = (elements.SemiLatusRectumM / radiusM - 1.0) / eccentricity;
            if (!(cosAnomaly > -1.0 && cosAnomaly < 1.0)) return false;

            double crossingAnomaly = -Math.Acos(cosAnomaly);
            seconds = elements.TimeSincePeriapsis(crossingAnomaly)
                      - elements.TimeSincePeriapsis(startAnomaly);
            if (!double.IsFinite(seconds)) return false;

            elements.StateAtTrueAnomaly(crossingAnomaly, out crossingLocalPositionM,
                out crossingLocalVelocityMps);
            return true;
        }


        /// <summary>An infeasible plan that also asks for a longer trip. The
        /// growing search in <see cref="Plan"/> reads the requirement, so a trip
        /// time that squeezes the arcs is retried longer instead of being reported
        /// as a geometry failure.</summary>
        private BuildResult StretchedPlan(ShipState ship, BodyRuntime target,
            double totalSeconds, string reason, bool pinStalled = false)
        {
            BuildResult result = InfeasiblePlan(ship, target, totalSeconds, reason);
            return new BuildResult(result.Plan, Math.Max(totalSeconds * 1.5,
                totalSeconds + MinimumCoastSeconds), pinStalled);
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
            Vector3d? startAcceleration = null, Vector3d? endAcceleration = null,
            bool fast = false)
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
            Measure(arc, fast);
            return arc;
        }

        private PlanArc MakeConicArc(FlightPhase phase, string bodyId, double startJd,
            double endJd, ConicArc path, Vector3d entryImpulseMps, bool fast = false)
        {
            var arc = new PlanArc(phase, bodyId, startJd, endJd, path)
            {
                EntryImpulseVectorMps = entryImpulseMps
            };
            Measure(arc, fast);
            return arc;
        }

        private PlanArc MakeFiniteBurnArc(FlightPhase phase, string bodyId,
            double startJd, double endJd, FiniteBurnArc path, bool fast = false)
        {
            var arc = new PlanArc(phase, bodyId, startJd, endJd, path);
            Measure(arc, fast);
            return arc;
        }

        private void Measure(PlanArc arc, bool fast = false)
        {
            if (fast)
            {
                MeasureFast(arc);
                return;
            }

            int samples = Math.Max(8, ArcSamples);
            double duration = arc.Path.DurationSeconds;
            double peak = 0.0;
            double deltaV = 0.0;
            double clearance = double.PositiveInfinity;
            string nearest = null;
            double previousMagnitude = 0.0;
            double previousSeconds = 0.0;

            // Sample uniformly in time, not in the path's parameter. A quintic
            // and a fixed burn are linear in time, so the two agree; a conic is
            // linear in true anomaly, so a coarse parameter grid puts most of a
            // long approach in one bin. A conic's field also changes fastest at
            // its ends — the approach to periapsis for an inbound arc, where
            // gravity goes as the inverse square of a radius that is falling
            // fast — so the two ends get a geometric refinement as well. The
            // delta-v is a time integral, and a uniform grid alone over-reports
            // the last bin by tens of km/s on a long transfer that ends in the
            // target's well.
            var times = new List<double>(samples + 2 * ConicEdgeSamples + 1);
            for (int i = 0; i <= samples; i++)
                times.Add(duration * i / samples);

            if (arc.Path is ConicArc)
            {
                for (int k = 0; k < ConicEdgeSamples; k++)
                {
                    double offset = duration * ConicEdgeFraction * Math.Pow(0.5, k + 1);
                    times.Add(offset);
                    times.Add(duration - offset);
                }

                times.Sort();
            }

            foreach (double seconds in times)
            {
                double u = arc.Path.ParameterAtSeconds(seconds);
                Vector3d position = arc.Path.PositionAt(u);
                Vector3d required = arc.Path.AccelerationAt(u);
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

                if (seconds > previousSeconds)
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

        /// <summary>
        /// The floor probe's cheap measurement. A fixed burn commands its
        /// acceleration by construction, a conic coasts, and a quintic is
        /// measured against its own reference body. Delta-v is the burn's
        /// commanded delta-v and clearance is not tracked: a probe only decides
        /// whether a duration fits, and the certified full-field build restores
        /// the numbers the plan reports.
        /// </summary>
        private void MeasureFast(PlanArc arc)
        {
            if (arc.Path is FiniteBurnArc burn)
            {
                arc.PeakThrustMps2 = burn.AccelerationMps2;
                arc.DeltaVMps = burn.AccelerationMps2 * arc.DurationSeconds;
            }
            else if (arc.Path is ConicArc)
            {
                arc.PeakThrustMps2 = 0.0;
                arc.DeltaVMps = 0.0;
            }
            else
            {
                BodyRuntime body = _model.Get(arc.ReferenceBodyId);
                arc.PeakThrustMps2 = body != null
                    ? BreakPeakCentralBodyAlongPath(arc.Path, arc.StartJulianDate, body)
                    : 0.0;
                arc.DeltaVMps = 0.0;
            }

            arc.MinimumClearanceM = double.PositiveInfinity;
            arc.NearestBodyId = null;
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
            public BuildResult(TrajectoryPlan plan, double requiredSeconds,
                bool pinStalled = false)
            {
                Plan = plan;
                RequiredSeconds = requiredSeconds;
                PinStalled = pinStalled;
            }

            public TrajectoryPlan Plan { get; }
            public double RequiredSeconds { get; }

            /// <summary>True when a certified burn–coast–burn build refused at
            /// the chain gate after its outer brake/departure fixed point ran its
            /// pass cap without converging. The build retries once with the
            /// longer cap; a probe never sets this.</summary>
            public bool PinStalled { get; }
        }

        /// <summary>A floor search's bracket: the last rejected request, the
        /// shortest feasible duration found, and the feasible plan itself.</summary>
        private readonly struct FloorSearch
        {
            public FloorSearch(double lowSeconds, double highSeconds, TrajectoryPlan plan,
                TrajectoryPlan lastPlan)
            {
                LowSeconds = lowSeconds;
                HighSeconds = highSeconds;
                Plan = plan;
                LastPlan = lastPlan;
            }

            /// <summary>The last request that was refused, or zero when the
            /// search found no refused request below the plan.</summary>
            public double LowSeconds { get; }

            /// <summary>The shortest feasible duration found.</summary>
            public double HighSeconds { get; }

            /// <summary>The feasible plan at <see cref="HighSeconds"/>, or null.</summary>
            public TrajectoryPlan Plan { get; }

            /// <summary>The last plan any probe returned, for the failure path.</summary>
            public TrajectoryPlan LastPlan { get; }
        }

        private readonly struct FloorProbe
        {
            public FloorProbe(TrajectoryPlan plan, bool feasible)
            {
                Plan = plan;
                Feasible = feasible;
            }

            public TrajectoryPlan Plan { get; }
            public bool Feasible { get; }
        }
    }
}
