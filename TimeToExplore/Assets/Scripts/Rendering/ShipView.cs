using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// A placeholder ship at the render origin: a small capsule kept at a
    /// constant apparent size, pointing along the thrust while the drive is on
    /// and along the direction of travel when it is off. It gives the orbit
    /// camera something to orbit and makes the burn direction visible.
    ///
    /// A second marker, the thrust vector, draws an arrow from the ship along
    /// the commanded acceleration. The capsule already turns with the thrust,
    /// but an arrow makes the direction readable against the route and the
    /// ship's actual velocity, which can point somewhere else during a brake.
    /// The arrow is a fixed apparent length: it says "thrusting this way", not
    /// "thrusting this hard". It is hidden whenever the drive is off.
    ///
    /// This is a marker, not the ship model. The ship is a point with a state;
    /// the art pass replaces the capsule.
    /// </summary>
    public class ShipView : MonoBehaviour
    {
        /// <summary>Apparent height of the marker, in pixels.</summary>
        public const float MarkerPixels = 10f;

        /// <summary>Apparent length of the thrust arrow, in pixels.</summary>
        public const float ArrowPixels = 52f;

        /// <summary>How far the arrow's tail sits from the ship centre, as a
        /// fraction of its length, so the arrow clears the marker.</summary>
        public const float ArrowStandoffFraction = 0.12f;

        /// <summary>Thrust below this acceleration is treated as off. It is the
        /// same threshold the marker's own orientation uses.</summary>
        public const double MinimumThrustMps2 = 1e-3;

        /// <summary>The arrow's colour: a bright green, distinct from the gold,
        /// blue, and red route arcs. Authored in sRGB; a material colour
        /// property converts to linear on submit.</summary>
        public static readonly Color ArrowColour = new Color(0.52f, 1f, 0.62f);

        private Transform _visual;
        private Renderer _renderer;
        private Transform _arrow;
        private bool _thrusting;
        private bool _globallyVisible = true;

        /// <summary>Whether the thrust arrow is currently drawn, for tests.</summary>
        public bool ThrustArrowVisible => _arrow != null && _arrow.gameObject.activeSelf;

        /// <summary>The arrow's world direction, for tests and diagnostics.</summary>
        public Vector3 ThrustArrowDirection =>
            _arrow != null ? _arrow.forward : Vector3.zero;

        /// <summary>The arrow's render-space length, for tests and diagnostics.</summary>
        public float ThrustArrowLength => _arrow != null ? _arrow.lossyScale.z : 0f;

        public void Initialise()
        {
            GameObject visualObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visualObject.name = "Ship Marker";
            _visual = visualObject.transform;
            _visual.SetParent(transform, false);

            Collider collider = visualObject.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            _renderer = visualObject.GetComponent<Renderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            Shader shader = Shader.Find(RenderingShaders.Unlit);
            var material = new Material(shader) { name = "Ship Marker Material" };
            material.SetColor("_Color", new Color(0.86f, 0.90f, 0.96f));
            _renderer.sharedMaterial = material;

            CreateArrow(shader);
        }

        /// <summary>
        /// One unit-long arrow along local +Z: a slim shaft from the tail to the
        /// head, and a cone that comes to a point at +Z. Scaling the transform by
        /// the apparent length keeps the proportions fixed at every zoom. The
        /// faces are emitted twice, back to back, so the unlit shader can cull
        /// back faces without a winding mistake making the arrow disappear.
        /// </summary>
        private void CreateArrow(Shader shader)
        {
            GameObject arrowObject = new GameObject("Thrust Vector");
            _arrow = arrowObject.transform;
            _arrow.SetParent(transform, false);

            var filter = arrowObject.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildArrowMesh();

            var renderer = arrowObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var material = new Material(shader) { name = "Thrust Vector Material" };
            material.SetColor("_Color", ArrowColour);
            renderer.sharedMaterial = material;

            _arrow.gameObject.SetActive(false);
        }

        private static Mesh BuildArrowMesh()
        {
            const int sides = 12;
            const float shaftRadius = 0.045f;
            const float headRadius = 0.16f;
            const float shaftEnd = 0.68f;
            const float tip = 1f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            Vector3 Ring(int index, float radius, float z)
            {
                float angle = 2f * Mathf.PI * index / sides;
                return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;

                AddQuad(vertices, triangles,
                    Ring(i, shaftRadius, 0f), Ring(next, shaftRadius, 0f),
                    Ring(next, shaftRadius, shaftEnd), Ring(i, shaftRadius, shaftEnd));

                AddQuad(vertices, triangles,
                    Ring(i, shaftRadius, shaftEnd), Ring(i, headRadius, shaftEnd),
                    Ring(next, headRadius, shaftEnd), Ring(next, shaftRadius, shaftEnd));

                AddTriangle(vertices, triangles,
                    Ring(i, headRadius, shaftEnd), Ring(next, headRadius, shaftEnd),
                    new Vector3(0f, 0f, tip));

                AddTriangle(vertices, triangles,
                    Ring(next, shaftRadius, 0f), Ring(i, shaftRadius, 0f),
                    Vector3.zero);
            }

            var mesh = new Mesh { name = "Thrust Vector Mesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            AddTriangle(vertices, triangles, a, b, c);
            AddTriangle(vertices, triangles, a, c, d);
        }

        private static void AddTriangle(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);

            // The mirrored copy makes each face double-sided.
            triangles.Add(index + 2);
            triangles.Add(index + 1);
            triangles.Add(index);
        }

        public void Refresh(Camera camera, ThrustCommand thrust, Vector3d velocityMps)
        {
            if (_visual == null || camera == null) return;

            // The ship is the render origin, so the camera distance is the
            // length of its position.
            float distance = camera.transform.position.magnitude;
            float angular = camera.fieldOfView * Mathf.Deg2Rad * MarkerPixels
                            / Mathf.Max(Screen.height, 1);
            float size = Mathf.Max(distance * angular, 1e-7f);

            _visual.localScale = new Vector3(size * 0.35f, size * 0.5f, size * 0.35f);

            _thrusting = thrust.MagnitudeMps2 > MinimumThrustMps2;

            Vector3d forward = _thrusting ? thrust.Direction : velocityMps;
            if (forward.SqrMagnitude < 1e-12) return;

            Vector3 direction = forward.Normalized.ToUnity();
            _visual.localRotation = LookAlong(direction);

            RefreshArrow(camera, direction, distance);
        }

        private void RefreshArrow(Camera camera, Vector3 direction, float distance)
        {
            if (_arrow == null) return;

            bool visible = _globallyVisible && _thrusting;
            _arrow.gameObject.SetActive(visible);
            if (!visible) return;

            // One angular rule for the whole arrow, using the same viewport the
            // marker uses. The length is the arrow's full extent in render units;
            // the mesh is one unit long, so the scale is that length.
            float viewportHeight = camera != null && camera.pixelHeight > 0
                ? camera.pixelHeight
                : Screen.height;
            float length = LineWidth.WorldWidth(distance, camera != null ? camera.fieldOfView : 60f,
                viewportHeight, ArrowPixels);
            length = Mathf.Max(length, 1e-7f);

            _arrow.localPosition = direction * (length * ArrowStandoffFraction);
            _arrow.localRotation = LookAlong(direction);
            _arrow.localScale = Vector3.one * length;
        }

        private static Quaternion LookAlong(Vector3 direction)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.995f
                ? Vector3.forward
                : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }

        /// <summary>Hide the marker, e.g. when the view is inside a planet.</summary>
        public void SetVisible(bool visible)
        {
            _globallyVisible = visible;
            if (_renderer != null) _renderer.enabled = visible;
            if (_arrow != null) _arrow.gameObject.SetActive(visible && _thrusting);
        }
    }
}
