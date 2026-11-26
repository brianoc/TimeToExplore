using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Draws a body's Keplerian ellipse as a screen-width-aware line. Orbit
    /// points are recomputed in the ship-relative frame every frame so the
    /// line remains accurate as the ship travels.
    /// </summary>
    public class OrbitLine : MonoBehaviour
    {
        private const int Samples = 160;

        private LineRenderer _line;
        private BodyRuntime _body;
        private SolarSystemModel _model;
        private Vector3d[] _buffer;
        private readonly List<Vector3> _relative = new List<Vector3>();
        private readonly List<Vector3> _run = new List<Vector3>();
        private readonly List<Vector3> _visible = new List<Vector3>();
        private Keyframe[] _widthKeys;
        private AnimationCurve _widthCurve;

        public void Initialise(BodyRuntime body, SolarSystemModel model, Color colour, Transform parent)
        {
            _body = body;
            _model = model;
            _buffer = new Vector3d[Samples + 1];
            _widthKeys = new Keyframe[Samples + 1];

            transform.SetParent(parent, false);
            name = body.Name + " Orbit";

            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = false;
            _line.positionCount = Samples + 1;
            _line.widthMultiplier = 1f;
            _line.numCapVertices = 0;
            _line.numCornerVertices = 0;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            // One key per sampled vertex, so the width curve is the exact
            // per-vertex screen-width rule rather than an approximation.
            _widthCurve = new AnimationCurve(new Keyframe(0f, 1f));
            _line.widthCurve = _widthCurve;

            Shader shader = Shader.Find(RenderingShaders.Line);
            _line.material = new Material(shader) { name = body.Name + " Orbit Material" };

            // The palette is authored in sRGB. A LineRenderer's vertex colour
            // reaches the shader unconverted, so the conversion to linear light
            // happens here, at the boundary.
            _line.startColor = colour.linear;
            _line.endColor = colour.linear;
        }

        /// <summary>
        /// Rebuild the ellipse in the ship-relative frame and apply the shared
        /// apparent width. The width comes from the camera, never from the
        /// orbit's radius: an orbit-sized rule is only correct when the whole
        /// orbit is framed, and becomes a screen-filling ribbon when the
        /// camera is inside it.
        ///
        /// Each vertex carries its own width, bounded to
        /// <see cref="LineWidth.DistanceFloorPixels"/> at the far end and to the
        /// line's own pixel width at the near end. The floor keeps the far arc
        /// from vanishing into the distance; the ceiling stops the near arc
        /// from turning into a ribbon when the camera is inside the orbit.
        /// </summary>
        public void Refresh(Vector3d shipPositionM, Camera camera, float unitsPerMeter,
            bool visible, float lookAtWidthUnits, float lookAtPixels,
            float lookAtDepthUnits = 1f)
        {
            if (_line == null) return;
            _line.enabled = visible;
            if (!visible) return;

            _model.SampleOrbitWorld(_body, Samples, _buffer);

            _relative.Clear();
            for (int i = 0; i <= Samples; i++)
                _relative.Add(((_buffer[i] - shipPositionM) * unitsPerMeter).ToUnity());

            // As with the route line, the part of the ellipse behind the
            // camera cannot be billboarded and would stretch into a wedge.
            int count = LineCameraClip.Clip(_relative, camera, _run, _visible);
            _line.positionCount = count;
            _line.enabled = visible && count >= 2;
            if (count < 2) return;

            if (_widthKeys == null || _widthKeys.Length != count)
                _widthKeys = new Keyframe[count];

            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            float fieldOfView = camera != null ? camera.fieldOfView : 60f;
            int viewportHeight = camera != null && camera.pixelHeight > 0
                ? camera.pixelHeight
                : Screen.height;
            float denominator = Mathf.Max(count - 1, 1);

            for (int i = 0; i < count; i++)
            {
                Vector3 relative = _visible[i];
                _line.SetPosition(i, relative);

                float depth = camera != null
                    ? Vector3.Distance(relative, cameraPosition)
                    : lookAtDepthUnits;
                _widthKeys[i] = new Keyframe(i / denominator,
                    LineWidth.SampleWorldWidth(lookAtWidthUnits, lookAtPixels, depth,
                        fieldOfView, viewportHeight));
            }

            _widthCurve.SetKeys(_widthKeys);
            _line.widthCurve = _widthCurve;
        }

        public void SetColour(Color colour)
        {
            if (_line == null) return;
            _line.startColor = colour.linear;
            _line.endColor = colour.linear;
        }
    }
}
