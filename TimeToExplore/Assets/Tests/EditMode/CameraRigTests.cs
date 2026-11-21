using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>How far the camera sits back, and how the map blend changes the rig.</summary>
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
            _rig.MapEnabled = true;
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
        public void TheMapPoseIsTopDownAndKeepsThePlayersStoredPose()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);
            float mapDistance = (float)(3.0 * radiusM * _rig.UnitsPerMeter);

            _rig.Orbit.YawDegrees = 37f;
            _rig.Orbit.PitchDegrees = 12f;
            _rig.Orbit.DistanceUnits = mapDistance;

            _rig.Apply(shipM, 0f);

            Assert.That(_rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(_rig.IsMapSide, Is.True);
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(37f), "the player's yaw is untouched");
            Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(12f), "and so is the pitch");

            Vector3 forward = _rig.PoseRotation * Vector3.forward;
            Assert.That(Vector3.Angle(forward, Vector3.down), Is.LessThan(1e-3f),
                "the map looks straight down the pole");

            Vector3 up = _rig.PoseRotation * Vector3.up;
            Assert.That(up.y, Is.EqualTo(0f).Within(1e-5f),
                "the up vector is horizontal");
            Assert.That(Vector3.Angle(up, MapView.MapScreenUp(37f)), Is.LessThan(0.05f),
                "the up vector keeps the player's yaw");

            Assert.That(_rig.PosePositionUnits.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(_rig.PosePositionUnits.z, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(_rig.PosePositionUnits.y, Is.EqualTo(mapDistance).Within(mapDistance * 1e-4f),
                "a top-down camera sits directly above the look-at point");
            AssertFinite(_rig.PoseRotation);
        }

        [Test]
        public void ZoomingOutToTheMapAndBackRestoresTheExactPose()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);
            float localDistance = (float)(radiusM * _rig.UnitsPerMeter);

            _rig.Orbit.YawDegrees = 123.5f;
            _rig.Orbit.PitchDegrees = -22.25f;
            _rig.Orbit.DistanceUnits = localDistance;
            _rig.Apply(shipM, 0f);

            float rememberedYaw = _rig.Orbit.YawDegrees;
            float rememberedPitch = _rig.Orbit.PitchDegrees;
            Quaternion localRotation = _rig.PoseRotation;
            Vector3 localOffset = _rig.PosePositionUnits;

            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-5f));

            _rig.Orbit.DistanceUnits = localDistance;
            _rig.Apply(shipM, 0f);

            Assert.That(_rig.MapBlendWeight, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(rememberedYaw));
            Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(rememberedPitch));
            Assert.That(Quaternion.Angle(_rig.PoseRotation, localRotation), Is.LessThan(1e-3f),
                "the remembered angle is back exactly");
            Assert.That(Vector3.Distance(_rig.PosePositionUnits, localOffset),
                Is.LessThan(localOffset.magnitude * 1e-4f));
            Assert.That(_rig.LookAtUnits.magnitude, Is.EqualTo(0f),
                "with no pan the local view looks at the ship again");
        }

        [Test]
        public void TheWheelEasesTheZoomAndTheTiltFollowsIt()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);
            float localDistance = (float)(radiusM * _rig.UnitsPerMeter); // w = 0 exactly

            _rig.Orbit.YawDegrees = 30f;
            _rig.Orbit.PitchDegrees = 12f;
            _rig.Orbit.DistanceUnits = localDistance;
            _rig.Apply(shipM, 1f / 60f);

            Assert.That(_rig.MapBlendWeight, Is.EqualTo(0f).Within(1e-6f));
            float localTilt = TiltFromTopDown(_rig.PoseRotation);

            // Six notches request 3.81x the local radius: fully on the map side.
            _rig.ApplyWheelSteps(6f);
            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(localDistance),
                "the notch requests a distance; the camera has not moved yet");

            float previousDistance = _rig.Orbit.DistanceUnits;
            float previousTilt = localTilt;
            int frames = 0;
            bool sawIntermediateTilt = false;

            while (!_rig.Orbit.IsDistanceSettled && frames < 600)
            {
                _rig.Apply(shipM, 1f / 60f);

                Assert.That(_rig.Orbit.DistanceUnits,
                    Is.GreaterThanOrEqualTo(previousDistance), "the zoom eases outward");
                Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(12f),
                    "the player's stored pitch is untouched");

                float tilt = TiltFromTopDown(_rig.PoseRotation);
                Assert.That(tilt, Is.LessThanOrEqualTo(previousTilt + 1e-3f),
                    "the pose tilts toward top-down, never away");
                if (tilt > 1f && tilt < localTilt - 1f) sawIntermediateTilt = true;

                previousDistance = _rig.Orbit.DistanceUnits;
                previousTilt = tilt;
                frames++;
            }

            Assert.That(frames, Is.GreaterThan(1), "the zoom takes many frames, not one");
            Assert.That(frames, Is.LessThan(600));
            Assert.That(sawIntermediateTilt, Is.True, "the tilt eases through the blend band");
            Assert.That(_rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(TiltFromTopDown(_rig.PoseRotation), Is.LessThan(0.01f),
                "the arrival is exactly top-down");
        }

        [Test]
        public void TheMapSidePansAndTheSixtyAuCapHoldsThroughTheRig()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);

            _rig.PanByDrag(100f, 0f);
            _rig.Apply(shipM, 0f);

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.GreaterThan(0.0),
                "a map drag moves the look-at offset");
            Assert.That(_rig.LookAtUnits.magnitude, Is.GreaterThan(0f),
                "the camera looks at ship + pan");

            Vector3 right = MapView.MapRotation(0f) * Vector3.right;
            Assert.That(Vector3.Dot(_rig.LookAtUnits, right), Is.LessThan(0f),
                "the content follows the cursor");

            _rig.PanByDrag(1.0e9f, -1.0e9f);
            _rig.Apply(shipM, 0f);

            Assert.That(_rig.Map.PanOffsetM.Magnitude,
                Is.LessThanOrEqualTo(MapView.MaxPanM + 1.0),
                "the 60 AU cap holds through the rig");
        }

        [Test]
        public void TheRightButtonAlwaysOrbitsAndTheLeftButtonPansTheMap()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.IsMapSide, Is.True, "the test starts on the map side");

            // The right button rotates on the map too, and never pans.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                RightPressed = true,
                Pointer = new Vector2(100f, 100f),
                Delta = new Vector2(10f, 0f)
            }, mapSide: true);

            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(30f).Within(1e-3f),
                "the right button keeps its 3 degrees per axis unit on the map");
            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "a right drag never pans");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { RightReleased = true }, true);

            // A left click's jitter stays inside the slop and must not nudge the
            // map: at the ceiling a few pixels are tens of millions of km.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                LeftPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(102f, 101f),
                Delta = new Vector2(0.2f, 0.1f)
            }, mapSide: true);
            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "a click's jitter is not a pan");

            // Past the slop the left button pans, and never rotates.
            float yaw = _rig.Orbit.YawDegrees;
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(138f, 100f),
                Delta = new Vector2(3.8f, 0f)
            }, mapSide: true);

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.GreaterThan(0.0),
                "the left button pans on the map");
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(yaw), "a left drag never rotates");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { LeftReleased = true }, true);

            // The local band has nothing to pan, so the same left drag is inert.
            _rig.Map.ClearPan();
            _rig.Orbit.DistanceUnits = (float)(0.5 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.IsMapSide, Is.False, "the test is back in the local band");

            yaw = _rig.Orbit.YawDegrees;
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                LeftPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: false);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(138f, 100f),
                Delta = new Vector2(3.8f, 0f)
            }, mapSide: false);

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "a left drag outside the map does not pan");
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(yaw),
                "and it does not orbit either");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { LeftReleased = true }, false);
        }

        [Test]
        public void APointerGestureThatStartsOnAPanelNeverMovesTheCamera()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.YawDegrees = 0f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.IsMapSide, Is.True, "the test starts on the map side");

            // A left press on the planning slider: the panel owns the whole drag,
            // including the frames where the pointer leaves it.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                LeftPressed = true,
                Pointer = new Vector2(100f, 100f),
                OverUi = true
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(160f, 100f),
                Delta = new Vector2(6f, 0f),
                OverUi = true
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(900f, 100f),
                Delta = new Vector2(6f, 0f)
            }, mapSide: true);

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "dragging a slider neither pans nor pans late, after leaving the panel");

            // The right button gets the same treatment.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { LeftReleased = true }, true);
            float yaw = _rig.Orbit.YawDegrees;
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                RightPressed = true,
                Pointer = new Vector2(100f, 100f),
                Delta = new Vector2(10f, 0f),
                OverUi = true
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(160f, 100f),
                Delta = new Vector2(10f, 0f)
            }, mapSide: true);

            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(yaw),
                "a right drag that starts on a panel never orbits");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { RightReleased = true }, true);
        }

        [Test]
        public void APressInTheWorldKeepsItsDragWhenItCrossesAPanel()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.IsMapSide, Is.True, "the test starts on the map side");

            // The press landed in the world, so the camera owns the gesture.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                LeftPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(160f, 100f),
                Delta = new Vector2(6f, 0f)
            }, mapSide: true);
            double panM = _rig.Map.PanOffsetM.Magnitude;
            Assert.That(panM, Is.GreaterThan(0.0), "the press in the world pans");

            // Crossing a panel mid-drag does not hand the gesture back.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(200f, 100f),
                Delta = new Vector2(6f, 0f),
                OverUi = true
            }, mapSide: true);

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.GreaterThan(panM),
                "a world press keeps panning over a panel until the release");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { LeftReleased = true }, true);
        }

        [Test]
        public void AnUnengagedPanIsCancelledIfThePointerTurnsOutToBeOnAPanel()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.IsMapSide, Is.True, "the test starts on the map side");

            // The press frame reads the EventSystem one frame late and reports
            // the world. The pan must not engage once the panel answers.
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                LeftPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(103f, 100f),
                Delta = new Vector2(0.3f, 0f),
                OverUi = true
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(160f, 100f),
                Delta = new Vector2(6f, 0f)
            }, mapSide: true);

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.EqualTo(0.0).Within(1e-6),
                "a late panel answer cancels the pan before it engages");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { LeftReleased = true }, true);
        }

        [Test]
        public void ALeftDragOnTheMapClearsBothAutoFlags()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Frame(_model.Get("mars"));
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.Auto, Is.True, "the map suspends framing but keeps the flags");

            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                LeftPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(120f, 100f),
                Delta = new Vector2(2f, 0f)
            }, mapSide: true);

            Assert.That(_rig.AutoAim, Is.False);
            Assert.That(_rig.AutoDistance, Is.False);
            Assert.That(_rig.Auto, Is.False, "a pan takes the view from the framing");
        }

        [Test]
        public void ReturningToTheLocalBandEasesThePanToZero()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            _rig.PanByDrag(200f, 0f);
            _rig.Apply(shipM, 0f);

            double pannedM = _rig.Map.PanOffsetM.Magnitude;
            Assert.That(pannedM, Is.GreaterThan(0.0));

            // Zoom back inside the local radius: w = 0, so the pan eases home.
            _rig.Orbit.DistanceUnits = (float)(0.9 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 1f / 60f);
            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.GreaterThan(0.9 * pannedM),
                "one frame does not snap the pan");

            double previous = _rig.Map.PanOffsetM.Magnitude;
            for (int i = 0; i < 600; i++)
            {
                _rig.Apply(shipM, 1f / 60f);
                Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.LessThanOrEqualTo(previous));
                previous = _rig.Map.PanOffsetM.Magnitude;
            }

            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.LessThan(1.0),
                "the local view lands on the ship");
            Assert.That(_rig.LookAtUnits.magnitude, Is.LessThan(1e-6f));
        }

        [Test]
        public void TheWheelClimbsToTheMapCeilingOnlyOnTheMapSide()
        {
            Assert.That(_rig.WheelDistanceUnits(1.0e5f, 30f, 1f),
                Is.EqualTo(MapView.MaxDistanceUnits),
                "the map side reaches 12 billion km");
            Assert.That(_rig.WheelDistanceUnits(1.0e5f, 30f, 0f),
                Is.EqualTo(OrbitCamera.MaxDistanceUnits),
                "the local side keeps its 5-million-unit clamp");

            Assert.That(_rig.WheelDistanceUnits(100f, 1f, 0.6f), Is.EqualTo(125f).Within(1e-3f));
            Assert.That(_rig.WheelDistanceUnits(100f, 1f, 0.4f), Is.EqualTo(125f).Within(1e-3f),
                "one notch is still 1.25x on both sides");
            Assert.That(_rig.WheelDistanceUnits(1f, -100f, 0.6f),
                Is.EqualTo(OrbitCamera.MinDistanceUnits));
            Assert.That(_rig.WheelDistanceUnits(1f, -100f, 0.4f),
                Is.EqualTo(OrbitCamera.MinDistanceUnits));
        }

        [Test]
        public void AutoFramingIsSuspendedOnTheMapAndResumesLocally()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);
            BodyRuntime mars = _model.Get("mars");

            _rig.Frame(mars);
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);

            float mapDistance = _rig.Orbit.DistanceUnits;
            float mapYaw = _rig.Orbit.YawDegrees;
            _rig.Apply(shipM, 1f);

            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(mapDistance),
                "auto-framing must not pull a map zoom back in");
            Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(mapYaw),
                "and must not turn the map away from the player's yaw");
            Assert.That(_rig.AutoDistance, Is.True,
                "the framing is suspended, not discarded");

            // Back inside the local band: the framing resumes. Framing a distant
            // target may ease back out across the blend, which is the map's job.
            _rig.Orbit.DistanceUnits = (float)(0.5 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 1f);
            Assert.That(_rig.Orbit.DistanceUnits, Is.GreaterThan(0.5 * radiusM * _rig.UnitsPerMeter),
                "the distance eases toward the framed target again");
        }

        [Test]
        public void TheNearPlaneFollowsTheBlendAndStaysInsideTheFarClip()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.PoseNearClipUnits,
                Is.EqualTo(MapView.NearClipUnits(_rig.Orbit.DistanceUnits, 1f)).Within(1e-6f));
            Assert.That(_rig.PoseNearClipUnits, Is.GreaterThan(0f));
            Assert.That(_rig.PoseNearClipUnits, Is.LessThan(MapView.FarClipUnits));

            _rig.Orbit.DistanceUnits = (float)(0.2 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.PoseNearClipUnits,
                Is.EqualTo(MapView.LocalNearClipUnits(_rig.Orbit.DistanceUnits)).Within(1e-6f),
                "the local side is exactly today's near plane");
        }

        [Test]
        public void TheLocalViewIsUnchangedWhenTheMapIsOff()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            _rig.MapEnabled = false;
            _rig.Orbit.YawDegrees = 210f;
            _rig.Orbit.PitchDegrees = -8f;
            _rig.Orbit.DistanceUnits = 0.185f;

            _rig.Apply(shipM, 0f);

            Assert.That(_rig.MapBlendWeight, Is.EqualTo(0f));
            Assert.That(Quaternion.Angle(_rig.PoseRotation, _rig.Orbit.Rotation),
                Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(_rig.PosePositionUnits, _rig.Orbit.OffsetUnits),
                Is.LessThan(1e-4f));
            Assert.That(_rig.PoseNearClipUnits,
                Is.EqualTo(MapView.LocalNearClipUnits(0.185f)).Within(1e-6f));
            Assert.That(_rig.WheelDistanceUnits(1.0e5f, 30f, 1f),
                Is.EqualTo(OrbitCamera.MaxDistanceUnits),
                "the local clamp still stops at 33 AU when the map is off");
        }

        [Test]
        public void ACursorZoomKeepsThePointUnderThePointerThroughTheTween()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);
            float mapDistance = (float)(3.0 * radiusM * _rig.UnitsPerMeter);

            _rig.Orbit.YawDegrees = 35f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = mapDistance;
            _rig.Apply(shipM, 0f);
            Assert.That(_rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-5f));

            var screen = new Vector2(1920f, 1080f);
            var pointer = new Vector2(1500f, 800f);
            const float fieldOfView = 60f;
            float aspect = screen.x / screen.y;
            Vector2 ndc = MapView.PointerNdc(pointer, screen);

            Vector3 anchor = MapView.CursorPointOnLookAtPlane(_rig.LookAtUnits,
                _rig.PoseRotation, _rig.Orbit.DistanceUnits, ndc, fieldOfView, aspect);

            _rig.ApplyWheelSteps(-4f, pointer, screen);
            Assert.That(_rig.Orbit.DistanceUnits, Is.EqualTo(mapDistance),
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
            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.GreaterThan(0.0),
                "zooming off-centre moved the look-at toward the pointer");
            Assert.That(_rig.MapBlendWeight, Is.LessThan(MapView.PanThreshold),
                "the test lands in the local band");

            // The pan is the point the player anchored. The local pan-return
            // must not pull it home while that anchor is held.
            double anchoredPanM = _rig.Map.PanOffsetM.Magnitude;
            for (int i = 0; i < 120; i++) _rig.Apply(shipM, 1f / 60f);

            Assert.That(_rig.Map.PanOffsetM.Magnitude,
                Is.EqualTo(anchoredPanM).Within(anchoredPanM * 1e-6));
        }

        [Test]
        public void SpaceEasesTheViewHomeToTheShipInOneSecond()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Frame(_model.Get("mars"));
            _rig.Orbit.YawDegrees = 37f;
            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);
            _rig.PanByDrag(200f, 0f);
            _rig.Apply(shipM, 0f);

            float startDistance = _rig.Orbit.DistanceUnits;
            double startPanM = _rig.Map.PanOffsetM.Magnitude;
            Assert.That(startPanM, Is.GreaterThan(0.0));
            Assert.That(_rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-5f));

            _rig.HomeToShip(shipM);
            Assert.That(_rig.IsHoming, Is.True);
            Assert.That(_rig.Auto, Is.False, "home takes the view from the framing");

            float previousDistance = startDistance;
            double previousPanM = startPanM;
            int frames = 0;
            while (_rig.IsHoming && frames < 600)
            {
                _rig.Apply(shipM, 1f / 60f);

                Assert.That(_rig.Orbit.DistanceUnits,
                    Is.LessThanOrEqualTo(previousDistance),
                    "the home zoom never runs outward");
                Assert.That(_rig.Map.PanOffsetM.Magnitude,
                    Is.LessThanOrEqualTo(previousPanM),
                    "the pan unwinds monotonically");
                Assert.That(_rig.Orbit.YawDegrees, Is.EqualTo(37f),
                    "the remembered angle is kept");
                Assert.That(_rig.Orbit.PitchDegrees, Is.EqualTo(10f));

                previousDistance = _rig.Orbit.DistanceUnits;
                previousPanM = _rig.Map.PanOffsetM.Magnitude;
                frames++;
            }

            Assert.That(frames, Is.InRange(59, 62),
                "about a second of frames, not a snap");
            Assert.That(_rig.Orbit.DistanceUnits,
                Is.EqualTo(_rig.HomeDistanceUnits(radiusM)).Within(1e-6f));
            Assert.That(_rig.Map.PanOffsetM.Magnitude, Is.EqualTo(0.0));
            Assert.That(_rig.LookAtUnits.magnitude, Is.EqualTo(0f).Within(1e-6f),
                "the camera looks at the ship again");
            Assert.That(_rig.MapBlendWeight, Is.EqualTo(0f).Within(1e-6f),
                "the arrival is the local view");
        }

        [Test]
        public void AWheelOrDragCancelsTheHomeTransition()
        {
            Vector3d shipM = _model.Get("gateway").PositionM;
            double radiusM = MapView.LocalRadiusM(_model, shipM);

            _rig.Orbit.PitchDegrees = 10f;
            _rig.Orbit.DistanceUnits = (float)(3.0 * radiusM * _rig.UnitsPerMeter);
            _rig.Apply(shipM, 0f);

            _rig.HomeToShip(shipM);
            _rig.Apply(shipM, 1f / 60f);
            Assert.That(_rig.IsHoming, Is.True);

            _rig.ApplyWheelSteps(1f);
            Assert.That(_rig.IsHoming, Is.False, "a wheel gesture takes the view back");

            _rig.HomeToShip(shipM);
            _rig.Apply(shipM, 1f / 60f);
            Assert.That(_rig.IsHoming, Is.True);

            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                RightPressed = true,
                Pointer = new Vector2(100f, 100f)
            }, mapSide: true);
            _rig.ApplyDragInput(new ShipCameraRig.DragInput
            {
                Pointer = new Vector2(120f, 100f),
                Delta = new Vector2(2f, 0f)
            }, mapSide: true);

            Assert.That(_rig.IsHoming, Is.False, "an orbit drag takes the view back");
            _rig.ApplyDragInput(new ShipCameraRig.DragInput { RightReleased = true }, true);
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

        /// <summary>0° is straight down the pole, 90° is looking along the ecliptic.</summary>
        private static float TiltFromTopDown(Quaternion rotation) =>
            Vector3.Angle(rotation * Vector3.forward, Vector3.down);

        private static void AssertFinite(Quaternion rotation)
        {            Assert.That(float.IsNaN(rotation.x) || float.IsInfinity(rotation.x), Is.False);
            Assert.That(float.IsNaN(rotation.y) || float.IsInfinity(rotation.y), Is.False);
            Assert.That(float.IsNaN(rotation.z) || float.IsInfinity(rotation.z), Is.False);
            Assert.That(float.IsNaN(rotation.w) || float.IsInfinity(rotation.w), Is.False);
        }
    }
}
