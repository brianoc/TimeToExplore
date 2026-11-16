using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The ship's integrated state: heliocentric position and velocity in SI
    /// units at a Julian date, plus the mass bookkeeping that the fuel model
    /// will use later. Mass does not affect the trajectory yet: the drive is
    /// assumed to hold a constant acceleration, per ADR-004.
    /// </summary>
    public struct ShipState
    {
        /// <summary>Design acceleration of the torch drive, one Earth gravity.</summary>
        public const double DefaultMaxAccelerationMps2 = 10.0;

        /// <summary>Mass of the ship without propellant. Balance value only.</summary>
        public const double DefaultDryMassKg = 1_000_000.0;

        public double JulianDate;
        public Vector3d PositionM;
        public Vector3d VelocityMps;
        public double DryMassKg;
        public double PropellantKg;

        public ShipState(double julianDate, Vector3d positionM, Vector3d velocityMps)
            : this(julianDate, positionM, velocityMps, DefaultDryMassKg, 0.0)
        {
        }

        public ShipState(double julianDate, Vector3d positionM, Vector3d velocityMps,
            double dryMassKg, double propellantKg)
        {
            JulianDate = julianDate;
            PositionM = positionM;
            VelocityMps = velocityMps;
            DryMassKg = dryMassKg;
            PropellantKg = propellantKg;
        }

        public double TotalMassKg => DryMassKg + PropellantKg;

        /// <summary>Position relative to a body, given the body's state.</summary>
        public Vector3d PositionRelativeTo(Vector3d bodyPositionM) => PositionM - bodyPositionM;

        /// <summary>Velocity relative to a body, given the body's state.</summary>
        public Vector3d VelocityRelativeTo(Vector3d bodyVelocityMps) => VelocityMps - bodyVelocityMps;

        /// <summary>Distance to a body at a known position.</summary>
        public double DistanceTo(Vector3d bodyPositionM) => Vector3d.Distance(PositionM, bodyPositionM);

        public ShipState WithVelocity(Vector3d velocityMps)
        {
            ShipState copy = this;
            copy.VelocityMps = velocityMps;
            return copy;
        }

        public ShipState WithJulianDate(double julianDate)
        {
            ShipState copy = this;
            copy.JulianDate = julianDate;
            return copy;
        }

        public override string ToString() =>
            $"JD {JulianDate:F6} r={PositionM.Magnitude:E4} m v={VelocityMps.Magnitude:E4} m/s";
    }
}
