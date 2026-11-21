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
            _line.startColor = colour;
            _line.endColor = colour;
        }

        /// <summary>
        /// Rebuild the ellipse in the ship-relative frame and apply the shared
        /// apparent width. The width comes from the camera, never from the
        /// orbit's radius: an orbit-sized rule is only correct when the whole
        /// orbit is framed, and becomes a screen-filling ribbon when the
        /// camera is inside it.
        ///
        /// Each vertex carries its own width: the look-at width near the ship,
        /// then the floor once perspective would take it below
        /// <see cref="LineWidth.DistanceFloorPixels"/>. A single width per
        /// ellipse made the far arc vanish into the distance.
        /// </summary>
        public void Refresh(Vector3d shipPositionM, Camera camera, float unitsPerMeter,
            bool visible, float lookAtWidthUnits, float lookAtPixels)
        {
            if (_line == null) return;
            _line.enabled = visible;
            if (!visible) return;

            _model.SampleOrbitWorld(_body, Samples, _buffer);

            Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
            float fieldOfView = camera != null ? camera.fieldOfView : 60f;
            int viewportHeight = camera != null && camera.pixelHeight > 0
                ? camera.pixelHeight
                : Screen.height;
            float denominator = Mathf.Max(Samples, 1);

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 relative = ((_buffer[i] - shipPositionM) * unitsPerMeter).ToUnity();
                _line.SetPosition(i, relative);

                float depth = camera != null
                    ? Vector3.Distance(relative, cameraPosition)
                    : 0f;
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
            _line.startColor = colour;
            _line.endColor = colour;
        }
    }
}
