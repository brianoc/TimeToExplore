using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Player
{
    /// <summary>
    /// Drives the view: right-drag orbits the ship, the wheel zooms, and a plan
    /// or a debug jump frames its destination automatically until the player
    /// takes the camera back.
    ///
    /// The camera always looks at the ship at the render origin. The ship never
    /// moves in the render frame, so orbiting is just placing the camera on a
    /// sphere around the origin; the world is untouched.
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

        public float UnitsPerMeter = 1e-6f;

        public OrbitCamera Orbit { get; } = new OrbitCamera();

        /// <summary>The body the camera is framing, or null for free orbit.</summary>
        public BodyRuntime AutoTarget { get; private set; }

        /// <summary>True while the view turns to keep the target centred.</summary>
        public bool AutoAim { get; private set; }

        /// <summary>True while the orbit distance follows the target.</summary>
        public bool AutoDistance { get; private set; }

        /// <summary>True while either part of the framing is following.</summary>
        public bool Auto => AutoAim || AutoDistance;

        private Camera _camera;
        private SolarSystemModel _model;
        private bool _dragging;

        public void Initialise(Camera camera, SolarSystemModel model, float unitsPerMeter)
        {
            _camera = camera;
            _model = model;
            UnitsPerMeter = unitsPerMeter;

            if (_camera != null) _camera.transform.rotation = Orbit.Rotation;
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
            ApplyToCamera(shipPositionM);
        }

        /// <summary>One frame: read input, follow the target, place the camera.</summary>
        public void Apply(Vector3d shipPositionM, float deltaTime)
        {
            HandleInput();

            if (AutoTarget != null)
            {
                Vector3d toTarget = AutoTarget.PositionM - shipPositionM;

                if (AutoAim && toTarget.SqrMagnitude > 1e-6)
                    Orbit.SlewToLookDirection(toTarget, AutoSlewDegreesPerSecond, deltaTime);

                if (AutoDistance)
                    Orbit.SmoothDistance(AutoDistanceUnits(shipPositionM, AutoTarget),
                        deltaTime, AutoZoomRate);
            }

            ApplyToCamera(shipPositionM);
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

        private void HandleInput()
        {
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
                    Orbit.Turn(x * OrbitDegreesPerMouseUnit, -y * OrbitDegreesPerMouseUnit);
                    AutoAim = false;
                }
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Orbit.Zoom(scroll, ZoomFactorPerStep);
                AutoDistance = false;
            }
        }

        private void ApplyToCamera(Vector3d shipPositionM)
        {
            if (_camera == null) return;

            Vector3 offset = AvoidBodies(Orbit.OffsetUnits, shipPositionM);
            _camera.transform.position = offset;
            _camera.transform.rotation = Quaternion.LookRotation(
                offset.sqrMagnitude > 1e-12f ? -offset.normalized : Vector3.forward,
                Vector3.up);

            // Keep the near plane proportional to the orbit, so zooming in on a
            // parking orbit does not cut the planet open with a 50,000 km clip.
            float distance = Mathf.Max(offset.magnitude, OrbitCamera.MinDistanceUnits);
            _camera.nearClipPlane = Mathf.Clamp(distance * 0.02f, 0.0002f, 0.05f);
        }

        /// <summary>
        /// Pull the camera in if the orbit would put it inside a body. The test
        /// is done in metres against the model, so it follows the real radii at
        /// the current date rather than a hand-tuned limit.
        /// </summary>
        private Vector3 AvoidBodies(Vector3 offset, Vector3d shipPositionM)
        {
            if (_model == null) return offset;

            Vector3 direction = offset.normalized;
            float distance = offset.magnitude;

            for (int i = 0; i < 8; i++)
            {
                Vector3d cameraPositionM = shipPositionM
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
