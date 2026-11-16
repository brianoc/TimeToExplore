using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Gravitational acceleration at a point and time. The planets stay on
    /// their rails, so the field is a function of position and Julian date
    /// only; the ship never perturbs it.
    /// </summary>
    public interface IGravityField
    {
        /// <summary>Select the bodies that matter near a position. Callers that
        /// evaluate the field along a path should call this before a batch of
        /// <see cref="AccelerationAt"/> queries; the integrator does so once per
        /// substep. It is safe to call more often than necessary.</summary>
        void Prepare(Vector3d positionM, double julianDate);

        /// <summary>Acceleration in m/s² in the heliocentric ecliptic frame.</summary>
        Vector3d AccelerationAt(Vector3d positionM, double julianDate);
    }
}
