using System.Collections.Generic;
using UnityEngine;
using TimeToExplore.Core;

namespace TimeToExplore.Rendering
{
    /// <summary>
    /// Builds one view per body and keeps every view in the ship-relative
    /// render frame. 1 Unity unit = 1 / UnitsPerMeter metres; the default
    /// 1e-6 means one unit is 1,000 km.
    /// </summary>
    public class SolarSystemRenderer : MonoBehaviour
    {
        public float UnitsPerMeter = 1e-6f;
        public bool TrueScale;
        public bool ShowOrbits = true;
        public bool ShowMoonOrbits = true;

        private readonly Dictionary<string, BodyView> _views = new Dictionary<string, BodyView>();
        private readonly List<OrbitLine> _planetOrbits = new List<OrbitLine>();
        private readonly List<OrbitLine> _moonOrbits = new List<OrbitLine>();
        private SolarSystemModel _model;
        private Camera _camera;
        private BodyView _sunView;
        private Transform _bodyRoot;
        private Transform _orbitRoot;
        private float _minAngularSize;

        public IEnumerable<BodyView> Views => _views.Values;

        public void Initialise(SolarSystemModel model, Camera camera, float unitsPerMeter)
        {
            _model = model;
            _camera = camera;
            UnitsPerMeter = unitsPerMeter;

            _bodyRoot = new GameObject("Bodies").transform;
            _bodyRoot.SetParent(transform, false);
            _orbitRoot = new GameObject("Orbits").transform;
            _orbitRoot.SetParent(transform, false);

            foreach (BodyRuntime body in model.Ordered)
            {
                if (!body.IsRenderable) continue;

                GameObject viewObject = new GameObject(body.Name);
                BodyView view = viewObject.AddComponent<BodyView>();
                view.Initialise(body, UnitsPerMeter, _bodyRoot);
                _views[body.Id] = view;
                if (body.Data.IsStar) _sunView = view;
            }

            foreach (BodyRuntime body in model.Ordered)
            {
                if (!body.HasOrbit) continue;
                if (body.Kind == "planet")
                {
                    _planetOrbits.Add(CreateOrbit(body, new Color(0.55f, 0.72f, 1f, 0.22f)));
                }
                else if (body.Kind == "moon")
                {
                    _moonOrbits.Add(CreateOrbit(body, new Color(0.7f, 0.7f, 0.7f, 0.13f)));
                }
            }
        }

        private OrbitLine CreateOrbit(BodyRuntime body, Color colour)
        {
            GameObject orbitObject = new GameObject(body.Name + " Orbit");
            OrbitLine line = orbitObject.AddComponent<OrbitLine>();
            line.Initialise(body, _model, colour, _orbitRoot);
            return line;
        }

        public BodyView GetView(BodyRuntime body) =>
            body != null && _views.TryGetValue(body.Id, out BodyView view) ? view : null;

        public void Refresh(Vector3d shipPositionM, double julianDate)
        {
            if (_model == null) return;

            float minPixels = 4.5f;
            _minAngularSize = _camera.fieldOfView * Mathf.Deg2Rad * minPixels / Mathf.Max(Screen.height, 1);

            foreach (BodyView view in _views.Values)
                view.Refresh(_camera, shipPositionM, julianDate, TrueScale, _minAngularSize);

            if (_sunView != null)
                Shader.SetGlobalVector("_SunPosition", _sunView.transform.position);

            foreach (OrbitLine orbit in _planetOrbits)
                orbit.Refresh(shipPositionM, UnitsPerMeter, ShowOrbits);

            foreach (OrbitLine orbit in _moonOrbits)
                orbit.Refresh(shipPositionM, UnitsPerMeter, ShowOrbits && ShowMoonOrbits);
        }
    }
}
