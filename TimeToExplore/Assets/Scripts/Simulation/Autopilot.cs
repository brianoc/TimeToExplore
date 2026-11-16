using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Tracks a trajectory plan. The feed-forward term is the plan's own
    /// acceleration minus the gravity at the ship, which reproduces the
    /// reference curve exactly when the ship starts on it. A small
    /// position/velocity feedback absorbs integrator error and a starting state
    /// that is a little off the plan.
    ///
    /// The feedback is deliberately soft: a time constant of a few minutes is
    /// fast compared with a three-day transfer and slow compared with a
    /// parking orbit.
    /// </summary>
    public sealed class Autopilot
    {
        private readonly IGravityField _gravity;
        private readonly TrajectoryPlan _plan;

        public Autopilot(IGravityField gravity, TrajectoryPlan plan)
        {
            _gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        public TrajectoryPlan Plan => _plan;

        /// <summary>Position error gain, in 1/s². Together with the velocity
        /// gain this is a critically damped second-order tracker.</summary>
        public double PositionGainPerSecond2 { get; set; } = 1.0e-5;

        /// <summary>Velocity error gain, in 1/s.</summary>
        public double VelocityGainPerSecond { get; set; } = 6.3e-3;

        public double MaxAccelerationMps2 => _plan.MaxAccelerationMps2;

        public ThrustCommand Compute(ShipState state)
        {
            return Compute(state, state.JulianDate);
        }

        public ThrustCommand Compute(ShipState state, double julianDate)
        {
            if (_plan.IsComplete(julianDate)) return ThrustCommand.None;

            Vector3d referencePosition = _plan.ReferencePositionAt(julianDate);
            Vector3d referenceVelocity = _plan.ReferenceVelocityAt(julianDate);
            Vector3d referenceAcceleration = _plan.ReferenceAccelerationAt(julianDate);
            Vector3d localGravity = _gravity.AccelerationAt(state.PositionM, julianDate);

            Vector3d command = referenceAcceleration - localGravity
                + (referencePosition - state.PositionM) * PositionGainPerSecond2
                + (referenceVelocity - state.VelocityMps) * VelocityGainPerSecond;

            return new ThrustCommand(command).ClampedTo(MaxAccelerationMps2);
        }

        /// <summary>Fly the whole plan, arc by arc, with each arc's own step hint.</summary>
        public IntegrationReport Fly(ref ShipState state, ShipIntegrator integrator)
        {
            if (integrator == null) throw new ArgumentNullException(nameof(integrator));

            var total = new IntegrationReport { MinStepSeconds = double.PositiveInfinity };
            foreach (PlanArc arc in _plan.Arcs)
            {
                IntegrationReport leg = integrator.Advance(ref state, arc.EndJulianDate,
                    Compute, arc.SuggestedMaxStepSeconds);
                total.Add(leg);
                if (leg.CollisionDetected || leg.StepLimitReached) break;
            }

            if (double.IsPositiveInfinity(total.MinStepSeconds)) total.MinStepSeconds = 0.0;
            return total;
        }
    }
}
