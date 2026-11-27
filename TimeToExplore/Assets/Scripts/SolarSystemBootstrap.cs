using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using TimeToExplore.UI;

namespace TimeToExplore
{
    /// <summary>
    /// Entry point for the prototype. Loads the generated catalogue, builds the
    /// renderer, the camera and the flight session, and advances the clock and
    /// the ship together. It creates itself in any scene that does not already
    /// contain one, so pressing Play in an empty scene is enough to see the
    /// solar system.
    ///
    /// The ship starts at Gateway Station, so the default Go To trip is the one
    /// the EditMode suite measures. The world clock is never allowed to run
    /// ahead of the ship: if the integrator's substep budget is exhausted, the
    /// date is clamped to the one the ship actually reached.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SolarSystemBootstrap : MonoBehaviour
    {
        public float UnitsPerMeter = 1e-6f; // 1 unit = 1,000 km
        public Camera ViewCamera { get; private set; }
        public SolarSystemModel Model { get; private set; }
        public SimulationClock Clock { get; private set; }
        public SolarSystemRenderer Renderer { get; private set; }
        public ShipController Ship { get; private set; }
        public CameraFocusDirector Focus { get; private set; }
        public FlightSession Flight { get; private set; }
        public TradeSession Trade { get; private set; }
        public TrajectoryLine Trajectory { get; private set; }
        public ShipCameraRig Rig { get; private set; }
        public ShipView ShipMarker { get; private set; }
        public SkyView Sky { get; private set; }
        public BodyRuntime Selected { get; private set; }
        public bool ShowHud = true;
        public bool ShowLabels = true;

        /// <summary>Debug free flight. A flight owns the ship while it runs.</summary>
        public bool FreeFlight { get; private set; }

        /// <summary>The scale the player asked for, before the warp policy caps it.</summary>
        public int RequestedScaleIndex { get; private set; }

        /// <summary>Set when the warp policy is holding the clock below the request.</summary>
        public string WarpNote { get; private set; }

        /// <summary>RK4 substeps one frame may spend on the ship.</summary>
        public int SubstepsPerFrame = FlightSession.DefaultSubstepsPerFrame;

        /// <summary>
        /// The frame's elapsed seconds. Defaults to <see cref="Time.deltaTime"/>,
        /// which the scene uses; the frame-loop acceptance test supplies a fixed
        /// step because EditMode does not advance time.
        /// </summary>
        public System.Func<float> DeltaTimeSeconds { get; set; } = () => Time.deltaTime;

        public Vector3d ShipPositionM => Flight != null ? Flight.State.PositionM : Vector3d.Zero;
        public Vector3d ShipVelocityMps => Flight != null ? Flight.State.VelocityMps : Vector3d.Zero;

        private readonly List<BodyRuntime> _targets = new List<BodyRuntime>();
        private readonly List<Vector3d> _routePoints = new List<Vector3d>();
        private GravityField _gravity;
        private TripPlanner _planner;
        private bool _wasFlying;

        /// <summary>The body whose finished plan should be drawn and framed, or
        /// null. Only an explicit Plot route click sets it, so reading a body's
        /// details never draws or moves anything. It covers both the plot a
        /// click queues and the warmed plan it adopts when that lands.</summary>
        private string _frameOnPreviewTargetId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Object.FindFirstObjectByType<SolarSystemBootstrap>() != null) return;
            GameObject root = new GameObject("Time to Explore");
            root.AddComponent<SolarSystemBootstrap>();
        }

        private void Awake()
        {
            TextAsset catalogue = Resources.Load<TextAsset>("solar_system");
            if (catalogue == null)
            {
                Debug.LogError("Could not load Resources/solar_system.json. Run tools/generate_solar_system.py.");
                enabled = false;
                return;
            }

            Model = new SolarSystemModel();
            Model.Load(catalogue.text);

            // The market is its own hand-edited file, loaded behind the same
            // kind of seam as the orbital catalogue. A missing or invalid file
            // is fatal for the prototype, so say which file and stop rather than
            // leaving the HUD with no session to read.
            try
            {
                TradeCatalogue tradeCatalogue = TradeCatalogueLoader.LoadFromResources();
                Trade = new TradeSession(tradeCatalogue,
                    new CargoHold(CargoHold.DefaultCapacityM3));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("Could not load Resources/trade_catalogue.json. "
                               + exception.Message);
                enabled = false;
                return;
            }

            Clock = new SimulationClock { JulianDate = Model.Data.startJulianDate };

            EnsureCamera();

            Sky = gameObject.AddComponent<SkyView>();
            Sky.Initialise(ViewCamera);

            Ship = gameObject.AddComponent<ShipController>();
            Ship.PositionSource = () => Flight != null ? Flight.State.PositionM : Vector3d.Zero;
            Ship.Teleported = position => Flight?.Teleport(position, Clock.JulianDate);

            _gravity = new GravityField(Model);
            _planner = new TripPlanner(Model, _gravity)
            {
                MaxAccelerationMps2 = ShipState.DefaultMaxAccelerationMps2
            };

            Flight = new FlightSession(Model, _gravity, _planner);

            Rig = gameObject.AddComponent<ShipCameraRig>();
            Rig.Initialise(ViewCamera, Model, UnitsPerMeter);

            Focus = gameObject.AddComponent<CameraFocusDirector>();
            Focus.Initialise(Model, Ship, Rig);

            Renderer = gameObject.AddComponent<SolarSystemRenderer>();
            Renderer.Initialise(Model, ViewCamera, UnitsPerMeter);

            Trajectory = gameObject.AddComponent<TrajectoryLine>();
            Trajectory.Initialise(UnitsPerMeter);

            ShipMarker = gameObject.AddComponent<ShipView>();
            ShipMarker.Initialise();

            ResetToGateway();

            SolarSystemHud hud = gameObject.AddComponent<SolarSystemHud>();
            hud.Initialise(this);

            BuildTargets();
            Selected = Model.Get("earth") ?? Model.Sun;

            Renderer.Refresh(ShipPositionM, Clock.JulianDate,
                Rig != null ? Rig.Orbit.DistanceUnits : 0f);
        }

        /// <summary>
        /// Stop the preview retime worker with the scene. The session owns a
        /// background thread once the slider has asked for a retime; it must not
        /// outlive the HUD and trajectory line whose plans it hands back. The
        /// worker is a background thread and checks the stop flag before
        /// publishing, so teardown never blocks on a plan being drawn.
        /// </summary>
        private void OnDestroy()
        {
            Flight?.Dispose();
        }

        private void EnsureCamera()
        {
            ViewCamera = Camera.main;
            if (ViewCamera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                ViewCamera = cameraObject.AddComponent<Camera>();
            }

            ViewCamera.transform.position = Vector3.zero;
            ViewCamera.transform.rotation = Quaternion.identity;
            ViewCamera.clearFlags = CameraClearFlags.SolidColor;
            ViewCamera.backgroundColor = Color.black;
            ViewCamera.nearClipPlane = 0.05f;
            ViewCamera.farClipPlane = 20_000_000f;
            ViewCamera.fieldOfView = 60f;

            // The volume (in the pipeline's default profile) supplies the bloom;
            // a camera built from scratch leaves post-processing off, so opt in
            // here or the effect silently never runs. The HUD is a Screen Space
            // Overlay canvas, which draws after post-processing, so it is not
            // smeared by the bloom.
            ViewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // Unity's legacy mouse dispatcher (SendMouseEvents, the OnMouse*
            // path) runs every frame even with Active Input Handling on the
            // Input System only. With the legacy backend off it has no position
            // to read, substitutes (0,0), then asks each camera for a ray from
            // that frustum corner and warns there once per camera per frame:
            // "Screen position out of view frustum". No OnMouse* handler exists
            // and world picking goes through PlayerControls, so this camera
            // sends no legacy mouse events. eventMask only gates that old path;
            // cullingMask still renders every layer.
            ViewCamera.eventMask = 0;
        }

        /// <summary>
        /// The frame time this frame uses, from the scene or from the test seam.
        /// </summary>
        private float FrameDeltaSeconds()
        {
            float seconds = DeltaTimeSeconds != null ? DeltaTimeSeconds() : Time.deltaTime;
            return seconds > 0f ? seconds : 0f;
        }

        private void Update()
        {
            if (Model == null) return;

            HandleHotkeys();
            if (Flight == null) return;

            float deltaTime = FrameDeltaSeconds();

            if (FreeFlight)
            {
                Clock.ScaleIndex = RequestedScaleIndex;
                Clock.Advance(deltaTime);
                Model.Update(Clock.JulianDate);
                return;
            }

            // The orrery must be at the frame's start date before the warp
            // decision, because the policy reads the nearest body's state.
            Model.Update(Clock.JulianDate);

            int allowed = WarpPolicy.ClampScaleIndex(
                WarpPolicy.MaxFrameSeconds(Flight.State, Clock.JulianDate, Model, _gravity, Flight.Plan),
                deltaTime, RequestedScaleIndex);

            Clock.ScaleIndex = allowed;
            WarpNote = allowed < RequestedScaleIndex
                ? $"auto from {ScaleLabel(RequestedScaleIndex)}"
                : null;

            Clock.Advance(deltaTime);

            FlightStep step = Flight.IsActive
                ? Flight.Advance(Clock.JulianDate, SubstepsPerFrame)
                : Flight.Coast(Clock.JulianDate, SubstepsPerFrame);

            // The world must never run ahead of the ship, whether a flight is
            // integrating or an idle ship is coasting. The clock still runs
            // with a preview because the coast keeps the ship moving too.
            if (step.ReachedJulianDate < Clock.JulianDate)
                Clock.JulianDate = step.ReachedJulianDate;

            Model.Update(Clock.JulianDate);
        }

        private void LateUpdate()
        {
            // A frame can arrive before Awake has built the world, or while a
            // scene teardown is destroying it (a script reload stops play mode
            // between frames). Drawing nothing for that frame is correct and
            // keeps the console free of one exception per frame.
            if (Model == null || Clock == null || Renderer == null) return;

            if (Flight != null)
            {
                if (Flight.IsFlying)
                {
                    Ship.InputEnabled = false;
                }
                else if (Focus == null || !Focus.IsTransitioning)
                {
                    // Hand the ship back after arrival, a cancel, or an abort.
                    Ship.InputEnabled = true;
                }

                // Follow the destination until the player takes the camera.
                if (Flight.IsActive && Rig != null && Flight.TargetId != null)
                {
                    BodyRuntime target = Model.Get(Flight.TargetId);
                    if (target != null && Rig.AutoTarget != target) Rig.Frame(target);
                }

                // The framing distance is captured when the flight starts, so
                // the cruise view does not keep winding in as the ship
                // approaches. Re-capture it once on arrival, so the camera
                // settles on the parking orbit instead of holding the cruise
                // distance. Both moves ease; neither snaps.
                bool flying = Flight.IsFlying;
                if (_wasFlying && !flying && Flight.Status == FlightStatus.Arrived)
                    Rig?.RecalculateFramingDistance(ShipPositionM);
                _wasFlying = flying;
            }

            if (Rig != null) Rig.Apply(ShipPositionM, FrameDeltaSeconds());

            if (ShipMarker != null)
            {
                ShipMarker.Refresh(ViewCamera,
                    Flight != null ? Flight.CurrentThrust : ThrustCommand.None,
                    ShipVelocityMps);
            }

            Renderer.Refresh(ShipPositionM, Clock.JulianDate,
                Rig != null ? Rig.Orbit.DistanceUnits : 0f);
            if (Trajectory != null) Trajectory.Refresh(ShipPositionM, ViewCamera);
        }

        private void HandleHotkeys()
        {
            PlayerControls.Hotkeys keys = PlayerControls.ReadHotkeys();
            if (keys.Home) Rig?.HomeToShip(ShipPositionM);
            if (keys.Pause) Clock.TogglePause();

            if (keys.ScaleIndex >= 0 && keys.ScaleIndex < SimulationClock.TimeScales.Length)
                SetScaleIndex(keys.ScaleIndex);

            if (keys.CycleTarget) CycleTarget(1);
            if (keys.FocusSelected) FocusSelected();
            if (keys.JumpSelected) JumpToSelected();
            if (keys.CancelFocus) Focus?.Cancel();
            if (keys.Reset) ResetToGateway();
            if (keys.CancelFlight) CancelFlight();
            if (keys.ToggleFreeFlight) SetFreeFlight(!FreeFlight);
            if (keys.ToggleTrueScale) Renderer.TrueScale = !Renderer.TrueScale;
            if (keys.ToggleOrbits)
            {
                Renderer.ShowOrbits = !Renderer.ShowOrbits;
                Renderer.ShowMoonOrbits = Renderer.ShowOrbits;
            }
            if (keys.ToggleLabels) ShowLabels = !ShowLabels;
            if (keys.ToggleHud) ShowHud = !ShowHud;
        }

        public void SetScaleIndex(int index)
        {
            RequestedScaleIndex = Mathf.Clamp(index, 0, SimulationClock.TimeScales.Length - 1);
            Clock.SetScaleIndex(RequestedScaleIndex);
        }

        public static string ScaleLabel(int index)
        {
            index = Mathf.Clamp(index, 0, SimulationClock.TimeScales.Length - 1);
            double scale = SimulationClock.TimeScales[index];
            return scale == 1.0 ? "x1" : "x" + scale.ToString("N0");
        }

        private void BuildTargets()
        {
            _targets.Clear();
            foreach (BodyRuntime body in Model.Ordered)
            {
                if (body.Kind == "star" || body.Kind == "planet" || body.Kind == "station")
                    _targets.Add(body);
            }
        }

        private void CycleTarget(int direction)
        {
            if (_targets.Count == 0) return;
            Focus?.Cancel();
            int current = _targets.IndexOf(Selected);
            int next = (current + direction + _targets.Count) % _targets.Count;
            Select(_targets[next]);
        }

        /// <summary>Start a Go To flight to a body. Raises the clock so the trip
        /// is watchable, and draws the plan. The ship's position becomes the
        /// flight session's for the duration. Begin consumes a plotted preview:
        /// inside the preview's freshness window it flies the plan that was
        /// drawn, otherwise it replans from the current state and date. A
        /// station is a
        /// point target with no gravitational parameter of its own, so it is
        /// allowed through; only a genuinely massless non-station is refused.</summary>
        public void BeginGoTo(BodyRuntime body)
        {
            if (body == null || Flight == null || Model == null) return;
            if (body.Gm <= 0.0 && !body.Data.IsStation) return;

            Focus?.Cancel();
            SetFreeFlight(false);
            Ship.InputEnabled = false;

            // The flight's own framing replaces any route framing a plot asked
            // for; a late preview must not pull the camera off the ship.
            _frameOnPreviewTargetId = null;

            TrajectoryPlan plan = Flight.Begin(body.Id, Clock.JulianDate);

            if (Flight.Status == FlightStatus.Flying)
            {
                ShowPlan(plan, body);
                Rig?.Frame(body);
                SetScaleIndex(WarpPolicy.SuggestInitialScaleIndex(plan));
            }
            else
            {
                Trajectory.Hide();
            }
        }

        /// <summary>
        /// Plot a route for inspection without starting the flight, and take
        /// the camera to the finished route. The preview never moves the ship;
        /// Begin flight flies it while it is fresh, and replans from the current
        /// state past that.
        /// A selection's warmed plan is adopted here, so the route and its
        /// report appear in the click's own frame; otherwise the plan is built
        /// on the background planner (see <see cref="FlightSession.PlotAsync"/>)
        /// and <see cref="ApplyPreviewIfReady"/> draws it once it is ready. A
        /// station is a valid point target; only a genuinely massless
        /// non-station is refused.
        /// </summary>
        public void PlotRoute(BodyRuntime body)
        {
            StartPlot(body);
        }

        /// <summary>
        /// Warm the selected body's plan in the background without showing it.
        /// Nothing on screen changes — no route, no flight panel, no trip-time
        /// slider, and Begin stays disabled — because the plan is held as a
        /// prime rather than published. Pressing Plot route adopts it, so the
        /// first, slow solve has already been paid for while the player read
        /// the details screen.
        /// </summary>
        public void PrimeRoute(BodyRuntime body)
        {
            if (!CanPlot(body)) return;

            // A route already on screen is the plan the selection wants.
            if (Flight.HasPreview && Flight.Preview.TargetId == body.Id) return;

            Flight.PrimeAsync(body.Id, Clock.JulianDate);
        }

        /// <summary>
        /// The one plot path. The selection's prime is adopted when it is ready,
        /// which shows the route in the same frame as the click; a prime still
        /// building is continued and revealed when it lands. A fresh preview
        /// that is still at the planner's fastest trip is framed instead of
        /// rebuilt, while a retimed or stale one is replotted — that is how the
        /// panel's Replot resets the trip time.
        /// </summary>
        private void StartPlot(BodyRuntime body)
        {
            if (!CanPlot(body)) return;

            if (Flight.TryAdoptPrimedPreview(body.Id, out TrajectoryPlan primed))
            {
                _frameOnPreviewTargetId = null;
                ShowPlan(primed, body);
                FramePlottedRoute(primed, body);
                return;
            }

            // The warmed plan is still being solved: show it when it lands.
            if (Flight.IsPrimePending(body.Id))
            {
                _frameOnPreviewTargetId = body.Id;
                return;
            }

            bool hasPreview = Flight.HasPreview && Flight.Preview.TargetId == body.Id;
            if (hasPreview && IsPreviewAtFastestTrip()
                           && !Flight.IsPreviewStale(Clock.JulianDate))
            {
                _frameOnPreviewTargetId = null;
                FramePlottedRoute(Flight.Preview, body);
                return;
            }

            if (!Flight.IsPlotPending(body.Id)) Flight.PlotAsync(body.Id, Clock.JulianDate);
            _frameOnPreviewTargetId = body.Id;
        }

        /// <summary>Whether a body can be planned to at all: a star, planet, or
        /// station with mass or a station's point target, and a session that is
        /// not already flying the ship elsewhere.</summary>
        private bool CanPlot(BodyRuntime body) =>
            body != null && Flight != null && Model != null
            && (body.Gm > 0.0 || body.Data.IsStation)
            && !Flight.IsFlying;

        /// <summary>Whether the published preview is still the planner's own
        /// fastest trip, rather than a duration the player chose on the
        /// slider.</summary>
        private bool IsPreviewAtFastestTrip()
        {
            if (Flight == null || !Flight.HasPreview) return false;

            double fastest = Flight.PreviewMinimumSeconds;
            double shown = Flight.PreviewDurationSeconds;
            return double.IsFinite(fastest) && double.IsFinite(shown)
                   && System.Math.Abs(shown - fastest)
                   <= 1e-9 * System.Math.Max(1.0, System.Math.Abs(fastest));
        }

        /// <summary>Begin the plotted route. While the preview is fresh the drawn
        /// plan is flown as it stands; past its window the flight is replanned
        /// from the current state and date, and keeps the flight time the player
        /// chose on the slider.</summary>
        public void BeginPlottedFlight()
        {
            if (Flight == null || !Flight.HasPreview) return;
            BeginGoTo(Model?.Get(Flight.Preview.TargetId));
        }

        /// <summary>
        /// Ask for the plotted route to be re-timed to a requested flight time.
        /// The request is queued; the session's worker builds it with its own
        /// planner and the HUD publishes it through
        /// <see cref="ApplyRetimeIfReady"/> once it is ready. Nothing blocks the
        /// frame, and the newest request wins: stale builds are dropped. The
        /// camera is deliberately not re-framed: a drag must not yank the view.
        /// </summary>
        public void RetimePreview(double flightTimeSeconds)
        {
            if (Flight == null || !Flight.HasPreview) return;

            Flight.RequestPreviewDuration(flightTimeSeconds);
        }

        /// <summary>
        /// Publish a finished background retime, on the main thread. The HUD
        /// calls this once per frame; the worker never touches the trajectory
        /// line or the HUD. A failed build is logged and leaves the published
        /// preview in place.
        /// </summary>
        public void ApplyRetimeIfReady()
        {
            if (Flight == null) return;

            string failure = Flight.TakeRetimeFailure();
            if (!string.IsNullOrEmpty(failure))
                Debug.LogWarning("Preview retime failed: " + failure);

            if (!Flight.TryApplyCompletedRetime(out TrajectoryPlan preview)) return;

            ShowPlan(preview, Model?.Get(preview.TargetId));
        }

        /// <summary>
        /// Draw a finished background plan, on the main thread. The planner
        /// worker hands its work here once per HUD update; the HUD never
        /// touches the worker, and nothing it produces is shown before now. A
        /// failed build is logged and leaves the published preview in place.
        /// A plot publishes a preview and draws it; a selection's prime is
        /// adopted only when the click that asked for the route is waiting, so
        /// warming a plan never draws anything by itself. Warp is raised by
        /// <see cref="BeginGoTo"/>, not by a plot.
        /// </summary>
        public void ApplyPreviewIfReady()
        {
            if (Flight == null) return;

            string failure = Flight.TakePreviewFailure();
            if (!string.IsNullOrEmpty(failure))
            {
                Debug.LogWarning("Preview plan failed: " + failure);
                // The build that would have taken the camera failed, so the
                // click that asked for it has nothing left to wait for.
                _frameOnPreviewTargetId = null;
            }

            if (Flight.TryApplyCompletedPreview(out TrajectoryPlan preview))
            {
                BodyRuntime target = Model?.Get(preview.TargetId);
                ShowPlan(preview, target);

                bool frame = _frameOnPreviewTargetId == preview.TargetId;
                _frameOnPreviewTargetId = null;
                if (frame) FramePlottedRoute(preview, target);
                return;
            }

            RevealPrimedIfAsked();
        }

        /// <summary>
        /// Adopt and draw a selection's warmed plan once the click that asked
        /// for the route is waiting on it. While no click is waiting the prime
        /// stays hidden, which is the whole point of warming it: a player who
        /// only reads the panel sees an idle session. A prime that can no longer
        /// be adopted — the clock ran past its freshness window, or the drive
        /// changed — is replaced by the plot the click asked for.
        /// </summary>
        private void RevealPrimedIfAsked()
        {
            string targetId = _frameOnPreviewTargetId;
            if (targetId == null) return;

            if (Flight.TryAdoptPrimedPreview(targetId, out TrajectoryPlan primed))
            {
                _frameOnPreviewTargetId = null;
                BodyRuntime target = Model?.Get(primed.TargetId);
                ShowPlan(primed, target);
                FramePlottedRoute(primed, target);
                return;
            }

            // Still solving the selection's plan, or already building the plan
            // this click asked for (a plot or a style/burn rebuild).
            if (Flight.IsPreviewPending(targetId)) return;

            _frameOnPreviewTargetId = null;
            Flight.PlotAsync(targetId, Clock.JulianDate);
            _frameOnPreviewTargetId = targetId;
        }

        /// <summary>
        /// Ease the view to a pose that contains the whole plotted route. The
        /// target's position on the arrival date joins the fit, so the arrival
        /// ghost is in shot with the ship's path. If the route is degenerate
        /// the old body framing is the fallback.
        /// </summary>
        private void FramePlottedRoute(TrajectoryPlan plan, BodyRuntime target)
        {
            if (Rig == null || plan == null) return;

            _routePoints.Clear();
            plan.SamplePath(RouteFraming.SamplesPerArc, _routePoints);

            Vector3d? arrivalM = null;
            if (Model != null && target != null)
            {
                Model.ComputeState(target, plan.ArrivalJulianDate,
                    out Vector3d arrivalPosition, out _);
                arrivalM = arrivalPosition;
            }

            if (!Rig.FrameRoute(_routePoints, ShipPositionM, arrivalM))
                Rig.Frame(target);
        }

        /// <summary>How the nav computer brings the ship into orbit: the cheap
        /// coasted conic capture, or the conic coasts with one fixed-thrust
        /// capture burn and an impulse trim.</summary>
        public CaptureStyle ArrivalStyle =>
            Flight != null ? Flight.ArrivalStyle : CaptureStyle.Conic;

        /// <summary>The chosen fixed-burn level in g, or the planner's default
        /// when there is no flight session yet.</summary>
        public double BurnLevelG =>
            Flight != null ? Flight.BurnLevelG : TripPlanner.DefaultBurnLevelG;

        /// <summary>
        /// Switch the arrival style. A plotted preview is rebuilt against the
        /// new style on the background planner — the HUD draws it through
        /// <see cref="ApplyPreviewIfReady"/> — and a live flight is left alone,
        /// because it keeps the plan it was begun with. A change of shape is a
        /// new drawing of the route, so the camera follows the rebuilt plan as
        /// it lands.
        /// </summary>
        public void SetArrivalStyle(CaptureStyle style)
        {
            if (Flight == null || Flight.ArrivalStyle == style) return;

            if (Flight.HasPreview) _frameOnPreviewTargetId = Flight.Preview.TargetId;
            Flight.SetArrivalStyle(style);
        }

        /// <summary>
        /// Set the burn level. A plotted preview is rebuilt on the background
        /// planner from the pinned departure state, because the profile's
        /// departure burn, brake, and hybrid capture all use it. A live flight
        /// is left alone. The rebuilt route keeps the camera, like an arrival
        /// style change.
        /// </summary>
        public void SetBurnLevel(double levelG)
        {
            if (Flight == null) return;

            double previous = Flight.BurnLevelG;
            Flight.SetBurnLevel(levelG);

            // A level the session rounds away rebuilds nothing, so it must not
            // leave a framing request waiting for some later plan.
            if (System.Math.Abs(Flight.BurnLevelG - previous) > 1e-9 && Flight.HasPreview)
                _frameOnPreviewTargetId = Flight.Preview.TargetId;
        }

        /// <summary>Throw away a preview, leaving a live flight alone.</summary>
        public void DiscardPreview()
        {
            if (Flight == null || !Flight.HasPreview) return;

            _frameOnPreviewTargetId = null;
            bool flightIsLive = Flight.Plan != null;
            Flight.DiscardPreview();
            if (!flightIsLive) Trajectory?.Hide();
        }

        private void ShowPlan(TrajectoryPlan plan, BodyRuntime target)
        {
            if (plan == null || Trajectory == null) return;

            if (Model != null && target != null)
            {
                Model.ComputeState(target, plan.ArrivalJulianDate,
                    out Vector3d arrivalPosition, out _);
                Trajectory.Show(plan, arrivalPosition);
            }
            else
            {
                Trajectory.Show(plan);
            }
        }

        /// <summary>Stop the flight and keep the ship where it is.</summary>
        public void CancelFlight()
        {
            if (Flight == null) return;
            Flight.Cancel();
            Trajectory?.Hide();
        }

        /// <summary>Debug free flight: the old inspection camera. A flight is
        /// cancelled before the camera takes the ship over.</summary>
        public void SetFreeFlight(bool enabled)
        {
            FreeFlight = enabled;
            if (Ship != null)
            {
                Ship.MovementEnabled = enabled;
                Ship.InputEnabled = true;
            }

            if (!enabled) return;

            Flight?.Cancel();
            Trajectory?.Hide();
            Rig?.ClearAuto();
        }

        /// <summary>
        /// F: ease the camera to the selected body and leave the ship where it
        /// is. The view follows the body until the player drags or presses
        /// Space; a running flight owns the camera, so the key is refused
        /// while it does.
        /// </summary>
        public void FocusSelected()
        {
            if (Flight != null && Flight.IsFlying) return;
            if (Selected == null || Rig == null) return;

            Rig.FocusBody(Selected, ShipPositionM);
        }

        /// <summary>
        /// J: the debug jump. Ease the ship to eight radii from the selected
        /// body and frame it there. This moves the ship, so a running flight
        /// refuses it.
        /// </summary>
        public void JumpToSelected()
        {
            // A running flight owns the ship; a teleport would destroy it.
            if (Flight != null && Flight.IsFlying) return;

            BodyRuntime target = Selected ?? Model.Get("earth") ?? Model.Sun;
            if (target == null) return;

            Flight?.Cancel();
            Trajectory?.Hide();
            SetFreeFlight(true);
            if (Focus != null) Focus.Focus(target);
        }

        public void ResetToGateway()
        {
            if (Model == null || Flight == null) return;

            Focus?.Cancel();
            Flight.Cancel();
            Trajectory?.Hide();
            Flight.PlaceAt("gateway", Clock.JulianDate);

            BodyRuntime earth = Model.Get("earth");
            if (earth != null) Rig?.SnapTo(ShipPositionM, earth);

            RequestedScaleIndex = 0;
            Clock.SetScaleIndex(0);
        }

        public BodyRuntime NearestBody()
        {
            return Model.FindNearest(ShipPositionM);
        }

        /// <summary>
        /// Select a body, which is the player's way of naming a destination.
        /// The plan for it is warmed in the background here, so the first — and
        /// only slow — solve is over by the time the player asks to see the
        /// route. Nothing on screen changes until they do: no route, no flight
        /// panel, no slider. Selecting the same body again keeps the warmed
        /// plan or the preview that is already published.
        /// </summary>
        public void Select(BodyRuntime body)
        {
            if (body == null) return;
            if (body != Selected)
            {
                Focus?.Cancel();
                if (Flight != null && Flight.HasPreview && Flight.Preview.TargetId != body.Id)
                    DiscardPreview();
            }

            Selected = body;
            PrimeRoute(body);
        }
    }
}
