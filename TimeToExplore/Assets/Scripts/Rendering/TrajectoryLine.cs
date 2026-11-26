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
    /// sit at each impulse or finite-burn start, with a marker at the arrival
    /// point. The path is sampled once per plan and re-projected
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
            public readonly List<Vector3> RelativePoints = new List<Vector3>();
            public readonly List<Vector3> VisibleRun = new List<Vector3>();
            public readonly List<Vector3> VisiblePoints = new List<Vector3>();
            public Keyframe[] WidthKeys;
            public AnimationCurve WidthCurve;
            public Color Colour;
            public bool Dashed;
            public string Label;
        }

        private readonly List<ArcLine> _lines = new List<ArcLine>();
        private readonly List<ArcLine> _activeLines = new List<ArcLine>();
        private readonly List<Vector3d> _burnPositionsM = new List<Vector3d>(2);

        private float _unitsPerMeter = 1e-6f;
        private Texture2D _dashTexture;
        private Transform _arrivalMarker;
        private Transform _ghostMarker;
        private Transform[] _burnMarkers;
        private TrajectoryPlan _plan;
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

        /// <summary>
        /// World-space length of one dash cycle on an arc, recovered from the
        /// material's texture scale. Zero for a solid arc. Tests use it to
        /// prove the dash does not shrink as an arc gets longer.
        /// </summary>
        public float ArcDashPeriodUnits(int index)
        {
            if (index < 0 || index >= _activeLines.Count) return 0f;
            ArcLine line = _activeLines[index];
            if (!line.Dashed || line.Material == null) return 0f;
            float scale = line.Material.mainTextureScale.x;
            return scale > 0f ? 1f / scale : 0f;
        }

        /// <summary>The legend label of one arc line, for the HUD.</summary>
        public string ArcLabel(int index) =>
            index >= 0 && index < _activeLines.Count ? _activeLines[index].Label : null;

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

            _arrivalMarker = CreateMarker("Arrival", new Color(0.55f, 1f, 0.72f));
            _ghostMarker = CreateMarker("Arrival Ghost", new Color(0.62f, 0.78f, 1f));

            _burnMarkers = new[]
            {
                CreateMarker("Departure Burn", new Color(1f, 0.96f, 0.85f)),
                CreateMarker("Brake", new Color(1f, 0.88f, 0.35f)),
                CreateMarker("Capture Burn", new Color(1f, 0.80f, 0.55f)),
                CreateMarker("Trim Burn", new Color(1f, 0.80f, 0.55f)),
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
        public void Show(TrajectoryPlan plan) =>
            Show(plan, default, false);

        /// <summary>
        /// Sample a plan and mark where the target body will be on the arrival
        /// date. The ghost is the planet, not the ship's parking position, so
        /// the player sees where the route has to meet it.
        /// </summary>
        public void Show(TrajectoryPlan plan, Vector3d targetArrivalPositionM)
            => Show(plan, targetArrivalPositionM, true);

        private void Show(TrajectoryPlan plan, Vector3d targetArrivalPositionM,
            bool showGhost)
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

            _arrivalPositionM = plan.ArrivalState.PositionM;

            _ghostPositionM = targetArrivalPositionM;
            _hasGhost = showGhost;

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

            var arcLine = new ArcLine
            {
                Renderer = line,
                Material = material,
                WidthCurve = new AnimationCurve(new Keyframe(0f, 1f))
            };
            line.widthCurve = arcLine.WidthCurve;
            return arcLine;
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

            // A LineRenderer's vertex colour reaches the shader unconverted.
            // The route palette is authored in sRGB, so this is the boundary
            // where it becomes linear light.
            arcLine.Renderer.startColor = style.Colour.linear;
            arcLine.Renderer.endColor = style.Colour.linear;
            arcLine.Material.mainTexture = style.Dashed ? _dashTexture : null;
        }

        public void Hide()
        {
            _plan = null;
            ClearArcLines();
            _burnPositionsM.Clear();
            _hasGhost = false;

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
            // orbit lines. The base depth is the camera's distance to the ship
            // (the render origin), where the route is anchored. Each vertex
            // then gets its own width from its own depth, held between the
            // 2 px distance floor and the 3 px route width, so the far end
            // stays visible and a route that sweeps close to a zoomed-in
            // camera stays a line rather than a ribbon.
            float baseDepth = camera != null
                ? Mathf.Max(camera.transform.position.magnitude, 1e-4f)
                : 1f;
            float fieldOfView = camera != null ? camera.fieldOfView : 60f;
            int viewportHeight = camera != null && camera.pixelHeight > 0
                ? camera.pixelHeight
                : Screen.height;
            float baseWidth = LineWidth.WorldWidth(baseDepth, fieldOfView,
                viewportHeight, LineWidth.Pixels);

            for (int i = 0; i < _activeLines.Count; i++)
            {
                ArcLine arcLine = _activeLines[i];
                SetPositions(arcLine, shipPositionM, camera, baseWidth, baseDepth);

                if (arcLine.Dashed)
                    UpdateDashScale(arcLine.Material, DashPeriodUnits(baseWidth));
            }

            UpdateMarker(_arrivalMarker, _arrivalPositionM, shipPositionM, camera);
            UpdateMarker(_ghostMarker, _ghostPositionM, shipPositionM, camera, 0.65f);

            for (int i = 0; i < BurnMarkerCount; i++)
                UpdateMarker(_burnMarkers[i], _burnPositionsM[i], shipPositionM, camera, 1.6f);
        }

        /// <summary>
        /// A dash cycle in render units: eight line widths at the route's
        /// anchor depth, so a coast reads as a dashed line rather than a
        /// thread. Four of the eight are drawn and four are the gap.
        /// </summary>
        public static float DashPeriodUnits(float lineWidthUnits) =>
            Mathf.Max(lineWidthUnits * 8f, 1e-6f);

        /// <summary>
        /// Point the dash texture at a world-space period. Unity's
        /// <see cref="LineTextureMode.Tile"/> puts one texture repeat per world
        /// unit along the line, so the scale is the inverse of the period.
        ///
        /// The old rule also multiplied the scale by the arc's whole length, so
        /// the repeat count grew with the square of the length. A long coast
        /// drew thousands of sub-pixel dashes and point sampling turned them
        /// into a broken, shimmering smear instead of a dotted line. The period
        /// is now independent of the arc's length, so every dashed arc of the
        /// plan gets the same dash at the same depth.
        /// </summary>
        private void UpdateDashScale(Material material, float periodUnits)
        {
            if (material == null) return;
            material.mainTextureScale = new Vector2(1f / Mathf.Max(periodUnits, 1e-6f), 1f);
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

        private void SetPositions(ArcLine arcLine, Vector3d shipPositionM, Camera camera,
            float baseWidth, float baseDepth)
        {
            LineRenderer line = arcLine.Renderer;
            if (line == null) return;

            List<Vector3d> points = arcLine.Points;
            arcLine.RelativePoints.Clear();
            for (int i = 0; i < points.Count; i++)
                arcLine.RelativePoints.Add(((points[i] - shipPositionM) * _unitsPerMeter).ToUnity());

            // Drop the part of the route behind the camera. A line that crosses
            // the camera plane cannot be billboarded consistently, and Unity's
            // stretched quad is the screen-filling wedge the player saw when
            // zooming in past the route's near end.
            int count = LineCameraClip.Clip(arcLine.RelativePoints, camera,
                arcLine.VisibleRun, arcLine.VisiblePoints);
            bool visible = count >= 2;
            line.enabled = visible;
            if (!visible) return;

            line.positionCount = count;
            if (arcLine.WidthKeys == null || arcLine.WidthKeys.Length != count)
            {
                // One key per point, so each vertex carries its own screen
                // width. The array is reused while the point count is stable.
                arcLine.WidthKeys = new Keyframe[count];
            }

            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            float fieldOfView = camera != null ? camera.fieldOfView : 60f;
            int viewportHeight = camera != null && camera.pixelHeight > 0
                ? camera.pixelHeight
                : Screen.height;
            float denominator = Mathf.Max(count - 1, 1);

            for (int i = 0; i < count; i++)
            {
                Vector3 relative = arcLine.VisiblePoints[i];
                line.SetPosition(i, relative);

                float depth = camera != null
                    ? Vector3.Distance(relative, cameraPosition)
                    : baseDepth;
                arcLine.WidthKeys[i] = new Keyframe(i / denominator,
                    LineWidth.SampleWorldWidth(baseWidth, LineWidth.Pixels, depth,
                        fieldOfView, viewportHeight));
            }

            arcLine.WidthCurve.SetKeys(arcLine.WidthKeys);
            line.widthCurve = arcLine.WidthCurve;
        }
    }
}
