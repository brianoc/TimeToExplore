using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;

namespace TimeToExplore.UI
{
    /// <summary>
    /// Immediate-mode HUD for the prototype: status panel, flight panel, body
    /// labels, and the body details screen opened by left-clicking a body.
    /// </summary>
    public class SolarSystemHud : MonoBehaviour
    {
        private const float PanelWidth = 430f;
        private const float DetailsWidth = 392f;
        private const float Margin = 16f;

        private SolarSystemBootstrap _owner;
        private GUIStyle _title;
        private GUIStyle _text;
        private GUIStyle _dim;
        private GUIStyle _label;
        private GUIStyle _panelTitle;
        private GUIStyle _section;
        private GUIStyle _rowLabel;
        private GUIStyle _rowValue;
        private GUIStyle _wrap;
        private GUIStyle _button;
        private Texture2D _pixel;

        private bool _detailsOpen;
        private int _detailsOpenedFrame = -1;
        private Rect _hudRect = new Rect(12f, 12f, PanelWidth, 252f);
        private Rect _detailsRect;
        private Rect _flightRect;
        private BodyRuntime _hover;
        private readonly List<ScreenTarget> _targets = new List<ScreenTarget>();

        public void Initialise(SolarSystemBootstrap owner)
        {
            _owner = owner;
        }

        /// <summary>The details screen is open. Clicking a body opens it.</summary>
        public bool DetailsOpen => _detailsOpen;

        public void CloseDetails()
        {
            _detailsOpen = false;
            _owner?.DiscardPreview();
        }

        private void EnsureStyles()
        {
            if (_text != null) return;

            _pixel = Texture2D.whiteTexture;
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.93f, 1f) }
            };
            _text = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.88f, 0.90f, 0.94f) }
            };
            _dim = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.62f, 0.68f, 0.76f) }
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _panelTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.94f, 0.97f, 1f) }
            };
            _section = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.48f, 0.66f, 0.88f) }
            };
            _rowLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.62f, 0.68f, 0.76f) }
            };
            _rowValue = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.90f, 0.93f, 0.97f) }
            };
            _wrap = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.76f, 0.82f, 0.89f) }
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 4, 4)
            };
        }

        private void Update()
        {
            if (_owner == null || _owner.Model == null) return;

            if (Input.GetKeyDown(KeyCode.Escape)) CloseDetails();

            UpdatePointer();
        }

        private void UpdatePointer()
        {
            _hover = null;

            // With the cursor locked for mouse look there is no pointer to aim.
            if (Cursor.lockState != CursorLockMode.None) return;

            Vector2 pointer = GuiPointer();
            if (PointerOverPanel(pointer)) return;

            _hover = PickAt(pointer);

            if (!Input.GetMouseButtonDown(0) || _hover == null) return;

            _owner.Select(_hover);
            _detailsOpen = true;
            _detailsOpenedFrame = Time.frameCount;
        }

        private static Vector2 GuiPointer() =>
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        private bool PointerOverPanel(Vector2 pointer) =>
            _hudRect.Contains(pointer) ||
            (_detailsOpen && _detailsRect.Contains(pointer)) ||
            HasFlightPanel && _flightRect.Contains(pointer);

        private bool HasFlightPanel =>
            _owner?.Flight != null && (_owner.Flight.Plan != null || _owner.Flight.HasPreview);

        private BodyRuntime PickAt(Vector2 pointer)
        {
            Camera camera = _owner.ViewCamera;
            if (camera == null || _owner.Renderer == null) return null;

            _targets.Clear();
            foreach (BodyView view in _owner.Renderer.Views)
            {
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
                    Depth = world.magnitude,
                    Priority = BodyPicker.PriorityOf(view.Body)
                });
            }

            bool classPriority = _owner.Rig != null && _owner.Rig.IsMapSide;
            return BodyPicker.Pick(_targets, pointer, BodyPicker.MinimumHitRadiusPixels,
                classPriority);
        }

        private void OnGUI()
        {
            if (_owner == null || _owner.Model == null) return;
            EnsureStyles();

            if (_owner.ShowHud) DrawPanel();
            if (_owner.ShowLabels) DrawLabels();
            DrawHover();
            DrawCrosshair();
            if (_detailsOpen) DrawDetailsPanel();
            if (HasFlightPanel) DrawFlightPanel();
            DrawTrajectoryLabels();
        }

        private void DrawPanel()
        {
            const float height = 252f;
            Rect panel = new Rect(12f, 12f, PanelWidth, height);
            _hudRect = panel;

            GUI.color = new Color(0.01f, 0.02f, 0.04f, 0.82f);
            GUI.DrawTexture(panel, _pixel);
            GUI.color = Color.white;

            Vector3d shipPosition = _owner.ShipPositionM;
            BodyRuntime sun = _owner.Model.Sun;
            double sunDistance = Vector3d.Distance(shipPosition, sun.PositionM);
            BodyRuntime nearest = _owner.Model.FindNearest(shipPosition);
            BodyRuntime selected = _owner.Selected;

            float x = panel.x + 14f;
            float y = panel.y + 10f;

            GUI.Label(new Rect(x, y, PanelWidth - 28f, 24f), "TIME TO EXPLORE  /  SOL", _title);
            y += 28f;
            GUI.Label(new Rect(x, y, PanelWidth - 28f, 18f),
                JulianDate.Format(_owner.Clock.JulianDate) + "     " + _owner.Clock.Label, _text);
            y += 19f;
            GUI.Label(new Rect(x, y, PanelWidth - 28f, 18f),
                $"Heliocentric distance {FormatDistance(sunDistance)}     {ShipStatusText()}", _text);
            y += 19f;
            GUI.Label(new Rect(x, y, PanelWidth - 28f, 18f),
                nearest != null
                    ? $"Nearest body: {nearest.Name} ({FormatDistance(Vector3d.Distance(nearest.PositionM, shipPosition))})"
                    : "Nearest body: —",
                _text);
            y += 22f;

            if (!string.IsNullOrEmpty(_owner.WarpNote))
            {
                GUI.Label(new Rect(x, y, PanelWidth - 28f, 16f),
                    "warp held by the flight policy  ·  " + _owner.WarpNote, _dim);
                y += 16f;
            }

            if (selected != null)
            {
                string parent = selected.Parent != null ? selected.Parent.Name : "—";
                string period = selected.HasOrbit
                    ? FormatDuration(selected.Elements.PeriodSeconds)
                    : "—";
                GUI.Label(new Rect(x, y, PanelWidth - 28f, 18f),
                    $"Target: {selected.Name}   R {FormatDistance(selected.RadiusM)}   GM {selected.Gm:0.00E+00} m³/s²", _text);
                y += 18f;
                GUI.Label(new Rect(x, y, PanelWidth - 28f, 18f),
                    $"Orbits {parent}   sidereal period {period}   {BodyDetails.KindLabel(selected)}", _dim);
                y += 22f;
            }

            GUI.Label(new Rect(x, y, PanelWidth - 28f, 58f),
                "LMB select body · RMB drag orbit · wheel zoom · 1–7 warp · Space pause\n" +
                "Tab target · R reset to Gateway · Esc cancel · T true scale · O orbits\n" +
                "L labels · H HUD · ` debug free flight (WASD fly, , . speed) · X cancel flight",
                _dim);
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

        private void DrawDetailsPanel()
        {
            BodyRuntime body = _owner.Selected;
            if (body == null)
            {
                _detailsOpen = false;
                return;
            }

            List<DetailRow> rows = BodyDetails.Build(body, _owner.Model, _owner.ShipPositionM);

            float headingHeight = 22f;
            float rowHeight = 17f;
            float rowsHeight = 0f;
            foreach (DetailRow row in rows)
                rowsHeight += row.IsHeading ? headingHeight : rowHeight;

            const float headerHeight = 95f;
            const float footerHeight = 122f;
            float height = headerHeight + rowsHeight + footerHeight;
            Rect panel = new Rect(Screen.width - DetailsWidth - Margin, Margin, DetailsWidth, height);
            _detailsRect = panel;

            GUI.color = new Color(0.015f, 0.025f, 0.045f, 0.92f);
            GUI.DrawTexture(panel, _pixel);

            Color accent = BodyColour(body);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(panel.x, panel.y, 4f, panel.height), _pixel);

            GUI.color = new Color(accent.r, accent.g, accent.b, 0.28f);
            GUI.DrawTexture(new Rect(panel.x + 4f, panel.y + 93f, panel.width - 4f, 1f), _pixel);
            GUI.color = Color.white;

            float x = panel.x + 18f;
            float contentWidth = panel.width - 36f;
            float y = panel.y + 10f;

            GUI.Label(new Rect(x, y, contentWidth - 54f, 28f), body.Name, _panelTitle);
            string kind = BodyDetails.KindLabel(body);
            GUI.Label(new Rect(panel.x + panel.width - 90f, y + 7f, 74f, 18f), kind.ToUpperInvariant(), _section);
            y += 31f;

            GUI.Label(new Rect(x, y, contentWidth, 17f), BodyDetails.Subtitle(body, _owner.Model), _dim);
            y += 20f;

            if (!string.IsNullOrEmpty(body.Data.description))
            {
                GUI.Label(new Rect(x, y, contentWidth, 30f), body.Data.description, _wrap);
            }
            y += 34f;

            foreach (DetailRow row in rows)
            {
                if (row.IsHeading)
                {
                    GUI.Label(new Rect(x, y, contentWidth, 18f), row.Label, _section);
                }
                else
                {
                    GUI.Label(new Rect(x, y - 1f, 138f, 17f), row.Label, _rowLabel);
                    GUI.Label(new Rect(x + 140f, y - 1f, contentWidth - 140f, 17f), row.Value, _rowValue);
                }
                y += row.IsHeading ? headingHeight : rowHeight;
            }

            float buttonY = panel.y + panel.height - 112f;

            // The click that opened this panel must not also press a button.
            bool interactive = Time.frameCount != _detailsOpenedFrame;
            FlightSession flight = _owner.Flight;
            bool flying = flight != null && flight.IsFlying;
            bool rejected = flight != null &&
                            flight.Status == FlightStatus.Rejected &&
                            flight.TargetId == body.Id;
            bool hasPreview = flight != null && flight.HasPreview &&
                              flight.Preview.TargetId == body.Id;
            bool plottable = body.Gm > 0.0 && !rejected;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = interactive && plottable;

            string routeLabel;
            if (rejected) routeLabel = "Plan infeasible";
            else if (flying) routeLabel = $"Replan to {body.Name}";
            else if (hasPreview) routeLabel = $"Replot route to {body.Name}";
            else routeLabel = $"Plot route to {body.Name}";

            if (GUI.Button(new Rect(x, buttonY, contentWidth, 30f), routeLabel, _button))
            {
                if (flying) _owner.BeginGoTo(body);
                else _owner.PlotRoute(body);
            }

            GUI.enabled = interactive && plottable && !flying && hasPreview;
            if (GUI.Button(new Rect(x, buttonY + 36f, contentWidth - 106f, 28f),
                    "Begin flight", _button))
                _owner.BeginPlottedFlight();

            GUI.enabled = interactive;
            if (GUI.Button(new Rect(x + contentWidth - 98f, buttonY + 36f, 98f, 28f),
                    "Close", _button))
                CloseDetails();

            GUI.enabled = interactive && !flying;
            if (GUI.Button(new Rect(x, buttonY + 70f, contentWidth - 106f, 28f),
                    "Jump (debug)", _button))
                _owner.FocusSelected();

            GUI.enabled = previousEnabled;
            GUI.color = Color.white;
        }

        private void DrawFlightPanel()
        {
            FlightSession flight = _owner.Flight;
            double now = _owner.Clock.JulianDate;
            string warp = _owner.Clock.Label +
                          (string.IsNullOrEmpty(_owner.WarpNote) ? "" : "  (" + _owner.WarpNote + ")");

            List<DetailRow> rows = FlightReport.Build(flight, _owner.Model, warp, now);

            float headingHeight = 20f;
            float rowHeight = 17f;
            float rowsHeight = 0f;
            foreach (DetailRow row in rows)
                rowsHeight += row.IsHeading ? headingHeight : rowHeight;

            float height = 58f + rowsHeight;
            Rect panel = new Rect(12f, Screen.height - height - 12f, PanelWidth, height);
            _flightRect = panel;

            GUI.color = new Color(0.012f, 0.03f, 0.05f, 0.90f);
            GUI.DrawTexture(panel, _pixel);

            Color accent = FlightAccent(flight.Status);
            GUI.color = accent;
            GUI.DrawTexture(new Rect(panel.x, panel.y, 4f, panel.height), _pixel);
            GUI.color = Color.white;

            float x = panel.x + 16f;
            float contentWidth = panel.width - 32f;
            float y = panel.y + 8f;

            GUI.Label(new Rect(x, y, contentWidth - 120f, 20f),
                "FLIGHT  /  " +
                ((flight.Plan ?? flight.Preview)?.TargetId ?? "—").ToUpperInvariant(), _title);
            GUI.Label(new Rect(panel.x + panel.width - 150f, y + 4f, 134f, 16f),
                FlightReport.StatusLabel(flight, now).ToUpperInvariant(), _section);
            y += 24f;

            string next = flight.Plan != null
                ? FlightReport.NextEvent(flight)
                : FlightReport.PreviewNextEvent(flight, now);
            GUI.Label(new Rect(x, y, contentWidth, 20f), next, _text);
            y += 26f;

            foreach (DetailRow row in rows)
            {
                if (row.IsHeading)
                {
                    GUI.Label(new Rect(x, y, contentWidth, 16f), row.Label, _section);
                }
                else
                {
                    GUI.Label(new Rect(x, y - 1f, 118f, 17f), row.Label, _rowLabel);
                    GUI.Label(new Rect(x + 120f, y - 1f, contentWidth - 120f, 17f), row.Value, _rowValue);
                }
                y += row.IsHeading ? headingHeight : rowHeight;
            }

            GUI.color = Color.white;
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

        private void DrawTrajectoryLabels()
        {
            TrajectoryLine trajectory = _owner.Trajectory;
            Camera camera = _owner.ViewCamera;
            if (trajectory == null || camera == null || !trajectory.IsShowing) return;

            DrawWorldLabel(camera, trajectory.FlipMarkerPosition, "FLIP  thrust reversal",
                new Color(1f, 0.55f, 0.35f));
            if (trajectory.HasGhost)
            {
                DrawWorldLabel(camera, trajectory.GhostMarkerPosition,
                    "TARGET AT ARRIVAL", new Color(0.62f, 0.78f, 1f));
            }
            else
            {
                DrawWorldLabel(camera, trajectory.ArrivalMarkerPosition,
                    "ARRIVAL  " + (_owner.Flight?.TargetId ?? "").ToUpperInvariant(),
                    new Color(0.55f, 1f, 0.72f));
            }
        }

        private void DrawWorldLabel(Camera camera, Vector3 position, string text, Color colour,
            bool hideAtOrigin = true)
        {
            if (camera == null) return;
            if (hideAtOrigin && position == Vector3.zero) return;

            Vector3 screen = camera.WorldToScreenPoint(position);
            if (screen.z <= 0f) return;

            var rect = new Rect(screen.x + 9f, Screen.height - screen.y - 9f, 250f, 18f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _label);
            GUI.color = colour;
            GUI.Label(rect, text, _label);
            GUI.color = Color.white;
        }

        private static Color BodyColour(BodyRuntime body)
        {
            if (body?.Data?.color == null || body.Data.color.Length < 3)
                return new Color(0.55f, 0.75f, 1f);
            return new Color(body.Data.color[0], body.Data.color[1], body.Data.color[2]);
        }

        private void DrawHover()
        {
            if (_hover == null || _hover == _owner.Selected) return;

            Color accent = BodyColour(_hover);
            Vector2 pointer = GuiPointer();
            string text = "LMB  " + _hover.Name;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(pointer.x + 15f, pointer.y + 13f, 260f, 18f), text, _label);
            GUI.color = accent;
            GUI.Label(new Rect(pointer.x + 14f, pointer.y + 12f, 260f, 18f), text, _label);
            GUI.color = Color.white;
        }

        private void DrawLabels()
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
                Rect rect = new Rect(screen.x + 9f, Screen.height - screen.y - 9f, 250f, 18f);

                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _label);
                GUI.color = selected
                    ? new Color(1f, 0.85f, 0.4f)
                    : new Color(0.85f, 0.92f, 1f, 0.92f);
                GUI.Label(rect, text, _label);
            }

            if (BodyLabels.ShouldLabelShip(mapSide))
                DrawWorldLabel(camera, Vector3.zero, "YOU ARE HERE",
                    new Color(0.86f, 0.90f, 0.96f), hideAtOrigin: false);

            GUI.color = Color.white;
        }

        private void DrawCrosshair()
        {
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 2f, Screen.height * 0.5f - 2f, 4f, 4f), _pixel);
            GUI.color = Color.white;
        }

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
    }
}
