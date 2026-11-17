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

        public void Initialise(BodyRuntime body, SolarSystemModel model, Color colour, Transform parent)
        {
            _body = body;
            _model = model;
            _buffer = new Vector3d[Samples + 1];

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
        /// </summary>
        public void Refresh(Vector3d shipPositionM, float unitsPerMeter, bool visible,
            float widthUnits)
        {
            if (_line == null) return;
            _line.enabled = visible;
            if (!visible) return;

            _model.SampleOrbitWorld(_body, Samples, _buffer);
            for (int i = 0; i <= Samples; i++)
            {
                Vector3d relative = (_buffer[i] - shipPositionM) * unitsPerMeter;
                _line.SetPosition(i, relative.ToUnity());
            }

            _line.widthMultiplier = Mathf.Max(widthUnits, 0f);
        }

        public void SetColour(Color colour)
        {
            if (_line == null) return;
            _line.startColor = colour;
            _line.endColor = colour;
        }
    }
}
