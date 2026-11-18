using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>The lifecycle of the player's ship.</summary>
    public enum FlightStatus
    {
        /// <summary>No flight. The ship sits still; a debug camera may move it.</summary>
        Idle,
        /// <summary>A plan is being flown.</summary>
        Flying,
        /// <summary>The plan completed and the ship is coasting in the parking orbit.</summary>
        Arrived,
        /// <summary>The integration hit a body or another fatal condition.</summary>
        Aborted,
        /// <summary>A plan was requested but the drive could not fly it.</summary>
        Rejected
    }

    /// <summary>What one frame's advance achieved.</summary>
    public struct FlightStep
    {
        /// <summary>The date the ship actually reached. Never ahead of the
        /// requested date; behind it when the substep budget ran out.</summary>
        public double ReachedJulianDate;

        public int Substeps;
        public bool BudgetExhausted;
        public bool Arrived;
        public bool CollisionDetected;
        public string CollisionBodyId;
    }

    /// <summary>
    /// Owns the ship: its integrated state, the active plan, and the integrator
    /// that advances it. This is the single writer of the ship's position during
    /// a flight ([ADR-007](/decisions/adr-007-flight-director.md)).
    ///
    /// The caller advances the clock first, then asks the session to reach the
    /// same Julian date. The session splits the request at plan arc boundaries,
    /// so the arrival state is hit exactly, and honours a substep budget: when
    /// the budget is exhausted the returned date is the date actually reached,
    /// and the caller must clamp the clock to it rather than let the world run
    /// ahead of the ship.
    ///
    /// This class is plain C# with no Unity dependency, so the whole flight loop
    /// is verifiable in EditMode tests without a scene.
    /// </summary>
    public sealed class FlightSession
    {
        /// <summary>Default RK4 substep budget for one rendered frame.</summary>
        public const int DefaultSubstepsPerFrame = 2_000;

        /// <summary>How close to the arrival date counts as arrival. One Julian
        /// date ULP at 2.4 million is 4.7e-10 days (40 µs); this is 0.9 ms, far
        /// below any physical quantity and safely above the representation floor.</summary>
        public const double ArrivalToleranceDays = 1e-8;

        /// <summary>The clock may advance this fraction of the planned flight
        /// time before a plotted preview is flagged stale.</summary>
        public const double PreviewStaleFraction = 0.01;

        private readonly SolarSystemModel _model;
        private readonly GravityField _gravity;
        private readonly TripPlanner _planner;
        private readonly ShipIntegrator _integrator;

        private ShipState _state;
        private Autopilot _autopilot;
        private readonly HashSet<PlanArc> _appliedImpulses = new HashSet<PlanArc>();

        /// <summary>Departure state the preview was solved from. Retiming
        /// rebuilds from this rather than the current (possibly coasted) state,
        /// so the preview stays one coherent draft.</summary>
        private ShipState _previewDeparture;

        public FlightSession(SolarSystemModel model, GravityField gravity, TripPlanner planner,
            ShipIntegrator integrator = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _integrator = integrator ?? new ShipIntegrator(gravity);

            _state = new ShipState(model.CurrentJulianDate, Vector3d.Zero, Vector3d.Zero);
        }

        public FlightStatus Status { get; private set; } = FlightStatus.Idle;

        public ShipState State => _state;

        /// <summary>The plan being flown, or the last plan the session built.</summary>
        public TrajectoryPlan Plan { get; private set; }

        /// <summary>A route the player plotted but has not begun, or null. The
        /// preview owns nothing: it never moves the ship and never changes
        /// <see cref="Status"/>. Begin consumes it by replanning from the
        /// current state.</summary>
        public TrajectoryPlan Preview { get; private set; }

        /// <summary>The date the preview was plotted at, or NaN.</summary>
        public double PreviewJulianDate { get; private set; } = double.NaN;

        /// <summary>The fastest feasible flight time for the current preview,
        /// or NaN. The trip-time control's lower bound.</summary>
        public double PreviewMinimumSeconds { get; private set; } = double.NaN;

        /// <summary>The slowest trip the trip-time control offers, or NaN. A
        /// fixed multiple of <see cref="PreviewMinimumSeconds"/>.</summary>
        public double PreviewMaximumSeconds =>
            double.IsNaN(PreviewMinimumSeconds)
                ? double.NaN
                : PreviewMinimumSeconds * TripPlanner.MaxDurationScale;

        /// <summary>The preview's current flight time, or NaN.</summary>
        public double PreviewDurationSeconds => Preview?.FlightTimeSeconds ?? double.NaN;

        public bool HasPreview => Preview != null;

        /// <summary>True while a preview exists and no flight owns the ship.</summary>
        public bool IsPreviewing => Preview != null && Plan == null;

        /// <summary>Why the flight stopped, when it did not arrive.</summary>
        public string Failure { get; private set; }

        /// <summary>The date the ship arrived on the parking orbit.</summary>
        public double ArrivedJulianDate { get; private set; } = double.NaN;

        /// <summary>The commanded thrust at the current state, refreshed after
        /// every advance. Safe to read from the HUD because the session prepares
        /// the field first.</summary>
        public ThrustCommand CurrentThrust { get; private set; } = ThrustCommand.None;

        /// <summary>Current commanded acceleration as a fraction of the drive.</summary>
        public double Throttle { get; private set; }

        public int SubstepsLastFrame { get; private set; }
        public bool BudgetExhaustedLastFrame { get; private set; }

        public bool HasPlan => Plan != null;
        public bool IsFlying => Status == FlightStatus.Flying;
        public bool IsActive => Status == FlightStatus.Flying || Status == FlightStatus.Arrived;
        public string TargetId => Plan?.TargetId;

        public ShipIntegrator Integrator => _integrator;

        /// <summary>Arrival style used for the next plan. The planner's quintic
        /// capture is the default; a conic arrival is the cheap, slow
        /// alternative.</summary>
        public CaptureStyle ArrivalStyle
        {
            get => _planner.ArrivalStyle;
            set => _planner.ArrivalStyle = value;
        }

        /// <summary>Current phase of the plan, or null when there is none.</summary>
        public FlightPhase? Phase =>
            Plan != null && Status != FlightStatus.Idle ? Plan.PhaseAt(_state.JulianDate) : (FlightPhase?)null;

        /// <summary>Seconds until the next arc boundary or the arrival. Infinity
        /// once the plan is complete; NaN when there is no plan.</summary>
        public double SecondsToNextEvent
        {
            get
            {
                if (Plan == null) return double.NaN;
                double boundary = Plan.NextBoundaryJulianDate(_state.JulianDate);
                return double.IsFinite(boundary)
                    ? (boundary - _state.JulianDate) * JulianDate.SecondsPerDay
                    : double.PositiveInfinity;
            }
        }

        /// <summary>Phase that follows the next boundary, or null when the next
        /// boundary is the arrival.</summary>
        public FlightPhase? NextPhase
        {
            get
            {
                if (Plan == null) return null;
                double boundary = Plan.NextBoundaryJulianDate(_state.JulianDate);
                if (!double.IsFinite(boundary)) return null;

                for (int i = 1; i < Plan.Arcs.Count; i++)
                    if (Plan.Arcs[i].StartJulianDate == boundary) return Plan.Arcs[i].Phase;

                return null; // the next boundary is the arrival

            }
        }

        /// <summary>The middle of the plan where the along-track thrust turns
        /// over, or NaN if this plan has no such point.</summary>
        public double FlipJulianDate { get; private set; } = double.NaN;

        /// <summary>Put the ship in a body's own state at a date. Gateway is the
        /// shipped starting point, so the default trip is the tested one.</summary>
        public void PlaceAt(string bodyId, double julianDate)
        {
            BodyRuntime body = _model.Get(bodyId)
                ?? throw new ArgumentException($"No body '{bodyId}' in the catalogue.", nameof(bodyId));

            _model.ComputeState(body, julianDate, out Vector3d position, out Vector3d velocity);

            Cancel();
            _state = new ShipState(julianDate, position, velocity,
                _state.DryMassKg, _state.PropellantKg);
            RefreshTelemetry();
        }

        /// <summary>Move the ship without a trajectory. Used by the debug camera
        /// and by the debug teleport, never during a flight. The velocity is
        /// cleared because a teleport has no trajectory.</summary>
        public void Teleport(Vector3d positionM, double julianDate)
        {
            Cancel();
            _state = new ShipState(julianDate, positionM, Vector3d.Zero,
                _state.DryMassKg, _state.PropellantKg);
            RefreshTelemetry();
        }

        /// <summary>
        /// Build a route preview from the ship's current state at a date,
        /// without moving the ship or taking ownership of it. A second plot
        /// replaces the first: there is only ever one preview.
        /// </summary>
        public TrajectoryPlan Plot(string targetId, double julianDate)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            _previewDeparture = _state.WithJulianDate(julianDate);
            Preview = _planner.Plan(_previewDeparture, targetId);
            PreviewJulianDate = julianDate;
            PreviewMinimumSeconds = Preview.FlightTimeSeconds;
            return Preview;
        }

        /// <summary>
        /// Re-time the current preview without re-solving the minimum or moving
        /// the ship. One planner build at the requested duration, so the route,
        /// its flip, and its delta-v all change while the arrival date slides.
        /// The value is clamped to the preview's feasible window; the departure
        /// state and displayed date stay pinned, which keeps a drag stable while
        /// the clock runs.
        /// </summary>
        public TrajectoryPlan SetPreviewDuration(double seconds)
        {
            if (Preview == null) return null;

            double minimum = PreviewMinimumSeconds;
            double duration = Math.Clamp(seconds, minimum, PreviewMaximumSeconds);
            Preview = _planner.PlanAtDuration(_previewDeparture, Preview.TargetId, duration);
            return Preview;
        }

        /// <summary>
        /// Choose the arrival style and rebuild the preview against it, from the
        /// same pinned departure state. The fastest feasible plan of the new
        /// style sets the trip-time window; the previous flight time is kept
        /// when it falls inside that window, so the choice changes the arrival
        /// shape rather than silently throwing away the player's trip time. A
        /// live flight is never touched: it keeps the plan it was begun with.
        /// Returns the rebuilt preview, or null when there was none.
        /// </summary>
        public TrajectoryPlan SetArrivalStyle(CaptureStyle style)
        {
            if (style == ArrivalStyle) return Preview;
            ArrivalStyle = style;
            if (Preview == null) return null;

            double requested = Preview.FlightTimeSeconds;
            string targetId = Preview.TargetId;

            Preview = _planner.Plan(_previewDeparture, targetId);
            PreviewMinimumSeconds = Preview.FlightTimeSeconds;

            if (Preview.IsFeasible && requested > PreviewMinimumSeconds)
                Preview = _planner.PlanAtDuration(_previewDeparture, targetId,
                    Math.Min(requested, PreviewMaximumSeconds));

            return Preview;
        }

        /// <summary>Forget the preview, if there is one.</summary>
        public void DiscardPreview()
        {
            Preview = null;
            PreviewJulianDate = double.NaN;
            PreviewMinimumSeconds = double.NaN;
        }

        /// <summary>
        /// Days of clock advance after which the preview is stale, or NaN when
        /// there is no preview. The window is a fraction of the planned flight
        /// time, so a long trip tolerates a longer look.
        /// </summary>
        public double PreviewStaleAfterDays =>
            Preview != null ? PreviewStaleFraction * Preview.FlightTimeDays : double.NaN;

        /// <summary>Clock days since the preview was plotted, or NaN when there
        /// is no preview.</summary>
        public double PreviewAgeDays(double julianDate) =>
            Preview != null ? julianDate - PreviewJulianDate : double.NaN;

        /// <summary>
        /// True when the clock has advanced far enough that the preview should
        /// be checked again. Begin is always allowed: the planner is
        /// deterministic and replans from the current state.
        /// </summary>
        public bool IsPreviewStale(double julianDate) =>
            Preview != null && PreviewAgeDays(julianDate) > PreviewStaleAfterDays;

        /// <summary>Seconds left before the preview goes stale (negative once
        /// it has), or NaN when there is no preview.</summary>
        public double PreviewStaleInSeconds(double julianDate) =>
            Preview != null
                ? (PreviewStaleAfterDays - PreviewAgeDays(julianDate)) * JulianDate.SecondsPerDay
                : double.NaN;

        /// <summary>Build a Go To plan from the current state and start flying
        /// it. Returns the plan even when it is rejected, so the UI can explain
        /// why. A rejected or infeasible plan leaves the ship where it was.
        /// Begin consumes any preview; begin always replans.</summary>
        public TrajectoryPlan Begin(string targetId, double julianDate)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            // A retimed preview carries the player's chosen flight time into
            // the flight. Read it before the preview is discarded, solve the
            // fastest plan at the current date, and re-time to the choice.
            double requestedSeconds = double.NaN;
            if (Preview != null && Preview.TargetId == targetId)
                requestedSeconds = Preview.FlightTimeSeconds;

            DiscardPreview();
            _state.JulianDate = julianDate;
            _appliedImpulses.Clear();
            Plan = _planner.Plan(_state, targetId);
            if (!double.IsNaN(requestedSeconds) && requestedSeconds > Plan.FlightTimeSeconds)
                Plan = _planner.PlanAtDuration(_state, targetId, requestedSeconds);
            FlipJulianDate = Plan.FindFlipJulianDate(_gravity);

            if (!Plan.IsFeasible)
            {
                _autopilot = null;
                Status = FlightStatus.Rejected;
                Failure = !string.IsNullOrEmpty(Plan.InfeasibleReason)
                    ? Plan.InfeasibleReason
                    : $"the plan peaks at {Plan.PeakThrustMps2:F1} m/s², above the " +
                      $"{Plan.MaxAccelerationMps2:F1} m/s² drive";
                RefreshTelemetry();
                return Plan;
            }

            _autopilot = new Autopilot(_gravity, Plan);
            Status = FlightStatus.Flying;
            Failure = null;
            ArrivedJulianDate = double.NaN;
            RefreshTelemetry();
            return Plan;
        }

        /// <summary>Stop the flight and keep the ship where it is. A preview is
        /// discarded too: it belongs to the ship's current situation.</summary>
        public void Cancel()
        {
            if (Plan != null && Status == FlightStatus.Flying)
            {
                Failure = "flight cancelled";
            }

            Plan = null;
            DiscardPreview();
            _autopilot = null;
            _appliedImpulses.Clear();
            FlipJulianDate = double.NaN;
            ArrivedJulianDate = double.NaN;
            Status = FlightStatus.Idle;
            CurrentThrust = ThrustCommand.None;
            Throttle = 0.0;
        }

        /// <summary>
        /// Advance an idle ship under gravity with the drive off, so time can
        /// pass while the player reads a preview and the ship stays in its
        /// parking orbit. A live flight owns the ship; call
        /// <see cref="Advance"/> for that instead.
        /// </summary>
        public FlightStep Coast(double toJulianDate, int substepBudget = DefaultSubstepsPerFrame)
        {
            var step = new FlightStep
            {
                ReachedJulianDate = _state.JulianDate
            };

            if (IsActive) return step;
            if (!(toJulianDate > _state.JulianDate)) return step;

            if (substepBudget <= 0)
            {
                step.BudgetExhausted = true;
                BudgetExhaustedLastFrame = true;
                SubstepsLastFrame = 0;
                return step;
            }

            int savedLimit = _integrator.MaxSubsteps;
            _integrator.MaxSubsteps = substepBudget;

            try
            {
                IntegrationReport report = _integrator.Advance(
                    ref _state, toJulianDate, (state, date) => ThrustCommand.None);

                step.Substeps = report.Substeps;
                step.BudgetExhausted = report.StepLimitReached;
                step.CollisionDetected = report.CollisionDetected;
                step.CollisionBodyId = report.CollisionBodyId;
            }
            finally
            {
                _integrator.MaxSubsteps = savedLimit;
            }

            step.ReachedJulianDate = _state.JulianDate;
            SubstepsLastFrame = step.Substeps;
            BudgetExhaustedLastFrame = step.BudgetExhausted;
            RefreshTelemetry();
            return step;
        }

        /// <summary>
        /// Advance the ship to a Julian date, integrating in segments split at
        /// plan arc boundaries and stopping if the substep budget runs out. The
        /// caller must clamp the clock to <see cref="FlightStep.ReachedJulianDate"/>.
        /// </summary>
        public FlightStep Advance(double toJulianDate, int substepBudget = DefaultSubstepsPerFrame)
        {
            var step = new FlightStep
            {
                ReachedJulianDate = _state.JulianDate
            };

            if (Plan == null) return step;
            if (Status != FlightStatus.Flying && Status != FlightStatus.Arrived) return step;

            // While the plan is live the ship stops on the arrival date. The
            // clock is then clamped back to it, so the world never coasts past
            // the arrival in a single frame and the matched state is kept.
            double target = toJulianDate;
            if (Status == FlightStatus.Flying && Plan.ArrivalJulianDate < target)
                target = Plan.ArrivalJulianDate;

            if (!(target > _state.JulianDate)) return step;

            if (substepBudget <= 0)
            {
                step.BudgetExhausted = true;
                BudgetExhaustedLastFrame = true;
                SubstepsLastFrame = 0;
                return step;
            }

            int remainingBudget = substepBudget;
            int savedLimit = _integrator.MaxSubsteps;
            _integrator.MaxSubsteps = substepBudget;

            try
            {
                // Guard against a leg that makes no progress: a handful of arcs
                // means at most a handful of legs per call, so a small cap is enough.
                for (int leg = 0; leg < 10 && remainingBudget > 0; leg++)
                {
                    if (!(_state.JulianDate < target)) break;

                    // Once the arrival is reached the drive is off; keep
                    // coasting so the ship and the clock stay together.
                    if (Plan.IsComplete(_state.JulianDate))
                    {
                        double beforeCoast = _state.JulianDate;
                        _integrator.MaxSubsteps = remainingBudget;
                        IntegrationReport coast = _integrator.Advance(
                            ref _state, target,
                            (probe, date) => ThrustCommand.None, 0.0);

                        step.Substeps += coast.Substeps;
                        remainingBudget -= Math.Max(1, coast.Substeps);

                        if (coast.CollisionDetected)
                        {
                            step.CollisionDetected = true;
                            step.CollisionBodyId = coast.CollisionBodyId;
                            Status = FlightStatus.Aborted;
                            Failure = $"collision with {coast.CollisionBodyId ?? "a body"}";
                            break;
                        }

                        if (coast.StepLimitReached)
                        {
                            step.BudgetExhausted = true;
                            break;
                        }

                        if (!(_state.JulianDate > beforeCoast)) break;
                        break;
                    }

                    PlanArc arc = Plan.ArcAt(_state.JulianDate);
                    ApplyImpulse(arc);

                    double legTarget = Math.Min(target, arc.EndJulianDate);
                    if (!(legTarget > _state.JulianDate))
                    {
                        // The seam is exactly on the state; the next iteration
                        // picks up the following arc and applies its impulse.
                        if (!(_state.JulianDate < target)) break;
                        continue;
                    }

                    double hint = arc.SuggestedMaxStepSeconds;
                    PlanArc legArc = arc;
                    double before = _state.JulianDate;
                    _integrator.MaxSubsteps = remainingBudget;
                    IntegrationReport report = _integrator.Advance(
                        ref _state, legTarget,
                        (probe, date) => ThrustForArc(legArc, probe, date), hint);

                    step.Substeps += report.Substeps;
                    remainingBudget -= Math.Max(1, report.Substeps);

                    if (report.CollisionDetected)
                    {
                        step.CollisionDetected = true;
                        step.CollisionBodyId = report.CollisionBodyId;
                        Status = FlightStatus.Aborted;
                        Failure = $"collision with {report.CollisionBodyId ?? "a body"}";
                        break;
                    }

                    if (report.StepLimitReached)
                    {
                        step.BudgetExhausted = true;
                        break;
                    }

                    if (!(_state.JulianDate > before)) break;

                    // Arrival is detected on the step that reaches the last arc
                    // boundary, with a representation-floor tolerance. The loop
                    // then keeps coasting to the requested date with the drive off.
                    if (Status == FlightStatus.Flying &&
                        _state.JulianDate >= Plan.ArrivalJulianDate - ArrivalToleranceDays)
                    {
                        Status = FlightStatus.Arrived;
                        ArrivedJulianDate = _state.JulianDate;
                        step.Arrived = true;
                    }
                }
            }
            finally
            {
                _integrator.MaxSubsteps = savedLimit;
            }

            step.ReachedJulianDate = _state.JulianDate;
            SubstepsLastFrame = step.Substeps;
            BudgetExhaustedLastFrame = step.BudgetExhausted;
            RefreshTelemetry();
            return step;
        }

        private ThrustCommand ThrustForArc(PlanArc arc, ShipState state, double julianDate)
        {
            if (Status != FlightStatus.Flying) return ThrustCommand.None;
            if (_autopilot == null || Plan == null || Plan.IsComplete(julianDate))
                return ThrustCommand.None;
            return _autopilot.ComputeForArc(state, arc, julianDate);
        }

        /// <summary>
        /// Apply an arc's impulsive burn once, when the ship reaches its start.
        /// The plan's conic arcs are exact in position; the burn is the velocity
        /// step between the previous arc's end and this arc's start.
        /// </summary>
        private void ApplyImpulse(PlanArc arc)
        {
            if (arc == null || arc.EntryImpulseMps <= 0.0) return;
            if (_appliedImpulses.Add(arc))
                _state.VelocityMps += arc.EntryImpulseVectorMps;
        }

        /// <summary>Recompute the displayed thrust with the field pointed at the
        /// ship. The autopilot reads the field without preparing it, which is
        /// only safe inside the integrator; the HUD path has to prepare first.</summary>
        private void RefreshTelemetry()
        {
            if (Plan == null)
            {
                CurrentThrust = ThrustCommand.None;
                Throttle = 0.0;
                return;
            }

            _gravity.Prepare(_state.PositionM, _state.JulianDate);

            if (Status == FlightStatus.Flying && _autopilot != null &&
                !Plan.IsComplete(_state.JulianDate))
            {
                CurrentThrust = _autopilot.Compute(_state);
            }
            else
            {
                CurrentThrust = ThrustCommand.None;
            }

            Throttle = Plan.MaxAccelerationMps2 > 0.0
                ? CurrentThrust.Throttle(Plan.MaxAccelerationMps2)
                : 0.0;
        }
    }
}
