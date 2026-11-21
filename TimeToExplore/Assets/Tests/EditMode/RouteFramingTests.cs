using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The plotted-route overview: the fit contains the whole path, the angle
    /// looks across the route's plane, and the eased move lands exactly on the
    /// frame before handing the view back to the player.
    /// </summary>
    [Category("presentation")]
    public class RouteFramingTests
    {
        private const float FieldOfViewDegrees = 60f;
        private const float Aspect = 16f / 9f;
        private const float UnitsPerMeter = 1e-6f;

        private GameObject _root;
        private ShipCameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Route Framing Test");
            _rig = _root.AddComponent<ShipCameraRig>();
            _rig.MapEnabled = false;
            _rig.Initialise(null, null, UnitsPerMeter);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void TheFrameContainsEveryPointOfACurvedRoute()
        {
            var centre = new Vector3d(1.0e11, -2.0e10, 3.0e10);
            var planeNormal = new Vector3d(0.2, -0.35, 0.91).Normalized;
            List<Vector3d> route = Circle(centre, 8.0e10, planeNormal, 96);
            var ship = centre - new Vector3d(8.0e10, 0.0, 0.0);

            RouteFraming.Frame frame = RouteFraming.Compute(route, ship, null,
                UnitsPerMeter, FieldOfViewDegrees, Aspect, 37f, 22f,
                false, OrbitCamera.MaxDistanceUnits);

            Assert.That(frame.IsValid, Is.True);
            Assert.That(frame.RadiusM, Is.EqualTo(8.0e10).Within(2.0e6),
                "the bounding sphere reaches the far side of the circle");
            Assert.That(Vector3d.Distance(frame.PanM, centre - ship), Is.LessThan(1.0));

            AssertEveryPointIsOnScreen(frame, route, ship);
        }

        [Test]
        public void TheFrameLooksAcrossThePlaneOfATiltedRoute()
        {
            var centre = new Vector3d(-3.0e10, 4.0e10, 1.0e10);
            var planeNormal = new Vector3d(0.25, 0.55, 0.80).Normalized;
            List<Vector3d> route = Circle(centre, 2.0e10, planeNormal, 64);
            var ship = route[0];

            // The player's current view is above the ecliptic, on the same side
            // of the route plane, so the overview stays on that side.
            RouteFraming.Frame frame = RouteFraming.Compute(route, ship, null,
                UnitsPerMeter, FieldOfViewDegrees, Aspect, 20f, 65f,
                false, OrbitCamera.MaxDistanceUnits);

            Assert.That(frame.IsValid, Is.True);
            Vector3 cameraOffset = OrbitCamera.Direction(frame.YawDegrees,
                frame.PitchDegrees);
            Vector3d offsetEcliptic = Vector3d.FromUnity(cameraOffset);

            double expected = System.Math.Cos(RouteFraming.ViewTiltDegrees *
                                              System.Math.PI / 180.0);
            Assert.That(System.Math.Abs(Vector3d.Dot(offsetEcliptic, planeNormal)),
                Is.EqualTo(expected).Within(1e-4),
                "the camera sits on a normal that is tilted off the plane's normal");
            Assert.That(Vector3d.Dot(offsetEcliptic,
                    Vector3d.FromUnity(OrbitCamera.Direction(20f, 65f))),
                Is.GreaterThan(0.0), "the view does not flip to the far side");
        }

        [Test]
        public void TheDistanceFitsTheProjectedRouteWithAMargin()
        {
            // A diagonal line across the camera's own up/right axes: a
            // bounding-sphere fit would leave a lot of the frame empty, while
            // the projected fit should put the ends near the margin.
            var route = new List<Vector3d>();
            for (int i = 0; i <= 32; i++)
            {
                double t = i / 32.0;
                route.Add(new Vector3d(
                    -1.0e11 + 2.0e11 * t,
                    4.0e10 + 0.0 * t,
                    1.0e10 + 2.0e10 * t));
            }

            var ship = route[0];
            RouteFraming.Frame frame = RouteFraming.Compute(route, ship, null,
                UnitsPerMeter, FieldOfViewDegrees, Aspect, 0f, 10f,
                false, OrbitCamera.MaxDistanceUnits);

            Assert.That(frame.IsValid, Is.True);
            AssertEveryPointIsWithinMargin(frame, route, ship);

            double halfVertical = FieldOfViewDegrees * 0.5 * System.Math.PI / 180.0;
            double halfHorizontal = System.Math.Atan(System.Math.Tan(halfVertical) * Aspect);
            double limiting = System.Math.Min(halfVertical, halfHorizontal);
            double sphereDistance = frame.RadiusM * UnitsPerMeter /
                                    System.Math.Sin(limiting) * RouteFraming.FitMargin;

            Assert.That(frame.DistanceUnits, Is.LessThan((float)sphereDistance),
                "fitting the projection is tighter than fitting the bounding sphere");
        }

        [Test]
        public void TheArrivalGhostIsInsideTheShotToo()
        {
            List<Vector3d> route = Circle(new Vector3d(5.0e10, 0.0, 0.0), 1.0e10,
                new Vector3d(0.0, 0.0, 1.0), 32);
            var ship = route[0];
            var arrival = new Vector3d(7.2e10, 0.0, 3.0e9);

            RouteFraming.Frame withGhost = RouteFraming.Compute(route, ship, arrival,
                UnitsPerMeter, FieldOfViewDegrees, Aspect, 0f, 10f,
                false, OrbitCamera.MaxDistanceUnits);
            RouteFraming.Frame withoutGhost = RouteFraming.Compute(route, ship, null,
                UnitsPerMeter, FieldOfViewDegrees, Aspect, 0f, 10f,
                false, OrbitCamera.MaxDistanceUnits);

            Assert.That(withGhost.RadiusM, Is.GreaterThan(withoutGhost.RadiusM),
                "the ghost extends the fitted bounds");

            var points = new List<Vector3d>(route) { arrival };
            AssertEveryPointIsOnScreen(withGhost, points, ship);
        }

        [Test]
        public void AStraightRouteIsSeenSideOnRatherThanEndOn()
        {
            var direction = new Vector3d(1.0, 0.2, 0.3).Normalized;
            var route = new List<Vector3d>();
            for (int i = 0; i <= 20; i++)
                route.Add(new Vector3d(4.0e10, 0.0, 0.0) + direction * (i * 1.0e9));

            RouteFraming.Frame frame = RouteFraming.Compute(route, route[0], null,
                UnitsPerMeter, FieldOfViewDegrees, Aspect, 0f, 12f,
                false, OrbitCamera.MaxDistanceUnits);

            Assert.That(frame.IsValid, Is.True, "a straight route still gets a frame");
            Vector3d offset = Vector3d.FromUnity(OrbitCamera.Direction(
                frame.YawDegrees, frame.PitchDegrees));
            Assert.That(System.Math.Abs(Vector3d.Dot(offset, direction)),
                Is.LessThan(1e-3), "the camera looks across the line, not along it");
            AssertEveryPointIsOnScreen(frame, route, route[0]);
        }

        [Test]
        public void TheTransitionTakesTheShortestTurnAndLandsExactly()
        {
            var transition = new RouteViewTransition { DurationSeconds = 1.5f };
            var startPan = new Vector3d(1.0e9, -2.0e9, 3.0e9);
            var targetPan = new Vector3d(5.0e10, 1.0e10, -2.0e10);

            transition.Begin(350f, -20f, 1f, startPan, 10f, 30f, 1000f, targetPan);
            Assert.That(transition.IsActive, Is.True);
            Assert.That(transition.YawDegreesAt, Is.EqualTo(350f).Within(1e-4f));
            Assert.That(transition.DistanceUnitsAt, Is.EqualTo(1f).Within(1e-4f));

            Assert.That(transition.Advance(0.75f), Is.False, "half way is not the end");
            Assert.That(transition.YawDegreesAt, Is.EqualTo(0f).Within(1e-3f),
                "350 to 10 turns +20 degrees, not -340");
            Assert.That(transition.PitchDegreesAt, Is.EqualTo(5f).Within(1e-4f));
            Assert.That(transition.DistanceUnitsAt, Is.EqualTo(31.6228f).Within(1e-3f),
                "distance is geometric, so a 1000x zoom reads evenly");
            Assert.That(Vector3d.Distance(transition.PanMAt,
                (startPan + targetPan) * 0.5), Is.LessThan(1.0));

            Assert.That(transition.Advance(0.75f), Is.True);
            Assert.That(transition.IsActive, Is.False);
            Assert.That(transition.YawDegreesAt, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(transition.PitchDegreesAt, Is.EqualTo(30f).Within(1e-4f));
            Assert.That(transition.DistanceUnitsAt, Is.EqualTo(1000f).Within(1e-3f));
            Assert.That(Vector3d.Distance(transition.PanMAt, targetPan),
                Is.LessThan(1e-6));
        }

        [Test]
        public void TheRigEasesToTheRouteAndTheWholeRouteFits()
        {
            var centre = new Vector3d(2.0e10, 0.0, 0.0);
            var planeNormal = new Vector3d(0.1, 0.35, 0.93).Normalized;
            List<Vector3d> route = Circle(centre, 5.0e9, planeNormal, 72);
            var ship = route[0];

            bool started = _rig.FrameRoute(route, ship, null);
            Assert.That(started, Is.True);
            Assert.That(_rig.IsFramingRoute, Is.True);
            Assert.That(_rig.Auto, Is.False, "the route view replaces body framing");

            float startDistance = _rig.Orbit.DistanceUnits;
            int frames = 0;
            while (_rig.IsFramingRoute && frames < 600)
            {
                _rig.Apply(ship, 1f / 60f);
                frames++;
            }

            Assert.That(frames, Is.InRange(89, 92), "about a second and a half of frames");
            Assert.That(_rig.Orbit.DistanceUnits, Is.GreaterThan(startDistance),
                "the view pulls back from the ship to the route");
            Assert.That(Vector3d.Distance(_rig.Map.PanOffsetM, centre - ship),
                Is.LessThan(1.0), "the look-at lands on the route's centre");
            Assert.That(_rig.LookAtUnits, Is.Not.EqualTo(Vector3.zero));

            var cameraPose = new RoutePose
            {
                PositionUnits = _rig.PosePositionUnits,
                Rotation = _rig.PoseRotation,
                DistanceUnits = _rig.Orbit.DistanceUnits
            };
            AssertEveryPointIsOnScreen(cameraPose, route, ship);
        }

        [Test]
        public void AWheelOrDragTakesTheViewBackFromTheRouteMove()
        {
            var centre = new Vector3d(2.0e10, 0.0, 0.0);
            List<Vector3d> route = Circle(centre, 5.0e9,
                new Vector3d(0.0, 0.0, 1.0), 48);
            var ship = route[0];

            _rig.FrameRoute(route, ship, null);
            _rig.Apply(ship, 1f / 60f);
            Assert.That(_rig.IsFramingRoute, Is.True);

            _rig.ApplyWheelSteps(1f);
            Assert.That(_rig.IsFramingRoute, Is.False, "a wheel gesture takes the view back");

            _rig.FrameRoute(route, ship, null);
            _rig.Apply(ship, 1f / 60f);
            Assert.That(_rig.IsFramingRoute, Is.True);

            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                RightPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: false);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(120f, 100f),
                Delta = new Vector2(2f, 0f)
            }, mapSide: false);

            Assert.That(_rig.IsFramingRoute, Is.False, "an orbit drag takes the view back");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { RightReleased = true }, false);

            _rig.FrameRoute(route, ship, null);
            _rig.Apply(ship, 1f / 60f);
            _rig.HomeToShip(ship);
            Assert.That(_rig.IsFramingRoute, Is.False, "Space's home cancels the route move too");
            Assert.That(_rig.IsHoming, Is.True);
        }

        [Test]
        public void AnEmptyOrDegenerateRouteLeavesTheViewAlone()
        {
            var ship = new Vector3d(1.0e9, 0.0, 0.0);
            Assert.That(_rig.FrameRoute(new List<Vector3d>(), ship, null), Is.False);
            Assert.That(_rig.FrameRoute(new List<Vector3d> { ship }, ship, null), Is.False,
                "a single point is not a route");
            Assert.That(_rig.IsFramingRoute, Is.False);
        }

        private static List<Vector3d> Circle(Vector3d centre, double radius,
            Vector3d normal, int count)
        {
            Vector3d helper = System.Math.Abs(normal.z) < 0.9
                ? new Vector3d(0.0, 0.0, 1.0)
                : new Vector3d(1.0, 0.0, 0.0);
            Vector3d u = Vector3d.Cross(normal, helper).Normalized;
            Vector3d v = Vector3d.Cross(normal, u).Normalized;

            var points = new List<Vector3d>(count);
            for (int i = 0; i < count; i++)
            {
                double angle = 2.0 * System.Math.PI * i / count;
                points.Add(centre + u * (radius * System.Math.Cos(angle))
                                   + v * (radius * System.Math.Sin(angle)));
            }

            return points;
        }

        private struct RoutePose
        {
            public Vector3 PositionUnits;
            public Quaternion Rotation;
            public float DistanceUnits;
        }

        private static void AssertEveryPointIsOnScreen(RouteFraming.Frame frame,
            IReadOnlyList<Vector3d> route, Vector3d ship)
        {
            Vector3 offset = OrbitCamera.Direction(frame.YawDegrees, frame.PitchDegrees);
            AssertEveryPointIsOnScreen(new RoutePose
            {
                PositionUnits = (frame.PanM * UnitsPerMeter).ToUnity()
                                + offset * frame.DistanceUnits,
                Rotation = Quaternion.LookRotation(-offset, Vector3.up),
                DistanceUnits = frame.DistanceUnits
            }, route, ship);
        }

        private static void AssertEveryPointIsOnScreen(RoutePose pose,
            IReadOnlyList<Vector3d> route, Vector3d ship)
        {
            AssertEveryPointIsWithin(pose, route, ship, 1f, "on screen");
        }

        private static void AssertEveryPointIsWithinMargin(RouteFraming.Frame frame,
            IReadOnlyList<Vector3d> route, Vector3d ship)
        {
            Vector3 offset = OrbitCamera.Direction(frame.YawDegrees, frame.PitchDegrees);
            var pose = new RoutePose
            {
                PositionUnits = (frame.PanM * UnitsPerMeter).ToUnity()
                                + offset * frame.DistanceUnits,
                Rotation = Quaternion.LookRotation(-offset, Vector3.up),
                DistanceUnits = frame.DistanceUnits
            };
            AssertEveryPointIsWithin(pose, route, ship, 1f / RouteFraming.FitMargin,
                "inside the fitted margin");
        }

        private static void AssertEveryPointIsWithin(RoutePose pose,
            IReadOnlyList<Vector3d> route, Vector3d ship, float limit, string what)
        {
            Vector3 right = pose.Rotation * Vector3.right;
            Vector3 up = pose.Rotation * Vector3.up;
            Vector3 forward = pose.Rotation * Vector3.forward;
            float halfHeight = Mathf.Tan(0.5f * FieldOfViewDegrees * Mathf.Deg2Rad);
            float tolerance = limit + 1e-3f;

            for (int i = 0; i < route.Count; i++)
            {
                Vector3 point = ((route[i] - ship) * UnitsPerMeter).ToUnity();
                Vector3 toPoint = point - pose.PositionUnits;
                float depth = Vector3.Dot(toPoint, forward);
                Assert.That(depth, Is.GreaterThan(0f), $"point {i} is in front of the camera");

                float ndcX = Vector3.Dot(toPoint, right) / (depth * halfHeight * Aspect);
                float ndcY = Vector3.Dot(toPoint, up) / (depth * halfHeight);
                Assert.That(Mathf.Abs(ndcX), Is.LessThanOrEqualTo(tolerance),
                    $"point {i} is {what} horizontally");
                Assert.That(Mathf.Abs(ndcY), Is.LessThanOrEqualTo(tolerance),
                    $"point {i} is {what} vertically");
            }
        }
    }
}
