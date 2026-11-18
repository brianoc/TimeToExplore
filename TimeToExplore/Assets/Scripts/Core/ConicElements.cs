using System;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Classical shape of a two-body orbit derived from a state vector, in the
    /// plane that state defines. It covers the ellipse, the hyperbola, and the
    /// parabolic boundary, and converts between true anomaly and time through
    /// Kepler's equation. The transfer-conic capture uses it to parameterise a
    /// coast by true anomaly, so sampling is dense at periapsis where the path
    /// turns hardest ([ADR-014](/decisions/adr-014-keplerian-coast.md)).
    ///
    /// The basis is perifocal: <see cref="PeriapsisDirection"/> points at
    /// periapsis, <see cref="TransverseDirection"/> is the prograde in-plane
    /// tangent, and <see cref="NormalDirection"/> completes the right-handed
    /// set. Position and velocity are relative to the focus.
    /// </summary>
    public sealed class ConicElements
    {
        private const double CircularEpsilon = 1e-9;
        private const double ParabolicEpsilon = 1e-7;

        private ConicElements()
        {
        }

        public double GravitationalParameter { get; private set; }
        public double Eccentricity { get; private set; }

        /// <summary>Semi-major axis: positive for an ellipse, negative for a
        /// hyperbola, infinite at the parabolic boundary.</summary>
        public double SemiMajorAxisM { get; private set; }

        public double SemiLatusRectumM { get; private set; }
        public double PeriapsisRadiusM { get; private set; }
        public double ApoapsisRadiusM { get; private set; }
        public double TrueAnomalyRad { get; private set; }
        public double TimeSincePeriapsisSeconds { get; private set; }
        public double MeanMotionRadPerSecond { get; private set; }
        public double PeriodSeconds { get; private set; }

        public Vector3d PeriapsisDirection { get; private set; }
        public Vector3d TransverseDirection { get; private set; }
        public Vector3d NormalDirection { get; private set; }

        public bool IsElliptic => Eccentricity < 1.0 - ParabolicEpsilon;
        public bool IsHyperbolic => Eccentricity > 1.0 + ParabolicEpsilon;
        public bool IsNearParabolic => Math.Abs(Eccentricity - 1.0) <= ParabolicEpsilon;
        public bool IsCircular => Eccentricity < CircularEpsilon;

        /// <summary>
        /// Derive the conic from a relative state. The velocity must have a
        /// non-zero component across the radius; a purely radial state has no
        /// orbital plane and is rejected.
        /// </summary>
        public static ConicElements FromState(Vector3d positionM, Vector3d velocityMps,
            double gravitationalParameter)
        {
            if (!(gravitationalParameter > 0.0))
                throw new ArgumentOutOfRangeException(nameof(gravitationalParameter));

            double radius = positionM.Magnitude;
            if (!(radius > 0.0))
                throw new ArgumentOutOfRangeException(nameof(positionM));

            Vector3d angularMomentum = Vector3d.Cross(positionM, velocityMps);
            double h = angularMomentum.Magnitude;
            if (!(h > 1e-12 * radius * Math.Max(velocityMps.Magnitude, 1.0)))
                throw new InvalidOperationException(
                    "A conic needs a non-radial state; the angular momentum is zero.");

            Vector3d eccentricityVector = Vector3d.Cross(velocityMps, angularMomentum)
                                          / gravitationalParameter
                                          - positionM / radius;
            double eccentricity = eccentricityVector.Magnitude;

            double energy = 0.5 * velocityMps.SqrMagnitude
                            - gravitationalParameter / radius;
            double semiMajorAxis = Math.Abs(energy) < 1e-18
                ? double.PositiveInfinity
                : -gravitationalParameter / (2.0 * energy);
            double p = h * h / gravitationalParameter;

            Vector3d normal = angularMomentum / h;
            Vector3d periapsisDirection = eccentricity > CircularEpsilon
                ? eccentricityVector / eccentricity
                : positionM / radius;
            Vector3d transverse = Vector3d.Cross(normal, periapsisDirection).Normalized;

            double trueAnomaly = Math.Atan2(
                Vector3d.Dot(positionM, transverse),
                Vector3d.Dot(positionM, periapsisDirection));

            var elements = new ConicElements
            {
                GravitationalParameter = gravitationalParameter,
                Eccentricity = eccentricity,
                SemiMajorAxisM = semiMajorAxis,
                SemiLatusRectumM = p,
                PeriapsisRadiusM = p / (1.0 + eccentricity),
                ApoapsisRadiusM = eccentricity < 1.0
                    ? p / (1.0 - eccentricity)
                    : double.PositiveInfinity,
                TrueAnomalyRad = trueAnomaly,
                NormalDirection = normal,
                PeriapsisDirection = periapsisDirection,
                TransverseDirection = transverse
            };

            if (eccentricity < 1.0 - ParabolicEpsilon)
            {
                elements.MeanMotionRadPerSecond =
                    Math.Sqrt(gravitationalParameter
                              / (semiMajorAxis * semiMajorAxis * semiMajorAxis));
                elements.PeriodSeconds = OrbitMath.TwoPi / elements.MeanMotionRadPerSecond;
            }
            else if (eccentricity > 1.0 + ParabolicEpsilon)
            {
                double negativeA = p / (eccentricity * eccentricity - 1.0);
                elements.MeanMotionRadPerSecond =
                    Math.Sqrt(gravitationalParameter / (negativeA * negativeA * negativeA));
                elements.PeriodSeconds = double.PositiveInfinity;
            }
            else
            {
                elements.MeanMotionRadPerSecond = double.NaN;
                elements.PeriodSeconds = double.PositiveInfinity;
            }

            elements.TimeSincePeriapsisSeconds = elements.TimeSincePeriapsis(trueAnomaly);
            return elements;
        }

        /// <summary>Position and velocity at a true anomaly, relative to the focus.</summary>
        public void StateAtTrueAnomaly(double trueAnomalyRad,
            out Vector3d positionM, out Vector3d velocityMps)
        {
            double cos = Math.Cos(trueAnomalyRad);
            double sin = Math.Sin(trueAnomalyRad);
            double denominator = 1.0 + Eccentricity * cos;

            if (!(denominator > 0.0))
                throw new ArgumentOutOfRangeException(nameof(trueAnomalyRad),
                    "The true anomaly lies outside this conic's branch.");

            double radius = SemiLatusRectumM / denominator;
            positionM = (PeriapsisDirection * cos + TransverseDirection * sin) * radius;

            double scale = Math.Sqrt(GravitationalParameter / SemiLatusRectumM);
            Vector3d radialVelocity = PeriapsisDirection * (-sin);
            Vector3d transverseVelocity = TransverseDirection * (Eccentricity + cos);
            velocityMps = (radialVelocity + transverseVelocity) * scale;
        }

        /// <summary>Time from periapsis to a true anomaly. Negative before
        /// periapsis, positive after.</summary>
        public double TimeSincePeriapsis(double trueAnomalyRad)
        {
            if (IsElliptic)
            {
                double e = Eccentricity;
                double eccentricAnomaly = 2.0 * Math.Atan2(
                    Math.Sqrt(1.0 - e) * Math.Sin(trueAnomalyRad / 2.0),
                    Math.Sqrt(1.0 + e) * Math.Cos(trueAnomalyRad / 2.0));
                double meanAnomaly = eccentricAnomaly - e * Math.Sin(eccentricAnomaly);
                return meanAnomaly / MeanMotionRadPerSecond;
            }

            if (IsHyperbolic)
            {
                double e = Eccentricity;
                double denominator = 1.0 + e * Math.Cos(trueAnomalyRad);
                if (!(denominator > 0.0)) return double.NaN;

                double sinhH = Math.Sqrt(e * e - 1.0)
                               * Math.Sin(trueAnomalyRad) / denominator;
                double hyperbolicAnomaly = Math.Asinh(sinhH);
                double meanAnomaly = e * Math.Sinh(hyperbolicAnomaly)
                                     - hyperbolicAnomaly;
                return meanAnomaly / MeanMotionRadPerSecond;
            }

            // Parabolic boundary: Barker's equation.
            double d = Math.Tan(trueAnomalyRad / 2.0);
            return 0.5 * Math.Sqrt(2.0 * Math.Pow(SemiLatusRectumM, 3.0)
                                   / GravitationalParameter)
                   * (d + d * d * d / 3.0);
        }

        /// <summary>True anomaly at a time measured from periapsis.</summary>
        public double TrueAnomalyAtTime(double secondsSincePeriapsis)
        {
            if (IsElliptic)
            {
                double meanAnomaly = MeanMotionRadPerSecond * secondsSincePeriapsis;
                double eccentricAnomaly = OrbitMath.SolveKepler(meanAnomaly, Eccentricity);
                return 2.0 * Math.Atan2(
                    Math.Sqrt(1.0 + Eccentricity) * Math.Sin(eccentricAnomaly / 2.0),
                    Math.Sqrt(1.0 - Eccentricity) * Math.Cos(eccentricAnomaly / 2.0));
            }

            if (IsHyperbolic)
            {
                double meanAnomaly = MeanMotionRadPerSecond * secondsSincePeriapsis;
                double hyperbolicAnomaly = SolveHyperbolicKepler(meanAnomaly, Eccentricity);
                return 2.0 * Math.Atan2(
                    Math.Sqrt(Eccentricity + 1.0) * Math.Sinh(hyperbolicAnomaly / 2.0),
                    Math.Sqrt(Eccentricity - 1.0) * Math.Cosh(hyperbolicAnomaly / 2.0));
            }

            double scale = 0.5 * Math.Sqrt(2.0 * Math.Pow(SemiLatusRectumM, 3.0)
                                           / GravitationalParameter);
            double a = secondsSincePeriapsis / scale;
            double term = 3.0 * a / 2.0;
            double root = Math.Sqrt(term * term + 1.0);
            double d = Math.Cbrt(term + root) + Math.Cbrt(term - root);
            return 2.0 * Math.Atan(d);
        }

        /// <summary>
        /// Solve e sinh H − H = M for the hyperbolic anomaly. The mean anomaly
        /// is monotone in H, so a bracket-and-bisect is deterministic even when
        /// e is close to one and Newton's seed is poor.
        /// </summary>
        public static double SolveHyperbolicKepler(double meanAnomaly, double eccentricity)
        {
            if (eccentricity <= 1.0)
                throw new ArgumentOutOfRangeException(nameof(eccentricity));

            if (meanAnomaly == 0.0) return 0.0;

            double sign = meanAnomaly < 0.0 ? -1.0 : 1.0;
            double target = Math.Abs(meanAnomaly);
            double low = 0.0;
            double high = 1.0;

            while (HyperbolicMean(high, eccentricity) < target && high < 1e8)
                high *= 2.0;

            for (int i = 0; i < 120; i++)
            {
                double mid = 0.5 * (low + high);
                if (HyperbolicMean(mid, eccentricity) < target) low = mid;
                else high = mid;
            }

            return sign * 0.5 * (low + high);
        }

        private static double HyperbolicMean(double anomaly, double eccentricity) =>
            eccentricity * Math.Sinh(anomaly) - anomaly;
    }
}
