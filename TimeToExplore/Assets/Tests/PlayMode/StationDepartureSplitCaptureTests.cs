using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Simulation;
using TimeToExplore.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Evidence for the station departure split, not a gate. The test docks at
    /// Gateway at the epoch, plots Gateway → Ares Junction through the real
    /// details panel and retimes it to the top of the preview window — the
    /// 3× cap, 21.31717 days — through the HUD's own retime entry point, then
    /// writes a screenshot of the trajectory line and the HUD.
    ///
    /// The window is the evidence. The split moved the station coast floor from
    /// 16.75780 d / 1,033.44 km/s (window 16.76–50.27 d) to 7.10572 d /
    /// 1,073.66 km/s (window 7.11–21.32 d). The fixed durations at 60 days and
    /// above are direct `PlanAtDuration` builds, above the 3× cap, and were
    /// never slider positions; the cap is just lower now.
    ///
    /// It carries only the <c>capture</c> category so the default
    /// <c>tools/check.sh</c> run, which is headless, never starts it. Run it by
    /// hand without <c>-nographics</c>:
    /// <code>
    /// Unity -batchmode -projectPath &lt;test project&gt; -runTests \
    ///   -testPlatform PlayMode -testCategory capture -testResults ...
    /// </code>
    /// </summary>
    [Category("capture")]
    public class StationDepartureSplitCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const double SecondsPerDay = JulianDate.SecondsPerDay;
        private const double KmPerSecond = 1_000.0;

        // Measured at the epoch through the HUD: the split-aware floor, the
        // 3× cap and the plan the window top builds.
        private const double ExpectedFloorDays = 7.10572;
        private const double ExpectedWindowTopDays = 21.31717;
        private const double ExpectedVInfinityKms = 8.0;
        private const double ExpectedTotalKms = 226.7126;

        [UnityTest]
        public IEnumerator TheStationSplitShowsItsWindowTopAtTwentyOneDays()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindAnyObjectByType<SolarSystemBootstrap>();
            Assert.That(bootstrap, Is.Not.Null,
                "RuntimeInitializeOnLoad must create the bootstrap in a bare play session");

            var hud = bootstrap.GetComponent<SolarSystemHud>();
            Assert.That(hud, Is.Not.Null, "the bootstrap must add the HUD");

            bootstrap.CancelFlight();
            bootstrap.DiscardPreview();
            bootstrap.ShowHud = true;
            bootstrap.ShowLabels = true;
            bootstrap.Clock.JulianDate = bootstrap.Model.Data.startJulianDate;
            bootstrap.SetScaleIndex(0);
            bootstrap.ResetToGateway();
            yield return null;

            // Plot Gateway → Ares Junction through the real details panel
            // button, so the preview, its framing and the HUD publish through
            // the player path.
            BodyRuntime ares = bootstrap.Model.Get("ares_junction");
            bootstrap.Select(ares);
            hud.OpenDetails();
            yield return null;
            yield return null;

            Button route = ButtonNamed(hud, "RouteButton");
            Assert.That(route.interactable, Is.True);
            route.onClick.Invoke();

            yield return WaitFor(
                () => bootstrap.Flight.HasPreview && bootstrap.Trajectory.IsShowing,
                "the HUD's frame pump must publish the plotted preview", 180f);
            yield return WaitFor(() => !bootstrap.Rig.IsFramingRoute,
                "plotting must ease the camera to the whole route");

            // The trip-time control appears with the preview. Its left end is
            // the split-aware coast floor, and its right end is exactly 3× it.
            Slider slider = SliderNamed(hud, "TimeSlider");
            Assert.That(slider.gameObject.activeInHierarchy, Is.True,
                "a preview offers the trip-time control");

            double minimum = bootstrap.Flight.PreviewMinimumSeconds;
            double maximum = bootstrap.Flight.PreviewMaximumSeconds;
            Assert.That(minimum / SecondsPerDay,
                Is.EqualTo(ExpectedFloorDays).Within(0.01),
                "the window minimum is the split-aware coast floor");
            Assert.That(maximum, Is.EqualTo(3.0 * minimum).Within(1.0),
                "the station window is exactly three times the split-aware floor");
            Assert.That(maximum / SecondsPerDay,
                Is.EqualTo(ExpectedWindowTopDays).Within(0.01),
                "the 3× cap lands on the measured 21.31717 days");

            // The handle has 64 whole steps; the far end is the window top.
            // Drive it there, then ask for the maximum exactly through the
            // same retime entry point the handle calls.
            slider.value = slider.maxValue;
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending,
                "the handle's retime must publish");

            bootstrap.RetimePreview(maximum);
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending
                      && bootstrap.Flight.Preview != null
                      && System.Math.Abs(bootstrap.Flight.Preview.FlightTimeSeconds
                                         - maximum) < 1.0,
                "the window-top retime must publish");

            TrajectoryPlan plan = bootstrap.Flight.Preview;
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.TargetId, Is.EqualTo("ares_junction"));
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FlightTimeDays,
                Is.EqualTo(ExpectedWindowTopDays).Within(0.01),
                "the retime lands on the window top");

            // A station climb is five arcs: the break, the escape conic, the
            // departure burn, the coast and the match. There is no approach,
            // parking or capture-chain arc.
            Assert.That(plan.Arcs.Count, Is.EqualTo(5));
            var phases = new List<FlightPhase>();
            foreach (PlanArc arc in plan.Arcs) phases.Add(arc.Phase);
            Assert.That(phases, Is.EqualTo(new[]
            {
                FlightPhase.Departure, FlightPhase.Escape, FlightPhase.Cruise,
                FlightPhase.Cruise, FlightPhase.Capture
            }), "the station plan is climb, cruise burn, coast, match");
            Assert.That(plan.ArcForPhase(FlightPhase.Approach), Is.Null,
                "a station has no planetocentric approach");
            Assert.That(plan.ArcForPhase(FlightPhase.Parking), Is.Null,
                "a station has no parking orbit");

            PlanArc escape = plan.ArcForPhase(FlightPhase.Escape);
            Assert.That(escape, Is.Not.Null,
                "a station with a departure body carries the escape conic");
            double escapeDays = escape.DurationSeconds / SecondsPerDay;

            // The match ends on the station's 500 m hold state.
            double arrivalJd = plan.ArrivalJulianDate;
            bootstrap.Model.ComputeState(ares, arrivalJd,
                out Vector3d stationPosition, out Vector3d stationVelocity);
            Vector3d hold = stationPosition
                            - stationVelocity.Normalized * TripPlanner.StationHoldDistanceM;
            PlanArc last = plan.Arcs[plan.Arcs.Count - 1];
            double holdPositionStep = Vector3d.Distance(last.Path.PositionAt(1.0), hold);
            double holdVelocityStep = (last.Path.VelocityAt(1.0) - stationVelocity).Magnitude;

            Debug.Log($"Station split capture: {plan.FlightTimeDays:F4} d, " +
                      $"v∞ {plan.DepartureVInfinityMps / KmPerSecond:F3} km/s, " +
                      $"escape {escapeDays:F4} d, " +
                      $"total {plan.DeltaVMps / KmPerSecond:F4} km/s, " +
                      $"propellant {plan.PropellantKg / 1_000.0:F1} t, " +
                      $"hold {holdPositionStep:E3} m / {holdVelocityStep:E3} m/s, " +
                      $"window {minimum / SecondsPerDay:F5}–" +
                      $"{maximum / SecondsPerDay:F5} d");

            Assert.That(plan.DepartureVInfinityMps,
                Is.EqualTo(ExpectedVInfinityKms * KmPerSecond).Within(0.01 * KmPerSecond),
                "the window top chooses the measured 8.0 km/s excess");
            Assert.That(plan.DeltaVMps / KmPerSecond,
                Is.EqualTo(ExpectedTotalKms).Within(0.5),
                "the window-top station split plan is the measured 226.7126 km/s total");
            Assert.That(escapeDays, Is.LessThan(10.74066),
                "the split climb is shorter than the old 10.74066-day parabolic climb");
            Assert.That(holdPositionStep, Is.LessThan(1.0),
                "the velocity match ends on the hold point");
            Assert.That(holdVelocityStep, Is.LessThan(1e-3),
                "the velocity match ends on the station's velocity");

            // The retime deliberately does not yank the camera, and the route
            // it framed was the floor preview. Frame the window-top plan the
            // same way the bootstrap publishes a fresh plot, so the whole line
            // and the arrival ghost are in shot behind the HUD.
            var points = new List<Vector3d>();
            plan.SamplePath(RouteFraming.SamplesPerArc, points);
            Assert.That(bootstrap.Rig.FrameRoute(points, bootstrap.ShipPositionM,
                stationPosition), Is.True, "the window-top route must be frameable");
            yield return WaitFor(() => !bootstrap.Rig.IsFramingRoute,
                "the reframed camera must settle");

            string path = Path.GetFullPath("station-departure-split.png");
            Debug.Log($"Station split capture: cwd {Directory.GetCurrentDirectory()}, " +
                      $"writing {path}");

            Screen.SetResolution(Width, Height, false);

            // Batch mode has no end-of-frame phase to wait on: the test runner
            // refuses WaitForEndOfFrame there, and an ordinary frame is enough.
            if (Application.isBatchMode)
                yield return null;
            else
                yield return new WaitForEndOfFrame();

            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;

            if (!File.Exists(path))
            {
                // Batch mode has no on-screen backbuffer, so the screen
                // capture writes nothing there. Render the same HUD canvas
                // through the game camera into a texture instead.
                Debug.Log("Station split capture: screen capture wrote nothing; " +
                          "rendering the canvas through the view camera");
                yield return CaptureThroughCamera(bootstrap.ViewCamera, hud, path);
            }

            Assert.That(File.Exists(path), Is.True, "the capture must write " + path);
        }

        /// <summary>
        /// Render the live HUD canvas with the game camera into a texture. The
        /// canvas is temporarily a screen-space camera canvas, which is how the
        /// overlay pass would composite it, so the panel pixels are the ones the
        /// player sees. Both the camera target and the canvas are restored.
        /// </summary>
        private static IEnumerator CaptureThroughCamera(Camera camera, SolarSystemHud hud,
            string path)
        {
            Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
            RenderMode renderMode = canvas.renderMode;
            Camera canvasCamera = canvas.worldCamera;
            float planeDistance = canvas.planeDistance;
            RenderTexture previousTarget = camera.targetTexture;

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            Texture2D image = null;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                camera.targetTexture = target;

                // Two real frames, so the canvas rebuilds for the camera and
                // the pipeline draws the world behind it.
                yield return null;
                yield return null;

                RenderTexture.active = target;
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = previousTarget;
                canvas.renderMode = renderMode;
                canvas.worldCamera = canvasCamera;
                canvas.planeDistance = planeDistance;
                if (image != null) Object.Destroy(image);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string message,
            float timeoutSeconds = 60f)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                    Assert.Fail(message);
                yield return null;
            }
        }

        private static Slider SliderNamed(SolarSystemHud hud, string name)
        {
            foreach (Slider slider in hud.GetComponentsInChildren<Slider>(true))
                if (slider.gameObject.name == name) return slider;

            Assert.Fail("no HUD Slider named " + name);
            return null;
        }

        private static Button ButtonNamed(SolarSystemHud hud, string name)
        {
            foreach (Button button in hud.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;

            Assert.Fail("no HUD button named " + name);
            return null;
        }
    }
}
