using System;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Player
{
    /// <summary>
    /// The local scale around the ship: the body whose sphere of influence
    /// contains it, with the smallest such sphere winning where they overlap.
    /// At Gateway this is the Moon, whose SOI sits inside Earth's. The scale
    /// sets the `Space` home distance, so the return lands in the ship's own
    /// neighbourhood rather than at an arbitrary zoom.
    ///
    /// Plain C# with no scene dependency, so the choice and the deep-space
    /// fallback are verified in EditMode.
    /// </summary>
    public static class LocalScale
    {
        /// <summary>
        /// Scale when the ship is outside every sphere of influence: 1 million
        /// km. It gives `Space` a useful neighbourhood even in deep space.
        /// </summary>
        public const double DeepSpaceRadiusM = 1.0e9;

        /// <summary>
        /// The body whose sphere of influence contains the ship, or null in
        /// deep space. The Sun has no parent and an infinite SOI, so it is
        /// never the local neighbourhood; a massless station cannot contain
        /// anything either.
        /// </summary>
        public static BodyRuntime DominantBody(SolarSystemModel model, Vector3d shipPositionM)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            BodyRuntime best = null;
            double bestRadiusM = double.PositiveInfinity;

            foreach (BodyRuntime body in model.Ordered)
            {
                double radiusM = SphereOfInfluence.RadiusM(body);

                if (!(radiusM > 0.0) || double.IsInfinity(radiusM)) continue;
                if (radiusM >= bestRadiusM) continue;
                if (!SphereOfInfluence.Contains(body, shipPositionM, body.PositionM)) continue;

                best = body;
                bestRadiusM = radiusM;
            }

            return best;
        }

        /// <summary>
        /// The local radius in metres: the dominant body's SOI radius, or
        /// <see cref="DeepSpaceRadiusM"/> when the ship is outside every SOI.
        /// </summary>
        public static double RadiusM(SolarSystemModel model, Vector3d shipPositionM)
        {
            BodyRuntime dominant = DominantBody(model, shipPositionM);
            if (dominant == null) return DeepSpaceRadiusM;

            double radiusM = SphereOfInfluence.RadiusM(dominant);
            return double.IsFinite(radiusM) && radiusM > 0.0 ? radiusM : DeepSpaceRadiusM;
        }
    }
}
