using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using TimeToExplore.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// The real composition root inside Unity's own play mode. Unlike
    /// <c>BootstrapSmokeTests</c>, no lifecycle method is invoked by
    /// reflection: <c>RuntimeInitializeOnLoad</c> creates the bootstrap, Unity
    /// calls <c>Update</c> and <c>LateUpdate</c> in order every frame, and the
    /// HUD and renderer are the runtime components the player sees. The tests
    /// therefore cover the frame order (simulation before presentation), the
    /// trajectory line the HUD publishes through the real frame pump, and HUD
    /// state that only a real frame sequence produces.
    ///
    /// The step is supplied through the bootstrap's <c>DeltaTimeSeconds</c> seam
    /// so the run is deterministic and a few seconds long instead of real time.
    /// The seam only replaces <c>Time.deltaTime</c>; the player loop, the
    /// execution order, and every <c>Update</c>/<c>LateUpdate</c> are Unity's.
    /// </summary>
    [Category("playmode")]
    public class BootstrapPlayModeTests
    {
        private const float FrameSeconds = 1f;
        private const double FrameDays = FrameSeconds / JulianDate.SecondsPerDay;

        private SolarSystemBootstrap _bootstrap;
        private SolarSystemHud _hud;
        private readonly List<GameObject> _probes = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Let the runtime's AfterSceneLoad hook create the composition root
            // and let its first frame run before touching it.
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<SolarSystemBootstrap>();
            Assert.That(_bootstrap, Is.Not.Null,
                "RuntimeInitializeOnLoad must create the bootstrap in a bare play session");

            _bootstrap.CancelFlight();
            _bootstrap.DiscardPreview();
            _bootstrap.SetFreeFlight(false);
            if (_bootstrap.ArrivalStyle != CaptureStyle.Conic)
                _bootstrap.SetArrivalStyle(CaptureStyle.Conic);
            _bootstrap.SetBurnLevel(TripPlanner.DefaultBurnLevelG);
            _bootstrap.ShowHud = true;
            _bootstrap.ShowLabels = true;
            _bootstrap.Renderer.ShowOrbits = true;
            _bootstrap.Renderer.ShowMoonOrbits = true;
            _bootstrap.Renderer.TrueScale = false;

            _bootstrap.Clock.JulianDate = _bootstrap.Model.Data.startJulianDate;
            _bootstrap.SetScaleIndex(0);
            _bootstrap.ResetToGateway();
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;
            _bootstrap.SubstepsPerFrame = FlightSession.DefaultSubstepsPerFrame;

            BodyRuntime earth = _bootstrap.Model.Get("earth");
            _bootstrap.Select(earth);

            _hud = _bootstrap.GetComponent<SolarSystemHud>();
            Assert.That(_hud, Is.Not.Null, "the bootstrap must add the HUD");

            // One settled frame at the reset date, with the fixed seam.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _bootstrap?.CancelFlight();
            _bootstrap?.DiscardPreview();

            foreach (GameObject probe in _probes)
                if (probe != null) Object.Destroy(probe);
            _probes.Clear();

            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheRuntimeCreatesTheBootstrapAndTheRealRenderer()
        {
            Assert.That(_bootstrap.gameObject.name, Is.EqualTo("Time to Explore"),
                "the runtime entry point names its own root");

            Assert.That(_bootstrap.Model, Is.Not.Null);
            Assert.That(_bootstrap.Clock, Is.Not.Null);
            Assert.That(_bootstrap.Flight, Is.Not.Null);
            Assert.That(_bootstrap.Renderer, Is.Not.Null);
            Assert.That(_bootstrap.Trajectory, Is.Not.Null);
            Assert.That(_bootstrap.Rig, Is.Not.Null);
            Assert.That(_bootstrap.ViewCamera, Is.Not.Null);
            Assert.That(_bootstrap.Sky.Applied, Is.True);

            int renderable = _bootstrap.Model.Ordered.Count(body => body.IsRenderable);
            Assert.That(_bootstrap.Renderer.Views.Count(), Is.EqualTo(renderable),
                "the renderer builds one view per renderable body");
            foreach (BodyView view in _bootstrap.Renderer.Views)
            {
                Assert.That(view, Is.Not.Null);
                Assert.That(view.Renderer, Is.Not.Null,
                    $"{view.name} has no mesh renderer");
                Assert.That(view.Body, Is.Not.Null);
            }

            // The HUD is built during Initialise, but it only starts tracking
            // the world once Unity calls its Update.
            Assert.That(_hud, Is.Not.Null);
            Canvas canvas = _hud.GetComponentInChildren<Canvas>(true);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.GetComponent<CanvasScaler>(), Is.Not.Null);
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);

            Text clock = TextNamed("StatusClock");
            Assert.That(clock.text,
                Is.EqualTo(JulianDate.Format(_bootstrap.Clock.JulianDate) + "     " +
                             _bootstrap.Clock.Label),
                "the HUD text comes from the same clock the frame advanced");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheRealRendererDrawsBodiesAndOrbitsAfterTheModelsUpdate()
        {
            bool bodiesMatch = false;
            bool orbitsValid = false;
            int enabledOrbits = 0;

            var probeObject = new GameObject("Renderer Probe");
            var probe = probeObject.AddComponent<LateUpdateProbe>();
            probe.Callback = () =>
            {
                bodiesMatch = true;
                foreach (BodyView view in _bootstrap.Renderer.Views)
                {
                    if (view == null) continue;

                    Vector3 expected = ((view.Body.PositionM -
                                         _bootstrap.ShipPositionM) *
                                        _bootstrap.UnitsPerMeter).ToUnity();
                    if (Vector3.Distance(view.transform.localPosition, expected) > 1e-4f)
                        bodiesMatch = false;
                }

                enabledOrbits = 0;
                orbitsValid = true;
                foreach (OrbitLine orbit in
                         _bootstrap.Renderer.GetComponentsInChildren<OrbitLine>(true))
                {
                    var line = orbit.GetComponent<LineRenderer>();
                    if (line == null || !line.enabled) continue;

                    enabledOrbits++;
                    if (line.positionCount < 2 ||
                        line.widthCurve.length != line.positionCount ||
                        !(line.widthCurve.Evaluate(0f) > 0f) ||
                        !(line.widthCurve.Evaluate(1f) > 0f))
                    {
                        orbitsValid = false;
                    }

                    for (int i = 0; i < line.positionCount; i++)
                    {
                        Vector3 point = line.GetPosition(i);
                        if (!float.IsFinite(point.x) || !float.IsFinite(point.y) ||
                            !float.IsFinite(point.z))
                            orbitsValid = false;
                    }
                }
            };
            _probes.Add(probeObject);

            yield return null;
            Assert.That(bodiesMatch, Is.True,
                "every body view carries the frame's ship-relative position");
            Assert.That(enabledOrbits, Is.GreaterThan(0),
                "the orbit lines are on screen in the neighbourhood");
            Assert.That(orbitsValid, Is.True,
                "each visible orbit has positions and one width key per vertex");

            _bootstrap.Renderer.ShowOrbits = false;
            yield return null;
            Assert.That(enabledOrbits, Is.Zero,
                "the renderer flag reaches the real line renderers on the next frame");

            _bootstrap.Renderer.ShowOrbits = true;
            _bootstrap.Renderer.ShowMoonOrbits = true;
            yield return null;
            Assert.That(enabledOrbits, Is.GreaterThan(0));

            Assert.That(_bootstrap.Renderer.Views.Count(),
                Is.EqualTo(_bootstrap.Model.Ordered.Count(body => body.IsRenderable)));
        }

        [UnityTest]
        public IEnumerator TheFrameRunsSimulationBeforePresentation()
        {
            // The ship starts beside Earth and shares its heliocentric motion,
            // so Earth barely moves in the ship-relative render frame. The Sun
            // does: it is at rest while the ship travels at the planet's
            // orbital speed, making one frame's offset unambiguous.
            BodyRuntime probeBody = _bootstrap.Model.Sun;

            var probeObject = new GameObject("Frame Order Probe");
            var probe = probeObject.AddComponent<FrameOrderProbe>();
            probe.Bootstrap = _bootstrap;
            probe.Body = probeBody;
            _probes.Add(probeObject);

            // Bootstrap has DefaultExecutionOrder(-100), so its Update has
            // already run when this probe's Update fires, while its LateUpdate
            // has not yet. A test coroutine resumes in the Update phase, so the
            // yields need to cover several complete frame pairs.
            yield return null;
            yield return null;
            yield return null;

            Assert.That(probe.Frames, Is.GreaterThanOrEqualTo(2),
                "the probe must observe at least two complete frames");

            Assert.That(probe.LastAdvanceDays, Is.EqualTo(FrameDays).Within(1e-8),
                "the clock advances one frame step per LateUpdate; " +
                "the wide tolerance is the float cancellation at a Julian date");

            Assert.That(probe.ClockHeldBetweenPhases, Is.True,
                "nothing advances the clock between Update and LateUpdate");

            // Update saw the model already propagated to this frame's date, but
            // the body view still holds last frame's render position.
            Assert.That(Vector3.Distance(probe.DrawnAtUpdate, probe.ExpectedAtUpdate),
                Is.GreaterThan(1e-3f),
                "the renderer must not have drawn this frame's state during Update");

            // LateUpdate runs after the bootstrap's refresh, so the view now
            // matches the state the same frame's Update produced.
            Assert.That(Vector3.Distance(probe.DrawnAtLateUpdate, probe.ExpectedAtLateUpdate),
                Is.LessThan(1e-4f),
                "the body view is written during the bootstrap's LateUpdate");
        }

        [UnityTest]
        public IEnumerator TheRealTrajectoryLineDrawsTheHudsPublishedPreview()
        {
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.SetScaleIndex(0);
            _bootstrap.Select(mars);

            // Drive the real details panel and its real button event, not the
            // bootstrap method directly.
            _hud.OpenDetails();
            yield return null;

            Button route = ButtonNamed("RouteButton");
            Assert.That(route.interactable, Is.False,
                "the click that opened the details screen cannot also press a control");

            yield return null;
            Assert.That(route.interactable, Is.True);

            route.onClick.Invoke();

            yield return WaitFor(
                () => _bootstrap.Flight.HasPreview && _bootstrap.Trajectory.IsShowing,
                "the HUD's frame pump must publish the plotted preview");
            yield return WaitFor(() => !_bootstrap.Rig.IsFramingRoute,
                "plotting must ease the camera to the whole route");
            yield return null;

            TrajectoryPlan preview = _bootstrap.Flight.Preview;
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.Trajectory.ArcCount, Is.EqualTo(preview.Arcs.Count));

            Assert.That(Child("FlightPanel").activeSelf, Is.True,
                "the flight panel appears with the preview");
            Assert.That(TextNamed("FlightTitle").text, Is.EqualTo("FLIGHT  /  MARS"));
            Assert.That(_hud.RouteLegendVisible, Is.True);
            Assert.That(_hud.RouteLegendCount, Is.GreaterThanOrEqualTo(3));

            var legendLabels = Enumerable.Range(0, _hud.RouteLegendCount)
                .Select(_hud.RouteLegendLabel)
                .ToList();
            Assert.That(legendLabels, Does.Contain("Quintic arc"));
            Assert.That(legendLabels, Does.Contain("Fixed burn"));
            Assert.That(legendLabels, Does.Contain("Conic arc"));

            AssertTrajectoryLines(preview);

            Assert.That(_bootstrap.Trajectory.HasGhost, Is.True);

            // The coroutine resumes in Update, after the bootstrap has advanced
            // the ship but before its LateUpdate moves the markers. Read the
            // marker where it is drawn: at the end of the frame.
            Vector3 ghostAtLateUpdate = Vector3.zero;
            Vector3 expectedGhostAtLateUpdate = Vector3.zero;
            var ghostProbeObject = new GameObject("Ghost Probe");
            var ghostProbe = ghostProbeObject.AddComponent<LateUpdateProbe>();
            ghostProbe.Callback = () =>
            {
                ghostAtLateUpdate = _bootstrap.Trajectory.GhostMarkerPosition;
                _bootstrap.Model.ComputeState(mars, preview.ArrivalJulianDate,
                    out Vector3d marsAtArrival, out _);
                expectedGhostAtLateUpdate =
                    ((marsAtArrival - _bootstrap.ShipPositionM) *
                     _bootstrap.UnitsPerMeter).ToUnity();
            };
            _probes.Add(ghostProbeObject);

            yield return null;

            Assert.That(Vector3.Distance(ghostAtLateUpdate, expectedGhostAtLateUpdate),
                Is.LessThan(1e-3f),
                "the ghost marks where Mars will be on arrival");
        }

        [UnityTest]
        public IEnumerator TheHudTracksTheClockAndRetimesThroughRealFrames()
        {
            Text clock = TextNamed("StatusClock");
            yield return null;
            Assert.That(clock.text,
                Is.EqualTo(JulianDate.Format(_bootstrap.Clock.JulianDate) + "     " +
                             _bootstrap.Clock.Label));

            string beforeText = clock.text;
            double beforeDate = _bootstrap.Clock.JulianDate;
            yield return null;
            yield return null;

            Assert.That(_bootstrap.Clock.JulianDate, Is.GreaterThan(beforeDate));
            Assert.That(clock.text, Is.Not.EqualTo(beforeText),
                "the status clock is rewritten every real frame");

            _bootstrap.Select(_bootstrap.Model.Get("mars"));
            _hud.OpenDetails();
            yield return null;
            yield return null;

            ButtonNamed("RouteButton").onClick.Invoke();
            yield return WaitFor(
                () => _bootstrap.Flight.HasPreview && _bootstrap.Trajectory.IsShowing,
                "the preview must publish");

            Slider slider = SliderNamed("TimeSlider");
            Assert.That(slider.gameObject.activeInHierarchy, Is.True);

            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            Assert.That(slider.minValue, Is.EqualTo((float)minimum).Within(1.0));
            Assert.That(slider.maxValue,
                Is.EqualTo((float)(minimum * TripPlanner.MaxDurationScale))
                    .Within(10.0));

            double chosen = Math.Min(2.0 * minimum,
                _bootstrap.Flight.PreviewMaximumSeconds);
            slider.value = (float)chosen;

            Assert.That(_bootstrap.Flight.IsRetimePending, Is.True,
                "the handle queues the retime; it does not block the frame");

            yield return WaitFor(
                () => !_bootstrap.Flight.IsRetimePending &&
                      Math.Abs(_bootstrap.Flight.PreviewDurationSeconds - chosen) < 1.0,
                "the real HUD pump must publish the re-timed preview");

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(TextNamed("TimeSliderLabel").text, Does.Contain("×2.00"),
                "the label reports the chosen multiple of the minimum");
            Assert.That(TextNamed("TimeSliderValue").text, Does.Contain("g burns"),
                "the profile reports its burn level, not a torch throttle");

            _bootstrap.ShowHud = false;
            yield return null;
            Assert.That(Child("StatusPanel").activeSelf, Is.False);
            Assert.That(_hud.RouteLegendVisible, Is.False,
                "the legend follows the HUD visibility");

            _bootstrap.ShowHud = true;
            var bodyLabelsBefore = LabelWidgets()
                .Where(label => label.gameObject.activeInHierarchy)
                .Select(label => label.text)
                .ToList();
            Assert.That(bodyLabelsBefore.Any(text => text.StartsWith("Mars")), Is.True,
                "the selected body is labelled while labels are on");

            _bootstrap.ShowLabels = false;
            yield return null;
            Assert.That(Child("StatusPanel").activeSelf, Is.True);
            Assert.That(LabelWidgets()
                .Where(label => label.gameObject.activeInHierarchy)
                .Any(label => label.text.StartsWith("Mars")), Is.False,
                "L hides the body labels; route labels are not body labels");

            _bootstrap.ShowLabels = true;
            yield return null;
            Assert.That(LabelWidgets()
                .Where(label => label.gameObject.activeInHierarchy)
                .Any(label => label.text.StartsWith("Mars")), Is.True);
        }

        [UnityTest]
        public IEnumerator PressingBeginInTheRealHudFliesAndCancelClearsTheRoute()
        {
            _bootstrap.Select(_bootstrap.Model.Get("mars"));
            _hud.OpenDetails();
            yield return null;
            yield return null;

            ButtonNamed("RouteButton").onClick.Invoke();
            yield return WaitFor(
                () => _bootstrap.Flight.HasPreview && _bootstrap.Trajectory.IsShowing,
                "the preview must publish");
            yield return null;

            Button begin = ButtonNamed("BeginButton");
            Assert.That(begin.interactable, Is.True);
            begin.onClick.Invoke();

            yield return null;

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_bootstrap.Flight.HasPreview, Is.False,
                "Begin consumes the preview");
            Assert.That(_bootstrap.Flight.Plan.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(Child("FlightPanel").activeSelf, Is.True);
            Assert.That(TextNamed("FlightStatus").text, Is.EqualTo("UNDER WAY"));

            // The world never runs ahead of the ship while a flight is live.
            Assert.That(_bootstrap.Clock.JulianDate,
                Is.EqualTo(_bootstrap.Flight.State.JulianDate).Within(1e-9));
            yield return null;
            Assert.That(_bootstrap.Clock.JulianDate,
                Is.EqualTo(_bootstrap.Flight.State.JulianDate).Within(1e-9),
                "the frame clamp keeps the clock and the ship together");

            _bootstrap.CancelFlight();
            yield return null;

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);
            Assert.That(_bootstrap.Trajectory.ArcCount, Is.Zero);
            Assert.That(Child("FlightPanel").activeSelf, Is.False,
                "the flight panel hides once the plan and preview are gone");
        }

        /// <summary>
        /// Every arc line the planner ordered is a real, enabled
        /// <see cref="LineRenderer"/> with positions written by the frame's
        /// <c>LateUpdate</c>, the arc's own colour, one width key per vertex, and
        /// the framed route in the camera's view.
        /// </summary>
        private void AssertTrajectoryLines(TrajectoryPlan preview)
        {
            Camera camera = _bootstrap.ViewCamera;
            Assert.That(camera, Is.Not.Null);

            int onScreen = 0;
            int total = 0;

            for (int i = 0; i < _bootstrap.Trajectory.ArcCount; i++)
            {
                Transform child = _bootstrap.Trajectory.transform.Find(
                    "Plan Arc " + (i + 1) + " Line");
                Assert.That(child, Is.Not.Null,
                    $"arc {i} has no line object");
                var line = child.GetComponent<LineRenderer>();
                Assert.That(line, Is.Not.Null);
                Assert.That(line.enabled, Is.True,
                    $"arc {i} is enabled after the frame's LateUpdate");
                Assert.That(line.positionCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(line.widthCurve.length, Is.EqualTo(line.positionCount),
                    "every vertex carries its own apparent width");
                AssertColour(line.startColor, _bootstrap.Trajectory.ArcColour(i),
                    $"arc {i} start colour");
                AssertColour(line.endColor, _bootstrap.Trajectory.ArcColour(i),
                    $"arc {i} end colour");

                for (int v = 0; v < line.positionCount; v++)
                {
                    Vector3 point = line.GetPosition(v);
                    Assert.That(float.IsFinite(point.x) && float.IsFinite(point.y) &&
                                float.IsFinite(point.z), Is.True,
                        $"arc {i} vertex {v} is finite");

                    float width = line.widthCurve.Evaluate(
                        line.positionCount == 1 ? 0f : v / (float)(line.positionCount - 1));
                    Assert.That(width, Is.GreaterThan(0f),
                        $"arc {i} vertex {v} has a positive width");

                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    total++;
                    if (viewport.z > 0f && viewport.x >= -0.05f && viewport.x <= 1.05f &&
                        viewport.y >= -0.05f && viewport.y <= 1.05f)
                        onScreen++;
                }
            }

            Assert.That(preview.Arcs.Count, Is.EqualTo(_bootstrap.Trajectory.ArcCount));
            Assert.That(total, Is.GreaterThan(0));
            Assert.That(onScreen, Is.GreaterThan(total / 2),
                "the route framing puts most of the drawn line in the camera's view");
        }

        /// <summary>LineRenderer stores colours as Color32, so a round trip
        /// through its property is quantised to 8 bits per channel.</summary>
        private static void AssertColour(Color actual, Color expected, string message)
        {
            Assert.That(Mathf.Abs(actual.r - expected.r), Is.LessThan(0.005f), message);
            Assert.That(Mathf.Abs(actual.g - expected.g), Is.LessThan(0.005f), message);
            Assert.That(Mathf.Abs(actual.b - expected.b), Is.LessThan(0.005f), message);
            Assert.That(Mathf.Abs(actual.a - expected.a), Is.LessThan(0.005f), message);
        }

        private static IEnumerator WaitFor(Func<bool> condition, string message,
            float timeoutSeconds = 30f)        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                    Assert.Fail(message);
                yield return null;
            }
        }

        private GameObject Child(string name)
        {
            foreach (Transform child in _hud.GetComponentsInChildren<Transform>(true))
                if (child.gameObject.name == name) return child.gameObject;

            Assert.Fail("no HUD child named " + name);
            return null;
        }

        private Text TextNamed(string name)
        {
            foreach (Text text in _hud.GetComponentsInChildren<Text>(true))
                if (text.gameObject.name == name) return text;

            Assert.Fail("no HUD text named " + name);
            return null;
        }

        private Button ButtonNamed(string name)
        {
            foreach (Button button in _hud.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;

            Assert.Fail("no HUD button named " + name);
            return null;
        }

        private Slider SliderNamed(string name)
        {
            foreach (Slider slider in _hud.GetComponentsInChildren<Slider>(true))
                if (slider.gameObject.name == name) return slider;

            Assert.Fail("no HUD slider named " + name);
            return null;
        }

        private IEnumerable<Text> LabelWidgets() =>
            _hud.GetComponentsInChildren<Text>(true)
                .Where(text => text.gameObject.name.StartsWith("Label"));
    }
}
