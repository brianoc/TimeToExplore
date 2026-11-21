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
    [Category("presentation")]
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
                "Space, P, Enter, and the arrows are gameplay keys, not UI navigation");
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
            WaitForPreview();
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
            WaitForPreview();
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
            WaitForPreview();
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
            Assert.That(_bootstrap.Flight.IsRetimePending, Is.True,
                "moving the handle queues a retime instead of blocking the frame");

            Update();
            Assert.That(slider.value, Is.EqualTo((float)(2.0 * minimum)).Within(1.0),
                "the handle keeps the player's ask while the plan lags");
            Assert.That(TextNamed("TimeSliderLabel").text, Does.Contain("×2.00"),
                "the label follows the handle, not the older published plan");

            WaitForRetime();
            Assert.That(_bootstrap.Flight.PreviewDurationSeconds,
                Is.EqualTo(2.0 * minimum).Within(1.0),
                "the finished plan is published on this thread");

            Update();
            Assert.That(TextNamed("TimeSliderLabel").text, Does.Contain("×2.00"));
        }

        [Test]
        public void TheArrivalStyleButtonsRebuildThePreviewAgainstTheChosenCapture()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            _hud.OpenDetails();

            // Opening frame: the click that opened the screen cannot also choose.
            Update();
            Button quintic = ButtonNamed("QuinticCaptureButton");
            Button conic = ButtonNamed("ConicCaptureButton");
            Assert.That(Child("TimeSliderGroup").activeSelf, Is.True);
            Assert.That(TextNamed("ArrivalStyleLabel").text, Is.EqualTo("ARRIVAL"));
            Assert.That(quintic.interactable, Is.False, "one frame of click protection");
            Assert.That(conic.interactable, Is.False);

            Update();
            Assert.That(quintic.interactable, Is.True);
            Assert.That(conic.interactable, Is.True);
            Assert.That(ButtonLabel("QuinticCaptureButton").color,
                Is.Not.EqualTo(ButtonLabel("ConicCaptureButton").color),
                "the active style is marked");
            Assert.That(_bootstrap.ArrivalStyle, Is.EqualTo(CaptureStyle.Quintic));

            double quinticMinimum = _bootstrap.Flight.PreviewMinimumSeconds;
            conic.onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview.Arcs.Any(
                arc => arc.Phase == FlightPhase.Coast),
                "the conic rebuild publishes");

            Assert.That(_bootstrap.ArrivalStyle, Is.EqualTo(CaptureStyle.Conic));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(
                arc => arc.Phase == FlightPhase.Coast), Is.True);
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(
                arc => arc.Phase == FlightPhase.Parking), Is.True);
            Assert.That(_bootstrap.Flight.PreviewMinimumSeconds,
                Is.Not.EqualTo(quinticMinimum),
                "the conic capture has its own fastest plan");
            Assert.That(SliderNamed("TimeSlider").minValue,
                Is.EqualTo((float)_bootstrap.Flight.PreviewMinimumSeconds).Within(1.0),
                "the trip-time window follows the new style");
            Assert.That(ButtonLabel("ConicCaptureButton").color,
                Is.Not.EqualTo(ButtonLabel("QuinticCaptureButton").color));

            quintic.onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview.Arcs.Count == 3,
                "the quintic rebuild publishes");

            Assert.That(_bootstrap.ArrivalStyle, Is.EqualTo(CaptureStyle.Quintic));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Count, Is.EqualTo(3),
                "switching back restores the quintic capture");
            Assert.That(ButtonLabel("QuinticCaptureButton").color,
                Is.Not.EqualTo(ButtonLabel("ConicCaptureButton").color));
        }

        [Test]
        public void TheThirdArrivalStyleButtonChoosesTheHybridFixedBurn()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            _hud.OpenDetails();

            Update();
            Button fixedBurn = ButtonNamed("FixedBurnButton");
            Assert.That(fixedBurn.interactable, Is.False, "one frame of click protection");
            Assert.That(ButtonLabel("FixedBurnButton").text, Is.EqualTo("0.3 g burn"));

            Update();
            Assert.That(fixedBurn.interactable, Is.True);
            fixedBurn.onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn),
                "the fixed-burn rebuild publishes");

            Assert.That(_bootstrap.ArrivalStyle, Is.EqualTo(CaptureStyle.FiniteBurn));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn), Is.True,
                "the fixed style previews a finite burn arc");
            Assert.That(_bootstrap.Flight.Preview.ArcForPhase(FlightPhase.Parking)
                .EntryImpulseMps, Is.GreaterThan(0.0),
                "the hybrid circularises impulsively");
            Assert.That(Child("BurnLevelSlider").activeSelf, Is.True);
            Assert.That(ButtonLabel("FixedBurnButton").color,
                Is.Not.EqualTo(ButtonLabel("QuinticCaptureButton").color));
            Assert.That(ButtonLabel("FixedBurnButton").color,
                Is.Not.EqualTo(ButtonLabel("ConicCaptureButton").color));
        }

        [Test]
        public void TheBurnLevelSliderRebuildsTheFixedThrustPreview()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            _hud.OpenDetails();

            Update();
            Update();
            ButtonNamed("FixedBurnButton").onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn),
                "the fixed-burn rebuild publishes");

            Slider level = SliderNamed("BurnLevelSlider");
            Assert.That(level.minValue, Is.EqualTo(0f).Within(1e-6));
            Assert.That(level.maxValue, Is.EqualTo(1f).Within(1e-6));
            Assert.That(level.value, Is.EqualTo(0.3f).Within(1e-4));
            Assert.That(TextNamed("BurnLevelValue").text, Does.Contain("0.30 g"));

            level.value = 0.5f;
            PumpUntil(() =>
                    System.Math.Abs(_bootstrap.Flight.Preview.FiniteBurnAccelerationMps2
                        - 0.5 * TripPlanner.StandardGravityMps2) < 1e-6,
                "the 0.5 g rebuild publishes");

            Assert.That(_bootstrap.Flight.BurnLevelG, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(_bootstrap.Flight.Preview.FiniteBurnAccelerationMps2,
                Is.EqualTo(0.5 * TripPlanner.StandardGravityMps2).Within(1e-6));
            Assert.That(ButtonLabel("FixedBurnButton").text, Is.EqualTo("0.5 g burn"));
            Assert.That(TextNamed("BurnLevelValue").text, Does.Contain("0.50 g"));

            level.value = 0f;
            PumpUntil(() => !_bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn),
                "the zero-level rebuild publishes");

            Assert.That(_bootstrap.Flight.BurnLevelG, Is.EqualTo(0.0));
            Assert.That(_bootstrap.Flight.Preview.FiniteBurnDeltaVMps, Is.EqualTo(0.0));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn), Is.False,
                "zero level is the pure conic arrival");
            Assert.That(ButtonLabel("FixedBurnButton").text, Is.EqualTo("0.0 g burn"));
        }

        [Test]
        public void TheCruiseProfileControlDefaultsToPoweredAndRebuildsThePreview()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            _hud.OpenDetails();

            Update();
            Assert.That(Child("TimeSliderGroup").activeSelf, Is.True);
            Assert.That(TextNamed("CruiseProfileLabel").text, Is.EqualTo("PROFILE"));
            Assert.That(_bootstrap.CruiseProfile, Is.EqualTo(CruiseStyle.Powered),
                "the powered torch plan stays the default");
            Button powered = ButtonNamed("PoweredProfileButton");
            Button coast = ButtonNamed("CoastProfileButton");
            Button burnCoastBurn = ButtonNamed("BurnCoastBurnProfileButton");
            Assert.That(powered.interactable, Is.False, "one frame of click protection");

            Update();
            Assert.That(powered.interactable, Is.True);
            Assert.That(ButtonLabel("PoweredProfileButton").color,
                Is.Not.EqualTo(ButtonLabel("CoastProfileButton").color),
                "the active profile is marked");
            Assert.That(Child("BurnLevelSlider").activeSelf, Is.False,
                "the powered profile has no fixed burn to level");

            burnCoastBurn.onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn),
                "the burn–coast–burn rebuild publishes");

            Assert.That(_bootstrap.CruiseProfile, Is.EqualTo(CruiseStyle.BurnCoastBurn));
            Assert.That(_bootstrap.Flight.CruiseProfile,
                Is.EqualTo(CruiseStyle.BurnCoastBurn));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn), Is.True,
                "the profile previews its departure burn and brake");
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(
                    arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast), Is.True);

            double minimum = _bootstrap.Flight.PreviewMinimumSeconds;
            Assert.That(minimum / JulianDate.SecondsPerDay, Is.LessThan(7.5),
                "the window starts at the profile's true floor, below the old 8.03 d one");
            Assert.That(minimum / JulianDate.SecondsPerDay, Is.GreaterThan(6.0),
                "the floor is the shortest plan the profile can fly, not the torch plan");
            double maximum = _bootstrap.Flight.PreviewMaximumSeconds;
            TestContext.WriteLine($"burn-coast-burn window: " +
                $"{minimum / JulianDate.SecondsPerDay:F2} d to " +
                $"{maximum / JulianDate.SecondsPerDay:F2} d");
            Assert.That(maximum, Is.GreaterThanOrEqualTo(minimum));
            Assert.That(maximum,
                Is.LessThanOrEqualTo(minimum * TripPlanner.MaxDurationScale + 1e-6),
                "the window never reaches past the planner's own span");
            Assert.That(SliderNamed("TimeSlider").minValue,
                Is.EqualTo((float)minimum).Within(1.0));
            Assert.That(SliderNamed("TimeSlider").maxValue,
                Is.EqualTo((float)maximum).Within(10.0));

            List<DetailRow> report = FlightReport.Build(_bootstrap.Flight,
                _bootstrap.Model, "x1");
            Assert.That(report.Any(row => row.Label == "Peak thrust"), Is.False,
                "a coast has no torch peak to report");
            DetailRow departureBurn = report.First(row => row.Label == "Departure burn");
            Assert.That(departureBurn.Value, Does.Contain("km/s"));
            Assert.That(departureBurn.Value, Does.Contain("0.3 g"));
            DetailRow brake = report.First(row => row.Label == "Brake");
            Assert.That(brake.Value, Does.Contain("km/s"));
            Assert.That(brake.Value, Does.Contain("0.3 g"));
            Assert.That(report.First(row => row.Label == "Coast" && 
                                               row.Value.StartsWith("drive off")).Value,
                Does.Contain("drive off"));
            Assert.That(report.Any(row => row.Label == "Capture"), Is.True);
            Assert.That(report.Any(row => row.Label == "Cruise" &&
                                          row.Value.Contains("prograde burn")), Is.True);
            Assert.That(report.Any(row => row.Label == "Capture" &&
                                          row.Value.Contains("brake")), Is.True);

            TrajectoryPlan atMaximum = _bootstrap.Flight.SetPreviewDuration(maximum);
            Assert.That(atMaximum.IsFeasible, Is.True, atMaximum.InfeasibleReason);
            Assert.That(TextNamed("TimeSliderValue").text, Does.Contain("g burns"));
            Assert.That(TextNamed("TimeSliderValue").text, Does.Not.Contain("% burn"),
                "a coast is not reported as a torch plan");

            Assert.That(Child("BurnLevelSlider").activeSelf, Is.True,
                "the profile plans its burns at the existing level control");
            Assert.That(SliderNamed("BurnLevelSlider").value, Is.EqualTo(0.3f).Within(1e-4));

            SliderNamed("BurnLevelSlider").value = 0.5f;
            PumpUntil(() =>
                    System.Math.Abs(_bootstrap.Flight.Preview.FiniteBurnAccelerationMps2
                        - 0.5 * TripPlanner.StandardGravityMps2) < 1e-6,
                "the burn-level rebuild publishes");

            Assert.That(_bootstrap.BurnLevelG, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(_bootstrap.Flight.Preview.FiniteBurnAccelerationMps2,
                Is.EqualTo(0.5 * TripPlanner.StandardGravityMps2).Within(1e-6),
                "the commanded level is the level the profile is planned at");

            coast.onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview.Arcs.All(arc => !arc.IsFiniteBurn),
                "the coast rebuild publishes");

            Assert.That(_bootstrap.CruiseProfile, Is.EqualTo(CruiseStyle.Coast));
            Assert.That(_bootstrap.Flight.Preview.Arcs.Any(arc => arc.IsFiniteBurn), Is.False,
                "the unpowered coast has no burns");
            Assert.That(TextNamed("TimeSliderValue").text, Does.Contain("drive off"));
            Assert.That(Child("BurnLevelSlider").activeSelf, Is.False,
                "an unpowered coast has no level to command");

            TrajectoryPlan coastPlan = _bootstrap.Flight.Preview;
            powered.onClick.Invoke();
            PumpUntil(() => _bootstrap.Flight.Preview != coastPlan,
                "the torch rebuild publishes");

            Assert.That(_bootstrap.CruiseProfile, Is.EqualTo(CruiseStyle.Powered));
            Assert.That(_bootstrap.Flight.PreviewMinimumSeconds,
                Is.LessThan(10.0 * JulianDate.SecondsPerDay),
                "switching back restores the torch plan's own window");
            Assert.That(TextNamed("TimeSliderValue").text, Does.Contain("% burn"));
        }

        [Test]
        public void TheProfileChoiceGainsAndLosesTheFixedBurnLegendRow()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            _hud.OpenDetails();

            Update();
            Update();
            Assert.That(_hud.RouteLegendCount, Is.EqualTo(1));
            Assert.That(_hud.RouteLegendLabel(0), Is.EqualTo("Quintic arc"));

            ButtonNamed("BurnCoastBurnProfileButton").onClick.Invoke();
            PumpUntil(() => _hud.RouteLegendCount == 3,
                "the burn–coast–burn rebuild publishes");

            Assert.That(_hud.RouteLegendCount, Is.EqualTo(3),
                "the profile draws a quintic break, fixed burns, and conic coasts");
            Assert.That(_hud.RouteLegendLabel(0), Is.EqualTo("Quintic arc"));
            Assert.That(_hud.RouteLegendLabel(1), Is.EqualTo("Fixed burn"));
            Assert.That(_hud.RouteLegendColour(1), Is.EqualTo(RouteColours.FixedBurn));
            Assert.That(_hud.RouteLegendIsDashed(1), Is.False);
            Assert.That(_hud.RouteLegendLabel(2), Is.EqualTo("Conic arc"));
            Assert.That(_hud.RouteLegendIsDashed(2), Is.True);

            int fixedRows = 0;
            for (int i = 0; i < _hud.RouteLegendCount; i++)
                if (_hud.RouteLegendLabel(i) == "Fixed burn") fixedRows++;
            Assert.That(fixedRows, Is.EqualTo(1),
                "the departure burn and the brake share one legend row");

            ButtonNamed("CoastProfileButton").onClick.Invoke();
            PumpUntil(() => _hud.RouteLegendCount == 2,
                "the coast rebuild publishes");

            Assert.That(_hud.RouteLegendCount, Is.EqualTo(2),
                "the unpowered coast loses the fixed-burn row");
            Assert.That(_hud.RouteLegendLabel(0), Is.EqualTo("Quintic arc"));
            Assert.That(_hud.RouteLegendLabel(1), Is.EqualTo("Conic arc"));

            ButtonNamed("PoweredProfileButton").onClick.Invoke();
            PumpUntil(() => _hud.RouteLegendCount == 1,
                "the torch rebuild publishes");

            Assert.That(_hud.RouteLegendCount, Is.EqualTo(1),
                "the torch plan loses the coast row too");
            Assert.That(_hud.RouteLegendLabel(0), Is.EqualTo("Quintic arc"));
        }

        [Test]
        public void TheRouteLegendListsTheVisibleArcTypesAboveTheFlightPanel()
        {
            Build();
            Assert.That(_hud.RouteLegendVisible, Is.False, "no route, no legend");
            Assert.That(Child("RouteLegend").GetComponent<Image>().raycastTarget, Is.False,
                "the legend is informational and must not swallow a click");

            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();
            Update();

            Assert.That(_hud.RouteLegendVisible, Is.True);
            Assert.That(_hud.RouteLegendCount, Is.EqualTo(1),
                "the quintic plan is three arcs of one type");
            Assert.That(_hud.RouteLegendLabel(0), Is.EqualTo("Quintic arc"));
            Assert.That(_hud.RouteLegendColour(0), Is.EqualTo(RouteColours.Quintic));
            Assert.That(_hud.RouteLegendIsDashed(0), Is.False);

            var flightRect = (RectTransform)Child("FlightPanel").transform;
            var legendRect = (RectTransform)Child("RouteLegend").transform;
            Assert.That(legendRect.anchoredPosition.y,
                Is.EqualTo(12f + flightRect.sizeDelta.y + 8f).Within(0.5f),
                "the legend stacks above the flight panel in the bottom-left corner");

            _bootstrap.SetArrivalStyle(CaptureStyle.FiniteBurn);
            PumpUntil(() => _hud.RouteLegendCount == 3,
                "the fixed-burn legend row appears when the rebuild publishes");
            Assert.That(_hud.RouteLegendLabel(0), Is.EqualTo("Quintic arc"));
            Assert.That(_hud.RouteLegendLabel(1), Is.EqualTo("Conic arc"));
            Assert.That(_hud.RouteLegendColour(1), Is.EqualTo(RouteColours.Conic));
            Assert.That(_hud.RouteLegendIsDashed(1), Is.True);
            Assert.That(_hud.RouteLegendLabel(2), Is.EqualTo("Fixed burn"));
            Assert.That(_hud.RouteLegendColour(2), Is.EqualTo(RouteColours.FixedBurn));
            Assert.That(_hud.RouteLegendIsDashed(2), Is.False);

            int conicRows = 0;
            for (int i = 0; i < _hud.RouteLegendCount; i++)
                if (_hud.RouteLegendLabel(i) == "Conic arc") conicRows++;
            Assert.That(conicRows, Is.EqualTo(1),
                "the three conic arcs share one legend row");

            _bootstrap.ShowHud = false;
            Update();
            Assert.That(_hud.RouteLegendVisible, Is.False,
                "H hides the legend with the rest of the status overlay");

            _bootstrap.ShowHud = true;
            _bootstrap.DiscardPreview();
            Update();
            Assert.That(_hud.RouteLegendVisible, Is.False, "discarding the route hides it");
        }

        [Test]
        public void ClosingTheDetailsScreenDiscardsAPlottedPreview()
        {
            Build();
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            _bootstrap.PlotRoute(mars);
            WaitForPreview();

            _hud.OpenDetails();
            Update();
            Update();
            ButtonNamed("CloseButton").onClick.Invoke();

            Assert.That(_hud.DetailsOpen, Is.False);
            Assert.That(_bootstrap.Flight.HasPreview, Is.False,
                "Close discards the preview, as the IMGUI screen did");
        }

        [Test]
        public void ThePointerPickSkipsAViewDestroyedByATeardown()
        {
            Build();
            BodyView victim = _bootstrap.Renderer.Views.First();
            Object.DestroyImmediate(victim.gameObject);

            // The renderer still lists the destroyed view. Picking at the centre
            // of the screen must skip it instead of throwing from every frame.
            MethodInfo pick = typeof(SolarSystemHud).GetMethod("PickAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(pick, Is.Not.Null, "SolarSystemHud.PickAt is missing");
            Assert.DoesNotThrow(() => pick.Invoke(_hud, new object[] { new Vector2(960f, 540f) }));
        }

        [Test]
        public void ARejectedPlanShowsItsCauseNextToPlanInfeasible()
        {
            Build();
            BodyRuntime mercury = _bootstrap.Model.Get("mercury");
            _bootstrap.Select(mercury);

            var planner = new TripPlanner(_bootstrap.Model,
                FlightFixture.FullField(_bootstrap.Model))
            {
                CruiseStyle = CruiseStyle.BurnCoastBurn,
                ArrivalStyle = CaptureStyle.Conic,
                BurnLevelG = 1.0
            };
            TrajectoryPlan rejected = planner.PlanAtDuration(
                FlightFixture.ShipAt(_bootstrap.Model, "gateway"), "mercury",
                3.19 * JulianDate.SecondsPerDay);
            Assert.That(rejected.IsFeasible, Is.False);
            Assert.That(rejected.InfeasibleReason, Does.Contain("departure break"));

            // A rejected flight is a state the planner only reaches through a
            // full Begin; put the session there directly so the display path is
            // what this test exercises.
            FlightSession flight = _bootstrap.Flight;
            SetField(flight, "<Plan>k__BackingField", rejected);
            SetField(flight, "<Status>k__BackingField", FlightStatus.Rejected);
            SetField(flight, "<Failure>k__BackingField", rejected.InfeasibleReason);

            _hud.OpenDetails();
            Update();

            Assert.That(ButtonNamed("RouteButton").GetComponentInChildren<Text>(true).text,
                Is.EqualTo("Plan infeasible"));

            string reason = rejected.InfeasibleReason;
            Assert.That(RowText("DetailsRow", 20), Does.Contain("departure break"),
                "the details panel names the cause beside the rejected route");
            Assert.That(RowText("FlightRow", 30), Does.Contain("departure break"),
                "the nav computer shows the max-thrust cause");
            Assert.That(RowText("FlightRow", 30), Does.Contain(reason),
                "the nav computer shows the whole reason");
        }

        private string RowText(string prefix, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Text value = TextNamed(prefix + i + "Value");
                if (value != null && value.gameObject.activeInHierarchy &&
                    !string.IsNullOrEmpty(value.text) &&
                    value.text.Contains("departure break"))
                {
                    return value.text;
                }
            }

            return string.Empty;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"{target.GetType().Name}.{name} is missing");
            field.SetValue(target, value);
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

        /// <summary>Pump the session's finished background retime until it is
        /// published, as the HUD's frame does. The worker is a real thread, so
        /// the test waits with a deadline rather than assuming one frame.</summary>
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

        /// <summary>Pump the HUD's update until the route preview is published,
        /// with a deadline so a broken worker fails instead of hanging.</summary>
        private void WaitForPreview()
        {
            PumpUntil(() => _bootstrap.Flight.HasPreview,
                "the background preview was never published");
        }

        /// <summary>Pump the HUD's update until <paramref name="condition"/>
        /// holds — background plans publish on the worker, so a single Update
        /// is not enough — with a deadline so a broken worker fails instead
        /// of hanging.</summary>
        private void PumpUntil(System.Func<bool> condition, string message)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                Update();
                if (watch.Elapsed.TotalSeconds > 20.0) Assert.Fail(message);
                System.Threading.Thread.Sleep(1);
            }
            Update();
        }

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
