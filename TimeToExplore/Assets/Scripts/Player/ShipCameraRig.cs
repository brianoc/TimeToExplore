using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.UI;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Drives the view: right-drag orbits the ship and the system map, left-drag
    /// pans the map, the wheel zooms toward the pointer, and a plan or a debug
    /// jump frames its destination automatically until the player takes the
    /// camera back. Space eases the camera home to the ship.
    ///
    /// The wheel requests a distance rather than placing one: the camera eases
    /// to it over <see cref="ZoomEaseRatePerSecond"/> (see
    /// <see cref="OrbitCamera.EaseDistance"/>), and the map blend reads the
    /// eased distance, so the top-down-to-local tilt and the rotation sweep
    /// with the zoom instead of snapping. The point under the pointer on the
    /// look-at plane is anchored, and the look-at is solved from it every frame,
    /// so that point stays under the pointer through the whole tween.
    ///
    /// With <see cref="MapEnabled"/>, the pose blends from the local orbit to a
    /// top-down view as the camera distance crosses the local sphere of
    /// influence, left-drag pans on the map side, and the wheel reaches the
    /// map's 12-billion-km ceiling. The right button keeps one meaning at every
    /// zoom — rotate — so the gesture never changes under the player. The
    /// player's stored yaw and pitch are never overwritten, so zooming back in
    /// restores the exact remembered angle. The map looks at
    /// <c>ship + pan</c> rather than at the ship, which bends ADR-008; ADR-009
    /// is deferred to the map's documentation step. The pan is now also the
    /// mechanism for cursor-anchored zoom in the local band (ADR-016), so the
    /// camera looks at <c>ship + pan</c> at every zoom.
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
        /// second. The map blend, the tilt, and the rotation all read the eased
        /// distance, so one tween smooths the whole pose change.
        /// </summary>
        public float ZoomEaseRatePerSecond = 7f;

        /// <summary>
        /// How long Space's return home takes, in seconds. The zoom and the pan
        /// unwind follow one smoothstep, so the view starts and ends at rest.
        /// </summary>
        public float HomeDurationSeconds = HomeTransition.DefaultDurationSeconds;

        /// <summary>
        /// The home view sits at this fraction of the local radius: comfortably
        /// inside the blend band, so the arrival is exactly the local view
        /// (<c>w = 0</c>), and far enough out to read the ship's neighbourhood.
        /// </summary>
        public const float HomeLocalRadiusFraction = 0.5f;

        /// <summary>
        /// Development switch for the system map. Off is today's orbit camera,
        /// exactly: the local zoom clamp, the local near plane, and right-drag
        /// orbiting. The bootstrap sets it from its serialized
        /// <c>EnableMapView</c> field, so the comparison scenes share one rig.
        /// </summary>
        public bool MapEnabled;

        /// <summary>
        /// Screen pixels per unit of the seam's mouse-drag axis. The seam applies
        /// the old Input Manager's 0.1 sensitivity to the raw pixel delta, so one
        /// axis unit is ten pixels and a map pan still follows the cursor.
        /// </summary>
        public float PanPixelsPerMouseUnit = 10f;

        public OrbitCamera Orbit { get; } = new OrbitCamera();

        /// <summary>The map side of the zoom: blend weight, range, and pan.</summary>
        public MapView Map { get; } = new MapView();

        /// <summary>The eased return to the ship that Space starts.</summary>
        public HomeTransition Home { get; } = new HomeTransition();

        /// <summary>True while Space's home transition is easing the view back.</summary>
        public bool IsHoming => Home.IsActive;

        /// <summary>
        /// Render units per metre. Held on <see cref="Map"/> so the blend, the
        /// pan, and the camera share one conversion.
        /// </summary>
        public float UnitsPerMeter
        {
            get => Map.UnitsPerMeter;
            set => Map.UnitsPerMeter = value;
        }

        /// <summary>The body the camera is framing, or null for free orbit.</summary>
        public BodyRuntime AutoTarget { get; private set; }

        /// <summary>True while the view turns to keep the target centred.</summary>
        public bool AutoAim { get; private set; }

        /// <summary>True while the orbit distance follows the target.</summary>
        public bool AutoDistance { get; private set; }

        /// <summary>True while either part of the framing is following.</summary>
        public bool Auto => AutoAim || AutoDistance;

        /// <summary>The blend weight placed on the last frame (0 local, 1 map).</summary>
        public float MapBlendWeight { get; private set; }

        /// <summary>
        /// The local scale used on the last frame: the dominant body's SOI
        /// radius, or the deep-space fallback. The renderer uses it to fade the
        /// marker aid outside the neighbourhood.
        /// </summary>
        public double NeighbourhoodRadiusM { get; private set; } = MapView.DeepSpaceRadiusM;

        /// <summary>True when the last frame was on the map side of the blend.</summary>
        public bool IsMapSide => MapEnabled && MapView.UsesPan(MapBlendWeight);

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
        private bool _panning;
        private bool _panEngaged;
        private bool _zoomPending;
        private bool _zoomAnchorActive;
        private Vector3 _zoomAnchorUnits;
        private Vector2 _zoomAnchorNdc;
        private float _zoomAnchorAspect = 1f;
        private float _zoomAnchorFieldOfViewDegrees = 60f;
        private bool _uiOverLastFrame;
        private bool _cursorCaptured;
        private Vector2 _panPressPointer;

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
                if (MapEnabled) _camera.farClipPlane = MapView.FarClipUnits;
            }
        }

        /// <summary>Frame a body and follow it until the player takes over.</summary>
        public void Frame(BodyRuntime body)
        {
            // A new framing target takes the view over from the player's
            // cursor-anchored zoom or home transition.
            _zoomAnchorActive = false;
            Home.Cancel();

            AutoTarget = body;
            AutoAim = body != null;
            AutoDistance = body != null;
        }

        /// <summary>Stop following; the player owns the view.</summary>
        public void ClearAuto()
        {
            _zoomAnchorActive = false;
            Home.Cancel();

            AutoAim = false;
            AutoDistance = false;
            AutoTarget = null;
        }

        /// <summary>Place the view without a transition, for a reset or a start.</summary>
        public void SnapTo(Vector3d shipPositionM, BodyRuntime body)
        {
            Frame(body);
            _zoomPending = false;
            _zoomAnchorActive = false;
            Home.Cancel();
            if (body != null)
            {
                Orbit.AimAlong(body.PositionM - shipPositionM);
                Orbit.SnapDistance(Mathf.Clamp(AutoDistanceUnits(shipPositionM, body),
                    OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits));
            }

            Map.ClearPan();
            double localRadiusM = LocalRadiusM(shipPositionM);
            RefreshBlendWeight(localRadiusM);
            ApplyToCamera(shipPositionM, localRadiusM);
        }

        /// <summary>
        /// Start the eased return to the ship: drop any framing, forget the
        /// cursor anchor, then ease the distance to a local view while the pan
        /// unwinds. Space calls this. The player's yaw and pitch are kept, so
        /// the ship returns to the centre of the same remembered angle.
        /// </summary>
        public void HomeToShip(Vector3d shipPositionM)
        {
            // Take the view from the framing exactly as a drag does: clear the
            // follow flags but keep AutoTarget, so an active flight's next
            // frame does not re-claim the camera and cancel the transition.
            AutoAim = false;
            AutoDistance = false;
            _zoomPending = false;
            _zoomAnchorActive = false;

            Home.DurationSeconds = Mathf.Max(0f, HomeDurationSeconds);
            Home.Begin(Orbit.DistanceUnits,
                HomeDistanceUnits(LocalRadiusM(shipPositionM)),
                Map.PanOffsetM);
        }

        /// <summary>
        /// The distance a home press targets: half the local radius, clamped to
        /// the local orbit range. That lands inside the blend band's lower edge,
        /// so the arrival is the local view and not a point on the map side.
        /// </summary>
        public float HomeDistanceUnits(double localRadiusM)
        {
            if (!(localRadiusM > 0.0) || double.IsInfinity(localRadiusM))
                localRadiusM = MapView.DeepSpaceRadiusM;

            double distanceM = HomeLocalRadiusFraction * localRadiusM;
            return Mathf.Clamp((float)(distanceM * UnitsPerMeter),
                OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits);
        }

        /// <summary>One frame: read input, follow the target, place the camera.</summary>
        public void Apply(Vector3d shipPositionM, float deltaTime)
        {
            double localRadiusM = LocalRadiusM(shipPositionM);
            RefreshBlendWeight(localRadiusM);

            HandleInput(MapBlendWeight);

            // Space's return home owns the distance and the pan until it lands.
            // One smoothstep drives both, so the zoom and the unwind start and
            // stop together, at rest.
            if (Home.IsActive)
            {
                bool arrived = Home.Advance(deltaTime);
                Orbit.SnapDistance(Home.DistanceUnitsAt);
                Map.SetPanM(Home.PanMAt);

                if (arrived)
                {
                    Orbit.SnapDistance(Home.TargetDistanceUnits);
                    Map.ClearPan();
                }
            }
            else if (_zoomPending && !Orbit.EaseDistance(deltaTime, ZoomEaseRatePerSecond))
            {
                // A wheel notch only requests a distance. Ease it before the
                // rest of the frame reads the blend, so the map tilt, the
                // rotation, and the near plane follow the zoom over one short
                // tween instead of snapping with the notch.
                _zoomPending = false;
            }

            // The wheel or the home transition may have moved the distance, so
            // re-read the blend before deciding what the rest of the frame
            // should do.
            RefreshBlendWeight(localRadiusM);
            bool mapSide = IsMapSide;

            // A pending wheel zoom owns the distance until it settles; the
            // automatic framing resumes after that, and stays suspended on the
            // map side exactly as before. The home transition owns the view
            // outright.
            if (!Home.IsActive && AutoTarget != null && !mapSide && !_zoomPending)
            {
                Vector3d toTarget = AutoTarget.PositionM - shipPositionM;

                if (AutoAim && toTarget.SqrMagnitude > 1e-6)
                    Orbit.SlewToLookDirection(toTarget, AutoSlewDegreesPerSecond, deltaTime);

                if (AutoDistance)
                {
                    Orbit.RequestDistance(AutoDistanceUnits(shipPositionM, AutoTarget));
                    Orbit.EaseDistance(deltaTime, AutoZoomRate);
                }
            }

            RefreshBlendWeight(localRadiusM);

            // A cursor-anchored zoom keeps the pan it has: the player put the
            // point there, so easing it back to the ship would move it from
            // under the pointer. Space is the way home.
            if (MapEnabled && !IsMapSide && !_zoomAnchorActive && !Home.IsActive)
                Map.EasePan(deltaTime);

            // Solve the look-at from the anchor last, after the aim and the
            // blend have moved, so the point stays pinned through the frame.
            ApplyZoomAnchor();

            ApplyToCamera(shipPositionM, localRadiusM);
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
        /// The distance one wheel gesture produces, routed by the blend: the
        /// map ceiling above <see cref="MapView.PanThreshold"/>, the local
        /// camera's 5-million-unit ceiling below it. Pure, so the routing is
        /// tested without input.
        /// </summary>
        public float WheelDistanceUnits(float distanceUnits, float steps, float blendWeight)
        {
            return MapEnabled && MapView.UsesPan(blendWeight)
                ? MapView.ZoomDistanceUnits(distanceUnits, steps, ZoomFactorPerStep)
                : OrbitCamera.ZoomDistanceUnits(distanceUnits, steps, ZoomFactorPerStep);
        }

        /// <summary>
        /// Pan the map by a screen-pixel drag. The content follows the cursor,
        /// and the scale is tied to the camera distance so the gesture feels the
        /// same at every zoom. Ignored while the map is off.
        /// </summary>
        public void PanByDrag(float deltaXPixels, float deltaYPixels)
        {
            if (!MapEnabled) return;

            float fieldOfView = _camera != null ? _camera.fieldOfView : 60f;
            float screenHeight = Screen.height > 0 ? Screen.height : 1080f;
            float distance = Mathf.Max(Orbit.DistanceUnits, OrbitCamera.MinDistanceUnits);

            Map.Pan(MapView.PanDeltaForDrag(deltaXPixels, deltaYPixels, distance,
                fieldOfView, screenHeight, Orbit.YawDegrees));
        }

        /// <summary>
        /// The pointer gestures for one frame, supplied by the caller so the
        /// orbit/pan routing is verified without hardware.
        /// </summary>
        public struct DragInput
        {
            public bool LeftPressed;
            public bool LeftReleased;
            public bool RightPressed;
            public bool RightReleased;

            /// <summary>The seam's drag delta: raw pixels times the 0.1 sensitivity.</summary>
            public Vector2 Delta;

            /// <summary>The pointer in screen pixels, for the click slop.</summary>
            public Vector2 Pointer;

            /// <summary>
            /// True when the pointer is over a uGUI panel. Only a press edge
            /// reads it: a gesture that begins on a panel belongs to the panel
            /// until the button is released, even if the pointer leaves it.
            /// </summary>
            public bool OverUi;
        }

        /// <summary>
        /// Route one frame of pointer gestures. The right button always orbits,
        /// at every zoom, so its meaning never changes under the player. The
        /// left button pans, and only on the map side: a press starts the pan,
        /// it engages once the pointer leaves the click slop, and the release
        /// ends it. The slop also keeps a click from nudging the map, which at
        /// the map ceiling would move it by tens of millions of kilometres.
        ///
        /// A press over a HUD panel starts neither gesture: the panel owns the
        /// pointer, so dragging a slider or a panel edge cannot pan the map or
        /// orbit the camera underneath it. The ownership lasts until the button
        /// is released, so pulling a slider past the panel's edge does not hand
        /// the gesture back to the camera halfway through.
        ///
        /// A gesture that actually moves cancels the home transition and
        /// releases a cursor-anchored zoom: the player has taken the view back.
        /// </summary>
        public void ApplyDragInput(DragInput input, bool mapSide)
        {
            if (input.RightPressed) _orbiting = !input.OverUi;
            else if (input.RightReleased) _orbiting = false;

            if (input.LeftPressed)
            {
                _panning = mapSide && !input.OverUi;
                _panEngaged = false;
                _panPressPointer = input.Pointer;
            }
            else if (input.LeftReleased)
            {
                _panning = false;
                _panEngaged = false;
            }
            if (!mapSide) _panning = false;

            if (_panning && !_panEngaged)
            {
                // The EventSystem can answer for the previous frame, so a press
                // reported as being in the world on its own frame is still
                // cancelled if the pointer turns out to be on a panel before
                // the pan engages.
                if (input.OverUi) _panning = false;
                else if (PlayerControls.ExceedsClickSlop(_panPressPointer, input.Pointer))
                    _panEngaged = true;
            }

            float x = input.Delta.x;
            float y = input.Delta.y;
            if (x == 0f && y == 0f) return;

            if (_orbiting)
            {
                Orbit.Turn(x * OrbitDegreesPerMouseUnit, -y * OrbitDegreesPerMouseUnit);
                AutoAim = false;
                Home.Cancel();
                _zoomAnchorActive = false;
            }
            else if (_panning && _panEngaged)
            {
                PanByDrag(x * PanPixelsPerMouseUnit, y * PanPixelsPerMouseUnit);
                AutoAim = false;
                AutoDistance = false;
                Home.Cancel();
                _zoomAnchorActive = false;
            }
        }

        private void HandleInput(float blendWeight)
        {
            bool mapSide = MapEnabled && MapView.UsesPan(blendWeight);

            // A panel takes the pointer for the whole gesture. The EventSystem
            // can trail by a frame, so a press counts as over the UI when either
            // this frame or the previous one said so.
            bool reportedOverUi = UiInput.PointerOverUi();
            bool overUi = reportedOverUi || _uiOverLastFrame;
            _uiOverLastFrame = reportedOverUi;

            // Only the orbit gesture captures the cursor: a map pan follows the
            // visible pointer and must not hide it. A panel keeps both.
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

            ApplyDragInput(new DragInput
            {
                LeftPressed = PlayerControls.LeftClickPressed,
                LeftReleased = PlayerControls.LeftClickReleased,
                RightPressed = PlayerControls.RightDragStarted,
                RightReleased = PlayerControls.RightDragEnded,
                Delta = PlayerControls.DragDelta,
                Pointer = PlayerControls.PointerPosition,
                OverUi = overUi
            }, mapSide);

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
        /// Route one wheel gesture through the blend and request its distance,
        /// anchored at the screen centre. The pose eases to the request over
        /// <see cref="ZoomEaseRatePerSecond"/>, so the map tilt and the rotation
        /// blend rather than snapping. Exposed so the tween is testable without
        /// a device.
        /// </summary>
        public void ApplyWheelSteps(float steps)
        {
            Vector2 screen = ScreenPixels();
            ApplyWheelSteps(steps, screen * 0.5f, screen);
        }

        /// <summary>
        /// Route one wheel gesture through the blend, request its distance, and
        /// anchor the point under <paramref name="pointerPixels"/> so it stays
        /// under the pointer through the tween. Pointer and screen are in the
        /// Input System's bottom-left screen pixels. Exposed so the anchor is
        /// testable without a device.
        /// </summary>
        public void ApplyWheelSteps(float steps, Vector2 pointerPixels, Vector2 screenPixels)
        {
            if (steps == 0f) return;

            Home.Cancel();

            // Compound on a pending request so a continuous gesture keeps its
            // 1.25x per notch; start from the placed distance when the last
            // tween has settled. Never compound on a stale auto-framing target.
            float from = _zoomPending ? Orbit.TargetDistanceUnits : Orbit.DistanceUnits;
            float weight = MapEnabled
                ? Map.BlendWeight(from, NeighbourhoodRadiusM)
                : 0f;

            // Take the anchor from the pose on screen now, not from the
            // distance being requested: that is the point the player is
            // looking at. The look-at is solved from it every frame.
            _zoomAnchorNdc = MapView.PointerNdc(pointerPixels, screenPixels);
            _zoomAnchorFieldOfViewDegrees = FieldOfViewDegrees;
            _zoomAnchorAspect = Aspect(screenPixels);
            _zoomAnchorUnits = MapView.CursorPointOnLookAtPlane(Map.PanOffsetUnits,
                CurrentRotation(),
                Mathf.Max(Orbit.DistanceUnits, OrbitCamera.MinDistanceUnits),
                _zoomAnchorNdc, _zoomAnchorFieldOfViewDegrees, _zoomAnchorAspect);
            _zoomAnchorActive = true;

            Orbit.RequestDistance(WheelDistanceUnits(from, steps, weight));
            _zoomPending = !Orbit.IsDistanceSettled;
            AutoDistance = false;
        }

        /// <summary>
        /// Solve the look-at from the anchored point for the current distance
        /// and rotation, so the point stays under the pointer while the zoom
        /// eases and the blend turns the pose. No-op without an anchor.
        /// </summary>
        private void ApplyZoomAnchor()
        {
            if (!_zoomAnchorActive) return;

            Vector3 panUnits = MapView.AnchoredLookAtUnits(_zoomAnchorUnits,
                CurrentRotation(),
                Mathf.Max(Orbit.DistanceUnits, OrbitCamera.MinDistanceUnits),
                _zoomAnchorNdc, _zoomAnchorFieldOfViewDegrees, _zoomAnchorAspect);

            Map.SetPanM(Vector3d.FromUnity(panUnits) / UnitsPerMeter);
        }

        /// <summary>The pose rotation for the current blend weight.</summary>
        private Quaternion CurrentRotation()
        {
            return MapEnabled
                ? MapView.BlendRotation(Orbit.Rotation, Orbit.YawDegrees, MapBlendWeight)
                : Orbit.Rotation;
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
                ? MapView.LocalRadiusM(_model, shipPositionM)
                : MapView.DeepSpaceRadiusM;
        }

        private void RefreshBlendWeight(double localRadiusM)
        {
            NeighbourhoodRadiusM = localRadiusM;
            MapBlendWeight = MapEnabled
                ? Map.BlendWeight(Orbit.DistanceUnits, localRadiusM)
                : 0f;
        }

        private void ApplyToCamera(Vector3d shipPositionM, double localRadiusM)
        {
            Quaternion rotation = CurrentRotation();

            float effectivePitch = MapEnabled
                ? Map.EffectivePitchDegrees(Orbit.PitchDegrees, Orbit.DistanceUnits, localRadiusM)
                : Orbit.PitchDegrees;

            Vector3 offset = OrbitCamera.Direction(Orbit.YawDegrees, effectivePitch)
                             * Orbit.DistanceUnits;

            // The pan is live at every zoom: on the map it is the browsed
            // centre, and in the local band it is the cursor-anchored offset
            // the wheel put there. Space unwinds it to zero.
            Vector3 panUnits = Map.PanOffsetUnits;
            Vector3d lookAtM = shipPositionM + Map.PanOffsetM;

            // Avoidance is measured from the look-at point, so on the map it
            // pulls toward the panned centre rather than toward the ship.
            Vector3 placed = AvoidBodies(offset, lookAtM);

            LookAtUnits = panUnits;
            PosePositionUnits = panUnits + placed;
            PoseRotation = rotation;
            PoseNearClipUnits = MapView.NearClipUnits(
                Mathf.Max(placed.magnitude, OrbitCamera.MinDistanceUnits), MapBlendWeight);

            if (_camera == null) return;

            _camera.transform.position = PosePositionUnits;
            _camera.transform.rotation = PoseRotation;

            // At w = 0 this is today's local rule exactly; on the map side it
            // follows the distance at one thousandth of the orbit.
            _camera.nearClipPlane = PoseNearClipUnits;
            if (MapEnabled) _camera.farClipPlane = MapView.FarClipUnits;
        }

        /// <summary>
        /// Pull the camera in if the orbit would put it inside a body. The test
        /// is done in metres against the model, so it follows the real radii at
        /// the current date rather than a hand-tuned limit.
        /// </summary>
        private Vector3 AvoidBodies(Vector3 offset, Vector3d lookAtPositionM)
        {
            if (_model == null) return offset;

            Vector3 direction = offset.normalized;
            float distance = offset.magnitude;

            for (int i = 0; i < 8; i++)
            {
                Vector3d cameraPositionM = lookAtPositionM
                    + Vector3d.FromUnity(direction * distance) * (1.0 / UnitsPerMeter);

                BodyRuntime nearest = _model.FindNearest(cameraPositionM);
                if (nearest == null || nearest.RadiusM <= 0.0) break;

                double clearance = Vector3d.Distance(cameraPositionM, nearest.PositionM)
                                   - nearest.RadiusM;
                if (clearance > nearest.RadiusM * 0.05) break;

                distance *= 0.6f;
                if (distance < OrbitCamera.MinDistanceUnits * 0.5f) break;
            }

            return direction * distance;
        }
    }
}
