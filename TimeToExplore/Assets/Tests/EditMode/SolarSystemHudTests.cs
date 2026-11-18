using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using TimeToExplore.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The uGUI HUD built at runtime: one Canvas with the scaling and raycast
    /// components, one EventSystem with the Input System module, panels whose
    /// text comes from the pure builders, and buttons wired to the same owner
    /// methods the IMGUI screen called.
    ///
    /// The frame methods are invoked by reflection, as in
    /// <see cref="BootstrapSmokeTests"/>, because EditMode does not run the
    /// player loop. What a pointer click actually does on screen still needs a
    /// Play mode frame.
    /// </summary>
    public class SolarSystemHudTests
    {
        private GameObject _root;
        private GameObject _cameraObject;
        private SolarSystemBootstrap _bootstrap;
        private SolarSystemHud _hud;

        [TearDown]
        public void TearDown()
        {
            if (_bootstrap != null && _bootstrap.Sky != null) _bootstrap.Sky.Remove();
            if (_root != null) Object.DestroyImmediate(_root);
            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);

            _root = null;
            _cameraObject = null;
            _bootstrap = null;
            _hud = null;
        }

        [Test]
        public void TheHudBuildsOneScaledCanvasAndOneEventSystem()
        {
            Build();

            Canvas canvas = _hud.GetComponentInChildren<Canvas>(true);
            Assert.That(canvas, Is.Not.Null, "the HUD must own a Canvas");
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode,
                Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f).Within(1e-6f));

            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null,
                "the panels must be clickable and able to swallow the pointer");

            EventSystem[] systems = EventSystems();
            Assert.That(systems.Length, Is.EqualTo(1), "exactly one EventSystem");
            InputSystemUIInputModule module =
                systems[0].GetComponent<InputSystemUIInputModule>();
            Assert.That(module, Is.Not.Null,
                "uGUI input must come from the Input System module");
            Assert.That(module.actionsAsset, Is.Not.Null,
                "the module must carry the default UI actions");
            Assert.That(systems[0].sendNavigationEvents, Is.False,
                "Space, Enter, and the arrows are gameplay keys, not UI navigation");
            Assert.That(systems[0].GetComponent<StandaloneInputModule>(), Is.Null,
                "the legacy input module must not be added");

            Assert.That(Child("StatusPanel").GetComponent<Image>().raycastTarget, Is.True,
                "a panel background swallows the pointer");
            Assert.That(LabelTexts().First().raycastTarget, Is.False,
                "labels must not swallow the pointer");

            UiInput.EnsureEventSystem(out bool created);
            Assert.That(created, Is.False, "an existing EventSystem must not be duplicated");
            Assert.That(EventSystems().Length, Is.EqualTo(1));
        }

        [Test]
        public void TheStatusPanelTracksTheClockTheShipAndTheSelection()
        {
            Build();

            Text clock = TextNamed("StatusClock");
            Assert.That(clock.text,
                Is.EqualTo(JulianDate.Format(_bootstrap.Clock.JulianDate) + "     " +
                             _bootstrap.Clock.Label));

            Text distance = TextNamed("StatusDistance");
            Assert.That(distance.text, Does.Contain("Heliocentric distance"));
            Assert.That(distance.text, Does.Contain("docked"),
                "the idle session reports the docked velocity");

            BodyRuntime nearest = _bootstrap.Model.FindNearest(_bootstrap.ShipPositionM);
            Assert.That(TextNamed("StatusNearest").text,
                Does.Contain("Nearest body: " + nearest.Name));
            Assert.That(TextNamed("StatusHelp").text, Does.Contain("H HUD"));
        }

        [Test]
        public void TheDetailsPanelOpensWithTheBodyRowsAndCloses()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");

            _bootstrap.Select(mars);
            _hud.OpenDetails();
            Update();

            Assert.That(_hud.DetailsOpen, Is.True);
            Assert.That(Child("DetailsPanel").activeSelf, Is.True);
            Assert.That(TextNamed("DetailsTitle").text, Is.EqualTo("Mars"));
            Assert.That(TextNamed("DetailsKind").text, Is.EqualTo("PLANET"));
            Assert.That(TextNamed("DetailsSubtitle").text, Does.Contain("orbits Sun"));

            List<string> active = ActiveTexts();
            foreach (DetailRow row in BodyDetails.Build(
                         mars, _bootstrap.Model, _bootstrap.ShipPositionM))
            {
                Assert.That(active, Does.Contain(row.Label),
                    "the details panel is missing the row label " + row.Label);
                if (!row.IsHeading)
                    Assert.That(active, Does.Contain(row.Value),
                        "the details panel is missing the value of " + row.Label);
            }

            _hud.CloseDetails();
            Update();

            Assert.That(_hud.DetailsOpen, Is.False);
            Assert.That(Child("DetailsPanel").activeSelf, Is.False);
        }

        [Test]
        public void TheFlightPanelAppearsForAPreviewAndForAPlan()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");

            Assert.That(Child("FlightPanel").activeSelf, Is.False,
                "an idle session has no flight panel");

            _bootstrap.PlotRoute(mars);
            Update();

            Assert.That(Child("FlightPanel").activeSelf, Is.True);
            Assert.That(TextNamed("FlightTitle").text, Is.EqualTo("FLIGHT  /  MARS"));
            List<string> preview = ActiveTexts();
            Assert.That(preview, Does.Contain("PREVIEW"));
            Assert.That(preview, Does.Contain("Target"));
            Assert.That(preview, Does.Contain("Mars"));
            Assert.That(preview, Does.Contain("Status"));
            Assert.That(preview, Does.Contain("Preview"));

            _bootstrap.BeginGoTo(mars);
            Update();

            Assert.That(Child("FlightPanel").activeSelf, Is.True);
            Assert.That(TextNamed("FlightStatus").text, Is.EqualTo("UNDER WAY"));
            List<string> plan = ActiveTexts();
            Assert.That(plan, Does.Contain("PLAN"));
            Assert.That(plan, Does.Contain("NOW"));
            Assert.That(plan, Does.Contain("Arrival"));
        }

        [Test]
        public void ShowHudAndShowLabelsToggleVisibility()
        {
            Build();

            // Put a selected body in front of the camera so a label is certain.
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            BodyView marsView = _bootstrap.Renderer.Views.First(view => view.Body == mars);
            _cameraObject.transform.position = Vector3.zero;
            _cameraObject.transform.LookAt(marsView.transform.position);
            Update();

            Assert.That(LabelTexts().Any(text => text.text.StartsWith("Mars")), Is.True,
                "the selected body carries a label");

            _bootstrap.ShowLabels = false;
            Update();
            Assert.That(LabelTexts().Any(text => text.gameObject.activeSelf), Is.False,
                "L hides every body, ship, and trajectory label");

            _bootstrap.ShowHud = false;
            Update();
            Assert.That(Child("StatusPanel").activeSelf, Is.False, "H hides the status panel");

            _bootstrap.ShowHud = true;
            Update();
            Assert.That(Child("StatusPanel").activeSelf, Is.True);

            _bootstrap.ShowLabels = true;
            Update();
            Assert.That(LabelTexts().Any(text =>
                    text.gameObject.activeSelf && text.text.StartsWith("Mars")), Is.True);
        }

        [Test]
        public void TheDetailsButtonsCallTheSameOwnerMethods()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _hud.OpenDetails();

            Button route = ButtonNamed("RouteButton");
            Button begin = ButtonNamed("BeginButton");
            Button close = ButtonNamed("CloseButton");
            Button jump = ButtonNamed("JumpButton");

            // Opening frame: the click that opened the screen cannot press it.
            Update();
            Assert.That(route.interactable, Is.False, "one frame of click protection");
            Assert.That(close.interactable, Is.False);

            Update();
            Assert.That(route.interactable, Is.True);
            Assert.That(begin.interactable, Is.False, "Begin flight needs a plotted route");
            Assert.That(close.interactable, Is.True);
            Assert.That(jump.interactable, Is.True);

            route.onClick.Invoke(); // Plot route
            Assert.That(_bootstrap.Flight.HasPreview, Is.True);
            Assert.That(_bootstrap.Flight.Preview.TargetId, Is.EqualTo("mars"));
            Update();
            Assert.That(begin.interactable, Is.True);
            Assert.That(ButtonLabel("RouteButton").text,
                Is.EqualTo("Replot route to Mars"));

            begin.onClick.Invoke(); // Begin flight
            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
            Update();
            Assert.That(ButtonLabel("RouteButton").text, Is.EqualTo("Replan to Mars"));
            Assert.That(begin.interactable, Is.False, "there is no preview while flying");
            Assert.That(jump.interactable, Is.False, "a flight owns the ship");

            double departure = _bootstrap.Flight.Plan.DepartureJulianDate;
            _bootstrap.Clock.JulianDate += 0.01;
            route.onClick.Invoke(); // Replan
            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
            Assert.That(_bootstrap.Flight.Plan.DepartureJulianDate, Is.GreaterThan(departure));

            jump.onClick.Invoke(); // refused while flying
            Assert.That(_bootstrap.FreeFlight, Is.False);
            Assert.That(_bootstrap.Flight.IsFlying, Is.True);

            _bootstrap.CancelFlight();
            Update();
            jump.onClick.Invoke(); // Jump (debug)
            Assert.That(_bootstrap.FreeFlight, Is.True);

            close.onClick.Invoke(); // Close
            Assert.That(_hud.DetailsOpen, Is.False);
        }

        [Test]
        public void TheTripTimeSliderAppearsForAPreviewAndRetimesIt()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _hud.OpenDetails();

            Update();
            GameObject group = Child("TimeSliderGroup");
            Assert.That(group.activeSelf, Is.False,
                "there is nothing to time before a route is plotted");

            _bootstrap.PlotRoute(mars);
            Update();

            Assert.That(group.activeSelf, Is.True, "a preview offers the trip-time control");
            Slider slider = SliderNamed("TimeSlider");
            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            Assert.That(slider.minValue, Is.EqualTo((float)minimum).Within(1.0),
                "the left end is the fastest feasible plan");
            Assert.That(slider.maxValue,
                Is.EqualTo((float)(minimum * TripPlanner.MaxDurationScale)).Within(10.0));
            Assert.That(TextNamed("TimeSliderLabel").text, Does.StartWith("TRIP TIME"));
            Assert.That(TextNamed("TimeSliderValue").text,
                Is.EqualTo(SolarSystemHud.FormatDuration(minimum) + "  ·  " +
                           $"{_bootstrap.Flight.Preview.PeakThrottle * 100.0:0}% burn"));

            slider.value = (float)(2.0 * minimum);
            Assert.That(_bootstrap.Flight.PreviewDurationSeconds,
                Is.EqualTo(2.0 * minimum).Within(1.0),
                "moving the handle re-times the preview");

            Update();
            Assert.That(TextNamed("TimeSliderLabel").text, Does.Contain("×2.00"));
        }

        [Test]
        public void ClosingTheDetailsScreenDiscardsAPlottedPreview()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);

            _hud.OpenDetails();
            Update();
            Update();
            ButtonNamed("CloseButton").onClick.Invoke();

            Assert.That(_hud.DetailsOpen, Is.False);
            Assert.That(_bootstrap.Flight.HasPreview, Is.False,
                "Close discards the preview, as the IMGUI screen did");
        }

        // --- Fixture ------------------------------------------------------------

        private void Build()
        {
            _cameraObject = new GameObject("Test Camera");
            _cameraObject.tag = "MainCamera";
            _cameraObject.AddComponent<Camera>();

            _root = new GameObject("Test Bootstrap");
            _bootstrap = _root.AddComponent<SolarSystemBootstrap>();
            Invoke(_bootstrap, "Awake");

            _hud = _root.GetComponent<SolarSystemHud>();
            Assert.That(_hud, Is.Not.Null, "the bootstrap must create the HUD");
        }

        private void Update() => Invoke(_hud, "Update");

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null,
                $"{target.GetType().Name}.{method} is missing");
            info.Invoke(target, null);
        }

        private static EventSystem[] EventSystems() =>
            Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        private GameObject Child(string name)
        {
            foreach (Transform child in _hud.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.name == name) return child.gameObject;
            }

            Assert.Fail("no HUD child named " + name);
            return null;
        }

        private Text TextNamed(string name)
        {
            foreach (Text text in _hud.GetComponentsInChildren<Text>(true))
            {
                if (text.gameObject.name == name) return text;
            }

            Assert.Fail("no HUD Text named " + name);
            return null;
        }

        private Button ButtonNamed(string name)
        {
            foreach (Button button in _hud.GetComponentsInChildren<Button>(true))
            {
                if (button.gameObject.name == name) return button;
            }

            Assert.Fail("no HUD Button named " + name);
            return null;
        }

        private Text ButtonLabel(string buttonName) =>
            ButtonNamed(buttonName).GetComponentInChildren<Text>(true);

        private Slider SliderNamed(string name)
        {
            foreach (Slider slider in _hud.GetComponentsInChildren<Slider>(true))
            {
                if (slider.gameObject.name == name) return slider;
            }

            Assert.Fail("no HUD Slider named " + name);
            return null;
        }

        private List<string> ActiveTexts() =>
            _hud.GetComponentsInChildren<Text>(true)
                .Where(text => text.gameObject.activeInHierarchy)
                .Select(text => text.text)
                .ToList();

        private IEnumerable<Text> LabelTexts() =>
            _hud.GetComponentsInChildren<Text>(true)
                .Where(text => text.gameObject.name.Length > 5 &&
                               text.gameObject.name.StartsWith("Label"));
    }
}
