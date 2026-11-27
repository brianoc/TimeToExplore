using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>How the burn–coast–burn arrival brings the ship into the
    /// parking orbit. Both choices take over where the brake hands over at the
    /// target's handover floor.</summary>
    public enum CaptureStyle
    {
        /// <summary>A Keplerian inbound hyperbola, an impulsive periapsis
        /// capture, a transfer ellipse, and a circularisation burn. The default:
        /// cheap, and free of the parking orbit's gravity ceiling.</summary>
        Conic,
        /// <summary>The same coasted hyperbola and transfer ellipse, but the
        /// long capture is a single finite burn at the player's chosen
        /// acceleration (<see cref="BurnLevelG"/>). A terminal impulse trims the
        /// burn's end state onto the exact transfer ellipse, and the
        /// circularisation is the conic style's impulse.</summary>
        FiniteBurn
    }

    /// <summary>
    /// Builds a Go To plan on the burn–coast–burn profile: a quintic break out
    /// of the departure sphere of influence, one prograde finite burn at the
    /// chosen acceleration (<see cref="BurnLevelG"/>), a heliocentric Lambert
    /// coast, a planetocentric approach, one retrograde finite brake, and the
    /// conic or hybrid capture
    /// ([Burn–coast–burn trips](../simulation/burn-coast-burn.md)).
    ///
    /// The solver is a search, not an optimal-control solve. The profile's
    /// floor is found by probing feasible durations, and the chosen duration is
    /// rebuilt and certified with the full field. At every seam the preceding
    /// arc ends on the next arc's start state, so the plan is continuous in
    /// position and velocity and its endpoint is exact.
    ///
    /// A trip that starts inside the target's sphere of influence has no
    /// crossing to plan. It is a single quintic capture onto the parking orbit
    /// and uses no cruise profile at all.
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

        /// <summary>Outer passes a **seeded** attempt may run before it is
        /// abandoned and the build is repeated cold at the normal cap. A seed
        /// from a neighbouring solved trip usually settles in one or two passes;
        /// a three-percent step at Uranus needed five to close its chain, so six
        /// leaves room for the drift and still caps a useless seed at half the
        /// default. The convergence test and the chain gate are the cold build's
        /// own — only the start and the cap differ.</summary>
        private const int SeedOuterPasses = 6;

        /// <summary>Outer passes a **seeded probe** may run. A probe is a search
        /// input and is accepted only when its pin settled, which from a
        /// neighbour's values takes one or two updates; two passes cap a seed
        /// that does not fit at a sixth of the certified build it would have
        /// cost.</summary>
        private const int ProbeSeedOuterPasses = 2;

        /// <summary>Change in the solved departure ray and burn duration that
        /// ends the pinned fixed point.</summary>
        private const double RayDirectionTolerance = 1e-10;
        private const double BurnDurationToleranceSeconds = 1e-6;

        /// <summary>The chain gate the burn-only seam must meet: a kilometre of
        /// position and a metre per second of velocity.</summary>
        private const double BurnOnlyPositionGateM = 1_000.0;
        private const double BurnOnlyVelocityGateMps = 1.0;

        /// <summary>What the burn-only Newton aims at, well inside the gate, so
        /// the built plan's own seams have margin. The finite-difference noise of
        /// the break's duration bisection (milliseconds of break, tens of metres
        /// of the departure body's motion) is the floor under this target.</summary>
        private const double BurnOnlyTargetPositionM = 250.0;
        private const double BurnOnlyTargetVelocityMps = 0.05;

        /// <summary>Damped-Newton iterations for the six-unknown burn-only seam.
        /// The measured Mercury case settles in six passes; the cap keeps a
        /// stalled basin from turning one plan into a minute of work.</summary>
        private const int BurnOnlyIterations = 32;

        /// <summary>Least-squares iterations of the fixed-direction diagnostic
        /// the failure path reports. It is never a solution, only a residual.</summary>
        private const int BurnOnlyDiagnosticIterations = 24;

        /// <summary>How closely the moving-target loop's total must agree with
        /// the date its capture chain was built at before the plan is accepted.
        /// The chain's geometry moves slowly with the date, so a few seconds is
        /// both a real convergence and far below any date the nav computer
        /// reports.</summary>
        private const double BurnOnlyDateToleranceSeconds = 2.0;

        /// <summary>Outer passes the moving-target loop may run. The measured
        /// Mercury case settles in two or three; the cap keeps a two-point ring
        /// from turning one plan into a minute of work, and a loop that hits it
        /// is reported as infeasible rather than returned.</summary>
        /// <summary>The moving-target date loop's pass cap. Twelve leaves room for
        /// the split's shorter coast floors: the loop starts several passes below
        /// the burn-only crossing's own total, and the first passes grow the date
        /// before the fixed point walks in.</summary>
        private const int BurnOnlyDateIterations = 12;

        /// <summary>Under-relaxation for a moving-target pass that overshoots
        /// and reverses the previous step. Halving the update turns a two-point
        /// ring into a geometric approach to the fixed point.</summary>
        private const double BurnOnlyDateDamping = 0.5;

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
        /// time/thrust trade without turning the plan into a drift. The window is
        /// anchored to the **coast** floor, not to a faster full-blast crossing,
        /// so the slow end does not move when the left end does.</summary>
        public const double MaxDurationScale = 3.0;

        /// <summary>The escape excess the full-blast grid is built around at a
        /// 0.3 g level. The time-optimal climb trades the escape conic (shorter
        /// with more excess) against the two fixed burns (longer, because the
        /// crossing starts faster and must brake harder). The measured optimum
        /// scales with the square root of the burn level — about 10 km/s at
        /// 0.05 g, 28 at 0.3 g and 48 at 1 g — so the grid multiplies this value
        /// by √level and <see cref="FullBlastVInfinityFactors"/>.</summary>
        private const double FullBlastVInfinityReferenceMps = 48_000.0;

        /// <summary>Multipliers around the full-blast reference excess. A step is
        /// about 1.3× the last, which puts the best candidate within minutes of a
        /// finer scan across the measured routes while keeping the grid small:
        /// five fixed-date builds per plot.</summary>
        private static readonly double[] FullBlastVInfinityFactors =
            { 0.6, 0.8, 1.0, 1.25, 1.6 };

        /// <summary>How far astern of a station, along the station's own velocity
        /// direction, a station trip holds: the point target's terminal state is
        /// the station's state at the arrival date with this offset, and the
        /// ship's velocity matched to the station's. A few hundred metres is a
        /// station-keeping box, not an orbit, and a station has no sphere, no
        /// parking orbit and no capture chain to hand over to.</summary>
        public const double StationHoldDistanceM = 500.0;

        /// <summary>Integrator step samples for a station's terminal velocity
        /// match. The match is the one short, high-acceleration burn in a
        /// multi-week plan and the one arc whose end is the arrival, so its own
        /// truncation is the whole tracked hold error: the default 250-step hint
        /// leaves about 4 × 10⁻⁴ m/s even with the arrival step keeping its final
        /// stage (<see cref="FlightSession.ThrustForArc"/>). Four times finer
        /// costs a few percent more substeps on the plan and keeps the tracked
        /// hold at centimetres and 10⁻⁴ m/s. Only this one arc pays; a planet
        /// brake keeps the default hint.</summary>
        public const double StationMatchStepSamples = 1_000.0;

        /// <summary>Seed duration of a station trip's final velocity-match burn.
        /// The brake's real length follows the arrival relative speed, so this is
        /// only the search's first guess along the distance, climb, and brake
        /// seed.</summary>
        private const double StationBrakeSeedSeconds = 3_600.0;

        /// <summary>The floor search's bisection tolerance for a station, relative
        /// to the feasible duration. The station floor is a new number, so it is
        /// solved to a tenth of a percent rather than the planetary floor's
        /// twentieth; every returned plan is a certified build either way.</summary>
        private const double StationFloorRelativeTolerance = 1e-3;

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

        /// <summary>Inbound speed the capture chain enters with: the hyperbolic
        /// excess speed of the arrival hyperbola.</summary>
        public double EntrySpeedMps { get; set; } = 5_000.0;

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

        /// <summary>Which capture the plan ends on. The conic capture is the
        /// default; the hybrid finite-burn capture is the alternative.</summary>
        public CaptureStyle ArrivalStyle { get; set; } = CaptureStyle.Conic;

        /// <summary>
        /// Test-only seam: when non-negative, force the departure escape conic's
        /// hyperbolic excess speed to this value instead of solving the one that
        /// fits the time left after the break. It is not a plan input and no
        /// product code sets it. The default `-1` leaves every plan exactly as it
        /// was. Used to measure the split between the climb and the coast; remove
        /// it when the split search lands.
        /// </summary>
        internal double DepartureVInfinityOverrideMps { get; set; } = -1.0;

        /// <summary>
        /// Search the departure climb split: choose the escape conic's hyperbolic
        /// excess speed that minimises the plan's total delta-v at the requested
        /// duration, instead of taking the minimum-energy parabolic climb whenever
        /// it fits. **On by default** since step 3: a little excess at periapsis
        /// buys a longer coast, and the longer coast shrinks both fixed burns by
        /// more than the break grows. Internal so the tests can turn it off and
        /// pin the pre-split anchors. The choice is made **once per build** and
        /// the same value reaches the reference skeleton, the pin loop, the
        /// settle pass and the direct branch entry points; re-choosing it inside
        /// the pin loop would move the fixed point. See
        /// <see cref="ChooseDepartureVInfinityMps(ShipState, BodyRuntime, double)"/>.
        /// </summary>
        internal bool UseDepartureSplit { get; set; } = true;

        /// <summary>How long the last <see cref="UseDepartureSplit"/> search took,
        /// in seconds. Diagnostics only: it is written when the search runs and
        /// never read by the planner. Step 3 uses it to budget the plot.</summary>
        internal double LastDepartureSplitSeconds { get; private set; }

        /// <summary>Design acceleration of a fixed-thrust burn. 0.3 g is the
        /// working level: low enough to be a real burn rather than an impulse,
        /// high enough that the capture still fits inside a low orbit. The
        /// player-facing level is <see cref="BurnLevelG"/>; this is the default
        /// before the drive's own limit is applied.</summary>
        public const double DefaultBurnLevelG = 0.3;

        /// <summary>Standard gravity, the scale of the player's burn-level
        /// control. 1 g is 9.80665 m/s².</summary>
        public const double StandardGravityMps2 = 9.80665;

        /// <summary>Lowest burn level the player can choose. The profile is two
        /// real burns around a coast, so there is no zero-thrust version: below
        /// this it would be the retired plain-coast plan.</summary>
        public const double MinimumBurnLevelG = 0.05;

        /// <summary>Highest burn level the player can choose.</summary>
        public const double MaxBurnLevelG = 1.0;

        private double _burnLevelG = DefaultBurnLevelG;

        /// <summary>Player-facing burn level in g, clamped to
        /// <see cref="MinimumBurnLevelG"/> and <see cref="MaxBurnLevelG"/>.</summary>
        public double BurnLevelG
        {
            get => _burnLevelG;
            set => _burnLevelG = Math.Clamp(value, MinimumBurnLevelG, MaxBurnLevelG);
        }

        /// <summary>Acceleration the departure burn, the brake, and the hybrid
        /// capture command: the chosen level, clamped to the drive's own thrust
        /// margin.</summary>
        public double FixedBurnAccelerationMps2 =>
            Math.Min(BurnLevelG * StandardGravityMps2, MaxAccelerationMps2 * ThrustMargin);

        /// <summary>Optional diagnostic sink for the planning loop.</summary>
        public Action<string> Trace { get; set; }

        /// <summary>Optional counters and phase timings for one call. The
        /// planner only writes to it, so attaching one never changes a plan,
        /// a tolerance, a gate, or a pass cap. Reset it before the call to
        /// measure and read it afterwards.</summary>
        public PlanMetrics Metrics { get; set; }

        /// <summary>
        /// The profile's fastest **coast** crossing: the burn–coast–burn floor.
        /// This is the default plan the nav computer's fixed-duration anchors and
        /// the flight session's Begin path use. The zero-coast crossing is faster
        /// at the same level but not part of this search; its own minimum is
        /// <see cref="PlanFastest"/>.
        /// </summary>
        public TrajectoryPlan Plan(ShipState ship, string targetId) =>
            PlanCoastFloor(ship, RequireTarget(targetId));

        /// <summary>The <see cref="Plan"/> body, with the target already
        /// resolved so <see cref="PlanFastest"/> can reuse the resolved body for
        /// its two branches.</summary>
        private TrajectoryPlan PlanCoastFloor(ShipState ship, BodyRuntime target)
        {
            // A station is a massless point target: there is no sphere of
            // influence to enter, no parking orbit, and no handover band, so it
            // takes its own branch rather than faking a gravitational parameter.
            if (target.Data.IsStation) return PlanStationFloor(ship, target);

            _model.ComputeState(target, ship.JulianDate, out Vector3d targetPosition, out _);
            if (Vector3d.Distance(targetPosition, ship.PositionM)
                < SphereOfInfluence.RadiusM(target))
            {
                return PlanInsideTarget(ship, target);
            }

            double parkingRadius = ParkingOrbit.ConicRadiusM(target);
            double distance = Math.Max(Vector3d.Distance(targetPosition, ship.PositionM),
                parkingRadius);

            // The estimate is only the search's seed. The profile enters at the
            // handover floor instead of the sphere of influence, so its capture's
            // fall is minutes rather than months and the seed should say so.
            double total = 1.1 * 2.0 * Math.Sqrt(distance / MaxAccelerationMps2);
            double entryRadius = SphereOfInfluence.RadiusM(target);
            HandoverBand band = HandoverBand.For(target, FixedBurnAccelerationMps2);
            if (band.IsValid) entryRadius = ArrivalEntryRadiusM(band, target);
            total += EstimateConicCaptureSeconds(target, entryRadius);

            return PlanBurnCoastBurnMinimum(ship, target, total);
        }

        /// <summary>
        /// The station trip's coast floor. A station is a point target, so the
        /// seed is the kinematic crossing to the hold point, the departure
        /// climb's own seed, and a short velocity-match brake; there is no
        /// parking orbit, handover band or capture chain to estimate. The fast
        /// probe grows a bracket until its seam closes, bisects it to
        /// <see cref="StationFloorRelativeTolerance"/>, and the feasible end is
        /// rebuilt as a certified full-field plan. A refused certificate steps
        /// the bracket up rather than loosening it. The probes go through the
        /// ordinary <see cref="Build"/>, so each carries the same split-aware
        /// departure choice its certified rebuild will.
        /// </summary>
        private TrajectoryPlan PlanStationFloor(ShipState ship, BodyRuntime station)
        {
            _model.ComputeState(station, ship.JulianDate,
                out Vector3d stationPosition, out _);
            double distance = Math.Max(
                Vector3d.Distance(stationPosition, ship.PositionM), 1.0);

            // The escape conic's days are not in this seed; the bracket's growth
            // finds them. The break and the brake are the seed's own terms.
            double seed = 1.1 * 2.0 * Math.Sqrt(distance / MaxAccelerationMps2)
                          + ClimbSeedSeconds(MinimumCoastSeconds)
                          + StationBrakeSeedSeconds;

            double low = 0.0;
            double high = 0.0;
            TrajectoryPlan lastPlan = null;
            double probe = Math.Max(seed, MinimumArcSeconds);

            for (int step = 0; step < 24; step++)
            {
                BuildResult result = Build(ship, station, probe, fast: true);
                lastPlan = result.Plan;
                Trace?.Invoke($"station floor {probe / SecondsPerDay:F5} d: " +
                              $"feasible {IsFeasibleAt(result.Plan, probe)}, " +
                              $"time {result.Plan.FlightTimeSeconds / SecondsPerDay:F5} d, " +
                              $"reason {result.Plan.InfeasibleReason}");
                if (IsFeasibleAt(result.Plan, probe))
                {
                    high = result.Plan.FlightTimeSeconds;
                    break;
                }

                low = probe;
                double next = Math.Min(
                    Math.Max(result.RequiredSeconds, probe * 1.5), probe * 4.0);
                if (!(next > low)) break;
                probe = next;
            }

            if (!(high > 0.0)) return Reported(lastPlan);

            double tolerance = Math.Max(FloorToleranceSeconds,
                high * StationFloorRelativeTolerance);
            for (int iteration = 0; iteration < 12 && high - low > tolerance; iteration++)
            {
                double middle = 0.5 * (low + high);
                BuildResult result = Build(ship, station, middle, fast: true);
                lastPlan = result.Plan;
                if (IsFeasibleAt(result.Plan, middle)
                    && result.Plan.FlightTimeSeconds < high)
                {
                    high = result.Plan.FlightTimeSeconds;
                }
                else
                {
                    low = middle;
                }
            }

            for (int certificate = 0; certificate < MaxFloorCertificates; certificate++)
            {
                // The certification is the ordinary build, so it gets the same
                // near-floor pin retry the planet certificate does.
                BuildResult certified = Build(ship, station, high);
                lastPlan = certified.Plan;
                Trace?.Invoke($"station certify {high / SecondsPerDay:F5} d: " +
                              $"feasible {IsFeasibleAt(certified.Plan, high)}, " +
                              $"reason {certified.Plan.InfeasibleReason}");
                if (IsFeasibleAt(certified.Plan, high))
                    return Reported(certified.Plan);

                if (certificate == MaxFloorCertificates - 1) break;
                high *= 1.02;
            }

            return Reported(lastPlan);
        }

        /// <summary>A trip that starts inside the target's sphere of influence
        /// has no crossing: it is one quintic capture onto the parking orbit.
        /// The generic growing loop finds a duration whose peak thrust fits the
        /// drive.</summary>
        private TrajectoryPlan PlanInsideTarget(ShipState ship, BodyRuntime target)
        {
            _model.ComputeState(target, ship.JulianDate, out Vector3d targetPosition, out _);
            double parkingRadius = ParkingOrbit.RadiusM(target, MaxAccelerationMps2);
            double distance = Math.Max(Vector3d.Distance(targetPosition, ship.PositionM),
                parkingRadius);
            double total = 1.1 * 2.0 * Math.Sqrt(distance / MaxAccelerationMps2);

            TrajectoryPlan plan = null;
            double required = total;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                BuildResult result = BuildInsideTarget(ship, target, total);
                plan = result.Plan;
                required = result.RequiredSeconds;

                if (!string.IsNullOrEmpty(plan.InfeasibleReason))
                {
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

            if (!plan.IsFeasible || required > total)
                plan = BuildInsideTarget(ship, target, Math.Max(required, total)).Plan;

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

                // A failed certificate is usually the fast/full gap at the fast
                // end: the full-field break or brake is a fraction of a percent
                // longer, so the climb misses the time available by seconds.
                // Retry the certified build a little above before paying for a
                // full-field probe bisection — a certificate is an answer, not a
                // probe. The 0.2% and 0.5% steps are the same near-floor retries
                // the probe searches use, and the continuation below remains the
                // fallback for a gap that is wider than that.
                double retry = candidate;
                for (int step = 0; step < 2; step++)
                {
                    retry = step == 0 ? candidate * 1.002 : candidate * 1.005;
                    if (tried.ContainsKey(retry)) continue;
                    BuildResult retried = Build(ship, target, retry);
                    tried[retry] = retried;
                    lastRefusal = retried.Plan;
                    bool retryFeasible = IsFeasibleAt(retried.Plan, retry);
                    Trace?.Invoke($"certify retry {retry / SecondsPerDay:F5} d: " +
                                  $"feasible {retryFeasible}, " +
                                  $"time {retried.Plan.FlightTimeSeconds / SecondsPerDay:F5} d, " +
                                  $"reason {retried.Plan.InfeasibleReason}");
                    if (retryFeasible) return Reported(retried.Plan);
                }

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

            // Carry the pin a probe built to the next probe. Two probes a step
            // apart converge to almost the same brake and departure burn, and
            // the floor search is where a first plot spends its pin passes. Only
            // a feasible probe seeds the next one, and only a trip within
            // <see cref="PinSeed.MaxRelativeGap"/> of it: a refusal or a
            // stretched plan carries no seed, and a seed from a trip of another
            // length is not a guess for this one.
            PinSeed carried = default;

            FloorProbe Probe(double seconds)
            {
                bool reused = false;
                if (tried == null || !tried.TryGetValue(seconds, out BuildResult result))
                {
                    // Only the **full-field continuation** is seeded. The cheap
                    // probe's feasibility is the floor's frontier, and a seed that
                    // lets a stalled probe close moves that frontier: measured at
                    // Uranus, seeding the fast search walked its high from
                    // 92.13 d to 91.08 d, where the certificate refuses, and the
                    // floor search then found nothing. A continuation probe runs
                    // above a refused certificate, so its pin may start warm.
                    PinSeed start = probe ? carried.Nearby(seconds) : default;
                    Trace?.Invoke($"floor request " +
                        $"{(fast ? "fast" : probe ? "probe" : "full")} " +
                        $"{seconds / SecondsPerDay:F5} d" +
                        (start.IsValid ? " [seeded]" : ""));
                    result = Build(ship, target, seconds, fast, probe, start);
                    if (tried != null) tried[seconds] = result;
                }
                else
                {
                    reused = true;
                }

                lastPlan = result.Plan;
                lastRequest = seconds;
                bool feasible = IsFeasibleAt(result.Plan, seconds);
                if (feasible && result.Seed.IsValid)
                    carried = result.Seed;
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

        /// <summary><see cref="Reported(TrajectoryPlan)"/> for a builder that
        /// returns a <see cref="BuildResult"/> rather than running the growth
        /// loop: the burn-only crossing's duration is an output.</summary>
        private static TrajectoryPlan Reported(BuildResult result) =>
            Reported(result.Plan);

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
        ///
        /// A duration between the full-blast minimum and the coast floor is the
        /// **short coast**: when the coast path refuses it for being squeezed,
        /// the plan is the burn-only seam with a heliocentric conic coast of the
        /// slack time. The total stays an input. At the minimum the plan is the
        /// burn-only crossing itself. A Lambert geometry refusal is tried too,
        /// but only a feasible short coast replaces it; otherwise the coast's own
        /// reason stands. See <see cref="BuildShortCoast"/>.
        /// </summary>
        public TrajectoryPlan PlanAtDuration(ShipState ship, string targetId,
            double durationSeconds, PinSeed seed = default)
        {
            BodyRuntime target = RequireDurationTarget(targetId, durationSeconds);

            BuildResult result = Build(ship, target, durationSeconds, seed: seed);

            // The coast path cannot fit a duration between the full-blast
            // minimum and its own floor: the break or the departure burn is
            // squeezed. That is the gap the short coast fills. The short coast is
            // also tried on a geometry refusal, but only a **feasible** short
            // coast replaces that answer; otherwise the coast's own reason (no
            // prograde short-way transfer) is kept rather than masked.
            string reason = result.Plan.InfeasibleReason ?? string.Empty;
            if (!result.Plan.IsFeasible
                && (result.ShortCoastWorthy
                    || result.Plan.PeakThrustMps2
                    > MaxAccelerationMps2 * ThrustMargin
                    || reason.Contains("short-way")
                    || reason.Contains("Lambert")))
            {
                BuildResult? shortCoast = BuildShortCoast(ship, target, durationSeconds);
                if (shortCoast.HasValue)
                {
                    BuildResult built = shortCoast.Value;
                    Trace?.Invoke(built.Plan.IsFeasible
                        ? $"short coast at {durationSeconds / SecondsPerDay:F5} d: built"
                        : $"short coast at {durationSeconds / SecondsPerDay:F5} d: " +
                          built.Plan.InfeasibleReason);
                    if (built.Plan.IsFeasible) result = built;
                }
            }

            return Reported(result.Plan);
        }

        /// <summary>
        /// The **coast profile's** answer at an explicit flight time, without the
        /// short-coast rescue. The rescue's range is the gap between the
        /// full-blast minimum and the coast floor; above the floor the coast's own
        /// answer is the profile's, and running the rescue as well costs a
        /// burn-only moving-target solve on every refused probe. The trip-time
        /// window's upper-end search runs here, because every probe starts at the
        /// coast floor and moves slower. A caller that needs the short-coast gap,
        /// or a duration the player asked for, uses <see cref="PlanAtDuration"/>.
        /// </summary>
        internal TrajectoryPlan PlanCoastAtDuration(ShipState ship, string targetId,
            double durationSeconds, PinSeed seed = default)
        {
            BodyRuntime target = RequireDurationTarget(targetId, durationSeconds);
            return Reported(Build(ship, target, durationSeconds, seed: seed).Plan);
        }

        private BodyRuntime RequireDurationTarget(string targetId,
            double durationSeconds)
        {
            BodyRuntime target = RequireTarget(targetId);
            if (!(durationSeconds > 0.0) || !double.IsFinite(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds),
                    "A pinned flight time must be finite and positive.");
            return target;
        }

        /// <summary>
        /// **Fixed date**: a **zero-coast** crossing built from
        /// the profile's own finite-burn arcs. The departure is the physical
        /// climb — the short break and the escape conic out to the handover
        /// ceiling — the capture chain is built at the arrival handover floor for
        /// <paramref name="arrivalJulianDate"/>, and the two burns meet at the
        /// flip with **no coast** between them:
        ///
        /// * the departure burn starts at the escape conic's ceiling state and
        ///   integrates forward through the summed field;
        /// * the brake ends on the capture chain's entry and integrates
        ///   **backward** from it, so its start state is the flip.
        ///
        /// The unknowns are the departure burn's thrust direction (two angles) and
        /// duration, the brake's direction (two angles), and the brake's
        /// duration: six unknowns for the six seam components (the forward
        /// burn's end position and velocity against the backward brake's start).
        /// The total crossing time is the **sum** of the solved burn and brake
        /// durations plus the break and capture, so it is an **output**: a
        /// burn-only crossing is one duration per geometry and level, not a
        /// slider value.
        ///
        /// The supplied date fixes the capture chain's geometry — its entry
        /// radius, plane, and fall — and its handover is placed where the solved
        /// brake actually ends, so the plan is continuous in date as well as
        /// state. This overload builds the chain **once** and is the step-2
        /// measurement; the date is solved by
        /// <see cref="PlanBurnOnly(ShipState, string)"/>, which owns the
        /// moving-target loop.
        ///
        /// <see cref="PlanFastest"/> is the nav computer's route through this
        /// crossing. A plan is returned only when the seam closes to the chain
        /// gate (1 km and 1 m/s); otherwise the returned plan names the residual
        /// the solve reached, so a failure is reported rather than hidden.
        /// </summary>
        public TrajectoryPlan PlanBurnOnly(ShipState ship, string targetId,
            double arrivalJulianDate)
        {
            BodyRuntime target = RequireTarget(targetId);
            if (!double.IsFinite(arrivalJulianDate) || !(arrivalJulianDate > ship.JulianDate))
            {
                throw new ArgumentOutOfRangeException(nameof(arrivalJulianDate),
                    "The burn-only crossing needs an arrival date after departure.");
            }

            return BuildBurnOnly(ship, target, arrivalJulianDate,
                forcedVInfinityMps: 0.0);
        }

        /// <summary>
        /// The nav computer's **minimum trip time**: the shorter feasible plan of
        /// the **full-blast** zero-coast crossing and the profile's coast floor.
        /// The full-blast crossing takes the time-optimal escape excess on a
        /// small grid (<see cref="FullBlastVInfinityFactors"/>) instead of the
        /// minimum-energy parabolic climb, so its total is an output of the seam
        /// solve; each candidate is one fixed-date build at the coast floor's
        /// arrival date, and the shortest one is re-solved through the moving
        /// target. Measured at the epoch, Gateway → Mars 0.3 g is **6.66965 d at
        /// a 26.3 km/s excess** against the coast floor's 7.16751 d, and the gain
        /// is 6–14% across the measured routes. When no candidate beats the
        /// floor, is infeasible, or does not settle, the coast floor is returned.
        ///
        /// The **window maximum stays three times the coast floor**, so
        /// <paramref name="coastFloor"/> is returned for the caller's anchor;
        /// only the slider's left end moves.
        ///
        /// This does **not** change <see cref="Plan"/>'s floors or the
        /// fixed-duration delta-v anchors, which still come from the coast path.
        /// A duration between the full-blast minimum and the coast floor is the
        /// **short coast**, built by <see cref="PlanAtDuration"/> across the whole
        /// range; the branch retries the full-blast excesses when the split's own
        /// climb does not fit.
        /// </summary>
        public TrajectoryPlan PlanFastest(ShipState ship, string targetId) =>
            PlanFastest(ship, RequireTarget(targetId), out _);

        /// <summary><see cref="PlanFastest(ShipState, string)"/> with the coast
        /// floor that anchors the window's slow end. The plan is the same; the
        /// floor is the build the window would otherwise have to repeat. Public
        /// because the player benchmark runs the same pair.</summary>
        public TrajectoryPlan PlanFastest(ShipState ship, string targetId,
            out TrajectoryPlan coastFloor) =>
            PlanFastest(ship, RequireTarget(targetId), out coastFloor);

        private TrajectoryPlan PlanFastest(ShipState ship, BodyRuntime target,
            out TrajectoryPlan coastFloor)
        {
            PlanMetrics metrics = Metrics;
            long coastStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            TrajectoryPlan coast = PlanCoastFloor(ship, target);
            coastFloor = coast;
            if (metrics != null)
                metrics.AddFastestCoast(PlanMetrics.Timestamp() - coastStart,
                    coast?.FlightTimeSeconds ?? double.NaN);
            if (target.Data.IsStation)
            {
                // The zero-coast crossing needs a capture chain; a station has
                // none. Its coast floor is the fastest plan the profile builds.
                Trace?.Invoke($"fastest: station target, coast " +
                              $"{coast.FlightTimeSeconds / SecondsPerDay:F5} d " +
                              $"(feasible {coast.IsFeasible})");
                return coast;
            }

            if (!coast.IsFeasible || !(coast.FlightTimeSeconds > 0.0))
            {
                // The full-blast crossing needs a capture chain and a date to
                // build it at. A refused coast floor is reported as it is; the
                // grid does not rescue a route the profile cannot plot.
                Trace?.Invoke($"fastest: coast floor infeasible " +
                              $"({coast.InfeasibleReason}); no full-blast search");
                return coast;
            }

            long burnOnlyStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            TrajectoryPlan fastest = PlanFullBlast(ship, target, coast,
                out double chosenVInfinityMps);
            if (metrics != null)
                metrics.AddFastestBurnOnly(PlanMetrics.Timestamp() - burnOnlyStart,
                    fastest?.FlightTimeSeconds ?? double.NaN);

            Trace?.Invoke($"fastest: full blast " +
                          $"{fastest.FlightTimeSeconds / SecondsPerDay:F5} d " +
                          $"(feasible {fastest.IsFeasible}, v∞ " +
                          $"{chosenVInfinityMps / 1000.0:F2} km/s), coast " +
                          $"{coast.FlightTimeSeconds / SecondsPerDay:F5} d " +
                          $"(feasible {coast.IsFeasible})");
            return fastest;
        }

        /// <summary>
        /// The full-blast search: one fixed-date crossing per grid excess, all
        /// built at the coast floor's arrival date so their totals compare, then
        /// the shortest candidate re-solved through the moving target so its
        /// capture chain belongs to the date it reaches. Returns the coast floor
        /// when no candidate is shorter, when the winner does not settle, or
        /// when every candidate is infeasible.
        /// </summary>
        private TrajectoryPlan PlanFullBlast(ShipState ship, BodyRuntime target,
            TrajectoryPlan coast, out double chosenVInfinityMps)
        {
            chosenVInfinityMps = double.NaN;
            double arrivalJulianDate = ship.JulianDate
                                       + coast.FlightTimeSeconds / SecondsPerDay;
            double reference = FullBlastVInfinityReferenceMps * Math.Sqrt(BurnLevelG);

            TrajectoryPlan best = null;
            double bestVInfinityMps = double.NaN;
            foreach (double factor in FullBlastVInfinityFactors)
            {
                double vInfinityMps = reference * factor;
                TrajectoryPlan candidate = BuildBurnOnly(ship, target,
                    arrivalJulianDate, vInfinityMps, probe: true);
                Trace?.Invoke($"full blast: v∞ {vInfinityMps / 1000.0:F3} km/s " +
                              $"-> {(string.IsNullOrEmpty(candidate.InfeasibleReason)
                                  ? $"{candidate.FlightTimeSeconds / SecondsPerDay:F5} d"
                                  : "infeasible: " + candidate.InfeasibleReason)}");
                // A probe's coarse arcs can read a peak above the drive; the
                // ranking only needs a closed crossing and its total. The winner
                // is rebuilt certified, where the peak is a verdict again.
                if (!string.IsNullOrEmpty(candidate.InfeasibleReason)
                    || !(candidate.FlightTimeSeconds > 0.0))
                    continue;
                if (best == null || candidate.FlightTimeSeconds < best.FlightTimeSeconds)
                {
                    best = candidate;
                    bestVInfinityMps = vInfinityMps;
                }
            }

            if (best == null || best.FlightTimeSeconds >= coast.FlightTimeSeconds)
            {
                Trace?.Invoke("full blast: no candidate beats the coast floor");
                return coast;
            }

            TrajectoryPlan settled = PlanBurnOnlyMovingTarget(ship, target,
                best.FlightTimeSeconds, bestVInfinityMps);
            if (!settled.IsFeasible || !(settled.FlightTimeSeconds > 0.0)
                || settled.FlightTimeSeconds >= coast.FlightTimeSeconds)
            {
                // The fixed-date winner is a valid crossing but its chain was
                // built at the coast floor's date. A moving-target solve that
                // does not settle is reported, and the coast floor is flown:
                // the nav minimum is never a plan the loop refused.
                Trace?.Invoke("full blast: the winning crossing did not settle; " +
                              "returning the coast floor");
                return coast;
            }

            chosenVInfinityMps = bestVInfinityMps;
            return settled;
        }

        /// <summary>
        /// The same crossing with the **arrival date solved**, as the nav
        /// computer needs it. The capture chain is built at
        /// <c>t0 + total</c>, the seam is solved there, and the plan's own total
        /// (break + departure burn + brake + capture) becomes the next date; the
        /// loop repeats until the two agree within
        /// <see cref="BurnOnlyDateToleranceSeconds"/>. The seed is the coast
        /// floor's own total, so the fixed-date measurement is the first pass and
        /// the chain starts in the basin the seam was measured in.
        ///
        /// A converged plan is continuous in date as well as state: its arrival
        /// date is the date its chain was built at, within the tolerance. A loop
        /// that does not settle is reported as infeasible with the last step it
        /// took; a moving-target plan that never converged is never returned as a
        /// solution. This is the nav computer's fastest crossing — see
        /// <see cref="PlanFastest"/>.
        /// </summary>
        public TrajectoryPlan PlanBurnOnly(ShipState ship, string targetId)
        {
            BodyRuntime target = RequireTarget(targetId);
            double seedSeconds = PlanCoastFloor(ship, target).FlightTimeSeconds;
            return PlanBurnOnlyMovingTarget(ship, target, seedSeconds,
                forcedVInfinityMps: 0.0);
        }

        /// <summary>
        /// The moving-target loop behind
        /// <see cref="PlanBurnOnly(ShipState, string)"/> and
        /// <see cref="PlanFastest"/>. Each pass rebuilds the capture chain at the
        /// total the previous pass solved, so the chain's geometry belongs to the
        /// date the plan actually reaches. The update is the plan's own total;
        /// when a pass overshoots and reverses the last step, the next update is
        /// damped by half so a two-point oscillation decays instead of ringing.
        /// </summary>
        private TrajectoryPlan PlanBurnOnlyMovingTarget(ShipState ship,
            BodyRuntime target, double seedSeconds, double forcedVInfinityMps)
            => PlanBurnOnlyMovingTarget(ship, target, seedSeconds,
                out _, forcedVInfinityMps);

        /// <summary>The moving-target loop plus the settled seam solution, for the
        /// short-coast solve's seed. A loop that does not settle returns the
        /// infeasible plan with a default solution.</summary>
        private TrajectoryPlan PlanBurnOnlyMovingTarget(ShipState ship,
            BodyRuntime target, double seedSeconds, out BurnOnlyEvaluation settled,
            double forcedVInfinityMps = -1.0)
        {
            settled = default;
            double total = Math.Max(seedSeconds, MinimumArcSeconds);
            double lastStep = double.PositiveInfinity;
            TrajectoryPlan plan = null;

            // Grow the seed until a fixed-date crossing fits. Since the split the
            // coast floor can be far below the burn-only crossing's own minimum
            // (Mars 0.3 g: 7.17 d against 16.49 d), so the seed may be a date the
            // crossing cannot fly. Growth is separate from the fixed point: the
            // twelve date passes below are the convergence budget, not the search.
            for (int growth = 0; growth < 6; growth++)
            {
                double chainDate = ship.JulianDate + total / SecondsPerDay;
                plan = BuildBurnOnly(ship, target, chainDate, forcedVInfinityMps);
                if (plan.IsFeasible) break;
                Trace?.Invoke($"burn-only seed growth {growth}: the build at " +
                              $"{total / SecondsPerDay:F5} d is infeasible: " +
                              plan.InfeasibleReason);
                if (growth == 5) return plan;
                total *= 1.5;
            }

            for (int pass = 0; pass < BurnOnlyDateIterations; pass++)
            {
                double chainJulianDate = ship.JulianDate + total / SecondsPerDay;
                plan = BuildBurnOnly(ship, target, chainJulianDate,
                    out BurnOnlyEvaluation passSolution, forcedVInfinityMps);
                if (!plan.IsFeasible)
                {
                    Trace?.Invoke($"burn-only moving target pass {pass}: the build at " +
                                  $"{total / SecondsPerDay:F5} d is infeasible: " +
                                  plan.InfeasibleReason);
                    return plan;
                }

                double actual = plan.FlightTimeSeconds;
                double step = actual - total;
                Trace?.Invoke($"burn-only moving target pass {pass}: chain at " +
                              $"{total / SecondsPerDay:F5} d, plan arrives " +
                              $"{actual / SecondsPerDay:F5} d, step {step:F1} s");

                if (Math.Abs(step) <= BurnOnlyDateToleranceSeconds)
                {
                    settled = passSolution;
                    return plan;
                }

                double next = actual;
                if (double.IsFinite(lastStep)
                    && Math.Sign(step) != Math.Sign(lastStep)
                    && Math.Abs(step) > Math.Abs(lastStep))
                {
                    // The two dates bracket the fixed point and the plain update
                    // jumps across it: halve the step to settle instead of ring.
                    next = total + BurnOnlyDateDamping * step;
                    Trace?.Invoke("burn-only moving target: damping an oscillating step");
                }

                lastStep = step;
                total = Math.Max(next, MinimumArcSeconds);
            }

            Trace?.Invoke($"burn-only moving target: did not settle after " +
                          $"{BurnOnlyDateIterations} passes; last step " +
                          $"{lastStep:F1} s at {total / SecondsPerDay:F5} d");
            return Reported(InfeasiblePlan(ship, target, total,
                $"burn-only: the moving target did not settle after " +
                $"{BurnOnlyDateIterations} passes (last step {lastStep:F1} s at " +
                $"{total / SecondsPerDay:F3} d)"));
        }

        /// <summary>The fixed-date crossing's body: the caller supplies the
        /// capture chain's date, and the handover lands where the solved brake
        /// ends.</summary>
        private TrajectoryPlan BuildBurnOnly(ShipState ship, BodyRuntime target,
            double arrivalJulianDate, double forcedVInfinityMps, bool probe = false)
            => BuildBurnOnly(ship, target, arrivalJulianDate, out _,
                forcedVInfinityMps, probe);

        /// <summary>The fixed-date crossing plus the seam solution it settled on.
        /// The short-coast solve seeds from these ray, burn, and brake directions;
        /// nothing else about the crossing is reused. A **probe** build ranks a
        /// candidate at the two-body field and a looser gate; only the winner is
        /// rebuilt certified.</summary>
        private TrajectoryPlan BuildBurnOnly(ShipState ship, BodyRuntime target,
            double arrivalJulianDate, out BurnOnlyEvaluation solution,
            double forcedVInfinityMps = -1.0, bool probe = false)
        {
            solution = default;
            PlanMetrics metrics = Metrics;
            metrics?.NoteBurnOnlyBuild();
            double totalSeconds = (arrivalJulianDate - ship.JulianDate) * SecondsPerDay;
            double acceleration = FixedBurnAccelerationMps2;
            if (!(acceleration > 0.0))
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: the drive commands no fixed-burn acceleration"));
            }

            if (target.Data.IsStation)
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: a station is a point target with no capture chain, so " +
                    "the zero-coast crossing is not built; the station plan's coast " +
                    "floor is the fastest trip"));
            }

            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            bool hasClimb = departureBody != null;

            _model.ComputeState(target, ship.JulianDate,
                out Vector3d targetAtDeparture, out _);
            if (Vector3d.Distance(ship.PositionM, targetAtDeparture)
                < SphereOfInfluence.RadiusM(target))
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: the ship starts inside the target's sphere of influence, " +
                    "so there is no crossing to plan"));
            }

            HandoverBand departureBand = null;
            if (hasClimb)
            {
                departureBand = HandoverBand.For(departureBody, acceleration);
                if (!departureBand.IsValid)
                {
                    return Reported(InfeasiblePlan(ship, target, totalSeconds,
                        "burn-only: " + departureBand.InvalidReason));
                }
            }

            HandoverBand arrivalBand = HandoverBand.For(target, acceleration);
            if (!arrivalBand.IsValid)
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: " + arrivalBand.InvalidReason));
            }

            double arrivalRadius = ArrivalEntryRadiusM(arrivalBand, target);

            // The capture chain is the fixed part of the request: the supplied
            // date chooses its plane and the handover floor chooses its entry.
            TransferConic chain = BuildCaptureShape(ship, target, arrivalRadius,
                arrivalJulianDate);
            if (!chain.IsValid)
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: conic capture: " + chain.InvalidReason));
            }

            // The reference coast seeds the two directions and the break, the
            // same way BuildBurnCoastBurn seeds its own solve. It is a search
            // input only: this crossing has no coast of its own.
            BuildResult reference = BuildCoastSkeleton(ship, target, totalSeconds,
                arrivalRadius, fast: probe, probe: false,
                forcedVInfinityMps: forcedVInfinityMps);
            PlanArc referenceCruise = null;
            if (reference.Plan != null)
            {
                foreach (PlanArc arc in reference.Plan.Arcs)
                {
                    if (arc.Phase == FlightPhase.Cruise && arc.IsCoast)
                    {
                        referenceCruise = arc;
                        break;
                    }
                }
            }

            // The departure climb: the short break onto the ship's prograde escape
            // state and the ballistic conic out to the departure ceiling. The
            // crossing's total is an output here, so the climb gets the time the
            // seed burn and brake leave; a request below the minimum-energy climb
            // hyperbolises it rather than stretching the break. Outside every
            // sphere there is no climb: the burn starts from the ship's state.
            Vector3d departureVelocityAtStart = Vector3d.Zero;
            double departureCeiling = 0.0;
            double seedBurnSeconds = MinimumArcSeconds;
            Vector3d referenceDepartureVelocity = ship.VelocityMps;
            if (hasClimb)
            {
                _model.ComputeState(departureBody, ship.JulianDate,
                    out _, out departureVelocityAtStart);
                departureCeiling = departureBand.DepartureRadiusM;
                referenceDepartureVelocity = ship.VelocityMps
                                             - departureVelocityAtStart;
                if (referenceCruise != null)
                {
                    _model.ComputeState(departureBody, referenceCruise.StartJulianDate,
                        out _, out Vector3d departureAtExit);
                    referenceDepartureVelocity =
                        referenceCruise.Path.VelocityAt(0.0) - departureAtExit;
                    seedBurnSeconds = Math.Max(
                        (referenceDepartureVelocity.Magnitude
                         - Math.Sqrt(2.0 * departureBody.Gm / departureCeiling))
                        / acceleration, MinimumArcSeconds);
                }
            }
            else if (referenceCruise != null)
            {
                referenceDepartureVelocity =
                    referenceCruise.Path.VelocityAt(0.0) - ship.VelocityMps;
                seedBurnSeconds = Math.Max(
                    referenceDepartureVelocity.Magnitude / acceleration,
                    MinimumArcSeconds);
            }

            // A half-and-half kinematic split is the seed: the straight-line
            // estimate is t_burn = t_brake = sqrt(distance / acceleration).
            double seedEntryJd = arrivalJulianDate - chain.TotalSeconds / SecondsPerDay;
            _model.ComputeState(target, seedEntryJd,
                out Vector3d targetAtSeedEntry, out Vector3d targetVelocityAtSeedEntry);
            Vector3d seedEntryPosition = targetAtSeedEntry + chain.EntryLocalPositionM;
            Vector3d seedEntryVelocity = targetVelocityAtSeedEntry
                                         + chain.EntryLocalVelocityMps;
            double crossingDistance = Math.Max(
                Vector3d.Distance(ship.PositionM, seedEntryPosition), 1.0);
            double halfSeconds = Math.Max(Math.Sqrt(crossingDistance / acceleration),
                MinimumArcSeconds);

            double climbAvailable = totalSeconds - chain.TotalSeconds
                                    - seedBurnSeconds - halfSeconds;
            DepartureClimb climb = default;
            Vector3d burnStartPosition = ship.PositionM;
            Vector3d burnStartVelocity = ship.VelocityMps;
            if (hasClimb)
            {
                if (!TryBuildDepartureClimb(ship, departureBody, departureCeiling,
                        Math.Max(climbAvailable, MinimumArcSeconds),
                        ClimbSeedSeconds(climbAvailable), cheap: probe,
                        out climb, out string climbReason, forcedVInfinityMps))
                {
                    return Reported(InfeasiblePlan(ship, target, totalSeconds,
                        "burn-only: " + climbReason));
                }

                ClimbBurnStart(departureBody, climb, out burnStartPosition,
                    out burnStartVelocity);
            }

            Vector3d seedRay = hasClimb
                ? referenceDepartureVelocity - climb.EscapeEndLocalVelocityMps
                : referenceDepartureVelocity;
            if (seedRay.SqrMagnitude < 0.5 && hasClimb)
                seedRay = climb.EscapeEndLocalVelocityMps;
            if (seedRay.SqrMagnitude < 0.5 && hasClimb)
                seedRay = ship.VelocityMps - departureVelocityAtStart;
            if (seedRay.SqrMagnitude < 0.5)
                seedRay = new Vector3d(1.0, 0.0, 0.0);
            seedRay = seedRay.Normalized;

            Vector3d seedBrakeDirection = seedEntryVelocity.SqrMagnitude > 1e-6
                ? -seedEntryVelocity.Normalized
                : -seedRay;

            var geometry = new BurnOnlyGeometry(departureBody, target, chain,
                acceleration, climb, probe, _gravity);

            Vector3d straightRay = seedEntryPosition - burnStartPosition;
            straightRay = straightRay.SqrMagnitude > 0.5 ? straightRay.Normalized : seedRay;

            // The second seed is the straight line between the two ends with the
            // brake opposed to it: a different basin from the reference coast's
            // ray, so a stalling first attempt is a real second attempt rather
            // than the same solve again.
            var seeds = new[]
            {
                new BurnOnlySeed(seedRay, seedBrakeDirection, halfSeconds, halfSeconds),
                new BurnOnlySeed(straightRay, -straightRay, halfSeconds, halfSeconds)
            };

            BurnOnlyEvaluation solved = default;
            string failure = null;
            long seamStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            foreach (BurnOnlySeed seed in seeds)
            {
                if (TrySolveBurnOnlySeam(geometry, ship, seed, out solved, out failure))
                {
                    failure = null;
                    break;
                }

                Trace?.Invoke("burn-only: attempt failed: " + failure);
            }

            if (metrics != null)
                metrics.AddBurnOnlySeam(PlanMetrics.Timestamp() - seamStart);

            if (failure != null)
            {
                // Diagnostic only: with the two directions fixed, what do the
                // two durations alone leave? This is the measured velocity
                // residual the failure is reported with, not a second attempt to
                // call a solution.
                TrySolveBurnOnlyDurations(geometry, ship, seeds[0].Ray,
                    seeds[0].BrakeDirection, seeds[0].BurnSeconds, seeds[0].BrakeSeconds,
                    out double diagnosticBurn, out double diagnosticBrake,
                    out double diagnosticPosition, out double diagnosticVelocity);
                Trace?.Invoke($"burn-only: directions fixed, durations " +
                              $"{diagnosticBurn:F0} s and {diagnosticBrake:F0} s leave " +
                              $"{diagnosticPosition:F0} m and {diagnosticVelocity:E2} m/s");

                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    failure + $"; with the two directions fixed the durations reach " +
                    $"{diagnosticPosition:F0} m and {diagnosticVelocity:E2} m/s"));
            }

            solution = solved;

            // The reported arcs are integrated at the certified resolution. The
            // departure burn is rebuilt from the same pinned start state; the
            // brake starts exactly where that burn ends, so the burn-to-brake
            // seam is structural and the brake-to-chain seam carries the
            // forward/backward integration difference.
            _model.ComputeState(_model.Sun, solved.BurnStartJulianDate,
                out Vector3d sunAtBurnStart, out Vector3d sunVelocityAtBurnStart);
            long departureCreateStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                solved.BurnStartJulianDate, solved.BurnSeconds,
                solved.BurnStartPositionM - sunAtBurnStart,
                solved.BurnStartVelocityMps - sunVelocityAtBurnStart, acceleration,
                retrograde: false, samples: Math.Max(512, ArcSamples),
                fixedThrustDirection: solved.Ray, gravity: geometry.Gravity);
            long departureCreateTicks =
                metrics != null ? PlanMetrics.Timestamp() - departureCreateStart : 0L;

            double brakeStartJd = solved.BurnStartJulianDate
                                  + solved.BurnSeconds / SecondsPerDay;
            // The Sun's ephemeris is the origin of the heliocentric frame, but it
            // is read at the brake's own date so the decomposition stays explicit.
            _model.ComputeState(_model.Sun, brakeStartJd,
                out Vector3d sunPositionAtBrakeStart,
                out Vector3d sunVelocityAtBrakeStart);
            Vector3d brakeStartPosition = departureBurn.EndLocalPositionM
                                          + sunPositionAtBrakeStart;
            Vector3d brakeStartVelocity = departureBurn.EndLocalVelocityMps
                                          + sunVelocityAtBrakeStart;
            double entryJd = solved.EntryJulianDate;

            // The first cut has no planetocentric approach leg. If the brake
            // starts inside the target's handover ceiling the deep part of the
            // arrival would need one, and inventing it here would be a different
            // change.
            _model.ComputeState(target, brakeStartJd,
                out Vector3d targetAtBrakeStart, out _);
            double brakeStartRadius = Vector3d.Distance(brakeStartPosition,
                targetAtBrakeStart);
            if (brakeStartRadius < arrivalBand.CeilingRadiusM)
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    $"burn-only: the brake starts {brakeStartRadius / 1000.0:F0} km from " +
                    $"{target.Name}, inside the {arrivalBand.CeilingRadiusM / 1000.0:F0} km " +
                    "handover ceiling; the planetocentric approach leg is not built in " +
                    "this step"));
            }

            long brakeCreateStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            FiniteBurnArc brake = FiniteBurnArc.Create(_model, _model.Sun, brakeStartJd,
                solved.BrakeSeconds, brakeStartPosition - sunPositionAtBrakeStart,
                brakeStartVelocity - sunVelocityAtBrakeStart, acceleration,
                retrograde: true, samples: Math.Max(512, ArcSamples),
                fixedThrustDirection: solved.BrakeDirection, gravity: geometry.Gravity);
            long brakeCreateTicks =
                metrics != null ? PlanMetrics.Timestamp() - brakeCreateStart : 0L;

            var arcs = new List<PlanArc>(8);
            if (hasClimb) AppendDepartureClimb(arcs, ship, departureBody, climb, fast: probe);
            long departureMeasureStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            PlanArc departureBurnArc = MakeFiniteBurnArc(FlightPhase.Cruise,
                _model.Sun.Id, solved.BurnStartJulianDate, brakeStartJd, departureBurn);
            long departureMeasureTicks =
                metrics != null ? PlanMetrics.Timestamp() - departureMeasureStart : 0L;
            metrics?.AddBurn(Math.Max(512, ArcSamples), departureCreateTicks,
                departureMeasureTicks, certified: !probe);
            arcs.Add(departureBurnArc);

            long brakeMeasureStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            PlanArc brakeArc = MakeFiniteBurnArc(FlightPhase.Capture, _model.Sun.Id,
                brakeStartJd, entryJd, brake);
            long brakeMeasureTicks =
                metrics != null ? PlanMetrics.Timestamp() - brakeMeasureStart : 0L;
            metrics?.AddBurn(Math.Max(512, ArcSamples), brakeCreateTicks,
                brakeMeasureTicks, certified: !probe);
            arcs.Add(brakeArc);

            double parkingEndJd;
            ConicArc parking;
            AppendConicCapture(arcs, target, entryJd, chain, Vector3d.Zero,
                out parkingEndJd, out parking);

            if (Vector3d.Dot(solved.BrakeDirection, brakeStartVelocity) >= 0.0)
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: the crossing arrives slower than the target's frame, so " +
                    "the seam needs an acceleration rather than a brake"));
            }

            if (!FixedBurnsClearBodies(arcs, departureBody, target,
                    out string clearanceReason))
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    clearanceReason));
            }

            if (!BurnChainIsClosed(arcs, probe ? geometry.GatePositionM : 1e3,
                    probe ? geometry.GateVelocityMps : 1.0, out string seamReason))
            {
                return Reported(InfeasiblePlan(ship, target, totalSeconds,
                    "burn-only: " + seamReason));
            }

            double actualSeconds = (parkingEndJd - ship.JulianDate) * SecondsPerDay;
            var arrivalState = new ShipState(parkingEndJd, parking.PositionAt(1.0),
                parking.VelocityAt(1.0), ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2, arcs, ship,
                arrivalState);
            plan.FiniteBurnAccelerationMps2 = acceleration;
            plan.CaptureEntryRadiusM = chain.EntryRadiusM;
            plan.CaptureInboundSeconds = chain.InboundSeconds;
            if (hasClimb) plan.DepartureVInfinityMps = climb.VInfinityMps;

            Trace?.Invoke($"burn-only: seam {solved.SeamPositionM.Magnitude:F1} m / " +
                          $"{solved.SeamVelocityMps.Magnitude:E2} m/s; " +
                          (hasClimb
                              ? $"break {climb.BreakSeconds / SecondsPerDay:F4} d, escape " +
                                $"{climb.EscapeSeconds / SecondsPerDay:F4} d " +
                                $"{(climb.IsParabolic ? "parabolic" : $"v∞ {climb.VInfinityMps:F0} m/s")}, "
                              : "heliocentric start, ") +
                          $"burn {solved.BurnSeconds / SecondsPerDay:F4} d, brake " +
                          $"{solved.BrakeSeconds / SecondsPerDay:F4} d, capture " +
                          $"{chain.TotalSeconds / SecondsPerDay:F5} d, total " +
                          $"{actualSeconds / SecondsPerDay:F5} d");

            // A probe is a ranking build: its coarse arcs can read a peak above
            // the drive even though the certified rebuild at the same solution
            // flies at the commanded level. The winner is rebuilt certified before
            // any plan is returned, so the probe's own peak is not a verdict.
            return probe ? plan : Reported(plan);
        }

        /// <summary>
        /// The **short-coast** crossing: the burn-only seam with a heliocentric
        /// conic coast of duration <c>c</c> between the departure burn and the
        /// brake. A requested total <c>T</c> is an input; the brake's duration is
        /// <c>T − break − burn − c − capture</c>, so the six unknowns are the
        /// escape ray (two angles), the departure burn's duration, the coast's
        /// duration, and the brake's direction (two angles). The six residuals are
        /// the conic's end position and velocity against the backward brake's
        /// start. No Lambert solve: the coast is the conic the forward burn's end
        /// state defines.
        ///
        /// At <c>c = 0</c> the conic is a point and the system reduces to the
        /// burn-only seam, so the family is continuous by construction. The solve
        /// is seeded from the moving-target burn-only solution with
        /// <c>c = T − T_burnonly</c>. A request below the burn-only minimum returns
        /// null and keeps the coast path's own refusal; a request whose conic has
        /// no branch reports the residual it reached rather than a plan.
        ///
        /// This runs only from <see cref="PlanAtDuration"/> for a coast refusal in
        /// the gap. <see cref="Plan"/>, its floor search, and the fixed-duration
        /// anchors never take this branch.
        /// </summary>
        private BuildResult? BuildShortCoast(ShipState ship, BodyRuntime target,
            double totalSeconds)
        {
            double acceleration = FixedBurnAccelerationMps2;
            if (!(acceleration > 0.0)) return null;

            if (target.Data.IsStation) return null;

            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            if (departureBody == null) return null;

            _model.ComputeState(target, ship.JulianDate,
                out Vector3d targetAtDeparture, out _);
            if (Vector3d.Distance(ship.PositionM, targetAtDeparture)
                < SphereOfInfluence.RadiusM(target))
            {
                return null;
            }

            HandoverBand departureBand = HandoverBand.For(departureBody, acceleration);
            HandoverBand arrivalBand = HandoverBand.For(target, acceleration);
            if (!departureBand.IsValid || !arrivalBand.IsValid) return null;

            double arrivalRadius = ArrivalEntryRadiusM(arrivalBand, target);

            // The split is chosen at this entry point too, once for the whole
            // short-coast solve: the burn-only seed and the reported climb must
            // both use it, or the seed and the answer would belong to different
            // departures.
            double forcedVInfinityMps = ChooseDepartureVInfinityMps(ship, target,
                totalSeconds);

            double arrivalJd = ship.JulianDate + totalSeconds / SecondsPerDay;
            TransferConic chain = BuildCaptureShape(ship, target, arrivalRadius,
                arrivalJd);
            if (!chain.IsValid) return null;

            // The seed is the burn-only crossing at this state. Its ray, burn,
            // and brake directions are the short-coast solve's starting point,
            // and its total sets the coast's seed duration. A request below that
            // total keeps the coast path's refusal: the short coast adds a coast
            // to the crossing, it cannot subtract time from it.
            TrajectoryPlan burnOnly = PlanBurnOnlyMovingTarget(ship, target,
                totalSeconds, out BurnOnlyEvaluation burnOnlySolution,
                forcedVInfinityMps);
            if (!burnOnly.IsFeasible || burnOnlySolution.Ray.SqrMagnitude < 0.5
                || burnOnly.FlightTimeSeconds > totalSeconds + 1.0)
            {
                // The split's cost choice can leave too long a climb for a
                // duration just above the full-blast minimum: at Mercury 1 g the
                // 16 km/s climb needs 0.64 d and a 3.45-day request leaves less
                // than that after the burns. The full-blast grid's shorter
                // climbs fit; the crossing is rebuilt at the requested total with
                // the first larger excess that does, and the short coast adds the
                // slack. A request below every candidate's own minimum still
                // keeps the coast path's refusal.
                double reference = FullBlastVInfinityReferenceMps * Math.Sqrt(BurnLevelG);
                foreach (double factor in FullBlastVInfinityFactors)
                {
                    double candidateVInfinityMps = reference * factor;
                    if (candidateVInfinityMps <= forcedVInfinityMps + 1.0) continue;
                    TrajectoryPlan candidate = BuildBurnOnly(ship, target, arrivalJd,
                        out BurnOnlyEvaluation candidateSolution, candidateVInfinityMps);
                    if (!candidate.IsFeasible || !(candidate.FlightTimeSeconds > 0.0)
                        || candidate.FlightTimeSeconds > totalSeconds + 1.0)
                        continue;
                    burnOnly = candidate;
                    burnOnlySolution = candidateSolution;
                    forcedVInfinityMps = candidateVInfinityMps;
                    Trace?.Invoke($"short coast: the split's climb does not fit; " +
                                  $"using v∞ {candidateVInfinityMps / 1000.0:F2} km/s");
                    break;
                }
            }

            if (!burnOnly.IsFeasible || burnOnlySolution.Ray.SqrMagnitude < 0.5)
            {
                Trace?.Invoke("short coast: the burn-only seed is not available: " +
                              burnOnly.InfeasibleReason);
                return null;
            }

            if (burnOnly.FlightTimeSeconds > totalSeconds + 1.0)
            {
                Trace?.Invoke($"short coast: {totalSeconds / SecondsPerDay:F5} d is " +
                              $"below the burn-only minimum " +
                              $"{burnOnly.FlightTimeSeconds / SecondsPerDay:F5} d");
                return null;
            }

            // At the minimum the crossing is the burn-only plan itself: the
            // coast is zero, so the short-coast system is the burn-only seam
            // again. Returning that plan rather than rebuilding it at a total a
            // fraction of a second away keeps the advertised minimum flyable —
            // the rebuilt brake would carry the moving-target loop's own
            // two-second date tolerance. This is the same rule FlightSession
            // applies to a preview that sat at its minimum.
            if (Math.Abs(totalSeconds - burnOnly.FlightTimeSeconds)
                <= BurnOnlyDateToleranceSeconds)
            {
                Trace?.Invoke("short coast: at the burn-only minimum, returning it");
                return new BuildResult(burnOnly, burnOnly.FlightTimeSeconds);
            }

            // The requested total fixes the climb's time: the capture and the two
            // solved burns come out of it, and the break and the escape conic take
            // what is left. The coast then absorbs the remainder, so a duration
            // above the burn-only minimum keeps the minimum-energy climb.
            double climbAvailable = totalSeconds - chain.TotalSeconds
                                    - burnOnlySolution.BurnSeconds
                                    - burnOnlySolution.BrakeSeconds;
            if (!TryBuildDepartureClimb(ship, departureBody,
                    departureBand.DepartureRadiusM, Math.Max(climbAvailable, MinimumArcSeconds),
                    ClimbSeedSeconds(climbAvailable), cheap: false,
                    out DepartureClimb climb, out string climbReason,
                    forcedVInfinityMps))
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "short coast: " + climbReason);
            }

            var geometry = new ShortCoastGeometry(departureBody, target, chain,
                acceleration, climb, totalSeconds);

            double coastSeedSeconds = Math.Max(
                totalSeconds - burnOnly.FlightTimeSeconds, 0.0);
            var seed = new BurnOnlySeed(burnOnlySolution.Ray,
                burnOnlySolution.BrakeDirection, burnOnlySolution.BurnSeconds,
                burnOnlySolution.BrakeSeconds);

            Trace?.Invoke($"short coast: seed c {coastSeedSeconds:F1} s from the " +
                          $"burn-only {burnOnly.FlightTimeSeconds / SecondsPerDay:F5} d");

            if (!TrySolveShortCoastSeam(geometry, ship, seed, coastSeedSeconds,
                    out ShortCoastEvaluation solved, out string failure))
            {
                return InfeasiblePlan(ship, target, totalSeconds, failure);
            }

            // The reported arcs are integrated at the certified resolution and
            // rebuilt from the same states and directions the solve settled on:
            // the break ends where the departure burn starts, the conic starts
            // where that burn ends, and the brake starts where the conic ends.
            // The brake's end is the only seam that carries the forward/backward
            // sample difference, exactly as in the burn-only build.
            _model.ComputeState(_model.Sun, solved.BurnStartJulianDate,
                out Vector3d sunAtBurnStart, out Vector3d sunVelocityAtBurnStart);
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                solved.BurnStartJulianDate, solved.BurnSeconds,
                solved.BurnStartPositionM - sunAtBurnStart,
                solved.BurnStartVelocityMps - sunVelocityAtBurnStart, acceleration,
                retrograde: false, samples: Math.Max(512, ArcSamples),
                fixedThrustDirection: solved.Ray, gravity: _gravity);

            double coastStartJd = solved.BurnStartJulianDate
                                  + solved.BurnSeconds / SecondsPerDay;
            double brakeStartJd = solved.BrakeStartJulianDate;
            _model.ComputeState(_model.Sun, brakeStartJd,
                out Vector3d sunAtBrakeStart, out Vector3d sunVelocityAtBrakeStart);

            ConicArc coast = null;
            Vector3d brakeStartPosition;
            Vector3d brakeStartVelocity;
            if (solved.CoastSeconds >= MinimumArcSeconds)
            {
                coast = ConicArc.Create(_model, _model.Sun, coastStartJd,
                    solved.CoastSeconds, departureBurn.EndLocalPositionM,
                    departureBurn.EndLocalVelocityMps);
                coast.LocalStateAt(1.0, out Vector3d localEndPosition,
                    out Vector3d localEndVelocity);
                brakeStartPosition = sunAtBrakeStart + localEndPosition;
                brakeStartVelocity = sunVelocityAtBrakeStart + localEndVelocity;
            }
            else
            {
                // A coast shorter than the shortest arc is a seam, not a phase:
                // it is reported as the point it is and its end state is the
                // burn's.
                brakeStartPosition = sunAtBrakeStart + departureBurn.EndLocalPositionM;
                brakeStartVelocity = sunVelocityAtBrakeStart
                                     + departureBurn.EndLocalVelocityMps;
            }

            _model.ComputeState(target, brakeStartJd,
                out Vector3d targetAtBrakeStart, out _);
            double brakeStartRadius = Vector3d.Distance(brakeStartPosition,
                targetAtBrakeStart);
            if (brakeStartRadius < arrivalBand.CeilingRadiusM)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    $"short coast: the brake starts " +
                    $"{brakeStartRadius / 1000.0:F0} km from {target.Name}, inside " +
                    $"the {arrivalBand.CeilingRadiusM / 1000.0:F0} km handover " +
                    "ceiling; the planetocentric approach leg is not built here");
            }

            if (Vector3d.Dot(solved.BrakeDirection, brakeStartVelocity) >= 0.0)
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "short coast: the crossing arrives slower than the target's " +
                    "frame, so the seam needs an acceleration rather than a brake");
            }

            FiniteBurnArc brake = FiniteBurnArc.Create(_model, _model.Sun,
                brakeStartJd, solved.BrakeSeconds,
                brakeStartPosition - sunAtBrakeStart,
                brakeStartVelocity - sunVelocityAtBrakeStart, acceleration,
                retrograde: true, samples: BrakeSamples,
                fixedThrustDirection: solved.BrakeDirection, gravity: _gravity);

            double entryJd = solved.EntryJulianDate;
            var arcs = new List<PlanArc>(9);
            AppendDepartureClimb(arcs, ship, departureBody, climb, fast: false);
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Cruise, _model.Sun.Id,
                solved.BurnStartJulianDate, coastStartJd, departureBurn));
            if (coast != null)
            {
                arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, coastStartJd,
                    brakeStartJd, coast, Vector3d.Zero));
            }
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Capture, _model.Sun.Id, brakeStartJd,
                entryJd, brake));

            double parkingEndJd;
            ConicArc parking;
            AppendConicCapture(arcs, target, entryJd, chain, Vector3d.Zero,
                out parkingEndJd, out parking);

            if (!FixedBurnsClearBodies(arcs, departureBody, target,
                    out string clearanceReason))
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "short coast: " + clearanceReason);
            }

            if (!BurnChainIsClosed(arcs, 1e3, 1.0, out string seamReason))
            {
                return InfeasiblePlan(ship, target, totalSeconds,
                    "short coast: " + seamReason);
            }

            double actualSeconds = (parkingEndJd - ship.JulianDate) * SecondsPerDay;
            var arrivalState = new ShipState(parkingEndJd, parking.PositionAt(1.0),
                parking.VelocityAt(1.0), ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2, arcs, ship,
                arrivalState);
            plan.FiniteBurnAccelerationMps2 = acceleration;
            plan.CaptureEntryRadiusM = chain.EntryRadiusM;
            plan.CaptureInboundSeconds = chain.InboundSeconds;
            plan.DepartureVInfinityMps = climb.VInfinityMps;

            Trace?.Invoke($"short coast: seam " +
                          $"{solved.SeamPositionM.Magnitude:F1} m / " +
                          $"{solved.SeamVelocityMps.Magnitude:E2} m/s; break " +
                          $"{solved.BreakSeconds / SecondsPerDay:F4} d, burn " +
                          $"{solved.BurnSeconds / SecondsPerDay:F4} d, coast " +
                          $"{solved.CoastSeconds / SecondsPerDay:F5} d, brake " +
                          $"{solved.BrakeSeconds / SecondsPerDay:F4} d, capture " +
                          $"{chain.TotalSeconds / SecondsPerDay:F5} d, total " +
                          $"{actualSeconds / SecondsPerDay:F5} d");

            return new BuildResult(plan, actualSeconds);
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
            if (target.Gm <= 0.0 && !target.Data.IsStation)
                throw new ArgumentException(
                    $"'{targetId}' has no gravitational parameter to orbit and is not " +
                    "a station point target.", nameof(targetId));
            if (!(MaxAccelerationMps2 > 0.0))
                throw new InvalidOperationException("MaxAccelerationMps2 must be positive.");

            return target;
        }

        private BuildResult Build(ShipState ship, BodyRuntime target, double totalSeconds,
            bool fast = false, bool probe = false, PinSeed seed = default)
        {
            if (target.Data.IsStation)
            {
                // The split is chosen once for the whole station build, exactly
                // as for a planet: the same excess speed reaches the reference
                // skeleton, the pin loop, the settle pass and the certificate.
                double stationVInfinityMps = ChooseDepartureVInfinityMps(ship, target,
                    totalSeconds);
                return BuildWithRetry(ship, target, totalSeconds, fast, probe,
                    station: true, seed,
                    forcedVInfinityMps: stationVInfinityMps);
            }

            _model.ComputeState(target, ship.JulianDate, out Vector3d targetAtDeparture, out _);
            if (Vector3d.Distance(ship.PositionM, targetAtDeparture)
                < SphereOfInfluence.RadiusM(target))
            {
                return BuildInsideTarget(ship, target, totalSeconds);
            }

            // The split is chosen once, from the requested duration, and the same
            // excess speed reaches every build this call makes: the reference
            // skeleton, the pin loop, the settle pass and the certified rebuild.
            // `-1` is "no forced excess", the unchanged profile.
            double forcedVInfinityMps = ChooseDepartureVInfinityMps(ship, target,
                totalSeconds);
            return BuildWithRetry(ship, target, totalSeconds, fast, probe, station: false,
                seed: seed, forcedVInfinityMps: forcedVInfinityMps);
        }

        /// <summary>
        /// One profile build plus the near-floor fixed point's single retry: the
        /// same tolerances and gates with the longer pass cap. A station and a
        /// planet both own their own builder, and a probe never pays for the
        /// retry.
        ///
        /// A **seed** starts the pin from a neighbouring build's converged brake
        /// and departure burn. The attempt runs a few passes; if no plan has been
        /// built by then the build is repeated cold at the normal cap, so a seed
        /// that does not fit costs a few cheap passes rather than a full build. A
        /// seeded attempt that does build has passed the same convergence test
        /// and the same chain gate as the cold one.
        /// </summary>
        private BuildResult BuildWithRetry(ShipState ship, BodyRuntime target,
            double totalSeconds, bool fast, bool probe, bool station, PinSeed seed,
            double forcedVInfinityMps)
        {
            if (seed.IsValid)
            {
                int seedPasses = fast || probe ? ProbeSeedOuterPasses : SeedOuterPasses;
                BuildResult seeded = station
                    ? BuildStationTrip(ship, target, totalSeconds, fast, probe,
                        seedPasses, seed, forcedVInfinityMps)
                    : BuildBurnCoastBurn(ship, target, totalSeconds, fast, probe,
                        seedPasses, seed, forcedVInfinityMps);
                // A reported build that closed its chain is the answer: a cold
                // build whose pin plateaus above its own tolerance is accepted on
                // exactly those terms, and the chain gate is the authority. A
                // **probe** is a search input, so it is only accepted when its pin
                // settled; otherwise the verdict is the cold one, because the
                // floor search's frontier is what that verdict decides.
                bool accept = seeded.Built && (seeded.PinSettled || !(fast || probe));
                if (accept) return seeded;

                Trace?.Invoke($"seed fallback at {totalSeconds / SecondsPerDay:F5} d: " +
                              $"{seedPasses} passes did not settle, rebuilding cold");
                Metrics?.NoteSeedFallback();
            }

            BuildResult result = station
                ? BuildStationTrip(ship, target, totalSeconds, fast, probe,
                    forcedVInfinityMps: forcedVInfinityMps)
                : BuildBurnCoastBurn(ship, target, totalSeconds, fast, probe,
                    forcedVInfinityMps: forcedVInfinityMps);
            if (!fast && !probe && !result.Plan.IsFeasible && result.PinStalled)
            {
                // The near-floor brake/departure fixed point can stop at its
                // default pass cap with a residual the chain gate reads as a
                // position miss. Give the same fixed point the longer cap once;
                // the tolerances and the gate are unchanged, so this is a
                // solver retry, not a relaxed answer.
                Trace?.Invoke($"pin retry at {totalSeconds / SecondsPerDay:F5} d: " +
                              $"{DefaultOuterPasses} -> {RetryOuterPasses} passes, " +
                              $"after {result.Plan.InfeasibleReason}");
                Metrics?.NotePinRetry();
                result = station
                    ? BuildStationTrip(ship, target, totalSeconds, fast, probe,
                        RetryOuterPasses, forcedVInfinityMps: forcedVInfinityMps)
                    : BuildBurnCoastBurn(ship, target, totalSeconds, fast, probe,
                        RetryOuterPasses, forcedVInfinityMps: forcedVInfinityMps);
            }

            return result;
        }

        /// <summary>
        /// A trip that starts inside the target's sphere of influence is one
        /// quintic capture onto the parking orbit. It is not the burn–coast–burn
        /// profile: there is no sphere to break out of and no heliocentric leg
        /// to coast, so the planner emits the smooth capture the short trip
        /// needs.
        /// </summary>
        private BuildResult BuildInsideTarget(ShipState ship, BodyRuntime target,
            double totalSeconds)
        {
            double t0 = ship.JulianDate;
            double arrivalJd = t0 + totalSeconds / SecondsPerDay;
            ArrivalGeometry arrival = ComputeArrival(target, ship, arrivalJd,
                SphereOfInfluence.RadiusM(target), insideTarget: true);

            var arcs = new List<PlanArc>(1);
            arcs.Add(MakeArc(FlightPhase.Capture, target.Id, t0, arrivalJd,
                ship.PositionM, ship.VelocityMps, arrival.ParkingPosition,
                arrival.ParkingVelocity));

            double limit = MaxAccelerationMps2 * ThrustMargin;
            double worst = 1.0;
            foreach (PlanArc arc in arcs)
            {
                if (arc.PeakThrustMps2 <= 0.0) continue;
                worst = Math.Max(worst, arc.PeakThrustMps2 / limit);
            }

            double actualSeconds = (arrivalJd - t0) * SecondsPerDay;
            double requiredSeconds = Math.Max(totalSeconds, actualSeconds)
                                     * Math.Clamp(Math.Sqrt(worst), 0.85, 3.0);

            var arrivalState = new ShipState(arrivalJd, arrival.ParkingPosition,
                arrival.ParkingVelocity, ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(target.Id, MaxAccelerationMps2, arcs, ship,
                arrivalState);

            return new BuildResult(plan, requiredSeconds);
        }

        /// <summary>
        /// Build the reference coast the burn–coast–burn solve starts from: a
        /// quintic break out of the departure sphere of influence retargeted so
        /// its endpoint velocity is the Lambert departure velocity, a
        /// heliocentric zero-revolution conic coast about the Sun, and the conic
        /// or hybrid capture chain. The capture geometry sets the entry point and
        /// the coast's time of flight; the solver then chooses the coast that
        /// lands there. The burn–coast–burn build inserts its departure burn and
        /// brake into this skeleton; it is never a plan the planner returns.
        ///
        /// The departure takes a share of the trip in proportion to its
        /// kinematic floor, because the Lambert break velocity is a real
        /// velocity change that a fixed few-hour escape cannot supply; whatever
        /// is left after the capture and the departure is coast. A trip time
        /// too short to leave the coast a phase comes out longer than asked
        /// rather than inventing a faster conic.
        ///
        /// Only one branch exists: the prograde, short-way, zero-revolution
        /// transfer. A date whose transfer angle is 180° or more, or whose
        /// transfer plane contains the ecliptic pole, has no coast. That is an
        /// infeasible trip time, reported with the geometry that rejected it,
        /// not a planner fault and not a long-way fallback.
        /// </summary>
        private BuildResult BuildCoastSkeleton(ShipState ship, BodyRuntime target,
            double totalSeconds, double entryRadiusM, bool fast, bool probe,
            double forcedVInfinityMps = -1.0)
        {
            bool cheap = fast || probe;
            double t0 = ship.JulianDate;

            BodyRuntime departureBody = null;
            foreach (BodyRuntime planet in _model.Sun.Children)
            {
                if (planet == target || planet == target.Parent) continue;

                double soi = SphereOfInfluence.RadiusM(planet);
                if (!double.IsFinite(soi)) continue;

                _model.ComputeState(planet, t0, out Vector3d planetPosition, out _);
                if (Vector3d.Distance(ship.PositionM, planetPosition) < soi)
                {
                    departureBody = planet;
                    break;
                }
            }

            double departureCeiling = departureBody != null
                ? SphereOfInfluence.RadiusM(departureBody) * HandoverBand.CeilingSoiFraction
                : 0.0;

            double arrivalJd = t0 + totalSeconds / SecondsPerDay;
            double captureSeconds = 0.0;
            double escapeSeconds = 0.0;
            double cruiseSeconds = 0.0;
            TransferConic chain = null;
            DepartureClimb climb = default;

            // Same five-pass arrival loop as the conic capture: the capture
            // shape fixes the entry date, the physical departure — the short break
            // and the ballistic escape conic — takes the time it needs from what
            // is left, and the cruise is the remainder. The departure no longer
            // takes a share of the trip: its duration is the break's own
            // commanded-level burn plus the conic's climb, and a trip too short for
            // the minimum-energy climb gets a hyperbolic one rather than a longer
            // break.
            for (int pass = 0; pass < 5; pass++)
            {
                chain = BuildCaptureShape(ship, target, entryRadiusM, arrivalJd);
                if (!chain.IsValid)
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "conic capture: " + chain.InvalidReason);

                captureSeconds = chain.TotalSeconds;
                double available = totalSeconds - captureSeconds;
                if (departureBody != null)
                {
                    if (!TryBuildDepartureClimb(ship, departureBody, departureCeiling,
                            available - MinimumCoastSeconds,
                            ClimbSeedSeconds(available), cheap, out climb,
                            out string climbReason, forcedVInfinityMps))
                    {
                        return InfeasiblePlan(ship, target, totalSeconds,
                            "coast: " + climbReason);
                    }

                    escapeSeconds = climb.ClimbSeconds;
                    cruiseSeconds = Math.Max(available - escapeSeconds,
                        MinimumArcSeconds);
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
            chain = BuildCaptureShape(ship, target, entryRadiusM, arrivalJd);
            if (!chain.IsValid)
                return InfeasiblePlan(ship, target, totalSeconds,
                    "conic capture: " + chain.InvalidReason);

            captureSeconds = chain.TotalSeconds;
            double entryJd = arrivalJd - captureSeconds / SecondsPerDay;
            _model.ComputeState(target, entryJd,
                out Vector3d targetAtEntry, out _);
            Vector3d entryPosition = targetAtEntry + chain.EntryLocalPositionM;

            if (departureBody != null)
            {
                double available = totalSeconds - captureSeconds;
                if (!TryBuildDepartureClimb(ship, departureBody, departureCeiling,
                        available - MinimumCoastSeconds, ClimbSeedSeconds(available),
                        cheap, out climb, out string climbReason, forcedVInfinityMps))
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "coast: " + climbReason);
                }

                escapeSeconds = climb.ClimbSeconds;
            }

            double escapeEndJd = departureBody != null
                ? climb.BurnStartJulianDate
                : t0;
            Vector3d exitPosition = ship.PositionM;
            if (departureBody != null)
            {
                _model.ComputeState(departureBody, escapeEndJd,
                    out Vector3d anchorPosition, out _);
                exitPosition = anchorPosition + climb.EscapeEndLocalPositionM;
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
            $"the coast needs a prograde transfer short of 180 deg";

        /// <summary>
        /// Build the burn–coast–burn profile: the reference coast of
        /// <see cref="BuildCoastSkeleton"/> with the two finite burns inserted. The
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
            int outerPassesLimit = DefaultOuterPasses, PinSeed seed = default,
            double forcedVInfinityMps = -1.0)
        {
            PlanMetrics metrics = Metrics;
            PlanBuildQuality quality = fast ? PlanBuildQuality.FastProbe
                : probe ? PlanBuildQuality.FullFieldProbe
                : PlanBuildQuality.Certified;
            metrics?.NoteBuild(quality);

            double acceleration = FixedBurnAccelerationMps2;
            double t0 = ship.JulianDate;
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
            bool hasClimb = departureBody != null;

            // The departure endpoint is the band's ceiling: the break's job is
            // escape, and the burn starts there and moves outward. The ceiling is
            // the same clearance sphere the profile has always used, so the
            // departure plan does not move. A ship outside every sphere has no
            // break to fly: its departure burn starts from its own heliocentric
            // state at t0 and the pin solves the attitude and duration from
            // there, so there is no departure band to build.
            HandoverBand departureBand = null;
            if (hasClimb)
            {
                departureBand = HandoverBand.For(departureBody, acceleration);
                if (!departureBand.IsValid)
                {
                    return InfeasiblePlan(ship, target, totalSeconds,
                        "burn-coast-burn: " + departureBand.InvalidReason);
                }
            }

            Trace?.Invoke("handover band: departure " +
                          (hasClimb
                              ? $"{departureBand.DepartureRadiusM / 1000.0:F0} km " +
                                "(ceiling)"
                              : "none (heliocentric start)") +
                          $", arrival {arrivalBand.ArrivalRadiusM / 1000.0:F0} km " +
                          $"(floor), chain entry {arrivalRadius / 1000.0:F0} km, " +
                          $"ceiling {arrivalBand.CeilingRadiusM / 1000.0:F0} km");

            // The reference coast supplies the capture chain, the handover the
            // brake spends, and a first guess at the outbound direction and the
            // burn's size. It is built at the floor entry radius, so its timing
            // and its handover are the ones the profile flies.
            BuildResult reference = BuildCoastSkeleton(ship, target, totalSeconds,
                arrivalRadius, cheap, probe, forcedVInfinityMps);
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

            // The departure climb's ceiling: the handover band's 1.02 × SOI. The
            // break is no longer stretched to a radial pin on this sphere; it is a
            // short burn to the ship's own prograde escape state, and a ballistic
            // conic carries that state from the parking radius out to this ceiling.
            // A heliocentric start has no ceiling and no escape speed to climb to.
            double climbCeilingRadius = hasClimb ? departureBand.DepartureRadiusM : 0.0;

            // The departure burn's seed: the target is the reference coast's local
            // departure velocity, and its duration the velocity change over the
            // commanded acceleration. The burn's start state and its solved attitude
            // belong to the climb and the Newton; this only starts the solve in the
            // right basin. Without a climb the seed is the ship's own velocity
            // change to the coast's departure, and there is no escape speed to
            // subtract.
            Vector3d seedDepartureVelocity = referenceCruise.Path.VelocityAt(0.0)
                                             - ship.VelocityMps;
            double seedEscapeSpeed = 0.0;
            if (hasClimb)
            {
                _model.ComputeState(departureBody, referenceCruise.StartJulianDate,
                    out _, out Vector3d departureAtExit);
                seedDepartureVelocity =
                    referenceCruise.Path.VelocityAt(0.0) - departureAtExit;
                seedEscapeSpeed = Math.Sqrt(2.0 * departureBody.Gm / climbCeilingRadius);
            }

            Vector3d burnDirection = seedDepartureVelocity.SqrMagnitude > 0.5
                ? seedDepartureVelocity.Normalized
                : new Vector3d(1.0, 0.0, 0.0);

            double burnSeconds = Math.Max(0.0,
                seedDepartureVelocity.Magnitude - seedEscapeSpeed) / acceleration;
            burnSeconds = Math.Max(burnSeconds, MinimumArcSeconds);

            double breakSeconds = MinimumArcSeconds;
            DepartureClimb climb = default;

            double brakeSeconds = MinimumArcSeconds;
            Vector3d brakeDirection = -referenceCruise.Path.VelocityAt(1.0).Normalized;
            Vector3d brakeStartPosition = entryPosition;
            Vector3d brakeStartVelocity = entryVelocity;

            // A neighbouring build's converged pin starts this one instead of the
            // reference coast's estimate. The escape ray is deliberately not part
            // of the seed: the climb is derived from the time budget on every
            // pass, and the ray decides which basin the pin settles in.
            bool seeded = seed.IsValid;
            if (seeded)
            {
                burnSeconds = seed.BurnSeconds;
                burnDirection = seed.BurnDirection.Normalized;
                brakeSeconds = seed.BrakeSeconds;
                brakeDirection = seed.BrakeDirection.Normalized;
            }

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
            Vector3d previousDirection = Vector3d.Zero;
            double brakeMismatch = double.PositiveInfinity;
            bool pinSettled = false;

            int outerPasses = fast
                ? Math.Min(FastProbeOuterPasses, outerPassesLimit)
                : outerPassesLimit;
            for (int pass = 0; pass < outerPasses; pass++)
            {
                long passStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
                metrics?.NotePinPass(quality);
                _model.ComputeState(_model.Sun, entryJd,
                    out Vector3d sunAtEntry, out Vector3d sunVelocityAtEntry);
                Vector3d brakeLocalPosition;
                Vector3d brakeLocalVelocity;
                long brakeStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
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

                if (metrics != null)
                    metrics.AddPinBrake(PlanMetrics.Timestamp() - brakeStart);

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

                // A cold first pass has a one-second brake, so the approach would
                // be a months-long fall and is dropped. A seeded first pass has the
                // neighbour's brake, and may fly the approach from the start —
                // that is where the seed saves the passes a cold pin spends
                // growing the brake. A seed whose brake still cannot reach the
                // ceiling falls back to the cold pass-0 shape.
                bool seedApproach = pass == 0 && seeded && seed.HasApproach;
                if (!hasApproach && !string.IsNullOrEmpty(approachReason) && !seedApproach)
                {
                    if (metrics != null)
                        metrics.AddPinLoop(PlanMetrics.Timestamp() - passStart);
                    return StretchedPlan(ship, target, totalSeconds, approachReason);
                }

                // A free fall from the ceiling to a brake start this deep can take
                // longer than the whole trip: on a cold first pass the brake is a
                // second long, so its start is the chain's entry and the conic back
                // out to the ceiling is a near-parabolic 119 days. Start the fixed
                // point on the profile's original shape — the coast running to the
                // brake's start — and let the brake grow before the planetocentric
                // leg appears. A seeded first pass whose brake already reaches the
                // ceiling keeps its approach.
                if ((pass == 0 && !seedApproach) || (hasApproach && approachStartJd <= t0))
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

                // A brake that has grown past the departure has already spent
                // more than the trip; there is no departure climb to place.
                if (!(coastEndJd > t0))
                {
                    if (metrics != null)
                        metrics.AddPinLoop(PlanMetrics.Timestamp() - passStart);
                    return StretchedPlan(ship, target, totalSeconds,
                        "burn-coast-burn: the trip time leaves no room for the " +
                        "departure climb and the coast", shortCoastWorthy: true);
                }

                // The trip's time for the departure: the coast's end it has to
                // reach, less the departure burn the last pass solved, the brake's
                // own tail when there is no planetocentric approach, and a minimum
                // coast. The break and the escape climb share it. The break is
                // always the short burn at the commanded level; the climb is the
                // minimum-energy parabolic one when that time allows it and a
                // hyperbolic one when it does not, so a faster trip pays more and
                // the coast absorbs whatever is left.
                double climbAvailable = (coastEndJd - t0) * SecondsPerDay - burnSeconds
                                        - (hasApproach ? 0.0 : brakeSeconds)
                                        - MinimumCoastSeconds;
                if (hasClimb)
                {
                    if (!TryBuildDepartureClimb(ship, departureBody,
                            climbCeilingRadius, climbAvailable,
                            ClimbSeedSeconds(climbAvailable), cheap, out climb,
                            out string climbReason, forcedVInfinityMps))
                    {
                        if (metrics != null)
                            metrics.AddPinLoop(PlanMetrics.Timestamp() - passStart);
                        return StretchedPlan(ship, target, totalSeconds,
                            "burn-coast-burn: " + climbReason, shortCoastWorthy: true);
                    }

                    breakSeconds = climb.BreakSeconds;
                    burnStartJd = climb.BurnStartJulianDate;
                    ClimbBurnStart(departureBody, climb, out burnStartPosition,
                        out burnStartVelocity);
                }
                else
                {
                    // Outside every sphere there is nothing to break out of:
                    // the burn begins at the ship's own state at t0.
                    climb = default;
                    breakSeconds = 0.0;
                    burnStartJd = t0;
                    burnStartPosition = ship.PositionM;
                    burnStartVelocity = ship.VelocityMps;
                }

                long solveStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
                bool departureSolved = TrySolvePinnedDepartureWithFallback(
                    ship, departureBody,
                    acceleration, burnStartJd, burnStartPosition, burnStartVelocity,
                    coastEndJd, coastEndPosition, ref burnDirection, ref burnSeconds,
                    solveSamples, fallbackSamples, burnGravity,
                    out coastStartPosition, out coastStartVelocity,
                    out coastArrivalVelocity, out coastSeconds, out string reason);
                if (metrics != null)
                    metrics.AddPinSolve(PlanMetrics.Timestamp() - solveStart);
                if (!departureSolved)
                {
                    if (metrics != null)
                        metrics.AddPinLoop(PlanMetrics.Timestamp() - passStart);
                    return StretchedPlan(ship, target, totalSeconds, reason,
                        pinStalled: !fast && !probe && !pinSettled,
                        shortCoastWorthy: true);
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
                    && (burnDirection - previousDirection).Magnitude <= directionTolerance;

                Trace?.Invoke($"pin pass {pass}: brake {brakeSeconds:F3} s, " +
                    $"mismatch {brakeMismatch:E2} m/s, " +
                    $"burn {burnSeconds:F3} s, " +
                    (hasClimb
                        ? $"break {breakSeconds:F3} s, escape " +
                          $"{climb.EscapeSeconds / SecondsPerDay:F4} d " +
                          $"({(climb.IsParabolic ? "parabolic" : $"v∞ {climb.VInfinityMps:F0} m/s")}), "
                        : "heliocentric start, ") +
                    $"converged {converged}" +
                    (hasApproach ? $", approach {approachSeconds:F1} s" : ""));

                previousBurnSeconds = burnSeconds;
                previousDirection = burnDirection;
                brakeSeconds = nextBrakeSeconds;
                brakeDirection = nextBrakeDirection;
                if (metrics != null)
                    metrics.AddPinLoop(PlanMetrics.Timestamp() - passStart);
                if (converged)
                {
                    pinSettled = true;
                    break;
                }
            }

            // Settle the climb and the departure against the brake the loop
            // finished with, so the arcs that are about to be built all belong to
            // one climb. A converged loop needs one pass; a near-floor probe whose
            // fixed point is still drifting gets the old three.
            int settlePasses = cheap ? 0 : 3;
            for (int settle = 0; settle < settlePasses; settle++)
            {
                long settleStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
                metrics?.NoteSettlePass();
                double previousSettleBurn = burnSeconds;
                Vector3d previousSettleDirection = burnDirection;

                if (!(coastEndJd > t0))
                {
                    return StretchedPlan(ship, target, totalSeconds,
                        "burn-coast-burn: the trip time leaves no room for the " +
                        "departure climb and the coast", shortCoastWorthy: true);
                }

                double settleAvailable = (coastEndJd - t0) * SecondsPerDay - burnSeconds
                                         - (hasApproach ? 0.0 : brakeSeconds)
                                         - MinimumCoastSeconds;
                if (hasClimb)
                {
                    if (!TryBuildDepartureClimb(ship, departureBody,
                            climbCeilingRadius, settleAvailable,
                            ClimbSeedSeconds(settleAvailable), cheap, out climb,
                            out string climbReason, forcedVInfinityMps))
                    {
                        return StretchedPlan(ship, target, totalSeconds,
                            "burn-coast-burn: " + climbReason, shortCoastWorthy: true);
                    }

                    breakSeconds = climb.BreakSeconds;
                    burnStartJd = climb.BurnStartJulianDate;
                    ClimbBurnStart(departureBody, climb, out burnStartPosition,
                        out burnStartVelocity);
                }
                else
                {
                    climb = default;
                    breakSeconds = 0.0;
                    burnStartJd = t0;
                    burnStartPosition = ship.PositionM;
                    burnStartVelocity = ship.VelocityMps;
                }

                if (!TrySolvePinnedDepartureWithFallback(ship, departureBody,
                        acceleration, burnStartJd, burnStartPosition, burnStartVelocity,
                        coastEndJd, coastEndPosition, ref burnDirection, ref burnSeconds,
                        solveSamples, fallbackSamples, burnGravity,
                        out coastStartPosition, out coastStartVelocity,
                        out coastArrivalVelocity, out coastSeconds, out string settleReason))
                {
                    return StretchedPlan(ship, target, totalSeconds, settleReason,
                        shortCoastWorthy: true);
                }

                if (metrics != null)
                    metrics.AddSettleLoop(PlanMetrics.Timestamp() - settleStart);
                if (Math.Abs(burnSeconds - previousSettleBurn) <= durationTolerance
                    && (burnDirection - previousSettleDirection).Magnitude
                    <= directionTolerance)
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

            // The reported arcs use the climb the solve settled on; its ceiling
            // state is the departure burn's start, so the break, the conic, and the
            // burn share one continuous state. A heliocentric start has no climb:
            // its burn begins at the ship's own state at t0.
            if (hasClimb)
            {
                breakSeconds = climb.BreakSeconds;
                burnStartJd = climb.BurnStartJulianDate;
                ClimbBurnStart(departureBody, climb, out burnStartPosition,
                    out burnStartVelocity);
            }
            else
            {
                breakSeconds = 0.0;
                burnStartJd = t0;
                burnStartPosition = ship.PositionM;
                burnStartVelocity = ship.VelocityMps;
            }

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
            long departureCreateStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                burnStartJd, burnSeconds, burnStartPosition - sunForBurn,
                burnStartVelocity - sunVelocityForBurn, acceleration,
                retrograde: false, samples: burnSamples,
                fixedThrustDirection: burnDirection, gravity: burnGravity);
            long departureCreateTicks =
                metrics != null ? PlanMetrics.Timestamp() - departureCreateStart : 0L;

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
            long brakeCreateStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            FiniteBurnArc brake = FiniteBurnArc.Create(_model, _model.Sun,
                brakeStartJd, brakeSeconds, brakeStartPosition - sunAtBrakeArc,
                brakeStartFinalVelocity - sunVelocityAtBrakeArc, acceleration,
                retrograde: true, samples: brakeSamples,
                fixedThrustDirection: brakeDirection, gravity: burnGravity);
            long brakeCreateTicks =
                metrics != null ? PlanMetrics.Timestamp() - brakeCreateStart : 0L;

            var arcs = new List<PlanArc>(10);
            if (hasClimb) AppendDepartureClimb(arcs, ship, departureBody, climb, cheap);

            long departureMeasureStart = metrics != null ? PlanMetrics.Timestamp() : 0L;
            PlanArc departureBurnArc = MakeFiniteBurnArc(FlightPhase.Cruise,
                _model.Sun.Id, burnStartJd, coastStartJd, departureBurn, cheap);
            long departureMeasureTicks =
                metrics != null ? PlanMetrics.Timestamp() - departureMeasureStart : 0L;
            metrics?.AddBurn(burnSamples, departureCreateTicks, departureMeasureTicks,
                quality == PlanBuildQuality.Certified);
            arcs.Add(departureBurnArc);

            arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, coastStartJd,
                coastEndFinalJd, coast, Vector3d.Zero, cheap));

            if (approach != null)
                arcs.Add(approach);

            if (brakeSeconds > 0.0)
            {
                long brakeMeasureStart =
                    metrics != null ? PlanMetrics.Timestamp() : 0L;
                PlanArc brakeArc = MakeFiniteBurnArc(FlightPhase.Capture,
                    _model.Sun.Id, brakeStartJd, entryJd, brake, cheap);
                long brakeMeasureTicks =
                    metrics != null ? PlanMetrics.Timestamp() - brakeMeasureStart : 0L;
                metrics?.AddBurn(brakeSamples, brakeCreateTicks, brakeMeasureTicks,
                    quality == PlanBuildQuality.Certified);
                arcs.Add(brakeArc);
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
                && !BurnChainIsClosed(arcs, 1e3, 1.0, out string seamReason))
            {
                // A chain miss after an unsettled pin is a solver artefact, not a
                // verdict on the trip time: the certified build retries the same
                // fixed point with the longer pass cap once (see Build).
                return StretchedPlan(ship, target, totalSeconds,
                    "burn-coast-burn: " + seamReason,
                    pinStalled: !fast && !probe && !pinSettled,
                    shortCoastWorthy: true);
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
            if (hasClimb) plan.DepartureVInfinityMps = climb.VInfinityMps;

            // The pin state this build settled on, so a neighbouring build can
            // start from it. Only a build that converged and closed its chain
            // gets here; a refusal or a stretched plan carries no seed.
            var settledSeed = new PinSeed(totalSeconds, brakeSeconds, brakeDirection,
                burnSeconds, burnDirection, hasApproach);
            plan.PinSeed = settledSeed;
            return new BuildResult(plan, requiredSeconds, pinSettled: pinSettled,
                seed: settledSeed, built: true);
        }

        /// <summary>
        /// The **station arrival**: the burn–coast–burn profile with a point
        /// target. A station is massless and has no sphere of influence, so the
        /// plan has no capture chain, no parking orbit, no handover band and no
        /// planetocentric approach. Its terminal condition is the hold state at
        /// the requested arrival date — the station's own state
        /// <see cref="StationHoldDistanceM"/> astern along its velocity
        /// direction, with the station's velocity — and the final burn is a
        /// velocity match, not necessarily a brake:
        ///
        /// * when the ship starts inside a planet's sphere, the departure is the
        ///   physical climb of the planet profile (short break and escape
        ///   conic) and the burn starts at the conic's ceiling state;
        /// * when it starts outside every sphere, the departure burn starts from
        ///   the ship's own heliocentric state at t0, with no break and no
        ///   escape arc.
        ///
        /// The brake/departure fixed point is the planet profile's: the brake is
        /// integrated backward from the hold state, the departure burn's
        /// attitude and duration are solved against the Lambert coast that ends
        /// there, and the brake's direction and duration are updated from the
        /// velocity change the coast's arrival needs. Unlike the planet
        /// arrival, a station rendezvous can shed **or add** speed, so no
        /// retrograde test rejects it.
        /// </summary>
        private BuildResult BuildStationTrip(ShipState ship, BodyRuntime station,
            double totalSeconds, bool fast = false, bool probe = false,
            int outerPassesLimit = DefaultOuterPasses, PinSeed seed = default,
            double forcedVInfinityMps = -1.0)
        {
            double acceleration = FixedBurnAccelerationMps2;
            double t0 = ship.JulianDate;
            bool cheap = fast || probe;
            int burnSamples = fast ? FastProbeBurnSamples
                : probe ? ProbeDepartureSamples
                : Math.Max(512, ArcSamples);
            int brakeSamples = fast ? FiniteBurnArc.MaximumSamples : BrakeSamples;
            IGravityField burnGravity = fast ? null : _gravity;
            int solveSamples = probe ? ProbeDepartureSamples : PinnedDepartureSamples;
            int fallbackSamples = cheap ? 0 : burnSamples;

            if (!(acceleration > 0.0))
            {
                return InfeasiblePlan(ship, station, totalSeconds,
                    "station: the drive commands no fixed-burn acceleration");
            }

            double arrivalJd = t0 + totalSeconds / SecondsPerDay;
            StationHoldState(station, arrivalJd, out Vector3d holdPosition,
                out Vector3d holdVelocity);

            BodyRuntime departureBody = FindDepartureBody(ship, station, t0);
            bool hasClimb = departureBody != null;
            double climbCeilingRadius = 0.0;
            if (hasClimb)
            {
                HandoverBand departureBand = HandoverBand.For(departureBody, acceleration);
                if (!departureBand.IsValid)
                {
                    return InfeasiblePlan(ship, station, totalSeconds,
                        "station: " + departureBand.InvalidReason);
                }

                climbCeilingRadius = departureBand.DepartureRadiusM;
            }

            // The seed is the velocity the rendezvous has to change: the burn and
            // the brake each start at |v_station - v_ship| / acceleration, the
            // scale of both fixed burns. The first pass re-seeds the burn from a
            // Lambert coast (below), so this only has to be a sane scale.
            double relativeSpeed = (holdVelocity - ship.VelocityMps).Magnitude;
            double seedSeconds = Math.Max(relativeSpeed / acceleration, MinimumArcSeconds);

            Vector3d seedRay = holdPosition - ship.PositionM;
            seedRay = seedRay.SqrMagnitude > 0.5
                ? seedRay.Normalized
                : new Vector3d(1.0, 0.0, 0.0);
            Vector3d seedBrake = holdVelocity - ship.VelocityMps;
            seedBrake = seedBrake.SqrMagnitude > 1.0
                ? -seedBrake.Normalized
                : -seedRay;

            Vector3d burnDirection = seedRay;
            Vector3d brakeDirection = seedBrake;
            double burnSeconds = seedSeconds;
            double brakeSeconds = seedSeconds;

            // A neighbouring station build's converged pin replaces the
            // relative-speed estimate.
            bool seeded = seed.IsValid;
            if (seeded)
            {
                burnDirection = seed.BurnDirection.Normalized;
                brakeDirection = seed.BrakeDirection.Normalized;
                burnSeconds = seed.BurnSeconds;
                brakeSeconds = seed.BrakeSeconds;
            }

            DepartureClimb climb = default;
            double burnStartJd = t0;
            Vector3d burnStartPosition = ship.PositionM;
            Vector3d burnStartVelocity = ship.VelocityMps;
            double brakeStartJd = arrivalJd;
            Vector3d brakeStartPosition = holdPosition;
            Vector3d brakeStartVelocity = holdVelocity;
            Vector3d coastArrivalVelocity = Vector3d.Zero;
            double coastSeconds = 0.0;

            double brakeTolerance = fast ? FastBrakeConvergenceMps : BrakeConvergenceMps;
            double directionTolerance = fast
                ? FastBrakeDirectionTolerance
                : BrakeDirectionTolerance;
            double durationTolerance = fast
                ? FastBrakeDurationToleranceSeconds
                : BrakeDurationToleranceSeconds;

            double previousBurnSeconds = double.NaN;
            Vector3d previousDirection = Vector3d.Zero;
            bool pinSettled = false;

            int outerPasses = fast ? FastProbeOuterPasses : outerPassesLimit;
            for (int pass = 0; pass < outerPasses; pass++)
            {
                // The brake ends on the hold state at the arrival date.
                _model.ComputeState(_model.Sun, arrivalJd,
                    out Vector3d sunAtHold, out Vector3d sunVelocityAtHold);
                Vector3d brakeLocalPosition;
                Vector3d brakeLocalVelocity;
                if (burnGravity == null)
                {
                    FiniteBurnArc.SimulateBackward(_model.Sun,
                        holdPosition - sunAtHold, holdVelocity - sunVelocityAtHold,
                        acceleration, retrograde: true, brakeSeconds, brakeSamples,
                        out brakeLocalPosition, out brakeLocalVelocity,
                        brakeDirection);
                }
                else
                {
                    FiniteBurnArc.SimulateBackward(_model, burnGravity, _model.Sun,
                        arrivalJd, holdPosition - sunAtHold,
                        holdVelocity - sunVelocityAtHold, acceleration,
                        retrograde: true, brakeSeconds, brakeSamples,
                        out brakeLocalPosition, out brakeLocalVelocity,
                        brakeDirection);
                }

                brakeStartJd = arrivalJd - brakeSeconds / SecondsPerDay;
                _model.ComputeState(_model.Sun, brakeStartJd,
                    out Vector3d sunAtBrakeStart, out Vector3d sunVelocityAtBrakeStart);
                brakeStartPosition = sunAtBrakeStart + brakeLocalPosition;
                brakeStartVelocity = sunVelocityAtBrakeStart + brakeLocalVelocity;

                if (!(brakeStartJd > t0))
                {
                    return StretchedPlan(ship, station, totalSeconds,
                        "station: the trip time leaves no room for the departure burn " +
                        "and the coast");
                }

                if (hasClimb)
                {
                    double climbAvailable = (brakeStartJd - t0) * SecondsPerDay
                                            - burnSeconds - MinimumCoastSeconds;
                    if (!TryBuildDepartureClimb(ship, departureBody,
                            climbCeilingRadius, climbAvailable,
                            ClimbSeedSeconds(climbAvailable), cheap, out climb,
                            out string climbReason, forcedVInfinityMps))
                    {
                        return StretchedPlan(ship, station, totalSeconds,
                            "station: " + climbReason);
                    }

                    burnStartJd = climb.BurnStartJulianDate;
                    ClimbBurnStart(departureBody, climb, out burnStartPosition,
                        out burnStartVelocity);
                }
                else
                {
                    climb = default;
                    burnStartJd = t0;
                    burnStartPosition = ship.PositionM;
                    burnStartVelocity = ship.VelocityMps;
                }

                // Re-seed the pin once from a Lambert coast between the burn's
                // start and the brake's start. The straight-line/relative-speed
                // seed is coarse for a long trip: a burn seeded at the whole
                // relative speed is many times the transfer's own, and the
                // Newton can sit in the wrong basin. The reference coast is the
                // seed the planet profile already uses. A seeded build already
                // has a converged burn and keeps it.
                if (pass == 0 && !seeded)
                {
                    double seedCoastGuess = burnSeconds;
                    for (int reseed = 0; reseed < 2; reseed++)
                    {
                        double seedCoastSeconds =
                            (brakeStartJd - burnStartJd) * SecondsPerDay
                            - seedCoastGuess;
                        if (!(seedCoastSeconds > MinimumArcSeconds)) break;

                        LambertSolution seedCoast = LambertSolver.SolveShortWay(
                            burnStartPosition, brakeStartPosition,
                            seedCoastSeconds, _model.Sun.Gm);
                        if (!seedCoast.HasSolution) break;

                        Vector3d seedChange = seedCoast.DepartureVelocityMps
                                              - burnStartVelocity;
                        if (seedChange.SqrMagnitude > 1e-3)
                            burnDirection = seedChange.Normalized;
                        seedCoastGuess = Math.Max(
                            seedChange.Magnitude / acceleration,
                            MinimumArcSeconds);
                    }

                    burnSeconds = seedCoastGuess;
                }

                if (!TrySolvePinnedDepartureWithFallback(ship, departureBody,
                        acceleration, burnStartJd, burnStartPosition, burnStartVelocity,
                        brakeStartJd, brakeStartPosition, ref burnDirection,
                        ref burnSeconds, solveSamples, fallbackSamples, burnGravity,
                        out _, out _, out coastArrivalVelocity, out coastSeconds,
                        out string reason))
                {
                    return StretchedPlan(ship, station, totalSeconds,
                        "station: " + reason);
                }

                double coastBeginJd = burnStartJd + burnSeconds / SecondsPerDay;
                if (!(coastBeginJd < brakeStartJd))
                {
                    return StretchedPlan(ship, station, totalSeconds,
                        "station: the trip time leaves no room for the coast between " +
                        "the burn and the brake");
                }

                // The brake's update is the planet profile's: what the burn would
                // have to supply so the brake, run backward from the hold state,
                // starts with the velocity the coast arrives with. A station has
                // no approach leg, so the whole residual is the brake's.
                double brakeMismatch = (brakeStartVelocity - coastArrivalVelocity)
                    .Magnitude;
                Vector3d thrustDelta = brakeDirection * (acceleration * brakeSeconds);
                Vector3d gravityDelta = holdVelocity - brakeStartVelocity - thrustDelta;
                Vector3d required = holdVelocity - coastArrivalVelocity - gravityDelta;

                double nextBrakeSeconds = Math.Max(required.Magnitude / acceleration,
                    MinimumArcSeconds);
                Vector3d nextBrakeDirection = required.SqrMagnitude > 1e-12
                    ? required.Normalized
                    : brakeDirection;

                bool converged = pass > 0
                    && brakeMismatch <= brakeTolerance
                    && Math.Abs(nextBrakeSeconds - brakeSeconds) <= durationTolerance
                    && (nextBrakeDirection - brakeDirection).Magnitude
                    <= directionTolerance
                    && Math.Abs(burnSeconds - previousBurnSeconds) <= durationTolerance
                    && (burnDirection - previousDirection).Magnitude <= directionTolerance;

                Trace?.Invoke($"station pin pass {pass}: brake {brakeSeconds:F3} s, " +
                    $"mismatch {brakeMismatch:E2} m/s, burn {burnSeconds:F3} s" +
                    (hasClimb
                        ? $", break {climb.BreakSeconds:F1} s, escape " +
                          $"{climb.EscapeSeconds / SecondsPerDay:F4} d"
                        : ", heliocentric start") +
                    $", converged {converged}");

                previousBurnSeconds = burnSeconds;
                previousDirection = burnDirection;
                brakeSeconds = nextBrakeSeconds;
                brakeDirection = nextBrakeDirection;
                if (converged)
                {
                    pinSettled = true;
                    break;
                }
            }

            // Settle the climb and the departure against the brake the loop
            // finished with, so every arc belongs to one climb. A converged loop
            // needs one pass; a near-floor probe that is still drifting gets the
            // old three.
            int settlePasses = cheap ? 0 : 3;
            for (int settle = 0; settle < settlePasses; settle++)
            {
                double previousSettleBurn = burnSeconds;
                Vector3d previousSettleDirection = burnDirection;

                if (!(brakeStartJd > t0))
                {
                    return StretchedPlan(ship, station, totalSeconds,
                        "station: the trip time leaves no room for the departure burn " +
                        "and the coast");
                }

                if (hasClimb)
                {
                    double settleAvailable = (brakeStartJd - t0) * SecondsPerDay
                                             - burnSeconds - MinimumCoastSeconds;
                    if (!TryBuildDepartureClimb(ship, departureBody,
                            climbCeilingRadius, settleAvailable,
                            ClimbSeedSeconds(settleAvailable), cheap, out climb,
                            out string climbReason, forcedVInfinityMps))
                    {
                        return StretchedPlan(ship, station, totalSeconds,
                            "station: " + climbReason);
                    }

                    burnStartJd = climb.BurnStartJulianDate;
                    ClimbBurnStart(departureBody, climb, out burnStartPosition,
                        out burnStartVelocity);
                }

                if (!TrySolvePinnedDepartureWithFallback(ship, departureBody,
                        acceleration, burnStartJd, burnStartPosition, burnStartVelocity,
                        brakeStartJd, brakeStartPosition, ref burnDirection,
                        ref burnSeconds, solveSamples, fallbackSamples, burnGravity,
                        out _, out _, out coastArrivalVelocity, out coastSeconds,
                        out string settleReason))
                {
                    return StretchedPlan(ship, station, totalSeconds,
                        "station: " + settleReason);
                }

                if (Math.Abs(burnSeconds - previousSettleBurn) <= durationTolerance
                    && (burnDirection - previousSettleDirection).Magnitude
                    <= directionTolerance)
                {
                    break;
                }
            }

            if (!(coastSeconds > MinimumArcSeconds))
            {
                return StretchedPlan(ship, station, totalSeconds,
                    "station: the trip time leaves no room for the coast between the " +
                    "burns");
            }

            if (hasClimb)
            {
                burnStartJd = climb.BurnStartJulianDate;
                ClimbBurnStart(departureBody, climb, out burnStartPosition,
                    out burnStartVelocity);
            }
            else
            {
                burnStartJd = t0;
                burnStartPosition = ship.PositionM;
                burnStartVelocity = ship.VelocityMps;
            }

            double coastStartJd = burnStartJd + burnSeconds / SecondsPerDay;

            // The reported burn integrates at the plan's resolution, not the
            // solve's, so the coast is re-solved from where it actually ends.
            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunForBurn, out Vector3d sunVelocityForBurn);
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                burnStartJd, burnSeconds, burnStartPosition - sunForBurn,
                burnStartVelocity - sunVelocityForBurn, acceleration,
                retrograde: false, samples: burnSamples,
                fixedThrustDirection: burnDirection, gravity: burnGravity);

            double coastFinalSeconds = (brakeStartJd - coastStartJd) * SecondsPerDay;
            if (!(coastFinalSeconds > MinimumArcSeconds))
            {
                return StretchedPlan(ship, station, totalSeconds,
                    "station: the trip time leaves no room for the coast between the " +
                    "burns");
            }

            _model.ComputeState(_model.Sun, coastStartJd,
                out Vector3d sunAtCoastStart, out Vector3d sunVelocityAtCoastStart);
            _model.ComputeState(_model.Sun, brakeStartJd,
                out Vector3d sunAtBrakeArc, out Vector3d sunVelocityAtBrakeArc);
            Vector3d coastStartPosition = departureBurn.EndLocalPositionM
                                          + sunAtCoastStart;

            LambertSolution coastSolution = LambertSolver.SolveShortWay(
                coastStartPosition, brakeStartPosition, coastFinalSeconds,
                _model.Sun.Gm);
            if (!coastSolution.HasSolution)
            {
                return InfeasiblePlan(ship, station, totalSeconds,
                    CoastReason(coastSolution));
            }

            if (!TryCorrectCoastDeparture(_model.Sun,
                    coastStartPosition - sunAtCoastStart,
                    coastSolution.DepartureVelocityMps - sunVelocityAtCoastStart,
                    coastFinalSeconds, brakeStartPosition - sunAtBrakeArc,
                    out Vector3d correctedLocalVelocity))
            {
                return InfeasiblePlan(ship, station, totalSeconds,
                    "station: the coast cannot be joined to the brake's start");
            }

            Vector3d coastStartVelocity = correctedLocalVelocity + sunVelocityAtCoastStart;
            ConicArc coast = ConicArc.Create(_model, _model.Sun, coastStartJd,
                coastFinalSeconds, coastStartPosition - sunAtCoastStart,
                coastStartVelocity - sunVelocityAtCoastStart);

            // The brake is built from the state the solve walked back from the
            // hold point, so its forward end is the hold state itself: the
            // arrival is exact, and the coast-to-brake seam carries the
            // re-solved coast's own velocity difference instead.
            FiniteBurnArc brake = FiniteBurnArc.Create(_model, _model.Sun,
                brakeStartJd, brakeSeconds, brakeStartPosition - sunAtBrakeArc,
                brakeStartVelocity - sunVelocityAtBrakeArc, acceleration,
                retrograde: true, samples: brakeSamples,
                fixedThrustDirection: brakeDirection, gravity: burnGravity);

            var arcs = new List<PlanArc>(5);
            if (hasClimb) AppendDepartureClimb(arcs, ship, departureBody, climb, cheap);
            arcs.Add(MakeFiniteBurnArc(FlightPhase.Cruise, _model.Sun.Id,
                burnStartJd, coastStartJd, departureBurn, cheap));
            arcs.Add(MakeConicArc(FlightPhase.Cruise, _model.Sun.Id, coastStartJd,
                brakeStartJd, coast, Vector3d.Zero, cheap));

            // The velocity match is the arrival. Give that one arc a finer
            // integrator hint than the default 250-step one so the match's own
            // truncation stays well below the hold-point contract; the rest of
            // the plan keeps its default cost.
            PlanArc match = MakeFiniteBurnArc(FlightPhase.Capture, _model.Sun.Id,
                brakeStartJd, arrivalJd, brake, cheap);
            match.StepHintSecondsOverride = Math.Clamp(
                match.DurationSeconds / StationMatchStepSamples, 0.5, 600.0);
            arcs.Add(match);

            // A station is a point target with no surface to clear: its hold
            // point is the arrival's own clearance, so only the departure burn
            // is gated against a body (when there is one).
            if (!FixedBurnsClearBodies(arcs, departureBody, null,
                    out string clearanceReason))
            {
                return StretchedPlan(ship, station, totalSeconds, clearanceReason);
            }

            if (!BurnChainIsClosed(arcs, 1e3, 1.0, out string seamReason))
            {
                return StretchedPlan(ship, station, totalSeconds,
                    "station: " + seamReason,
                    pinStalled: !cheap && !pinSettled);
            }

            // The terminal contract is the hold state itself, not a parking
            // orbit: the brake must reach it inside the chain gate. The brake is
            // built from the state the solve walked back from the hold point, so
            // this closes at the integrator's own reversibility unless a fixed
            // point did not settle; a refusal names the miss.
            double holdPositionError = Vector3d.Distance(
                brake.PositionAt(1.0), holdPosition);
            double holdVelocityError = (brake.VelocityAt(1.0) - holdVelocity).Magnitude;
            if (holdPositionError > BurnOnlyPositionGateM
                || holdVelocityError > BurnOnlyVelocityGateMps)
            {
                return StretchedPlan(ship, station, totalSeconds,
                    $"station: the arrival brake misses the hold point " +
                    $"({holdPositionError:F0} m, {holdVelocityError:F3} m/s)",
                    pinStalled: !cheap && !pinSettled);
            }

            double limit = MaxAccelerationMps2 * ThrustMargin;
            double worst = 1.0;
            foreach (PlanArc arc in arcs)
            {
                if (arc.PeakThrustMps2 <= 0.0) continue;
                worst = Math.Max(worst, arc.PeakThrustMps2 / limit);
            }

            double actualSeconds = (arrivalJd - t0) * SecondsPerDay;
            double requiredSeconds = Math.Max(totalSeconds, actualSeconds)
                                     * Math.Clamp(Math.Sqrt(worst), 0.85, 3.0);
            var arrivalState = new ShipState(arrivalJd, holdPosition, holdVelocity,
                ship.DryMassKg, ship.PropellantKg);
            var plan = new TrajectoryPlan(station.Id, MaxAccelerationMps2, arcs, ship,
                arrivalState);
            plan.FiniteBurnAccelerationMps2 = acceleration;
            if (hasClimb) plan.DepartureVInfinityMps = climb.VInfinityMps;

            // The station profile has no planetocentric approach, so the seed
            // always reports none.
            var settledSeed = new PinSeed(totalSeconds, brakeSeconds, brakeDirection,
                burnSeconds, burnDirection, hasApproach: false);
            plan.PinSeed = settledSeed;

            Trace?.Invoke($"station: hold {StationHoldDistanceM:F0} m astern, " +
                          $"seam {Vector3d.Distance(arcs[arcs.Count - 1].Path.PositionAt(1.0), holdPosition):F2} m / " +
                          $"{(arcs[arcs.Count - 1].Path.VelocityAt(1.0) - holdVelocity).Magnitude:E2} m/s; " +
                          (hasClimb
                              ? $"break {climb.BreakSeconds / SecondsPerDay:F4} d, escape " +
                                $"{climb.EscapeSeconds / SecondsPerDay:F4} d, "
                              : "heliocentric start, ") +
                          $"burn {burnSeconds / SecondsPerDay:F4} d, coast " +
                          $"{coastFinalSeconds / SecondsPerDay:F4} d, brake " +
                          $"{brakeSeconds / SecondsPerDay:F4} d, total " +
                          $"{actualSeconds / SecondsPerDay:F5} d");

            return new BuildResult(plan, requiredSeconds, pinSettled: pinSettled,
                seed: settledSeed, built: true);
        }

        /// <summary>
        /// A station trip's terminal state: the station's own state at the
        /// arrival date, offset <see cref="StationHoldDistanceM"/> astern along
        /// its velocity direction, with the station's velocity. The hold point is
        /// derived from <see cref="SolarSystemModel.ComputeState"/> — never from
        /// the frame loop's finite-difference velocity — so it is exact at any
        /// date the planner asks for.
        /// </summary>
        private void StationHoldState(BodyRuntime station, double julianDate,
            out Vector3d holdPositionM, out Vector3d holdVelocityMps)
        {
            _model.ComputeState(station, julianDate, out holdPositionM,
                out holdVelocityMps);
            if (holdVelocityMps.SqrMagnitude > 1e-12)
            {
                holdPositionM -= holdVelocityMps.Normalized * StationHoldDistanceM;
            }
        }

        /// <summary>The fast peak of an already-built quintic, against its own
        /// reference body alone. The floor probes use this instead of the full
        /// <see cref="Measure"/>, so a probe pays one body's ephemeris per
        /// sample rather than the whole catalogue.</summary>
        private double BreakPeakCentralBodyAlongPath(IArcPath path, double startJd,
            BodyRuntime body, int samples = 0)
        {
            if (samples <= 0) samples = Math.Max(8, ArcSamples);
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

        /// <summary>The integrated delta-v of an already-built quintic against
        /// its own reference body alone: the same two-tier model the break search
        /// uses for its peak. A fast probe reports zero for a quintic's delta-v
        /// (<see cref="MeasureFast"/>), so a split candidate's break is restored
        /// here without paying for the full field.</summary>
        private double BreakDeltaVCentralBodyAlongPath(IArcPath path, double startJd,
            BodyRuntime body, int samples = 0)
        {
            if (samples <= 0) samples = Math.Max(8, ArcSamples);
            double total = 0.0;
            double previousMagnitude = 0.0;
            double previousSeconds = 0.0;
            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                Vector3d position = path.PositionAt(u);
                double seconds = path.SecondsAt(u);
                double julianDate = startJd + seconds / SecondsPerDay;
                double magnitude = (path.AccelerationAt(u)
                                    - CentralBodyGravity(body, position, julianDate)).Magnitude;
                if (i > 0)
                    total += 0.5 * (previousMagnitude + magnitude)
                             * (seconds - previousSeconds);
                previousMagnitude = magnitude;
                previousSeconds = seconds;
            }

            return total;
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
        /// The ship's own orbital basis about the departure body: the radial
        /// direction, its prograde tangent, and the orbit normal. The tangent is
        /// the component of the relative velocity across the radius, rotated into
        /// the plane — not the outbound escape ray. The break aims at the ship's
        /// own prograde direction: a station's orbital direction has no relation
        /// to the transfer, and a burn that points at the target cannot start from
        /// the parking radius, it would push the ship into the planet.
        /// </summary>
        private bool TryDepartureBasis(ShipState ship, BodyRuntime body,
            out Vector3d radial, out Vector3d tangent, out Vector3d normal,
            out double radius, out double angularRateRadPerSecond)
        {
            radial = Vector3d.Zero;
            tangent = Vector3d.Zero;
            normal = Vector3d.Zero;
            radius = 0.0;
            angularRateRadPerSecond = 0.0;

            _model.ComputeState(body, ship.JulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d relativePosition = ship.PositionM - bodyPosition;
            Vector3d relativeVelocity = ship.VelocityMps - bodyVelocity;
            radius = relativePosition.Magnitude;
            if (!(radius > body.RadiusM) || !(radius > 0.0)) return false;

            radial = relativePosition / radius;
            Vector3d angularMomentum = Vector3d.Cross(relativePosition, relativeVelocity);
            if (!(angularMomentum.Magnitude > 1e-9 * radius * relativeVelocity.Magnitude))
                return false;

            normal = angularMomentum.Normalized;
            tangent = Vector3d.Cross(normal, radial).Normalized;
            if (tangent.SqrMagnitude < 0.5) return false;

            angularRateRadPerSecond = Vector3d.Dot(relativeVelocity, tangent) >= 0.0
                ? relativeVelocity.Magnitude / radius
                : -relativeVelocity.Magnitude / radius;
            return true;
        }

        /// <summary>Rotate a vector about a unit axis by an angle, right-handed.</summary>
        private static Vector3d RotateAbout(Vector3d vector, Vector3d axisUnit,
            double angleRad)
        {
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            return vector * cos
                   + Vector3d.Cross(axisUnit, vector) * sin
                   + axisUnit * (Vector3d.Dot(axisUnit, vector) * (1.0 - cos));
        }

        /// <summary>
        /// Where the break ends: the parking position advanced by the phase the
        /// ship sweeps during the burn, with the planetocentric escape speed along
        /// the prograde tangent there. The periapsis speed is
        /// <c>√(2μ/r₀ + v∞²)</c>, so <c>v∞ = 0</c> is the parabolic boundary and
        /// the break's endpoint is the escape conic's periapsis.
        /// </summary>
        private void BreakEndpoint(ShipState ship, BodyRuntime body, Vector3d radial,
            Vector3d tangent, Vector3d normal, double radius, double angularRateRadPerSecond,
            double periapsisSpeedMps, double seconds,
            out Vector3d endLocalPositionM, out Vector3d endLocalVelocityMps,
            out Vector3d endPositionM, out Vector3d endVelocityMps,
            out double endJulianDate)
        {
            double angle = angularRateRadPerSecond * seconds;
            Vector3d rotatedRadial = RotateAbout(radial, normal, angle);
            Vector3d rotatedTangent = RotateAbout(tangent, normal, angle);
            endLocalPositionM = rotatedRadial * radius;
            endLocalVelocityMps = rotatedTangent * periapsisSpeedMps;

            endJulianDate = ship.JulianDate + seconds / SecondsPerDay;
            _model.ComputeState(body, endJulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);
            endPositionM = bodyPosition + endLocalPositionM;
            endVelocityMps = bodyVelocity + endLocalVelocityMps;
        }

        /// <summary>The quintic break's peak commanded thrust for one duration,
        /// with the endpoint on the ship's own prograde escape state. The endpoint
        /// accelerations are full-field, exactly as <see cref="MakeArc"/> builds
        /// the final break; a probe measures only the departure body's gravity
        /// along the arc.</summary>
        private double BreakPeakToEndpoint(ShipState ship, BodyRuntime body,
            Vector3d radial, Vector3d tangent, Vector3d normal, double radius,
            double angularRateRadPerSecond, double periapsisSpeedMps, double seconds,
            bool cheap, bool rankOnly = false)
        {
            BreakEndpoint(ship, body, radial, tangent, normal, radius,
                angularRateRadPerSecond, periapsisSpeedMps, seconds,
                out _, out _, out Vector3d endPosition, out Vector3d endVelocity,
                out double endJulianDate);

            Vector3d startGravity;
            Vector3d endGravity;
            if (rankOnly)
            {
                // The rank-only cost compares excess speeds, not plans: the
                // departure body's own gravity at both endpoints is enough to
                // place the quintic, and it skips the catalogue sweep the plan's
                // break pays for. The reported break still uses the full field.
                startGravity = CentralBodyGravity(body, ship.PositionM, ship.JulianDate);
                endGravity = CentralBodyGravity(body, endPosition, endJulianDate);
            }
            else
            {
                _gravity.Prepare(ship.PositionM, ship.JulianDate);
                startGravity = _gravity.AccelerationAt(ship.PositionM, ship.JulianDate);
                _gravity.Prepare(endPosition, endJulianDate);
                endGravity = _gravity.AccelerationAt(endPosition, endJulianDate);
            }

            Quintic path = Quintic.Create(ship.PositionM, ship.VelocityMps, startGravity,
                endPosition, endVelocity, endGravity, seconds);

            if (cheap)
                return BreakPeakCentralBodyAlongPath(path, ship.JulianDate, body,
                    rankOnly ? DepartureSplitBreakSamples : 0);

            double fast = BreakPeakCentralBodyAlongPath(path, ship.JulianDate, body);
            double limit = MaxAccelerationMps2 * ThrustMargin;
            return fast <= limit * (1.0 - BreakScreenMargin)
                ? fast
                : BreakPeakFullFieldAlongPath(path, ship.JulianDate);
        }

        /// <summary>
        /// The break's duration: the quintic whose peak commanded thrust is the
        /// commanded acceleration <see cref="FixedBurnAccelerationMps2"/>, solved
        /// against the ship's own prograde escape endpoint. The seed is the speed
        /// change over the acceleration, and the peak falls as the duration grows,
        /// so the solve grows or shrinks from the seed and bisects. The trip enters
        /// only through <paramref name="capSeconds"/>: a trip too short to fly the
        /// break at the commanded level comes back at the cap and is reported
        /// over-thrust rather than stretched.
        /// </summary>
        private double SolveBreakSecondsToEscapeState(ShipState ship, BodyRuntime body,
            Vector3d radial, Vector3d tangent, Vector3d normal, double radius,
            double angularRateRadPerSecond, double periapsisSpeedMps,
            double seedSeconds, double capSeconds, bool cheap, bool rankOnly = false)
        {
            double limit = FixedBurnAccelerationMps2;
            double cap = Math.Max(MinimumArcSeconds, capSeconds);
            double Peak(double seconds) => BreakPeakToEndpoint(ship, body, radial, tangent,
                normal, radius, angularRateRadPerSecond, periapsisSpeedMps, seconds, cheap,
                rankOnly);

            double seed = Math.Clamp(seedSeconds, MinimumArcSeconds, cap);
            double low;
            double high;

            if (Peak(seed) > limit)
            {
                // Too hard: grow until the peak crosses below the target.
                low = seed;
                high = seed;
                bool crossed = false;
                for (int step = 0; step < 64 && high < cap; step++)
                {
                    double next = Math.Min(high * 1.25, cap);
                    if (Peak(next) <= limit)
                    {
                        high = next;
                        crossed = true;
                        break;
                    }

                    low = next;
                    high = next;
                }

                if (!crossed) return cap;
            }
            else
            {
                // Too gentle: shrink until the peak crosses above the target.
                high = seed;
                low = seed;
                bool crossed = false;
                for (int step = 0; step < 96 && low > MinimumArcSeconds; step++)
                {
                    double next = Math.Max(low * 0.8, MinimumArcSeconds);
                    if (Peak(next) > limit)
                    {
                        low = next;
                        crossed = true;
                        break;
                    }

                    high = next;
                    low = next;
                }

                if (!crossed) return MinimumArcSeconds;
            }

            double tolerance = cheap ? 1e-3 * high : 1e-6 * high;
            for (int iteration = 0; iteration < 64 && high - low > tolerance; iteration++)
            {
                double middle = 0.5 * (low + high);
                if (Peak(middle) <= limit) high = middle;
                else low = middle;
            }

            return Math.Clamp(high, MinimumArcSeconds, cap);
        }

        /// <summary>
        /// The ballistic planetocentric climb from the parking radius to the
        /// departure ceiling. The conic starts at the break's endpoint — the
        /// parking radius on the ship's prograde tangent at the planetocentric
        /// escape speed — so its periapsis is that state. The ceiling crossing is
        /// solved from the conic's own elements, not from a time share: the true
        /// anomaly is where <c>p / (1 + e cos ν) = r_ceiling</c> on the outward
        /// branch, and the time is the conic's own time from periapsis.
        /// </summary>
        private bool TryEscapeCeilingState(BodyRuntime body, Vector3d radial,
            Vector3d tangent, double parkingRadiusM, double periapsisSpeedMps,
            double ceilingRadiusM, out ConicElements elements, out double anomalyRad,
            out double seconds, out Vector3d localPositionM, out Vector3d localVelocityMps)
        {
            elements = null;
            anomalyRad = 0.0;
            seconds = 0.0;
            localPositionM = Vector3d.Zero;
            localVelocityMps = Vector3d.Zero;

            if (!(ceilingRadiusM > parkingRadiusM)) return false;

            try
            {
                elements = ConicElements.FromState(radial * parkingRadiusM,
                    tangent * periapsisSpeedMps, body.Gm);
            }
            catch (Exception)
            {
                return false;
            }

            // The endpoint is at escape speed or above, so the conic is parabolic
            // or hyperbolic and reaches any radius outside its periapsis. An
            // elliptic conic cannot climb to the ceiling.
            if (elements.Eccentricity < 1.0 - 1e-7) return false;

            double cos = (elements.SemiLatusRectumM / ceilingRadiusM - 1.0)
                         / elements.Eccentricity;
            if (!(cos > -1.0 && cos < 1.0)) return false;

            anomalyRad = Math.Acos(cos);
            seconds = elements.TimeSincePeriapsis(anomalyRad);
            if (!(seconds > 0.0) || !double.IsFinite(seconds)) return false;

            elements.StateAtTrueAnomaly(anomalyRad, out localPositionM, out localVelocityMps);
            return true;
        }

        /// <summary>
        /// The hyperbolic excess speed whose escape conic reaches the ceiling in
        /// <paramref name="targetSeconds"/>. The flight time falls monotonically as
        /// the excess speed rises, so the solve is a bisection on v∞; the
        /// minimum-energy parabolic climb is the v∞ = 0 end.
        /// </summary>
        private bool TrySolveVInfinityForEscapeSeconds(BodyRuntime body, Vector3d radial,
            Vector3d tangent, double parkingRadiusM, double ceilingRadiusM,
            double targetSeconds, out double vInfinityMps, out double periapsisSpeedMps,
            out double escapeSeconds, double forcedVInfinityMps = -1.0)
        {
            vInfinityMps = 0.0;
            periapsisSpeedMps = 0.0;
            escapeSeconds = 0.0;

            if (!(targetSeconds > 0.0)) return false;

            double escapeSpeed = Math.Sqrt(2.0 * body.Gm / parkingRadiusM);
            double Time(double vInfinity)
            {
                double speed = Math.Sqrt(escapeSpeed * escapeSpeed
                                         + vInfinity * vInfinity);
                return TryEscapeCeilingState(body, radial, tangent, parkingRadiusM, speed,
                    ceilingRadiusM, out _, out _, out double seconds, out _, out _)
                    ? seconds
                    : double.PositiveInfinity;
            }

            // The test seam takes precedence over the search's chosen split: it is
            // a forced measurement, not a plan input.
            double forced = DepartureVInfinityOverrideMps >= 0.0
                ? DepartureVInfinityOverrideMps
                : forcedVInfinityMps;
            if (forced >= 0.0)
            {
                // One forced v∞, no solve. Its climb time is whatever the conic
                // takes; the caller reports a trip that cannot fit it rather than
                // bending the excess to the requested time.
                vInfinityMps = forced;
                periapsisSpeedMps = Math.Sqrt(escapeSpeed * escapeSpeed
                                              + vInfinityMps * vInfinityMps);
                escapeSeconds = Time(vInfinityMps);
                return escapeSeconds > 0.0 && double.IsFinite(escapeSeconds);
            }

            double low = 0.0;
            double lowSeconds = Time(0.0);
            if (!(lowSeconds > targetSeconds)) return false;

            double high = 1.0;
            for (int step = 0; step < 80; step++)
            {
                double seconds = Time(high);
                if (double.IsFinite(seconds) && seconds <= targetSeconds) break;
                high *= 2.0;
                if (!(high < 1e9)) return false;
            }

            if (!(Time(high) <= targetSeconds)) return false;

            for (int iteration = 0; iteration < 80; iteration++)
            {
                double middle = 0.5 * (low + high);
                double seconds = Time(middle);
                if (seconds <= targetSeconds) high = middle;
                else low = middle;
            }

            vInfinityMps = high;
            periapsisSpeedMps = Math.Sqrt(escapeSpeed * escapeSpeed
                                          + vInfinityMps * vInfinityMps);
            escapeSeconds = Time(vInfinityMps);
            return true;
        }

        /// <summary>
        /// Diagnostic only: the **full** step-2 candidate cost, as the split
        /// search costed a candidate before it was made cheap — a fast probe
        /// build with its own pin loop, plus the break's integrated delta-v.
        /// The planner never calls this; the split test does, to prove the cheap
        /// rank-only chooser reaches the same answer. A cheap candidate's cost is
        /// the rank-only Lambert estimate in <see cref="DepartureSplitCost"/>.
        /// </summary>
        internal (double CostMps, double Milliseconds) MeasureFullSplitCandidateCost(
            ShipState ship, string targetId, double totalSeconds, double vInfinityMps,
            int passes = 12)
        {
            BodyRuntime target = RequireTarget(targetId);
            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BuildResult candidate = BuildBurnCoastBurn(ship, target, totalSeconds,
                fast: true, probe: false, outerPassesLimit: passes,
                forcedVInfinityMps: vInfinityMps);
            double cost = double.PositiveInfinity;
            if (string.IsNullOrEmpty(candidate.Plan.InfeasibleReason))
            {
                cost = candidate.Plan.DeltaVMps;
                foreach (PlanArc arc in candidate.Plan.Arcs)
                {
                    if (arc.Phase != FlightPhase.Departure || arc.IsFiniteBurn) continue;
                    cost += BreakDeltaVCentralBodyAlongPath(arc.Path,
                        arc.StartJulianDate, departureBody);
                }
            }

            return (cost, clock.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// Diagnostic only: the **full** candidate cost for a station target —
        /// a fast station build with its own pin loop at one forced excess
        /// speed, plus the break's integrated delta-v. It is the station twin of
        /// <see cref="MeasureFullSplitCandidateCost"/>; the planner never calls
        /// it and the station split test does. A station plan ends on the hold
        /// state and has no capture chain, so the build's own total is the whole
        /// terminal cost, and its velocity match may add speed rather than only
        /// shed it.
        /// </summary>
        internal (double CostMps, double Milliseconds) MeasureFullStationSplitCandidateCost(
            ShipState ship, string targetId, double totalSeconds, double vInfinityMps,
            int passes = 12)
        {
            BodyRuntime target = RequireTarget(targetId);
            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BuildResult candidate = BuildStationTrip(ship, target, totalSeconds,
                fast: true, outerPassesLimit: passes,
                forcedVInfinityMps: vInfinityMps);
            double cost = double.PositiveInfinity;
            if (string.IsNullOrEmpty(candidate.Plan.InfeasibleReason))
            {
                cost = candidate.Plan.DeltaVMps;
                foreach (PlanArc arc in candidate.Plan.Arcs)
                {
                    if (arc.Phase != FlightPhase.Departure || arc.IsFiniteBurn) continue;
                    cost += BreakDeltaVCentralBodyAlongPath(arc.Path,
                        arc.StartJulianDate, departureBody);
                }
            }

            return (cost, clock.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// Diagnostic only: one rank-only candidate cost, as the shipping chooser
        /// measures it. The split test grids it and proves its winner matches the
        /// full <see cref="MeasureFullSplitCandidateCost"/> search. Returns
        /// <see cref="double.PositiveInfinity"/> for a candidate that does not fit.
        /// </summary>
        internal double MeasureCheapSplitCandidate(ShipState ship, string targetId,
            double totalSeconds, double vInfinityMps)
        {
            BodyRuntime target = RequireTarget(targetId);
            if (target.Data.IsStation) return double.PositiveInfinity;
            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            if (departureBody == null) return double.PositiveInfinity;
            HandoverBand arrivalBand = HandoverBand.For(target, FixedBurnAccelerationMps2);
            HandoverBand departureBand = HandoverBand.For(departureBody,
                FixedBurnAccelerationMps2);
            if (!arrivalBand.IsValid || !departureBand.IsValid)
                return double.PositiveInfinity;
            double arrivalRadius = ArrivalEntryRadiusM(arrivalBand, target);
            double arrivalJd = ship.JulianDate + totalSeconds / SecondsPerDay;
            TransferConic chain = BuildCaptureShape(ship, target, arrivalRadius, arrivalJd);
            if (!chain.IsValid) return double.PositiveInfinity;
            DepartureSplitCost cost = DepartureSplitCost.ForCaptureChain(this, ship,
                target, departureBody, departureBand, chain, totalSeconds, arrivalJd);
            return cost.Cost(vInfinityMps);
        }

        /// <summary>
        /// Diagnostic only: one rank-only candidate cost for a **station**
        /// target, exactly as the shipping chooser measures it. The entry state
        /// is the hold state at the arrival date — the station's own state, 500 m
        /// astern along its velocity — so there is no capture chain to build and
        /// no chain duration to take off the trip. Everything else is the planet
        /// cost: the same climb solve, the same 600 s burn and brake seeds, and
        /// the same three terms, with the brake a magnitude because a rendezvous
        /// may add speed. Returns <see cref="double.PositiveInfinity"/> when the
        /// ship has no departure body — a heliocentric start has no climb, which
        /// is the no-climb control — or when a candidate does not fit.
        /// </summary>
        internal double MeasureStationSplitCandidate(ShipState ship, string targetId,
            double totalSeconds, double vInfinityMps)
        {
            BodyRuntime target = RequireTarget(targetId);
            if (!target.Data.IsStation) return double.PositiveInfinity;
            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            if (departureBody == null) return double.PositiveInfinity;
            HandoverBand departureBand = HandoverBand.For(departureBody,
                FixedBurnAccelerationMps2);
            if (!departureBand.IsValid) return double.PositiveInfinity;
            double arrivalJd = ship.JulianDate + totalSeconds / SecondsPerDay;
            DepartureSplitCost cost = DepartureSplitCost.AtStationHold(this, ship,
                target, departureBody, departureBand, totalSeconds, arrivalJd);
            return cost.Cost(vInfinityMps);
        }

        /// <summary>The coarse grid the departure split search starts from, in
        /// m/s. The measured curve is <b>not</b> unimodal — 10 km/s wins at 18
        /// days, 3 at 30 and 1 at 90 — so a single bisection could sit in the
        /// wrong lobe. 16 km/s is the cap.</summary>
        private static readonly double[] DepartureSplitGridMps =
        {
            0.0, 1_000.0, 3_000.0, 6_000.0, 10_000.0, 16_000.0
        };

        /// <summary>The nominal burn and brake duration a rank-only candidate
        /// assumes. It is held constant across the grid, so the candidates are
        /// compared at the same coast duration and only the climb's cost and the
        /// Lambert velocity change move. Ten minutes is a tenth of the fastest
        /// trip's burn and a fraction of the slow end's; the point is that it is
        /// the same number for every candidate. The first refinement round is
        /// measured at the winner's own cost, not at this seed.</summary>
        private const double DepartureSplitSeedBurnSeconds = 600.0;

        /// <summary>Refinement rounds between the best grid point and its
        /// neighbours. One round probes two midpoints: the rank-only cost is
        /// monotone enough near the best that a second round buys less than its
        /// share of the 50 ms budget.</summary>
        private const int DepartureSplitRefinements = 1;

        /// <summary>The rank-only candidate cost. It is built once per search
        /// from the geometry every candidate shares — the terminal's entry state,
        /// the departure body and the bands — and then costs each excess speed
        /// with one climb solve and one Lambert solve.
        ///
        /// The entry state comes from the target type. A planet's is the capture
        /// chain's own entry state, and the chain's duration is time the climb
        /// and coast do not get. A station has no chain: its entry is the hold
        /// state at the arrival date, so nothing is taken off the trip.
        ///
        /// The cost keeps the profile's three terms separate, which is what the
        /// reference skeleton cannot do:
        /// <list type="bullet">
        /// <item>the **break**'s integrated delta-v against the departure body,
        /// measured on the quintic the climb solve builds at the rank-only
        /// sample count;</item>
        /// <item>the **departure burn**'s change from the escape conic's ceiling
        /// state to the Lambert coast's departure velocity;</item>
        /// <item>the **brake**'s change from the coast's arrival velocity to the
        /// entry velocity. It is a magnitude, not a retrograde brake: a station
        /// rendezvous may add speed as well as shed it.</item>
        /// </list>
        /// The terminal's own cost is the same for every candidate (the arrival
        /// date is fixed), so it is left out. This is a cost model, not a plan:
        /// no arcs are measured, the fixed burns are velocity changes at their
        /// endpoints rather than integrated arcs, and the seed's burn and brake
        /// duration is common to the grid.
        /// </summary>
        private sealed class DepartureSplitCost
        {
            private readonly TripPlanner _planner;
            private readonly ShipState _ship;
            private readonly BodyRuntime _target;
            private readonly BodyRuntime _departureBody;
            private readonly HandoverBand _departureBand;
            private readonly double _chainSeconds;
            private readonly double _totalSeconds;
            private readonly double _entryJulianDate;
            private readonly Vector3d _entryPositionM;
            private readonly Vector3d _entryVelocityMps;

            private DepartureSplitCost(TripPlanner planner, ShipState ship,
                BodyRuntime target, BodyRuntime departureBody,
                HandoverBand departureBand, double chainSeconds,
                double totalSeconds, double entryJulianDate,
                Vector3d entryPositionM, Vector3d entryVelocityMps)
            {
                _planner = planner;
                _ship = ship;
                _target = target;
                _departureBody = departureBody;
                _departureBand = departureBand;
                _chainSeconds = chainSeconds;
                _totalSeconds = totalSeconds;
                _entryJulianDate = entryJulianDate;
                _entryPositionM = entryPositionM;
                _entryVelocityMps = entryVelocityMps;
            }

            /// <summary>A planet's cost: the entry is the capture chain's own
            /// entry state, so the chain's duration is subtracted from the time
            /// the climb and coast share.</summary>
            public static DepartureSplitCost ForCaptureChain(TripPlanner planner,
                ShipState ship, BodyRuntime target, BodyRuntime departureBody,
                HandoverBand departureBand, TransferConic chain,
                double totalSeconds, double arrivalJulianDate)
            {
                double entryJulianDate = arrivalJulianDate
                                         - chain.TotalSeconds / SecondsPerDay;
                planner._model.ComputeState(target, entryJulianDate,
                    out Vector3d targetPosition, out Vector3d targetVelocity);
                return new DepartureSplitCost(planner, ship, target, departureBody,
                    departureBand, chain.TotalSeconds, totalSeconds,
                    entryJulianDate,
                    targetPosition + chain.EntryLocalPositionM,
                    targetVelocity + chain.EntryLocalVelocityMps);
            }

            /// <summary>A station's cost: the entry is the hold state at the
            /// arrival date, so there is no chain and no chain duration to
            /// subtract.</summary>
            public static DepartureSplitCost AtStationHold(TripPlanner planner,
                ShipState ship, BodyRuntime station, BodyRuntime departureBody,
                HandoverBand departureBand, double totalSeconds,
                double arrivalJulianDate)
            {
                planner.StationHoldState(station, arrivalJulianDate,
                    out Vector3d holdPositionM, out Vector3d holdVelocityMps);
                return new DepartureSplitCost(planner, ship, station, departureBody,
                    departureBand, 0.0, totalSeconds, arrivalJulianDate,
                    holdPositionM, holdVelocityMps);
            }

            public double Cost(double vInfinityMps)
            {
                double available = _totalSeconds - _chainSeconds
                                   - DepartureSplitSeedBurnSeconds
                                   - DepartureSplitSeedBurnSeconds;
                if (!(available > MinimumArcSeconds))
                    return double.PositiveInfinity;

                if (!_planner.TryBuildDepartureClimb(_ship, _departureBody,
                        _departureBand.DepartureRadiusM, available,
                        ClimbSeedSeconds(available), cheap: true,
                        out DepartureClimb climb, out _, vInfinityMps,
                        rankOnly: true))
                    return double.PositiveInfinity;

                _planner.ClimbBurnStart(_departureBody, climb,
                    out Vector3d exitPosition, out Vector3d exitVelocity);
                double brakeStartJd = _entryJulianDate
                                      - DepartureSplitSeedBurnSeconds / SecondsPerDay;
                double coastSeconds = (brakeStartJd - climb.BurnStartJulianDate)
                                      * SecondsPerDay;
                if (!(coastSeconds > MinimumArcSeconds))
                    return double.PositiveInfinity;

                LambertSolution coast = LambertSolver.SolveShortWay(
                    exitPosition, _entryPositionM, coastSeconds,
                    _planner._model.Sun.Gm);
                if (!coast.HasSolution) return double.PositiveInfinity;

                double departureCost = (coast.DepartureVelocityMps - exitVelocity)
                    .Magnitude;
                double brakeCost = (_entryVelocityMps - coast.ArrivalVelocityMps)
                    .Magnitude;
                return departureCost + brakeCost + BreakCost(climb);
            }

            /// <summary>The break's integrated thrust against the departure
            /// body alone, on the same quintic the plan builds: the ship's state
            /// to the conic's periapsis on the prograde tangent. The rank-only
            /// model uses the body's own gravity at both quintic endpoints and a
            /// coarse sample count; the reported plan's break is built and
            /// measured by <see cref="AppendDepartureClimb"/> at the full
            /// quality, so this number never reaches a plan.</summary>
            private double BreakCost(DepartureClimb climb)
            {
                _planner._model.ComputeState(_departureBody, climb.BreakEndJulianDate,
                    out Vector3d bodyAtEnd, out Vector3d bodyVelocityAtEnd);
                Vector3d endPosition = bodyAtEnd + climb.BreakEndLocalPositionM;
                Vector3d endVelocity = bodyVelocityAtEnd + climb.BreakEndLocalVelocityMps;
                Vector3d startGravity = _planner.CentralBodyGravity(_departureBody,
                    _ship.PositionM, _ship.JulianDate);
                Vector3d endGravity = _planner.CentralBodyGravity(_departureBody,
                    endPosition, climb.BreakEndJulianDate);
                Quintic path = Quintic.Create(_ship.PositionM, _ship.VelocityMps,
                    startGravity, endPosition, endVelocity, endGravity,
                    climb.BreakSeconds);
                return _planner.BreakDeltaVCentralBodyAlongPath(path, _ship.JulianDate,
                    _departureBody, DepartureSplitBreakSamples);
            }
        }

        /// <summary>Samples the rank-only break cost integrates over. The break
        /// is a smooth quintic of minutes against one body, so a sixteenth of the
        /// plan's grid still resolves its cost to a fraction of a km/s.</summary>
        private const int DepartureSplitBreakSamples = 16;

        /// <summary>
        /// Choose the departure split: the escape conic's hyperbolic excess speed
        /// that minimises the plan's total delta-v at
        /// <paramref name="totalSeconds"/>. The cost of a candidate is a **fast
        /// probe build's** total delta-v plus the break's own integrated delta-v —
        /// the departure, the brake, the capture and the break — so the search is
        /// a cheap proxy for the certified build, never a plan. A candidate whose
        /// climb cannot fit the requested time is skipped.
        ///
        /// The search is a grid plus a local bisection, not a golden-section
        /// search: the measured curve has several local minima, and the global
        /// best at the requested duration is what the plan needs. The same value
        /// is then held for the whole build; re-choosing it as the pin moves
        /// would stop the fixed point converging.
        ///
        /// Returns `-1` when the split does not apply: the flag is off, the ship
        /// starts outside every sphere, the target is a heliocentric station with
        /// no departure body, or no candidate fits. `-1` is the climb solver's
        /// "no forced excess", so the build is then exactly the profile it was.
        ///
        /// A **climbing station** is costed like a planet, entering at the hold
        /// state at the arrival date. A station has no capture chain and no
        /// handover band of its own, so there is no arrival band to require and
        /// no chain duration to take off the trip.
        /// </summary>
        private double ChooseDepartureVInfinityMps(ShipState ship, BodyRuntime target,
            double totalSeconds)
        {
            if (!UseDepartureSplit) return -1.0;

            // The test seam is a forced answer, not a plan input: when it is set
            // there is nothing to choose, and the climb solver takes the seam.
            if (DepartureVInfinityOverrideMps >= 0.0) return -1.0;

            if (!(totalSeconds > 0.0)) return -1.0;

            BodyRuntime departureBody = FindDepartureBody(ship, target, ship.JulianDate);
            if (departureBody == null) return -1.0;

            HandoverBand departureBand = HandoverBand.For(departureBody,
                FixedBurnAccelerationMps2);
            if (!departureBand.IsValid) return -1.0;

            var clock = System.Diagnostics.Stopwatch.StartNew();

            double arrivalJd = ship.JulianDate + totalSeconds / SecondsPerDay;

            // The geometry every candidate shares: the terminal's entry state,
            // the departure band and the departure body. Only the climb and the
            // Lambert coast move with the excess speed.
            DepartureSplitCost cost;
            if (target.Data.IsStation)
            {
                // A station is a point target: its entry is the hold state at the
                // arrival date, so there is no chain and no arrival band.
                cost = DepartureSplitCost.AtStationHold(this, ship, target,
                    departureBody, departureBand, totalSeconds, arrivalJd);
            }
            else
            {
                HandoverBand arrivalBand = HandoverBand.For(target,
                    FixedBurnAccelerationMps2);
                if (!arrivalBand.IsValid) return -1.0;

                double arrivalRadius = ArrivalEntryRadiusM(arrivalBand, target);
                TransferConic chain = BuildCaptureShape(ship, target, arrivalRadius,
                    arrivalJd);
                if (!chain.IsValid) return -1.0;

                cost = DepartureSplitCost.ForCaptureChain(this, ship, target,
                    departureBody, departureBand, chain, totalSeconds, arrivalJd);
            }

            double Cost(double vInfinity)
            {
                double value = cost.Cost(vInfinity);
                Trace?.Invoke($"departure split at {totalSeconds / SecondsPerDay:F4} d: " +
                              $"v∞ {vInfinity / 1000.0:F3} km/s -> " +
                              $"{(double.IsFinite(value) ? $"{value / 1000.0:F3} km/s" : "no fit")}");
                return value;
            }

            double bestVInfinity = -1.0;
            double bestCost = double.PositiveInfinity;
            int bestIndex = -1;
            for (int index = 0; index < DepartureSplitGridMps.Length; index++)
            {
                double value = Cost(DepartureSplitGridMps[index]);
                if (!(value < bestCost)) continue;
                bestCost = value;
                bestVInfinity = DepartureSplitGridMps[index];
                bestIndex = index;
            }

            if (bestIndex < 0)
            {
                LastDepartureSplitSeconds = clock.Elapsed.TotalSeconds;
                Trace?.Invoke($"departure split at {totalSeconds / SecondsPerDay:F4} d: " +
                              "no candidate fits, leaving the climb unforced");
                return -1.0;
            }

            double low = bestIndex > 0 ? DepartureSplitGridMps[bestIndex - 1] : 0.0;
            double high = bestIndex < DepartureSplitGridMps.Length - 1
                ? DepartureSplitGridMps[bestIndex + 1]
                : DepartureSplitGridMps[DepartureSplitGridMps.Length - 1];

            for (int round = 0; round < DepartureSplitRefinements; round++)
            {
                double lower = 0.5 * (low + bestVInfinity);
                double upper = 0.5 * (bestVInfinity + high);
                double lowerCost = Cost(lower);
                double upperCost = Cost(upper);
                if (lowerCost < bestCost && lowerCost <= upperCost)
                {
                    high = bestVInfinity;
                    bestVInfinity = lower;
                    bestCost = lowerCost;
                }
                else if (upperCost < bestCost)
                {
                    low = bestVInfinity;
                    bestVInfinity = upper;
                    bestCost = upperCost;
                }
                else
                {
                    // The bracket's own point stays the best: narrow onto it.
                    low = lower;
                    high = upper;
                }
            }

            Trace?.Invoke($"departure split chose v∞ " +
                          $"{bestVInfinity / 1000.0:F3} km/s at " +
                          $"{bestCost / 1000.0:F3} km/s for " +
                          $"{totalSeconds / SecondsPerDay:F4} d");
            LastDepartureSplitSeconds = clock.Elapsed.TotalSeconds;
            return bestVInfinity;
        }

        /// <summary>
        /// Build the departure: the short break and the ballistic climb, in the
        /// time the trip leaves for them. A trip long enough for the minimum-energy
        /// parabolic climb gets exactly that; a shorter one gets a hyperbolic
        /// excess speed so the conic still reaches the ceiling in the available
        /// time, and the coast absorbs whatever is left. A non-negative
        /// <paramref name="forcedVInfinityMps"/> takes that excess instead of
        /// solving it, which is how the split search holds its choice across the
        /// pin loop and the settle pass. If the break and even the fastest climb
        /// cannot fit the available time, the trip is infeasible and the numbers
        /// are reported rather than a plan invented.
        /// </summary>
        private bool TryBuildDepartureClimb(ShipState ship, BodyRuntime body,
            double ceilingRadiusM, double availableSeconds, double seedBreakSeconds,
            bool cheap, out DepartureClimb climb, out string reason,
            double forcedVInfinityMps = -1.0, bool rankOnly = false)
        {
            climb = default;
            reason = null;

            if (!TryDepartureBasis(ship, body, out Vector3d radial, out Vector3d tangent,
                    out Vector3d normal, out double radius, out double angularRate))
            {
                reason = "the ship has no prograde direction about the departure body " +
                         "to break out along";
                return false;
            }

            if (!(radius < ceilingRadiusM))
            {
                reason = $"the ship starts at {radius / 1000.0:F0} km from " +
                         $"{body.Name}'s centre, at or outside the " +
                         $"{ceilingRadiusM / 1000.0:F0} km departure ceiling";
                return false;
            }

            double escapeSpeed = Math.Sqrt(2.0 * body.Gm / radius);
            double speedChange = Math.Abs(escapeSpeed
                                          - Vector3d.Dot(ship.VelocityMps
                                                         - BodyVelocity(body, ship.JulianDate),
                                              tangent));
            double seed = Math.Max(seedBreakSeconds, speedChange / FixedBurnAccelerationMps2);
            double cap = Math.Max(MinimumArcSeconds, availableSeconds);

            double periapsisSpeed = escapeSpeed;
            double breakSeconds = SolveBreakSecondsToEscapeState(ship, body, radial,
                tangent, normal, radius, angularRate, periapsisSpeed, seed, cap, cheap,
                rankOnly);

            if (!TryEscapeCeilingState(body, radial, tangent, radius, periapsisSpeed,
                    ceilingRadiusM, out ConicElements parabolicElements,
                    out double parabolicAnomaly, out double parabolicSeconds,
                    out Vector3d parabolicPosition, out Vector3d parabolicVelocity))
            {
                reason = $"the escape conic cannot reach {body.Name}'s " +
                         $"{ceilingRadiusM / 1000.0:F0} km ceiling from the " +
                         $"{radius / 1000.0:F0} km parking radius";
                return false;
            }

            double vInfinity = 0.0;
            double escapeSeconds = parabolicSeconds;
            double anomaly = parabolicAnomaly;
            Vector3d escapePosition = parabolicPosition;
            Vector3d escapeVelocity = parabolicVelocity;
            ConicElements elements = parabolicElements;
            bool parabolic = true;

            // The test seam wins over the search's chosen split. A chosen value
            // turns the climb hyperbolic even when the parabolic one would fit:
            // the split is a decision about cost, not only about time.
            double forced = DepartureVInfinityOverrideMps >= 0.0
                ? DepartureVInfinityOverrideMps
                : forcedVInfinityMps;

            if (forced >= 0.0)
            {
                parabolic = false;
                vInfinity = forced;
                periapsisSpeed = Math.Sqrt(escapeSpeed * escapeSpeed
                                           + vInfinity * vInfinity);
                breakSeconds = SolveBreakSecondsToEscapeState(ship, body, radial,
                    tangent, normal, radius, angularRate, periapsisSpeed, seed, cap,
                    cheap, rankOnly);

                if (!TryEscapeCeilingState(body, radial, tangent, radius, periapsisSpeed,
                        ceilingRadiusM, out elements, out anomaly, out escapeSeconds,
                        out escapePosition, out escapeVelocity))
                {
                    reason = "the hyperbolic escape conic has no ceiling crossing";
                    return false;
                }

                if (!(breakSeconds + escapeSeconds <= availableSeconds + 1e-6))
                {
                    reason = $"the chosen {vInfinity / 1000.0:F3} km/s escape climb " +
                             $"needs {(breakSeconds + escapeSeconds) / SecondsPerDay:F5} d " +
                             $"at the {FixedBurnAccelerationMps2:F3} m/s² level, and the " +
                             $"trip leaves {availableSeconds / SecondsPerDay:F5} d";
                    return false;
                }
            }
            else if (breakSeconds + escapeSeconds > availableSeconds + 1e-6)
            {
                // The trip does not leave the minimum-energy climb its own time:
                // solve the hyperbolic excess that fits the escape into what is
                // left after the break, then correct the break for the speed it
                // now has to supply. The map is strongly contractive (the break is
                // minutes against a climb of days), so a handful of passes settles
                // it without a nested bisection for every evaluation.
                parabolic = false;
                for (int pass = 0; pass < 6; pass++)
                {
                    double target = availableSeconds - breakSeconds;
                    if (!(target > MinimumArcSeconds))
                    {
                        reason = availableSeconds > MinimumArcSeconds
                            ? $"the trip leaves {availableSeconds / SecondsPerDay:F5} d " +
                              $"for the break and the escape climb, and the break alone " +
                              $"needs {breakSeconds / SecondsPerDay:F5} d at the " +
                              $"{FixedBurnAccelerationMps2:F3} m/s² level"
                            : $"the trip time leaves no room for the break and the " +
                              $"escape climb at the {FixedBurnAccelerationMps2:F3} m/s² level";
                        return false;
                    }

                    if (!TrySolveVInfinityForEscapeSeconds(body, radial, tangent, radius,
                            ceilingRadiusM, target, out vInfinity, out periapsisSpeed,
                            out escapeSeconds, forcedVInfinityMps))
                    {
                        reason = $"the trip leaves {target / SecondsPerDay:F5} d for the " +
                                 $"escape climb from {body.Name}, which needs a hyperbolic " +
                                 "excess speed beyond the break's range";
                        return false;
                    }

                    breakSeconds = SolveBreakSecondsToEscapeState(ship, body, radial,
                        tangent, normal, radius, angularRate, periapsisSpeed, seed, cap,
                        cheap, rankOnly);

                    if (breakSeconds + escapeSeconds <= availableSeconds + 1e-6) break;
                }

                if (!(breakSeconds + escapeSeconds <= availableSeconds + 1e-6))
                {
                    reason = $"the break and the fastest escape climb need " +
                             $"{(breakSeconds + escapeSeconds) / SecondsPerDay:F5} d at " +
                             $"the {FixedBurnAccelerationMps2:F3} m/s² level, and the trip " +
                             $"leaves {availableSeconds / SecondsPerDay:F5} d";
                    return false;
                }

                // The final escape state belongs to the v∞ the last pass chose.
                if (!TrySolveVInfinityForEscapeSeconds(body, radial, tangent, radius,
                        ceilingRadiusM, availableSeconds - breakSeconds,
                        out vInfinity, out periapsisSpeed, out escapeSeconds,
                        forcedVInfinityMps))
                {
                    reason = $"the escape climb cannot be placed in " +
                             $"{(availableSeconds - breakSeconds) / SecondsPerDay:F5} d";
                    return false;
                }

                if (!TryEscapeCeilingState(body, radial, tangent, radius, periapsisSpeed,
                        ceilingRadiusM, out elements, out anomaly, out escapeSeconds,
                        out escapePosition, out escapeVelocity))
                {
                    reason = "the hyperbolic escape conic has no ceiling crossing";
                    return false;
                }
            }

            BreakEndpoint(ship, body, radial, tangent, normal, radius, angularRate,
                periapsisSpeed, breakSeconds,
                out Vector3d breakLocalPosition, out Vector3d breakLocalVelocity,
                out _, out _, out double breakEndJd);

            // The conic's shape and duration were solved from the unrotated
            // periapsis state; the break actually ends at the parking position
            // advanced by the phase it swept, so the whole conic is that same
            // conic rotated forward by the phase. Rotation about the focus leaves
            // the radius, anomaly and time unchanged, so the ceiling crossing is
            // the same state in the ship's own plane.
            double breakPhase = angularRate * breakSeconds;
            escapePosition = RotateAbout(escapePosition, normal, breakPhase);
            escapeVelocity = RotateAbout(escapeVelocity, normal, breakPhase);

            double burnStartJd = breakEndJd + escapeSeconds / SecondsPerDay;

            climb = new DepartureClimb(radius, periapsisSpeed, vInfinity, parabolic,
                breakSeconds, escapeSeconds,
                BreakPeakToEndpoint(ship, body, radial, tangent, normal, radius,
                    angularRate, periapsisSpeed, breakSeconds, cheap: false,
                    rankOnly: rankOnly),
                breakEndJd, burnStartJd, breakLocalPosition, breakLocalVelocity,
                escapePosition, escapeVelocity);
            return true;
        }

        /// <summary>The departure body's velocity at a date, in the heliocentric
        /// frame. A convenience for the climb's speed-change seed.</summary>
        private Vector3d BodyVelocity(BodyRuntime body, double julianDate)
        {
            _model.ComputeState(body, julianDate, out _, out Vector3d velocity);
            return velocity;
        }

        /// <summary>The break arc and the escape conic for a solved climb, in the
        /// plan's arc chain. The break is a quintic from the ship's state to the
        /// conic's periapsis; the escape is a Keplerian coast about the departure
        /// body from there to the ceiling, and its end state is the departure
        /// burn's start state.</summary>
        private void AppendDepartureClimb(List<PlanArc> arcs, ShipState ship,
            BodyRuntime body, DepartureClimb climb, bool fast)
        {
            _model.ComputeState(body, climb.BreakEndJulianDate,
                out Vector3d bodyAtBreakEnd, out Vector3d bodyVelocityAtBreakEnd);
            Vector3d breakEndPosition = bodyAtBreakEnd + climb.BreakEndLocalPositionM;
            Vector3d breakEndVelocity = bodyVelocityAtBreakEnd
                                        + climb.BreakEndLocalVelocityMps;

            arcs.Add(MakeArc(FlightPhase.Departure, body.Id, ship.JulianDate,
                climb.BreakEndJulianDate, ship.PositionM, ship.VelocityMps,
                breakEndPosition, breakEndVelocity, fast: fast));

            ConicArc escape = ConicArc.Create(_model, body, climb.BreakEndJulianDate,
                climb.EscapeSeconds, climb.BreakEndLocalPositionM,
                climb.BreakEndLocalVelocityMps);
            arcs.Add(MakeConicArc(FlightPhase.Escape, body.Id, climb.BreakEndJulianDate,
                climb.BurnStartJulianDate, escape, Vector3d.Zero, fast));
        }

        /// <summary>The departure burn's start state in the heliocentric frame:
        /// the escape conic's ceiling state, which the climb fixed. The position
        /// and velocity are both states, not an (escape ray, radius, speed) triple,
        /// so the burn starts exactly where the ballistic climb ends.</summary>
        private void ClimbBurnStart(BodyRuntime body, DepartureClimb climb,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            _model.ComputeState(body, climb.BurnStartJulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);
            positionM = bodyPosition + climb.EscapeEndLocalPositionM;
            velocityMps = bodyVelocity + climb.EscapeEndLocalVelocityMps;
        }

        /// <summary>The climb's seed break duration as a fraction of the trip: the
        /// old reference coast gave the break a time share, and the new break only
        /// uses it as a starting duration for its own peak solve.</summary>
        private static double ClimbSeedSeconds(double availableSeconds) =>
            Math.Max(MinimumArcSeconds, Math.Min(availableSeconds, 3_600.0));

        /// <summary>The norm the burn-only seam solve reduces: the seam's
        /// position and velocity, each measured against the chain gate, so a norm
        /// of one is exactly the gate.</summary>
        private static double BurnOnlyNorm(double positionM, double velocityMps) =>
            Math.Sqrt(
                (positionM / BurnOnlyPositionGateM) * (positionM / BurnOnlyPositionGateM)
                + (velocityMps / BurnOnlyVelocityGateMps)
                * (velocityMps / BurnOnlyVelocityGateMps));

        /// <summary>An orthonormal pair about a direction, for parameterising a
        /// ray by two small angles.</summary>
        private static void Tangents(Vector3d direction, out Vector3d tangent1,
            out Vector3d tangent2)
        {
            Vector3d axis = Math.Abs(direction.z) < 0.9
                ? new Vector3d(0.0, 0.0, 1.0)
                : new Vector3d(0.0, 1.0, 0.0);
            tangent1 = Vector3d.Cross(direction, axis).Normalized;
            if (tangent1.SqrMagnitude < 0.5)
                tangent1 = new Vector3d(1.0, 0.0, 0.0);
            tangent2 = Vector3d.Cross(direction, tangent1).Normalized;
            if (tangent2.SqrMagnitude < 0.5)
                tangent2 = new Vector3d(0.0, 1.0, 0.0);
        }

        /// <summary>
        /// One evaluation of the burn-only seam: the fixed departure climb (short
        /// break and escape conic), the departure burn forward from the conic's
        /// ceiling state, the brake backward from the capture chain's entry, and
        /// the six residuals between them. The handover date is the crossing's own
        /// — the brake's end — so the two burns meet at one instant by construction
        /// and the total time stays an output.
        /// </summary>
        private bool EvaluateBurnOnlySeam(BurnOnlyGeometry geometry, ShipState ship,
            Vector3d raySeed, Vector3d rayTangent1, Vector3d rayTangent2,
            Vector3d brakeSeed, Vector3d brakeTangent1, Vector3d brakeTangent2,
            double[] x, out BurnOnlyEvaluation evaluation)
        {
            evaluation = default;
            double burnSeconds = x[2];
            double brakeSeconds = x[5];
            if (!(burnSeconds > MinimumArcSeconds) || !(brakeSeconds > MinimumArcSeconds))
                return false;

            Vector3d ray = Ray(raySeed, rayTangent1, rayTangent2, x[0], x[1]);
            Vector3d brakeDirection = Ray(brakeSeed, brakeTangent1, brakeTangent2,
                x[3], x[4]);

            double breakSeconds = geometry.DepartureBody != null
                ? geometry.Climb.BreakSeconds
                : 0.0;
            double burnStartJd = geometry.DepartureBody != null
                ? geometry.Climb.BurnStartJulianDate
                : ship.JulianDate;
            Vector3d burnStartPosition;
            Vector3d burnStartVelocity;
            if (geometry.DepartureBody != null)
            {
                ClimbBurnStart(geometry.DepartureBody, geometry.Climb,
                    out burnStartPosition, out burnStartVelocity);
            }
            else
            {
                burnStartPosition = ship.PositionM;
                burnStartVelocity = ship.VelocityMps;
            }

            double brakeStartJd = burnStartJd + burnSeconds / SecondsPerDay;
            double entryJd = brakeStartJd + brakeSeconds / SecondsPerDay;

            _model.ComputeState(geometry.Target, entryJd,
                out Vector3d targetAtEntry, out Vector3d targetVelocityAtEntry);
            Vector3d entryPosition = targetAtEntry + geometry.Chain.EntryLocalPositionM;
            Vector3d entryVelocity = targetVelocityAtEntry
                                     + geometry.Chain.EntryLocalVelocityMps;

            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunAtBurnStart, out Vector3d sunVelocityAtBurnStart);
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                burnStartJd, burnSeconds, burnStartPosition - sunAtBurnStart,
                burnStartVelocity - sunVelocityAtBurnStart, geometry.AccelerationMps2,
                retrograde: false, samples: geometry.Samples,
                fixedThrustDirection: ray, gravity: geometry.Gravity);

            _model.ComputeState(_model.Sun, entryJd,
                out Vector3d sunAtEntry, out Vector3d sunVelocityAtEntry);
            Vector3d brakeLocalStartPosition;
            Vector3d brakeLocalStartVelocity;
            if (geometry.Gravity != null)
            {
                FiniteBurnArc.SimulateBackward(_model, geometry.Gravity, _model.Sun,
                    entryJd, entryPosition - sunAtEntry, entryVelocity - sunVelocityAtEntry,
                    geometry.AccelerationMps2, retrograde: true, brakeSeconds,
                    geometry.BrakeSamplesCount,
                    out brakeLocalStartPosition, out brakeLocalStartVelocity, brakeDirection);
            }
            else
            {
                FiniteBurnArc.SimulateBackward(_model.Sun,
                    entryPosition - sunAtEntry, entryVelocity - sunVelocityAtEntry,
                    geometry.AccelerationMps2, retrograde: true, brakeSeconds,
                    geometry.BrakeSamplesCount,
                    out brakeLocalStartPosition, out brakeLocalStartVelocity, brakeDirection);
            }

            _model.ComputeState(_model.Sun, brakeStartJd,
                out Vector3d sunAtBrakeStart, out Vector3d sunVelocityAtBrakeStart);
            Vector3d brakeStartPosition = sunAtBrakeStart + brakeLocalStartPosition;
            Vector3d brakeStartVelocity = sunVelocityAtBrakeStart + brakeLocalStartVelocity;

            Vector3d burnEndPosition = departureBurn.EndLocalPositionM + sunAtBrakeStart;
            Vector3d burnEndVelocity = departureBurn.EndLocalVelocityMps
                                       + sunVelocityAtBrakeStart;

            evaluation = new BurnOnlyEvaluation(
                burnEndPosition - brakeStartPosition,
                burnEndVelocity - brakeStartVelocity,
                ray, brakeDirection, breakSeconds, burnSeconds, brakeSeconds,
                burnStartJd, entryJd, burnStartPosition, burnStartVelocity);
            return true;
        }

        /// <summary>
        /// The six-unknown burn-only seam: a damped Newton with a
        /// finite-difference Jacobian, in the same style as the pinned departure
        /// solve. The unknowns are the two ray angles, the departure burn's
        /// duration, the two brake-direction angles, and the brake's duration;
        /// the residuals are the seam's three position and three velocity
        /// components. A point at the chain gate is accepted if the tighter
        /// target cannot be reached, and the gate is the planner's own 1 km /
        /// 1 m/s — no tolerance is widened to call a failure a success.
        /// </summary>
        private bool TrySolveBurnOnlySeam(BurnOnlyGeometry geometry, ShipState ship,
            BurnOnlySeed seed, out BurnOnlyEvaluation solution, out string reason)
        {
            solution = default;
            reason = null;

            Tangents(seed.Ray, out Vector3d rayTangent1, out Vector3d rayTangent2);
            Tangents(seed.BrakeDirection, out Vector3d brakeTangent1,
                out Vector3d brakeTangent2);

            var x = new double[6];
            x[2] = Math.Max(seed.BurnSeconds, MinimumArcSeconds);
            x[5] = Math.Max(seed.BrakeSeconds, MinimumArcSeconds);

            double bestNorm = double.PositiveInfinity;
            double bestPosition = double.PositiveInfinity;
            double bestVelocity = double.PositiveInfinity;
            var best = new double[6];
            Array.Copy(x, best, x.Length);

            var jacobian = new double[6, 6];
            var rhs = new double[6];
            int jacobianAge = int.MaxValue;
            string stall = null;

            for (int iteration = 0; iteration < geometry.Iterations; iteration++)
            {
                if (!EvaluateBurnOnlySeam(geometry, ship, seed.Ray, rayTangent1,
                        rayTangent2, seed.BrakeDirection, brakeTangent1, brakeTangent2,
                        x, out BurnOnlyEvaluation current))
                {
                    reason = "burn-only: the crossing has no seam at this seed";
                    return false;
                }

                double position = current.SeamPositionM.Magnitude;
                double velocity = current.SeamVelocityMps.Magnitude;
                double norm = BurnOnlyNorm(position, velocity);
                if (norm < bestNorm)
                {
                    bestNorm = norm;
                    bestPosition = position;
                    bestVelocity = velocity;
                    Array.Copy(x, best, x.Length);
                }

                Trace?.Invoke($"burn-only pass {iteration}: {position:F0} m, " +
                              $"{velocity:E2} m/s, burn {x[2]:F0} s, " +
                              $"brake {x[5]:F0} s");

                if (position <= geometry.TargetPositionM
                    && velocity <= geometry.TargetVelocityMps)
                {
                    solution = current;
                    return true;
                }

                if (jacobianAge >= 2)
                {
                    double angleStep = 1e-4;
                    double burnStep = Math.Clamp(1e-4 * x[2], 0.5, 20.0);
                    double brakeStep = Math.Clamp(1e-4 * x[5], 0.5, 20.0);

                    for (int column = 0; column < 6; column++)
                    {
                        double step = column < 2 ? angleStep
                            : column == 2 ? burnStep
                            : column < 5 ? angleStep
                            : brakeStep;
                        var trial = new double[6];
                        Array.Copy(x, trial, x.Length);
                        trial[column] += step;
                        if (!EvaluateBurnOnlySeam(geometry, ship, seed.Ray, rayTangent1,
                                rayTangent2, seed.BrakeDirection, brakeTangent1,
                                brakeTangent2, trial, out BurnOnlyEvaluation shifted))
                        {
                            reason = "burn-only: the crossing has no seam beside this seed";
                            return false;
                        }

                        for (int row = 0; row < 3; row++)
                        {
                            jacobian[row, column] = (Component(shifted.SeamPositionM, row)
                                                     - Component(current.SeamPositionM, row))
                                                    / step;
                            jacobian[row + 3, column] =
                                (Component(shifted.SeamVelocityMps, row)
                                 - Component(current.SeamVelocityMps, row)) / step;
                        }
                    }

                    jacobianAge = 0;
                }
                else
                {
                    jacobianAge++;
                }

                for (int row = 0; row < 3; row++)
                {
                    rhs[row] = -Component(current.SeamPositionM, row);
                    rhs[row + 3] = -Component(current.SeamVelocityMps, row);
                }

                if (!SolveLinear(jacobian, rhs, out double[] stepVector))
                {
                    stall = "the seam's Jacobian is singular at this seed";
                    break;
                }

                double scale = 1.0;
                double maxAngle = Math.Max(
                    Math.Max(Math.Abs(stepVector[0]), Math.Abs(stepVector[1])),
                    Math.Max(Math.Abs(stepVector[3]), Math.Abs(stepVector[4])));
                if (maxAngle > 0.5) scale = Math.Min(scale, 0.5 / maxAngle);
                if (Math.Abs(stepVector[2]) > 0.5 * x[2])
                    scale = Math.Min(scale, 0.5 * x[2] / Math.Abs(stepVector[2]));
                if (Math.Abs(stepVector[5]) > 0.5 * x[5])
                    scale = Math.Min(scale, 0.5 * x[5] / Math.Abs(stepVector[5]));

                bool accepted = false;
                for (int attempt = 0; attempt < 6 && !accepted; attempt++, scale *= 0.5)
                {
                    var trial = new double[6];
                    for (int i = 0; i < 6; i++) trial[i] = x[i] + scale * stepVector[i];
                    if (!(trial[2] > MinimumArcSeconds)
                        || !(trial[5] > MinimumArcSeconds)) continue;
                    if (!EvaluateBurnOnlySeam(geometry, ship, seed.Ray, rayTangent1,
                            rayTangent2, seed.BrakeDirection, brakeTangent1,
                            brakeTangent2, trial, out BurnOnlyEvaluation trialEvaluation))
                    {
                        continue;
                    }

                    double trialNorm = BurnOnlyNorm(
                        trialEvaluation.SeamPositionM.Magnitude,
                        trialEvaluation.SeamVelocityMps.Magnitude);
                    if (trialNorm < norm)
                    {
                        Array.Copy(trial, x, x.Length);
                        accepted = true;
                    }
                }

                if (!accepted)
                {
                    stall = "the damped step stopped improving the seam";
                    break;
                }
            }

            // The gate is the planner's own. Accept the best point if it is
            // inside it; a failure names the residual it reached.
            if (bestPosition <= geometry.GatePositionM
                && bestVelocity <= geometry.GateVelocityMps)
            {
                Array.Copy(best, x, x.Length);
                if (EvaluateBurnOnlySeam(geometry, ship, seed.Ray, rayTangent1, rayTangent2,
                        seed.BrakeDirection, brakeTangent1, brakeTangent2, x,
                        out BurnOnlyEvaluation gate))
                {
                    solution = gate;
                    Trace?.Invoke($"burn-only: settled at the gate " +
                                  $"({bestPosition:F0} m, {bestVelocity:E2} m/s)");
                    return true;
                }
            }

            string why = stall == null ? "the iteration cap was reached" : stall;
            reason = $"burn-only: the seam did not close ({why}; best " +
                     $"{bestPosition:F0} m and {bestVelocity:E2} m/s with burn " +
                     $"{best[2]:F0} s and brake {best[5]:F0} s)";
            return false;
        }

        /// <summary>
        /// One evaluation of the short-coast seam: the pinned break for the trial
        /// ray, the departure burn forward, a heliocentric conic for the trial
        /// coast <c>c</c>, and the brake backward from the capture chain's entry.
        /// The brake's duration is not an unknown: the requested total fixes it as
        /// <c>T − break − burn − c − capture</c>. The six residuals are the
        /// conic's end state against the brake's start state.
        ///
        /// A coast shorter than <see cref="MinimumArcSeconds"/> is a seam, so its
        /// end state is the burn's, stepped by the two-body series. That keeps the
        /// branch smooth through <c>c = 0</c> rather than jumping when a conic
        /// becomes representable: at zero coast the system is the burn-only seam.
        /// A conic whose geometry has no branch at the trial coast is reported by
        /// returning false; no plan is invented for it.
        /// </summary>
        private bool EvaluateShortCoastSeam(ShortCoastGeometry geometry, ShipState ship,
            Vector3d raySeed, Vector3d rayTangent1, Vector3d rayTangent2,
            Vector3d brakeSeed, Vector3d brakeTangent1, Vector3d brakeTangent2,
            double[] x, out ShortCoastEvaluation evaluation)
        {
            evaluation = default;
            double burnSeconds = x[2];
            double coastSeconds = x[3];
            if (!(burnSeconds > MinimumArcSeconds)
                || !(coastSeconds >= 0.0) || !double.IsFinite(coastSeconds))
            {
                return false;
            }

            Vector3d ray = Ray(raySeed, rayTangent1, rayTangent2, x[0], x[1]);
            Vector3d brakeDirection = Ray(brakeSeed, brakeTangent1, brakeTangent2,
                x[4], x[5]);

            double breakSeconds = geometry.Climb.BreakSeconds;
            double burnStartJd = geometry.Climb.BurnStartJulianDate;
            ClimbBurnStart(geometry.DepartureBody, geometry.Climb,
                out Vector3d burnStartPosition, out Vector3d burnStartVelocity);

            double brakeSeconds = geometry.TotalSeconds - breakSeconds
                                  - geometry.Climb.EscapeSeconds - burnSeconds
                                  - coastSeconds - geometry.Chain.TotalSeconds;
            if (!(brakeSeconds > MinimumArcSeconds)) return false;

            double entryJd = ship.JulianDate
                             + (geometry.TotalSeconds - geometry.Chain.TotalSeconds)
                             / SecondsPerDay;
            double coastStartJd = burnStartJd + burnSeconds / SecondsPerDay;
            double brakeStartJd = entryJd - brakeSeconds / SecondsPerDay;

            _model.ComputeState(geometry.Target, entryJd,
                out Vector3d targetAtEntry, out Vector3d targetVelocityAtEntry);
            Vector3d entryPosition = targetAtEntry + geometry.Chain.EntryLocalPositionM;
            Vector3d entryVelocity = targetVelocityAtEntry
                                     + geometry.Chain.EntryLocalVelocityMps;

            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunAtBurnStart, out Vector3d sunVelocityAtBurnStart);
            FiniteBurnArc departureBurn = FiniteBurnArc.Create(_model, _model.Sun,
                burnStartJd, burnSeconds, burnStartPosition - sunAtBurnStart,
                burnStartVelocity - sunVelocityAtBurnStart, geometry.AccelerationMps2,
                retrograde: false, samples: PinnedDepartureSamples,
                fixedThrustDirection: ray, gravity: _gravity);

            _model.ComputeState(_model.Sun, brakeStartJd,
                out Vector3d sunAtBrakeStart, out Vector3d sunVelocityAtBrakeStart);

            Vector3d coastStartLocalPosition = departureBurn.EndLocalPositionM;
            Vector3d coastStartLocalVelocity = departureBurn.EndLocalVelocityMps;
            Vector3d coastEndLocalPosition;
            Vector3d coastEndLocalVelocity;
            if (coastSeconds >= MinimumArcSeconds)
            {
                try
                {
                    ConicArc coast = ConicArc.Create(_model, _model.Sun, coastStartJd,
                        coastSeconds, coastStartLocalPosition, coastStartLocalVelocity);
                    coast.LocalStateAt(1.0, out coastEndLocalPosition,
                        out coastEndLocalVelocity);
                }
                catch (Exception)
                {
                    // No conic branch at this coast: hyperbolic/parabolic geometry
                    // or a radial state. The trial is refused; the caller reports
                    // the residual, not a plan.
                    return false;
                }
            }
            else
            {
                // Two-body series for the sub-arc coast. The jerk term over one
                // second is well under a millimetre, so this is smooth and exact
                // enough down to c = 0, where it is the burn's own end state.
                double radius = coastStartLocalPosition.Magnitude;
                Vector3d acceleration = radius > 0.0
                    ? coastStartLocalPosition
                      * (-_model.Sun.Gm / (radius * radius * radius))
                    : Vector3d.Zero;
                coastEndLocalPosition = coastStartLocalPosition
                                        + coastStartLocalVelocity * coastSeconds
                                        + acceleration * (0.5 * coastSeconds * coastSeconds);
                coastEndLocalVelocity = coastStartLocalVelocity
                                        + acceleration * coastSeconds;
            }

            Vector3d conicEndPosition = sunAtBrakeStart + coastEndLocalPosition;
            Vector3d conicEndVelocity = sunVelocityAtBrakeStart + coastEndLocalVelocity;

            _model.ComputeState(_model.Sun, entryJd,
                out Vector3d sunAtEntry, out Vector3d sunVelocityAtEntry);
            FiniteBurnArc.SimulateBackward(_model, _gravity, _model.Sun, entryJd,
                entryPosition - sunAtEntry, entryVelocity - sunVelocityAtEntry,
                geometry.AccelerationMps2, retrograde: true, brakeSeconds, BrakeSamples,
                out Vector3d brakeLocalStartPosition,
                out Vector3d brakeLocalStartVelocity, brakeDirection);

            Vector3d brakeStartPosition = sunAtBrakeStart + brakeLocalStartPosition;
            Vector3d brakeStartVelocity = sunVelocityAtBrakeStart + brakeLocalStartVelocity;

            evaluation = new ShortCoastEvaluation(
                conicEndPosition - brakeStartPosition,
                conicEndVelocity - brakeStartVelocity,
                ray, brakeDirection, breakSeconds, burnSeconds, coastSeconds,
                brakeSeconds, burnStartJd, coastStartJd, brakeStartJd, entryJd,
                burnStartPosition, burnStartVelocity);
            return true;
        }

        /// <summary>
        /// The six-unknown short-coast seam: a damped Newton with a
        /// finite-difference Jacobian, the burn-only solve plus the coast's
        /// duration. The unknowns are the two ray angles, the departure burn's
        /// duration, the coast's duration, and the two brake-direction angles; the
        /// brake's duration is fixed by the requested total. A point at the chain
        /// gate is accepted, and a failure names the residual it reached.
        /// </summary>
        private bool TrySolveShortCoastSeam(ShortCoastGeometry geometry, ShipState ship,
            BurnOnlySeed seed, double coastSeedSeconds, out ShortCoastEvaluation solution,
            out string reason)
        {
            solution = default;
            reason = null;

            Tangents(seed.Ray, out Vector3d rayTangent1, out Vector3d rayTangent2);
            Tangents(seed.BrakeDirection, out Vector3d brakeTangent1,
                out Vector3d brakeTangent2);

            var x = new double[6];
            x[2] = Math.Max(seed.BurnSeconds, MinimumArcSeconds);
            x[3] = Math.Max(coastSeedSeconds, 0.0);

            double bestNorm = double.PositiveInfinity;
            double bestPosition = double.PositiveInfinity;
            double bestVelocity = double.PositiveInfinity;
            var best = new double[6];
            Array.Copy(x, best, x.Length);

            var jacobian = new double[6, 6];
            var rhs = new double[6];
            int jacobianAge = int.MaxValue;
            string stall = null;

            for (int iteration = 0; iteration < BurnOnlyIterations; iteration++)
            {
                if (!EvaluateShortCoastSeam(geometry, ship, seed.Ray, rayTangent1,
                        rayTangent2, seed.BrakeDirection, brakeTangent1, brakeTangent2,
                        x, out ShortCoastEvaluation current))
                {
                    reason = "short coast: the crossing has no seam at this seed " +
                             "(the conic has no branch or the coast leaves no brake time)";
                    return false;
                }

                double position = current.SeamPositionM.Magnitude;
                double velocity = current.SeamVelocityMps.Magnitude;
                double norm = BurnOnlyNorm(position, velocity);
                if (norm < bestNorm)
                {
                    bestNorm = norm;
                    bestPosition = position;
                    bestVelocity = velocity;
                    Array.Copy(x, best, x.Length);
                }

                Trace?.Invoke($"short-coast pass {iteration}: {position:F0} m, " +
                              $"{velocity:E2} m/s, burn {x[2]:F0} s, " +
                              $"coast {x[3]:F0} s");

                if (position <= BurnOnlyTargetPositionM
                    && velocity <= BurnOnlyTargetVelocityMps)
                {
                    solution = current;
                    return true;
                }

                if (jacobianAge >= 2)
                {
                    double angleStep = 1e-4;
                    double burnStep = Math.Clamp(1e-4 * x[2], 0.5, 20.0);
                    double coastStep = Math.Clamp(1e-4 * Math.Max(x[3], 1.0), 0.5, 20.0);

                    for (int column = 0; column < 6; column++)
                    {
                        double step = column < 2 ? angleStep
                            : column == 2 ? burnStep
                            : column == 3 ? coastStep
                            : angleStep;
                        var trial = new double[6];
                        Array.Copy(x, trial, x.Length);
                        trial[column] += step;
                        if (!EvaluateShortCoastSeam(geometry, ship, seed.Ray,
                                rayTangent1, rayTangent2, seed.BrakeDirection,
                                brakeTangent1, brakeTangent2, trial,
                                out ShortCoastEvaluation shifted))
                        {
                            reason = "short coast: the crossing has no seam beside " +
                                     "this seed";
                            return false;
                        }

                        for (int row = 0; row < 3; row++)
                        {
                            jacobian[row, column] = (Component(shifted.SeamPositionM, row)
                                                     - Component(current.SeamPositionM, row))
                                                    / step;
                            jacobian[row + 3, column] =
                                (Component(shifted.SeamVelocityMps, row)
                                 - Component(current.SeamVelocityMps, row)) / step;
                        }
                    }

                    jacobianAge = 0;
                }
                else
                {
                    jacobianAge++;
                }

                for (int row = 0; row < 3; row++)
                {
                    rhs[row] = -Component(current.SeamPositionM, row);
                    rhs[row + 3] = -Component(current.SeamVelocityMps, row);
                }

                if (!SolveLinear(jacobian, rhs, out double[] stepVector))
                {
                    stall = "the short coast's Jacobian is singular at this seed";
                    break;
                }

                double scale = 1.0;
                double maxAngle = Math.Max(
                    Math.Max(Math.Abs(stepVector[0]), Math.Abs(stepVector[1])),
                    Math.Max(Math.Abs(stepVector[4]), Math.Abs(stepVector[5])));
                if (maxAngle > 0.5) scale = Math.Min(scale, 0.5 / maxAngle);
                if (Math.Abs(stepVector[2]) > 0.5 * x[2])
                    scale = Math.Min(scale, 0.5 * x[2] / Math.Abs(stepVector[2]));
                double coastLimit = Math.Max(0.5 * x[3], 30.0);
                if (Math.Abs(stepVector[3]) > coastLimit)
                    scale = Math.Min(scale, coastLimit / Math.Abs(stepVector[3]));

                bool accepted = false;
                for (int attempt = 0; attempt < 6 && !accepted; attempt++, scale *= 0.5)
                {
                    var trial = new double[6];
                    for (int i = 0; i < 6; i++) trial[i] = x[i] + scale * stepVector[i];
                    if (!(trial[2] > MinimumArcSeconds) || !(trial[3] >= 0.0)) continue;
                    if (!EvaluateShortCoastSeam(geometry, ship, seed.Ray, rayTangent1,
                            rayTangent2, seed.BrakeDirection, brakeTangent1,
                            brakeTangent2, trial, out ShortCoastEvaluation trialEvaluation))
                    {
                        continue;
                    }

                    double trialNorm = BurnOnlyNorm(
                        trialEvaluation.SeamPositionM.Magnitude,
                        trialEvaluation.SeamVelocityMps.Magnitude);
                    if (trialNorm < norm)
                    {
                        Array.Copy(trial, x, x.Length);
                        accepted = true;
                    }
                }

                if (!accepted)
                {
                    stall = "the damped step stopped improving the short coast's seam";
                    break;
                }
            }

            if (bestPosition <= BurnOnlyPositionGateM
                && bestVelocity <= BurnOnlyVelocityGateMps)
            {
                Array.Copy(best, x, x.Length);
                if (EvaluateShortCoastSeam(geometry, ship, seed.Ray, rayTangent1,
                        rayTangent2, seed.BrakeDirection, brakeTangent1, brakeTangent2,
                        x, out ShortCoastEvaluation gate))
                {
                    solution = gate;
                    Trace?.Invoke($"short coast: settled at the gate " +
                                  $"({bestPosition:F0} m, {bestVelocity:E2} m/s)");
                    return true;
                }
            }

            string why = stall == null ? "the iteration cap was reached" : stall;
            reason = $"short coast: the seam did not close ({why}; best " +
                     $"{bestPosition:F0} m and {bestVelocity:E2} m/s with burn " +
                     $"{best[2]:F0} s and coast {best[3]:F0} s)";
            return false;
        }

        /// <summary>
        /// Diagnostic only: with the two directions held at a seed, solve the two
        /// durations against the six residuals in the least-squares sense. It is
        /// the honest answer to "how much of the miss is the durations?" — the
        /// failure path reports its residual, and it is never presented as a
        /// solution.
        /// </summary>
        private bool TrySolveBurnOnlyDurations(BurnOnlyGeometry geometry, ShipState ship,
            Vector3d ray, Vector3d brakeDirection, double burnSeedSeconds,
            double brakeSeedSeconds, out double burnSeconds, out double brakeSeconds,
            out double positionM, out double velocityMps)
        {
            burnSeconds = Math.Max(burnSeedSeconds, MinimumArcSeconds);
            brakeSeconds = Math.Max(brakeSeedSeconds, MinimumArcSeconds);
            positionM = double.PositiveInfinity;
            velocityMps = double.PositiveInfinity;

            for (int iteration = 0; iteration < BurnOnlyDiagnosticIterations; iteration++)
            {
                if (!EvaluateBurnOnlyDurations(geometry, ship, ray, brakeDirection,
                        burnSeconds, brakeSeconds, out Vector3d seamPosition,
                        out Vector3d seamVelocity))
                {
                    return false;
                }

                double position = seamPosition.Magnitude;
                double velocity = seamVelocity.Magnitude;
                positionM = position;
                velocityMps = velocity;
                if (position <= 1.0 && velocity <= 1e-3) return true;

                var residual = new double[6];
                for (int row = 0; row < 3; row++)
                {
                    residual[row] = Component(seamPosition, row);
                    residual[row + 3] = Component(seamVelocity, row);
                }

                double burnStep = Math.Clamp(1e-4 * burnSeconds, 0.5, 20.0);
                double brakeStep = Math.Clamp(1e-4 * brakeSeconds, 0.5, 20.0);
                var jacobian = new double[6, 2];
                if (!EvaluateBurnOnlyDurations(geometry, ship, ray, brakeDirection,
                        burnSeconds + burnStep, brakeSeconds,
                        out Vector3d shiftedPosition, out Vector3d shiftedVelocity))
                {
                    return false;
                }

                for (int row = 0; row < 3; row++)
                {
                    jacobian[row, 0] = (Component(shiftedPosition, row)
                                        - Component(seamPosition, row)) / burnStep;
                    jacobian[row + 3, 0] = (Component(shiftedVelocity, row)
                                            - Component(seamVelocity, row)) / burnStep;
                }

                if (!EvaluateBurnOnlyDurations(geometry, ship, ray, brakeDirection,
                        burnSeconds, brakeSeconds + brakeStep,
                        out shiftedPosition, out shiftedVelocity))
                {
                    return false;
                }

                for (int row = 0; row < 3; row++)
                {
                    jacobian[row, 1] = (Component(shiftedPosition, row)
                                        - Component(seamPosition, row)) / brakeStep;
                    jacobian[row + 3, 1] = (Component(shiftedVelocity, row)
                                            - Component(seamVelocity, row)) / brakeStep;
                }

                var normal = new double[2, 2];
                var rhs = new double[2];
                for (int row = 0; row < 6; row++)
                {
                    normal[0, 0] += jacobian[row, 0] * jacobian[row, 0];
                    normal[0, 1] += jacobian[row, 0] * jacobian[row, 1];
                    normal[1, 1] += jacobian[row, 1] * jacobian[row, 1];
                    rhs[0] -= jacobian[row, 0] * residual[row];
                    rhs[1] -= jacobian[row, 1] * residual[row];
                }

                normal[1, 0] = normal[0, 1];
                if (!SolveLinear(normal, rhs, out double[] step)) return false;

                double scale = 1.0;
                if (Math.Abs(step[0]) > 0.25 * burnSeconds)
                    scale = Math.Min(scale, 0.25 * burnSeconds / Math.Abs(step[0]));
                if (Math.Abs(step[1]) > 0.25 * brakeSeconds)
                    scale = Math.Min(scale, 0.25 * brakeSeconds / Math.Abs(step[1]));

                bool accepted = false;
                double norm = BurnOnlyNorm(position, velocity);
                for (int attempt = 0; attempt < 6 && !accepted; attempt++, scale *= 0.5)
                {
                    double trialBurn = burnSeconds + scale * step[0];
                    double trialBrake = brakeSeconds + scale * step[1];
                    if (!(trialBurn > MinimumArcSeconds)
                        || !(trialBrake > MinimumArcSeconds)) continue;
                    if (!EvaluateBurnOnlyDurations(geometry, ship, ray, brakeDirection,
                            trialBurn, trialBrake, out Vector3d trialPosition,
                            out Vector3d trialVelocity))
                    {
                        continue;
                    }

                    double trialNorm = BurnOnlyNorm(trialPosition.Magnitude,
                        trialVelocity.Magnitude);
                    if (trialNorm < norm)
                    {
                        burnSeconds = trialBurn;
                        brakeSeconds = trialBrake;
                        accepted = true;
                    }
                }

                if (!accepted) break;
            }

            return positionM <= BurnOnlyPositionGateM
                   && velocityMps <= BurnOnlyVelocityGateMps;
        }

        private bool EvaluateBurnOnlyDurations(BurnOnlyGeometry geometry, ShipState ship,
            Vector3d ray, Vector3d brakeDirection, double burnSeconds,
            double brakeSeconds, out Vector3d seamPosition,
            out Vector3d seamVelocity)
        {
            var x = new double[6];
            x[2] = burnSeconds;
            x[5] = brakeSeconds;
            if (!EvaluateBurnOnlySeam(geometry, ship, ray, Vector3d.Zero, Vector3d.Zero,
                    brakeDirection, Vector3d.Zero, Vector3d.Zero, x,
                    out BurnOnlyEvaluation evaluation))
            {
                seamPosition = Vector3d.Zero;
                seamVelocity = Vector3d.Zero;
                return false;
            }

            seamPosition = evaluation.SeamPositionM;
            seamVelocity = evaluation.SeamVelocityMps;
            return true;
        }

        /// <summary>
        /// Solve the departure burn's thrust direction and duration so that a burn
        /// integrated forward from the escape conic's ceiling state ends on the
        /// Lambert coast's departure velocity. Three equations (the velocity
        /// mismatch) in three unknowns (two thrust-direction angles and the burn's
        /// duration), solved by a damped Newton with a finite-difference Jacobian.
        /// The burn's start state and date are fixed by the climb; only its
        /// attitude and length are free, because the position the burn ends at is
        /// the coast's start. The coast runs from the burn's end to the coast's end
        /// in the time the trip leaves it.
        /// </summary>
        private bool TrySolvePinnedDeparture(ShipState ship, BodyRuntime departureBody,
            double acceleration, double burnStartJd, Vector3d burnStartPosition,
            Vector3d burnStartVelocity, double coastEndJd, Vector3d coastEnd,
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
            bool restarted = false;

            for (int iteration = 0; iteration < DepartureSolveIterations; iteration++)
            {
                if (!EvaluatePinnedDeparture(ship, departureBody, direction,
                        tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                        burnStartVelocity, coastEndJd, coastEnd, angle1, angle2, seconds,
                        samples, burnGravity, out Vector3d mismatch, out coastStartPosition,
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
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
                            angle1 + angleStep, angle2, seconds, samples, burnGravity,
                            out Vector3d mismatch1, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
                            angle1 - angleStep, angle2, seconds, samples, burnGravity,
                            out Vector3d mismatch1Low, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
                            angle1, angle2 + angleStep, seconds, samples, burnGravity,
                            out Vector3d mismatch2, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
                            angle1, angle2 - angleStep, seconds, samples, burnGravity,
                            out Vector3d mismatch2Low, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
                            angle1, angle2, seconds + timeStep, samples, burnGravity,
                            out Vector3d mismatch3, out _, out _, out _, out _)
                        || !EvaluatePinnedDeparture(ship, departureBody, direction,
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
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
                            tangent1, tangent2, acceleration, burnStartJd, burnStartPosition,
                            burnStartVelocity, coastEndJd, coastEnd,
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
                    // A flat basin can stall a hair above the tolerance: the
                    // damped Newton finds no step that lowers the residual, but
                    // the residual is already within ten tolerances. Retry once
                    // from a perturbed burn duration before rejecting the trip;
                    // the tolerance and the chain gate do not move.
                    if (!restarted && best <= 10.0 * DepartureSolveToleranceMps)
                    {
                        restarted = true;
                        angle1 = 0.0;
                        angle2 = 0.0;
                        seconds = burnSeconds * 1.01;
                        jacobianAge = int.MaxValue;
                        best = double.PositiveInfinity;
                        continue;
                    }

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
            BodyRuntime departureBody, double acceleration, double burnStartJd,
            Vector3d burnStartPosition, Vector3d burnStartVelocity, double coastEndJd,
            Vector3d coastEnd, ref Vector3d direction, ref double burnSeconds,
            int samples, int fallbackSamples, IGravityField burnGravity,
            out Vector3d coastStartPosition, out Vector3d coastStartVelocity,
            out Vector3d coastArrivalVelocity, out double coastSeconds,
            out string reason)
        {
            if (TrySolvePinnedDeparture(ship, departureBody, acceleration, burnStartJd,
                    burnStartPosition, burnStartVelocity, coastEndJd, coastEnd,
                    ref direction, ref burnSeconds, samples, burnGravity,
                    out coastStartPosition, out coastStartVelocity,
                    out coastArrivalVelocity, out coastSeconds, out reason))
            {
                return true;
            }

            if (fallbackSamples <= samples) return false;

            return TrySolvePinnedDeparture(ship, departureBody, acceleration, burnStartJd,
                burnStartPosition, burnStartVelocity, coastEndJd, coastEnd,
                ref direction, ref burnSeconds, fallbackSamples, burnGravity,
                out coastStartPosition, out coastStartVelocity,
                out coastArrivalVelocity, out coastSeconds, out reason);
        }

        private bool EvaluatePinnedDeparture(ShipState ship, BodyRuntime departureBody,
            Vector3d direction, Vector3d tangent1, Vector3d tangent2,
            double acceleration, double burnStartJd, Vector3d burnStartPosition,
            Vector3d burnStartVelocity, double coastEndJd, Vector3d coastEnd,
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

            double coastStartJd = burnStartJd + seconds / SecondsPerDay;
            coastSeconds = (coastEndJd - coastStartJd) * SecondsPerDay;
            if (!(coastSeconds > MinimumArcSeconds)) return false;

            _model.ComputeState(_model.Sun, burnStartJd,
                out Vector3d sunAtStart, out Vector3d sunVelocityAtStart);
            FiniteBurnArc burn = FiniteBurnArc.Create(_model, _model.Sun, burnStartJd,
                seconds, burnStartPosition - sunAtStart,
                burnStartVelocity - sunVelocityAtStart,
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
        private static bool BurnChainIsClosed(List<PlanArc> arcs,
            double positionGateM, double velocityGateMps, out string reason)
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

                if (positionStep <= positionGateM && velocityStep <= velocityGateMps)
                    continue;

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

                // A heliocentric departure has no body to clear at its burn's
                // start, and a station's hold point is its arrival's own
                // clearance, so the gate names a body only when there is one.
                if (near == null) continue;

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
            double totalSeconds, string reason, bool pinStalled = false,
            bool shortCoastWorthy = false)
        {
            BuildResult result = InfeasiblePlan(ship, target, totalSeconds, reason);
            return new BuildResult(result.Plan, Math.Max(totalSeconds * 1.5,
                totalSeconds + MinimumCoastSeconds), pinStalled, shortCoastWorthy);
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

        /// <summary>One departure: the short break out of the parking orbit, the
        /// ballistic escape conic it hands over to, and the exact states and
        /// dates the two arcs meet at. The break is a quintic to the parking
        /// radius on the ship's own prograde direction; the conic starts there and
        /// ends on the departure handover ceiling. The departure burn starts at the
        /// conic's ceiling state, which is why the burn's start is a state and not
        /// an (escape ray, radius, speed) triple.</summary>
        private readonly struct DepartureClimb
        {
            public DepartureClimb(double parkingRadiusM, double periapsisSpeedMps,
                double vInfinityMps, bool parabolic, double breakSeconds,
                double escapeSeconds, double breakPeakMps2,
                double breakEndJulianDate, double burnStartJulianDate,
                Vector3d breakEndLocalPositionM, Vector3d breakEndLocalVelocityMps,
                Vector3d escapeEndLocalPositionM, Vector3d escapeEndLocalVelocityMps)
            {
                ParkingRadiusM = parkingRadiusM;
                PeriapsisSpeedMps = periapsisSpeedMps;
                VInfinityMps = vInfinityMps;
                IsParabolic = parabolic;
                BreakSeconds = breakSeconds;
                EscapeSeconds = escapeSeconds;
                BreakPeakMps2 = breakPeakMps2;
                BreakEndJulianDate = breakEndJulianDate;
                BurnStartJulianDate = burnStartJulianDate;
                BreakEndLocalPositionM = breakEndLocalPositionM;
                BreakEndLocalVelocityMps = breakEndLocalVelocityMps;
                EscapeEndLocalPositionM = escapeEndLocalPositionM;
                EscapeEndLocalVelocityMps = escapeEndLocalVelocityMps;
            }

            public double ParkingRadiusM { get; }
            public double PeriapsisSpeedMps { get; }
            public double VInfinityMps { get; }
            public bool IsParabolic { get; }
            public double BreakSeconds { get; }
            public double EscapeSeconds { get; }
            public double BreakPeakMps2 { get; }
            public double BreakEndJulianDate { get; }
            public double BurnStartJulianDate { get; }

            /// <summary>Break end state, relative to the departure body at the
            /// break's end. This is the escape conic's start state.</summary>
            public Vector3d BreakEndLocalPositionM { get; }
            public Vector3d BreakEndLocalVelocityMps { get; }

            /// <summary>Escape conic end state, relative to the departure body at
            /// the ceiling. This is the departure burn's start state.</summary>
            public Vector3d EscapeEndLocalPositionM { get; }
            public Vector3d EscapeEndLocalVelocityMps { get; }

            public double ClimbSeconds => BreakSeconds + EscapeSeconds;
        }

        /// <summary>One seed for the burn-only seam: the two directions and the
        /// two durations.</summary>
        private readonly struct BurnOnlySeed
        {
            public BurnOnlySeed(Vector3d ray, Vector3d brakeDirection,
                double burnSeconds, double brakeSeconds)
            {
                Ray = ray;
                BrakeDirection = brakeDirection;
                BurnSeconds = burnSeconds;
                BrakeSeconds = brakeSeconds;
            }

            public Vector3d Ray { get; }
            public Vector3d BrakeDirection { get; }
            public double BurnSeconds { get; }
            public double BrakeSeconds { get; }
        }

        /// <summary>The fixed part of a burn-only crossing: the departure and
        /// arrival bodies, the capture chain the supplied date chose, the level,
        /// and the solved departure climb whose ceiling state is the burn's
        /// start. A **probe** is a ranking build: it integrates the two-body
        /// field at a fraction of the samples and stops at a looser gate, so the
        /// full-blast grid costs a fraction of the certified solve. The reported
        /// plan is always the certified build.</summary>
        private readonly struct BurnOnlyGeometry
        {
            public BurnOnlyGeometry(BodyRuntime departureBody, BodyRuntime target,
                TransferConic chain, double accelerationMps2, DepartureClimb climb,
                bool probe = false, IGravityField gravity = null)
            {
                DepartureBody = departureBody;
                Target = target;
                Chain = chain;
                AccelerationMps2 = accelerationMps2;
                Climb = climb;
                Samples = probe ? 32 : PinnedDepartureSamples;
                BrakeSamplesCount = probe ? 64 : BrakeSamples;
                Iterations = probe ? 16 : BurnOnlyIterations;
                TargetPositionM = probe ? 5_000.0 : BurnOnlyTargetPositionM;
                TargetVelocityMps = probe ? 5.0 : BurnOnlyTargetVelocityMps;
                GatePositionM = probe ? 50_000.0 : BurnOnlyPositionGateM;
                GateVelocityMps = probe ? 50.0 : BurnOnlyVelocityGateMps;
                Gravity = probe ? null : gravity;
            }

            public BodyRuntime DepartureBody { get; }
            public BodyRuntime Target { get; }
            public TransferConic Chain { get; }
            public double AccelerationMps2 { get; }
            public DepartureClimb Climb { get; }
            public int Samples { get; }
            public int BrakeSamplesCount { get; }
            public int Iterations { get; }
            public double TargetPositionM { get; }
            public double TargetVelocityMps { get; }
            public double GatePositionM { get; }
            public double GateVelocityMps { get; }
            public IGravityField Gravity { get; }
        }

        /// <summary>The fixed part of a short-coast crossing: the burn-only
        /// geometry plus the requested total, which fixes the brake's time.</summary>
        private readonly struct ShortCoastGeometry
        {
            public ShortCoastGeometry(BodyRuntime departureBody, BodyRuntime target,
                TransferConic chain, double accelerationMps2, DepartureClimb climb,
                double totalSeconds)
            {
                DepartureBody = departureBody;
                Target = target;
                Chain = chain;
                AccelerationMps2 = accelerationMps2;
                Climb = climb;
                TotalSeconds = totalSeconds;
            }

            public BodyRuntime DepartureBody { get; }
            public BodyRuntime Target { get; }
            public TransferConic Chain { get; }
            public double AccelerationMps2 { get; }
            public DepartureClimb Climb { get; }
            public double TotalSeconds { get; }
        }

        /// <summary>One evaluation of the short-coast seam: the six residuals,
        /// the solved arc states and dates, and the coast's own duration.</summary>
        private readonly struct ShortCoastEvaluation
        {
            public ShortCoastEvaluation(Vector3d seamPositionM, Vector3d seamVelocityMps,
                Vector3d ray, Vector3d brakeDirection, double breakSeconds,
                double burnSeconds, double coastSeconds, double brakeSeconds,
                double burnStartJulianDate, double coastStartJulianDate,
                double brakeStartJulianDate, double entryJulianDate,
                Vector3d burnStartPositionM, Vector3d burnStartVelocityMps)
            {
                SeamPositionM = seamPositionM;
                SeamVelocityMps = seamVelocityMps;
                Ray = ray;
                BrakeDirection = brakeDirection;
                BreakSeconds = breakSeconds;
                BurnSeconds = burnSeconds;
                CoastSeconds = coastSeconds;
                BrakeSeconds = brakeSeconds;
                BurnStartJulianDate = burnStartJulianDate;
                CoastStartJulianDate = coastStartJulianDate;
                BrakeStartJulianDate = brakeStartJulianDate;
                EntryJulianDate = entryJulianDate;
                BurnStartPositionM = burnStartPositionM;
                BurnStartVelocityMps = burnStartVelocityMps;
            }

            public Vector3d SeamPositionM { get; }
            public Vector3d SeamVelocityMps { get; }
            public Vector3d Ray { get; }
            public Vector3d BrakeDirection { get; }
            public double BreakSeconds { get; }
            public double BurnSeconds { get; }
            public double CoastSeconds { get; }
            public double BrakeSeconds { get; }
            public double BurnStartJulianDate { get; }
            public double CoastStartJulianDate { get; }
            public double BrakeStartJulianDate { get; }
            public double EntryJulianDate { get; }
            public Vector3d BurnStartPositionM { get; }
            public Vector3d BurnStartVelocityMps { get; }
        }

        /// <summary>One evaluation of the burn-only seam: the six residuals and
        /// the solved crossing's own states and dates.</summary>
        private readonly struct BurnOnlyEvaluation
        {
            public BurnOnlyEvaluation(Vector3d seamPositionM, Vector3d seamVelocityMps,
                Vector3d ray, Vector3d brakeDirection, double breakSeconds,
                double burnSeconds, double brakeSeconds, double burnStartJulianDate,
                double entryJulianDate, Vector3d burnStartPositionM,
                Vector3d burnStartVelocityMps)
            {
                SeamPositionM = seamPositionM;
                SeamVelocityMps = seamVelocityMps;
                Ray = ray;
                BrakeDirection = brakeDirection;
                BreakSeconds = breakSeconds;
                BurnSeconds = burnSeconds;
                BrakeSeconds = brakeSeconds;
                BurnStartJulianDate = burnStartJulianDate;
                EntryJulianDate = entryJulianDate;
                BurnStartPositionM = burnStartPositionM;
                BurnStartVelocityMps = burnStartVelocityMps;
            }

            public Vector3d SeamPositionM { get; }
            public Vector3d SeamVelocityMps { get; }
            public Vector3d Ray { get; }
            public Vector3d BrakeDirection { get; }
            public double BreakSeconds { get; }
            public double BurnSeconds { get; }
            public double BrakeSeconds { get; }
            public double BurnStartJulianDate { get; }
            public double EntryJulianDate { get; }
            public Vector3d BurnStartPositionM { get; }
            public Vector3d BurnStartVelocityMps { get; }
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
                bool pinStalled = false, bool shortCoastWorthy = false,
                bool pinSettled = false, PinSeed seed = default,
                bool built = false)
            {
                Plan = plan;
                RequiredSeconds = requiredSeconds;
                PinStalled = pinStalled;
                ShortCoastWorthy = shortCoastWorthy;
                PinSettled = pinSettled;
                Seed = seed;
                Built = built;
            }

            public TrajectoryPlan Plan { get; }
            public double RequiredSeconds { get; }

            /// <summary>True when the brake/departure fixed point met its own
            /// convergence test. A plan can still be built with this false: the
            /// chain gate, not the pin's stall, is what decides a plan.</summary>
            public bool PinSettled { get; }

            /// <summary>True when the build reached its plan — the chain gate
            /// accepted it — rather than returning a refusal. A seeded attempt
            /// that did not build is rebuilt cold.</summary>
            public bool Built { get; }

            /// <summary>The pin state the build settled on, or a
            /// <c>default(PinSeed)</c> when it refused or stretched.</summary>
            public PinSeed Seed { get; }

            /// <summary>True when a certified burn–coast–burn build refused at
            /// the chain gate after its outer brake/departure fixed point ran its
            /// pass cap without converging. The build retries once with the
            /// longer cap; a probe never sets this.</summary>
            public bool PinStalled { get; }

            /// <summary>True when the refusal is the coast path being squeezed by
            /// the trip time — the departure burn cannot be placed or the chain
            /// does not close — rather than the coast's own geometry. This is the
            /// gap between the burn-only minimum and the coast floor.
            /// <see cref="PlanAtDuration"/> hands this refusal to the short-coast
            /// build, and also tries that build on a Lambert geometry refusal;
            /// only a feasible short coast replaces either answer.</summary>
            public bool ShortCoastWorthy { get; }
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
