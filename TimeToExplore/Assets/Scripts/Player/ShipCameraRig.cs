using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.UI;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Drives the view: right-drag orbits the ship, the wheel zooms toward the
    /// pointer, and a plan, a debug jump, a reset, or `Space` frames its
    /// destination automatically until the player takes the camera back.
    /// Plotting a route instead eases the view to a pose that contains the
    /// whole route, looking across the plane it lies in.
    ///
    /// The wheel requests a distance rather than placing one: the camera eases
    /// to it over <see cref="ZoomEaseRatePerSecond"/> (see
    /// <see cref="OrbitCamera.EaseDistance"/>). The point under the pointer on
    /// the look-at plane is anchored, and the look-at is solved from it every
    /// frame, so that point stays under the pointer through the whole tween.
    /// Yaw and pitch are never written by the zoom, so the player's angle is
    /// the same at every distance.
    ///
    /// The ship never moves in the render frame, so orbiting is just placing
    /// the camera on a sphere around the origin; the world is untouched.
    /// </summary>
    public class ShipCameraRig : MonoBehaviour
    {
        /// <summary>Degrees of orbit per unit of mouse delta.</summary>
        public float OrbitDegreesPerMouseUnit = 3f;

        /// <summary>Distance multiplier per mouse-wheel notch.</summary>
        public float ZoomFactorPerStep = 1.25f;

        /// <summary>How fast the auto-framing turns the view, in degrees per second.</summary>
        public float AutoSlewDegreesPerSecond = 45f;

        /// <summary>How fast the auto-framing eases the orbit distance.</summary>
        public float AutoZoomRate = 2.5f;

        /// <summary>
        /// How fast a wheel zoom closes on the distance it requested, per
        /// second. One tween smooths the whole pose change.
        /// </summary>
        public float ZoomEaseRatePerSecond = 7f;

        /// <summary>
        /// How long Space's return home takes, in seconds. The zoom and the
        /// look-at unwind follow one smoothstep, so the view starts and ends at
        /// rest.
        /// </summary>
        public float HomeDurationSeconds = HomeTransition.DefaultDurationSeconds;

        /// <summary>
        /// How long the move to the selected body takes, in seconds. F starts
        /// it; the aim, the distance, and the look-at point follow one
        /// smoothstep, like Space's return home in the other direction.
        /// </summary>
        public float BodyFocusDurationSeconds = 1f;

        /// <summary>
        /// How long the move to a plotted route's overview takes, in seconds.
        /// The aim, the distance, and the look-at point follow one smoothstep.
        /// </summary>
        public float RouteViewDurationSeconds = RouteViewTransition.DefaultDurationSeconds;

        /// <summary>How long the move to the flight's follow pose takes.</summary>
        public float FlightViewDurationSeconds = 1.2f;

        /// <summary>
        /// The home view sits at this fraction of the local radius: close
        /// enough to read the ship's neighbourhood, far enough out not to be
        /// inside it.
        /// </summary>
        public const float HomeLocalRadiusFraction = 0.5f;

        public OrbitCamera Orbit { get; } = new OrbitCamera();

        /// <summary>The eased return to the ship that Space starts.</summary>
        public HomeTransition Home { get; } = new HomeTransition();

        /// <summary>The eased move to a plotted route's overview.</summary>
        public RouteViewTransition RouteView { get; } = new RouteViewTransition();

        /// <summary>
        /// The eased move to the flight's chase pose. It reuses the route
        /// transition's maths: yaw, pitch, distance, and look-at on one
        /// smoothstep.
        /// </summary>
        public RouteViewTransition FlightView { get; } = new RouteViewTransition();

        /// <summary>
        /// The eased move to the selected body. It reuses the route-view pose
        /// transition, because the maths is the same: shortest yaw, linear
        /// pitch and look-at, geometric distance, one smoothstep.
        /// </summary>
        public RouteViewTransition BodyFocus { get; } = new RouteViewTransition();

        /// <summary>True while Space's home transition is easing the view back.</summary>
        public bool IsHoming => Home.IsActive;

        /// <summary>True while the view is easing to a plotted route.</summary>
        public bool IsFramingRoute => RouteView.IsActive;

        /// <summary>True while the view is easing to the flight's chase pose.</summary>
        public bool IsFramingFlight => FlightView.IsActive;

        /// <summary>True while the flight owns the orbit distance. A drag keeps
        /// it; a wheel, a home press, or the arrival ends it.</summary>
        public bool IsFollowingFlight => _flightDistanceFollow;

        /// <summary>True while the flight owns the view's composition. A wheel
        /// keeps it (the ship stays centred) but takes the distance.</summary>
        public bool IsFlightView => _flightViewActive;

        /// <summary>True while F's move to the selected body is easing.</summary>
        public bool IsFocusingBody => BodyFocus.IsActive;

        /// <summary>
        /// The body whose position is the render origin while focused, or null
        /// in the ordinary ship-relative frame. The origin is not the camera
        /// target: the body itself is at zero and the camera orbits it.
        /// </summary>
        public BodyRuntime FocusTarget { get; private set; }

        /// <summary>True while the render frame is centred on a body.</summary>
        public bool IsFocused => FocusTarget != null;

        /// <summary>Render units per metre.</summary>
        public float UnitsPerMeter { get; set; } = 1e-6f;

        /// <summary>The body the camera is framing, or null for free orbit.</summary>
        public BodyRuntime AutoTarget { get; private set; }

        /// <summary>True while the view turns to keep the target centred.</summary>
        public bool AutoAim { get; private set; }

        /// <summary>True while the orbit distance follows the target.</summary>
        public bool AutoDistance { get; private set; }

        /// <summary>
        /// The distance the automatic framing is easing toward. Captured from
        /// the ship-to-target distance when framing starts (or when the flight
        /// arrives), not recomputed every frame, so a moving target cannot keep
        /// pulling the view in under the player.
        /// </summary>
        public float AutoDistanceTargetUnits { get; private set; }

        /// <summary>True while either part of the framing is following.</summary>
        public bool Auto => AutoAim || AutoDistance;

        /// <summary>The camera's look-at offset from the ship, in ecliptic metres.</summary>
        public Vector3d LookAtOffsetM { get; private set; }

        /// <summary>The same look-at offset in render units.</summary>
        public Vector3 LookAtOffsetUnits => (LookAtOffsetM * UnitsPerMeter).ToUnity();

        /// <summary>The look-at point relative to the ship, in render units.</summary>
        public Vector3 LookAtUnits { get; private set; }

        /// <summary>The camera position placed on the last frame, relative to the ship.</summary>
        public Vector3 PosePositionUnits { get; private set; }

        /// <summary>The camera rotation placed on the last frame.</summary>
        public Quaternion PoseRotation { get; private set; } = Quaternion.identity;

        /// <summary>The near plane placed on the last frame, in render units.</summary>
        public float PoseNearClipUnits { get; private set; }

        private Camera _camera;
        private SolarSystemModel _model;
        private bool _orbiting;
        private bool _zoomPending;
        private bool _zoomAnchorActive;
        private bool _autoDistancePending;
        private bool _flightDistanceFollow;
        private bool _flightViewActive;
        private float _flightDistanceTarget = FlightFraming.DistanceUnits;
        private Vector3 _zoomAnchorUnits;
        private Vector2 _zoomAnchorNdc;
        private float _zoomAnchorAspect = 1f;
        private float _zoomAnchorFieldOfViewDegrees = 60f;
        private bool _uiOverLastFrame;
        private bool _cursorCaptured;

        public void Initialise(Camera camera, SolarSystemModel model, float unitsPerMeter)
        {
            _camera = camera;
            _model = model;
            UnitsPerMeter = unitsPerMeter;
            _zoomPending = false;
            _zoomAnchorActive = false;
            Home.Cancel();
            Orbit.SnapDistance(Orbit.DistanceUnits);

            if (_camera != null)
            {
                _camera.transform.rotation = Orbit.Rotation;
                _camera.farClipPlane = OrbitCamera.FarClipUnits;
            }
        }

        /// <summary>Frame a body and follow it until the player takes over.</summary>
        public void Frame(BodyRuntime body)
        {
            // A new framing target takes the view over from the player's
            // cursor-anchored zoom, the route overview, the body focus, or
            // home transition.
            _zoomAnchorActive = false;
            ExitFocus();
            RouteView.Cancel();
            FlightView.Cancel();
            _flightDistanceFollow = false;
            _flightViewActive = false;
            Home.Cancel();

            AutoTarget = body;
            AutoAim = body != null;
            AutoDistance = body != null;
            _autoDistancePending = body != null;
        }

        /// <summary>
        /// Capture a new framing distance from the current ship position. Called
        /// once when framing starts, and again when a flight arrives; between
        /// those events the distance is held so a moving target cannot keep
        /// winding the zoom in.
        /// </summary>
        public void RecalculateFramingDistance(Vector3d shipPositionM)
        {
            if (AutoTarget == null) return;

            AutoDistanceTargetUnits = AutoDistanceUnits(shipPositionM, AutoTarget);
            _autoDistancePending = false;
        }

        /// <summary>
        /// Ease the view to the chase pose for a flight. The direction is the
        /// route ahead of the ship; the pose is
        /// <see cref="FlightFraming"/>'s: behind, 20° above, and 30° to one
        /// side, at a close distance. The angle is chosen once, so the camera
        /// follows the ship without hunting as the route curves. Input, a new
        /// framing target, the arrival frame, or Space cancels it.
        /// </summary>
        public bool FrameFlight(Vector3d flightDirection)
        {
            if (!FlightFraming.Pose(flightDirection, out float yaw, out float pitch))
                return false;

            // The flight owns the view now: no body follow, no wheel request,
            // and no cursor anchor left over from the previous zoom.
            AutoAim = false;
            AutoDistance = false;
            AutoTarget = null;
            _autoDistancePending = false;
            _zoomPending = false;
            _zoomAnchorActive = false;
            ExitFocus();
            Home.Cancel();
            RouteView.Cancel();
            BodyFocus.Cancel();

            FlightView.DurationSeconds = Mathf.Max(0f, FlightViewDurationSeconds);
            _flightViewActive = true;
            _flightDistanceFollow = true;
            _flightDistanceTarget = FlightFraming.DistanceUnits;
            FlightView.Begin(Orbit.YawDegrees, Orbit.PitchDegrees, Orbit.DistanceUnits,
                LookAtOffsetM, yaw, pitch, FlightFraming.DistanceUnits, Vector3d.Zero);
            return true;
        }

        /// <summary>
        /// Set the chase distance for the current frame. The bootstrap calls
        /// this while a flight is live with a target that grows as the warp
        /// rises, so the route slides across the view instead of jumping a
        /// screen or more per frame. A wheel or Space ends the follow.
        /// </summary>
        public void FollowFlight(float distanceUnits)
        {
            _flightDistanceTarget = Mathf.Clamp(distanceUnits,
                OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits);
        }

        /// <summary>Stop following; the player owns the view.</summary>
        public void ClearAuto()
        {
            _zoomAnchorActive = false;
            ExitFocus();
            RouteView.Cancel();
            FlightView.Cancel();
            _flightDistanceFollow = false;
            _flightViewActive = false;
            Home.Cancel();

            AutoAim = false;
            AutoDistance = false;
            AutoTarget = null;
            _autoDistancePending = false;
        }

        /// <summary>Place the view without a transition, for a reset or a start.</summary>
        public void SnapTo(Vector3d shipPositionM, BodyRuntime body)
        {
            Frame(body);
            _zoomPending = false;
            _zoomAnchorActive = false;
            CancelBodyFocus();
            RouteView.Cancel();
            FlightView.Cancel();
            _flightDistanceFollow = false;
            _flightViewActive = false;
            Home.Cancel();
            if (body != null)
            {
                Orbit.AimAlong(body.PositionM - shipPositionM);
                RecalculateFramingDistance(shipPositionM);
                Orbit.SnapDistance(Mathf.Clamp(AutoDistanceTargetUnits,
                    OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits));
            }

            LookAtOffsetM = Vector3d.Zero;
            ApplyToCamera();
        }

        /// <summary>
        /// Start the eased return to the ship: drop any framing, forget the
        /// cursor anchor, then ease the distance to a local view while the
        /// look-at offset unwinds. Space calls this. The player's yaw and pitch
        /// are kept, so the ship returns to the centre of the same remembered
        /// angle.
        /// </summary>
        public void HomeToShip(Vector3d shipPositionM)
        {
            // Leaving a focused body also changes the render origin, so there
            // is no continuous camera path back to the ship. Land on the home
            // view directly; the content cut is the cost of the origin move.
            if (FocusTarget != null)
            {
                ExitFocus();
                AutoAim = false;
                AutoDistance = false;
                _zoomPending = false;
                _zoomAnchorActive = false;
                RouteView.Cancel();
                FlightView.Cancel();
                _flightDistanceFollow = false;
                _flightViewActive = false;
                Home.Cancel();
                LookAtOffsetM = Vector3d.Zero;
                Orbit.SnapDistance(HomeDistanceUnits(LocalRadiusM(shipPositionM)));
                return;
            }

            // Take the view from the framing exactly as a drag does: clear the
            // follow flags but keep AutoTarget, so an active flight's next
            // frame does not re-claim the camera and cancel the transition.
            AutoAim = false;
            AutoDistance = false;
            _zoomPending = false;
            _zoomAnchorActive = false;
            CancelBodyFocus();
            RouteView.Cancel();
            FlightView.Cancel();
            _flightDistanceFollow = false;
            _flightViewActive = false;

            Home.DurationSeconds = Mathf.Max(0f, HomeDurationSeconds);
            Home.Begin(Orbit.DistanceUnits,
                HomeDistanceUnits(LocalRadiusM(shipPositionM)),
                LookAtOffsetM);
        }

        /// <summary>
        /// Frame the selected body by making it the render origin, without
        /// moving the ship. F calls this. The body is at zero, the camera keeps
        /// the player's yaw and pitch, and the distance eases to
        /// <see cref="FocusDistanceUnits"/>; if the old ship-frame distance was
        /// already wider, it starts there and zooms in. Space leaves the focus
        /// and returns to the ship's local view.
        ///
        /// The origin switch is the point: a body at interplanetary distance is
        /// far below float precision when the ship is the origin, so no amount
        /// of camera zoom can show its surface. The simulation ship stays put;
        /// only the render frame moves. The debug jump (`J`) still moves the
        /// ship when the player wants to be there physically.
        /// </summary>
        public void FocusBody(BodyRuntime body)
        {
            if (body == null) return;

            // The focus owns the whole pose until it lands. Drop the cursor
            // anchor, any wheel request, a route overview, and the home move.
            _zoomPending = false;
            _zoomAnchorActive = false;
            Home.Cancel();
            RouteView.Cancel();
            FlightView.Cancel();
            _flightDistanceFollow = false;
            _flightViewActive = false;
            BodyFocus.Cancel();
            AutoAim = false;
            AutoDistance = false;
            AutoTarget = null;
            _autoDistancePending = false;

            FocusTarget = body;
            SetLookAtOffsetM(Vector3d.Zero);

            float focusDistance = FocusDistanceUnits(body);
            float startDistance = Mathf.Max(Orbit.DistanceUnits, focusDistance);
            Orbit.SnapDistance(startDistance);

            BodyFocus.DurationSeconds = Mathf.Max(0f, BodyFocusDurationSeconds);
            BodyFocus.Begin(Orbit.YawDegrees, Orbit.PitchDegrees, startDistance,
                Vector3d.Zero, Orbit.YawDegrees, Orbit.PitchDegrees, focusDistance,
                Vector3d.Zero);
        }

        /// <summary>
        /// Leave the focused body and return the render origin to the ship. The
        /// caller should use <see cref="HomeToShip"/> when the camera should
        /// also land at the ship's local view.
        /// </summary>
        public void ExitFocus()
        {
            FocusTarget = null;
            BodyFocus.Cancel();
        }

        /// <summary>Stop the body-focus move but keep the focused origin.</summary>
        private void CancelBodyFocus()
        {
            BodyFocus.Cancel();
        }

        /// <summary>
        /// Ease the view to a pose that contains the whole plotted route. The
        /// pose is computed from the sampled path and the target's arrival
        /// position by <see cref="RouteFraming"/>: its centre becomes the
        /// look-at point, its plane chooses the viewing angle, and its projected
        /// extent sets the distance. Input, a new framing target, or Space
        /// cancels the move.
        /// </summary>
        public bool FrameRoute(IReadOnlyList<Vector3d> routePointsM,
            Vector3d shipPositionM, Vector3d? targetArrivalM)
        {
            if (routePointsM == null || routePointsM.Count < 2) return false;

            ExitFocus();
            Vector2 screen = ScreenPixels();
            RouteFraming.Frame frame = RouteFraming.Compute(routePointsM, shipPositionM,
                targetArrivalM, UnitsPerMeter, FieldOfViewDegrees, Aspect(screen),
                Orbit.YawDegrees, Orbit.PitchDegrees, OrbitCamera.MaxDistanceUnits);
            if (!frame.IsValid) return false;

            // The route owns the view now: no body follow, no wheel request,
            // and no cursor anchor left over from the previous zoom.
            AutoAim = false;
            AutoDistance = false;
            AutoTarget = null;
            CancelBodyFocus();
            FlightView.Cancel();
            _flightDistanceFollow = false;
            _flightViewActive = false;
            Home.Cancel();
            _zoomPending = false;
            _zoomAnchorActive = false;

            RouteView.DurationSeconds = Mathf.Max(0f, RouteViewDurationSeconds);
            RouteView.Begin(Orbit.YawDegrees, Orbit.PitchDegrees, Orbit.DistanceUnits,
                LookAtOffsetM, frame.YawDegrees, frame.PitchDegrees,
                frame.DistanceUnits, frame.LookAtOffsetM);
            return true;
        }

        /// <summary>
        /// The distance a home press targets: half the local radius, clamped to
        /// the orbit range, so the return lands in the ship's own neighbourhood.
        /// </summary>
        public float HomeDistanceUnits(double localRadiusM)
        {
            if (!(localRadiusM > 0.0) || double.IsInfinity(localRadiusM))
                localRadiusM = LocalScale.DeepSpaceRadiusM;

            double distanceM = HomeLocalRadiusFraction * localRadiusM;
            return Mathf.Clamp((float)(distanceM * UnitsPerMeter),
                OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits);
        }

        /// <summary>One frame: read input, follow the target, place the camera.</summary>
        public void Apply(Vector3d shipPositionM, float deltaTime)
        {
            HandleInput();

            // Space's return home owns the distance and the look-at offset until
            // it lands. One smoothstep drives both, so they start and stop
            // together, at rest.
            if (Home.IsActive)
            {
                bool arrived = Home.Advance(deltaTime);
                Orbit.SnapDistance(Home.DistanceUnitsAt);
                SetLookAtOffsetM(Home.LookAtOffsetMAt);

                if (arrived)
                {
                    Orbit.SnapDistance(Home.TargetDistanceUnits);
                    LookAtOffsetM = Vector3d.Zero;
                }
            }
            else if (BodyFocus.IsActive)
            {
                // F's move to the focused body owns the pose until it lands.
                // The body stays at the origin, so no follow target is needed;
                // the bootstrap keeps the origin on the body each frame.
                bool arrived = BodyFocus.Advance(deltaTime);
                Orbit.YawDegrees = BodyFocus.YawDegreesAt;
                Orbit.PitchDegrees = BodyFocus.PitchDegreesAt;
                Orbit.SnapDistance(BodyFocus.DistanceUnitsAt);
                SetLookAtOffsetM(BodyFocus.LookAtOffsetMAt);

                if (arrived)
                {
                    Orbit.YawDegrees = BodyFocus.TargetYawDegrees;
                    Orbit.PitchDegrees = BodyFocus.TargetPitchDegrees;
                    Orbit.SnapDistance(BodyFocus.TargetDistanceUnits);
                    SetLookAtOffsetM(BodyFocus.TargetLookAtOffsetM);
                }
            }
            else if (RouteView.IsActive)
            {
                // The route overview owns the aim, the distance, and the
                // look-at point together, so the whole pose eases toward the
                // fit rather than the aim arriving before the zoom.
                bool arrived = RouteView.Advance(deltaTime);
                Orbit.YawDegrees = RouteView.YawDegreesAt;
                Orbit.PitchDegrees = RouteView.PitchDegreesAt;
                Orbit.SnapDistance(RouteView.DistanceUnitsAt);
                SetLookAtOffsetM(RouteView.LookAtOffsetMAt);

                if (arrived)
                {
                    Orbit.YawDegrees = RouteView.TargetYawDegrees;
                    Orbit.PitchDegrees = RouteView.TargetPitchDegrees;
                    Orbit.SnapDistance(RouteView.TargetDistanceUnits);
                    SetLookAtOffsetM(RouteView.TargetLookAtOffsetM);
                }
            }
            else if (FlightView.IsActive)
            {
                // The flight's chase pose follows the same transition, so the
                // camera settles into the angle rather than cutting to it.
                bool arrived = FlightView.Advance(deltaTime);
                Orbit.YawDegrees = FlightView.YawDegreesAt;
                Orbit.PitchDegrees = FlightView.PitchDegreesAt;
                Orbit.SnapDistance(FlightView.DistanceUnitsAt);
                SetLookAtOffsetM(FlightView.LookAtOffsetMAt);

                if (arrived)
                {
                    Orbit.YawDegrees = FlightView.TargetYawDegrees;
                    Orbit.PitchDegrees = FlightView.TargetPitchDegrees;
                    Orbit.SnapDistance(FlightView.TargetDistanceUnits);
                    SetLookAtOffsetM(FlightView.TargetLookAtOffsetM);
                }
            }
            else if (_flightDistanceFollow)
            {
                // The chase distance tracks the frame's travel so the route
                // slides rather than jumps as the warp rises. A drag keeps it
                // (only the angle is the player's); a wheel ends it.
                Orbit.RequestDistance(_flightDistanceTarget);
                Orbit.EaseDistance(deltaTime, AutoZoomRate);
            }
            else if (_zoomPending && !Orbit.EaseDistance(deltaTime, ZoomEaseRatePerSecond))
            {
                // A wheel notch only requests a distance. Ease it before the
                // rest of the frame reads the pose, so the whole view follows
                // one short tween instead of snapping with the notch.
                _zoomPending = false;
            }

            // A pending wheel zoom owns the distance until it settles; the
            // automatic framing resumes after that. The home transition owns
            // the view outright.
            if (!Home.IsActive && AutoTarget != null && !_zoomPending)
            {
                Vector3d toTarget = AutoTarget.PositionM - shipPositionM;

                if (AutoAim && toTarget.SqrMagnitude > 1e-6)
                    Orbit.SlewToLookDirection(toTarget, AutoSlewDegreesPerSecond, deltaTime);

                if (AutoDistance)
                {
                    // Capture the framing distance on the first frame after
                    // Frame/SnapTo. After that it is held: a flight approaching
                    // its target gets a stable view, and the distance is
                    // recalculated once on arrival instead of every frame.
                    if (_autoDistancePending) RecalculateFramingDistance(shipPositionM);

                    Orbit.RequestDistance(AutoDistanceTargetUnits);
                    Orbit.EaseDistance(deltaTime, AutoZoomRate);
                }
            }

            // Solve the look-at from the anchor last, after the aim has moved,
            // so the point stays pinned through the frame.
            ApplyZoomAnchor();

            ApplyToCamera();
        }

        /// <summary>
        /// How far back to sit when framing a body: close enough that the body
        /// fills a useful part of the view, far enough that the whole approach
        /// reads. 60% of the remaining distance, never inside three radii.
        /// </summary>
        public float AutoDistanceUnits(Vector3d shipPositionM, BodyRuntime target)
        {
            double toTarget = Vector3d.Distance(shipPositionM, target.PositionM);
            double distanceM = System.Math.Clamp(
                0.6 * toTarget,
                3.0 * System.Math.Max(target.RadiusM, 1.0),
                5.0e11); // 3.3 AU, enough to see a whole inner-system plan
            return (float)(distanceM * UnitsPerMeter);
        }

        /// <summary>
        /// How far the camera sits from a focused body: four radii, so the disc
        /// fills about half the view and the atmosphere has room at the limb.
        /// The orbit range still bounds it, so a tiny asteroid is not framed
        /// from inside the near plane.
        /// </summary>
        public float FocusDistanceUnits(BodyRuntime body)
        {
            if (body == null) return OrbitCamera.DefaultDistanceUnits;

            double distanceM = 4.0 * System.Math.Max(body.RadiusM, 1.0);
            return Mathf.Clamp((float)(distanceM * UnitsPerMeter),
                OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits);
        }

        /// <summary>
        /// The pointer gestures for one frame, supplied by the caller so the
        /// orbit routing is verified without hardware.
        /// </summary>
        public struct OrbitInput
        {
            public bool RightPressed;
            public bool RightReleased;

            /// <summary>The seam's drag delta: raw pixels times the 0.1 sensitivity.</summary>
            public Vector2 Delta;

            /// <summary>
            /// True when the pointer is over a uGUI panel. Only a press edge
            /// reads it: a gesture that begins on a panel belongs to the panel
            /// until the button is released, even if the pointer leaves it.
            /// </summary>
            public bool OverUi;
        }

        /// <summary>
        /// Route one frame of pointer gestures. The right button orbits. A
        /// press over a HUD panel starts no gesture: the panel owns the pointer,
        /// so dragging a slider or a panel edge cannot orbit the camera
        /// underneath it. The ownership lasts until the button is released, so
        /// pulling a slider past the panel's edge does not hand the gesture
        /// back to the camera halfway through.
        ///
        /// A gesture that actually moves cancels the home transition and
        /// releases a cursor-anchored zoom: the player has taken the view back.
        /// </summary>
        public void ApplyOrbitInput(OrbitInput input)
        {
            if (input.RightPressed) _orbiting = !input.OverUi;
            else if (input.RightReleased) _orbiting = false;

            float x = input.Delta.x;
            float y = input.Delta.y;
            if (x == 0f && y == 0f) return;

            if (_orbiting)
            {
                Orbit.Turn(x * OrbitDegreesPerMouseUnit, -y * OrbitDegreesPerMouseUnit);
                AutoAim = false;
                CancelBodyFocus();
                Home.Cancel();
                RouteView.Cancel();
                FlightView.Cancel();
                _zoomAnchorActive = false;
            }
        }

        private void HandleInput()
        {
            // A panel takes the pointer for the whole gesture. The EventSystem
            // can trail by a frame, so a press counts as over the UI when either
            // this frame or the previous one said so.
            bool reportedOverUi = UiInput.PointerOverUi();
            bool overUi = reportedOverUi || _uiOverLastFrame;
            _uiOverLastFrame = reportedOverUi;

            // Only the orbit gesture captures the cursor.
            if (PlayerControls.RightDragStarted && !overUi)
            {
                _cursorCaptured = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (PlayerControls.RightDragEnded && _cursorCaptured)
            {
                _cursorCaptured = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            ApplyOrbitInput(new OrbitInput
            {
                RightPressed = PlayerControls.RightDragStarted,
                RightReleased = PlayerControls.RightDragEnded,
                Delta = PlayerControls.DragDelta,
                OverUi = overUi
            });

            float scroll = PlayerControls.WheelSteps;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                // A panel keeps the pointer, so the wheel zooms about the
                // look-at rather than anchoring to a spot on the panel.
                Vector2 screen = ScreenPixels();
                Vector2 pointer = overUi ? screen * 0.5f : PlayerControls.PointerPosition;
                ApplyWheelSteps(scroll, pointer, screen);
            }
        }

        /// <summary>
        /// Route one wheel gesture and request its distance, anchored at the
        /// screen centre. Exposed so the tween is testable without a device.
        /// </summary>
        public void ApplyWheelSteps(float steps)
        {
            Vector2 screen = ScreenPixels();
            ApplyWheelSteps(steps, screen * 0.5f, screen);
        }

        /// <summary>
        /// Route one wheel gesture and request its distance. Outside a flight
        /// the point under <paramref name="pointerPixels"/> is anchored so it
        /// stays under the pointer through the tween; in the flight's chase the
        /// zoom stays centred on the ship. Pointer and screen are in the Input
        /// System's bottom-left screen pixels. Exposed so the anchor is testable
        /// without a device.
        /// </summary>
        public void ApplyWheelSteps(float steps, Vector2 pointerPixels, Vector2 screenPixels)
        {
            if (steps == 0f) return;

            Home.Cancel();
            RouteView.Cancel();
            FlightView.Cancel();
            _flightDistanceFollow = false;

            // Compound on a pending request so a continuous gesture keeps its
            // 1.25x per notch; start from the placed distance when the last
            // tween has settled. Never compound on a stale auto-framing target.
            float from = _zoomPending ? Orbit.TargetDistanceUnits : Orbit.DistanceUnits;

            if (_flightViewActive)
            {
                // The chase always looks at the ship. A pointer anchor would
                // slide the look-at aside as the distance changes, so the zoom
                // stays centred and the ship stays framed.
                _zoomAnchorActive = false;
                SetLookAtOffsetM(Vector3d.Zero);
            }
            else
            {
                // Take the anchor from the pose on screen now, not from the
                // distance being requested: that is the point the player is
                // looking at. The look-at is solved from it every frame.
                _zoomAnchorNdc = CursorZoom.PointerNdc(pointerPixels, screenPixels);
                _zoomAnchorFieldOfViewDegrees = FieldOfViewDegrees;
                _zoomAnchorAspect = Aspect(screenPixels);
                _zoomAnchorUnits = CursorZoom.CursorPointOnLookAtPlane(LookAtOffsetUnits,
                    Orbit.Rotation,
                    Mathf.Max(Orbit.DistanceUnits, OrbitCamera.MinDistanceUnits),
                    _zoomAnchorNdc, _zoomAnchorFieldOfViewDegrees, _zoomAnchorAspect);
                _zoomAnchorActive = true;
            }

            Orbit.RequestDistance(OrbitCamera.ZoomDistanceUnits(from, steps,
                ZoomFactorPerStep));
            _zoomPending = !Orbit.IsDistanceSettled;
            AutoDistance = false;
        }

        /// <summary>
        /// Solve the look-at from the anchored point for the current distance
        /// and rotation, so the point stays under the pointer while the zoom
        /// eases. No-op without an anchor.
        /// </summary>
        private void ApplyZoomAnchor()
        {
            if (!_zoomAnchorActive) return;

            Vector3 offsetUnits = CursorZoom.AnchoredLookAtUnits(_zoomAnchorUnits,
                Orbit.Rotation,
                Mathf.Max(Orbit.DistanceUnits, OrbitCamera.MinDistanceUnits),
                _zoomAnchorNdc, _zoomAnchorFieldOfViewDegrees, _zoomAnchorAspect);

            SetLookAtOffsetM(Vector3d.FromUnity(offsetUnits) / UnitsPerMeter);
        }

        private void SetLookAtOffsetM(Vector3d offsetM)
        {
            LookAtOffsetM = CursorZoom.ClampLookAtOffsetM(offsetM);
        }

        /// <summary>
        /// The camera's viewport in screen pixels. Falls back to the screen and
        /// then to a 1920×1080 default, so a headless run still has a scale.
        /// </summary>
        private Vector2 ScreenPixels()
        {
            if (_camera != null && _camera.pixelWidth > 0 && _camera.pixelHeight > 0)
                return new Vector2(_camera.pixelWidth, _camera.pixelHeight);

            float width = Screen.width > 0 ? Screen.width : 1920f;
            float height = Screen.height > 0 ? Screen.height : 1080f;
            return new Vector2(width, height);
        }

        private float FieldOfViewDegrees => _camera != null ? _camera.fieldOfView : 60f;

        private float Aspect(Vector2 screenPixels)
        {
            if (_camera != null && _camera.aspect > 0f) return _camera.aspect;
            return screenPixels.y > 0f ? screenPixels.x / screenPixels.y : 1f;
        }

        private double LocalRadiusM(Vector3d shipPositionM)
        {
            return _model != null
                ? LocalScale.RadiusM(_model, shipPositionM)
                : LocalScale.DeepSpaceRadiusM;
        }

        private void ApplyToCamera()
        {
            float distanceUnits = Mathf.Max(Orbit.DistanceUnits, OrbitCamera.MinDistanceUnits);
            Vector3 offset = Orbit.OffsetDirection * distanceUnits;

            Vector3 offsetUnits = LookAtOffsetUnits;

            LookAtUnits = offsetUnits;
            PosePositionUnits = offsetUnits + offset;
            PoseRotation = Orbit.Rotation;
            PoseNearClipUnits = OrbitCamera.NearClipUnits(offset.magnitude);

            if (_camera == null) return;

            _camera.transform.position = PosePositionUnits;
            _camera.transform.rotation = PoseRotation;
            _camera.nearClipPlane = PoseNearClipUnits;
            _camera.farClipPlane = OrbitCamera.FarClipUnits;
        }
    }
}
