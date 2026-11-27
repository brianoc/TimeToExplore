using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>How far the camera sits back, and how the rig places one orbit pose.</summary>
    [Category("presentation")]
    public class CameraRigTests
    {
        private SolarSystemModel _model;
        private GameObject _root;
        private ShipCameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _root = new GameObject("Camera Rig Test");
            _rig = _root.AddComponent<ShipCameraRig>();
            _rig.Initialise(null, _model, 1e-6f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void ArrivalIsFramedAtThreeBodyRadii()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            ParkingOrbit.StateAt(mars, marsPosition, marsVelocity, 0.0,
                out Vector3d shipPosition, out _);

            float units = _rig.AutoDistanceUnits(shipPosition, mars);

            double metres = units / _rig.UnitsPerMeter;
            Assert.That(metres, Is.EqualTo(3.0 * mars.RadiusM).Within(mars.RadiusM * 0.01),
                "the parking orbit is only 300 km up, so three radii wins over the fraction");
        }

        [Test]
        public void CruiseOpensTheViewOut()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            var shipPosition = marsPosition + new Vector3d(7.5e10, 0.0, 0.0); // 0.5 AU
            float units = _rig.AutoDistanceUnits(shipPosition, mars);

            double metres = units / _rig.UnitsPerMeter;
            Assert.That(metres, Is.EqualTo(0.6 * 7.5e10).Within(1e6));
            Assert.That(units, Is.GreaterThan(1f), "tens of thousands of render units");
        }

        [Test]
        public void TheViewIsClampedForAVeryDistantTarget()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            var shipPosition = marsPosition + new Vector3d(0.0, 1.0e13, 0.0);
            float units = _rig.AutoDistanceUnits(shipPosition, mars);

            Assert.That(units / _rig.UnitsPerMeter, Is.LessThanOrEqualTo(5.0e11 + 1.0));
        }

        [Test]
        public void ThePlanLineStaysThinAtEveryZoom()
        {
            // The reported bug: the line's width came from the length of the
            // plan, so at a 185,000 km orbit it was 225,000 km thick and the
            // camera was inside it. It must always be a sliver of the distance.
            float[] distances =
            {
                OrbitCamera.MinDistanceUnits,
                0.185f,   // the Gateway opening shot
                1_000f,
                120_000f, // a whole inner-system plan
                OrbitCamera.MaxDistanceUnits
            };

            foreach (float distance in distances)
            {
                float width = LineWidth.WorldWidth(distance, 60f, 1080f);
                Assert.That(width / distance, Is.LessThan(0.01f),
                    $"a {distance} unit camera must not sit inside a {width} unit line");
                Assert.That(width, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void FramingABodyTurnsAutoOnAndClearingItHandsBack()
        {
            BodyRuntime earth = _model.Get("earth");

            _rig.Frame(earth);
            Assert.That(_rig.Auto, Is.True);
            Assert.That(_rig.AutoAim, Is.True);
            Assert.That(_rig.AutoDistance, Is.True);
            Assert.That(_rig.AutoTarget, Is.SameAs(earth));

            _rig.ClearAuto();
            Assert.That(_rig.Auto, Is.False);
            Assert.That(_rig.AutoAim, Is.False);
            Assert.That(_rig.AutoDistance, Is.False);
            Assert.That(_rig.AutoTarget, Is.Null);
        }

        [Test]
        public void ZoomingOutAndBackRestoresTheExactPose()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;

            _rig.Orbit.YawDegrees = 123.5f;
            _rig.Orbit.PitchDegrees = -22.25f;
            _rig.Orbit.DistanceUnits = 0.185f;
            _rig.Apply(shipM, 0f);

            float rememberedYaw = _rig.Orbit.YawDegrees;
            float rememberedPitch = _rig.Orbit.PitchDegrees;
            Quaternion localRotation = _rig.PoseRotation;
            Vector3 localOffset = _rig.PosePositionUnits;

            _rig.Orbit.DistanceUnits = OrbitCamera.MaxDistanceUnits;
            _rig.Apply(shipM, 0f);

            Assert.That(Vector3.Angle(_rig.PoseRotation * Vector3.forward,
                OrbitCamera.Direction(rememberedYaw, rememberedPitch) * -1f),
                Is.LessThan(1e-2f),
                "the far view keeps the same orbit pose, without a top-down tilt");

            _rig.Orbit.DistanceUnits = 0.185f;
            _rig.Apply(shipM, 0f);

            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(rememberedYaw));
            Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(rememberedPitch));
            Assert.That(Quaternion.Angle(_rig.PoseRotation, localRotation), Is.LessThan(1e-3f),
                "the remembered angle is back exactly");
            Assert.That(Vector3.Distance(_rig.PosePositionUnits, localOffset),
                Is.LessThan(localOffset.magnitude * 1e-4f));
            Assert.That(_rig.LookAtUnits.magnitude, Is.EqualTo(0f),
                "with no anchor the view looks at the ship again");
        }

        [Test]
        public void TheWheelReachesTheWholeSystemAndEasesThere()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            _rig.Orbit.DistanceUnits = 0.185f;
            _rig.Apply(shipM, 0f);

            // Enough notches to pass the old 33 AU ceiling and reach 80 AU.
            _rig.ApplyWheelSteps(90f);
            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(0.185f),
                "the notch requests a distance; the camera has not moved yet");
            Assert.That(_rig.Orbit.TargetDistanceUnits,
                Is.EqualTo(OrbitCamera.MaxDistanceUnits));

            float previousDistance = _rig.Orbit.DistanceUnits;
            int frames = 0;
            while (!_rig.Orbit.IsDistanceSettled && frames < 600)
            {
                _rig.Apply(shipM, 1f / 60f);
                Assert.That(_rig.Orbit.DistanceUnits,
                    Is.GreaterThanOrEqualTo(previousDistance), "the zoom eases outward");
                previousDistance = _rig.Orbit.DistanceUnits;
                frames++;
            }

            Assert.That(frames, Is.GreaterThan(1), "the zoom takes many frames, not one");
            Assert.That(frames, Is.LessThan(600));
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(OrbitCamera.MaxDistanceUnits));
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(0f),
                "zooming never rotates the view");
            Assert.That(_rig.Orbit.PitchDegrees,
                Is.EqualTo(OrbitCamera.DefaultPitchDegrees));
        }

        [Test]
        public void TheRightButtonOrbitsAtEveryZoom()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;

            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = OrbitCamera.MaxDistanceUnits;
            _rig.Apply(shipM, 0f);

            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                RightPressed = true,
                Delta = new Vector2(10f, 0f)
            });

            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(30f).Within(1e-3f),
                "the right button keeps its 3 degrees per axis unit at any distance");
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "an orbit drag never moves the look-at point");
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput { RightReleased = true });
        }

        [Test]
        public void APointerGestureThatStartsOnAPanelNeverMovesTheCamera()
        {
            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 10f;

            float yaw = _rig.Orbit.YawDegrees;
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                RightPressed = true,
                Delta = new Vector2(10f, 0f),
                OverUi = true
            });
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                Delta = new Vector2(10f, 0f)
            });

            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(yaw),
                "a right drag that starts on a panel never orbits");
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput { RightReleased = true });
        }

        [Test]
        public void APressInTheWorldKeepsItsDragWhenItCrossesAPanel()
        {
            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                RightPressed = true,
                Delta = new Vector2(0f, 0f)
            });
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                Delta = new Vector2(6f, 0f)
            });
            float yaw = _rig.Orbit.YawDegrees;

            // Crossing a panel mid-drag does not hand the gesture back.
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                Delta = new Vector2(6f, 0f),
                OverUi = true
            });

            Assert.That(_rig.Orbit.YawDegrees, Is.GreaterThan(yaw),
                "a world press keeps orbiting over a panel until the release");
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput { RightReleased = true });
        }

        [Test]
        public void TheNearPlaneScalesWithTheOrbitAndStaysInsideTheFarClip()
        {
            Assert.That(OrbitCamera.NearClipUnits(OrbitCamera.MinDistanceUnits),
                Is.EqualTo(OrbitCamera.MinNearClipUnits));
            Assert.That(OrbitCamera.NearClipUnits(1f),
                Is.EqualTo(1f * OrbitCamera.NearClipFraction).Within(1e-6f));
            Assert.That(OrbitCamera.NearClipUnits(OrbitCamera.MaxDistanceUnits),
                Is.EqualTo(OrbitCamera.MaxNearClipUnits));
            Assert.That(OrbitCamera.MaxNearClipUnits,
                Is.LessThan(OrbitCamera.FarClipUnits));
            Assert.That(OrbitCamera.MaxDistanceUnits,
                Is.LessThan(OrbitCamera.FarClipUnits));

            Vector3d shipM = _model.Get("gateway").PositionM;
            _rig.Orbit.DistanceUnits = 1_000f;
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.PoseNearClipUnits,
                Is.EqualTo(OrbitCamera.NearClipUnits(_rig.Orbit.DistanceUnits)).Within(1e-6f));
        }

        [Test]
        public void ACursorZoomKeepsThePointUnderThePointerThroughTheTween()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;

            _rig.Orbit.YawDegrees = 35f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = 1_000f;
            _rig.Apply(shipM, 0f);

            var screen = new Vector2(1920f, 1080f);
            var pointer = new Vector2(1500f, 800f);
            const float fieldOfView = 60f;
            float aspect = screen.x / screen.y;
            Vector2 ndc = CursorZoom.PointerNdc(pointer, screen);

            Vector3 anchor = CursorZoom.CursorPointOnLookAtPlane(_rig.LookAtUnits,
                _rig.PoseRotation, _rig.Orbit.DistanceUnits, ndc, fieldOfView, aspect);

            _rig.ApplyWheelSteps(-4f, pointer, screen);
            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(1_000f),
                "the notch only requests the zoom");

            float previousDistance = _rig.Orbit.DistanceUnits;
            int frames = 0;
            while (!_rig.Orbit.IsDistanceSettled && frames < 600)
            {
                _rig.Apply(shipM, 1f / 60f);

                Vector2 projected = NdcOf(anchor, _rig.PosePositionUnits,
                    _rig.PoseRotation, fieldOfView, aspect);
                Assert.That(projected.x, Is.EqualTo(ndc.x).Within(1e-3f),
                    $"frame {frames}: the anchor stays under the pointer horizontally");
                Assert.That(projected.y, Is.EqualTo(ndc.y).Within(1e-3f),
                    $"frame {frames}: and vertically");

                Assert.That(_rig.Orbit.DistanceUnits, Is.LessThan(previousDistance),
                    "the zoom eases inward");
                previousDistance = _rig.Orbit.DistanceUnits;
                frames++;
            }

            Assert.That(frames, Is.GreaterThan(1), "the zoom is a tween, not a snap");
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.GreaterThan(0.0),
                "zooming off-centre moved the look-at toward the pointer");

            // The look-at is the point the player anchored. It must not ease
            // home on its own; Space is the explicit way back.
            double anchoredOffsetM = _rig.LookAtOffsetM.Magnitude;
            for (int i = 0; i < 120; i++) _rig.Apply(shipM, 1f / 60f);

            Assert.That(_rig.LookAtOffsetM.Magnitude,
                Is.EqualTo(anchoredOffsetM).Within(anchoredOffsetM * 1e-6));
        }

        [Test]
        public void SpaceEasesTheViewHomeToTheShipInOneSecond()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = LocalScale.RadiusM(_model, shipM);

            _rig.Orbit.YawDegrees = 37f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = OrbitCamera.MaxDistanceUnits;
            _rig.Apply(shipM, 0f);

            // Anchor an off-centre look-at point with a wheel gesture.
            _rig.ApplyWheelSteps(-2f, new Vector2(1500f, 800f),
                new Vector2(1920f, 1080f));
            int settle = 0;
            while (!_rig.Orbit.IsDistanceSettled && settle < 600)
            {
                _rig.Apply(shipM, 1f / 60f);
                settle++;
            }
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.GreaterThan(0.0));

            float startDistance = _rig.Orbit.DistanceUnits;
            double startOffsetM = _rig.LookAtOffsetM.Magnitude;

            _rig.HomeToShip(shipM);
            Assert.That(_rig.IsHoming, Is.True);
            Assert.That(_rig.Auto, Is.False, "home takes the view from the framing");

            float previousDistance = startDistance;
            double previousOffsetM = startOffsetM;
            int frames = 0;
            while (_rig.IsHoming && frames < 600)
            {
                _rig.Apply(shipM, 1f / 60f);

                Assert.That(_rig.Orbit.DistanceUnits,
                    Is.LessThanOrEqualTo(previousDistance),
                    "the home zoom never runs outward");
                Assert.That(_rig.LookAtOffsetM.Magnitude,
                    Is.LessThanOrEqualTo(previousOffsetM),
                    "the look-at offset unwinds monotonically");
                Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(37f),
                    "the remembered angle is kept");
                Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(10f));

                previousDistance = _rig.Orbit.DistanceUnits;
                previousOffsetM = _rig.LookAtOffsetM.Magnitude;
                frames++;
            }

            Assert.That(frames, Is.InRange(59, 62),
                "about a second of frames, not a snap");
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(_rig.HomeDistanceUnits(radiusM)).Within(1e-6f));
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.EqualTo(0.0));
            Assert.That(_rig.LookAtUnits.magnitude, Is.EqualTo(0f).Within(1e-6f),
                "the camera looks at the ship again");
        }

        [Test]
        public void AWheelOrDragCancelsTheHomeTransition()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;

            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = OrbitCamera.MaxDistanceUnits;
            _rig.Apply(shipM, 0f);

            _rig.HomeToShip(shipM);
            _rig.Apply(shipM, 1f / 60f);
            Assert.That(_rig.IsHoming, Is.True);

            _rig.ApplyWheelSteps(1f);
            Assert.That(_rig.IsHoming, Is.False, "a wheel gesture takes the view back");

            _rig.HomeToShip(shipM);
            _rig.Apply(shipM, 1f / 60f);
            Assert.That(_rig.IsHoming, Is.True);

            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                RightPressed = true,
                Delta = new Vector2(2f, 0f)
            });

            Assert.That(_rig.IsHoming, Is.False, "an orbit drag takes the view back");
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput { RightReleased = true });
        }

        [Test]
        public void AutoFramingCapturesTheDistanceInsteadOfRecomputingIt()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d earthM = earth.PositionM;
            Vector3d farShip = earthM + new Vector3d(7.5e10, 0.0, 0.0);

            _rig.Frame(earth);
            _rig.Apply(farShip, 0f);

            float captured = _rig.AutoDistanceTargetUnits;
            Assert.That(captured, Is.GreaterThan(0f));
            Assert.That(captured,
                Is.EqualTo(_rig.AutoDistanceUnits(farShip, earth)).Within(1e-3f));

            // The ship closes half the distance. A per-frame re-target would
            // halve the framing distance; a captured one stays put.
            Vector3d closerShip = earthM + new Vector3d(3.75e10, 0.0, 0.0);
            for (int i = 0; i < 60; i++) _rig.Apply(closerShip, 1f / 60f);

            Assert.That(_rig.AutoDistanceTargetUnits, Is.EqualTo(captured).Within(1e-3f),
                "the framing distance is captured once");
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(captured).Within(captured * 1e-4f),
                "the camera eases to the captured distance, not the moving one");

            // The arrival hook is the one place that re-captures it.
            _rig.RecalculateFramingDistance(closerShip);
            Assert.That(_rig.AutoDistanceTargetUnits,
                Is.EqualTo(_rig.AutoDistanceUnits(closerShip, earth)).Within(1e-3f));
            Assert.That(_rig.AutoDistanceTargetUnits, Is.LessThan(captured));
        }

        [Test]
        public void ChangingTheFramingTargetTurnsAtTheSlewRate()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;

            _rig.Frame(_model.Get("earth"));
            _rig.Apply(shipM, 0f);

            _rig.Frame(_model.Get("mars"));
            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 0f;
            _rig.Apply(shipM, 1f / 60f);

            float expected = _rig.AutoSlewDegreesPerSecond / 60f;
            Assert.That(Mathf.Abs(_rig.Orbit.YawDegrees), Is.LessThanOrEqualTo(expected + 1e-3f),
                "a new target is slewed, not snapped to");
            Assert.That(Mathf.Abs(_rig.Orbit.PitchDegrees), Is.LessThanOrEqualTo(expected + 1e-3f));
        }

        [Test]
        public void TheLookDirectionAnglesAgreeWithAiming()
        {
            var direction = new Vector3d(1.0, 2.0, -3.0).Normalized;

            OrbitCamera.AnglesForLookDirection(direction, out float yaw, out float pitch);
            var orbit = new OrbitCamera();
            orbit.AimAlong(direction);

            Assert.That(orbit.YawDegrees, Is.EqualTo(yaw).Within(1e-4f));
            Assert.That(orbit.PitchDegrees, Is.EqualTo(pitch).Within(1e-4f));

            Quaternion rotation = Quaternion.LookRotation(
                -OrbitCamera.Direction(yaw, pitch), Vector3.up);
            Assert.That(Vector3.Angle(rotation * Vector3.forward, direction.ToUnity()),
                Is.LessThan(1e-3f), "the camera looks along the requested direction");
        }

        [Test]
        public void FocusDistanceIsFourRadiiClampedToTheOrbitRange()
        {
            BodyRuntime earth = _model.Get("earth");
            Assert.That(_rig.FocusDistanceUnits(earth),
                Is.EqualTo(4.0 * earth.RadiusM * _rig.UnitsPerMeter).Within(1e-4f));

            var tiny = new BodyRuntime(new BodyData
                { id = "tiny", kind = "asteroid", radiusM = 1.0 });
            Assert.That(_rig.FocusDistanceUnits(tiny),
                Is.EqualTo(OrbitCamera.MinDistanceUnits).Within(1e-6f),
                "a tiny body is framed from the closest orbit, not from inside it");

            var giant = new BodyRuntime(new BodyData
                { id = "giant", kind = "star", radiusM = 1.0e13 });
            Assert.That(_rig.FocusDistanceUnits(giant),
                Is.EqualTo(OrbitCamera.MaxDistanceUnits).Within(1e-3f));
        }

        [Test]
        public void FocusingABodyEasesTheCameraAndFramesItAtFourRadii()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d shipM = earth.PositionM + new Vector3d(7.5e10, 0.0, 0.0);
            float expectedDistance = _rig.FocusDistanceUnits(earth);

            _rig.Orbit.YawDegrees = 37f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = 1_000f;
            _rig.Apply(shipM, 0f);

            _rig.FocusBody(earth);
            Assert.That(_rig.IsFocusingBody, Is.True);
            Assert.That(_rig.IsFocused, Is.True);
            Assert.That(_rig.FocusTarget, Is.SameAs(earth));
            Assert.That(_rig.Auto, Is.False, "the focus owns the pose until it lands");
            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(1_000f),
                "the focused frame starts at the wider of the old distance and four radii");
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6));

            int frames = 0;
            while (_rig.IsFocusingBody && frames < 180)
            {
                _rig.Apply(earth.PositionM, 1f / 60f);
                frames++;
            }

            Assert.That(frames, Is.InRange(59, 62), "about a second of frames, not a snap");
            Assert.That(_rig.IsFocused, Is.True);
            Assert.That(_rig.FocusTarget, Is.SameAs(earth));
            Assert.That(_rig.AutoTarget, Is.Null, "the body is the origin; nothing follows");
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(expectedDistance).Within(expectedDistance * 1e-4f));
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(37f).Within(1e-4f));
            Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void SpaceLeavesAFocusAndADragKeepsIt()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d shipM = earth.PositionM + new Vector3d(7.5e10, 0.0, 0.0);

            _rig.FocusBody(earth);
            for (int i = 0; i < 5; i++) _rig.Apply(earth.PositionM, 1f / 60f);
            Assert.That(_rig.IsFocusingBody, Is.True);

            _rig.HomeToShip(shipM);
            Assert.That(_rig.IsFocusingBody, Is.False, "Space stops the focus move");
            Assert.That(_rig.IsFocused, Is.False, "Space leaves the focused origin");
            Assert.That(_rig.IsHoming, Is.False,
                "the origin cut lands on the home view without a cross-system zoom");
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(_rig.HomeDistanceUnits(LocalScale.RadiusM(_model, shipM)))
                    .Within(1e-6f));
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6));

            _rig.FocusBody(earth);
            for (int i = 0; i < 5; i++) _rig.Apply(earth.PositionM, 1f / 60f);
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                RightPressed = true,
                Delta = new Vector2(2f, 0f)
            });

            Assert.That(_rig.IsFocusingBody, Is.False, "a drag stops the focus move");
            Assert.That(_rig.IsFocused, Is.True,
                "a drag around the focused body keeps the focused origin");
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput { RightReleased = true });

            _rig.ExitFocus();
            Assert.That(_rig.IsFocused, Is.False);
        }

        [Test]
        public void TheFlightPoseSitsAboveAndBehindTheRoute()
        {
            var route = new Vector3d(1.0, 0.0, 0.0);
            Assert.That(FlightFraming.Pose(route, out float yaw, out float pitch), Is.True);

            Vector3 offset = OrbitCamera.Direction(yaw, pitch);
            Vector3d offsetM = Vector3d.FromUnity(offset);

            // The camera is above the ecliptic and behind the travel direction.
            Assert.That(Mathf.Asin((float)offsetM.z) * Mathf.Rad2Deg,
                Is.EqualTo(FlightFraming.ElevationDegrees).Within(0.01f));
            Assert.That(offsetM.x, Is.LessThan(0.0), "behind the ship, not ahead of it");

            // And 30° off the route's heading. The horizontal component points
            // opposite the route, turned by the side angle.
            var horizontal = new Vector3d(offsetM.x, offsetM.y, 0.0).Normalized;
            float offAxis = Mathf.Acos(Mathf.Clamp(
                (float)Vector3d.Dot(horizontal, -route), -1f, 1f)) * Mathf.Rad2Deg;
            Assert.That(offAxis, Is.EqualTo(FlightFraming.SideDegrees).Within(0.01f));
        }

        [Test]
        public void TheFlightPoseFollowsAnyHeading()
        {
            Vector3d route = new Vector3d(-1.0, 2.0, 0.5).Normalized;
            Assert.That(FlightFraming.Pose(route, out float yaw, out float pitch), Is.True);

            Vector3d cameraLook = Vector3d.FromUnity(
                Quaternion.LookRotation(-OrbitCamera.Direction(yaw, pitch), Vector3.up)
                * Vector3.forward);

            // The view direction is the route turned 30° to the side and 20°
            // down; the components are independent, so the dot product is the
            // product of the two cosines.
            float dot = (float)Vector3d.Dot(cameraLook, route);
            Assert.That(dot, Is.EqualTo(
                Mathf.Cos(FlightFraming.ElevationDegrees * Mathf.Deg2Rad)
                * Mathf.Cos(FlightFraming.SideDegrees * Mathf.Deg2Rad)).Within(1e-4f));
        }

        [Test]
        public void StartingAFlightEasesTheCameraIntoTheChasePose()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d shipM = earth.PositionM + new Vector3d(7.5e10, 0.0, 0.0);
            Vector3d route = new Vector3d(1.0, 0.2, 0.0);

            _rig.Orbit.YawDegrees = 37f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = 1_000f;
            _rig.Apply(shipM, 0f);

            Assert.That(_rig.FrameFlight(route), Is.True);
            Assert.That(_rig.IsFramingFlight, Is.True);
            Assert.That(_rig.Auto, Is.False, "the flight owns the pose, not body following");

            int frames = 0;
            while (_rig.IsFramingFlight && frames < 300)
            {
                _rig.Apply(shipM, 1f / 60f);
                frames++;
            }

            FlightFraming.Pose(route, out float expectedYaw, out float expectedPitch);
            Assert.That(frames, Is.InRange(60, 80), "about a second of frames, not a snap");
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(expectedYaw).Within(1e-3f));
            Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(expectedPitch).Within(1e-3f));
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(FlightFraming.DistanceUnits).Within(1e-6f));
            Assert.That(_rig.LookAtOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "the ship is the subject, so it sits at the centre");
        }

        [Test]
        public void TheFlightDistanceGrowsWithTheFrameTravel()
        {
            Assert.That(FlightFraming.FollowDistanceUnits(0.0, 1e-6f),
                Is.EqualTo(FlightFraming.DistanceUnits));

            // 10,000 km in a frame at an 8% motion budget needs 125,000 km,
            // which is 125 render units.
            Assert.That(FlightFraming.FollowDistanceUnits(1.0e7, 1e-6f),
                Is.EqualTo(125f).Within(1e-3f));

            // A million kilometres in a frame would need 12.5 million km; the
            // ceiling holds.
            Assert.That(FlightFraming.FollowDistanceUnits(1.0e9, 1e-6f),
                Is.EqualTo(FlightFraming.MaxFollowDistanceUnits));
        }

        [Test]
        public void FollowFlightEasesTheChaseDistanceAndAWheelEndsIt()
        {
            Vector3d shipM = _model.Get("earth").PositionM;
            _rig.FrameFlight(new Vector3d(1.0, 0.0, 0.0));
            _rig.FollowFlight(50f);

            for (int i = 0; i < 480
                            && Mathf.Abs(_rig.Orbit.DistanceUnits - 50f) > 5e-3f; i++)
                _rig.Apply(shipM, 1f / 60f);

            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(50f).Within(5e-3f));
            Assert.That(_rig.IsFollowingFlight, Is.True);

            _rig.ApplyWheelSteps(1f);
            Assert.That(_rig.IsFollowingFlight, Is.False,
                "a wheel takes the distance back from the flight");
        }

        [Test]
        public void AFlightZoomKeepsTheShipCentred()
        {
            Vector3d shipM = _model.Get("earth").PositionM;
            _rig.FrameFlight(new Vector3d(1.0, 0.0, 0.0));
            _rig.FollowFlight(50f);
            for (int i = 0; i < 480; i++) _rig.Apply(shipM, 1f / 60f);

            Assert.That(_rig.LookAtOffsetUnits.magnitude, Is.LessThan(1e-5f),
                "the chase looks at the ship");

            // A wheel at a corner would normally anchor that point and slide
            // the look-at aside. In the chase it must stay centred.
            _rig.ApplyWheelSteps(-4f, new Vector2(120f, 120f), new Vector2(1600f, 900f));
            Assert.That(_rig.IsFlightView, Is.True,
                "zooming does not leave the flight view");
            Assert.That(_rig.IsFollowingFlight, Is.False,
                "the wheel takes the automatic distance");
            Assert.That(_rig.LookAtOffsetUnits.magnitude, Is.LessThan(1e-5f),
                "the ship stays at the centre of the zoom");

            for (int i = 0; i < 30; i++) _rig.Apply(shipM, 1f / 60f);
            Assert.That(_rig.LookAtOffsetUnits.magnitude, Is.LessThan(1e-5f),
                "and stays centred through the tween");
        }

        [Test]
        public void ADragOrAnArrivalTakesTheCameraBackFromTheChasePose()
        {
            Vector3d shipM = _model.Get("earth").PositionM;

            _rig.FrameFlight(new Vector3d(1.0, 0.0, 0.0));
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput
            {
                RightPressed = true,
                Delta = new Vector2(2f, 0f)
            });
            Assert.That(_rig.IsFramingFlight, Is.False, "a drag stops the chase move");
            _rig.ApplyOrbitInput(new ShipCameraRig.OrbitInput { RightReleased = true });

            _rig.FrameFlight(new Vector3d(1.0, 0.0, 0.0));
            _rig.Apply(shipM, 1f / 60f);
            _rig.Frame(_model.Get("mars"));
            Assert.That(_rig.IsFramingFlight, Is.False,
                "the arrival frame takes over from the chase");
            Assert.That(_rig.AutoTarget, Is.SameAs(_model.Get("mars")));
        }

        /// <summary>The pointer-side NDC a point projects to from a given pose.</summary>
        private static Vector2 NdcOf(Vector3 pointUnits, Vector3 cameraUnits,
            Quaternion rotation, float fieldOfViewDegrees, float aspect)
        {
            Vector3 toPoint = pointUnits - cameraUnits;
            float depth = Vector3.Dot(toPoint, rotation * Vector3.forward);
            Assert.That(depth, Is.GreaterThan(0f), "the anchor is in front of the camera");

            float halfHeight = Mathf.Tan(0.5f * fieldOfViewDegrees * Mathf.Deg2Rad);
            return new Vector2(
                Vector3.Dot(toPoint, rotation * Vector3.right) / (depth * halfHeight * aspect),
                Vector3.Dot(toPoint, rotation * Vector3.up) / (depth * halfHeight));
        }
    }
}
