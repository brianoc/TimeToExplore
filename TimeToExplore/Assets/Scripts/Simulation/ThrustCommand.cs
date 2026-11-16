using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// A commanded thrust expressed as an acceleration vector. The drive is
    /// throttleable from zero to the ship's design acceleration, so magnitude
    /// is free and direction is free; there is no attitude state yet.
    /// </summary>
    public struct ThrustCommand
    {
        public Vector3d AccelerationMps2;

        public ThrustCommand(Vector3d accelerationMps2)
        {
            AccelerationMps2 = accelerationMps2;
        }

        public static ThrustCommand None => new ThrustCommand(Vector3d.Zero);

        public double MagnitudeMps2 => AccelerationMps2.Magnitude;

        public bool IsActive => AccelerationMps2.SqrMagnitude > 1e-24;

        public Vector3d Direction => AccelerationMps2.Normalized;

        /// <summary>Scale the command down to a maximum acceleration. Direction
        /// is preserved and a command already inside the limit is unchanged.</summary>
        public ThrustCommand ClampedTo(double maxAccelerationMps2)
        {
            if (maxAccelerationMps2 < 0.0)
                throw new ArgumentOutOfRangeException(nameof(maxAccelerationMps2));

            double magnitude = AccelerationMps2.Magnitude;
            if (magnitude <= maxAccelerationMps2 || magnitude <= 0.0) return this;
            return new ThrustCommand(AccelerationMps2 * (maxAccelerationMps2 / magnitude));
        }

        /// <summary>Throttle setting in the range 0..1 for a given design
        /// acceleration, for display and for the future fuel model.</summary>
        public double Throttle(double maxAccelerationMps2)
        {
            if (maxAccelerationMps2 <= 0.0) return 0.0;
            return Math.Clamp(AccelerationMps2.Magnitude / maxAccelerationMps2, 0.0, 1.0);
        }

        public override string ToString() => $"{MagnitudeMps2:F3} m/s²";
    }
}
