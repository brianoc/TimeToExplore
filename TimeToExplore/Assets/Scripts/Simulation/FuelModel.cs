using System;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// First-pass fuel accounting: how much propellant a plan would burn. The
    /// ship carries no finite tank yet and its mass does not change in flight,
    /// so this is a cost readout rather than a budget. The number comes from the
    /// rocket equation at a fixed effective exhaust velocity, the design's one
    /// balance value for the torch drive (see the flight model).
    /// </summary>
    public static class FuelModel
    {
        /// <summary>Effective exhaust velocity, 10,000 km/s — about 3.3% of
        /// light speed. A balance value, not a modelled reactor.</summary>
        public const double ExhaustVelocityMps = 10_000_000.0;

        /// <summary>
        /// Propellant needed to buy <paramref name="deltaVMps"/> from a ship
        /// that arrives dry at <paramref name="dryMassKg"/>:
        /// <c>m_p = m_dry (exp(delta-v / v_e) - 1)</c>.
        /// </summary>
        public static double PropellantKg(double deltaVMps, double dryMassKg)
        {
            if (double.IsNaN(deltaVMps) || double.IsInfinity(deltaVMps) || deltaVMps < 0.0)
                throw new ArgumentOutOfRangeException(nameof(deltaVMps),
                    "delta-v must be finite and non-negative.");
            if (!(dryMassKg > 0.0) || double.IsInfinity(dryMassKg))
                throw new ArgumentOutOfRangeException(nameof(dryMassKg),
                    "dry mass must be finite and positive.");

            return dryMassKg * (Math.Exp(deltaVMps / ExhaustVelocityMps) - 1.0);
        }

        /// <summary>Mass at departure divided by mass at arrival, for a
        /// delta-v: <c>exp(delta-v / v_e)</c>.</summary>
        public static double MassRatio(double deltaVMps)
        {
            if (double.IsNaN(deltaVMps) || double.IsInfinity(deltaVMps) || deltaVMps < 0.0)
                throw new ArgumentOutOfRangeException(nameof(deltaVMps),
                    "delta-v must be finite and non-negative.");

            return Math.Exp(deltaVMps / ExhaustVelocityMps);
        }

        /// <summary>Propellant as a fraction of the mass at departure: the
        /// "propellant fraction" in the flight model's table. It is
        /// <c>1 - 1 / mass ratio</c>.</summary>
        public static double PropellantFraction(double deltaVMps)
        {
            double ratio = MassRatio(deltaVMps);
            return (ratio - 1.0) / ratio;
        }
    }
}
