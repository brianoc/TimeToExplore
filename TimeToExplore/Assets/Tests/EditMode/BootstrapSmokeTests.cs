using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using TimeToExplore.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Runs the real composition root in edit mode: the same <c>Awake</c>,
    /// <c>Update</c>, and <c>LateUpdate</c> the scene would call. Edit mode does
    /// not call them itself, so they are invoked by reflection. This catches
    /// wiring mistakes that the pure-C# suites cannot see — a missing camera, a
    /// null field, or a renderer created without the ship.
    /// </summary>
    [Category("smoke")]
    public class BootstrapSmokeTests
    {
        private GameObject _root;
        private GameObject _cameraObject;
        private SolarSystemBootstrap _bootstrap;

        [TearDown]
        public void TearDown()
        {
            if (_bootstrap != null && _bootstrap.Sky != null) _bootstrap.Sky.Remove();
            if (_root != null) Object.DestroyImmediate(_root);
            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
        }

        [Test]
        public void TheBootstrapComposesItselfAndStartsWithTheTestedTrip()
        {
            Build();

            Assert.That(_bootstrap.Model, Is.Not.Null);
            Assert.That(_bootstrap.Clock, Is.Not.Null);
            Assert.That(_bootstrap.Flight, Is.Not.Null);
            Assert.That(_bootstrap.Ship, Is.Not.Null);
            Assert.That(_bootstrap.Renderer, Is.Not.Null);
            Assert.That(_bootstrap.Trajectory, Is.Not.Null);
            Assert.That(_bootstrap.Rig, Is.Not.Null);
            Assert.That(_bootstrap.ShipMarker, Is.Not.Null);
            Assert.That(_bootstrap.ViewCamera, Is.Not.Null);
            Assert.That(_bootstrap.Sky, Is.Not.Null);
            Assert.That(_bootstrap.Sky.Applied, Is.True,
                "the sky texture and shader must resolve from Resources");
            Assert.That(_bootstrap.ViewCamera.clearFlags,
                Is.EqualTo(CameraClearFlags.Skybox));

            _bootstrap.Model.ComputeState(_bootstrap.Model.Get("gateway"),
                _bootstrap.Clock.JulianDate, out Vector3d gateway, out _);
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, gateway), Is.LessThan(1e-6),
                "the ship must start at Gateway");
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);
            Assert.That(_bootstrap.Selected?.Id, Is.EqualTo("earth"));
            Assert.That(_bootstrap.Rig.AutoTarget?.Id, Is.EqualTo("earth"),
                "the opening shot frames Earth");
            Assert.That(_bootstrap.EnableMapView, Is.False,
                "the shipped path keeps the old camera by default");
            Assert.That(_bootstrap.Rig.MapEnabled, Is.False);
        }

        [Test]
        public void TheFrameLoopDropsAFrameUntilTheWorldIsBuilt()
        {
            _root = new GameObject("Half-built Bootstrap");
            _bootstrap = _root.AddComponent<SolarSystemBootstrap>();

            // A frame can be dispatched to a component whose Awake has not run
            // (a script reload leaves exactly this state). It must do nothing.
            Assert.DoesNotThrow(() => Invoke("Update"));
            Assert.DoesNotThrow(() => Invoke("LateUpdate"));
        }

        [Test]
        public void TheFrameLoopSkipsAViewDestroyedByATeardown()
        {
            Build();
            BodyView victim = _bootstrap.Renderer.Views.First();
            Object.DestroyImmediate(victim.gameObject);

            // The dictionary still holds the destroyed view; a teardown stops
            // play mode before the bootstrap's own objects go. Drawing the frame
            // must skip it rather than throw once per frame.
            Assert.DoesNotThrow(() => Invoke("LateUpdate"));
            Assert.DoesNotThrow(() => Invoke("LateUpdate"));
        }

        [Test]
        public void TheMapSwitchReachesTheRig()
        {
            Build(enableMapView: true);

            Assert.That(_bootstrap.EnableMapView, Is.True);
            Assert.That(_bootstrap.Rig.MapEnabled, Is.True);
            Assert.That(_bootstrap.ViewCamera.farClipPlane,
                Is.EqualTo(MapView.FarClipUnits),
                "the map camera needs the larger far clip");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TheMapSwitchGatesTheNeighbourhoodAidFade(bool enableMapView)
        {
            Build(enableMapView);

            if (!enableMapView)
            {
                Assert.That(_bootstrap.Renderer.LocalAidRadiusM, Is.Zero,
                    "the shipped scene keeps the old full-strength aid");
                return;
            }

            double moonSoiM = SphereOfInfluence.RadiusM(_bootstrap.Model.Get("moon"));
            Assert.That(_bootstrap.Renderer.LocalAidRadiusM,
                Is.EqualTo(moonSoiM).Within(1.0),
                "Gateway's neighbourhood is the Moon's SOI");
        }

        [Test]
        public void PressingGoToDrawsThePlanAndStartsTheFlight()
        {
            Build();

            _bootstrap.BeginGoTo(_bootstrap.Model.Get("mars"));

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_bootstrap.Flight.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.RequestedScaleIndex, Is.GreaterThan(0),
                "a four-day trip must not start at 1×");

            // One frame of the real frame order. Time.deltaTime is zero in edit
            // mode, so this checks the wiring rather than the propagation.
            Invoke("Update");
            Invoke("LateUpdate");

            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
        }

        [Test]
        public void CancelAndResetLeaveTheShipWhereTheDesignSays()
        {
            Build();
            _bootstrap.BeginGoTo(_bootstrap.Model.Get("mars"));

            _bootstrap.CancelFlight();
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);

            _bootstrap.ResetToGateway();
            _bootstrap.Model.ComputeState(_bootstrap.Model.Get("gateway"),
                _bootstrap.Clock.JulianDate, out Vector3d gateway, out _);
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, gateway), Is.LessThan(1e-6));
            Assert.That(_bootstrap.Clock.ScaleIndex, Is.EqualTo(0));
        }

        /// <summary>A flight owns the ship, so the debug jump must refuse.</summary>
        [Test]
        public void TheDebugJumpIsRefusedWhileFlying()
        {
            Build();
            _bootstrap.BeginGoTo(_bootstrap.Model.Get("mars"));
            Vector3d before = _bootstrap.ShipPositionM;

            _bootstrap.FocusSelected();

            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, before), Is.LessThan(1e-6));
            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
            Assert.That(_bootstrap.FreeFlight, Is.False);
        }

        [Test]
        public void PlottingDrawsThePreviewAndTheArrivalGhostWithoutFlying()
        {
            Build();
            Vector3d before = _bootstrap.ShipPositionM;
            double date = _bootstrap.Clock.JulianDate;

            _bootstrap.PlotRoute(_bootstrap.Model.Get("mars"));

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            Assert.That(_bootstrap.Flight.Preview.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True);
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, before), Is.Zero,
                "a preview must not move the ship");
            Assert.That(_bootstrap.Clock.JulianDate, Is.EqualTo(date));
            Assert.That(_bootstrap.RequestedScaleIndex, Is.EqualTo(0),
                "a preview must not raise warp");

            Invoke("LateUpdate");

            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Model.ComputeState(mars, _bootstrap.Flight.Preview.ArrivalJulianDate,
                out Vector3d marsAtArrival, out _);
            Vector3 expected = ((marsAtArrival - _bootstrap.ShipPositionM) *
                                _bootstrap.UnitsPerMeter).ToUnity();

            Assert.That(Vector3.Distance(_bootstrap.Trajectory.GhostMarkerPosition, expected),
                Is.LessThan(1e-3f), "the ghost marks where Mars will be on arrival");
        }

        /// <summary>
        /// Each arc is its own line, coloured by the type of path it carries.
        /// Consecutive arcs of one type take different shades, so the three
        /// quintics of the default plan and the three conics of the coasted
        /// plan stay individually readable, and the hybrid's fixed burn sits in
        /// red between two conic runs.
        /// </summary>
        [Test]
        public void TheRouteDrawsOneColouredLinePerArc()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.PlotRoute(mars);

            Assert.That(_bootstrap.Trajectory.ArcCount,
                Is.EqualTo(_bootstrap.Flight.Preview.Arcs.Count));
            Assert.That(_bootstrap.Trajectory.ArcColour(0),
                Is.EqualTo(RouteColours.Quintic));
            Assert.That(_bootstrap.Trajectory.ArcColour(1),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(0)),
                "the cruise is the second quintic in a row");
            Assert.That(_bootstrap.Trajectory.ArcLabel(0), Is.EqualTo("Quintic arc"));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(0), Is.False);

            _bootstrap.SetArrivalStyle(CaptureStyle.Conic);

            Assert.That(_bootstrap.Trajectory.ArcCount, Is.EqualTo(5),
                "departure, cruise, inbound coast, capture ellipse, parking");
            Assert.That(_bootstrap.Trajectory.ArcColour(2),
                Is.EqualTo(RouteColours.Conic));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(2), Is.True);
            Assert.That(_bootstrap.Trajectory.ArcLabel(2), Is.EqualTo("Conic arc"));
            Assert.That(_bootstrap.Trajectory.ArcColour(3),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(2)),
                "the transfer ellipse is the second conic in a row");
            Assert.That(_bootstrap.Trajectory.ArcColour(4),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(2)));
            Assert.That(_bootstrap.Trajectory.ArcColour(4),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(3)));

            _bootstrap.SetArrivalStyle(CaptureStyle.FiniteBurn);

            Assert.That(_bootstrap.Trajectory.ArcCount, Is.EqualTo(6),
                "the fixed burn splits the arrival into two conic runs");
            Assert.That(_bootstrap.Trajectory.ArcColour(3),
                Is.EqualTo(RouteColours.FixedBurn));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(3), Is.False,
                "a fixed-thrust burn is solid");
            Assert.That(_bootstrap.Trajectory.ArcLabel(3), Is.EqualTo("Fixed burn"));
            Assert.That(_bootstrap.Trajectory.ArcColour(4),
                Is.EqualTo(_bootstrap.Trajectory.ArcColour(2)),
                "the transfer coast restarts the conic run at the base shade");
            Assert.That(_bootstrap.Trajectory.ArcColour(5),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(4)),
                "the parking coast is the second conic in its run");
        }

        [Test]
        public void BeginningAPlottedRouteReplansAtTheCurrentDateAndFlies()
        {
            Build();
            _bootstrap.PlotRoute(_bootstrap.Model.Get("mars"));
            double later = _bootstrap.Clock.JulianDate + 0.05;
            _bootstrap.Clock.JulianDate = later;

            _bootstrap.BeginPlottedFlight();

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_bootstrap.Flight.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_bootstrap.Flight.Plan.DepartureJulianDate,
                Is.EqualTo(later).Within(1e-12));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True);
            Assert.That(_bootstrap.RequestedScaleIndex, Is.GreaterThan(0));
        }

        [Test]
        public void TheTripTimeSliderRetimesThePreviewThroughTheRealHud()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            SolarSystemHud hud = _root.GetComponent<SolarSystemHud>();
            Assert.That(hud, Is.Not.Null);
            hud.OpenDetails();
            _bootstrap.PlotRoute(mars);

            // The HUD builds its rows and the slider's range in its own Update,
            // which edit mode does not call for us.
            InvokeHud("Update");

            Slider slider = _root.GetComponentInChildren<Slider>(true);
            Assert.That(slider, Is.Not.Null, "the preview footer carries a trip-time slider");
            Assert.That(slider.gameObject.activeInHierarchy, Is.True);

            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            Assert.That(slider.minValue, Is.EqualTo((float)minimum).Within(1.0));
            Assert.That(slider.maxValue,
                Is.EqualTo((float)(minimum * TripPlanner.MaxDurationScale)).Within(10.0));

            Vector3d before = _bootstrap.ShipPositionM;
            float chosen = (float)(2.0 * minimum);
            slider.value = chosen;

            Assert.That(_bootstrap.Flight.IsRetimePending, Is.True,
                "the handle requests a retime; it does not block the frame");
            Assert.That(_bootstrap.Flight.PreviewDurationSeconds,
                Is.EqualTo(minimum).Within(1.0),
                "the previous preview stays published until the new plan is ready");

            WaitForRetime();
            Assert.That(_bootstrap.Flight.PreviewDurationSeconds,
                Is.EqualTo(chosen).Within(1.0),
                "moving the handle re-times the preview on the worker");
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, before), Is.Zero,
                "the slider never moves the ship");
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True,
                "the re-timed route is redrawn when the plan is published");
        }

        [Test]
        public void TheClockRunsWhileIdleSoAPlottedPreviewCanAge()
        {
            Build();
            _bootstrap.PlotRoute(_bootstrap.Model.Get("mars"));
            double staleDate = _bootstrap.Clock.JulianDate + 0.08;

            _bootstrap.Clock.JulianDate = staleDate;
            Invoke("Update");

            Assert.That(_bootstrap.Clock.JulianDate, Is.GreaterThanOrEqualTo(staleDate),
                "with no active flight the clock must not be clamped back to the ship");
            Assert.That(_bootstrap.Flight.IsPreviewStale(_bootstrap.Clock.JulianDate), Is.True);
            Assert.That(_bootstrap.Flight.HasPreview, Is.True, "ageing does not discard the preview");
        }

        [Test]
        public void DiscardingAPreviewLeavesTheShipAndItsFlightAlone()
        {
            Build();
            _bootstrap.PlotRoute(_bootstrap.Model.Get("mars"));

            _bootstrap.DiscardPreview();

            Assert.That(_bootstrap.Flight.HasPreview, Is.False);
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);
        }

        /// <summary>
        /// Step 6 of the system map: one wheel gesture from the neighbourhood to
        /// the whole system and back, through the real bootstrap. The pose is
        /// blended, not saved and restored, so the remembered angle must come
        /// back exactly.
        /// </summary>
        [Test]
        public void ZoomingOutToTheMapAndBackRestoresTheExactAngleThroughTheBootstrap()
        {
            Build(enableMapView: true);
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;

            // The wheel takes the camera, so the opening auto-framing stops,
            // exactly as a real notch clears AutoDistance in the rig's input.
            _bootstrap.Rig.ClearAuto();
            _bootstrap.Rig.Orbit.YawDegrees = 123.5f;
            _bootstrap.Rig.Orbit.PitchDegrees = -22.25f;
            _bootstrap.Rig.Orbit.DistanceUnits =
                (float)(0.5 * MapView.LocalRadiusM(_bootstrap.Model, _bootstrap.ShipPositionM)
                        * _bootstrap.UnitsPerMeter);
            Invoke("LateUpdate");

            float yaw = _bootstrap.Rig.Orbit.YawDegrees;
            float pitch = _bootstrap.Rig.Orbit.PitchDegrees;
            Quaternion localRotation = _bootstrap.Rig.PoseRotation;
            Assert.That(_bootstrap.Rig.MapBlendWeight, Is.EqualTo(0f).Within(1e-6f));

            // One gesture is many notches, each routed by the blend at the time
            // it arrives, which is what the real wheel does over the band.
            for (int i = 0; i < MapNotches; i++) ScrollOnce(1f);

            Assert.That(_bootstrap.Rig.IsMapSide, Is.True, "the gesture reaches the map");
            Assert.That(_bootstrap.Rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                Is.EqualTo(MapView.MaxDistanceUnits), "the map ceiling");
            Assert.That(Vector3.Angle(_bootstrap.Rig.PoseRotation * Vector3.forward, Vector3.down),
                Is.LessThan(1e-3f), "the map looks straight down the pole");
            Assert.That(_bootstrap.ViewCamera.farClipPlane, Is.EqualTo(MapView.FarClipUnits));

            for (int i = 0; i < MapNotches; i++) ScrollOnce(-1f);

            Assert.That(_bootstrap.Rig.MapBlendWeight, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(_bootstrap.Rig.Orbit.YawDegrees, Is.EqualTo(yaw));
            Assert.That(_bootstrap.Rig.Orbit.PitchDegrees, Is.EqualTo(pitch));
            Assert.That(Quaternion.Angle(_bootstrap.Rig.PoseRotation, localRotation),
                Is.LessThan(1e-3f), "the exact remembered angle is back");
            Assert.That(_bootstrap.Rig.LookAtUnits.magnitude, Is.LessThan(1e-6f),
                "the local view looks at the ship again");
        }

        /// <summary>
        /// The burn–coast–burn profile end to end through the composition root:
        /// choose the profile before plotting (so the plot is the profile's own
        /// fastest plan), fly the plotted route, and arrive on the same conic
        /// parking orbit the planner promised. This is the smoke acceptance for
        /// the new profile, not a second planner test.
        /// </summary>
        [Test]
        public void ABurnCoastBurnFlightArrivesThroughTheBootstrap()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.SetCruiseProfile(CruiseStyle.BurnCoastBurn);
            _bootstrap.PlotRoute(mars);

            Assert.That(_bootstrap.CruiseProfile, Is.EqualTo(CruiseStyle.BurnCoastBurn));
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            TrajectoryPlan preview = _bootstrap.Flight.Preview;
            Assert.That(preview.Arcs.Any(arc => arc.IsFiniteBurn), Is.True,
                "the preview carries the departure burn and the brake");

            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            Assert.That(minimum / JulianDate.SecondsPerDay, Is.LessThan(9.0),
                "the profile's true floor is below the old 9.64 d first-feasible plan");
            Assert.That(minimum / JulianDate.SecondsPerDay, Is.GreaterThan(7.0),
                "the floor is a real plan, not the profile being flown faster than it can");
            Assert.That(_bootstrap.Flight.PreviewMaximumSeconds,
                Is.LessThanOrEqualTo(minimum * TripPlanner.MaxDurationScale + 1e-6));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);

            _bootstrap.BeginPlottedFlight();

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_bootstrap.Flight.HasPreview, Is.False, "Begin consumes the preview");
            TrajectoryPlan plan = _bootstrap.Flight.Plan;
            Assert.That(plan.TargetId, Is.EqualTo("mars"));
            Assert.That(plan.Arcs.Count(arc => arc.IsFiniteBurn), Is.GreaterThanOrEqualTo(2),
                "a departure burn and a brake at minimum");
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.RequestedScaleIndex, Is.GreaterThan(0),
                "a two-month trip must not start at 1x");

            int frames = 0;
            while (_bootstrap.Flight.IsFlying && frames < MaxFrames)
            {
                Invoke("Update");
                Invoke("LateUpdate");
                frames++;
            }

            TestContext.WriteLine(
                $"bootstrap burn-coast-burn flight: arrived " +
                $"{JulianDate.Format(_bootstrap.Flight.ArrivedJulianDate)} in " +
                $"{frames:N0} frames ({frames * FrameSeconds:F1} s real), " +
                $"warp {SolarSystemBootstrap.ScaleLabel(_bootstrap.RequestedScaleIndex)}, " +
                $"{plan.FlightTimeDays:F1} d planned");

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Arrived),
                "the flight must finish through the frame loop, not a teleport");
            Assert.That(frames, Is.LessThan(MaxFrames), "the loop is bounded");
            Assert.That(_bootstrap.Clock.JulianDate,
                Is.EqualTo(_bootstrap.Flight.State.JulianDate).Within(1e-9),
                "the world never runs ahead of the ship");
            Assert.That(_bootstrap.Flight.ArrivedJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);

            double positionError = Vector3d.Distance(_bootstrap.Flight.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (_bootstrap.Flight.State.VelocityMps
                - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"arrival error {positionError:F1} m, " +
                                  $"{velocityError:F4} m/s");

            Assert.That(positionError, Is.LessThan(50_000.0), "matched within 50 km");
            Assert.That(velocityError, Is.LessThan(20.0), "matched within 20 m/s");

            _bootstrap.Model.ComputeState(mars, _bootstrap.Flight.State.JulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            Vector3d relativePosition = _bootstrap.Flight.State.PositionM - marsPosition;
            Vector3d relativeVelocity = _bootstrap.Flight.State.VelocityMps - marsVelocity;
            double radius = ParkingOrbit.ConicRadiusM(mars);
            double eccentricity = TwoBody.Eccentricity(relativePosition, relativeVelocity,
                mars.Gm);

            TestContext.WriteLine($"parking orbit r={relativePosition.Magnitude / 1000.0:F0} km " +
                                  $"(target {radius / 1000.0:F0} km), e={eccentricity:E3}");

            Assert.That(Math.Abs(relativePosition.Magnitude - radius) / radius,
                Is.LessThan(0.02), "the ship is on the conic parking orbit");
            Assert.That(eccentricity, Is.LessThan(0.03), "and the orbit is near-circular");
            Assert.That(_bootstrap.Flight.CurrentThrust.IsActive, Is.False, "the drive is off");
        }

        /// <summary>
        /// Step 6's frame-loop acceptance: map, select Mars, plot, Begin, and
        /// arrive — all through the real composition root. The existing
        /// Gateway→Mars numbers must hold, so this is the scene-level version of
        /// <c>SceneFlightTests</c>.
        /// </summary>
        [Test]
        public void PlottingAndBeginningMarsFromTheMapArrivesOnTheParkingOrbit()
        {
            Build(enableMapView: true);
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;
            _bootstrap.Rig.ClearAuto();
            for (int i = 0; i < MapNotches; i++) ScrollOnce(1f);
            Assert.That(_bootstrap.Rig.IsMapSide, Is.True, "the mission starts on the map");

            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            Assert.That(_bootstrap.Selected, Is.SameAs(mars));

            _bootstrap.PlotRoute(mars);
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            Assert.That(_bootstrap.Flight.Preview.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True);
            Assert.That(_bootstrap.Rig.IsMapSide, Is.True,
                "plotting frames the target without yanking the map back");

            _bootstrap.BeginPlottedFlight();
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_bootstrap.Flight.HasPreview, Is.False, "Begin consumes the preview");
            Assert.That(_bootstrap.Flight.Plan.TargetId, Is.EqualTo("mars"));

            TrajectoryPlan plan = _bootstrap.Flight.Plan;
            int frames = 0;
            while (_bootstrap.Flight.IsFlying && frames < MaxFrames)
            {
                Invoke("Update");
                Invoke("LateUpdate");
                frames++;
            }

            TestContext.WriteLine(
                $"bootstrap map flight: arrived {JulianDate.Format(_bootstrap.Flight.ArrivedJulianDate)} " +
                $"in {frames:N0} frames ({frames * FrameSeconds:F1} s real), " +
                $"warp {SolarSystemBootstrap.ScaleLabel(_bootstrap.RequestedScaleIndex)}");

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Arrived),
                "the flight must finish through the frame loop, not a teleport");
            Assert.That(frames, Is.LessThan(MaxFrames), "the loop is bounded");
            Assert.That(_bootstrap.Clock.JulianDate,
                Is.EqualTo(_bootstrap.Flight.State.JulianDate).Within(1e-9),
                "the world never runs ahead of the ship");
            Assert.That(_bootstrap.Flight.ArrivedJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Rig.IsMapSide, Is.True,
                "the map stays usable for the whole flight");

            double positionError = Vector3d.Distance(_bootstrap.Flight.State.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (_bootstrap.Flight.State.VelocityMps
                - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"arrival error {positionError:F1} m, {velocityError:F4} m/s");

            Assert.That(positionError, Is.LessThan(10_000.0), "matched within 10 km");
            Assert.That(velocityError, Is.LessThan(10.0), "matched within 10 m/s");

            BodyRuntime marsAtArrival = _bootstrap.Model.Get("mars");
            _bootstrap.Model.ComputeState(marsAtArrival, _bootstrap.Flight.State.JulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            Vector3d relativePosition = _bootstrap.Flight.State.PositionM - marsPosition;
            Vector3d relativeVelocity = _bootstrap.Flight.State.VelocityMps - marsVelocity;
            double radius = ParkingOrbit.RadiusM(marsAtArrival, plan.MaxAccelerationMps2);
            double eccentricity = TwoBody.Eccentricity(relativePosition, relativeVelocity,
                marsAtArrival.Gm);

            TestContext.WriteLine($"parking orbit r={relativePosition.Magnitude / 1000.0:F0} km " +
                                  $"(target {radius / 1000.0:F0} km), e={eccentricity:E3}");

            Assert.That(Math.Abs(relativePosition.Magnitude - radius) / radius,
                Is.LessThan(0.02), "the ship is on the parking orbit");
            Assert.That(eccentricity, Is.LessThan(0.03), "and the orbit is near-circular");
            Assert.That(_bootstrap.Flight.CurrentThrust.IsActive, Is.False, "the drive is off");
        }

        /// <summary>
        /// The zoom is a request plus an ease, not a teleport: one gesture moves
        /// the camera over many frames, and the map blend sweeps through its
        /// band instead of snapping to the top-down pose.
        /// </summary>
        [Test]
        public void OneWheelGestureEasesToTheMapInsteadOfSnapping()
        {
            Build(enableMapView: true);
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;
            _bootstrap.Rig.ClearAuto();
            _bootstrap.Rig.Orbit.YawDegrees = 40f;
            _bootstrap.Rig.Orbit.PitchDegrees = 10f;

            double radiusM = MapView.LocalRadiusM(_bootstrap.Model, _bootstrap.ShipPositionM);
            float localDistance = (float)(radiusM * _bootstrap.UnitsPerMeter);
            _bootstrap.Rig.Orbit.DistanceUnits = localDistance;
            Invoke("LateUpdate");
            Assert.That(_bootstrap.Rig.MapBlendWeight, Is.EqualTo(0f).Within(1e-6f));

            _bootstrap.Rig.ApplyWheelSteps(6f);
            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits, Is.EqualTo(localDistance),
                "a wheel gesture requests a distance; the camera has not moved");

            int frames = 0;
            bool sawIntermediateBlend = false;
            float previousDistance = localDistance;

            while (!_bootstrap.Rig.Orbit.IsDistanceSettled && frames < 600)
            {
                Invoke("LateUpdate");

                Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                    Is.GreaterThanOrEqualTo(previousDistance),
                    "the zoom eases outward through the frame loop");

                float weight = _bootstrap.Rig.MapBlendWeight;
                if (weight > 0.01f && weight < 0.99f) sawIntermediateBlend = true;

                previousDistance = _bootstrap.Rig.Orbit.DistanceUnits;
                frames++;
            }

            Assert.That(frames, Is.GreaterThan(1), "the zoom is a tween, not a snap");
            Assert.That(frames, Is.LessThan(600));
            Assert.That(sawIntermediateBlend, Is.True, "the map blend sweeps through its band");
            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                Is.EqualTo(_bootstrap.Rig.Orbit.TargetDistanceUnits));
            Assert.That(_bootstrap.Rig.MapBlendWeight, Is.EqualTo(1f).Within(1e-4f));
        }

        private const float FrameSeconds = 1f / 60f;
        private const int MapNotches = 90;
        private const int MaxFrames = 20_000;

        /// <summary>One wheel notch through the rig's real routing, then the
        /// camera frame that applies it.</summary>
        private void ScrollOnce(float steps)
        {
            ShipCameraRig rig = _bootstrap.Rig;
            rig.Orbit.DistanceUnits = rig.WheelDistanceUnits(
                rig.Orbit.DistanceUnits, steps, rig.MapBlendWeight);
            Invoke("LateUpdate");
        }

        private void Build(bool enableMapView = false)
        {
            _cameraObject = new GameObject("Test Camera");
            _cameraObject.tag = "MainCamera";
            _cameraObject.AddComponent<Camera>();

            _root = new GameObject("Test Bootstrap");
            _bootstrap = _root.AddComponent<SolarSystemBootstrap>();
            _bootstrap.EnableMapView = enableMapView;
            Invoke("Awake");
        }

        private void Invoke(string method)
        {
            MethodInfo info = typeof(SolarSystemBootstrap).GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, $"SolarSystemBootstrap.{method} is missing");
            info.Invoke(_bootstrap, null);
        }

        private void InvokeHud(string method)
        {
            SolarSystemHud hud = _root.GetComponent<SolarSystemHud>();
            Assert.That(hud, Is.Not.Null, "the HUD is part of the bootstrap");
            MethodInfo info = typeof(SolarSystemHud).GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, $"SolarSystemHud.{method} is missing");
            info.Invoke(hud, null);
        }

        /// <summary>Pump the background retime until the session publishes it,
        /// with a deadline so a broken worker fails instead of hanging.</summary>
        private void WaitForRetime()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (_bootstrap.Flight.IsRetimePending)
            {
                _bootstrap.ApplyRetimeIfReady();
                if (!_bootstrap.Flight.IsRetimePending) break;

                if (watch.Elapsed.TotalSeconds > 20.0)
                    Assert.Fail("the background retime was never published");
                System.Threading.Thread.Sleep(1);
            }
        }
    }
}
