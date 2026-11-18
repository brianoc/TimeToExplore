using System;

namespace TimeToExplore.Core
{
    /// <summary>
    /// The result of a <see cref="LambertSolver"/> solve. When
    /// <see cref="HasSolution"/> is false the velocity fields are zero; the
    /// transfer angle still describes the geometry that was rejected, so a
    /// caller can tell a 180° transfer from a prograde long-way one.
    /// </summary>
    public readonly struct LambertSolution
    {
        private LambertSolution(bool hasSolution, Vector3d departureVelocityMps,
            Vector3d arrivalVelocityMps, double transferAngleRad)
        {
            HasSolution = hasSolution;
            DepartureVelocityMps = departureVelocityMps;
            ArrivalVelocityMps = arrivalVelocityMps;
            TransferAngleRad = transferAngleRad;
        }

        /// <summary>True when the solve produced a conic and two velocities.</summary>
        public bool HasSolution { get; }

        /// <summary>The velocity at the departure position, v₁.</summary>
        public Vector3d DepartureVelocityMps { get; }

        /// <summary>The velocity at the arrival position, v₂.</summary>
        public Vector3d ArrivalVelocityMps { get; }

        /// <summary>
        /// The angle swept between the two positions, in radians, in the short
        /// direction (0 to π). It is reported even when no solution exists.
        /// </summary>
        public double TransferAngleRad { get; }

        internal static LambertSolution Solved(Vector3d departureVelocityMps,
            Vector3d arrivalVelocityMps, double transferAngleRad) =>
            new LambertSolution(true, departureVelocityMps, arrivalVelocityMps,
                transferAngleRad);

        internal static LambertSolution Rejected(double transferAngleRad) =>
            new LambertSolution(false, Vector3d.Zero, Vector3d.Zero, transferAngleRad);
    }

    /// <summary>
    /// Zero-revolution Lambert solver in universal variables. It answers the
    /// question the planner asks of a coast: given two positions and a time of
    /// flight, what departure and arrival velocities connect them?
    ///
    /// The solver implements one branch only: the <b>prograde short-way</b>
    /// arc. "Prograde" is measured about the ecliptic pole (+Z,
    /// <see cref="Vector3d.Up"/>), matching every orbit in the catalogue;
    /// "short-way" means the arc sweeps less than π radians. The long-way,
    /// retrograde, and multi-revolution branches are deliberately absent, so
    /// there is no sign flag or revolution count to pass.
    ///
    /// The universal variable formulation covers the ellipse, the parabola,
    /// and the hyperbola with one solve, and reuses
    /// <see cref="KeplerPropagator.Stumpff"/> for C(z) and S(z). The unknown
    /// is z = αχ²; time of flight increases monotonically with z on
    /// (-∞, 4π²), so a bracket-and-bisect solve is deterministic and does not
    /// depend on a good seed. The classic A term,
    /// √(r₁r₂(1 + cos Δν)), vanishes for a 180° transfer: the plane is
    /// undefined there and the velocities have no unique limit, so that
    /// geometry is rejected rather than divided through. The solver does not
    /// throw for it.
    ///
    /// This is the coast solver described in
    /// [Burn–coast–burn trips](/simulation/burn-coast-burn.md).
    /// </summary>
    public static class LambertSolver
    {
        // z = 4π² is one full revolution, where the elliptic time of flight
        // goes to infinity. Stop just short of it: any closer and C(z) rounds
        // to zero in double precision.
        private const double MaxZ = 4.0 * Math.PI * Math.PI * (1.0 - 1e-6);

        // Below this the Stumpff cosh/sinh overflow. It is far beyond any
        // transfer the game can ask for; a shorter time is reported as
        // infeasible instead of overflowing.
        private const double MinZ = -1e6;

        // Reject a 180° transfer when the A term is this small relative to
        // √(r₁r₂). The transfer is ill-conditioned long before A is exactly
        // zero, and the branch has no unique plane.
        private const double CollinearEpsilon = 1e-8;

        /// <summary>
        /// Solve the prograde short-way Lambert problem: find the zero-revolution
        /// conic that leaves <paramref name="departurePositionM"/> and reaches
        /// <paramref name="arrivalPositionM"/> after
        /// <paramref name="timeOfFlightSeconds"/>, sweeping less than π radians
        /// counter-clockwise about the ecliptic pole.
        ///
        /// The returned <see cref="LambertSolution.DepartureVelocityMps"/> and
        /// <see cref="LambertSolution.ArrivalVelocityMps"/> are the two velocities
        /// of that conic at the given positions. Propagation is one-directional:
        /// the time of flight must be positive and the caller supplies no
        /// intermediate states.
        ///
        /// A result with <see cref="LambertSolution.HasSolution"/> false is
        /// returned, not thrown, when the geometry is outside this branch or the
        /// solve cannot be bracketed:
        /// <list type="bullet">
        /// <item>the two positions are collinear, including an exactly or nearly
        /// 180° transfer, where the classic A term vanishes and the plane is
        /// undefined;</item>
        /// <item>the prograde arc would be the long way (>π) or the transfer
        /// plane contains the ecliptic pole, so no prograde short arc exists;</item>
        /// <item>the time of flight is shorter than the numerical lower bound or
        /// longer than a zero-revolution arc can take, or the solve does not
        /// converge to finite velocities.</item>
        /// </list>
        ///
        /// Genuinely invalid arguments do throw: a non-positive or non-finite
        /// gravitational parameter, a zero or non-finite position, or a
        /// non-positive or non-finite time of flight. These are not geometry
        /// choices; they cannot describe a Lambert problem at all.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="gravitationalParameter"/> is not positive and finite,
        /// a position is zero or not finite, or
        /// <paramref name="timeOfFlightSeconds"/> is not positive and finite.
        /// </exception>
        public static LambertSolution SolveShortWay(Vector3d departurePositionM,
            Vector3d arrivalPositionM, double timeOfFlightSeconds,
            double gravitationalParameter)
        {
            if (!(gravitationalParameter > 0.0) || !double.IsFinite(gravitationalParameter))
                throw new ArgumentOutOfRangeException(nameof(gravitationalParameter));
            if (!double.IsFinite(timeOfFlightSeconds) || !(timeOfFlightSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(timeOfFlightSeconds));

            double r1 = departurePositionM.Magnitude;
            double r2 = arrivalPositionM.Magnitude;
            if (!(r1 > 0.0) || !double.IsFinite(r1))
                throw new ArgumentOutOfRangeException(nameof(departurePositionM));
            if (!(r2 > 0.0) || !double.IsFinite(r2))
                throw new ArgumentOutOfRangeException(nameof(arrivalPositionM));

            double cosine = Math.Clamp(
                Vector3d.Dot(departurePositionM, arrivalPositionM) / (r1 * r2),
                -1.0, 1.0);
            double transferAngle = Math.Acos(cosine);

            // Prograde: the short arc must carry the position counter-clockwise
            // about the ecliptic pole. A plane that contains the pole has no
            // prograde sense and is rejected with the same test.
            Vector3d cross = Vector3d.Cross(departurePositionM, arrivalPositionM);
            if (!(Vector3d.Dot(cross, Vector3d.Up) > 0.0))
                return LambertSolution.Rejected(transferAngle);

            // The classic A term. It vanishes at 180°, and near there the
            // velocities lose precision faster than the solve gains it.
            double aTerm = Math.Sqrt(r1 * r2 * (1.0 + cosine));
            if (!(aTerm > CollinearEpsilon * Math.Sqrt(r1 * r2)))
                return LambertSolution.Rejected(transferAngle);

            double sqrtMu = Math.Sqrt(gravitationalParameter);
            double z = SolveZ(r1, r2, aTerm, sqrtMu, timeOfFlightSeconds);
            if (!double.IsFinite(z))
                return LambertSolution.Rejected(transferAngle);

            KeplerPropagator.Stumpff(z, out double c, out double s);
            if (!(c > 0.0))
                return LambertSolution.Rejected(transferAngle);

            double y = r1 + r2 + aTerm * (z * s - 1.0) / Math.Sqrt(c);
            if (!(y > 0.0) || !double.IsFinite(y))
                return LambertSolution.Rejected(transferAngle);

            double f = 1.0 - y / r1;
            double g = aTerm * Math.Sqrt(y / gravitationalParameter);
            double gdot = 1.0 - y / r2;
            if (!(Math.Abs(g) > 0.0) || !double.IsFinite(g))
                return LambertSolution.Rejected(transferAngle);

            Vector3d departureVelocity = (arrivalPositionM - departurePositionM * f) / g;
            Vector3d arrivalVelocity = (arrivalPositionM * gdot - departurePositionM) / g;
            if (!IsFinite(departureVelocity) || !IsFinite(arrivalVelocity))
                return LambertSolution.Rejected(transferAngle);

            return LambertSolution.Solved(departureVelocity, arrivalVelocity,
                transferAngle);
        }

        /// <summary>
        /// Solve the universal-variable time-of-flight equation t(z) = Δt for z. It is monotone increasing
        /// on the branch, so a bracket walk followed by bisection is enough;
        /// an invalid z (y ≤ 0, C ≤ 0) is treated as below the root.
        /// </summary>
        private static double SolveZ(double r1, double r2, double aTerm, double sqrtMu,
            double timeOfFlightSeconds)
        {
            double f0 = TimeOfFlight(0.0, r1, r2, aTerm, sqrtMu) - timeOfFlightSeconds;
            if (!double.IsFinite(f0))
                return double.NaN;

            double lo;
            double hi;

            if (f0 > 0.0)
            {
                // The requested time is shorter than the parabolic arc, so the
                // solution is hyperbolic and z is negative.
                hi = 0.0;
                lo = -1.0;
                while (true)
                {
                    double residual = TimeOfFlight(lo, r1, r2, aTerm, sqrtMu)
                                      - timeOfFlightSeconds;
                    if (!double.IsFinite(residual) || residual < 0.0) break;
                    hi = lo;
                    lo *= 2.0;
                    if (lo < MinZ) return double.NaN;
                }
            }
            else if (f0 < 0.0)
            {
                // The requested time is longer than the parabolic arc, so the
                // solution is elliptic and z is positive.
                lo = 0.0;
                hi = MaxZ;
                double residual = TimeOfFlight(hi, r1, r2, aTerm, sqrtMu)
                                  - timeOfFlightSeconds;
                if (!double.IsFinite(residual) || residual < 0.0) return double.NaN;
            }
            else
            {
                return 0.0;
            }

            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5 * (lo + hi);
                double residual = TimeOfFlight(mid, r1, r2, aTerm, sqrtMu)
                                  - timeOfFlightSeconds;
                if (!double.IsFinite(residual) || residual < 0.0) lo = mid;
                else hi = mid;

                if (hi - lo <= 1e-13 * Math.Max(1.0, Math.Abs(mid))) break;
            }

            return 0.5 * (lo + hi);
        }

        /// <summary>
        /// Universal-variable time of flight for a trial z, in seconds. NaN
        /// marks a z outside the valid branch; the solve treats it as "too
        /// small".
        /// </summary>
        private static double TimeOfFlight(double z, double r1, double r2, double aTerm,
            double sqrtMu)
        {
            KeplerPropagator.Stumpff(z, out double c, out double s);
            if (!(c > 0.0)) return double.NaN;

            double y = r1 + r2 + aTerm * (z * s - 1.0) / Math.Sqrt(c);
            if (!(y > 0.0) || !double.IsFinite(y)) return double.NaN;

            double x = Math.Sqrt(y / c);
            double time = (x * x * x * s + aTerm * Math.Sqrt(y)) / sqrtMu;
            return double.IsFinite(time) ? time : double.NaN;
        }

        private static bool IsFinite(Vector3d value) =>
            double.IsFinite(value.x) && double.IsFinite(value.y)
            && double.IsFinite(value.z);
    }
}
