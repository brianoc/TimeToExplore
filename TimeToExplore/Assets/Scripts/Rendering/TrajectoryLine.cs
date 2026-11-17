using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Draws a Go To plan in the ship-relative render frame: a solid line for
    /// the departure and cruise arcs, a dashed line for the capture arc, and a
    /// marker at the flip and at the arrival point. The path is sampled once
    /// per plan and re-projected every frame, so it stays exact as the ship
    /// travels ([ADR-002](/decisions/adr-002-double-precision.md)).
    /// </summary>
    public class TrajectoryLine : MonoBehaviour
    {
        private const float MarkerPixels = 7f;

        private readonly List<Vector3d> _solid = new List<Vector3d>();
        private readonly List<Vector3d> _dashed = new List<Vector3d>();

        private float _unitsPerMeter = 1e-6f;
        private LineRenderer _solidLine;
        private LineRenderer _dashedLine;
        private Material _dashedMaterial;
        private Transform _flipMarker;
        private Transform _arrivalMarker;
        private Transform _ghostMarker;
        private TrajectoryPlan _plan;
        private Vector3d _flipPositionM;
        private Vector3d _arrivalPositionM;
        private Vector3d _ghostPositionM;
        private bool _hasGhost;

        public bool IsShowing => _plan != null;

        /// <summary>Render-space position of the flip marker, for a screen label.</summary>
        public Vector3 FlipMarkerPosition => _flipMarker != null ? _flipMarker.position : Vector3.zero;

        /// <summary>Render-space position of the arrival marker, for a screen label.</summary>
        public Vector3 ArrivalMarkerPosition => _arrivalMarker != null ? _arrivalMarker.position : Vector3.zero;

        /// <summary>Render-space position of the target at the arrival date, for
        /// the "where the planet will be" label. Zero unless one is drawn.</summary>
        public Vector3 GhostMarkerPosition => _ghostMarker != null ? _ghostMarker.position : Vector3.zero;

        public bool HasGhost => _hasGhost;

        public void Initialise(float unitsPerMeter)
        {
            _unitsPerMeter = unitsPerMeter;

            _solidLine = CreateLine("Plan", new Color(0.95f, 0.78f, 0.35f, 0.85f), false);
            _dashedLine = CreateLine("Capture", new Color(1f, 0.55f, 0.35f, 0.95f), true);

            _flipMarker = CreateMarker("Flip", new Color(1f, 0.42f, 0.25f));
            _arrivalMarker = CreateMarker("Arrival", new Color(0.55f, 1f, 0.72f));
            _ghostMarker = CreateMarker("Arrival Ghost", new Color(0.62f, 0.78f, 1f));

            Hide();
        }

        private LineRenderer CreateLine(string name, Color colour, bool dashed)
        {
            GameObject lineObject = new GameObject(name + " Line");
            lineObject.transform.SetParent(transform, false);

            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = 1f;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = dashed ? LineTextureMode.Tile : LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var material = new Material(Shader.Find(RenderingShaders.Line)) { name = name + " Line Material" };
            if (dashed) material.mainTexture = CreateDashTexture();
            line.material = material;
            line.startColor = colour;
            line.endColor = colour;

            if (dashed) _dashedMaterial = material;
            return line;
        }

        /// <summary>An 8-on, 8-off alpha strip, tiled along the capture line.</summary>
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

        /// <summary>Sample a plan into the solid and dashed polylines.</summary>
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
            _solid.Clear();
            _dashed.Clear();
            _hasGhost = false;

            if (plan == null)
            {
                Hide();
                return;
            }

            foreach (PlanArc arc in plan.Arcs)
            {
                List<Vector3d> target = arc.Phase == FlightPhase.Capture ? _dashed : _solid;

                // Curvature-adaptive, not uniform in time. A quintic is nearly
                // straight across the cruise and turns hard at the departure
                // and capture ends, so uniform samples spend most of their
                // budget on the easy part and leave visible chords in the
                // turns. A shared seam keeps the solid and dashed runs joined.
                PathTessellation.Append(arc.Path, target, includeStart: target.Count == 0);
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

            // Refresh fills the buffers later in the same frame; hide the lines
            // until it does so a stale path is never drawn.
            _solidLine.enabled = false;
            _dashedLine.enabled = false;
        }

        public void Hide()
        {
            _plan = null;
            _solid.Clear();
            _dashed.Clear();
            _hasGhost = false;

            if (_solidLine != null) _solidLine.enabled = false;
            if (_dashedLine != null) _dashedLine.enabled = false;
            if (_flipMarker != null) _flipMarker.gameObject.SetActive(false);
            if (_arrivalMarker != null) _arrivalMarker.gameObject.SetActive(false);
            if (_ghostMarker != null) _ghostMarker.gameObject.SetActive(false);
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

            SetPositions(_solidLine, _solid, shipPositionM, width);
            SetPositions(_dashedLine, _dashed, shipPositionM, width);

            UpdateDashScale(DashLengthUnits(_dashed, shipPositionM), Mathf.Max(width * 8f, 1e-6f));
            UpdateMarker(_flipMarker, _flipPositionM, shipPositionM, camera);
            UpdateMarker(_arrivalMarker, _arrivalPositionM, shipPositionM, camera);
            UpdateMarker(_ghostMarker, _ghostPositionM, shipPositionM, camera, 0.65f);
        }

        private void UpdateDashScale(float lengthUnits, float periodUnits)
        {
            if (_dashedMaterial == null) return;
            float scale = Mathf.Max(1f, lengthUnits / periodUnits);
            _dashedMaterial.mainTextureScale = new Vector2(scale, 1f);
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
