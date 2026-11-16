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

            Shader shader = Shader.Find("Sprites/Default");
            _line.material = new Material(shader) { name = body.Name + " Orbit Material" };
            _line.startColor = colour;
            _line.endColor = colour;
        }

        public void Refresh(Vector3d shipPositionM, float unitsPerMeter, bool visible)
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

            // Keep the line roughly 1-2 pixels wide at any zoom level.
            double parentDistance = _body.Parent != null
                ? Vector3d.Distance(_body.Parent.PositionM, shipPositionM)
                : 0.0;
            double orbitRadius = _body.Elements.semiMajorAxisM;
            float reference = (float)(System.Math.Max(parentDistance, orbitRadius) * unitsPerMeter);
            _line.widthMultiplier = Mathf.Clamp(reference * 0.0015f, 0.35f, 4000f);
        }

        public void SetColour(Color colour)
        {
            if (_line == null) return;
            _line.startColor = colour;
            _line.endColor = colour;
        }
    }
}
