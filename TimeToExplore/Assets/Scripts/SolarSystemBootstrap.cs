using System.Collections.Generic;
using UnityEngine;
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
            Selected = _targets[next];
        }

        /// <summary>Start a Go To flight to a body. Raises the clock so the trip
        /// is watchable, and draws the plan. The ship's position becomes the
        /// flight session's for the duration. Begin always replans, so it
        /// consumes a plotted preview and flies the fresh plan.</summary>
        public void BeginGoTo(BodyRuntime body)
        {
            if (body == null || Flight == null || Model == null) return;
            if (body.Gm <= 0.0) return;

            Focus?.Cancel();
            SetFreeFlight(false);
            Ship.InputEnabled = false;

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
        /// Plot a route for inspection without starting the flight. The preview
        /// never moves the ship; Begin flight replans from the current state.
        /// The plan is built on the background planner (see
        /// <see cref="FlightSession.PlotAsync"/>), and the HUD's per-frame
        /// <see cref="ApplyPreviewIfReady"/> draws it once it is ready.
        /// </summary>
        public void PlotRoute(BodyRuntime body)
        {
            if (body == null || Flight == null || Model == null) return;
            if (body.Gm <= 0.0) return;

            Flight.PlotAsync(body.Id, Clock.JulianDate);
        }

        /// <summary>Begin the plotted route. Replans from the current state and
        /// date, so the flown curve is close to the preview but never stale, and
        /// keeps the flight time the player chose on the slider.</summary>
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
        /// Draw a finished background preview, on the main thread. The
        /// planner worker hands its plan here once per HUD update; the HUD
        /// never touches the worker, and nothing it produces is shown before
        /// now. A failed build is logged and leaves the published preview in
        /// place. Publishing draws the route the same way the synchronous
        /// <see cref="PlotRoute"/> path always did; warp is raised by
        /// <see cref="BeginGoTo"/>, not by a plot.
        /// </summary>
        public void ApplyPreviewIfReady()
        {
            if (Flight == null) return;

            string failure = Flight.TakePreviewFailure();
            if (!string.IsNullOrEmpty(failure))
                Debug.LogWarning("Preview plan failed: " + failure);

            if (!Flight.TryApplyCompletedPreview(out TrajectoryPlan preview)) return;

            BodyRuntime target = Model?.Get(preview.TargetId);
            ShowPlan(preview, target);
            FramePlottedRoute(preview, target);
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
        /// because it keeps the plan it was begun with. The camera is not
        /// re-framed: the choice changes the arrival, not the viewer's aim.
        /// </summary>
        public void SetArrivalStyle(CaptureStyle style)
        {
            if (Flight == null || Flight.ArrivalStyle == style) return;

            Flight.SetArrivalStyle(style);
        }

        /// <summary>
        /// Set the burn level. A plotted preview is rebuilt on the background
        /// planner from the pinned departure state, because the profile's
        /// departure burn, brake, and hybrid capture all use it. A live flight
        /// is left alone.
        /// </summary>
        public void SetBurnLevel(double levelG)
        {
            if (Flight == null) return;

            Flight.SetBurnLevel(levelG);
        }

        /// <summary>Throw away a preview, leaving a live flight alone.</summary>
        public void DiscardPreview()
        {
            if (Flight == null || !Flight.HasPreview) return;

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

        public void FocusSelected()
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
        }
    }
}
