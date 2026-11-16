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
        public BodyRuntime Selected { get; private set; }
        public bool ShowHud = true;
        public bool ShowLabels = true;

        /// <summary>
        /// Development switch for the system map. Off keeps today's orbit
        /// camera; on enables the blended top-down pose, pan, and the extended
        /// zoom. Serialized per scene so the comparison scene can opt in while
        /// the shipped scene stays on the old camera. A later step removes the
        /// switch (or defaults it on) once the comparison is done.
        /// </summary>
        public bool EnableMapView = false;

        /// <summary>Debug free flight. A flight owns the ship while it runs.</summary>
        public bool FreeFlight { get; private set; }

        /// <summary>The scale the player asked for, before the warp policy caps it.</summary>
        public int RequestedScaleIndex { get; private set; }

        /// <summary>Set when the warp policy is holding the clock below the request.</summary>
        public string WarpNote { get; private set; }

        /// <summary>RK4 substeps one frame may spend on the ship.</summary>
        public int SubstepsPerFrame = FlightSession.DefaultSubstepsPerFrame;

        public Vector3d ShipPositionM => Flight != null ? Flight.State.PositionM : Vector3d.Zero;
        public Vector3d ShipVelocityMps => Flight != null ? Flight.State.VelocityMps : Vector3d.Zero;

        private readonly List<BodyRuntime> _targets = new List<BodyRuntime>();
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
            Rig.MapEnabled = EnableMapView;
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

            Renderer.Refresh(ShipPositionM, Clock.JulianDate);
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

        private void Update()
        {
            if (Model == null) return;

            HandleHotkeys();
            if (Flight == null) return;

            float deltaTime = Time.deltaTime;

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

            FlightStep step = Flight.Advance(Clock.JulianDate, SubstepsPerFrame);
            if (step.ReachedJulianDate < Clock.JulianDate)
                Clock.JulianDate = step.ReachedJulianDate;

            Model.Update(Clock.JulianDate);
        }

        private void LateUpdate()
        {
            if (Renderer == null) return;

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

            if (Rig != null) Rig.Apply(ShipPositionM, Time.deltaTime);

            if (ShipMarker != null)
            {
                ShipMarker.Refresh(ViewCamera,
                    Flight != null ? Flight.CurrentThrust : ThrustCommand.None,
                    ShipVelocityMps);
            }

            Renderer.Refresh(ShipPositionM, Clock.JulianDate);
            if (Trajectory != null) Trajectory.Refresh(ShipPositionM, ViewCamera);
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Space)) Clock.TogglePause();

            for (int i = 0; i < SimulationClock.TimeScales.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) SetScaleIndex(i);
            }

            if (Input.GetKeyDown(KeyCode.Tab)) CycleTarget(1);
            if (Input.GetKeyDown(KeyCode.F)) FocusSelected();
            if (Input.GetKeyDown(KeyCode.Escape)) Focus?.Cancel();
            if (Input.GetKeyDown(KeyCode.R)) ResetToGateway();
            if (Input.GetKeyDown(KeyCode.X)) CancelFlight();
            if (Input.GetKeyDown(KeyCode.BackQuote)) SetFreeFlight(!FreeFlight);
            if (Input.GetKeyDown(KeyCode.T)) Renderer.TrueScale = !Renderer.TrueScale;
            if (Input.GetKeyDown(KeyCode.O))
            {
                Renderer.ShowOrbits = !Renderer.ShowOrbits;
                Renderer.ShowMoonOrbits = Renderer.ShowOrbits;
            }
            if (Input.GetKeyDown(KeyCode.L)) ShowLabels = !ShowLabels;
            if (Input.GetKeyDown(KeyCode.H)) ShowHud = !ShowHud;
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
        /// flight session's for the duration.</summary>
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
                Trajectory.Show(plan, _gravity);
                Rig?.Frame(body);
                SetScaleIndex(WarpPolicy.SuggestInitialScaleIndex(plan));
            }
            else
            {
                Trajectory.Hide();
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
            if (body != Selected) Focus?.Cancel();
            Selected = body;
        }
    }
}
