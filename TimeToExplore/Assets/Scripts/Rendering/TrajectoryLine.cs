using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Draws a Go To plan in the ship-relative render frame. Every arc is its
    /// own line with its own colour from <see cref="RouteColours"/>: solid for
    /// a powered arc or a fixed-thrust burn, dashed for a coast. Burn markers
    /// sit at each impulse or finite-burn start, with a marker at the flip and
    /// at the arrival point. The path is sampled once per plan and re-projected
    /// every frame, so it stays exact as the ship travels
    /// ([ADR-002](/decisions/adr-002-double-precision.md)).
    /// </summary>
    public class TrajectoryLine : MonoBehaviour
    {
        private const float MarkerPixels = 7f;

        private sealed class ArcLine
        {
            public LineRenderer Renderer;
            public Material Material;
            public readonly List<Vector3d> Points = new List<Vector3d>();
            public Color Colour;
            public bool Dashed;
            public string Label;
        }

        private readonly List<ArcLine> _lines = new List<ArcLine>();
        private readonly List<ArcLine> _activeLines = new List<ArcLine>();
        private readonly List<Vector3d> _burnPositionsM = new List<Vector3d>(2);

        private float _unitsPerMeter = 1e-6f;
        private Texture2D _dashTexture;
        private Transform _flipMarker;
        private Transform _arrivalMarker;
        private Transform _ghostMarker;
        private Transform[] _burnMarkers;
        private TrajectoryPlan _plan;
        private Vector3d _flipPositionM;
        private Vector3d _arrivalPositionM;
        private Vector3d _ghostPositionM;
        private bool _hasGhost;

        public bool IsShowing => _plan != null;

        /// <summary>Number of coloured arc lines on the current plan.</summary>
        public int ArcCount => _activeLines.Count;

        /// <summary>Colour of one arc line, for tests and diagnostics.</summary>
        public Color ArcColour(int index) =>
            index >= 0 && index < _activeLines.Count ? _activeLines[index].Colour : Color.clear;

        /// <summary>Whether one arc line is dashed, for tests and diagnostics.</summary>
        public bool ArcIsDashed(int index) =>
            index >= 0 && index < _activeLines.Count && _activeLines[index].Dashed;

        /// <summary>The legend label of one arc line, for the HUD.</summary>
        public string ArcLabel(int index) =>
            index >= 0 && index < _activeLines.Count ? _activeLines[index].Label : null;

        /// <summary>Render-space position of the flip marker, for a screen label.</summary>
        public Vector3 FlipMarkerPosition => _flipMarker != null ? _flipMarker.position : Vector3.zero;

        /// <summary>Render-space position of the arrival marker, for a screen label.</summary>
        public Vector3 ArrivalMarkerPosition => _arrivalMarker != null ? _arrivalMarker.position : Vector3.zero;

        /// <summary>Render-space position of the target at the arrival date, for
        /// the "where the planet will be" label. Zero unless one is drawn.</summary>
        public Vector3 GhostMarkerPosition => _ghostMarker != null ? _ghostMarker.position : Vector3.zero;

        public bool HasGhost => _hasGhost;

        /// <summary>Number of impulsive burn markers on the current plan.</summary>
        public int BurnMarkerCount =>
            _burnPositionsM == null || _burnMarkers == null
                ? 0
                : System.Math.Min(_burnPositionsM.Count, _burnMarkers.Length);

        /// <summary>Render-space position of a burn marker, for a screen label.</summary>
        public Vector3 BurnMarkerPosition(int index) =>
            _burnMarkers != null && index >= 0 && index < BurnMarkerCount
                ? _burnMarkers[index].position
                : Vector3.zero;

        public void Initialise(float unitsPerMeter)
        {
            _unitsPerMeter = unitsPerMeter;
            _dashTexture = CreateDashTexture();

            _flipMarker = CreateMarker("Flip", new Color(1f, 0.42f, 0.25f));
            _arrivalMarker = CreateMarker("Arrival", new Color(0.55f, 1f, 0.72f));
            _ghostMarker = CreateMarker("Arrival Ghost", new Color(0.62f, 0.78f, 1f));

            _burnMarkers = new[]
            {
                CreateMarker("Periapsis Burn", new Color(1f, 0.96f, 0.85f)),
                CreateMarker("Circularise Burn", new Color(1f, 0.88f, 0.35f))
            };

            Hide();
        }

        /// <summary>An 8-on, 8-off alpha strip, tiled along a dashed arc.</summary>
        private static Texture2D CreateDashTexture()
        {
            const int width = 16;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "Dash",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };

            var pixels = new Color32[width];
            for (int i = 0; i < width; i++)
                pixels[i] = i < width / 2 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private Transform CreateMarker(string name, Color colour)
        {
            GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = name + " Marker";
            markerObject.transform.SetParent(transform, false);

            Collider collider = markerObject.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            var renderer = markerObject.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Shader shader = Shader.Find(RenderingShaders.Unlit);
            var material = new Material(shader) { name = name + " Marker Material" };
            material.SetColor("_Color", colour);
            renderer.sharedMaterial = material;
            return markerObject.transform;
        }

        /// <summary>Sample a plan into one coloured polyline per arc.</summary>
        public void Show(TrajectoryPlan plan, IGravityField gravity) =>
            Show(plan, gravity, default, false);

        /// <summary>
        /// Sample a plan and mark where the target body will be on the arrival
        /// date. The ghost is the planet, not the ship's parking position, so
        /// the player sees where the route has to meet it.
        /// </summary>
        public void Show(TrajectoryPlan plan, IGravityField gravity, Vector3d targetArrivalPositionM)
            => Show(plan, gravity, targetArrivalPositionM, true);

        private void Show(TrajectoryPlan plan, IGravityField gravity,
            Vector3d targetArrivalPositionM, bool showGhost)
        {
            _plan = plan;
            ClearArcLines();
            _burnPositionsM.Clear();
            _hasGhost = false;

            if (plan == null)
            {
                Hide();
                return;
            }

            for (int i = 0; i < plan.Arcs.Count; i++)
            {
                PlanArc arc = plan.Arcs[i];
                RouteArcStyle style = RouteColours.For(plan.Arcs, i);
                ArcLine arcLine = AcquireLine(_activeLines.Count);
                ConfigureLine(arcLine, style);

                // Curvature-adaptive, not uniform in time. A conic is
                // anomaly-parameterised, so periapsis is sampled densely; a
                // quintic's turns are found by the sagitta test.
                PathTessellation.Append(arc.Path, arcLine.Points, includeStart: true);
                _activeLines.Add(arcLine);

                if (arc.EntryImpulseMps > 0.0 || arc.IsFiniteBurn)
                    _burnPositionsM.Add(arc.Path.PositionAt(0.0));
            }

            double flip = plan.FindFlipJulianDate(gravity);
            _flipPositionM = double.IsNaN(flip)
                ? Vector3d.Zero
                : plan.ReferencePositionAt(flip);
            _arrivalPositionM = plan.ArrivalState.PositionM;

            _ghostPositionM = targetArrivalPositionM;
            _hasGhost = showGhost;

            bool hasFlip = !double.IsNaN(flip);
            _flipMarker.gameObject.SetActive(hasFlip);
            _arrivalMarker.gameObject.SetActive(true);
            _ghostMarker.gameObject.SetActive(_hasGhost);
            for (int i = 0; i < _burnMarkers.Length; i++)
                _burnMarkers[i].gameObject.SetActive(i < _burnPositionsM.Count);

            // Refresh fills the buffers later in the same frame; hide the lines
            // until it does so a stale path is never drawn.
            for (int i = 0; i < _activeLines.Count; i++)
                _activeLines[i].Renderer.enabled = false;
        }

        /// <summary>Reuse a pooled line, or create one the first time the plan
        /// needs this many arcs.</summary>
        private ArcLine AcquireLine(int index)
        {
            while (_lines.Count <= index) _lines.Add(CreateLine(_lines.Count));

            ArcLine line = _lines[index];
            line.Points.Clear();
            return line;
        }

        private ArcLine CreateLine(int index)
        {
            GameObject lineObject = new GameObject("Plan Arc " + (index + 1) + " Line");
            lineObject.transform.SetParent(transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = 1f;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var material = new Material(Shader.Find(RenderingShaders.Line))
            {
                name = "Plan Arc " + (index + 1) + " Line Material"
            };
            line.material = material;

            return new ArcLine { Renderer = line, Material = material };
        }

        /// <summary>Apply an arc's colour and dash style to a pooled line.</summary>
        private void ConfigureLine(ArcLine arcLine, RouteArcStyle style)
        {
            arcLine.Colour = style.Colour;
            arcLine.Dashed = style.Dashed;
            arcLine.Label = style.Label;

            arcLine.Renderer.textureMode = style.Dashed
                ? LineTextureMode.Tile
                : LineTextureMode.Stretch;
            arcLine.Renderer.startColor = style.Colour;
            arcLine.Renderer.endColor = style.Colour;
            arcLine.Material.mainTexture = style.Dashed ? _dashTexture : null;
        }

        public void Hide()
        {
            _plan = null;
            ClearArcLines();
            _burnPositionsM.Clear();
            _hasGhost = false;

            if (_flipMarker != null) _flipMarker.gameObject.SetActive(false);
            if (_arrivalMarker != null) _arrivalMarker.gameObject.SetActive(false);
            if (_ghostMarker != null) _ghostMarker.gameObject.SetActive(false);
            if (_burnMarkers != null)
                foreach (Transform marker in _burnMarkers)
                    if (marker != null) marker.gameObject.SetActive(false);
        }

        private void ClearArcLines()
        {
            for (int i = 0; i < _activeLines.Count; i++)
            {
                ArcLine line = _activeLines[i];
                line.Points.Clear();
                if (line.Renderer != null) line.Renderer.enabled = false;
            }

            _activeLines.Clear();
        }

        public void Refresh(Vector3d shipPositionM, Camera camera)
        {
            if (_plan == null) return;

            // Width is apparent, not world-scaled: the same pixel rule as the
            // orbit lines. The depth is the camera's distance to the ship
            // (the render origin), which is where the route is anchored; on a
            // panned map that is the true distance to the route.
            float depth = camera != null
                ? Mathf.Max(camera.transform.position.magnitude, 1e-4f)
                : 1f;
            float width = LineWidth.WorldWidth(camera, depth);

            for (int i = 0; i < _activeLines.Count; i++)
            {
                ArcLine arcLine = _activeLines[i];
                SetPositions(arcLine.Renderer, arcLine.Points, shipPositionM, width);

                if (arcLine.Dashed)
                {
                    UpdateDashScale(arcLine.Material,
                        DashLengthUnits(arcLine.Points, shipPositionM),
                        Mathf.Max(width * 8f, 1e-6f));
                }
            }

            UpdateMarker(_flipMarker, _flipPositionM, shipPositionM, camera);
            UpdateMarker(_arrivalMarker, _arrivalPositionM, shipPositionM, camera);
            UpdateMarker(_ghostMarker, _ghostPositionM, shipPositionM, camera, 0.65f);

            for (int i = 0; i < BurnMarkerCount; i++)
                UpdateMarker(_burnMarkers[i], _burnPositionsM[i], shipPositionM, camera, 1.6f);
        }

        private void UpdateDashScale(Material material, float lengthUnits, float periodUnits)
        {
            if (material == null) return;
            float scale = Mathf.Max(1f, lengthUnits / periodUnits);
            material.mainTextureScale = new Vector2(scale, 1f);
        }

        private float DashLengthUnits(List<Vector3d> points, Vector3d shipPositionM)
        {
            double length = 0.0;
            for (int i = 1; i < points.Count; i++)
                length += Vector3d.Distance(points[i - 1], points[i]);
            return (float)(length * _unitsPerMeter);
        }

        private void UpdateMarker(Transform marker, Vector3d positionM, Vector3d shipPositionM,
            Camera camera, float scale = 1f)
        {
            if (marker == null || !marker.gameObject.activeSelf) return;

            Vector3 relative = ((positionM - shipPositionM) * _unitsPerMeter).ToUnity();
            marker.position = relative;

            // Size from the camera, not the ship, so a marker stays a marker
            // however far the orbit camera has been pulled back.
            float cameraDistance = camera != null
                ? Vector3.Distance(relative, camera.transform.position)
                : 1f;
            float pixels = camera != null
                ? camera.fieldOfView * Mathf.Deg2Rad * MarkerPixels / Mathf.Max(Screen.height, 1)
                : 0.01f;
            float size = Mathf.Max(cameraDistance * pixels * scale, 1e-6f);
            marker.localScale = Vector3.one * size;
        }

        private void SetPositions(LineRenderer line, List<Vector3d> points,
            Vector3d shipPositionM, float width = 0f)
        {
            if (line == null) return;

            bool visible = points.Count >= 2;
            line.enabled = visible;
            if (!visible) return;

            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3d relative = (points[i] - shipPositionM) * _unitsPerMeter;
                line.SetPosition(i, relative.ToUnity());
            }

            if (width > 0f) line.widthMultiplier = width;
        }
    }
}
