using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// One leg of a plan, parameterised by u ∈ [0, 1]. Position, velocity, and
    /// acceleration are in the plan's inertial frame and are derivatives with
    /// respect to <em>real time</em>. The mapping from u to time is the path's
    /// own choice: a quintic is linear in time, while a conic is linear in true
    /// anomaly so periapsis is sampled densely. <see cref="SecondsAt"/> and
    /// <see cref="ParameterAtSeconds"/> are the only sanctioned way to cross
    /// between the two.
    /// </summary>
    public interface IArcPath
    {
        double DurationSeconds { get; }

        Vector3d PositionAt(double u);
        Vector3d VelocityAt(double u);
        Vector3d AccelerationAt(double u);

        /// <summary>Time in seconds from the arc's start at parameter u.</summary>
        double SecondsAt(double u);

        /// <summary>Parameter u at a time offset in seconds from the arc's start.</summary>
        double ParameterAtSeconds(double seconds);
    }
}
