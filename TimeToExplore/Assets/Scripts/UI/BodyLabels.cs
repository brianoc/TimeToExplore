using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Simulation;

namespace TimeToExplore.UI
{
    /// <summary>
    /// Which body labels the HUD draws. The local view keeps its original
    /// distance thresholds; the map follows the zoom so it does not fill with
    /// every moon at full-system scale:
    ///
    /// * Sun, planets, and stations always label.
    /// * A moon labels on the map when the camera is inside five times its
    ///   parent's sphere of influence — zoom into a neighbourhood to read it.
    /// * An asteroid labels on the map only when hovered or selected.
    ///
    /// Pure C# so the density rule is tested without a HUD or a camera.
    /// </summary>
    public static class BodyLabels
    {
        /// <summary>How many parent-SOI radii count as the moon's neighbourhood.</summary>
        public const float MoonNeighbourhoodFactor = 5f;

        /// <summary>The local view's existing moon label radius: 2 million km.</summary>
        public const double LocalMoonDistanceM = 2.0e9;

        /// <summary>The local view's existing asteroid label radius: 200,000 km.</summary>
        public const double LocalAsteroidDistanceM = 2.0e8;

        /// <summary>
        /// True when <paramref name="body"/> should carry a label this frame.
        /// </summary>
        /// <param name="body">The body being considered.</param>
        /// <param name="distanceToShipM">Distance from the ship, in metres.</param>
        /// <param name="cameraDistanceUnits">Orbit distance of the camera, in render units.</param>
        /// <param name="mapBlendWeight">The camera rig's map blend, 0 local to 1 map.</param>
        /// <param name="hover">The body is under the pointer.</param>
        /// <param name="selected">The body is the current selection.</param>
        /// <param name="unitsPerMeter">Render units per metre.</param>
        public static bool ShouldLabel(BodyRuntime body, double distanceToShipM,
            float cameraDistanceUnits, float mapBlendWeight, bool hover, bool selected,
            float unitsPerMeter)
        {
            if (body == null) return false;
            if (selected) return true;

            bool mapSide = MapView.UsesPan(mapBlendWeight);
            if (mapSide && hover) return true;

            switch (body.Kind)
            {
                case "star":
                case "planet":
                case "station":
                    return true;

                case "moon":
                    return mapSide
                        ? InMoonNeighbourhood(body, cameraDistanceUnits, unitsPerMeter)
                        : distanceToShipM < LocalMoonDistanceM;

                case "asteroid":
                    // On the map only hover and selection reveal an asteroid;
                    // locally keep the old close-range rule.
                    return !mapSide && distanceToShipM < LocalAsteroidDistanceM;

                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the camera is close enough that a moon's neighbourhood
        /// fills the view: the camera distance is less than five parent-SOI
        /// radii. The 5× factor is the design's label-density rule.
        /// </summary>
        public static bool InMoonNeighbourhood(BodyRuntime moon, float cameraDistanceUnits,
            float unitsPerMeter)
        {
            if (moon == null || !(unitsPerMeter > 0f) || float.IsInfinity(unitsPerMeter))
                return false;

            BodyRuntime parent = moon.Parent;
            if (parent == null) return false;

            double parentSoiM = SphereOfInfluence.RadiusM(parent);
            if (!(parentSoiM > 0.0) || !double.IsFinite(parentSoiM)) return false;

            double cameraDistanceM = cameraDistanceUnits / (double)unitsPerMeter;
            return cameraDistanceM < MoonNeighbourhoodFactor * parentSoiM;
        }

        /// <summary>
        /// The ship's "you are here" label is a map reading rule: the marker
        /// exists in both views, but only the map names it.
        /// </summary>
        public static bool ShouldLabelShip(bool mapSide) => mapSide;
    }
}
