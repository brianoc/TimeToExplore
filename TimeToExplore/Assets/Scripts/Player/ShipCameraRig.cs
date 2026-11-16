using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Drives the view: right-drag orbits the ship, the wheel zooms, and a plan
    /// or a debug jump frames its destination automatically until the player
    /// takes the camera back.
    ///
    /// With <see cref="MapEnabled"/>, the same controls also reach the system
    /// map: the pose blends from the local orbit to a top-down view as the
    /// camera distance crosses the local sphere of influence, right-drag pans
    /// on the map side, and the wheel reaches the map's 12-billion-km ceiling.
    /// The player's stored yaw and pitch are never overwritten, so zooming back
    /// in restores the exact remembered angle. The map looks at
    /// <c>ship + pan</c> rather than at the ship, which bends ADR-008; ADR-009
    /// is deferred to the map's documentation step.
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

        /// <summary>How fast auto-framing eases the orbit distance.</summary>
        public float AutoZoomRate = 2.5f;

        /// <summary>
        /// Development switch for the system map. Off is today's orbit camera,
        /// exactly: the local zoom clamp, the local near plane, and right-drag
        /// orbiting. The bootstrap sets it from its serialized
        /// <c>EnableMapView</c> field, so the comparison scenes share one rig.
        /// </summary>
        public bool MapEnabled;

        /// <summary>
        /// Screen pixels per unit of the old Input Manager's Mouse X/Y axes.
        /// Unity's default mouse axes use a 0.1 sensitivity, so one axis unit is
        /// ten pixels.
        /// </summary>
        public float PanPixelsPerMouseUnit = 10f;

        public OrbitCamera Orbit { get; } = new OrbitCamera();

        /// <summary>The map side of the zoom: blend weight, range, and pan.</summary>
        public MapView Map { get; } = new MapView();

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
        private bool _dragging;

        public void Initialise(Camera camera, SolarSystemModel model, float unitsPerMeter)
        {
            _camera = camera;
            _model = model;
            UnitsPerMeter = unitsPerMeter;

            if (_camera != null)
            {
                _camera.transform.rotation = Orbit.Rotation;
                if (MapEnabled) _camera.farClipPlane = MapView.FarClipUnits;
            }
        }

        /// <summary>Frame a body and follow it until the player takes over.</summary>
        public void Frame(BodyRuntime body)
        {
            AutoTarget = body;
            AutoAim = body != null;
            AutoDistance = body != null;
        }

        /// <summary>Stop following; the player owns the view.</summary>
        public void ClearAuto()
        {
            AutoAim = false;
            AutoDistance = false;
            AutoTarget = null;
        }

        /// <summary>Place the view without a transition, for a reset or a start.</summary>
        public void SnapTo(Vector3d shipPositionM, BodyRuntime body)
        {
            Frame(body);
            if (body != null)
            {
                Orbit.AimAlong(body.PositionM - shipPositionM);
                Orbit.DistanceUnits = Mathf.Clamp(AutoDistanceUnits(shipPositionM, body),
                    OrbitCamera.MinDistanceUnits, OrbitCamera.MaxDistanceUnits);
            }

            Map.ClearPan();
            double localRadiusM = LocalRadiusM(shipPositionM);
            RefreshBlendWeight(localRadiusM);
            ApplyToCamera(shipPositionM, localRadiusM);
        }

        /// <summary>One frame: read input, follow the target, place the camera.</summary>
        public void Apply(Vector3d shipPositionM, float deltaTime)
        {
            double localRadiusM = LocalRadiusM(shipPositionM);
            RefreshBlendWeight(localRadiusM);

            HandleInput(MapBlendWeight);

            // The wheel and a drag may have moved the distance, so re-read the
            // blend before deciding what the rest of the frame should do.
            RefreshBlendWeight(localRadiusM);
            bool mapSide = IsMapSide;

            if (AutoTarget != null && !mapSide)
            {
                Vector3d toTarget = AutoTarget.PositionM - shipPositionM;

                if (AutoAim && toTarget.SqrMagnitude > 1e-6)
                    Orbit.SlewToLookDirection(toTarget, AutoSlewDegreesPerSecond, deltaTime);

                if (AutoDistance)
                    Orbit.SmoothDistance(AutoDistanceUnits(shipPositionM, AutoTarget),
                        deltaTime, AutoZoomRate);
            }

            RefreshBlendWeight(localRadiusM);
            if (MapEnabled && !IsMapSide) Map.EasePan(deltaTime);

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

        private void HandleInput(float blendWeight)
        {
            bool mapSide = MapEnabled && MapView.UsesPan(blendWeight);

            if (Input.GetMouseButtonDown(1))
            {
                _dragging = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (Input.GetMouseButtonUp(1))
            {
                _dragging = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (_dragging)
            {
                float x = Input.GetAxis("Mouse X");
                float y = Input.GetAxis("Mouse Y");
                if (x != 0f || y != 0f)
                {
                    if (mapSide)
                    {
                        // On the map the button pans. Taking the pan means the
                        // player owns the view, exactly as an orbit drag does.
                        PanByDrag(x * PanPixelsPerMouseUnit, y * PanPixelsPerMouseUnit);
                        AutoAim = false;
                        AutoDistance = false;
                    }
                    else
                    {
                        Orbit.Turn(x * OrbitDegreesPerMouseUnit, -y * OrbitDegreesPerMouseUnit);
                        AutoAim = false;
                    }
                }
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Orbit.DistanceUnits = WheelDistanceUnits(Orbit.DistanceUnits, scroll, blendWeight);
                AutoDistance = false;
            }
        }

        private double LocalRadiusM(Vector3d shipPositionM)
        {
            return _model != null
                ? MapView.LocalRadiusM(_model, shipPositionM)
                : MapView.DeepSpaceRadiusM;
        }

        private void RefreshBlendWeight(double localRadiusM)
        {
            MapBlendWeight = MapEnabled
                ? Map.BlendWeight(Orbit.DistanceUnits, localRadiusM)
                : 0f;
        }

        private void ApplyToCamera(Vector3d shipPositionM, double localRadiusM)
        {
            Quaternion rotation = MapEnabled
                ? MapView.BlendRotation(Orbit.Rotation, Orbit.YawDegrees, MapBlendWeight)
                : Orbit.Rotation;

            float effectivePitch = MapEnabled
                ? Map.EffectivePitchDegrees(Orbit.PitchDegrees, Orbit.DistanceUnits, localRadiusM)
                : Orbit.PitchDegrees;

            Vector3 offset = OrbitCamera.Direction(Orbit.YawDegrees, effectivePitch)
                             * Orbit.DistanceUnits;

            Vector3 panUnits = MapEnabled ? Map.PanOffsetUnits : Vector3.zero;
            Vector3d lookAtM = shipPositionM + (MapEnabled ? Map.PanOffsetM : Vector3d.Zero);

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
