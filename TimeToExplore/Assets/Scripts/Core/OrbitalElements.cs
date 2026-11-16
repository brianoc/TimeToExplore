using System;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Classical Keplerian elements plus the parent's gravitational parameter.
    /// All angles are radians, all distances metres, times seconds.
    /// </summary>
    public struct OrbitalElements
    {
        public double semiMajorAxisM;
        public double eccentricity;
        public double inclinationRad;
        public double ascendingNodeRad;
        public double argumentOfPeriapsisRad;
        public double meanAnomalyRad;
        public double epochJulianDate;
        public double gravitationalParameter;
        public bool parentEquatorFrame;

        public double PeriodSeconds
        {
            get
            {
                double a = semiMajorAxisM;
                return OrbitMath.TwoPi * Math.Sqrt(a * a * a / gravitationalParameter);
            }
        }

        /// <summary>Position and velocity relative to the parent, in the
        /// element frame (ecliptic, or parent-equator when that flag is set).</summary>
        public void GetState(double julianDate, out Vector3d position, out Vector3d velocity)
        {
            double dt = (julianDate - epochJulianDate) * JulianDate.SecondsPerDay;
            double n = Math.Sqrt(gravitationalParameter / (semiMajorAxisM * semiMajorAxisM * semiMajorAxisM));
            double meanAnomaly = meanAnomalyRad + n * dt;
            double eAnomaly = OrbitMath.SolveKepler(meanAnomaly, eccentricity);

            double cosE = Math.Cos(eAnomaly);
            double sinE = Math.Sin(eAnomaly);
            double e2 = eccentricity * eccentricity;
            double radius = semiMajorAxisM * (1.0 - eccentricity * cosE);

            double x = semiMajorAxisM * (cosE - eccentricity);
            double y = semiMajorAxisM * Math.Sqrt(1.0 - e2) * sinE;

            double speedScale = Math.Sqrt(gravitationalParameter * semiMajorAxisM) / radius;
            double vx = -speedScale * sinE;
            double vy = speedScale * Math.Sqrt(1.0 - e2) * cosE;

            position = RotateToReferenceFrame(x, y);
            velocity = RotateToReferenceFrame(vx, vy);
        }

        /// <summary>Sample the closed ellipse for an orbit line. The points are
        /// in the element frame and are independent of time.</summary>
        public void SampleEllipse(int samples, Vector3d[] output)
        {
            if (output == null || output.Length < samples + 1)
                throw new ArgumentException("Output buffer too small", nameof(output));

            double e2 = eccentricity * eccentricity;
            double b = semiMajorAxisM * Math.Sqrt(1.0 - e2);
            for (int i = 0; i <= samples; i++)
            {
                double eAnomaly = OrbitMath.TwoPi * i / samples;
                double x = semiMajorAxisM * (Math.Cos(eAnomaly) - eccentricity);
                double y = b * Math.Sin(eAnomaly);
                output[i] = RotateToReferenceFrame(x, y);
            }
        }

        private Vector3d RotateToReferenceFrame(double x, double y)
        {
            double cw = Math.Cos(argumentOfPeriapsisRad);
            double sw = Math.Sin(argumentOfPeriapsisRad);
            double x1 = cw * x - sw * y;
            double y1 = sw * x + cw * y;

            double ci = Math.Cos(inclinationRad);
            double si = Math.Sin(inclinationRad);
            double y2 = ci * y1;
            double z2 = si * y1;

            double cO = Math.Cos(ascendingNodeRad);
            double sO = Math.Sin(ascendingNodeRad);
            return new Vector3d(
                cO * x1 - sO * y2,
                sO * x1 + cO * y2,
                z2);
        }
    }
}
