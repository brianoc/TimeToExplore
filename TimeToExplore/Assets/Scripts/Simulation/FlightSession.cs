using System;
using System.Collections.Generic;
using System.Threading;
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
        /// <summary>The plan completed and the ship is holding at the arrival
        /// state: a parking orbit for a planet, the hold point for a station.</summary>
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
    /// Retimes and preview plans both run on a background thread: the worker
    /// builds the newest request with its own planner and gravity field, and
    /// <see cref="TryApplyCompletedRetime"/> / <see cref="TryApplyCompletedPreview"/>
    /// publish the finished plan on the main thread.
    /// <see cref="RequestPreviewDuration"/> is the slider's retime path;
    /// <see cref="PlotAsync"/> plots a route and <see cref="SetArrivalStyle"/>
    /// and <see cref="SetBurnLevel"/> rebuild the preview. Every one of them is
    /// latest-wins: the newest request supersedes
    /// an older one, so a drag never queues work behind a stale plan. The
    /// synchronous <see cref="SetPreviewDuration"/> remains the deterministic
    /// path for callers that need a plan immediately.
    ///
    /// This class is plain C# with no Unity dependency, so the whole flight loop
    /// is verifiable in EditMode tests without a scene. Dispose it to stop the
    /// retime worker: the scene calls it from the bootstrap's teardown.
    /// </summary>
    public sealed class FlightSession : IDisposable
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

        /// <summary>Recent preview windows, keyed by departure date, target,
        /// burn level, and arrival style. A repeat selection reuses the floor
        /// plan and the slow end instead of re-solving either; the cache is owned
        /// here because the preview is the session's draft. It is cleared when
        /// the departure date or the ship's pinned state changes.</summary>
        private readonly Dictionary<WindowKey, PreviewWindow> _windows =
            new Dictionary<WindowKey, PreviewWindow>();

        /// <summary>The departure date the cached windows belong to, or NaN
        /// before the first plot.</summary>
        private double _windowEpoch = double.NaN;

        /// <summary>Keeps the preview cache from growing without bound while a
        /// player tries many targets at one date.</summary>
        private const int MaxCachedWindows = 8;

        /// <summary>Plans already built at a requested flight time inside a
        /// preview window. A slider handle returned to a value the player has
        /// already waited for is a dictionary hit rather than another
        /// multi-second build. The entries share the window cache's key and are
        /// dropped with it, so a changed departure date or a
        /// place/teleport can never read one back.</summary>
        private readonly Dictionary<WindowKey, List<CachedRetime>> _retimedPlans =
            new Dictionary<WindowKey, List<CachedRetime>>();

        /// <summary>How many retimed plans one window keeps. A route is about
        /// 50 KB: two finite-burn interpolation tables of 512 samples dominate,
        /// at 24 bytes a sample for position and velocity. Sixty-four is about
        /// 3 MB per window, enough that a long scrub across the slider does not
        /// evict the value the player is about to return to, while the eight
        /// window keys bound the whole cache at tens of megabytes.</summary>
        private const int MaxCachedRetimesPerWindow = 64;

        /// <summary>How close a repeat ask must be to a cached build. A slider
        /// is continuous, so a handle returned near its old position differs by
        /// a fraction of the window rather than being bit-identical. A fifth of
        /// a percent of the window is under a pixel of the track and far below
        /// any physical difference in the route.</summary>
        private const double RetimeCacheFraction = 0.002;

        /// <summary>How many neighbouring slider steps a published plot
        /// prefetches on each side of its duration. A fresh plot starts at the
        /// window minimum, so the lower side clamps away.</summary>
        private const int PlotPrefetchSteps = 2;

        /// <summary>How many steps a drag prefetches in the direction the
        /// handle is travelling.</summary>
        private const int DragPrefetchSteps = 3;

        /// <summary>Departure state the preview was solved from. Retiming
        /// rebuilds from this rather than the current (possibly coasted) state,
        /// so the preview stays one coherent draft.</summary>
        private ShipState _previewDeparture;

        /// <summary>How long <see cref="Dispose"/> waits for an in-flight
        /// background plan build to finish. A build is one <c>PlanAtDuration</c>
        /// or window solve (about 100–150 ms at the epoch), so this is generous;
        /// the worker is a background thread and checks the stop flag before
        /// publishing, so a thread that somehow outlives the wait still cannot
        /// touch a discarded plan.</summary>
        public const int RetimeJoinMilliseconds = 5_000;

        /// <summary>Guards the request queue, the finished plan, and the stop
        /// flag. The worker never reads a plan the session has published, and
        /// the main thread never mutates a plan the worker is building.</summary>
        private readonly object _retimeLock = new object();

        private bool _retimeStop;
        private bool _retimeBusy;
        private long _retimeGeneration;
        private RetimeRequest _pendingRetime;
        private TrajectoryPlan _completedRetime;
        private long _completedRetimeGeneration;
        private string _retimeFailure;
        private double _requestedPreviewSeconds = double.NaN;

        /// <summary>The background planner's generation for preview results.
        /// Every new preview request bumps it, and every cancellation that
        /// discards a preview build bumps it, so a stale build can never
        /// publish over a newer ask.</summary>
        private long _previewGeneration;

        /// <summary>A queued background preview build, or the newest of them.
        /// Only the newest pending request is kept: the worker takes
        /// <see cref="_pendingPreview"/> when it picks up work.</summary>
        private PreviewRequest _pendingPreview;

        /// <summary>The preview build the worker has taken and is running. A
        /// repeat ask for that target continues it rather than queueing the
        /// same work again behind it. Cleared when the build finishes, whether
        /// it publishes, fails, or is superseded.</summary>
        private PreviewRequest _buildingPreview;

        /// <summary>A finished background preview build, waiting for the main
        /// thread to publish it. Null while none is waiting or one was
        /// discarded.</summary>
        private CompletedPreview _completedPreview;
        private long _completedPreviewGeneration;
        private string _previewFailure;

        /// <summary>A finished plan that a selection warmed and nothing has
        /// shown: the route a Plot click can adopt as its preview without
        /// solving again. It is not <see cref="Preview"/>, so every panel, the
        /// trajectory line, and Begin still read the session as idle. Cleared
        /// by any newer preview request, an adoption, or a discard.</summary>
        private CompletedPreview _primedPreview;
        private long _primedPreviewGeneration;

        /// <summary>Neighbouring durations queued for the background planner,
        /// the **lowest** priority on the worker: a retime, a rebuild, or a plot
        /// takes the worker first, and any real request drops this queue. A
        /// prefetch fills a cache slot and never publishes, so it must never
        /// delay the plan the player is waiting for.</summary>
        private readonly List<PrefetchRequest> _pendingPrefetch =
            new List<PrefetchRequest>();

        /// <summary>True while a prefetch item is building. Tracked apart from
        /// <see cref="_retimeBusy"/> so a prefetch never reads as an
        /// outstanding retime to the HUD's handle.</summary>
        private bool _prefetchBuilding;

        /// <summary>The published preview's inputs, captured when it was plotted:
        /// the frozen departure state, the target, the burn level, the arrival
        /// style, and the window the slot indices live in. A prefetch built from
        /// anything else fills a slot the retime path cannot read. The worker
        /// never reads these; every item carries its own copy.</summary>
        private bool _prefetchArmed;
        private string _prefetchTargetId;
        private ShipState _prefetchDeparture;
        private double _prefetchBurnLevelG;
        private CaptureStyle _prefetchArrival;
        private double _prefetchMinimum = double.NaN;
        private double _prefetchMaximum = double.NaN;

        /// <summary>The duration of the last accepted preview ask: the plot's
        /// own total, then each slider request as it lands. A new request's
        /// travel direction is the sign of the difference against this, which is
        /// how a drag is inferred without the HUD saying so.</summary>
        private double _lastPreviewRequestSeconds = double.NaN;

        /// <summary>Counters for the benchmark and the tests: retimes the
        /// worker built, asks the retime cache answered, and prefetched
        /// neighbours it filled. They decide nothing.</summary>
        private int _retimeBuilds;
        private int _retimeCacheHits;
        private int _prefetchBuilds;

        /// <summary>The worker's own planner and gravity field. The field is
        /// mutable (it prepares an active set), so sharing the flight
        /// simulation's instance would race the integrator; the catalogue is
        /// read-only, so a second field over the same model is isolated. It
        /// serves both background retimes and background preview builds.</summary>
        private TripPlanner _workerPlanner;

        /// <summary>The background planner thread. It is renamed at the first
        /// request; the field name predates the preview builds.</summary>
        private Thread _plannerThread;

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
        /// <see cref="Status"/>. Begin consumes it, flying it while it is fresh
        /// and replanning past that.</summary>
        public TrajectoryPlan Preview { get; private set; }

        /// <summary>The date the preview was plotted at, or NaN.</summary>
        public double PreviewJulianDate { get; private set; } = double.NaN;

        /// <summary>The nav computer's minimum trip time for the current preview,
        /// or NaN. The trip-time control's lower bound: the planner's
        /// <see cref="TripPlanner.PlanFastest"/> — the zero-coast burn-only
        /// crossing when it wins, otherwise the coast floor.</summary>
        public double PreviewMinimumSeconds { get; private set; } = double.NaN;

        /// <summary>The slowest trip the trip-time control offers, or NaN. It is
        /// the slowest duration the planner still accepts inside
        /// <see cref="TripPlanner.MaxDurationScale"/> times the minimum, found by
        /// probing the window, because a long coast is rejected once its transfer
        /// sweeps 180° or its arrival no longer needs a brake.</summary>
        public double PreviewMaximumSeconds { get; private set; } = double.NaN;

        /// <summary>The preview's current flight time, or NaN.</summary>
        public double PreviewDurationSeconds => Preview?.FlightTimeSeconds ?? double.NaN;

        public bool HasPreview => Preview != null;

        /// <summary>True while a preview exists and no flight owns the ship.</summary>
        public bool IsPreviewing => Preview != null && Plan == null;

        /// <summary>Why the flight stopped, when it did not arrive.</summary>
        public string Failure { get; private set; }

        /// <summary>The date the ship arrived on its arrival state: a parking
        /// orbit for a planet, the hold point for a station.</summary>
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

        /// <summary>
        /// True when the plan being flown, or the preview when nothing is
        /// flying, targets a station. A station is a point target, so its
        /// arrival is a hold-point rendezvous rather than an orbit: the panel,
        /// the status line, and the phase table read it from here instead of
        /// guessing from the arc shapes.
        /// </summary>
        public bool TargetIsStation
        {
            get
            {
                TrajectoryPlan plan = Plan ?? Preview;
                if (plan == null) return false;

                BodyRuntime body = _model.Get(plan.TargetId);
                return body != null && body.Data.IsStation;
            }
        }

        public ShipIntegrator Integrator => _integrator;

        /// <summary>Arrival style used for the next plan. The conic capture is
        /// the default; a fixed-thrust arrival flies the same coasts with finite
        /// burns at <see cref="BurnLevelG"/> instead of impulses.</summary>
        public CaptureStyle ArrivalStyle
        {
            get => _planner.ArrivalStyle;
            set => _planner.ArrivalStyle = value;
        }

        /// <summary>Player-facing burn level in g for the profile's departure
        /// burn, brake, and hybrid capture.</summary>
        public double BurnLevelG
        {
            get => _planner.BurnLevelG;
            set => _planner.BurnLevelG = value;
        }

        /// <summary>Acceleration the fixed-thrust arrival's burn commands after
        /// the drive's own thrust margin is applied.</summary>
        public double FixedBurnAccelerationMps2 => _planner.FixedBurnAccelerationMps2;

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

        /// <summary>Put the ship in a body's own state at a date. Gateway is the
        /// shipped starting point, so the default trip is the tested one.</summary>
        public void PlaceAt(string bodyId, double julianDate)
        {
            BodyRuntime body = _model.Get(bodyId)
                ?? throw new ArgumentException($"No body '{bodyId}' in the catalogue.", nameof(bodyId));

            _model.ComputeState(body, julianDate, out Vector3d position, out Vector3d velocity);

            Cancel();
            ClearWindows();
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
            ClearWindows();
            _state = new ShipState(julianDate, positionM, Vector3d.Zero,
                _state.DryMassKg, _state.PropellantKg);
            RefreshTelemetry();
        }

        /// <summary>
        /// Build a route preview from the ship's current state at a date,
        /// without moving the ship or taking ownership of it. A second plot
        /// replaces the first: there is only ever one preview. This is the
        /// synchronous, deterministic reference path; the scene plots through
        /// <see cref="PlotAsync"/> so the calculation runs off the main
        /// thread. Prefetch belongs to the background path: this call queues no
        /// neighbours and disarms any the previous preview left.
        /// </summary>
        public TrajectoryPlan Plot(string targetId, double julianDate)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            CancelRetime();
            BeginPreviewReplacement();
            DisarmPrefetch();
            _previewDeparture = _state.WithJulianDate(julianDate);
            PreviewWindow window = WindowFor(targetId, _previewDeparture, _planner,
                _planner.BurnLevelG, _planner.ArrivalStyle);
            Preview = window.Plan;
            PreviewJulianDate = julianDate;
            PreviewMinimumSeconds = Preview.FlightTimeSeconds;
            PreviewMaximumSeconds = window.MaximumSeconds;
            _lastPreviewRequestSeconds = Preview.FlightTimeSeconds;
            return Preview;
        }

        /// <summary>
        /// Queue a route preview build on the background planner. The worker
        /// solves the fastest feasible plan for the target and the departure
        /// window with its own planner and gravity field, so the caller's
        /// thread is free while the plan is being computed — the first plot is
        /// the call that used to freeze the main thread for a couple of
        /// seconds. The plan is published with <see cref="TryApplyCompletedPreview"/>,
        /// which the HUD pumps once per frame; the previous preview, if any,
        /// stays on screen until then.
        ///
        /// A second plot replaces the first: the newest request wins, and an
        /// outstanding retime is cancelled, matching the synchronous
        /// <see cref="Plot"/>. Returns false when the session has been
        /// disposed.
        /// </summary>
        public bool PlotAsync(string targetId, double julianDate)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            // A plot for this target that is already queued or building is the
            // answer this ask wants. Restarting it would throw away the seconds
            // it has spent and leave the player waiting from zero, so a second
            // ask — a selection priming the route and then a Plot click — just
            // waits for the first one.
            if (IsPlotPending(targetId)) return true;

            _previewDeparture = _state.WithJulianDate(julianDate);
            _lastPreviewRequestSeconds = double.NaN;
            var request = new PreviewRequest(PreviewKind.Plot, targetId, _previewDeparture,
                double.NaN, ArrivalStyle, BurnLevelG, _planner);
            return EnqueuePreview(request);
        }

        /// <summary>True while a plot for this target is queued or building. A
        /// superseded build does not count: its generation no longer matches,
        /// so it can never publish. <see cref="PlotAsync"/> reads it to continue
        /// work that is already under way instead of restarting it.</summary>
        public bool IsPlotPending(string targetId) => IsKindPending(PreviewKind.Plot, targetId);

        /// <summary>
        /// Warm the plan for a target in the background without publishing it.
        /// The selection calls this so the first, slow solve is over by the
        /// time the player asks to see the route, and nothing changes if they
        /// never do: no preview, no drawn route, no panel rows. The finished
        /// plan waits in <see cref="_primedPreview"/> for
        /// <see cref="TryAdoptPrimedPreview"/>, which turns it into the preview
        /// the Plot click asked for without solving again.
        ///
        /// A prime is the same build a plot is, so it shares the queue and the
        /// newest request wins. A failure is dropped silently: the player asked
        /// for nothing yet, and the Plot click will report its own failure if
        /// it has to build the plan itself.
        /// </summary>
        public bool PrimeAsync(string targetId, double julianDate)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            // Work for this target is already under way, or its plan is already
            // waiting: either is the head start this ask wants.
            if (IsPrimePending(targetId)) return true;

            lock (_retimeLock)
            {
                if (IsPrimedFor(targetId) && IsWithinFreshnessWindow(_primedPreview))
                    return true;
            }

            var request = new PreviewRequest(PreviewKind.Prime, targetId,
                _state.WithJulianDate(julianDate), double.NaN, ArrivalStyle,
                BurnLevelG, _planner);
            return EnqueuePreview(request);
        }

        /// <summary>True while any preview build for this target is queued or
        /// building, whatever kind it is. The reveal path uses this to wait for
        /// the plan it asked for instead of queueing a plot over a rebuild.</summary>
        public bool IsPreviewPending(string targetId)
        {
            if (targetId == null) return false;

            lock (_retimeLock)
            {
                return IsPendingRequest(_pendingPreview, targetId)
                       || IsPendingRequest(_buildingPreview, targetId);
            }
        }

        /// <summary>True while a primer for this target is queued or building,
        /// so <see cref="PrimeAsync"/> does not solve the same route twice and
        /// a Plot click knows to wait for the plan it asked for.</summary>
        public bool IsPrimePending(string targetId) =>
            IsKindPending(PreviewKind.Prime, targetId);

        /// <summary>True when a primed plan for the target is waiting to be
        /// adopted. The prime is not exposed as data: this only lets a caller
        /// ask whether the head start is ready.</summary>
        public bool HasPrimedPreview(string targetId)
        {
            if (targetId == null) return false;

            lock (_retimeLock)
            {
                return _primedPreview != null
                       && _primedPreviewGeneration == _previewGeneration
                       && _primedPreview.FinalPlan != null
                       && _primedPreview.FinalPlan.TargetId == targetId;
            }
        }

        /// <summary>
        /// Turn a finished prime into the published preview, on the main thread.
        /// This is the Plot click's fast path: the plan was solved while the
        /// player read the screen, so the route and its window appear in the
        /// same frame. The primed plan is refused when it no longer describes
        /// what the controls ask for — another target, an infeasible plan, a
        /// changed arrival style or drive, or a departure the clock has moved
        /// past the preview's own freshness window — and the caller then asks
        /// for a fresh plot as it always did.
        /// </summary>
        public bool TryAdoptPrimedPreview(string targetId, out TrajectoryPlan plan)
        {
            plan = null;
            if (targetId == null) return false;

            CompletedPreview primed;
            lock (_retimeLock)
            {
                if (_primedPreview == null
                    || _primedPreviewGeneration != _previewGeneration) return false;

                // Another body's warmed plan is not this ask's; keeping it lets
                // the click that names its own body still use it.
                if (_primedPreview.FinalPlan == null
                    || _primedPreview.FinalPlan.TargetId != targetId) return false;

                primed = _primedPreview;
                _primedPreview = null;
            }

            TrajectoryPlan fastest = primed.FinalPlan;
            if (!fastest.IsFeasible) return false;
            if (primed.Arrival != ArrivalStyle) return false;

            double drive = _planner.MaxAccelerationMps2;
            if (Math.Abs(fastest.MaxAccelerationMps2 - drive) > 1e-9 * Math.Max(1.0, drive))
                return false;

            if (!IsWithinFreshnessWindow(primed)) return false;

            Preview = fastest;
            PreviewJulianDate = primed.DepartureJulianDate;
            PreviewMinimumSeconds = primed.WindowPlan.FlightTimeSeconds;
            PreviewMaximumSeconds = primed.MaximumSeconds;

            // The retime path builds from the pinned departure, exactly as it
            // does after a plot: adopting a plan without it would re-time a
            // zeroed ship state.
            _previewDeparture = primed.Departure;
            _lastPreviewRequestSeconds = fastest.FlightTimeSeconds;
            ArmPrefetch(primed);
            plan = fastest;
            return true;
        }

        /// <summary>Whether a finished background plan is still inside the
        /// preview's own freshness window: a plan older than that is a shape
        /// rather than a contract, so it must not become the preview a click
        /// asked for. The caller holds <see cref="_retimeLock"/>.</summary>
        private bool IsWithinFreshnessWindow(CompletedPreview completed)
        {
            if (completed?.FinalPlan == null) return false;

            double age = _state.JulianDate - completed.DepartureJulianDate;
            return age >= 0.0
                   && age <= PreviewStaleFraction * completed.FinalPlan.FlightTimeDays;
        }

        /// <summary>A request that is still wanted: it is the asked-for kind of
        /// build, it names the target, and its generation is the current one.
        /// The caller holds <see cref="_retimeLock"/>.</summary>
        /// <summary>A request for this target that is still wanted: it names the
        /// target and its generation is the current one. The caller holds
        /// <see cref="_retimeLock"/>.</summary>
        private bool IsPendingRequest(PreviewRequest request, string targetId) =>
            request != null
            && request.TargetId == targetId
            && request.Generation == _previewGeneration;

        /// <summary>A request of one kind for this target is queued or building.
        /// The caller must not hold <see cref="_retimeLock"/>.</summary>
        private bool IsKindPending(PreviewKind kind, string targetId)
        {
            if (targetId == null) return false;

            lock (_retimeLock)
            {
                return IsPendingRequest(_pendingPreview, kind, targetId)
                       || IsPendingRequest(_buildingPreview, kind, targetId);
            }
        }

        /// <summary>A request of one kind for this target that is still wanted.
        /// The caller holds <see cref="_retimeLock"/>.</summary>
        private bool IsPendingRequest(PreviewRequest request, PreviewKind kind, string targetId) =>
            request != null
            && request.Kind == kind
            && request.TargetId == targetId
            && request.Generation == _previewGeneration;

        /// <summary>A primed plan for the target is waiting. The caller holds
        /// <see cref="_retimeLock"/>.</summary>
        private bool IsPrimedFor(string targetId) =>
            _primedPreview != null
            && _primedPreviewGeneration == _previewGeneration
            && _primedPreview.FinalPlan != null
            && _primedPreview.FinalPlan.TargetId == targetId;

        /// <summary>
        /// Re-time the current preview without re-solving the minimum or moving
        /// the ship. One planner build at the requested duration, so the route,
        /// its delta-v all change while the arrival date slides.
        /// The value is clamped to the preview's feasible window; the departure
        /// state and displayed date stay pinned, which keeps a drag stable while
        /// the clock runs.
        ///
        /// A request that lands on the floor is the plan the window already
        /// holds, so it is returned as it is. A full-field build is the
        /// profile's most expensive call, and a caller asking for the floor
        /// after a level change (or dragging the handle to the left end of a
        /// window it just solved) would otherwise pay for the floor twice.
        /// </summary>
        public TrajectoryPlan SetPreviewDuration(double seconds)
        {
            if (Preview == null) return null;

            // A synchronous retime is the authoritative answer: drop any
            // outstanding background request so it cannot publish over this.
            CancelRetime();
            BeginPreviewReplacement();
            double minimum = PreviewMinimumSeconds;
            double duration = Math.Clamp(seconds, minimum, PreviewMaximumSeconds);
            _lastPreviewRequestSeconds = duration;

            if (TryGetCachedWindow(Preview.TargetId, _previewDeparture.JulianDate,
                    out PreviewWindow window)
                && Math.Abs(duration - window.Plan.FlightTimeSeconds)
                   <= 1e-9 * Math.Max(1.0, duration))
            {
                Preview = window.Plan;
                return Preview;
            }

            string targetId = Preview.TargetId;
            double julianDate = _previewDeparture.JulianDate;
            double burnLevelG = BurnLevelG;
            CaptureStyle arrival = ArrivalStyle;

            // A duration the player has already waited for is a dictionary
            // hit. This is the synchronous twin of the slider's cache path.
            if (TryGetCachedRetime(targetId, julianDate, burnLevelG, arrival, duration,
                    RetimeCacheTolerance, out TrajectoryPlan cached))
            {
                Preview = cached;
                return Preview;
            }

            Preview = _planner.PlanAtDuration(_previewDeparture, targetId, duration,
                RetimeSeed(targetId, julianDate, burnLevelG, arrival, duration,
                    minimum, PreviewMaximumSeconds));
            CacheRetime(targetId, julianDate, burnLevelG, arrival, duration, Preview);
            return Preview;
        }

        /// <summary>
        /// Ask for the preview to be re-timed on a background thread. This is
        /// the slider's path: it returns immediately, the newest request wins,
        /// and a plan that finishes after a newer ask arrived is discarded. The
        /// published preview is left in place until
        /// <see cref="TryApplyCompletedRetime"/> publishes the finished plan, so
        /// the route and report never fall back to the fast search model: every
        /// plan handed out is the full-field build.
        ///
        /// The request is clamped to the preview's window exactly as
        /// <see cref="SetPreviewDuration"/> clamps it, and the worker's planner
        /// copies the session planner's configuration, so the background plan is
        /// the same plan the synchronous path would build for that duration. A
        /// duration this window has already built — within a fraction of the
        /// slider's track — is published from the retime cache without queueing
        /// work. The accepted ask also queues three steps of prefetch in the
        /// direction it travelled: the drag is read from this ask against the
        /// previous one, so the HUD passes nothing extra. Returns false when
        /// there is no preview or the session has been disposed.
        /// </summary>
        public bool RequestPreviewDuration(double seconds)
        {
            if (Preview == null) return false;

            double duration = Math.Clamp(seconds, PreviewMinimumSeconds,
                PreviewMaximumSeconds);
            double previous = _lastPreviewRequestSeconds;

            // A request that lands on the floor is the plan the window already
            // solved. Hand it to the publisher directly instead of queueing a
            // build that would reproduce it: the build is the profile's most
            // expensive call, and on a near-floor needle the cold rebuild can
            // refuse the very duration the window advertises. The generation
            // bump discards any older request still in flight.
            if (TryGetCachedWindow(Preview.TargetId, _previewDeparture.JulianDate,
                    out PreviewWindow floorWindow)
                && Math.Abs(duration - floorWindow.Plan.FlightTimeSeconds)
                   <= 1e-9 * Math.Max(1.0, duration))
            {
                lock (_retimeLock)
                {
                    if (_retimeStop) return false;

                    _retimeGeneration++;
                    _pendingRetime = null;
                    _completedRetime = floorWindow.Plan;
                    _completedRetimeGeneration = _retimeGeneration;
                    _requestedPreviewSeconds = duration;
                    _lastPreviewRequestSeconds = duration;
                    BeginPreviewReplacement();
                }

                ScheduleDragPrefetch(duration, previous);
                return true;
            }

            string targetId = Preview.TargetId;
            double julianDate = _previewDeparture.JulianDate;
            double burnLevelG = BurnLevelG;
            CaptureStyle arrival = ArrivalStyle;

            // A duration the player has already waited for is answered at once:
            // the plan is in the retime cache, so a handle returned to an old
            // value must not queue another multi-second build behind it.
            if (TryGetCachedRetime(targetId, julianDate, burnLevelG, arrival, duration,
                    RetimeCacheTolerance, out TrajectoryPlan cached))
            {
                lock (_retimeLock)
                {
                    if (_retimeStop) return false;

                    _retimeGeneration++;
                    _retimeCacheHits++;
                    _pendingRetime = null;
                    _completedRetime = cached;
                    _completedRetimeGeneration = _retimeGeneration;
                    _requestedPreviewSeconds = duration;
                    _lastPreviewRequestSeconds = duration;
                    BeginPreviewReplacement();
                }

                ScheduleDragPrefetch(duration, previous);
                return true;
            }

            var request = new RetimeRequest(_previewDeparture, targetId, duration,
                arrival, burnLevelG, _planner,
                PreviewMinimumSeconds, PreviewMaximumSeconds,
                RetimeSeed(targetId, julianDate, burnLevelG, arrival, duration,
                    PreviewMinimumSeconds, PreviewMaximumSeconds));

            lock (_retimeLock)
            {
                if (_retimeStop) return false;

                _retimeGeneration++;
                request.Generation = _retimeGeneration;
                _pendingRetime = request;
                _completedRetime = null; // a newer ask supersedes a finished one
                _requestedPreviewSeconds = duration;
                _lastPreviewRequestSeconds = duration;
                Monitor.Pulse(_retimeLock);
                BeginPreviewReplacement();
            }

            ScheduleDragPrefetch(duration, previous);
            StartPlannerWorker();
            return true;
        }

        /// <summary>
        /// Publish a finished background retime, on the main thread. Returns
        /// false when nothing is waiting, when a newer request has superseded
        /// the finished plan, or when the preview has gone away since the build
        /// started; in every case the current preview is left untouched. The
        /// caller applies the returned plan to the route and the report.
        /// </summary>
        public bool TryApplyCompletedRetime(out TrajectoryPlan plan)
        {
            plan = null;

            lock (_retimeLock)
            {
                if (_completedRetime == null) return false;

                if (_completedRetimeGeneration != _retimeGeneration)
                {
                    _completedRetime = null;
                    return false;
                }

                plan = _completedRetime;
                _completedRetime = null;
            }

            if (Preview == null || Preview.TargetId != plan.TargetId) return false;

            Preview = plan;
            _requestedPreviewSeconds = plan.FlightTimeSeconds;
            return true;
        }

        /// <summary>True while a background retime is queued, building, or
        /// finished and waiting to be published. The slider uses it to keep the
        /// handle where the player put it instead of snapping back to the
        /// published plan's older duration.</summary>
        public bool IsRetimePending
        {
            get
            {
                lock (_retimeLock) return HasOutstandingRetimeLocked;
            }
        }

        /// <summary>The trip time the player last asked for while a background
        /// retime is outstanding, or NaN when none is. The published preview is
        /// the last full-field plan; this is the newer ask that is still being
        /// built.</summary>
        public double PendingPreviewSeconds
        {
            get
            {
                lock (_retimeLock)
                    return HasOutstandingRetimeLocked
                        ? _requestedPreviewSeconds
                        : double.NaN;
            }
        }

        /// <summary>True while there is an ask that will still be published: a
        /// queued or completed request, or a build in flight, and a requested
        /// duration that has not been cancelled. A stale build that is still
        /// unwinding after a discard is not outstanding.</summary>
        private bool HasOutstandingRetimeLocked =>
            !double.IsNaN(_requestedPreviewSeconds) &&
            (_pendingRetime != null || _completedRetime != null || _retimeBusy);

        /// <summary>True while the background planner is building any request
        /// (a retime or a preview). A finished but still-unpublished plan is
        /// not building; see <see cref="IsRetimePending"/>. A prefetch is not a
        /// request: it is reported by <see cref="IsPrefetchBuilding"/>.</summary>
        public bool IsRetimeBuilding
        {
            get
            {
                lock (_retimeLock) return _retimeBusy;
            }
        }

        /// <summary>True while a prefetched neighbour is building on the
        /// worker.</summary>
        internal bool IsPrefetchBuilding
        {
            get
            {
                lock (_retimeLock) return _prefetchBuilding;
            }
        }

        /// <summary>True while prefetched neighbours are queued or building. The
        /// benchmark waits for this to clear so a drag starts from a quiet
        /// planner.</summary>
        internal bool IsPrefetchPending
        {
            get
            {
                lock (_retimeLock) return _prefetchBuilding || _pendingPrefetch.Count > 0;
            }
        }

        /// <summary>How many neighbours are waiting on the prefetch queue.</summary>
        internal int QueuedPrefetchCount
        {
            get
            {
                lock (_retimeLock) return _pendingPrefetch.Count;
            }
        }

        /// <summary>The durations waiting on the prefetch queue. A diagnostic
        /// for the tests, which prove a real request replaces a stale queue with
        /// its own direction's steps.</summary>
        internal double[] QueuedPrefetchSeconds()
        {
            lock (_retimeLock)
            {
                var seconds = new double[_pendingPrefetch.Count];
                for (int i = 0; i < seconds.Length; i++)
                    seconds[i] = _pendingPrefetch[i].Parameters.DurationSeconds;
                return seconds;
            }
        }

        /// <summary>Retimes the worker built. A request answered from the cache
        /// is counted by <see cref="RetimeCacheHits"/> instead, so the two sum to
        /// the asks that reached the worker's publisher.</summary>
        internal int RetimeBuilds
        {
            get
            {
                lock (_retimeLock) return _retimeBuilds;
            }
        }

        /// <summary>Asks answered from the retime cache, either at request time
        /// or when the worker picked a queued ask a prefetch had already
        /// filled.</summary>
        internal int RetimeCacheHits
        {
            get
            {
                lock (_retimeLock) return _retimeCacheHits;
            }
        }

        /// <summary>Neighbouring plans the prefetch filled.</summary>
        internal int PrefetchBuilds
        {
            get
            {
                lock (_retimeLock) return _prefetchBuilds;
            }
        }

        /// <summary>True when the published preview's window has a plan at (or
        /// within the cache's tolerance of) a duration. A diagnostic for the
        /// tests and the benchmark; it decides nothing.</summary>
        internal bool HasCachedPlanAt(double seconds)
        {
            string targetId;
            double julianDate;
            double burnLevelG;
            CaptureStyle arrival;
            double tolerance;

            if (_prefetchArmed)
            {
                targetId = _prefetchTargetId;
                julianDate = _prefetchDeparture.JulianDate;
                burnLevelG = _prefetchBurnLevelG;
                arrival = _prefetchArrival;
                tolerance = CacheTolerance(_prefetchMinimum, _prefetchMaximum);
            }
            else if (Preview != null)
            {
                targetId = Preview.TargetId;
                julianDate = _previewDeparture.JulianDate;
                burnLevelG = BurnLevelG;
                arrival = ArrivalStyle;
                tolerance = RetimeCacheTolerance;
            }
            else
            {
                return false;
            }

            return TryGetCachedRetime(targetId, julianDate, burnLevelG, arrival,
                seconds, tolerance, out _);
        }

        /// <summary>True while the background planner thread is alive. A
        /// diagnostic for the lifecycle test: a disposed session reports false
        /// once the thread has stopped, and a session that never asked for
        /// background work reports false.</summary>
        public bool HasRetimeWorker
        {
            get
            {
                lock (_retimeLock)
                    return _plannerThread != null && _plannerThread.IsAlive;
            }
        }

        /// <summary>Take the background retime's last failure, if any, and clear
        /// it. The worker cannot log; the main thread does. A failure discards
        /// the plan and leaves the published preview alone.</summary>
        public string TakeRetimeFailure()
        {
            lock (_retimeLock)
            {
                string failure = _retimeFailure;
                _retimeFailure = null;
                return failure;
            }
        }

        /// <summary>
        /// Publish a finished background preview, on the main thread. Returns
        /// false when nothing is waiting, when a newer request has superseded
        /// the finished plan, or when the preview was discarded since the
        /// build started; the current preview is left untouched. The caller
        /// applies the returned plan to the route and the arrival ghost. A
        /// failed build never publishes:
        /// <see cref="TakePreviewFailure"/> carries the reason and keeps the
        /// published preview in place.
        /// </summary>
        public bool TryApplyCompletedPreview(out TrajectoryPlan plan)
        {
            plan = null;

            CompletedPreview completed;
            lock (_retimeLock)
            {
                if (_completedPreview == null) return false;

                if (_completedPreviewGeneration != _previewGeneration)
                {
                    _completedPreview = null;
                    return false;
                }

                completed = _completedPreview;
                _completedPreview = null;
            }

            Preview = completed.FinalPlan;
            PreviewJulianDate = completed.DepartureJulianDate;
            PreviewMinimumSeconds = completed.WindowPlan.FlightTimeSeconds;
            PreviewMaximumSeconds = completed.MaximumSeconds;
            plan = completed.FinalPlan;

            ArmPrefetch(completed);
            return true;
        }

        /// <summary>Take the background preview build's last failure, if any,
        /// and clear it. The worker cannot log; the main thread does. A failed
        /// build publishes nothing: <see cref="TryApplyCompletedPreview"/>
        /// keeps the previous preview in place.</summary>
        public string TakePreviewFailure()
        {
            lock (_retimeLock)
            {
                string failure = _previewFailure;
                _previewFailure = null;
                return failure;
            }
        }

        /// <summary>Drop a queued, built, or in-flight background retime. Called
        /// when the preview is replaced or discarded: a plan for an older
        /// preview must never be published over the new one.</summary>
        private void CancelRetime()
        {
            lock (_retimeLock)
            {
                _retimeGeneration++;
                _pendingRetime = null;
                _completedRetime = null;
                _requestedPreviewSeconds = double.NaN;
                _pendingPrefetch.Clear();
            }
        }

        /// <summary>Invalidate every background preview result: a new preview
        /// replaces what a caller asked for, so a plan that lands later must
        /// never publish over it. Queued prefetch work belongs to the preview
        /// being replaced and is dropped with it.</summary>
        private void BeginPreviewReplacement()
        {
            lock (_retimeLock)
            {
                _previewGeneration++;
                _pendingPreview = null;
                _completedPreview = null;
                _primedPreview = null;
                _pendingPrefetch.Clear();
            }
        }

        /// <summary>A synchronous plot or a discard ends the preview the
        /// prefetch context describes.</summary>
        private void DisarmPrefetch()
        {
            _prefetchArmed = false;
            _prefetchTargetId = null;
            _prefetchDeparture = default;
            _prefetchBurnLevelG = 0.0;
            _prefetchArrival = default;
            _prefetchMinimum = double.NaN;
            _prefetchMaximum = double.NaN;
            _lastPreviewRequestSeconds = double.NaN;
            lock (_retimeLock) _pendingPrefetch.Clear();
        }

        /// <summary>False when a caller is timing a drag with no prefetch at all.
        /// A diagnostic switch for the route benchmark's without-prefetch
        /// comparison, not a player setting: the scene never clears it.</summary>
        internal bool PrefetchEnabled { get; set; } = true;

        /// <summary>Capture the published preview's own inputs and queue two
        /// steps each side of its duration. The preview starts at the window
        /// minimum, so a fresh plot's lower two clamp away by design. The
        /// queued work is speculative and is dropped by the next real
        /// request.</summary>
        private void ArmPrefetch(CompletedPreview completed)
        {
            if (!PrefetchEnabled) return;

            TrajectoryPlan plan = completed.FinalPlan;
            _prefetchArmed = plan != null && plan.IsFeasible;
            _prefetchTargetId = plan?.TargetId;
            _prefetchDeparture = completed.Departure;
            _prefetchBurnLevelG = completed.BurnLevelG;
            _prefetchArrival = completed.Arrival;
            _prefetchMinimum = completed.WindowPlan.FlightTimeSeconds;
            _prefetchMaximum = completed.MaximumSeconds;
            _lastPreviewRequestSeconds = plan?.FlightTimeSeconds ?? double.NaN;

            if (!_prefetchArmed) return;
            SchedulePrefetch(_lastPreviewRequestSeconds, PlotPrefetchSteps,
                PlotPrefetchSteps);
        }

        /// <summary>Queue three steps in the direction the handle travelled.
        /// The direction is the sign of this request's step index against the
        /// last accepted ask's; a repeated ask has no direction and queues
        /// nothing.</summary>
        private void ScheduleDragPrefetch(double seconds, double previousSeconds)
        {
            if (!_prefetchArmed || double.IsNaN(previousSeconds)) return;

            double step = PrefetchStepSeconds();
            if (!double.IsFinite(step)) return;

            int index = PrefetchIndex(seconds);
            int previous = PrefetchIndex(previousSeconds);
            int direction = Math.Sign(index - previous);
            if (direction == 0) return;

            if (direction > 0) SchedulePrefetch(seconds, 0, DragPrefetchSteps);
            else SchedulePrefetch(seconds, DragPrefetchSteps, 0);
        }

        /// <summary>Queue the slider steps within <paramref name="below"/> and
        /// <paramref name="above"/> of a duration's own step. A step outside the
        /// window is clamped away rather than faked: the minimum and the maximum
        /// are edges the planner does not promise past.</summary>
        private void SchedulePrefetch(double centreSeconds, int below, int above)
        {
            if (!_prefetchArmed) return;

            double step = PrefetchStepSeconds();
            int index = PrefetchIndex(centreSeconds);
            if (!double.IsFinite(step) || index < 0) return;

            var durations = new List<double>(below + above);
            for (int i = 1; i <= below; i++)
            {
                if (index - i >= 0)
                    durations.Add(_prefetchMinimum + (index - i) * step);
            }

            for (int i = 1; i <= above; i++)
            {
                if (index + i <= SliderStepCount - 1)
                    durations.Add(_prefetchMinimum + (index + i) * step);
            }

            EnqueuePrefetch(durations);
        }

        /// <summary>Add the durations this window has not cached or queued to
        /// the prefetch queue and wake the worker. A slot already filled is
        /// skipped: the point is to keep the planner idle, not to rebuild what
        /// it built.</summary>
        private void EnqueuePrefetch(List<double> durations)
        {
            if (!_prefetchArmed || durations == null || durations.Count == 0) return;

            double tolerance = CacheTolerance(_prefetchMinimum, _prefetchMaximum);
            var wanted = new List<double>(durations.Count);
            for (int i = 0; i < durations.Count; i++)
            {
                if (TryGetCachedRetime(_prefetchTargetId, _prefetchDeparture.JulianDate,
                        _prefetchBurnLevelG, _prefetchArrival, durations[i],
                        tolerance, out _))
                    continue;
                wanted.Add(durations[i]);
            }

            if (wanted.Count == 0) return;

            bool added = false;
            lock (_retimeLock)
            {
                if (_retimeStop) return;

                for (int i = 0; i < wanted.Count; i++)
                {
                    if (IsQueuedPrefetchLocked(wanted[i])) continue;

                    _pendingPrefetch.Add(new PrefetchRequest(
                        new RetimeRequest(_prefetchDeparture, _prefetchTargetId,
                            wanted[i], _prefetchArrival, _prefetchBurnLevelG, _planner,
                            _prefetchMinimum, _prefetchMaximum),
                        _prefetchMinimum, _prefetchMaximum));
                    added = true;
                }

                if (added) Monitor.Pulse(_retimeLock);
            }

            if (added) StartPlannerWorker();
        }

        private bool IsQueuedPrefetchLocked(double seconds)
        {
            for (int i = 0; i < _pendingPrefetch.Count; i++)
            {
                if (Math.Abs(_pendingPrefetch[i].Parameters.DurationSeconds - seconds)
                    <= 1e-6)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A slider step in the captured window's seconds, or NaN.</summary>
        private double PrefetchStepSeconds()
        {
            double span = _prefetchMaximum - _prefetchMinimum;
            if (!double.IsFinite(span) || !(span > 0.0)) return double.NaN;
            return span / (SliderStepCount - 1);
        }

        /// <summary>The slider step a duration is nearest, clamped to the
        /// control's ends, or -1 when the captured window is unusable.</summary>
        private int PrefetchIndex(double seconds)
        {
            double step = PrefetchStepSeconds();
            if (!double.IsFinite(step) || !(step > 0.0)) return -1;
            if (!double.IsFinite(seconds)) return -1;

            double index = Math.Round((seconds - _prefetchMinimum) / step);
            return (int)Math.Clamp(index, 0.0, SliderStepCount - 1);
        }

        /// <summary>Queue a background preview build, superseding outstanding
        /// retimes and any other preview build: the newest ask wins, so both
        /// pipelines bump the other's generation here. A retime or a rebuild
        /// makes an in-flight build stale; a new plot makes in-flight retimes
        /// stale — matching today's synchronous semantics where the last call
        /// before a publish owns the preview.</summary>
        private bool EnqueuePreview(PreviewRequest request)
        {
            // A new preview has its own departure state, burn level, and arrival
            // style; the old context's neighbours are not slots it can read.
            DisarmPrefetch();

            lock (_retimeLock)
            {
                if (_retimeStop) return false;

                _retimeGeneration++;
                _pendingRetime = null;
                _completedRetime = null;
                _requestedPreviewSeconds = double.NaN;
                _previewGeneration++;
                request.Generation = _previewGeneration;
                _pendingPreview = request;
                _completedPreview = null;

                // A primed plan belongs to the preview that is being replaced:
                // it was solved against the old date, target, or style.
                _primedPreview = null;
                Monitor.Pulse(_retimeLock);
            }
            StartPlannerWorker();
            return true;
        }

        private void StartPlannerWorker()
        {
            lock (_retimeLock)
            {
                if (_retimeStop) return;
                if (_plannerThread != null && _plannerThread.IsAlive) return;

                // The worker's own field: GravityField keeps a prepared active
                // set, so it must not be the instance the flight integrator uses.
                if (_workerPlanner == null)
                    _workerPlanner = new TripPlanner(_model, new GravityField(_model));

                _plannerThread = new Thread(PlannerLoop)
                {
                    IsBackground = true,
                    Name = "TimeToExplore preview planner"
                };
                _plannerThread.Start();
            }
        }

        /// <summary>
        /// The worker: wait for a request, build it, and publish only if no newer
        /// request has arrived. A superseded build is dropped and the newest
        /// request is taken next, so a drag never queues work behind stale plans.
        /// A retime is the newest ask when both pipelines are queued, so it
        /// builds first; a preview still waits behind it. **Prefetch is last**:
        /// one item is taken only when no retime or preview is queued, so a real
        /// request arriving between items abandons the rest of the queue.
        /// </summary>
        private void PlannerLoop()
        {
            while (true)
            {
                RetimeRequest retime = null;
                PreviewRequest preview = null;
                PrefetchRequest prefetch = null;
                lock (_retimeLock)
                {
                    while (!_retimeStop && _pendingRetime == null && _pendingPreview == null
                           && _pendingPrefetch.Count == 0)
                        Monitor.Wait(_retimeLock);

                    if (_retimeStop) return;

                    if (_pendingRetime != null)
                    {
                        retime = _pendingRetime;
                        _pendingRetime = null;
                    }
                    else if (_pendingPreview != null)
                    {
                        preview = _pendingPreview;
                        _pendingPreview = null;
                        _buildingPreview = preview;
                    }
                    else
                    {
                        prefetch = _pendingPrefetch[0];
                        _pendingPrefetch.RemoveAt(0);
                        _prefetchBuilding = true;
                    }

                    _retimeBusy = retime != null || preview != null;
                }

                TrajectoryPlan retimePlan = null;
                CompletedPreview previewResult = null;
                string failure = null;
                try
                {
                    if (retime != null)
                        retimePlan = BuildRetime(retime);
                    else if (preview != null)
                        previewResult = preview.Kind == PreviewKind.Rebuild
                            ? BuildRebuild(preview)
                            : BuildPlotWindow(preview);
                    else if (prefetch != null)
                        BuildPrefetch(prefetch);
                }
                catch (Exception error)
                {
                    // Never let an exception kill the worker: a dead thread
                    // would leave every later request waiting for ever. A
                    // prefetch is speculative, so its failure is dropped
                    // silently rather than reported as the player's plan.
                    if (prefetch == null) failure = error.Message;
                }

                lock (_retimeLock)
                {
                    _retimeBusy = false;
                    _prefetchBuilding = false;
                    _buildingPreview = null;
                    if (_retimeStop) return;

                    if (failure != null)
                    {
                        // A prime is a head start, not the player's ask: a
                        // failure is dropped, and the Plot click reports its own
                        // if it has to build the plan itself.
                        if (preview != null && preview.Generation == _previewGeneration
                            && preview.Kind != PreviewKind.Prime)
                            _previewFailure = failure;
                        else if (retime != null && retime.Generation == _retimeGeneration)
                            _retimeFailure = failure;
                        continue;
                    }

                    if (retime != null && retime.Generation == _retimeGeneration)
                    {
                        _completedRetime = retimePlan;
                        _completedRetimeGeneration = retime.Generation;
                    }
                    else if (preview != null && preview.Generation == _previewGeneration
                             && preview.Kind == PreviewKind.Prime)
                    {
                        _primedPreview = previewResult;
                        _primedPreviewGeneration = preview.Generation;
                    }
                    else if (preview != null && preview.Generation == _previewGeneration)
                    {
                        _completedPreview = previewResult;
                        _completedPreviewGeneration = preview.Generation;
                    }

                    // A prefetch publishes nothing: its plan is already in the
                    // cache for whichever ask arrives next.
                }
            }
        }

        /// <summary>One background retime build. Runs on the worker thread
        /// only: the planner and its gravity field belong to that thread for
        /// the session's life. A duration a prefetch filled while the ask was
        /// queued is answered from the cache rather than built again.</summary>
        private TrajectoryPlan BuildRetime(RetimeRequest request)
        {
            if (TryGetCachedRetime(request.TargetId, request.Departure.JulianDate,
                    request.BurnLevelG, request.Arrival, request.DurationSeconds,
                    CacheTolerance(request.MinimumSeconds, request.MaximumSeconds),
                    out TrajectoryPlan cached))
            {
                lock (_retimeLock) _retimeCacheHits++;
                return cached;
            }

            request.ApplyTo(_workerPlanner);
            lock (_retimeLock) _retimeBuilds++;
            TrajectoryPlan plan = _workerPlanner.PlanAtDuration(request.Departure,
                request.TargetId, request.DurationSeconds, request.Seed);

            // Cache even a superseded build: the plan is still the right answer
            // for this window and duration, and it is exactly the work a
            // returned slider handle is asking to skip.
            CacheRetime(request.TargetId, request.Departure.JulianDate,
                request.BurnLevelG, request.Arrival, request.DurationSeconds, plan);
            return plan;
        }

        /// <summary>One prefetched neighbour. It fills the retime cache and
        /// nothing else: it never publishes, never bumps a generation, and is
        /// seeded from the nearest plan the cache already holds so it costs the
        /// warm build rather than the cold one. A duration in the short-coast
        /// gap that the planner refuses is not cached, so a later ask for it
        /// builds and reports the refusal exactly as it does today.</summary>
        private void BuildPrefetch(PrefetchRequest request)
        {
            RetimeRequest parameters = request.Parameters;
            double tolerance = CacheTolerance(request.MinimumSeconds,
                request.MaximumSeconds);

            if (TryGetCachedRetime(parameters.TargetId, parameters.Departure.JulianDate,
                    parameters.BurnLevelG, parameters.Arrival, parameters.DurationSeconds,
                    tolerance, out _))
                return;

            parameters.ApplyTo(_workerPlanner);
            PinSeed seed = RetimeSeed(parameters.TargetId,
                parameters.Departure.JulianDate, parameters.BurnLevelG,
                parameters.Arrival, parameters.DurationSeconds,
                request.MinimumSeconds, request.MaximumSeconds);
            TrajectoryPlan plan = _workerPlanner.PlanAtDuration(parameters.Departure,
                parameters.TargetId, parameters.DurationSeconds, seed);
            if (plan == null || !plan.IsFeasible) return;

            CacheRetime(parameters.TargetId, parameters.Departure.JulianDate,
                parameters.BurnLevelG, parameters.Arrival, parameters.DurationSeconds,
                plan);
            lock (_retimeLock) _prefetchBuilds++;
        }

        /// <summary>One background Plot build: the window for the request's
        /// target at the request's departure, on the worker's own planner. A
        /// repeat of a window the session already solved returns the cached
        /// plan without paying for another build.</summary>
        private CompletedPreview BuildPlotWindow(PreviewRequest request)
        {
            request.ApplyTo(_workerPlanner);
            PreviewWindow window = WindowFor(request.TargetId, request.Departure,
                _workerPlanner, request.BurnLevelG, request.Arrival);
            return new CompletedPreview(window.Plan, window.Plan,
                window.MaximumSeconds, request.Departure, request.BurnLevelG,
                request.Arrival);
        }

        /// <summary>One background Rebuild build: the window, then the
        /// requested duration when the window's fastest plan is slower than
        /// the player's ask. Mirrors the old synchronous rebuild semantics:
        /// the window plan's duration is the published minimum; a kept flight
        /// time re-solves to it when the new window contains it. A rebuild that
        /// is keeping the minimum itself stays on the new window's minimum,
        /// because the two styles' burn-only totals can differ by seconds and
        /// the old number would fall into the zero-coast gap.</summary>
        private CompletedPreview BuildRebuild(PreviewRequest request)
        {
            request.ApplyTo(_workerPlanner);
            PreviewWindow window = WindowFor(request.TargetId, request.Departure,
                _workerPlanner, request.BurnLevelG, request.Arrival);
            TrajectoryPlan plan = window.Plan;
            if (!request.AtMinimum && plan.IsFeasible
                && request.DurationSeconds > plan.FlightTimeSeconds)
            {
                double duration = Math.Min(request.DurationSeconds, window.MaximumSeconds);
                plan = _workerPlanner.PlanAtDuration(request.Departure,
                    request.TargetId, duration,
                    RetimeSeed(request.TargetId, request.Departure.JulianDate,
                        request.BurnLevelG, request.Arrival, duration,
                        window.Plan.FlightTimeSeconds, window.MaximumSeconds));
                CacheRetime(request.TargetId, request.Departure.JulianDate,
                    request.BurnLevelG, request.Arrival, duration, plan);
            }
            return new CompletedPreview(plan, window.Plan,
                window.MaximumSeconds, request.Departure, request.BurnLevelG,
                request.Arrival);
        }

        /// <summary>Stop and join the retime worker. Safe to call more than once
        /// and from a session that never started one. After this, a retime
        /// request is refused rather than starting a new thread.</summary>
        public void Dispose()
        {
            Thread thread;
            lock (_retimeLock)
            {
                _retimeStop = true;
                _retimeGeneration++;
                _pendingRetime = null;
                _completedRetime = null;
                _requestedPreviewSeconds = double.NaN;
                _pendingPreview = null;
                _buildingPreview = null;
                _primedPreview = null;
                _completedPreview = null;
                _pendingPrefetch.Clear();
                _prefetchBuilding = false;
                _windows.Clear();
                _retimedPlans.Clear();
                Monitor.PulseAll(_retimeLock);
                thread = _plannerThread;
            }

            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(RetimeJoinMilliseconds);

            lock (_retimeLock)
            {
                if (_plannerThread == thread) _plannerThread = null;
            }
        }

        /// <summary>
        /// Choose the arrival style and rebuild the preview against it, from the
        /// same pinned departure state. The fastest feasible plan of the new
        /// style sets the trip-time window; the previous flight time is kept
        /// when it falls inside that window, so the choice changes the arrival
        /// shape rather than silently throwing away the player's trip time. A
        /// live flight is never touched: it keeps the plan it was begun with.
        /// Returns the current preview at once; the rebuilt plan arrives
        /// through <see cref="TryApplyCompletedPreview"/> and the HUD's
        /// per-frame pump.
        /// </summary>
        public TrajectoryPlan SetArrivalStyle(CaptureStyle style)
        {
            if (style == ArrivalStyle) return Preview;
            ArrivalStyle = style;
            RequestRebuild();
            return Preview;
        }

        /// <summary>
        /// Choose the fixed-burn level and rebuild a preview that uses it from
        /// the pinned departure state. The profile's departure burn, brake, and
        /// hybrid capture all plan at this level. The level is rounded to 0.05 g
        /// so a slider drag does not re-solve the whole route at every pixel. A
        /// live flight keeps the plan it was begun with. Returns the current
        /// preview at once; the rebuilt plan arrives through
        /// <see cref="TryApplyCompletedPreview"/> and the HUD's per-frame pump.
        /// </summary>
        public TrajectoryPlan SetBurnLevel(double levelG)
        {
            levelG = Math.Clamp(levelG, TripPlanner.MinimumBurnLevelG,
                TripPlanner.MaxBurnLevelG);
            levelG = Math.Round(levelG * 20.0) / 20.0;
            if (Math.Abs(levelG - BurnLevelG) < 1e-9) return Preview;

            BurnLevelG = levelG;
            if (Preview == null) return Preview;

            RequestRebuild();
            return Preview;
        }

        /// <summary>Queue a background rebuild of the published preview
        /// against the session's current style and burn level, keeping the
        /// player's flight time when the new window contains it. The rebuild is
        /// the route-updating half of the preview lifecycle: the worker builds it
        /// with its own planner, and the previous plan stays published until the
        /// new one is ready. Outstanding retimes and other preview builds are
        /// superseded, as in <see cref="EnqueuePreview"/>.</summary>
        private void RequestRebuild()
        {
            if (Preview == null) return;

            string targetId = Preview.TargetId;
            double requested = Preview.FlightTimeSeconds;
            bool atMinimum = Math.Abs(requested - PreviewMinimumSeconds)
                             <= 1e-9 * Math.Max(1.0, PreviewMinimumSeconds);
            var request = new PreviewRequest(PreviewKind.Rebuild, targetId, _previewDeparture,
                requested, ArrivalStyle, BurnLevelG, _planner, atMinimum);
            EnqueuePreview(request);
        }

        /// <summary>
        /// The fastest plan and the trip-time window for a target, at the given
        /// pinned departure state. The minimum is the planner's
        /// <see cref="TripPlanner.PlanFastest"/> — the zero-coast burn-only
        /// crossing when it is the shorter feasible plan, otherwise the coast
        /// floor — and the upper end is still probed with
        /// <see cref="TripPlanner.PlanAtDuration"/>. Runs on the caller's thread:
        /// the session's synchronous <see cref="Plot"/> and the background
        /// worker's <see cref="BuildPlotWindow"/> and <see cref="BuildRebuild"/>.
        /// The cache is shared between threads, so every access is under
        /// <see cref="_retimeLock"/>; the slow planner calls run outside it, on
        /// the worker for async builds.
        ///
        /// The range between a burn-only minimum and the coast floor is a known
        /// gap: <see cref="TripPlanner.PlanAtDuration"/> cannot build those
        /// durations yet, so a retime that lands there comes back stretched or
        /// infeasible and the panel reports it. The window does not fake a plan
        /// for the gap.
        /// </summary>
        private PreviewWindow WindowFor(string targetId, ShipState departure,
            TripPlanner planner, double burnLevelG, CaptureStyle arrival)
        {
            double julianDate = departure.JulianDate;
            var key = new WindowKey(julianDate, targetId, burnLevelG, arrival);

            lock (_retimeLock)
            {
                if (double.IsNaN(_windowEpoch) || _windowEpoch != julianDate)
                {
                    _windows.Clear();
                    _retimedPlans.Clear();
                    _windowEpoch = julianDate;
                }
                if (_windows.TryGetValue(key, out PreviewWindow cached)) return cached;
            }

            TrajectoryPlan plan = planner.PlanFastest(departure, targetId);
            var window = new PreviewWindow(plan,
                MaximumPreviewSeconds(targetId, plan.FlightTimeSeconds, planner,
                    departure),
                plan?.PinSeed ?? default);

            lock (_retimeLock)
            {
                if (_windows.Count >= MaxCachedWindows)
                {
                    _windows.Clear();
                    _retimedPlans.Clear();
                }
                _windows[key] = window;
            }
            return window;
        }

        /// <summary>
        /// Look up a window this session already solved, without solving one. A
        /// retime to the floor uses this to return the floor plan the window
        /// holds instead of rebuilding it. A miss is cheap and means the caller
        /// must build.
        /// </summary>
        private bool TryGetCachedWindow(string targetId, double julianDate,
            out PreviewWindow window)
        {
            window = default;
            if (double.IsNaN(_windowEpoch) || _windowEpoch != julianDate) return false;

            var key = new WindowKey(julianDate, targetId, BurnLevelG, ArrivalStyle);
            lock (_retimeLock) return _windows.TryGetValue(key, out window);
        }

        /// <summary>How close a repeat request must be to a cached retime to
        /// reuse it. It is a fraction of the preview's own window, because the
        /// slider's range is the window: a fifth of a percent is under a pixel
        /// of the track. A preview without a finite window matches to the
        /// second.</summary>
        private double RetimeCacheTolerance
        {
            get => CacheTolerance(PreviewMinimumSeconds, PreviewMaximumSeconds);
        }

        /// <summary>The cache's match tolerance for any window, so the worker
        /// can use a request's captured window instead of the published one
        /// without reading the main thread's fields.</summary>
        private static double CacheTolerance(double minimum, double maximum)
        {
            double span = maximum - minimum;
            return double.IsFinite(span) && span > 0.0
                ? Math.Max(1.0, RetimeCacheFraction * span)
                : 1.0;
        }

        /// <summary>
        /// Look up a plan this session already built at (or near) a requested
        /// duration. The window inputs are part of the key and the cache is
        /// shared with the worker, so every access is under
        /// <see cref="_retimeLock"/>. A miss is cheap and means the caller must
        /// build; a hit is the whole point of the retime cache.
        /// </summary>
        private bool TryGetCachedRetime(string targetId, double julianDate,
            double burnLevelG, CaptureStyle arrival, double seconds,
            double tolerance, out TrajectoryPlan plan)
        {
            plan = null;
            var key = new WindowKey(julianDate, targetId, burnLevelG, arrival);

            lock (_retimeLock)
            {
                if (double.IsNaN(_windowEpoch) || _windowEpoch != julianDate)
                    return false;
                if (!_retimedPlans.TryGetValue(key, out List<CachedRetime> entries))
                    return false;

                for (int i = 0; i < entries.Count; i++)
                {
                    if (Math.Abs(entries[i].RequestedSeconds - seconds) <= tolerance)
                    {
                        plan = entries[i].Plan;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>How many slider steps at the bottom of the window are never
        /// seeded, and the control's step count. The two are the same numbers the
        /// HUD's <c>TimeSliderSteps</c> uses; a test pins them together. The
        /// window's minimum is where the burn-only crossing and the short coast
        /// meet the coast profile, and a seed from another family would start the
        /// build in the wrong geometry.</summary>
        internal const int SeedExcludedSliderSteps = 2;
        internal const int SliderStepCount = 64;

        /// <summary>The fraction of the window covered by the two lowest
        /// slider steps, which are never seeded. Public so the test that pins it
        /// to the HUD's own step count can read it.</summary>
        public const double SeedExcludedLowFraction =
            SeedExcludedSliderSteps / (double)(SliderStepCount - 1);

        /// <summary>
        /// The pin a retime build at <paramref name="seconds"/> should start
        /// from: the nearest cached retime's, else the window's floor plan's,
        /// else the cold start. The two lowest slider steps are not seeded,
        /// because that is where the build's geometry changes family.
        /// </summary>
        private PinSeed RetimeSeed(string targetId, double julianDate,
            double burnLevelG, CaptureStyle arrival, double seconds,
            double minimum, double maximum)
        {
            if (!(minimum > 0.0) || !double.IsFinite(minimum)) return default;
            double span = maximum - minimum;
            if (double.IsFinite(span) && span > 0.0
                && seconds - minimum < SeedExcludedLowFraction * span)
            {
                return default;
            }

            if (TryGetNearestRetimeSeed(targetId, julianDate, burnLevelG, arrival,
                    seconds, out PinSeed nearest))
            {
                return nearest.Nearby(seconds);
            }

            if (TryGetCachedWindow(targetId, julianDate, out PreviewWindow window))
                return window.FloorSeed.Nearby(seconds);

            return default;
        }

        /// <summary>The cached retime closest to a requested duration, if any of
        /// them can start it. The cache already holds the window's solved trips,
        /// so picking the nearest is its job; a miss means the caller starts
        /// cold.</summary>
        private bool TryGetNearestRetimeSeed(string targetId, double julianDate,
            double burnLevelG, CaptureStyle arrival, double seconds, out PinSeed seed)
        {
            seed = default;
            var key = new WindowKey(julianDate, targetId, burnLevelG, arrival);

            lock (_retimeLock)
            {
                if (double.IsNaN(_windowEpoch) || _windowEpoch != julianDate)
                    return false;
                if (!_retimedPlans.TryGetValue(key, out List<CachedRetime> entries))
                    return false;

                double best = double.PositiveInfinity;
                for (int i = 0; i < entries.Count; i++)
                {
                    PinSeed candidate = entries[i].Seed;
                    if (!candidate.Covers(seconds)) continue;

                    double gap = Math.Abs(entries[i].RequestedSeconds - seconds);
                    if (gap >= best) continue;

                    best = gap;
                    seed = candidate;
                }
            }

            return seed.IsValid;
        }

        /// <summary>
        /// Keep a finished plan for the window and duration that produced it, on
        /// whichever thread built it. The oldest entry of a full window is
        /// dropped first. A plan for a window the session has since left is
        /// refused, so a superseded worker cannot repopulate a dropped date.
        /// </summary>
        private void CacheRetime(string targetId, double julianDate,
            double burnLevelG, CaptureStyle arrival, double requestedSeconds,
            TrajectoryPlan plan)
        {
            if (plan == null) return;
            PinSeed seed = plan.PinSeed;

            var key = new WindowKey(julianDate, targetId, burnLevelG, arrival);
            lock (_retimeLock)
            {
                if (_retimeStop) return;
                if (double.IsNaN(_windowEpoch) || _windowEpoch != julianDate)
                    return;

                if (!_retimedPlans.TryGetValue(key, out List<CachedRetime> entries))
                {
                    entries = new List<CachedRetime>();
                    _retimedPlans[key] = entries;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    if (Math.Abs(entries[i].RequestedSeconds - requestedSeconds) <= 1e-6)
                    {
                        entries[i] = new CachedRetime(requestedSeconds, plan, seed);
                        return;
                    }
                }

                if (entries.Count >= MaxCachedRetimesPerWindow) entries.RemoveAt(0);
                entries.Add(new CachedRetime(requestedSeconds, plan, seed));
            }
        }

        /// <summary>Drop every cached preview window and retimed plan. Called
        /// when the ship's pinned departure state is replaced, so a later plot
        /// cannot reuse a plan that belonged to another state.</summary>
        private void ClearWindows()
        {
            DisarmPrefetch();
            lock (_retimeLock)
            {
                _windows.Clear();
                _retimedPlans.Clear();
                _windowEpoch = double.NaN;
            }
        }

        /// <summary>Forget the preview, if there is one, and drop any background
        /// planning that belonged to it: a discarded preview is never
        /// published over a new one.</summary>
        public void DiscardPreview()
        {
            CancelRetime();
            BeginPreviewReplacement();
            DisarmPrefetch();
            Preview = null;
            PreviewJulianDate = double.NaN;
            PreviewMinimumSeconds = double.NaN;
            PreviewMaximumSeconds = double.NaN;
        }

        /// <summary>How many bisection probes find the profile's slowest
        /// accepted trip. Six puts the right-hand end within a few percent of
        /// the planner's actual limit, which is finer than the slider needs.</summary>
        private const int WindowProbes = 6;

        /// <summary>
        /// The slowest trip the trip-time control may offer. The profile has a
        /// real upper bound: the short-way Lambert transfer sweeps past 180°, and
        /// a slow coast arrives slower than the target's frame, so the planner
        /// rejects those durations. The end is the largest accepted duration
        /// inside <see cref="TripPlanner.MaxDurationScale"/> times the minimum,
        /// found by probing; the slider can never ask for a plan the planner
        /// refuses.
        ///
        /// Internal so the route benchmark can time the exact pair
        /// <see cref="WindowFor"/> runs, instead of timing a copy that can drift
        /// from the real probe count. It reads no session state.
        /// </summary>
        internal double MaximumPreviewSeconds(string targetId, double minimum,
            TripPlanner planner, ShipState departure)
        {
            if (!(minimum > 0.0) || !double.IsFinite(minimum))
                return double.NaN;

            double cap = minimum * TripPlanner.MaxDurationScale;
            if (IsDurationFeasible(targetId, cap, planner, departure)) return cap;

            // The minimum is the plan the planner just found, so it is the
            // feasible floor; the cap is the infeasible ceiling.
            double feasible = minimum;
            double infeasible = cap;
            for (int probe = 0; probe < WindowProbes; probe++)
            {
                double middle = 0.5 * (feasible + infeasible);
                if (IsDurationFeasible(targetId, middle, planner, departure))
                    feasible = middle;
                else infeasible = middle;
            }

            return feasible;
        }

        private bool IsDurationFeasible(string targetId, double seconds,
            TripPlanner planner, ShipState departure)
        {
            if (!(seconds > 0.0) || !double.IsFinite(seconds)) return false;
            return planner.PlanAtDuration(departure, targetId, seconds).IsFeasible;
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
        ///
        /// Begin consumes any preview. Inside the preview's own freshness
        /// window the drawn plan **is** the flight: it is certified from the
        /// departure it was drawn at, and replanning would reproduce a plan the
        /// HUD still advertises at the cost of a second plot. The ship adopts
        /// that departure state, so the plan and its arrival date do not move.
        /// Outside the window, and whenever a rebuild is outstanding, Begin
        /// replans at the current state and date. Its base plan is
        /// <see cref="TripPlanner.PlanFastest"/> — the same minimum the preview's
        /// trip-time window advertises — so a flight begun at a burn-only
        /// minimum flies the no-coast crossing that was drawn; a preview that
        /// asked for a longer time still re-times through
        /// <see cref="TripPlanner.PlanAtDuration"/>. A stale preview that sat at
        /// its minimum flies the fresh minimum, because the old number can sit
        /// in the zero-coast gap at the new date.</summary>
        public TrajectoryPlan Begin(string targetId, double julianDate)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            if (CanFlyPreview(targetId, julianDate))
            {
                TrajectoryPlan drawn = Preview;
                _state = drawn.DepartureState;
                DiscardPreview();
                // The cached windows belong to this departure, which is still
                // the ship's state, so they stay valid. Only a replan below
                // changes the departure and drops them.
                Plan = drawn;
                _appliedImpulses.Clear();
                return StartFlight();
            }

            // A retimed preview carries the player's chosen flight time into
            // the flight. Read it before the preview is discarded, solve the
            // fastest plan at the current date, and re-time to the choice.
            // The fastest plan is PlanFastest, the same minimum the preview's
            // trip-time window advertises, so beginning at a burn-only minimum
            // flies the plan that was drawn instead of the coast floor. A
            // preview that sat at its minimum carries the minimum, not a
            // duration: the clock may have moved since the plot, and the fresh
            // minimum is the answer. Re-timing to the old number by a few
            // seconds would land in the zero-coast gap.
            double requestedSeconds = double.NaN;
            bool previewAtMinimum = false;
            if (Preview != null && Preview.TargetId == targetId)
            {
                requestedSeconds = Preview.FlightTimeSeconds;
                previewAtMinimum = Math.Abs(requestedSeconds - PreviewMinimumSeconds)
                    <= 1e-9 * Math.Max(1.0, PreviewMinimumSeconds);
            }

            DiscardPreview();
            ClearWindows();
            _state.JulianDate = julianDate;
            TrajectoryPlan fastest = _planner.PlanFastest(_state, targetId);
            if (!previewAtMinimum && !double.IsNaN(requestedSeconds)
                && requestedSeconds > fastest.FlightTimeSeconds)
                fastest = _planner.PlanAtDuration(_state, targetId, requestedSeconds);

            Plan = fastest;
            _appliedImpulses.Clear();
            return StartFlight();
        }

        /// <summary>True when the drawn preview can be flown as it stands. The
        /// session already calls a preview valid until
        /// <see cref="PreviewStaleAfterDays"/> of clock has passed, and the plan
        /// is certified from the departure it was drawn at, so the date window
        /// is the reuse deadband: an exact date match would throw the plan away
        /// on the next frame. There is no separate position deadband — the only
        /// way an idle ship moves between plot and Begin is the coast that same
        /// window bounds, and every other mover (a place, a teleport, a cancel,
        /// a style or burn-level change) discards or rebuilds the preview — so
        /// Begin adopts the preview's departure state rather than comparing it.
        /// A rebuild or a retime that has not been published fails the check:
        /// the published plan would no longer be what the panel shows.</summary>
        private bool CanFlyPreview(string targetId, double julianDate)
        {
            if (Preview == null || Preview.TargetId != targetId) return false;
            if (!Preview.IsFeasible) return false;

            // A level changed without a rebuild would fly a plan the panel's
            // level control no longer shows.
            double drive = _planner.MaxAccelerationMps2;
            if (Math.Abs(Preview.MaxAccelerationMps2 - drive)
                > 1e-9 * Math.Max(1.0, drive)) return false;

            double age = julianDate - PreviewJulianDate;
            double window = PreviewStaleAfterDays;
            if (!(age >= 0.0) || !(window >= 0.0) || age > window) return false;

            lock (_retimeLock)
            {
                if (_pendingPreview != null || _completedPreview != null) return false;
            }

            return !IsRetimePending;
        }

        /// <summary>Start flying <see cref="Plan"/> and report it. A rejected or
        /// infeasible plan leaves the ship where it is and names the reason.</summary>
        private TrajectoryPlan StartFlight()
        {
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
            if (_autopilot == null || Plan == null)
                return ThrustCommand.None;

            // The last RK4 stage of the arrival step lands exactly on the
            // arrival date, and that stage still belongs to the terminal finite
            // burn: dropping its thrust here loses h/6 of the match command
            // (metres per second) from the tracked arrival. The tolerance is the
            // same one arrival detection uses, so only the stage at the arrival
            // instant is kept and a completed plan still commands nothing.
            if (Plan.IsComplete(julianDate - ArrivalToleranceDays))
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

        /// <summary>One background retime: the pinned departure, the target, the
        /// chosen duration, the session planner's configuration, and the
        /// generation that makes it latest-wins. Immutable apart from
        /// <see cref="Generation"/>, which only the requesting thread sets
        /// before the request is queued.</summary>
        private sealed class RetimeRequest
        {
            public RetimeRequest(ShipState departure, string targetId, double durationSeconds,
                CaptureStyle arrival, double burnLevelG, TripPlanner settings,
                double minimumSeconds, double maximumSeconds, PinSeed seed = default)
            {
                Departure = departure;
                TargetId = targetId;
                DurationSeconds = durationSeconds;
                Arrival = arrival;
                Seed = seed;
                BurnLevelG = burnLevelG;
                MinimumSeconds = minimumSeconds;
                MaximumSeconds = maximumSeconds;
                MaxAccelerationMps2 = settings.MaxAccelerationMps2;
                ThrustMargin = settings.ThrustMargin;
                EntrySpeedMps = settings.EntrySpeedMps;
                ArcSamples = settings.ArcSamples;
                MaxIterations = settings.MaxIterations;
                ConvergenceTolerance = settings.ConvergenceTolerance;
                ParkingPhaseOffsetRad = settings.ParkingPhaseOffsetRad;
            }

            public ShipState Departure { get; }
            public string TargetId { get; }
            public double DurationSeconds { get; }
            public CaptureStyle Arrival { get; }

            /// <summary>The pin the build should start from: the nearest
            /// solved trip's, captured on the requesting thread, or the cold
            /// start.</summary>
            public PinSeed Seed { get; }

            public double BurnLevelG { get; }

            /// <summary>The preview window the request belongs to, which bounds
            /// the duration and sets the cache's match tolerance.</summary>
            public double MinimumSeconds { get; }
            public double MaximumSeconds { get; }

            public double MaxAccelerationMps2 { get; }
            public double ThrustMargin { get; }
            public double EntrySpeedMps { get; }
            public int ArcSamples { get; }
            public int MaxIterations { get; }
            public double ConvergenceTolerance { get; }
            public double ParkingPhaseOffsetRad { get; }

            /// <summary>Set by <see cref="RequestPreviewDuration"/> before the
            /// request is queued; the worker only reads it.</summary>
            public long Generation { get; set; }

            /// <summary>Copy this request onto the worker's own planner. The
            /// worker is its only caller.</summary>
            public void ApplyTo(TripPlanner planner)
            {
                planner.ArrivalStyle = Arrival;
                planner.BurnLevelG = BurnLevelG;
                planner.MaxAccelerationMps2 = MaxAccelerationMps2;
                planner.ThrustMargin = ThrustMargin;
                planner.EntrySpeedMps = EntrySpeedMps;
                planner.ArcSamples = ArcSamples;
                planner.MaxIterations = MaxIterations;
                planner.ConvergenceTolerance = ConvergenceTolerance;
                planner.ParkingPhaseOffsetRad = ParkingPhaseOffsetRad;
            }
        }

        /// <summary>One queued prefetch: the retime parameters it builds, and
        /// the window its seed and cache tolerance come from. It has no
        /// generation and no publisher — a prefetch can only fill a cache
        /// slot.</summary>
        private sealed class PrefetchRequest
        {
            public PrefetchRequest(RetimeRequest parameters, double minimumSeconds,
                double maximumSeconds)
            {
                Parameters = parameters;
                MinimumSeconds = minimumSeconds;
                MaximumSeconds = maximumSeconds;
            }

            public RetimeRequest Parameters { get; }
            public double MinimumSeconds { get; }
            public double MaximumSeconds { get; }
        }

        /// <summary>Which background route-planning build a <see cref="PreviewRequest"/>
        /// asks for.</summary>
        private enum PreviewKind
        {
            /// <summary>A route preview is being plotted: the first plot or a
            /// replot after a discard. The build solves the fastest feasible
            /// plan and the trip-time window.</summary>
            Plot,

            /// <summary>The published preview is being rebuilt against a
            /// changed arrival style or burn level. The build
            /// keeps the player's flight time when the new window contains
            /// it.</summary>
            Rebuild,

            /// <summary>A plan is being warmed for a selection. The build is a
            /// plot's, but the result is held as
            /// <see cref="_primedPreview"/> instead of published: nothing draws
            /// it and no panel reads it until a Plot click adopts it.</summary>
            Prime
        }

        /// <summary>One background preview build, queued for the worker. The
        /// requester sets <see cref="Generation"/> when it queues the request;
        /// the worker only reads it. Each request carries its own snapshot of
        /// the session planner's configuration, applied to the worker's
        /// planner before the build, exactly as <see cref="RetimeRequest"/>
        /// does for retimes.</summary>
        private sealed class PreviewRequest
        {
            public PreviewRequest(PreviewKind kind, string targetId, ShipState departure,
                double durationSeconds, CaptureStyle arrival,
                double burnLevelG, TripPlanner settings, bool atMinimum = false)
            {
                Kind = kind;
                TargetId = targetId;
                Departure = departure;
                DurationSeconds = durationSeconds;
                Arrival = arrival;
                BurnLevelG = burnLevelG;
                AtMinimum = atMinimum;
                MaxAccelerationMps2 = settings.MaxAccelerationMps2;
                ThrustMargin = settings.ThrustMargin;
                EntrySpeedMps = settings.EntrySpeedMps;
                ArcSamples = settings.ArcSamples;
                MaxIterations = settings.MaxIterations;
                ConvergenceTolerance = settings.ConvergenceTolerance;
                ParkingPhaseOffsetRad = settings.ParkingPhaseOffsetRad;
            }

            public PreviewKind Kind { get; }
            public string TargetId { get; }
            public ShipState Departure { get; }

            /// <summary>The requested flight time for a rebuild; NaN to solve
            /// the fastest feasible plan (a plot).</summary>
            public double DurationSeconds { get; }

            /// <summary>True when the rebuild is keeping a preview that sat at
            /// its minimum, rather than a duration the player chose. The new
            /// style's own minimum is then the answer: its burn-only total can
            /// sit a few seconds either side of the old one, and asking the
            /// planner for the old number would land inside the zero-coast gap
            /// the new style may open.</summary>
            public bool AtMinimum { get; }

            public CaptureStyle Arrival { get; }
            public double BurnLevelG { get; }
            public double MaxAccelerationMps2 { get; }
            public double ThrustMargin { get; }
            public double EntrySpeedMps { get; }
            public int ArcSamples { get; }
            public int MaxIterations { get; }
            public double ConvergenceTolerance { get; }
            public double ParkingPhaseOffsetRad { get; }

            /// <summary>Set by <see cref="EnqueuePreview"/> before the request
            /// is queued; the worker only reads it.</summary>
            public long Generation { get; set; }

            /// <summary>Copy this request onto the worker's own planner. The
            /// worker is its only caller.</summary>
            public void ApplyTo(TripPlanner planner)
            {
                planner.ArrivalStyle = Arrival;
                planner.BurnLevelG = BurnLevelG;
                planner.MaxAccelerationMps2 = MaxAccelerationMps2;
                planner.ThrustMargin = ThrustMargin;
                planner.EntrySpeedMps = EntrySpeedMps;
                planner.ArcSamples = ArcSamples;
                planner.MaxIterations = MaxIterations;
                planner.ConvergenceTolerance = ConvergenceTolerance;
                planner.ParkingPhaseOffsetRad = ParkingPhaseOffsetRad;
            }
        }

        /// <summary>One finished background preview build, ready to publish on
        /// the main thread. <see cref="WindowPlan"/> is the fastest plan the
        /// window solved; <see cref="FinalPlan"/> is what the preview
        /// publishes: the same plan, or a slower requested duration.
        /// <see cref="MaximumSeconds"/> is the window's upper bound, and the
        /// departure date pins the display date. The worker creates it; the
        /// main thread reads it under the request lock.</summary>
        private sealed class CompletedPreview
        {
            public CompletedPreview(TrajectoryPlan finalPlan,
                TrajectoryPlan windowPlan, double maximumSeconds,
                ShipState departure, double burnLevelG, CaptureStyle arrival)
            {
                FinalPlan = finalPlan;
                WindowPlan = windowPlan;
                MaximumSeconds = maximumSeconds;
                Departure = departure;
                BurnLevelG = burnLevelG;
                Arrival = arrival;
            }

            public TrajectoryPlan FinalPlan { get; }
            public TrajectoryPlan WindowPlan { get; }
            public double MaximumSeconds { get; }

            /// <summary>The frozen departure state the build used. A prefetch
            /// must build from this, not from the ship's later state.</summary>
            public ShipState Departure { get; }

            /// <summary>The burn level the build used, captured before the
            /// build so a later change cannot retarget a queued prefetch.</summary>
            public double BurnLevelG { get; }

            /// <summary>The arrival style the build used.</summary>
            public CaptureStyle Arrival { get; }

            public double DepartureJulianDate => Departure.JulianDate;
        }

        /// <summary>One cached preview window: the fastest feasible plan and the
        /// slowest trip the slider may offer.</summary>
        private readonly struct PreviewWindow
        {
            public PreviewWindow(TrajectoryPlan plan, double maximumSeconds,
                PinSeed floorSeed = default)
            {
                Plan = plan;
                MaximumSeconds = maximumSeconds;
                FloorSeed = floorSeed;
            }

            public TrajectoryPlan Plan { get; }
            public double MaximumSeconds { get; }

            /// <summary>The pin the window's floor plan settled on, or the cold
            /// start when the window plan never ran the pin (the burn-only
            /// crossing at a high level carries none).</summary>
            public PinSeed FloorSeed { get; }
        }

        /// <summary>A plan the session already built at a requested flight time,
        /// kept so a returned slider handle is instant. The stored seconds are
        /// the request, not the plan's own total, so the next drag asks at the
        /// same value it used before. The seed is the pin that plan settled on;
        /// the nearest entry starts the next build.</summary>
        private readonly struct CachedRetime
        {
            public CachedRetime(double requestedSeconds, TrajectoryPlan plan,
                PinSeed seed = default)
            {
                RequestedSeconds = requestedSeconds;
                Plan = plan;
                Seed = seed;
            }

            public double RequestedSeconds { get; }
            public TrajectoryPlan Plan { get; }
            public PinSeed Seed { get; }
        }

        /// <summary>The inputs that make a preview window unique. The departure
        /// date is part of the key and the cache is also dropped when it changes,
        /// so a stale date can never be read back.</summary>
        private readonly struct WindowKey : IEquatable<WindowKey>
        {
            public WindowKey(double julianDate, string targetId, double burnLevelG,
                CaptureStyle arrival)
            {
                JulianDate = julianDate;
                TargetId = targetId;
                BurnLevelG = burnLevelG;
                Arrival = arrival;
            }

            public double JulianDate { get; }
            public string TargetId { get; }
            public double BurnLevelG { get; }
            public CaptureStyle Arrival { get; }

            public bool Equals(WindowKey other) =>
                JulianDate.Equals(other.JulianDate)
                && string.Equals(TargetId, other.TargetId, StringComparison.Ordinal)
                && BurnLevelG.Equals(other.BurnLevelG)
                && Arrival == other.Arrival;

            public override bool Equals(object obj) => obj is WindowKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = JulianDate.GetHashCode();
                    hash = hash * 397 ^ (TargetId != null ? TargetId.GetHashCode() : 0);
                    hash = hash * 397 ^ BurnLevelG.GetHashCode();
                    hash = hash * 397 ^ (int)Arrival;
                    return hash;
                }
            }
        }
    }
}
