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
using UnityEngine.Rendering.Universal;
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
            Assert.That(_bootstrap.ViewCamera.eventMask, Is.Zero,
                "the view camera must opt out of the legacy OnMouse* dispatcher, " +
                "which otherwise warns every frame with Input System-only input");
            Assert.That(_bootstrap.ViewCamera.cullingMask, Is.Not.Zero,
                "the opt-out is the mouse event mask, not rendering");
            Assert.That(
                _bootstrap.ViewCamera.GetUniversalAdditionalCameraData()
                    .renderPostProcessing,
                Is.True,
                "a bare camera defaults to post-processing off, so the bloom " +
                "would silently never run on the runtime camera");

            _bootstrap.Model.ComputeState(_bootstrap.Model.Get("gateway"),
                _bootstrap.Clock.JulianDate, out Vector3d gateway, out _);
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, gateway), Is.LessThan(1e-6),
                "the ship must start at Gateway");
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);
            Assert.That(_bootstrap.Selected?.Id, Is.EqualTo("earth"));
            Assert.That(_bootstrap.Rig.AutoTarget?.Id, Is.EqualTo("earth"),
                "the opening shot frames Earth");
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
            WaitForPreview();

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            Assert.That(_bootstrap.Flight.Preview.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True);
            Assert.That(_bootstrap.Rig.IsFramingRoute, Is.True,
                "plotting eases the camera to the whole route, not just the target");
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
        /// A station is a Go To target with no gravitational parameter of its
        /// own. From a ship parked at Ares Junction (outside every sphere) the
        /// route to Gateway must plot, draw every arc and the arrival ghost, and
        /// then begin as a flight — not silently do nothing at the
        /// <c>Gm &gt; 0</c> guard.
        /// </summary>
        [Test]
        public void AStationCanBePlottedAndBegunFromOutsideEverySphere()
        {
            Build();
            _bootstrap.Flight.PlaceAt("ares_junction", _bootstrap.Clock.JulianDate);
            BodyRuntime gateway = _bootstrap.Model.Get("gateway");

            _bootstrap.PlotRoute(gateway);
            WaitForPreview();

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            Assert.That(_bootstrap.Flight.Preview.TargetId, Is.EqualTo("gateway"));
            Assert.That(_bootstrap.Flight.Preview.IsFeasible, Is.True,
                _bootstrap.Flight.Preview.InfeasibleReason);
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.ArcCount,
                Is.EqualTo(_bootstrap.Flight.Preview.Arcs.Count));
            Assert.That(_bootstrap.Trajectory.ArcCount, Is.GreaterThan(0));
            for (int i = 0; i < _bootstrap.Trajectory.ArcCount; i++)
                Assert.That(_bootstrap.Trajectory.ArcLabel(i), Is.Not.Null,
                    $"arc {i} is drawn with a legend label");
            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True,
                "the arrival ghost still marks the target at the arrival date");

            // The ghost is placed during the frame's LateUpdate, as in the
            // planet plot test.
            Invoke("LateUpdate");
            _bootstrap.Model.ComputeState(gateway,
                _bootstrap.Flight.Preview.ArrivalJulianDate,
                out Vector3d gatewayAtArrival, out _);
            Vector3 expected = ((gatewayAtArrival - _bootstrap.ShipPositionM) *
                                _bootstrap.UnitsPerMeter).ToUnity();
            Assert.That(Vector3.Distance(_bootstrap.Trajectory.GhostMarkerPosition, expected),
                Is.LessThan(1e-3f), "the ghost marks where the station will be");

            _bootstrap.BeginGoTo(gateway);

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying),
                _bootstrap.Flight.Failure);
            Assert.That(_bootstrap.Flight.TargetId, Is.EqualTo("gateway"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.RequestedScaleIndex, Is.GreaterThan(0),
                "a fifty-day station trip must not start at 1x");

            Invoke("Update");
            Invoke("LateUpdate");
            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
        }

        [Test]
        public void TheRouteDrawsOneColouredLinePerArc()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.PlotRoute(mars);
            WaitForPreview();

            Assert.That(_bootstrap.Trajectory.ArcCount,
                Is.EqualTo(_bootstrap.Flight.Preview.Arcs.Count));
            Assert.That(_bootstrap.Trajectory.ArcColour(0),
                Is.EqualTo(RouteColours.Quintic));
            Assert.That(_bootstrap.Trajectory.ArcLabel(0), Is.EqualTo("Quintic arc"));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(0), Is.False);

            Assert.That(_bootstrap.Trajectory.ArcColour(1),
                Is.EqualTo(RouteColours.Conic));
            Assert.That(_bootstrap.Trajectory.ArcLabel(1), Is.EqualTo("Conic arc"));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(1), Is.True,
                "the escape conic is dashed");

            Assert.That(_bootstrap.Trajectory.ArcLabel(2), Is.EqualTo("Fixed burn"));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(2), Is.False,
                "the departure burn is solid");
            Assert.That(_bootstrap.Trajectory.ArcLabel(3), Is.EqualTo("Fixed burn"));
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(3), Is.False);
            Assert.That(_bootstrap.Trajectory.ArcColour(3),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(2)),
                "two fixed burns back to back take two shades");
            Assert.That(_bootstrap.Trajectory.ArcColour(4),
                Is.EqualTo(RouteColours.Conic),
                "the brake ends the fixed run and the capture chain starts");
            Assert.That(_bootstrap.Trajectory.ArcIsDashed(4), Is.True);
            Assert.That(_bootstrap.Trajectory.ArcColour(5),
                Is.Not.EqualTo(_bootstrap.Trajectory.ArcColour(4)),
                "the transfer is the second conic in the run");

            TrajectoryPlan conic = _bootstrap.Flight.Preview;
            _bootstrap.SetArrivalStyle(CaptureStyle.FiniteBurn);
            WaitForRebuild(conic);

            Assert.That(_bootstrap.ArrivalStyle, Is.EqualTo(CaptureStyle.FiniteBurn));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Count, Is.EqualTo(7),
                "the zero-coast minimum keeps its conic capture chain");
            Assert.That(_bootstrap.Flight.Preview.Arcs.Count(arc => arc.IsFiniteBurn),
                Is.EqualTo(2),
                "departure burn and brake");

            // Above the floor the arrival style is visible: the hybrid capture
            // adds its finite capture burn.
            TrajectoryPlan coasting = _bootstrap.Flight.SetPreviewDuration(
                2.0 * _bootstrap.Flight.PreviewMinimumSeconds);
            Assert.That(coasting.Arcs.Count, Is.EqualTo(9),
                "the hybrid capture adds its finite capture burn");
            Assert.That(coasting.Arcs.Count(arc => arc.IsFiniteBurn), Is.EqualTo(3),
                "departure burn, brake, and the hybrid capture burn");
        }

        [Test]
        public void EveryDashedArcUsesTheSameWorldDashHoweverLongItIs()
        {
            Build();
            _bootstrap.PlotRoute(_bootstrap.Model.Get("mars"));
            WaitForPreview();
            Invoke("LateUpdate");

            float period = 0f;
            int dashed = 0;
            for (int i = 0; i < _bootstrap.Trajectory.ArcCount; i++)
            {
                if (!_bootstrap.Trajectory.ArcIsDashed(i)) continue;

                float arcPeriod = _bootstrap.Trajectory.ArcDashPeriodUnits(i);
                Assert.That(arcPeriod, Is.GreaterThan(0f),
                    $"dashed arc {i} has no dash period");
                if (dashed == 0) period = arcPeriod;
                else
                    Assert.That(arcPeriod, Is.EqualTo(period).Within(1e-9f),
                        $"arc {i} has a different dash period, so a longer arc " +
                        "would draw a denser dash instead of the same dotted line");

                dashed++;
            }

            Assert.That(dashed, Is.GreaterThanOrEqualTo(2),
                "the conic plan has several dashed arcs of different lengths");
        }

        [Test]
        public void TheDashPeriodIsEightLineWidths()
        {
            Assert.That(TrajectoryLine.DashPeriodUnits(2f), Is.EqualTo(16f).Within(1e-6f));
            Assert.That(TrajectoryLine.DashPeriodUnits(0f), Is.GreaterThan(0f),
                "a zero detail must not divide by zero in the texture scale");
        }

        [Test]
        public void BeginningAPlottedRouteReplansAtTheCurrentDateAndFlies()
        {
            Build();
            _bootstrap.PlotRoute(_bootstrap.Model.Get("mars"));
            WaitForPreview();
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
            WaitForPreview();

            // The HUD builds its rows and the slider's range in its own Update,
            // which edit mode does not call for us.
            InvokeHud("Update");

            Slider slider = _root.GetComponentInChildren<Slider>(true);
            Assert.That(slider, Is.Not.Null, "the preview footer carries a trip-time slider");
            Assert.That(slider.gameObject.activeInHierarchy, Is.True);

            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            double maximum = _bootstrap.Flight.PreviewMaximumSeconds;
            Assert.That(slider.wholeNumbers, Is.True);
            Assert.That(slider.minValue, Is.EqualTo(0f).Within(1e-6));
            Assert.That(slider.maxValue,
                Is.EqualTo(SolarSystemHud.TimeSliderSteps - 1f).Within(1e-6),
                "the handle offers 64 distinct trips");

            Vector3d before = _bootstrap.ShipPositionM;
            float step = (float)System.Math.Round(
                (2.0 * minimum - minimum) / (maximum - minimum) * slider.maxValue);
            double chosen = minimum + step / slider.maxValue * (maximum - minimum);
            slider.value = step;

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
            WaitForPreview();
            double staleDate = _bootstrap.Clock.JulianDate
                               + _bootstrap.Flight.PreviewStaleAfterDays + 0.01;

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
            WaitForPreview();

            _bootstrap.DiscardPreview();

            Assert.That(_bootstrap.Flight.HasPreview, Is.False);
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);
        }

        /// <summary>
        /// One wheel gesture from the neighbourhood to the 12-billion-km
        /// ceiling and back, through the real bootstrap. The zoom never writes
        /// the player's yaw or pitch, so the remembered angle comes back
        /// exactly and the pose never tilts.
        /// </summary>
        [Test]
        public void ZoomingToTheOuterCeilingAndBackRestoresTheExactAngleThroughTheBootstrap()
        {
            Build();
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;

            // The wheel takes the camera, so the opening auto-framing stops,
            // exactly as a real notch clears AutoDistance in the rig's input.
            _bootstrap.Rig.ClearAuto();
            _bootstrap.Rig.Orbit.YawDegrees = 123.5f;
            _bootstrap.Rig.Orbit.PitchDegrees = -22.25f;
            _bootstrap.Rig.Orbit.DistanceUnits =
                (float)(0.5 * LocalScale.RadiusM(_bootstrap.Model, _bootstrap.ShipPositionM)
                        * _bootstrap.UnitsPerMeter);
            Invoke("LateUpdate");

            float yaw = _bootstrap.Rig.Orbit.YawDegrees;
            float pitch = _bootstrap.Rig.Orbit.PitchDegrees;
            Quaternion localRotation = _bootstrap.Rig.PoseRotation;

            RunZoom(ZoomNotches);

            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                Is.EqualTo(OrbitCamera.MaxDistanceUnits), "the 80 AU ceiling");
            Assert.That(Vector3.Angle(_bootstrap.Rig.PoseRotation * Vector3.forward,
                OrbitCamera.Direction(yaw, pitch) * -1f),
                Is.LessThan(1e-2f), "the far view keeps the ordinary orbit pose");
            Assert.That(_bootstrap.ViewCamera.farClipPlane,
                Is.EqualTo(OrbitCamera.FarClipUnits));

            RunZoom(-ZoomNotches);

            Assert.That(_bootstrap.Rig.Orbit.YawDegrees, Is.EqualTo(yaw));
            Assert.That(_bootstrap.Rig.Orbit.PitchDegrees, Is.EqualTo(pitch));
            Assert.That(Quaternion.Angle(_bootstrap.Rig.PoseRotation, localRotation),
                Is.LessThan(1e-3f), "the exact remembered angle is back");
            Assert.That(_bootstrap.Rig.LookAtUnits.magnitude, Is.LessThan(1e-6f),
                "a centre zoom looks at the ship again");
        }

        /// <summary>
        /// The burn–coast–burn profile end to end through the composition root:
        /// choose the profile before plotting (so the plot is the profile's own
        /// fastest plan), fly the plotted route, and arrive on the same conic
        /// parking orbit the planner promised. This is the smoke acceptance for
        /// the new profile, not a second planner test.
        /// </summary>
        [Test]
        public void AProfileFlightArrivesThroughTheBootstrap()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();

            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            TrajectoryPlan preview = _bootstrap.Flight.Preview;
            Assert.That(preview.Arcs.Any(arc => arc.IsFiniteBurn), Is.True,
                "the preview carries the departure burn and the brake");

            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            Assert.That(minimum / JulianDate.SecondsPerDay, Is.LessThan(17.5),
                "the window starts at the nav computer's burn-only minimum");
            Assert.That(minimum / JulianDate.SecondsPerDay, Is.GreaterThan(16.0),
                "the minimum is a real plan, not the profile being flown faster than it can");
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
        /// The frame-loop acceptance: select Mars, plot, Begin, and arrive — all
        /// through the real composition root. The existing Gateway→Mars numbers
        /// must hold, so this is the scene-level version of <c>SceneFlightTests</c>.
        /// </summary>
        [Test]
        public void PlottingAndBeginningMarsArrivesOnTheParkingOrbit()
        {
            Build();
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;

            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            Assert.That(_bootstrap.Selected, Is.SameAs(mars));

            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            Assert.That(_bootstrap.Flight.Preview.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True);

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
            Assert.That(_bootstrap.Rig.AutoDistanceTargetUnits,
                Is.EqualTo(_bootstrap.Rig.AutoDistanceUnits(
                    _bootstrap.ShipPositionM, marsAtArrival)).Within(1.0),
                "arrival re-captures the framing distance on the parking orbit");
        }

        /// <summary>
        /// The zoom is a request plus an ease, not a teleport: one gesture moves
        /// the camera over many frames.
        /// </summary>
        [Test]
        public void OneWheelGestureEasesInsteadOfSnapping()
        {
            Build();
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;
            _bootstrap.Rig.ClearAuto();
            _bootstrap.Rig.Orbit.YawDegrees = 40f;
            _bootstrap.Rig.Orbit.PitchDegrees = 10f;
            _bootstrap.Rig.Orbit.DistanceUnits = 0.185f;
            Invoke("LateUpdate");

            _bootstrap.Rig.ApplyWheelSteps(6f);
            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits, Is.EqualTo(0.185f),
                "a wheel gesture requests a distance; the camera has not moved");

            int frames = 0;
            float previousDistance = 0.185f;

            while (!_bootstrap.Rig.Orbit.IsDistanceSettled && frames < 600)
            {
                Invoke("LateUpdate");

                Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                    Is.GreaterThanOrEqualTo(previousDistance),
                    "the zoom eases outward through the frame loop");

                previousDistance = _bootstrap.Rig.Orbit.DistanceUnits;
                frames++;
            }

            Assert.That(frames, Is.GreaterThan(1), "the zoom is a tween, not a snap");
            Assert.That(frames, Is.LessThan(600));
            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                Is.EqualTo(_bootstrap.Rig.Orbit.TargetDistanceUnits));
        }

        private const float FrameSeconds = 1f / 60f;
        private const int ZoomNotches = 90;
        private const int MaxFrames = 20_000;

        /// <summary>One continuous wheel gesture through the frame loop, until
        /// the eased distance settles.</summary>
        private void RunZoom(float notches)
        {
            _bootstrap.Rig.ApplyWheelSteps(notches);
            int frames = 0;
            while (!_bootstrap.Rig.Orbit.IsDistanceSettled && frames < 600)
            {
                Invoke("LateUpdate");
                frames++;
            }

            Assert.That(frames, Is.LessThan(600), "the zoom must settle");
        }

        private void Build()
        {
            _cameraObject = new GameObject("Test Camera");
            _cameraObject.tag = "MainCamera";
            _cameraObject.AddComponent<Camera>();

            _root = new GameObject("Test Bootstrap");
            _bootstrap = _root.AddComponent<SolarSystemBootstrap>();
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

        /// <summary>Pump the background planner until the route preview is
        /// published, with a deadline so a broken worker fails instead of
        /// hanging. Publication redraws the route exactly as the HUD's
        /// per-frame pump does, so the route's assertions see the plan.</summary>
        private void WaitForPreview()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!_bootstrap.Flight.HasPreview)
            {
                _bootstrap.ApplyPreviewIfReady();
                if (watch.Elapsed.TotalSeconds > 20.0)
                    Assert.Fail("the background preview was never published");
                System.Threading.Thread.Sleep(1);
            }
            _bootstrap.ApplyPreviewIfReady();
        }

        /// <summary>Pump the background planner until a rebuild publishes over
        /// <paramref name="previous"/>, redrawing the route, with a deadline.
        /// The published plan is a different instance than the one the style
        /// change superseded.</summary>
        private void WaitForRebuild(TrajectoryPlan previous)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (_bootstrap.Flight.Preview == previous)
            {
                _bootstrap.ApplyPreviewIfReady();
                if (watch.Elapsed.TotalSeconds > 20.0)
                    Assert.Fail("the background rebuild was never published");
                System.Threading.Thread.Sleep(1);
            }
            _bootstrap.ApplyPreviewIfReady();
        }
    }
}
