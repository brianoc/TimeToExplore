using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// Evidence for the full-blast minimum-time route, not a gate. The test
    /// docks at Gateway at the epoch, plots Gateway → Mars through the real
    /// details panel, and writes a screenshot of the **left end** of the
    /// trip-time window: the time-optimal zero-coast crossing at 6.66965 days,
    /// seven arcs, with the window still ending at 21.50253 days (three times
    /// the 7.16751-day coast floor). It then retimes one slider step above the
    /// minimum and asserts the short coast builds, so the capture records a
    /// window with no dead step.
    ///
    /// It carries only the <c>capture</c> category. Run it by hand without
    /// <c>-nographics</c>:
    /// <code>
    /// Unity -batchmode -projectPath &lt;test project&gt; -runTests \
    ///   -testPlatform PlayMode -testCategory capture -testResults ...
    /// </code>
    /// </summary>
    [Category("capture")]
    public class FullBlastCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const double SecondsPerDay = JulianDate.SecondsPerDay;
        private const double ExpectedMinimumDays = 6.66965;
        private const double ExpectedMaximumDays = 21.50253;
        private const double ExpectedVInfinityKms = 26.3;
        private const double ExpectedTotalKms = 1538.1;

        [UnityTest]
        public IEnumerator TheFullBlastMinimumIsTheWindowsLeftEnd()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindFirstObjectByType<SolarSystemBootstrap>();
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

            double minimum = bootstrap.Flight.PreviewMinimumSeconds;
            double maximum = bootstrap.Flight.PreviewMaximumSeconds;

            // The plot opens eight steps above the window's fastest trip.
            // Retime to the left end so this capture is the full-blast crossing.
            bootstrap.RetimePreview(minimum);
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending
                      && System.Math.Abs(bootstrap.Flight.Preview.FlightTimeSeconds
                                         - minimum) < 1.0,
                "the minimum must be published for the capture");
            TrajectoryPlan plan = bootstrap.Flight.Preview;

            Debug.Log($"Full-blast capture: minimum {minimum / SecondsPerDay:F5} d, " +
                      $"maximum {maximum / SecondsPerDay:F5} d, " +
                      $"{plan.FlightTimeDays:F5} d, {plan.Arcs.Count} arcs, " +
                      $"v∞ {plan.DepartureVInfinityMps / 1_000.0:F3} km/s, " +
                      $"total {plan.DeltaVMps / 1_000.0:F3} km/s, " +
                      $"propellant {plan.PropellantKg / 1_000.0:F1} t");

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.TargetId, Is.EqualTo("mars"));
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(minimum / SecondsPerDay,
                Is.EqualTo(ExpectedMinimumDays).Within(0.01),
                "the window starts at the full-blast crossing");
            Assert.That(maximum / SecondsPerDay,
                Is.EqualTo(ExpectedMaximumDays).Within(0.05),
                "the window still ends at three times the coast floor");
            Assert.That(plan.FlightTimeDays,
                Is.EqualTo(ExpectedMinimumDays).Within(0.01));
            Assert.That(plan.Arcs.Count, Is.EqualTo(7),
                "break, escape, departure burn, brake, and the capture chain");
            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "the full-blast minimum has no cruise coast");
            Assert.That(plan.DepartureVInfinityMps / 1_000.0,
                Is.EqualTo(ExpectedVInfinityKms).Within(1.0),
                "the grid's 1.0 step at 0.3 g");
            Assert.That(plan.DeltaVMps / 1_000.0,
                Is.EqualTo(ExpectedTotalKms).Within(2.0));

            // One slider step above the minimum is the short coast: the window
            // has no dead step between the crossing and the floor.
            float step = 1f;
            double chosen = minimum + (maximum - minimum) / (SolarSystemHud.TimeSliderSteps - 1);
            Slider slider = SliderNamed(hud, "TimeSlider");
            slider.value = step;
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending,
                "the first slider step's retime must publish");
            TrajectoryPlan shortCoast = bootstrap.Flight.Preview;
            Debug.Log($"Full-blast capture: first step {chosen / SecondsPerDay:F5} d -> " +
                      $"{shortCoast.FlightTimeDays:F5} d, {shortCoast.Arcs.Count} arcs, " +
                      $"feasible {shortCoast.IsFeasible}");
            Assert.That(shortCoast.IsFeasible, Is.True, shortCoast.InfeasibleReason);
            Assert.That(shortCoast.FlightTimeSeconds,
                Is.EqualTo(chosen).Within(1.0));
            Assert.That(shortCoast.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise
                                                   && arc.IsCoast),
                Is.True, "the first step is the short coast");

            // Back to the minimum for the capture, so the panel shows ×1.00 and
            // the no-coast crossing.
            bootstrap.RetimePreview(minimum);
            yield return WaitFor(
                () => !bootstrap.Flight.IsRetimePending
                      && System.Math.Abs(bootstrap.Flight.Preview.FlightTimeSeconds
                                         - minimum) < 1.0,
                "the minimum must be republished");

            TrajectoryPlan framed = bootstrap.Flight.Preview;
            var points = new List<Vector3d>();
            framed.SamplePath(RouteFraming.SamplesPerArc, points);
            bootstrap.Model.ComputeState(mars, framed.ArrivalJulianDate,
                out Vector3d arrivalM, out _);
            Assert.That(bootstrap.Rig.FrameRoute(points, bootstrap.ShipPositionM, arrivalM),
                Is.True, "the full-blast route must be frameable");
            yield return WaitFor(() => !bootstrap.Rig.IsFramingRoute,
                "the reframed camera must settle");

            string path = Path.GetFullPath("full-blast.png");
            Debug.Log($"Full-blast capture: cwd {Directory.GetCurrentDirectory()}, " +
                      $"writing {path}");

            Screen.SetResolution(Width, Height, false);

            if (Application.isBatchMode)
                yield return null;
            else
                yield return new WaitForEndOfFrame();

            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;

            if (!File.Exists(path))
            {
                Debug.Log("Full-blast capture: screen capture wrote nothing; " +
                          "rendering the canvas through the view camera");
                yield return CaptureThroughCamera(bootstrap.ViewCamera, hud, path);
            }

            Assert.That(File.Exists(path), Is.True, "the capture must write " + path);
        }

        /// <summary>Render the live HUD canvas with the game camera into a texture.</summary>
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
            float timeoutSeconds = 120f)
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
