using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;

namespace TimeToExplore.UI
{
    /// <summary>
    /// The runtime-built uGUI HUD: status panel, flight panel, details screen,
    /// body and trajectory labels, the hover prompt, and the crosshair.
    ///
    /// The object graph is created once in <see cref="Initialise"/>. Every frame
    /// only text, visibility, colours, and positions change, so there is no
    /// per-frame allocation of GameObjects and no layout rebuild of the panels.
    /// The text itself comes from the pure builders — <see cref="BodyDetails"/>,
    /// <see cref="FlightReport"/>, and <see cref="BodyLabels"/> — exactly as it
    /// did under IMGUI.
    ///
    /// The Canvas is a Screen Space Overlay with a 1920×1080 reference and a 0.5
    /// match, so the IMGUI pixel sizes are kept at 1080p and scale with the
    /// window. Panel backgrounds accept raycasts; labels, the hover prompt, and
    /// the crosshair never do, so they cannot swallow a click.
    /// </summary>
    public class SolarSystemHud : MonoBehaviour
    {
        private const float PanelWidth = 430f;
        private const float DetailsWidth = 392f;
        private const float Margin = 16f;
        private const float StatusPanelHeight = 252f;

        private const float DetailsHeaderHeight = 95f;
        private const float DetailsFooterHeight = 122f;
        private const float DetailsSliderHeight = 154f;
        private const float DetailsHeadingHeight = 22f;
        private const float DetailsRowHeight = 17f;
        private const float FlightHeaderHeight = 58f;
        private const float FlightHeadingHeight = 20f;
        private const float FlightRowHeight = 17f;

        private const float LegendWidth = 178f;
        private const float LegendHeaderHeight = 22f;
        private const float LegendRowHeight = 18f;
        private const float LegendSwatchWidth = 26f;
        private const float LegendSwatchHeight = 3f;

        private const int MaxDetailsRows = 20;
        private const int MaxFlightRows = 30;
        private const int MaxLegendRows = 6;
        private const int InitialLabelPool = 24;

        private static readonly Color StatusBackground = new Color(0.01f, 0.02f, 0.04f, 0.82f);
        private static readonly Color DetailsBackground = new Color(0.015f, 0.025f, 0.045f, 0.92f);
        private static readonly Color FlightBackground = new Color(0.012f, 0.03f, 0.05f, 0.90f);

        private static readonly Color TitleColour = new Color(0.85f, 0.93f, 1f);
        private static readonly Color PanelTitleColour = new Color(0.94f, 0.97f, 1f);
        private static readonly Color TextColour = new Color(0.88f, 0.90f, 0.94f);
        private static readonly Color DimColour = new Color(0.62f, 0.68f, 0.76f);
        private static readonly Color SectionColour = new Color(0.48f, 0.66f, 0.88f);
        private static readonly Color RowLabelColour = new Color(0.62f, 0.68f, 0.76f);
        private static readonly Color RowValueColour = new Color(0.90f, 0.93f, 0.97f);
        private static readonly Color WrapColour = new Color(0.76f, 0.82f, 0.89f);
        private static readonly Color LabelColour = new Color(0.85f, 0.92f, 1f, 0.92f);
        private static readonly Color SelectedLabelColour = new Color(1f, 0.85f, 0.4f);
        private static readonly Color ShipLabelColour = new Color(0.86f, 0.90f, 0.96f);
        private static readonly Color FlipLabelColour = new Color(1f, 0.55f, 0.35f);
        private static readonly Color BurnLabelColour = new Color(1f, 0.96f, 0.85f);
        private static readonly Color ParkBurnLabelColour = new Color(1f, 0.88f, 0.35f);
        private static readonly Color GhostLabelColour = new Color(0.62f, 0.78f, 1f);
        private static readonly Color ArrivalLabelColour = new Color(0.55f, 1f, 0.72f);
        private static readonly Color ButtonTextColour = new Color(0.92f, 0.95f, 1f);
        private static readonly Color CrosshairColour = new Color(1f, 1f, 1f, 0.65f);
        private static readonly Color FallbackAccent = new Color(0.55f, 0.75f, 1f);

        private const string HelpText =
            "LMB select · LMB drag pans map · RMB drag orbits · wheel zoom · 1–7 warp\n" +
            "Space ship · P pause · Tab target · R reset · Esc cancel · T true scale\n" +
            "O orbits · L labels · H HUD · ` free flight (WASD, , . speed) · X cancel";

        private SolarSystemBootstrap _owner;
        private bool _built;
        private Font _font;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private RectTransform _overlay;
        private GameObject _canvasObject;
        private GameObject _eventSystemObject;

        private GameObject _statusPanel;
        private RectTransform _statusRect;
        private Text _statusClock;
        private Text _statusDistance;
        private Text _statusNearest;
        private Text _statusWarpNote;
        private Text _statusTarget;
        private Text _statusTargetOrbit;
        private Text _statusHelp;

        private GameObject _detailsPanel;
        private RectTransform _detailsRect;
        private Image _detailsAccent;
        private Image _detailsDivider;
        private Text _detailsTitle;
        private Text _detailsKind;
        private Text _detailsSubtitle;
        private Text _detailsDescription;
        private readonly List<RowView> _detailsRows = new List<RowView>();
        private Button _routeButton;
        private Text _routeLabel;
        private Button _beginButton;
        private Button _closeButton;
        private Button _jumpButton;
        private BodyRuntime _detailsBody;

        // Trip-time and arrival-style controls, shown only while a preview for
        // the selected body exists. The slider's value is flight time in
        // seconds, so its range is the preview's own feasible window; the
        // profile buttons choose how the ship crosses, and the arrival buttons
        // choose the capture model the preview was built with.
        private RectTransform _timeSliderGroup;
        private Button _poweredButton;
        private Button _coastButton;
        private Button _burnCoastBurnButton;
        private Button _quinticButton;
        private Button _conicButton;
        private Button _fixedBurnButton;
        private Text _poweredLabel;
        private Text _coastLabel;
        private Text _burnCoastBurnLabel;
        private Text _quinticLabel;
        private Text _conicLabel;
        private Text _fixedBurnLabel;
        private Slider _timeSlider;
        private Text _timeLabel;
        private Text _timeValue;
        private Text _burnLevelLabel;
        private Text _burnLevelValue;
        private Slider _burnLevelSlider;
        private bool _syncingSlider;
        private bool _syncingBurnLevel;
        private string _sliderBodyId;
        private double _sliderMinimumSeconds = double.NaN;

        private GameObject _flightPanel;
        private RectTransform _flightRect;
        private Image _flightAccent;
        private Text _flightTitle;
        private Text _flightStatus;
        private Text _flightNext;
        private readonly List<RowView> _flightRows = new List<RowView>();

        // The route legend: one swatch and label per distinct arc kind on the
        // visible plan, stacked above the flight panel in the bottom-left
        // corner. Informational only, so it never swallows a click.
        private GameObject _legendPanel;
        private RectTransform _legendRect;
        private Text _legendTitle;
        private readonly List<LegendRow> _legendRows = new List<LegendRow>();
        private readonly string[] _legendLabels = new string[MaxLegendRows];
        private readonly Color[] _legendColours = new Color[MaxLegendRows];
        private readonly bool[] _legendDashed = new bool[MaxLegendRows];
        private int _legendCount;

        private readonly List<LabelView> _labels = new List<LabelView>();
        private int _labelCursor;
        private GameObject _crosshair;

        private bool _detailsOpen;
        private bool _detailsOpenedThisFrame;
        private BodyRuntime _hover;
        private int _hoverHitCount;
        private BodyRuntime _clickBody;
        private Vector2 _clickPressPointer;
        private bool _clickDragged;
        private readonly List<ScreenTarget> _targets = new List<ScreenTarget>();
        private readonly List<ScreenTarget> _hoverHits = new List<ScreenTarget>();
        private readonly List<ScreenTarget> _clickHits = new List<ScreenTarget>();
        private readonly PickCycle _pickCycle = new PickCycle();

        // --- Public API ---------------------------------------------------------

        public void Initialise(SolarSystemBootstrap owner)
        {
            _owner = owner;
            BuildUi();
        }

        /// <summary>The details screen is open. Clicking a body opens it.</summary>
        public bool DetailsOpen => _detailsOpen;

        /// <summary>True while the route colour legend is on screen.</summary>
        public bool RouteLegendVisible => _legendPanel != null && _legendPanel.activeSelf;

        /// <summary>Number of distinct arc kinds listed in the route legend.</summary>
        public int RouteLegendCount => _legendCount;

        public string RouteLegendLabel(int index) =>
            index >= 0 && index < _legendCount ? _legendLabels[index] : null;

        public Color RouteLegendColour(int index) =>
            index >= 0 && index < _legendCount ? _legendColours[index] : Color.clear;

        public bool RouteLegendIsDashed(int index) =>
            index >= 0 && index < _legendCount && _legendDashed[index];

        /// <summary>Open the details screen for the current selection.</summary>
        public void OpenDetails()
        {
            if (_owner == null || _owner.Selected == null) return;

            _detailsOpen = true;
            _detailsBody = _owner.Selected;
            _detailsOpenedThisFrame = true;
        }

        public void CloseDetails()
        {
            _detailsOpen = false;
            _owner?.DiscardPreview();
        }

        // --- Frame --------------------------------------------------------------

        private void Update()
        {
            if (_owner == null || _owner.Model == null) return;
            if (!_built) BuildUi();

            if (PlayerControls.CancelPressed) CloseDetails();

            // The planner worker hands its finished plans back here, on the
            // main thread, before the panels read the preview. The worker
            // only builds; nothing a frame draws comes from it: the preview
            // the player sees is always a published plan.
            _owner.ApplyPreviewIfReady();
            _owner.ApplyRetimeIfReady();

            UpdatePointer();
            Refresh();

            // One clear frame of protection: the click that opened the details
            // screen cannot also press a button inside it, whichever order the
            // EventSystem and this Update run in.
            _detailsOpenedThisFrame = false;
        }

        private void OnDestroy()
        {
            DestroyObject(_canvasObject);
            DestroyObject(_eventSystemObject);
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void UpdatePointer()
        {
            _hover = null;
            _hoverHitCount = 0;

            Vector2 screen = PlayerControls.PointerPosition;
            bool pressed = PlayerControls.LeftClickPressed;
            bool released = PlayerControls.LeftClickReleased;

            // A press always starts a fresh click candidate, whatever it is over,
            // so no earlier gesture can select on a later release.
            if (pressed)
            {
                _clickBody = null;
                _clickHits.Clear();
                _clickPressPointer = screen;
                _clickDragged = false;
            }
            else if (_clickBody != null && (PlayerControls.LeftClickHeld || released) &&
                     PlayerControls.ExceedsClickSlop(_clickPressPointer, screen))
            {
                _clickDragged = true;
            }

            // With the cursor locked for mouse look there is no pointer to aim.
            if (Cursor.lockState != CursorLockMode.None)
            {
                if (pressed) _pickCycle.Reset();
                if (released) _clickBody = null;
                return;
            }

            // Any raycast-target panel swallows the pointer before the world sees
            // it. IsPointerOverGameObject can lag the EventSystem by a frame, so
            // the panel rects are tested immediately as well.
            if (UiInput.PointerOverUi() || PointerOverPanel(screen))
            {
                if (pressed) _pickCycle.Reset();
                if (released) _clickBody = null;
                return;
            }

            Vector2 pointer = PlayerControls.ToGuiPointer(screen, Screen.height);
            PickHitsAt(pointer, _hoverHits);
            _hoverHitCount = _hoverHits.Count;

            // Hover names the body the next click would select, so the prompt
            // follows the cycle instead of always naming the top target.
            int next = _pickCycle.Peek(_hoverHits, pointer);
            _hover = next >= 0 ? _hoverHits[next].Body : null;

            if (pressed)
            {
                // A click on empty space ends the cycle; a click on this stack
                // keeps it, and the release advances it.
                if (_hoverHits.Count == 0) _pickCycle.Reset();
                _clickHits.AddRange(_hoverHits);
                _clickBody = _hover;
            }

            if (!released) return;

            // The pan shares the left button, so a body is selected only when the
            // press and release stayed inside the click slop. A drag is a pan and
            // never opens the details screen.
            if (_clickDragged) _pickCycle.Reset();

            BodyRuntime clicked = null;
            if (!_clickDragged && _clickHits.Count > 0)
            {
                int index = _pickCycle.Advance(_clickHits, _clickPressPointer);
                if (index >= 0) clicked = _clickHits[index].Body;
            }

            _clickBody = null;
            if (clicked == null) return;

            _owner.Select(clicked);
            OpenDetails();
        }

        /// <summary>Immediate screen-space guard for the three HUD panels.</summary>
        private bool PointerOverPanel(Vector2 screenPoint) =>
            ContainsScreenPoint(_statusRect, screenPoint) ||
            (_detailsOpen && ContainsScreenPoint(_detailsRect, screenPoint)) ||
            (HasFlightPanel && ContainsScreenPoint(_flightRect, screenPoint));

        /// <summary>
        /// Whether a screen point is inside an active overlay-canvas rect. Used
        /// alongside the EventSystem so a panel click cannot reach the world on
        /// the frame the EventSystem has not raycast yet.
        /// </summary>
        public static bool ContainsScreenPoint(RectTransform rect, Vector2 screenPoint) =>
            rect != null && rect.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);

        private bool HasFlightPanel =>
            _owner?.Flight != null && (_owner.Flight.Plan != null || _owner.Flight.HasPreview);

        private void PickHitsAt(Vector2 pointer, List<ScreenTarget> hits)
        {
            CollectTargets();
            BodyPicker.OrderedHits(_targets, pointer, hits);
        }

        private void CollectTargets()
        {
            _targets.Clear();

            Camera camera = _owner.ViewCamera;
            if (camera == null || _owner.Renderer == null) return;

            foreach (BodyView view in _owner.Renderer.Views)
            {
                // A view destroyed by a scene teardown is fake-null; skip it.
                if (view == null) continue;

                Vector3 world = view.transform.position;
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;

                Vector3 edge = world + camera.transform.right * view.VisualRadiusUnits;
                Vector3 edgeScreen = camera.WorldToScreenPoint(edge);
                float radius = Vector2.Distance(
                    new Vector2(screen.x, screen.y),
                    new Vector2(edgeScreen.x, edgeScreen.y));

                _targets.Add(new ScreenTarget
                {
                    Body = view.Body,
                    Centre = new Vector2(screen.x, Screen.height - screen.y),
                    RadiusPixels = radius,
                    Depth = world.magnitude
                });
            }
        }

        private void Refresh()
        {
            RefreshStatus();
            RefreshDetails();
            RefreshFlight();
            RefreshLegend();
            RefreshOverlay();
        }

        // --- Status panel -------------------------------------------------------

        private void RefreshStatus()
        {
            bool show = _owner.ShowHud;
            if (_statusPanel.activeSelf != show) _statusPanel.SetActive(show);
            if (!show) return;

            Vector3d shipPosition = _owner.ShipPositionM;
            BodyRuntime sun = _owner.Model.Sun;
            double sunDistance = Vector3d.Distance(shipPosition, sun.PositionM);
            BodyRuntime nearest = _owner.Model.FindNearest(shipPosition);
            BodyRuntime selected = _owner.Selected;

            _statusClock.text = JulianDate.Format(_owner.Clock.JulianDate) + "     " + _owner.Clock.Label;
            _statusDistance.text =
                $"Heliocentric distance {FormatDistance(sunDistance)}     {ShipStatusText()}";
            _statusNearest.text = nearest != null
                ? $"Nearest body: {nearest.Name} ({FormatDistance(Vector3d.Distance(nearest.PositionM, shipPosition))})"
                : "Nearest body: —";

            float y = 98f;

            bool hasNote = !string.IsNullOrEmpty(_owner.WarpNote);
            if (_statusWarpNote.gameObject.activeSelf != hasNote)
                _statusWarpNote.gameObject.SetActive(hasNote);
            if (hasNote)
            {
                _statusWarpNote.text = "warp held by the flight policy  ·  " + _owner.WarpNote;
                PlaceTopLeft(_statusWarpNote, 14f, y, PanelWidth - 28f, 16f);
                y += 16f;
            }

            bool hasTarget = selected != null;
            if (_statusTarget.gameObject.activeSelf != hasTarget)
                _statusTarget.gameObject.SetActive(hasTarget);
            if (_statusTargetOrbit.gameObject.activeSelf != hasTarget)
                _statusTargetOrbit.gameObject.SetActive(hasTarget);

            if (hasTarget)
            {
                string parent = selected.Parent != null ? selected.Parent.Name : "—";
                string period = selected.HasOrbit
                    ? FormatDuration(selected.Elements.PeriodSeconds)
                    : "—";
                _statusTarget.text =
                    $"Target: {selected.Name}   R {FormatDistance(selected.RadiusM)}   GM {selected.Gm:0.00E+00} m³/s²";
                _statusTargetOrbit.text =
                    $"Orbits {parent}   sidereal period {period}   {BodyDetails.KindLabel(selected)}";
                PlaceTopLeft(_statusTarget, 14f, y, PanelWidth - 28f, 18f);
                PlaceTopLeft(_statusTargetOrbit, 14f, y + 18f, PanelWidth - 28f, 18f);
                y += 40f;
            }

            PlaceTopLeft(_statusHelp, 14f, y, PanelWidth - 28f, 58f);
        }

        private string ShipStatusText()
        {
            FlightSession flight = _owner.Flight;
            if (flight == null) return "—";
            if (_owner.FreeFlight)
                return $"free flight · {FormatSpeed(_owner.Ship.FlySpeedMps)}";

            switch (flight.Status)
            {
                case FlightStatus.Flying:
                    return $"{FormatSpeed(flight.State.VelocityMps.Magnitude)} · " +
                           $"{flight.Throttle * 100.0:0}% thrust";
                case FlightStatus.Arrived:
                    return $"{FormatSpeed(flight.State.VelocityMps.Magnitude)} · in orbit";
                case FlightStatus.Rejected:
                    return "plan rejected";
                case FlightStatus.Aborted:
                    return "flight aborted";
                default:
                    return $"docked · {FormatSpeed(flight.State.VelocityMps.Magnitude)}";
            }
        }

        // --- Details panel ------------------------------------------------------

        private void RefreshDetails()
        {
            BodyRuntime body = _owner.Selected;

            if (!_detailsOpen || body == null)
            {
                if (body == null) _detailsOpen = false;
                if (_detailsPanel.activeSelf) _detailsPanel.SetActive(false);
                return;
            }

            if (!_detailsPanel.activeSelf) _detailsPanel.SetActive(true);
            _detailsBody = body;

            List<DetailRow> rows = BodyDetails.Build(body, _owner.Model, _owner.ShipPositionM);

            // The click that opened this panel must not also press a control.
            bool interactive = !_detailsOpenedThisFrame;
            FlightSession flight = _owner.Flight;
            bool flying = flight != null && flight.IsFlying;
            bool rejected = flight != null &&
                            flight.Status == FlightStatus.Rejected &&
                            flight.TargetId == body.Id;
            bool hasPreview = flight != null && flight.HasPreview &&
                              flight.Preview.TargetId == body.Id;
            bool plottable = body.Gm > 0.0 && !rejected;
            bool showSlider = hasPreview;

            // The rejected route button says "Plan infeasible"; the row above it
            // carries the cause, so the player is not left guessing which part of
            // the profile the drive could not fly.
            if (rejected && !string.IsNullOrEmpty(flight.Failure))
                rows.Add(DetailRow.Of("Plan", flight.Failure));

            float rowsHeight = 0f;
            foreach (DetailRow row in rows)
                rowsHeight += row.IsHeading ? DetailsHeadingHeight : DetailsRowHeight;

            float height = DetailsHeaderHeight + rowsHeight + DetailsFooterHeight +
                           (showSlider ? DetailsSliderHeight : 0f);
            _detailsRect.sizeDelta = new Vector2(DetailsWidth, height);

            _detailsTitle.text = body.Name;
            _detailsKind.text = BodyDetails.KindLabel(body).ToUpperInvariant();
            _detailsSubtitle.text = BodyDetails.Subtitle(body, _owner.Model);

            bool hasDescription = !string.IsNullOrEmpty(body.Data.description);
            if (_detailsDescription.gameObject.activeSelf != hasDescription)
                _detailsDescription.gameObject.SetActive(hasDescription);
            if (hasDescription) _detailsDescription.text = body.Data.description;

            Color accent = BodyColour(body);
            _detailsAccent.color = accent;
            _detailsDivider.color = new Color(accent.r, accent.g, accent.b, 0.28f);

            float contentWidth = DetailsWidth - 36f;
            float y = DetailsHeaderHeight;
            for (int i = 0; i < _detailsRows.Count; i++)
            {
                RowView entry = _detailsRows[i];
                bool used = i < rows.Count;
                if (entry.Heading.gameObject.activeSelf != used)
                {
                    entry.Heading.gameObject.SetActive(used);
                    entry.Label.gameObject.SetActive(used);
                    entry.Value.gameObject.SetActive(used);
                }
                if (!used) continue;

                DetailRow row = rows[i];
                if (row.IsHeading)
                {
                    if (entry.Value.gameObject.activeSelf) entry.Value.gameObject.SetActive(false);
                    if (entry.Label.gameObject.activeSelf) entry.Label.gameObject.SetActive(false);
                    entry.Heading.gameObject.SetActive(true);
                    entry.Heading.text = row.Label;
                    PlaceTopLeft(entry.Heading, 18f, y, contentWidth, 18f);
                }
                else
                {
                    if (entry.Heading.gameObject.activeSelf) entry.Heading.gameObject.SetActive(false);
                    entry.Label.gameObject.SetActive(true);
                    entry.Value.gameObject.SetActive(true);
                    entry.Label.text = row.Label;
                    entry.Value.text = row.Value;
                    PlaceTopLeft(entry.Label, 18f, y - 1f, 138f, 17f);
                    PlaceTopLeft(entry.Value, 158f, y - 1f, contentWidth - 140f, 17f);
                }
                y += row.IsHeading ? DetailsHeadingHeight : DetailsRowHeight;
            }

            float buttonY = height - 112f;
            PlaceTopLeft(_routeButton.image, 18f, buttonY, contentWidth, 30f);
            PlaceTopLeft(_beginButton.image, 18f, buttonY + 36f, contentWidth - 106f, 28f);
            PlaceTopLeft(_closeButton.image, 18f + contentWidth - 98f, buttonY + 36f, 98f, 28f);
            PlaceTopLeft(_jumpButton.image, 18f, buttonY + 70f, contentWidth - 106f, 28f);

            PlaceTimeSlider(showSlider, flight, height, contentWidth);

            _routeLabel.text = rejected
                ? "Plan infeasible"
                : flying
                    ? $"Replan to {body.Name}"
                    : hasPreview
                        ? $"Replot route to {body.Name}"
                        : $"Plot route to {body.Name}";

            _routeButton.interactable = interactive && plottable;
            _beginButton.interactable = interactive && plottable && !flying && hasPreview;
            _closeButton.interactable = interactive;
            _jumpButton.interactable = interactive && !flying;
        }

        private void OnRouteClicked()
        {
            BodyRuntime body = _detailsBody ?? _owner?.Selected;
            if (body == null || _owner == null) return;

            bool flying = _owner.Flight != null && _owner.Flight.IsFlying;
            if (flying) _owner.BeginGoTo(body);
            else _owner.PlotRoute(body);
        }

        private void OnBeginClicked()
        {
            _owner?.BeginPlottedFlight();
        }

        private void OnCloseClicked()
        {
            CloseDetails();
        }

        private void OnJumpClicked()
        {
            _owner?.FocusSelected();
        }

        /// <summary>
        /// Position and fill the trip-time control. It appears only while the
        /// preview belongs to the selected body, and its range is that
        /// preview's feasible window, so the handle cannot ask for a plan the
        /// drive cannot fly. The slider's value is flight time in seconds.
        /// </summary>
        private void PlaceTimeSlider(bool show, FlightSession flight, float height,
            float contentWidth)
        {
            if (_timeSliderGroup == null) return;

            if (_timeSliderGroup.gameObject.activeSelf != show)
                _timeSliderGroup.gameObject.SetActive(show);
            if (!show || flight?.Preview == null) return;

            float top = height - DetailsFooterHeight - DetailsSliderHeight + 4f;
            _timeSliderGroup.anchoredPosition = new Vector2(18f, -top);
            _timeSliderGroup.sizeDelta = new Vector2(contentWidth, DetailsSliderHeight);

            // The click that opened the screen cannot also choose a style.
            bool interactive = !_detailsOpenedThisFrame;
            _poweredButton.interactable = interactive;
            _coastButton.interactable = interactive;
            _burnCoastBurnButton.interactable = interactive;
            _quinticButton.interactable = interactive;
            _conicButton.interactable = interactive;
            _fixedBurnButton.interactable = interactive;

            CruiseStyle profile = _owner.CruiseProfile;
            ShowModeChoice(_poweredButton, _poweredLabel, profile == CruiseStyle.Powered);
            ShowModeChoice(_coastButton, _coastLabel, profile == CruiseStyle.Coast);
            ShowModeChoice(_burnCoastBurnButton, _burnCoastBurnLabel,
                profile == CruiseStyle.BurnCoastBurn);

            CaptureStyle style = _owner.ArrivalStyle;
            ShowModeChoice(_quinticButton, _quinticLabel,
                style == CaptureStyle.Quintic);
            ShowModeChoice(_conicButton, _conicLabel,
                style == CaptureStyle.Conic);
            ShowModeChoice(_fixedBurnButton, _fixedBurnLabel,
                style == CaptureStyle.FiniteBurn);

            if (flight != null)
            {
                double g = flight.FixedBurnAccelerationMps2 / TripPlanner.StandardGravityMps2;
                _fixedBurnLabel.text = $"{g:0.0} g burn";
            }

            // The level belongs to the fixed-thrust arrival and to the
            // burn–coast–burn cruise, which plans both its burns at it.
            bool showLevel = style == CaptureStyle.FiniteBurn ||
                             profile == CruiseStyle.BurnCoastBurn;
            if (_burnLevelLabel.gameObject.activeSelf != showLevel)
            {
                _burnLevelLabel.gameObject.SetActive(showLevel);
                _burnLevelValue.gameObject.SetActive(showLevel);
                _burnLevelSlider.gameObject.SetActive(showLevel);
            }

            if (showLevel && flight != null)
            {
                double level = flight.BurnLevelG;
                _burnLevelSlider.interactable = interactive;
                if (Mathf.Abs(_burnLevelSlider.value - (float)level) > 1e-4f)
                {
                    _syncingBurnLevel = true;
                    _burnLevelSlider.value = (float)level;
                    _syncingBurnLevel = false;
                }

                double acceleration = flight.FixedBurnAccelerationMps2;
                _burnLevelValue.text = level < 0.005
                    ? profile == CruiseStyle.BurnCoastBurn
                        ? "0.0 g · coast"
                        : "0.0 g · conic"
                    : $"{level:0.00} g  ·  {acceleration:0.00} m/s²";
            }

            _timeSlider.interactable = interactive;

            double minimum = flight.PreviewMinimumSeconds;
            double maximum = flight.PreviewMaximumSeconds;

            // While a background retime is outstanding the published preview is
            // the previous full-field plan and the handle is the player's newer
            // ask. Show the ask and leave the handle alone: snapping it back to
            // the older plan would fight the drag.
            bool retimePending = flight.IsRetimePending;
            double duration = retimePending
                ? flight.PendingPreviewSeconds
                : flight.PreviewDurationSeconds;

            bool rangeChanged = _sliderBodyId != flight.Preview.TargetId ||
                                _sliderMinimumSeconds != minimum;
            if (rangeChanged)
            {
                _sliderBodyId = flight.Preview.TargetId;
                _sliderMinimumSeconds = minimum;
                _syncingSlider = true;
                _timeSlider.minValue = (float)minimum;
                _timeSlider.maxValue = (float)maximum;
                _timeSlider.value = (float)duration;
                _syncingSlider = false;
            }
            else if (!retimePending &&
                     Mathf.Abs(_timeSlider.value - (float)duration) >
                     Mathf.Max(0.5f, 1e-4f * (float)(maximum - minimum)))
            {
                // The plan and the handle disagree (a fresh plot or a replan).
                // Do not fight a drag: the retimed plan's flight time is exactly
                // the value the handle already holds, so this only runs on a
                // deliberate reset.
                _syncingSlider = true;
                _timeSlider.value = (float)duration;
                _syncingSlider = false;
            }

            double scale = minimum > 0.0 ? duration / minimum : 1.0;
            _timeLabel.text = $"TRIP TIME   ×{scale:0.00}";
            _timeValue.text = FormatDuration(duration) + "  ·  " + DriveStateLabel(flight);
        }

        private void OnTimeSliderChanged(float seconds)
        {
            if (_syncingSlider || _owner == null) return;
            _owner.RetimePreview(seconds);
        }

        private void OnQuinticCaptureClicked()
        {
            if (_detailsOpenedThisFrame || _owner == null) return;
            _owner.SetArrivalStyle(CaptureStyle.Quintic);
        }

        private void OnConicCaptureClicked()
        {
            if (_detailsOpenedThisFrame || _owner == null) return;
            _owner.SetArrivalStyle(CaptureStyle.Conic);
        }

        private void OnFixedBurnClicked()
        {
            if (_detailsOpenedThisFrame || _owner == null) return;
            _owner.SetArrivalStyle(CaptureStyle.FiniteBurn);
        }

        private void OnBurnLevelChanged(float levelG)
        {
            if (_syncingBurnLevel || _detailsOpenedThisFrame || _owner == null) return;
            _owner.SetBurnLevel(levelG);
        }

        private void OnPoweredProfileClicked()
        {
            if (_detailsOpenedThisFrame || _owner == null) return;
            _owner.SetCruiseProfile(CruiseStyle.Powered);
        }

        private void OnCoastProfileClicked()
        {
            if (_detailsOpenedThisFrame || _owner == null) return;
            _owner.SetCruiseProfile(CruiseStyle.Coast);
        }

        private void OnBurnCoastBurnProfileClicked()
        {
            if (_detailsOpenedThisFrame || _owner == null) return;
            _owner.SetCruiseProfile(CruiseStyle.BurnCoastBurn);
        }

        /// <summary>
        /// The one-line state of the drive under the trip-time slider. A coast
        /// is reported as a coast, never as the torch plan's peak throttle.
        /// </summary>
        private static string DriveStateLabel(FlightSession flight)
        {
            if (flight?.Preview == null) return "—";

            switch (flight.CruiseProfile)
            {
                case CruiseStyle.Coast:
                    return "coast · drive off";
                case CruiseStyle.BurnCoastBurn:
                    double g = flight.FixedBurnAccelerationMps2
                               / TripPlanner.StandardGravityMps2;
                    return g < 0.005
                        ? "coast · drive off"
                        : $"{g:0.0} g burns · coast";
                default:
                    return $"{flight.Preview.PeakThrottle * 100.0:0}% burn";
            }
        }

        /// <summary>Mark the active profile or arrival style: its label takes the
        /// selected gold and its fill is brighter, so the row reads as one
        /// choice.</summary>
        private static void ShowModeChoice(Button button, Text label, bool active)
        {
            label.color = active ? SelectedLabelColour : DimColour;

            ColorBlock colours = button.colors;
            Color normal = active
                ? new Color(0.22f, 0.35f, 0.50f, 0.98f)
                : new Color(0.11f, 0.15f, 0.22f, 0.95f);
            if (colours.normalColor == normal) return;

            colours.normalColor = normal;
            colours.selectedColor = normal;
            colours.highlightedColor = active
                ? new Color(0.28f, 0.43f, 0.60f, 1f)
                : new Color(0.19f, 0.27f, 0.37f, 0.98f);
            button.colors = colours;
        }

        // --- Flight panel -------------------------------------------------------

        private void RefreshFlight()
        {
            if (!HasFlightPanel)
            {
                if (_flightPanel.activeSelf) _flightPanel.SetActive(false);
                return;
            }

            if (!_flightPanel.activeSelf) _flightPanel.SetActive(true);
            FlightSession flight = _owner.Flight;
            double now = _owner.Clock.JulianDate;
            string warp = _owner.Clock.Label +
                          (string.IsNullOrEmpty(_owner.WarpNote) ? "" : "  (" + _owner.WarpNote + ")");

            List<DetailRow> rows = FlightReport.Build(flight, _owner.Model, warp, now);

            float rowsHeight = 0f;
            foreach (DetailRow row in rows)
                rowsHeight += row.IsHeading ? FlightHeadingHeight : FlightRowHeight;

            float height = FlightHeaderHeight + rowsHeight;
            _flightRect.sizeDelta = new Vector2(PanelWidth, height);

            _flightTitle.text = "FLIGHT  /  " +
                                ((flight.Plan ?? flight.Preview)?.TargetId ?? "—").ToUpperInvariant();
            _flightStatus.text = FlightReport.StatusLabel(flight, now).ToUpperInvariant();
            _flightNext.text = flight.Plan != null
                ? FlightReport.NextEvent(flight)
                : FlightReport.PreviewNextEvent(flight, now);
            _flightAccent.color = FlightAccent(flight.Status);

            float contentWidth = PanelWidth - 32f;
            float y = FlightHeaderHeight;
            for (int i = 0; i < _flightRows.Count; i++)
            {
                RowView entry = _flightRows[i];
                bool used = i < rows.Count;
                if (entry.Heading.gameObject.activeSelf != used)
                {
                    entry.Heading.gameObject.SetActive(used);
                    entry.Label.gameObject.SetActive(used);
                    entry.Value.gameObject.SetActive(used);
                }
                if (!used) continue;

                DetailRow row = rows[i];
                if (row.IsHeading)
                {
                    if (entry.Value.gameObject.activeSelf) entry.Value.gameObject.SetActive(false);
                    if (entry.Label.gameObject.activeSelf) entry.Label.gameObject.SetActive(false);
                    entry.Heading.gameObject.SetActive(true);
                    entry.Heading.text = row.Label;
                    PlaceTopLeft(entry.Heading, 16f, y, contentWidth, 16f);
                }
                else
                {
                    if (entry.Heading.gameObject.activeSelf) entry.Heading.gameObject.SetActive(false);
                    entry.Label.gameObject.SetActive(true);
                    entry.Value.gameObject.SetActive(true);
                    entry.Label.text = row.Label;
                    entry.Value.text = row.Value;
                    PlaceTopLeft(entry.Label, 16f, y - 1f, 118f, 17f);
                    PlaceTopLeft(entry.Value, 136f, y - 1f, contentWidth - 120f, 17f);
                }
                y += row.IsHeading ? FlightHeadingHeight : FlightRowHeight;
            }
        }

        // --- Route legend -------------------------------------------------------

        /// <summary>
        /// The colour key for the plotted or flying route: one row per distinct
        /// arc kind, in the order the arcs appear on the plan. It is rebuilt from
        /// the trajectory line, so it always matches what is drawn — a conic
        /// route lists its blue coast and pink capture, while the hybrid lists a
        /// red burn between the coasts. The panel sits above the flight panel,
        /// which owns the bottom-left corner whenever a route is on screen.
        /// </summary>
        private void RefreshLegend()
        {
            TrajectoryLine trajectory = _owner.Trajectory;
            _legendCount = 0;

            if (_owner.ShowHud && trajectory != null && trajectory.IsShowing)
            {
                for (int i = 0; i < trajectory.ArcCount && _legendCount < MaxLegendRows; i++)
                {
                    string label = trajectory.ArcLabel(i);
                    if (string.IsNullOrEmpty(label) || HasLegendLabel(label)) continue;

                    _legendLabels[_legendCount] = label;
                    _legendColours[_legendCount] = trajectory.ArcColour(i);
                    _legendDashed[_legendCount] = trajectory.ArcIsDashed(i);
                    _legendCount++;
                }
            }

            bool show = _legendCount > 0;
            if (_legendPanel.activeSelf != show) _legendPanel.SetActive(show);
            if (!show) return;

            float height = LegendHeaderHeight + _legendCount * LegendRowHeight + 6f;
            _legendRect.sizeDelta = new Vector2(LegendWidth, height);

            float flightHeight = HasFlightPanel && _flightPanel.activeSelf
                ? _flightRect.sizeDelta.y
                : 0f;
            _legendRect.anchoredPosition = new Vector2(12f,
                12f + flightHeight + (flightHeight > 0f ? 8f : 0f));

            for (int i = 0; i < _legendRows.Count; i++)
            {
                LegendRow row = _legendRows[i];
                bool used = i < _legendCount;
                bool dashed = used && _legendDashed[i];

                if (row.Label.gameObject.activeSelf != used)
                    row.Label.gameObject.SetActive(used);
                if (row.Solid.gameObject.activeSelf != (used && !dashed))
                    row.Solid.gameObject.SetActive(used && !dashed);
                for (int d = 0; d < row.Dashes.Length; d++)
                {
                    if (row.Dashes[d].gameObject.activeSelf != dashed)
                        row.Dashes[d].gameObject.SetActive(dashed);
                }

                if (!used) continue;

                Color colour = _legendColours[i];
                row.Label.text = _legendLabels[i];
                row.Solid.color = colour;
                for (int d = 0; d < row.Dashes.Length; d++)
                    row.Dashes[d].color = colour;
            }
        }

        private bool HasLegendLabel(string label)
        {
            for (int i = 0; i < _legendCount; i++)
                if (_legendLabels[i] == label) return true;
            return false;
        }

        private static Color FlightAccent(FlightStatus status)
        {
            switch (status)
            {
                case FlightStatus.Flying: return new Color(1f, 0.72f, 0.32f);
                case FlightStatus.Arrived: return new Color(0.45f, 0.95f, 0.65f);
                case FlightStatus.Aborted:
                case FlightStatus.Rejected: return new Color(1f, 0.42f, 0.38f);
                default: return new Color(0.55f, 0.75f, 1f);
            }
        }

        // --- Labels, hover, crosshair ------------------------------------------

        private void RefreshOverlay()
        {
            _labelCursor = 0;

            if (_owner.ShowLabels) DrawBodyLabels();
            DrawHover();
            DrawTrajectoryLabels();

            // Only the labels this frame did not use are switched off, so an
            // unchanged label is not deactivated and reactivated every frame.
            for (int i = _labelCursor; i < _labels.Count; i++)
            {
                GameObject label = _labels[i].Text.gameObject;
                if (label.activeSelf) label.SetActive(false);
            }

            if (!_crosshair.activeSelf) _crosshair.SetActive(true);
        }

        private void DrawBodyLabels()
        {
            Camera camera = _owner.ViewCamera;
            if (camera == null || _owner.Renderer == null) return;

            Vector3d shipPosition = _owner.ShipPositionM;
            float blendWeight = _owner.Rig != null ? _owner.Rig.MapBlendWeight : 0f;
            float cameraDistance = _owner.Rig != null ? _owner.Rig.Orbit.DistanceUnits : 0f;
            bool mapSide = _owner.Rig != null && _owner.Rig.IsMapSide;

            foreach (BodyView view in _owner.Renderer.Views)
            {
                BodyRuntime body = view.Body;
                double distance = Vector3d.Distance(body.PositionM, shipPosition);
                bool hovered = body == _hover;
                bool selected = body == _owner.Selected;
                if (!BodyLabels.ShouldLabel(body, distance, cameraDistance, blendWeight,
                        hovered, selected, _owner.UnitsPerMeter))
                    continue;

                Vector3 screen = camera.WorldToScreenPoint(view.transform.position);
                if (screen.z <= 0f) continue;
                if (screen.x < -40f || screen.x > Screen.width + 40f ||
                    screen.y < -40f || screen.y > Screen.height + 40f) continue;

                string text = body.Name + "   " + FormatDistance(distance);
                PlaceLabel(text, selected ? SelectedLabelColour : LabelColour,
                    new Vector2(screen.x, screen.y), 9f, 9f);
            }

            if (BodyLabels.ShouldLabelShip(mapSide))
                DrawWorldLabel(camera, Vector3.zero, "YOU ARE HERE", ShipLabelColour,
                    hideAtOrigin: false);
        }

        private void DrawTrajectoryLabels()
        {
            TrajectoryLine trajectory = _owner.Trajectory;
            Camera camera = _owner.ViewCamera;
            if (trajectory == null || camera == null || !trajectory.IsShowing) return;

            if (trajectory.BurnMarkerCount > 0)
                DrawWorldLabel(camera, trajectory.BurnMarkerPosition(0),
                    "PERIAPSIS BURN", BurnLabelColour);
            if (trajectory.BurnMarkerCount > 1)
                DrawWorldLabel(camera, trajectory.BurnMarkerPosition(1),
                    "CIRCULARISE", ParkBurnLabelColour);

            DrawWorldLabel(camera, trajectory.FlipMarkerPosition, "FLIP  thrust reversal",
                FlipLabelColour);
            if (trajectory.HasGhost)
            {
                DrawWorldLabel(camera, trajectory.GhostMarkerPosition,
                    "TARGET AT ARRIVAL", GhostLabelColour);
            }
            else
            {
                DrawWorldLabel(camera, trajectory.ArrivalMarkerPosition,
                    "ARRIVAL  " + (_owner.Flight?.TargetId ?? "").ToUpperInvariant(),
                    ArrivalLabelColour);
            }
        }

        private void DrawWorldLabel(Camera camera, Vector3 position, string text, Color colour,
            bool hideAtOrigin = true)
        {
            if (camera == null) return;
            if (hideAtOrigin && position == Vector3.zero) return;

            Vector3 screen = camera.WorldToScreenPoint(position);
            if (screen.z <= 0f) return;

            PlaceLabel(text, colour, new Vector2(screen.x, screen.y), 9f, 9f);
        }

        private void DrawHover()
        {
            if (_hover == null) return;

            // A lone selected body has nothing more to say; a stack still
            // advertises that another click will reach the next object.
            if (_hover == _owner.Selected && _hoverHitCount <= 1) return;

            string prompt = _hover == _owner.Selected
                ? _hoverHitCount + " here — click again to cycle"
                : "LMB  " + _hover.Name;

            if (_hover != _owner.Selected && _hoverHitCount > 1)
                prompt += "   ·   " + _hoverHitCount + " here — click again to cycle";

            // The prompt sits just below and right of the pointer, and is placed
            // from the bottom-left screen point so it does not depend on IMGUI.
            PlaceLabel(prompt, BodyColour(_hover),
                PlayerControls.PointerPosition, 14f, -12f);
        }

        private void PlaceLabel(string text, Color colour, Vector2 screenPoint, float dx, float dy)
        {
            if (_labelCursor >= _labels.Count)
                _labels.Add(CreateLabel(_overlay, "Label" + _labels.Count));

            LabelView label = _labels[_labelCursor++];
            if (!label.Text.gameObject.activeSelf) label.Text.gameObject.SetActive(true);
            label.Text.text = text;
            label.Text.color = colour;

            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            label.Rect.anchoredPosition =
                new Vector2(screenPoint.x + dx, screenPoint.y + dy) / scale;
        }

        private static Color BodyColour(BodyRuntime body)
        {
            if (body?.Data?.color == null || body.Data.color.Length < 3)
                return FallbackAccent;
            return new Color(body.Data.color[0], body.Data.color[1], body.Data.color[2]);
        }

        // --- Construction -------------------------------------------------------

        private void BuildUi()
        {
            if (_built) return;
            _built = true;

            _font = LoadFont();

            _canvasObject = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasObject.transform.SetParent(transform, false);

            _canvas = _canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            _canvasRect = (RectTransform)_canvasObject.transform;

            CanvasScaler scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EventSystemLink();

            _overlay = CreateRect("Overlay", _canvasRect, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            BuildStatusPanel();
            BuildDetailsPanel();
            BuildFlightPanel();
            BuildLegendPanel();

            for (int i = 0; i < InitialLabelPool; i++)
                _labels.Add(CreateLabel(_overlay, "Label" + i));

            _crosshair = CreateImage(_canvasRect, "Crosshair", CrosshairColour,
                new Vector2(4f, 4f)).gameObject;
            RectTransform crosshairRect = (RectTransform)_crosshair.transform;
            crosshairRect.anchorMin = new Vector2(0.5f, 0.5f);
            crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
            crosshairRect.pivot = new Vector2(0.5f, 0.5f);
            crosshairRect.anchoredPosition = Vector2.zero;
            crosshairRect.sizeDelta = new Vector2(4f, 4f);

            Refresh();
        }

        private void EventSystemLink()
        {
            var system = UiInput.EnsureEventSystem(out bool created);
            _eventSystemObject = created ? system.gameObject : null;
        }

        private void BuildStatusPanel()
        {
            _statusPanel = CreatePanel("StatusPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -12f), new Vector2(PanelWidth, StatusPanelHeight),
                StatusBackground);
            _statusRect = (RectTransform)_statusPanel.transform;

            Transform parent = _statusPanel.transform;
            const float left = 14f;
            const float width = PanelWidth - 28f;

            Text title = CreateText(parent, "StatusTitle", "TIME TO EXPLORE  /  SOL", 18,
                TitleColour, FontStyle.Bold);
            PlaceTopLeft(title, left, 10f, width, 24f);

            _statusClock = CreateText(parent, "StatusClock", "", 12, TextColour);
            PlaceTopLeft(_statusClock, left, 38f, width, 18f);

            _statusDistance = CreateText(parent, "StatusDistance", "", 12, TextColour);
            PlaceTopLeft(_statusDistance, left, 57f, width, 18f);

            _statusNearest = CreateText(parent, "StatusNearest", "", 12, TextColour);
            PlaceTopLeft(_statusNearest, left, 76f, width, 18f);

            _statusWarpNote = CreateText(parent, "StatusWarpNote", "", 11, DimColour);
            PlaceTopLeft(_statusWarpNote, left, 98f, width, 16f);

            _statusTarget = CreateText(parent, "StatusTarget", "", 12, TextColour);
            PlaceTopLeft(_statusTarget, left, 98f, width, 18f);

            _statusTargetOrbit = CreateText(parent, "StatusTargetOrbit", "", 11, DimColour);
            PlaceTopLeft(_statusTargetOrbit, left, 116f, width, 18f);

            _statusHelp = CreateText(parent, "StatusHelp", HelpText, 11, DimColour);
            PlaceTopLeft(_statusHelp, left, 138f, width, 58f);
        }

        private void BuildDetailsPanel()
        {
            _detailsPanel = CreatePanel("DetailsPanel", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-Margin, -Margin),
                new Vector2(DetailsWidth, DetailsHeaderHeight + DetailsFooterHeight),
                DetailsBackground);
            _detailsRect = (RectTransform)_detailsPanel.transform;

            Transform parent = _detailsPanel.transform;
            _detailsAccent = CreateAccentBar(parent, "DetailsAccent");

            _detailsDivider = CreateImage(parent, "DetailsDivider",
                new Color(0.55f, 0.75f, 1f, 0.28f), Vector2.zero);
            RectTransform divider = (RectTransform)_detailsDivider.transform;
            divider.anchorMin = new Vector2(0f, 1f);
            divider.anchorMax = new Vector2(1f, 1f);
            divider.pivot = new Vector2(0f, 1f);
            divider.anchoredPosition = new Vector2(4f, -93f);
            divider.sizeDelta = new Vector2(-4f, 1f);

            _detailsTitle = CreateText(parent, "DetailsTitle", "", 22, PanelTitleColour,
                FontStyle.Bold);
            PlaceTopLeft(_detailsTitle, 18f, 10f, DetailsWidth - 36f - 54f, 28f);

            _detailsKind = CreateText(parent, "DetailsKind", "", 10, SectionColour,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform kindRect = _detailsKind.rectTransform;
            kindRect.anchorMin = new Vector2(1f, 1f);
            kindRect.anchorMax = new Vector2(1f, 1f);
            kindRect.pivot = new Vector2(1f, 1f);
            kindRect.anchoredPosition = new Vector2(-16f, -17f);
            kindRect.sizeDelta = new Vector2(74f, 18f);

            _detailsSubtitle = CreateText(parent, "DetailsSubtitle", "", 11, DimColour);
            PlaceTopLeft(_detailsSubtitle, 18f, 41f, DetailsWidth - 36f, 17f);

            _detailsDescription = CreateText(parent, "DetailsDescription", "", 11, WrapColour,
                FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            PlaceTopLeft(_detailsDescription, 18f, 61f, DetailsWidth - 36f, 30f);

            for (int i = 0; i < MaxDetailsRows; i++)
                _detailsRows.Add(CreateRow(parent, "DetailsRow" + i));

            _routeButton = CreateButton(parent, "RouteButton", "");
            _routeLabel = _routeButton.GetComponentInChildren<Text>(true);
            _routeButton.onClick.AddListener(OnRouteClicked);

            _beginButton = CreateButton(parent, "BeginButton", "Begin flight");
            _beginButton.onClick.AddListener(OnBeginClicked);

            _closeButton = CreateButton(parent, "CloseButton", "Close");
            _closeButton.onClick.AddListener(OnCloseClicked);

            _jumpButton = CreateButton(parent, "JumpButton", "Jump (debug)");
            _jumpButton.onClick.AddListener(OnJumpClicked);

            BuildTimeSlider(parent);

            _detailsPanel.SetActive(false);
        }

        private void BuildFlightPanel()
        {
            _flightPanel = CreatePanel("FlightPanel", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 12f), new Vector2(PanelWidth, FlightHeaderHeight + 220f),
                FlightBackground);
            _flightRect = (RectTransform)_flightPanel.transform;

            Transform parent = _flightPanel.transform;
            _flightAccent = CreateAccentBar(parent, "FlightAccent");

            _flightTitle = CreateText(parent, "FlightTitle", "", 18, TitleColour,
                FontStyle.Bold);
            PlaceTopLeft(_flightTitle, 16f, 8f, PanelWidth - 32f - 120f, 20f);

            _flightStatus = CreateText(parent, "FlightStatus", "", 10, SectionColour,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform statusRect = _flightStatus.rectTransform;
            statusRect.anchorMin = new Vector2(1f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(1f, 1f);
            statusRect.anchoredPosition = new Vector2(-16f, -12f);
            statusRect.sizeDelta = new Vector2(134f, 16f);

            _flightNext = CreateText(parent, "FlightNext", "", 12, TextColour);
            PlaceTopLeft(_flightNext, 16f, 32f, PanelWidth - 32f, 20f);

            for (int i = 0; i < MaxFlightRows; i++)
                _flightRows.Add(CreateRow(parent, "FlightRow" + i));

            _flightPanel.SetActive(false);
        }

        /// <summary>
        /// The route legend: a small bottom-left panel of colour swatches and
        /// arc labels. Built once with a fixed row pool; <see cref="RefreshLegend"/>
        /// fills only the rows the visible plan needs. A solid arc is one bar, a
        /// coast is three short dashes, so the swatch carries the line style as
        /// well as the colour.
        /// </summary>
        private void BuildLegendPanel()
        {
            _legendPanel = CreatePanel("RouteLegend", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 12f), new Vector2(LegendWidth, LegendHeaderHeight + 6f),
                FlightBackground);
            _legendPanel.GetComponent<Image>().raycastTarget = false;
            _legendRect = (RectTransform)_legendPanel.transform;

            Transform parent = _legendPanel.transform;
            _legendTitle = CreateText(parent, "RouteLegendTitle", "ROUTE", 10, SectionColour,
                FontStyle.Bold);
            PlaceTopLeft(_legendTitle, 12f, 7f, LegendWidth - 24f, 16f);

            for (int i = 0; i < MaxLegendRows; i++)
            {
                float y = LegendHeaderHeight + i * LegendRowHeight;

                Image solid = CreateImage(parent, "RouteLegendSolid" + i, Color.white,
                    new Vector2(LegendSwatchWidth, LegendSwatchHeight));
                PlaceTopLeft(solid, 12f, y + 6f, LegendSwatchWidth, LegendSwatchHeight);

                var dashes = new Image[3];
                for (int d = 0; d < dashes.Length; d++)
                {
                    dashes[d] = CreateImage(parent, "RouteLegendDash" + i + "_" + d,
                        Color.white, new Vector2(5f, LegendSwatchHeight));
                    PlaceTopLeft(dashes[d], 12f + d * 9f, y + 6f, 5f, LegendSwatchHeight);
                }

                Text label = CreateText(parent, "RouteLegendLabel" + i, "", 11, RowValueColour,
                    FontStyle.Normal, TextAnchor.MiddleLeft);
                PlaceTopLeft(label, 12f + LegendSwatchWidth + 10f, y,
                    LegendWidth - 24f - LegendSwatchWidth - 10f, 16f);

                _legendRows.Add(new LegendRow { Solid = solid, Dashes = dashes, Label = label });
                solid.gameObject.SetActive(false);
                foreach (Image dash in dashes) dash.gameObject.SetActive(false);
                label.gameObject.SetActive(false);
            }

            _legendPanel.SetActive(false);
        }

        /// <summary>
        /// The footer controls: the trip-time slider, the cruise-profile buttons,
        /// the three arrival-style buttons, and the fixed-burn level slider. The
        /// profile and the style are modes, not slider values, because each
        /// model trades time, delta-v, and park radius differently; the level is
        /// a continuous property of the fixed-thrust style and of the
        /// burn–coast–burn profile's two burns
        /// ([ADR-014](../../../Docs/decisions/adr-014-keplerian-coast.md),
        /// [ADR-015](../../../Docs/decisions/adr-015-fixed-thrust-burns.md)).
        /// </summary>
        private void BuildTimeSlider(Transform parent)
        {
            _timeSliderGroup = CreateRect("TimeSliderGroup", parent, new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                new Vector2(DetailsWidth - 36f, DetailsSliderHeight));

            Text profileLabel = CreateText(_timeSliderGroup, "CruiseProfileLabel",
                "PROFILE", 10, SectionColour, FontStyle.Bold);
            PlaceTopLeft(profileLabel, 0f, 0f, 62f, 20f);

            _poweredButton = CreateButton(_timeSliderGroup, "PoweredProfileButton",
                "Powered");
            PlaceTopLeft(_poweredButton.image, 62f, 0f, 84f, 22f);
            _poweredLabel = _poweredButton.GetComponentInChildren<Text>(true);
            _poweredButton.onClick.AddListener(OnPoweredProfileClicked);

            _coastButton = CreateButton(_timeSliderGroup, "CoastProfileButton", "Coast");
            PlaceTopLeft(_coastButton.image, 148f, 0f, 68f, 22f);
            _coastLabel = _coastButton.GetComponentInChildren<Text>(true);
            _coastButton.onClick.AddListener(OnCoastProfileClicked);

            _burnCoastBurnButton = CreateButton(_timeSliderGroup,
                "BurnCoastBurnProfileButton", "Burn-coast-burn");
            PlaceTopLeft(_burnCoastBurnButton.image, 218f, 0f, 138f, 22f);
            _burnCoastBurnLabel = _burnCoastBurnButton.GetComponentInChildren<Text>(true);
            _burnCoastBurnButton.onClick.AddListener(OnBurnCoastBurnProfileClicked);

            Text arrivalLabel = CreateText(_timeSliderGroup, "ArrivalStyleLabel", "ARRIVAL", 10,
                SectionColour, FontStyle.Bold);
            PlaceTopLeft(arrivalLabel, 0f, 30f, 76f, 20f);

            _quinticButton = CreateButton(_timeSliderGroup, "QuinticCaptureButton",
                "Quintic");
            PlaceTopLeft(_quinticButton.image, 76f, 30f, 80f, 22f);
            _quinticLabel = _quinticButton.GetComponentInChildren<Text>(true);
            _quinticButton.onClick.AddListener(OnQuinticCaptureClicked);

            _conicButton = CreateButton(_timeSliderGroup, "ConicCaptureButton",
                "Conic");
            PlaceTopLeft(_conicButton.image, 158f, 30f, 66f, 22f);
            _conicLabel = _conicButton.GetComponentInChildren<Text>(true);
            _conicButton.onClick.AddListener(OnConicCaptureClicked);

            _fixedBurnButton = CreateButton(_timeSliderGroup, "FixedBurnButton",
                "0.3 g burn");
            PlaceTopLeft(_fixedBurnButton.image, 226f, 30f, 130f, 22f);
            _fixedBurnLabel = _fixedBurnButton.GetComponentInChildren<Text>(true);
            _fixedBurnButton.onClick.AddListener(OnFixedBurnClicked);

            _timeLabel = CreateText(_timeSliderGroup, "TimeSliderLabel", "TRIP TIME", 10,
                SectionColour, FontStyle.Bold);
            PlaceTopLeft(_timeLabel, 0f, 60f, 190f, 16f);

            _timeValue = CreateText(_timeSliderGroup, "TimeSliderValue", "", 11, TextColour,
                FontStyle.Normal, TextAnchor.MiddleRight);
            PlaceTopLeft(_timeValue, DetailsWidth - 36f - 210f, 60f, 210f, 16f);

            _timeSlider = BuildSlider(_timeSliderGroup, "TimeSlider", 82f,
                OnTimeSliderChanged);

            _burnLevelLabel = CreateText(_timeSliderGroup, "BurnLevelLabel",
                "BURN LEVEL", 10, SectionColour, FontStyle.Bold);
            PlaceTopLeft(_burnLevelLabel, 0f, 108f, 160f, 16f);

            _burnLevelValue = CreateText(_timeSliderGroup, "BurnLevelValue", "", 11,
                TextColour, FontStyle.Normal, TextAnchor.MiddleRight);
            PlaceTopLeft(_burnLevelValue, DetailsWidth - 36f - 230f, 108f, 230f, 16f);

            _burnLevelSlider = BuildSlider(_timeSliderGroup, "BurnLevelSlider", 130f,
                OnBurnLevelChanged);
            _burnLevelSlider.minValue = 0f;
            _burnLevelSlider.maxValue = (float)TripPlanner.MaxBurnLevelG;
            _burnLevelSlider.value = (float)TripPlanner.DefaultBurnLevelG;

            _timeSliderGroup.gameObject.SetActive(false);
        }

        /// <summary>A stock uGUI slider in the details footer. The fill and
        /// handle geometry follows Unity's own Slider layout, because
        /// <see cref="Slider"/> drives those anchors itself; the background is
        /// the raycast target so a click anywhere on the track starts a drag.</summary>
        private Slider BuildSlider(Transform parent, string name, float y,
            UnityEngine.Events.UnityAction<float> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            PlaceTopLeft((RectTransform)go.transform, 0f, y, DetailsWidth - 36f, 18f);

            Image background = CreateImage(go.transform, "Background",
                new Color(0.10f, 0.15f, 0.22f, 0.95f), Vector2.zero);
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(-16f, 4f);
            background.raycastTarget = true;

            RectTransform fillArea = CreateRect("Fill Area", go.transform,
                new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            fillArea.offsetMin = new Vector2(8f, 0f);
            fillArea.offsetMax = new Vector2(-8f, 0f);

            Image fill = CreateImage(fillArea, "Fill",
                new Color(0.34f, 0.61f, 0.92f, 1f), Vector2.zero);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            RectTransform handleArea = CreateRect("Handle Slide Area", go.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.zero);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            Image handle = CreateImage(handleArea, "Handle",
                new Color(0.86f, 0.93f, 1f, 1f), Vector2.zero);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(12f, 0f);
            handle.raycastTarget = true;

            Slider slider = go.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.None;
            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        // --- Widget helpers -----------------------------------------------------

        private static Font LoadFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) return font;

            return Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Helvetica", "Liberation Sans" }, 12);
        }

        private GameObject CreatePanel(string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(_canvasRect, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = true;
            return go;
        }

        private static Image CreateImage(Transform parent, string name, Color colour,
            Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;

            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return image;
        }

        private static Image CreateAccentBar(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = FallbackAccent;
            image.raycastTarget = false;

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(4f, 0f);
            return image;
        }

        private static RectTransform CreateRect(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position,
            Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private Text CreateText(Transform parent, string name, string value, int fontSize,
            Color colour, FontStyle style = FontStyle.Normal,
            TextAnchor alignment = TextAnchor.UpperLeft, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = colour;
            text.fontStyle = style;
            text.alignment = alignment;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = wrap
                ? HorizontalWrapMode.Wrap
                : HorizontalWrapMode.Overflow;
            text.verticalOverflow = wrap
                ? VerticalWrapMode.Truncate
                : VerticalWrapMode.Overflow;
            return text;
        }

        private RowView CreateRow(Transform parent, string name)
        {
            return new RowView
            {
                Heading = CreateText(parent, name + "Heading", "", 10, SectionColour,
                    FontStyle.Bold),
                Label = CreateText(parent, name + "Label", "", 11, RowLabelColour),
                Value = CreateText(parent, name + "Value", "", 11, RowValueColour)
            };
        }

        private LabelView CreateLabel(Transform parent, string name)
        {
            Text text = CreateText(parent, name, "", 11, Color.white, FontStyle.Normal,
                TextAnchor.MiddleLeft);

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(250f, 18f);

            Shadow shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
            shadow.useGraphicAlpha = true;

            text.gameObject.SetActive(false);
            return new LabelView { Text = text, Rect = rect };
        }

        private Button CreateButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colours = button.colors;
            colours.normalColor = new Color(0.16f, 0.23f, 0.34f, 0.95f);
            colours.highlightedColor = new Color(0.24f, 0.34f, 0.47f, 0.98f);
            colours.pressedColor = new Color(0.11f, 0.16f, 0.24f, 1f);
            colours.selectedColor = colours.normalColor;
            colours.disabledColor = new Color(0.12f, 0.14f, 0.18f, 0.45f);
            colours.colorMultiplier = 1f;
            colours.fadeDuration = 0.05f;
            button.colors = colours;

            Text text = CreateText(go.transform, "Label", label, 12, ButtonTextColour,
                FontStyle.Normal, TextAnchor.MiddleCenter);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 4f);
            rect.offsetMax = new Vector2(-8f, -4f);

            return button;
        }

        private static void PlaceTopLeft(Graphic graphic, float x, float y, float width,
            float height)
        {
            PlaceTopLeft(graphic.rectTransform, x, y, width, height);
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        // --- Formatting ---------------------------------------------------------

        public static string FormatDistance(double metres)
        {
            if (metres < 1_000_000_000.0)
                return $"{metres / 1000.0:N0} km";
            if (metres < 1.0e10)
                return $"{metres / 1.0e9:N2} million km";
            return $"{metres / 1.495978707e11:N4} AU";
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 3600.0) return $"{seconds / 60.0:N1} min";
            if (seconds < 172800.0) return $"{seconds / 3600.0:N1} h";
            if (seconds < 3.156e7) return $"{seconds / 86400.0:N1} d";
            return $"{seconds / 3.15576e7:N2} y";
        }

        public static string FormatSpeed(double metresPerSecond)
        {
            if (metresPerSecond < 1000.0)
                return $"{metresPerSecond:N1} m/s";
            return $"{metresPerSecond / 1000.0:N0} km/s";
        }

        // --- Small types --------------------------------------------------------

        private sealed class RowView
        {
            public Text Heading;
            public Text Label;
            public Text Value;
        }

        private sealed class LabelView
        {
            public Text Text;
            public RectTransform Rect;
        }

        private sealed class LegendRow
        {
            public Image Solid;
            public Image[] Dashes;
            public Text Label;
        }
    }
}
