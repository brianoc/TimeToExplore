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
    /// Evidence for the departure climb split, not a gate. The test docks at
    /// Gateway at the epoch, plots Gateway → Mars through the real details
    /// panel and retimes it to 18 days through the HUD's own retime entry
    /// point, then writes a screenshot of the trajectory line and the HUD.
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
    public class DepartureSplitCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const double SecondsPerDay = JulianDate.SecondsPerDay;
        private const double TripDays = 18.0;

        [UnityTest]
        public IEnumerator TheDepartureSplitShowsItsShortClimbAtEighteenDays()
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

            // Plot Gateway → Mars through the real details panel button, so the
            // preview, its framing and the HUD publish through the player path.
            BodyRuntime mars = bootstrap.Model.Get("mars");
            bootstrap.Select(mars);
            hud.OpenDetails();
            yield return null;
            yield return null;

            Button route = ButtonNamed(hud, "RouteButton");
            Assert.That(route.interactable, Is.True);
            route.onClick.Invoke();

            yield return WaitFor(
                () => bootstrap.Flight.HasPreview && bootstrap.Trajectory.IsShowing,
                "the HUD's frame pump must publish the plotted preview");
            yield return WaitFor(() => !bootstrap.Rig.IsFramingRoute,
                "plotting must ease the camera to the whole route");

            // The trip-time control appears with the preview, and its window
            // holds the 18-day mark. The handle has 64 whole steps, so a step
            // near 18 days retimes first; then ask for 18 days exactly through
            // the same retime entry point the handle calls.
            Slider slider = SliderNamed(hud, "TimeSlider");
            Assert.That(slider.gameObject.activeInHierarchy, Is.True,
                "a preview offers the trip-time control");

            double minimum = bootstrap.Flight.PreviewMinimumSeconds;
            double maximum = bootstrap.Flight.PreviewMaximumSeconds;
            Assert.That(TripDays * SecondsPerDay, Is.InRange(minimum, maximum),
                "the 18-day trip must sit inside the preview window");

            float step = (float)System.Math.Round(
                (TripDays * SecondsPerDay - minimum) / (maximum - minimum) * slider.maxValue);
            slider.value = step;
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending,
                "the handle's retime must publish");

            bootstrap.RetimePreview(TripDays * SecondsPerDay);
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending
                      && bootstrap.Flight.Preview != null
                      && System.Math.Abs(bootstrap.Flight.Preview.FlightTimeSeconds
                                         - TripDays * SecondsPerDay) < 1.0,
                "the exact 18-day retime must publish");

            TrajectoryPlan plan = bootstrap.Flight.Preview;
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.TargetId, Is.EqualTo("mars"));
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            PlanArc escape = plan.ArcForPhase(FlightPhase.Escape);
            Assert.That(escape, Is.Not.Null,
                "a planet departure carries the escape conic");
            double escapeDays = escape.DurationSeconds / SecondsPerDay;

            Debug.Log($"Departure split capture: {plan.FlightTimeDays:F4} d, " +
                      $"v∞ {plan.DepartureVInfinityMps / 1_000.0:F3} km/s, " +
                      $"escape {escapeDays:F4} d, " +
                      $"total {plan.DeltaVMps / 1_000.0:F3} km/s, " +
                      $"propellant {plan.PropellantKg / 1_000.0:F1} t");

            Assert.That(escapeDays, Is.EqualTo(1.027).Within(0.02),
                "18 days chooses the split climb near the measured 1.03-day escape");
            Assert.That(escapeDays, Is.LessThan(5.0),
                "the split climb is well under the old 10.74-day parabolic climb");
            Assert.That(plan.DeltaVMps / 1_000.0, Is.EqualTo(276.099).Within(0.5),
                "the 18-day split plan is the measured 276.1 km/s total");

            // The retime deliberately does not yank the camera, and the route
            // it framed was the floor preview. Frame the 18-day plan the same
            // way the bootstrap publishes a fresh plot, so the whole line and
            // the arrival ghost are in shot behind the HUD.
            var points = new List<Vector3d>();
            plan.SamplePath(RouteFraming.SamplesPerArc, points);
            bootstrap.Model.ComputeState(mars, plan.ArrivalJulianDate,
                out Vector3d arrivalM, out _);
            Assert.That(bootstrap.Rig.FrameRoute(points, bootstrap.ShipPositionM, arrivalM),
                Is.True, "the 18-day route must be frameable");
            yield return WaitFor(() => !bootstrap.Rig.IsFramingRoute,
                "the reframed camera must settle");

            string path = Path.GetFullPath("departure-split.png");
            Debug.Log($"Departure split capture: cwd {Directory.GetCurrentDirectory()}, " +
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
                Debug.Log("Departure split capture: screen capture wrote nothing; " +
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
