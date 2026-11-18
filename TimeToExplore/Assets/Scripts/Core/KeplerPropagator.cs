using System;

namespace TimeToExplore.Core
{
    /// <summary>
    /// Two-body propagation with universal variables. One formulation covers
    /// elliptic, parabolic, and hyperbolic orbits, so a coast arc can be
    /// propagated without first classifying it. The independent variable is the
    /// universal anomaly χ; the Stumpff functions C(z) and S(z) remove the
    /// singularity at the parabolic case.
    ///
    /// The propagator is the state-vector counterpart of
    /// <see cref="OrbitalElements.GetState"/>: elements to state already existed,
    /// state to future state did not. It is used by the transfer-conic capture
    /// ([ADR-014](/decisions/adr-014-keplerian-coast.md)).
    /// </summary>
    public static class KeplerPropagator
    {
        private const double AlphaEpsilon = 1e-10;

        /// <summary>
        /// Propagate a relative state by <paramref name="dtSeconds"/> under a
        /// point mass. Negative times are valid and propagate backwards.
        /// </summary>
        public static void Propagate(Vector3d positionM, Vector3d velocityMps,
            double gravitationalParameter, double dtSeconds,
            out Vector3d propagatedPositionM, out Vector3d propagatedVelocityMps)
        {
            if (!(gravitationalParameter > 0.0))
                throw new ArgumentOutOfRangeException(nameof(gravitationalParameter));
            if (!double.IsFinite(dtSeconds))
                throw new ArgumentOutOfRangeException(nameof(dtSeconds));

            double r0 = positionM.Magnitude;
            if (!(r0 > 0.0))
                throw new ArgumentOutOfRangeException(nameof(positionM),
                    "A Kepler propagation needs a non-zero position.");

            if (dtSeconds == 0.0)
            {
                propagatedPositionM = positionM;
                propagatedVelocityMps = velocityMps;
                return;
            }

            double sqrtMu = Math.Sqrt(gravitationalParameter);
            double rdotv = Vector3d.Dot(positionM, velocityMps);
            double alpha = 2.0 / r0 - velocityMps.SqrMagnitude / gravitationalParameter;
            double target = sqrtMu * dtSeconds;

            double chi = InitialGuess(alpha, gravitationalParameter, r0, dtSeconds);
            double tolerance = 1e-10 * Math.Max(1.0, Math.Abs(target));
            bool converged = false;

            for (int i = 0; i < 80; i++)
            {
                double residual = Residual(chi, alpha, r0, rdotv, sqrtMu, target);
                if (Math.Abs(residual) <= tolerance)
                {
                    converged = true;
                    break;
                }

                double derivative = Derivative(chi, alpha, r0, rdotv, sqrtMu);
                if (!(derivative > 1e-30)) break;

                double step = residual / derivative;
                double next = chi - step;
                if (!double.IsFinite(next)) break;

                chi = next;
                if (Math.Abs(step) <= 1e-13 * Math.Max(1.0, Math.Abs(chi)))
                {
                    converged = true;
                    break;
                }
            }

            if (!converged) chi = Bisect(alpha, r0, rdotv, sqrtMu, target, chi, dtSeconds);

            double z = alpha * chi * chi;
            Stumpff(z, out double c, out double s);
            double chi2 = chi * chi;

            double f = 1.0 - chi2 / r0 * c;
            double g = dtSeconds - chi2 * chi / sqrtMu * s;

            propagatedPositionM = positionM * f + velocityMps * g;
            double r = propagatedPositionM.Magnitude;

            double fdot = sqrtMu / (r * r0) * (alpha * chi2 * chi * s - chi);
            double gdot = 1.0 - chi2 / r * c;
            propagatedVelocityMps = positionM * fdot + velocityMps * gdot;
        }

        private static double InitialGuess(double alpha, double gravitationalParameter,
            double r0, double dtSeconds)
        {
            double sqrtMu = Math.Sqrt(gravitationalParameter);

            if (alpha > AlphaEpsilon)
                return sqrtMu * dtSeconds * alpha;

            if (alpha < -AlphaEpsilon)
            {
                // A hyperbolic seed from the mean anomaly. It only has to put
                // Newton in the basin; the bisection fallback covers the rest.
                double semiMajorAxis = 1.0 / alpha;
                double n = Math.Sqrt(gravitationalParameter
                                     / Math.Pow(-semiMajorAxis, 3.0));
                double meanAnomaly = n * dtSeconds;
                double anomaly = Math.Asinh(meanAnomaly);
                return Math.Sqrt(-semiMajorAxis) * anomaly;
            }

            return sqrtMu * dtSeconds / r0;
        }

        private static double Residual(double chi, double alpha, double r0,
            double rdotv, double sqrtMu, double target)
        {
            double z = alpha * chi * chi;
            Stumpff(z, out double c, out double s);
            double chi2 = chi * chi;
            return rdotv / sqrtMu * chi2 * c
                   + (1.0 - alpha * r0) * chi * chi2 * s
                   + r0 * chi
                   - target;
        }

        private static double Derivative(double chi, double alpha, double r0,
            double rdotv, double sqrtMu)
        {
            double z = alpha * chi * chi;
            Stumpff(z, out double c, out double s);
            double chi2 = chi * chi;
            return chi2 * c
                   + rdotv / sqrtMu * chi * (1.0 - z * s)
                   + r0 * (1.0 - z * c);
        }

        private static double Bisect(double alpha, double r0, double rdotv,
            double sqrtMu, double target, double seed, double dtSeconds)
        {
            double lo;
            double hi;

            if (dtSeconds > 0.0)
            {
                lo = 0.0;
                hi = Math.Max(1.0, Math.Abs(seed));
                while (Residual(hi, alpha, r0, rdotv, sqrtMu, target) < 0.0
                       && hi < 1e15)
                    hi *= 2.0;
            }
            else
            {
                hi = 0.0;
                lo = -Math.Max(1.0, Math.Abs(seed));
                while (Residual(lo, alpha, r0, rdotv, sqrtMu, target) > 0.0
                       && lo > -1e15)
                    lo *= 2.0;
            }

            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Residual(mid, alpha, r0, rdotv, sqrtMu, target) < 0.0) lo = mid;
                else hi = mid;
            }

            return 0.5 * (lo + hi);
        }

        /// <summary>Stumpff C(z) and S(z), with a series at the parabolic point.</summary>
        public static void Stumpff(double z, out double c, out double s)
        {
            if (z > 1e-5)
            {
                double root = Math.Sqrt(z);
                c = (1.0 - Math.Cos(root)) / z;
                s = (root - Math.Sin(root)) / (z * root);
            }
            else if (z < -1e-5)
            {
                double root = Math.Sqrt(-z);
                c = (Math.Cosh(root) - 1.0) / (-z);
                s = (Math.Sinh(root) - root) / ((-z) * root);
            }
            else
            {
                double z2 = z * z;
                double z3 = z2 * z;
                double z4 = z3 * z;
                c = 0.5 - z / 24.0 + z2 / 720.0 - z3 / 40320.0 + z4 / 3628800.0;
                s = 1.0 / 6.0 - z / 120.0 + z2 / 5040.0 - z3 / 362880.0
                    + z4 / 39916800.0;
            }
        }
    }
}
