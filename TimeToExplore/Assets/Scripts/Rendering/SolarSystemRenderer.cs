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

        public IEnumerable<BodyView> Views => _views.Values;

        /// <summary>
        /// The local scale used by the last <see cref="Refresh"/>, in metres.
        /// Zero means the neighbourhood fade is off and the aid is full
        /// everywhere, as in the shipped scene while its map switch is off.
        /// </summary>
        public double LocalAidRadiusM { get; private set; }

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

            // Planets share one warm-to-cool ramp, assigned outward from
            // Mercury so the orbit lines read as a system rather than eight
            // arbitrary colours. Moons stay neutral: they are subordinate to
            // their planet, not another stop on the ramp.
            Dictionary<string, Color> planetColours = OrbitColours.ForPlanets(model.Ordered);

            foreach (BodyRuntime body in model.Ordered)
            {
                if (!body.HasOrbit) continue;
                if (body.Kind == "planet" && planetColours.TryGetValue(body.Id, out Color colour))
                {
                    _planetOrbits.Add(CreateOrbit(body, colour));
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

        /// <summary>
        /// Rebuild the frame: positions, marker sizes, and orbit lines.
        /// <paramref name="localRadiusM"/> is the dominant body's SOI radius.
        /// A non-positive or infinite value disables the neighbourhood fade
        /// and keeps the old full-strength aid everywhere, which is what the
        /// shipped scene passes while its map switch is off.
        /// </summary>
        public void Refresh(Vector3d shipPositionM, double julianDate,
            float mapBlendWeight = 0f, float cameraDistanceUnits = 0f,
            double localRadiusM = 0.0)
        {
            if (_model == null) return;

            LocalAidRadiusM = localRadiusM;

            float fieldOfView = _camera != null ? _camera.fieldOfView : 60f;

            // The marker and line rules are per screen pixel, so they follow the
            // viewport the camera is actually drawing into. In play that is the
            // screen; a camera rendering into a RenderTexture reports that
            // texture's height, which keeps the apparent width correct there too.
            int viewportHeight = _camera != null && _camera.pixelHeight > 0
                ? _camera.pixelHeight
                : Screen.height;

            float minPixels = MarkerSizing.MinimumPixels(mapBlendWeight);
            float baseMinAngularSize = MarkerSizing.MinimumAngularSize(
                minPixels, fieldOfView, viewportHeight);
            double cameraDistanceM = UnitsPerMeter > 0f
                ? cameraDistanceUnits / (double)UnitsPerMeter
                : 0.0;

            foreach (BodyView view in _views.Values)
            {
                // A scene teardown or load can destroy the view objects while
                // the bootstrap that owns them is still being ticked. A destroyed
                // Unity object compares equal to null; skip it rather than
                // throwing out of the frame loop.
                if (view == null) continue;

                double bodyDistanceM = Vector3d.Distance(view.Body.PositionM, shipPositionM);
                float aidStrength = MarkerSizing.AidStrength(cameraDistanceM, bodyDistanceM,
                    localRadiusM, mapBlendWeight);
                view.Refresh(_camera, shipPositionM, julianDate, TrueScale,
                    baseMinAngularSize * aidStrength);
            }

            if (_sunView != null)
                Shader.SetGlobalVector("_SunPosition", _sunView.transform.position);

            // Every line shares one apparent width. The camera distance is the
            // depth of the look-at plane; a LineRenderer cannot vary its width
            // per vertex, so the near and far arcs of one ellipse still share
            // one world width and fall off naturally with perspective. Orbit
            // lines thicken with the map blend, from the local 3 px thread to
            // the solar system view's 10 px stroke.
            float lineDepth = cameraDistanceUnits > 0f
                ? cameraDistanceUnits
                : (_camera != null ? _camera.transform.position.magnitude : 0f);
            float orbitPixels = LineWidth.OrbitPixels(mapBlendWeight);
            float lineWidth = LineWidth.WorldWidth(lineDepth, fieldOfView, viewportHeight,
                orbitPixels);

            foreach (OrbitLine orbit in _planetOrbits)
            {
                if (orbit == null) continue;
                orbit.Refresh(shipPositionM, UnitsPerMeter, ShowOrbits, lineWidth);
            }

            foreach (OrbitLine orbit in _moonOrbits)
            {
                if (orbit == null) continue;
                orbit.Refresh(shipPositionM, UnitsPerMeter, ShowOrbits && ShowMoonOrbits,
                    lineWidth);
            }
        }
    }
}
