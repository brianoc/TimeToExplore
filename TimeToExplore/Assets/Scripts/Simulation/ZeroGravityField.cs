using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>Absent gravity. Useful for kinematic checks and for tests that
    /// need a clean straight-line reference.</summary>
    public sealed class ZeroGravityField : IGravityField
    {
        public static readonly ZeroGravityField Instance = new ZeroGravityField();

        public void Prepare(Vector3d positionM, double julianDate)
        {
        }

        public Vector3d AccelerationAt(Vector3d positionM, double julianDate) => Vector3d.Zero;
    }
}
