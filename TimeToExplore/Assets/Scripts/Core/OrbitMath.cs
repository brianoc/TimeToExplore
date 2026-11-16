using System;

namespace TimeToExplore.Core
{
    public enum LagrangePoint
    {
        L1,
        L2,
        L4,
        L5
    }

    public static class OrbitMath
    {
        public const double TwoPi = Math.PI * 2.0;
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;
        public const double ObliquityJ2000 = 23.43928 * Deg2Rad;

        public static double WrapTwoPi(double angle)
        {
            angle %= TwoPi;
            if (angle < 0) angle += TwoPi;
            return angle;
        }

        public static double WrapPi(double angle)
        {
            angle = WrapTwoPi(angle);
            return angle > Math.PI ? angle - TwoPi : angle;
        }

        /// <summary>
        /// Solve Kepler's equation M = E - e sin E for the eccentric anomaly.
        /// Newton-Raphson with a bisection fallback for high eccentricity.
        /// </summary>
        public static double SolveKepler(double meanAnomaly, double eccentricity)
        {
            double m = WrapTwoPi(meanAnomaly);
            double e = Math.Clamp(eccentricity, 0.0, 0.999999);
            double eAnomaly = e < 0.8 ? m : Math.PI;

            for (int i = 0; i < 40; i++)
            {
                double f = eAnomaly - e * Math.Sin(eAnomaly) - m;
                double fp = 1.0 - e * Math.Cos(eAnomaly);
                double step = f / fp;
                eAnomaly -= step;
                if (Math.Abs(step) < 1e-13) return eAnomaly;
            }

            double lo = 0.0;
            double hi = TwoPi;
            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5 * (lo + hi);
                double f = mid - e * Math.Sin(mid) - m;
                if (f > 0.0) hi = mid;
                else lo = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>
        /// Position of a Lagrange point of the primary/secondary pair, in the
        /// same inertial frame as the input positions. L1 and L2 solve the
        /// circular restricted three-body quintic rather than using the Hill
        /// approximation, so the result is good to a few percent.
        /// </summary>
        public static Vector3d ComputeLagrangePoint(
            Vector3d primaryPosition,
            double primaryGm,
            Vector3d secondaryPosition,
            double secondaryGm,
            Vector3d secondaryVelocity,
            LagrangePoint point)
        {
            Vector3d rel = secondaryPosition - primaryPosition;
            double distance = rel.Magnitude;
            if (distance < 1e-6) return secondaryPosition;

            Vector3d direction = rel / distance;
            double mu = secondaryGm / (primaryGm + secondaryGm);

            switch (point)
            {
                case LagrangePoint.L1:
                    return secondaryPosition - direction * (SolveGamma(mu, true) * distance);
                case LagrangePoint.L2:
                    return secondaryPosition + direction * (SolveGamma(mu, false) * distance);
            }

            Vector3d angularMomentum = Vector3d.Cross(rel, secondaryVelocity);
            Vector3d normal = angularMomentum.SqrMagnitude > 1e-12
                ? angularMomentum.Normalized
                : Vector3d.Up;
            Vector3d side = Vector3d.Cross(normal, direction).Normalized;
            double sign = point == LagrangePoint.L4 ? 1.0 : -1.0;
            Vector3d offset = (direction * 0.5 + side * (Math.Sqrt(3.0) * 0.5 * sign)) * distance;
            return primaryPosition + offset;
        }

        private static double SolveGamma(double mu, bool l1)
        {
            // f(g) changes sign once between 0 and 0.5 for both L1 and L2.
            double lo = 1e-12;
            double hi = 0.5;
            for (int i = 0; i < 90; i++)
            {
                double g = 0.5 * (lo + hi);
                double g2 = g * g;
                double g3 = g2 * g;
                double g4 = g3 * g;
                double g5 = g4 * g;

                double f = l1
                    ? g5 - (3.0 - mu) * g4 + (3.0 - 2.0 * mu) * g3 - mu * g2 + 2.0 * mu * g - mu
                    : g5 + (3.0 - mu) * g4 + (3.0 - 2.0 * mu) * g3 - mu * g2 - 2.0 * mu * g - mu;

                if (f > 0.0) hi = g;
                else lo = g;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>Convert an equatorial J2000 pole (RA/Dec) to an ecliptic unit vector.</summary>
        public static Vector3d PoleFromRaDec(double rightAscensionDeg, double declinationDeg)
        {
            double ra = rightAscensionDeg * Deg2Rad;
            double dec = declinationDeg * Deg2Rad;
            double xEq = Math.Cos(dec) * Math.Cos(ra);
            double yEq = Math.Cos(dec) * Math.Sin(ra);
            double zEq = Math.Sin(dec);

            double c = Math.Cos(ObliquityJ2000);
            double s = Math.Sin(ObliquityJ2000);
            return new Vector3d(
                xEq,
                c * yEq + s * zEq,
                -s * yEq + c * zEq);
        }
    }
}
