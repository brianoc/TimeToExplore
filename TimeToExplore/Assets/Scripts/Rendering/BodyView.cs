using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// A single renderable body. The transform holds the ship-relative render
    /// position; the child visual is scaled to the body's true radius unless
    /// the minimum-apparent-size aid kicks in.
    /// </summary>
    public class BodyView : MonoBehaviour
    {
        public BodyRuntime Body { get; private set; }
        public Renderer Renderer { get; private set; }

        /// <summary>Radius of the drawn marker in Unity units, including the
        /// minimum-apparent-size aid. Used for mouse picking.</summary>
        public float VisualRadiusUnits =>
            _visual != null ? Mathf.Abs(_visual.localScale.x) * 0.5f : 0f;

        private Transform _visual;
        private Material _material;
        private float _scaleFactor = 1f;
        private float _unitsPerMeter;

        public void Initialise(BodyRuntime body, float unitsPerMeter, Transform parent)
        {
            Body = body;
            _unitsPerMeter = unitsPerMeter;
            name = body.Name;
            transform.SetParent(parent, false);

            PrimitiveType primitive = body.Data.IsStation ? PrimitiveType.Cube : PrimitiveType.Sphere;
            GameObject visualObject = GameObject.CreatePrimitive(primitive);
            visualObject.name = "Visual";
            _visual = visualObject.transform;
            _visual.SetParent(transform, false);

            Collider collider = visualObject.GetComponent<Collider>();
            if (collider != null)
            {
                // Views can also be built from an editor tool, so mirror Unity's
                // play/edit rule rather than logging an error in edit mode.
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            Renderer = visualObject.GetComponent<Renderer>();
            Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Renderer.receiveShadows = false;

            string shaderName = body.Data.IsStar ? "Unlit/Color" : "TimeToExplore/BodyLit";
            Shader shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _material = new Material(shader) { name = body.Name + " Material" };

            Color colour = ColorFrom(body.Data);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", colour);
            Renderer.sharedMaterial = _material;
        }

        private static Color ColorFrom(BodyData data)
        {
            if (data.color == null || data.color.Length < 3)
                return new Color(0.7f, 0.7f, 0.7f);
            return new Color(data.color[0], data.color[1], data.color[2]);
        }

        public void Refresh(Camera camera, Vector3d shipPositionM, double julianDate,
            bool trueScale, float minAngularSize)
        {
            Vector3 relative = ((Body.PositionM - shipPositionM) * _unitsPerMeter).ToUnity();
            transform.localPosition = relative;

            double displayRadius = Body.RadiusM * _unitsPerMeter;
            float distance = relative.magnitude;
            float angularSize = (float)(displayRadius / Mathf.Max(distance, 1e-6f));

            float targetFactor = TargetScaleFactor(angularSize, minAngularSize, trueScale);

            // Smooth the marker-size transition so distant bodies do not pop.
            _scaleFactor = Mathf.Lerp(_scaleFactor, targetFactor, 1f - Mathf.Exp(-8f * Time.deltaTime));

            float fullSize = (float)(displayRadius * 2.0 * _scaleFactor);
            _visual.localScale = Vector3.one * Mathf.Max(fullSize, 1e-7f);

            if (!Body.Data.IsStation)
            {
                float spin = (float)(Body.SpinAngleRadians(julianDate) * OrbitMath.Rad2Deg);
                float tilt = (float)Body.Data.axialTiltDeg;
                _visual.localRotation =
                    Quaternion.AngleAxis(tilt, Vector3.forward) *
                    Quaternion.AngleAxis(spin, Vector3.up);
            }
            else
            {
                float spin = (float)(Body.SpinAngleRadians(julianDate) * OrbitMath.Rad2Deg);
                _visual.localRotation = Quaternion.Euler(15f, spin, 0f);
            }
        }

        public float DistanceTo(Vector3d pointM) => (float)(Body.PositionM - pointM).Magnitude;

        /// <summary>
        /// How much the true radius must grow to reach the minimum apparent
        /// size. Pure so the aid and the true-scale override are testable in
        /// edit mode, where the smoothed scale does not move in one call.
        /// </summary>
        public static float TargetScaleFactor(float angularRadius, float minAngularSize,
            bool trueScale)
        {
            if (trueScale) return 1f;
            if (angularRadius >= minAngularSize) return 1f;
            return minAngularSize / Mathf.Max(angularRadius, 1e-9f);
        }
    }
}
