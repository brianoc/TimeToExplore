using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Sphere-of-influence radius, the patched-conic estimate
    /// r_SOI ≈ a (m / M)^(2/5) where the masses are expressed as GM and `a` is
    /// the orbital semi-major axis about the primary.
    ///
    /// The ship's dynamics do not use this: the gravity field is continuous and
    /// has no seams. The radius is only used by the planner to choose where the
    /// cruise leg hands over to the capture leg.
    /// </summary>
    public static class SphereOfInfluence
    {
        public static double RadiusM(BodyRuntime body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (body.Parent == null || body.Parent.Gm <= 0.0) return double.PositiveInfinity;

            double semiMajorAxis = body.HasOrbit && body.Elements.semiMajorAxisM > 0.0
                ? body.Elements.semiMajorAxisM
                : Vector3d.Distance(body.PositionM, body.Parent.PositionM);

            return semiMajorAxis * Math.Pow(body.Gm / body.Parent.Gm, 0.4);
        }

        public static bool Contains(BodyRuntime body, Vector3d pointM, Vector3d bodyPositionM)
        {
            double radius = RadiusM(body);
            if (!double.IsFinite(radius)) return true;
            return Vector3d.Distance(pointM, bodyPositionM) < radius;
        }
    }
}
