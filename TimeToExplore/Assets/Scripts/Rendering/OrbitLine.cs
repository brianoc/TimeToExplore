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

        /// <summary>Line width as a fraction of the camera distance: ~1.5 px at 60°/1080p.</summary>
        public const float WidthPerCameraDistance = 0.0015f;

        /// <summary>The old local ceiling, kept so the local look does not change.</summary>
        public const float LocalMaxWidthUnits = 4000f;

        /// <summary>
        /// The map ceiling at the 12-million-unit zoom: 20,000 units is about
        /// 1.5 px at 60°/1080p, so the line stays visible instead of thinning
        /// to sub-pixel as the view opens out.
        /// </summary>
        public const float MapMaxWidthUnits = 20_000f;

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

        public void Refresh(Vector3d shipPositionM, float unitsPerMeter, bool visible,
            float cameraDistanceUnits = 0f, float mapBlendWeight = 0f)
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

            // The local rule sizes the line from the orbit, which is correct
            // while the camera is inside it but turns a planet's orbit into a
            // ribbon at close map zoom. The map rule sizes it from the camera
            // distance instead, so it stays about a pixel wide at any zoom.
            double parentDistance = _body.Parent != null
                ? Vector3d.Distance(_body.Parent.PositionM, shipPositionM)
                : 0.0;
            double orbitRadius = _body.Elements.semiMajorAxisM;
            float reference = (float)(System.Math.Max(parentDistance, orbitRadius) * unitsPerMeter);
            _line.widthMultiplier = WidthForView(reference, cameraDistanceUnits, mapBlendWeight);
        }

        /// <summary>
        /// Width of an orbit line for a view: the original orbit-sized rule at
        /// w = 0 (the local look, unchanged), easing to a camera-distance rule
        /// on the map so a distant orbit can never swamp the neighbourhood.
        /// </summary>
        public static float WidthForView(float referenceUnits, float cameraDistanceUnits,
            float blendWeight)
        {
            float local = Mathf.Clamp(referenceUnits * WidthPerCameraDistance, 0.35f,
                LocalMaxWidthUnits);
            float map = Mathf.Clamp(cameraDistanceUnits * WidthPerCameraDistance, 0.35f,
                MapMaxWidthUnits);
            return Mathf.Lerp(local, map, Mathf.Clamp01(blendWeight));
        }

        public void SetColour(Color colour)
        {
            if (_line == null) return;
            _line.startColor = colour;
            _line.endColor = colour;
        }
    }
}
