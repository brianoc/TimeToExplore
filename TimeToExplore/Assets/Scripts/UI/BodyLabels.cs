using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.UI
{
    /// <summary>
    /// Which body labels the HUD draws. Sun, planets, and stations always
    /// label; a moon labels inside 2 million km and an asteroid inside
    /// 200,000 km; the selection always labels.
    ///
    /// Pure C# so the density rule is tested without a HUD or a camera.
    /// </summary>
    public static class BodyLabels
    {
        /// <summary>A moon's label radius: 2 million km.</summary>
        public const double MoonDistanceM = 2.0e9;

        /// <summary>An asteroid's label radius: 200,000 km.</summary>
        public const double AsteroidDistanceM = 2.0e8;

        /// <summary>
        /// True when <paramref name="body"/> should carry a label this frame.
        /// </summary>
        /// <param name="body">The body being considered.</param>
        /// <param name="distanceToShipM">Distance from the ship, in metres.</param>
        /// <param name="selected">The body is the current selection.</param>
        public static bool ShouldLabel(BodyRuntime body, double distanceToShipM, bool selected)
        {
            if (body == null) return false;
            if (selected) return true;

            switch (body.Kind)
            {
                case "star":
                case "planet":
                case "station":
                    return true;

                case "moon":
                    return distanceToShipM < MoonDistanceM;

                case "asteroid":
                    return distanceToShipM < AsteroidDistanceM;

                default:
                    return false;
            }
        }
    }
}
