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
    /// This is a marker, not the ship model. The ship is a point with a state;
    /// the art pass replaces the capsule.
    /// </summary>
    public class ShipView : MonoBehaviour
    {
        /// <summary>Apparent height of the marker, in pixels.</summary>
        public const float MarkerPixels = 10f;

        private Transform _visual;
        private Renderer _renderer;

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

            Shader shader = Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "Ship Marker Material" };
            material.SetColor("_Color", new Color(0.86f, 0.90f, 0.96f));
            _renderer.sharedMaterial = material;
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

            Vector3d forward = thrust.MagnitudeMps2 > 1e-3 ? thrust.Direction : velocityMps;
            if (forward.SqrMagnitude < 1e-6) return;

            Vector3 direction = forward.Normalized.ToUnity();
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.995f
                ? Vector3.forward
                : Vector3.up;
            _visual.localRotation = Quaternion.LookRotation(direction, up);
        }

        /// <summary>Hide the marker, e.g. when the view is inside a planet.</summary>
        public void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible;
        }
    }
}
