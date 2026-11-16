using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    public struct IntegrationReport
    {
        public int Substeps;
        public double MinStepSeconds;
        public double MaxStepSeconds;
        public double ElapsedSeconds;
        public bool StepLimitReached;
        public bool CollisionDetected;
        public string CollisionBodyId;

        public void Add(IntegrationReport other)
        {
            Substeps += other.Substeps;
            if (other.MinStepSeconds < MinStepSeconds) MinStepSeconds = other.MinStepSeconds;
            if (other.MaxStepSeconds > MaxStepSeconds) MaxStepSeconds = other.MaxStepSeconds;
            ElapsedSeconds += other.ElapsedSeconds;
            StepLimitReached |= other.StepLimitReached;
            if (other.CollisionDetected)
            {
                CollisionDetected = true;
                CollisionBodyId = other.CollisionBodyId;
            }
        }
    }

    /// <summary>
    /// Fixed-step fourth-order Runge-Kutta integration of the ship under
    /// gravity plus its own thrust, with an adaptive step bounded by the local
    /// dynamical time. The integration advances to a Julian date rather than a
    /// frame, so the result does not depend on frame rate or time warp.
    ///
    /// The step is limited by the nearest active body: about 1% of the local
    /// orbital timescale and 1% of the time to cross the local radius. In
    /// heliocentric cruise that is minutes; in a parking orbit it is about ten
    /// seconds. Callers may pass a tighter hint, which the plan supplies per arc.
    /// </summary>
    public sealed class ShipIntegrator
    {
        private readonly IGravityField _gravity;

        public ShipIntegrator(IGravityField gravity)
        {
            _gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
        }

        public IGravityField Gravity => _gravity;

        /// <summary>Hard upper bound on a single step.</summary>
        public double MaxStepSeconds { get; set; } = 3600.0;

        /// <summary>Fraction of the local orbital timescale per step.</summary>
        public double OrbitSafety { get; set; } = 0.01;

        /// <summary>Fraction of the local radius a step may cross.</summary>
        public double RadiusSafety { get; set; } = 0.01;

        /// <summary>Smallest step the controller will take before reporting a stall.</summary>
        public double MinStepSeconds { get; set; } = 1e-3;

        public int MaxSubsteps { get; set; } = 500_000;

        public IntegrationReport Advance(ref ShipState state, double toJulianDate,
            Func<ShipState, double, ThrustCommand> thrust)
        {
            return Advance(ref state, toJulianDate, thrust, 0.0);
        }

        public IntegrationReport Advance(ref ShipState state, double toJulianDate,
            Func<ShipState, double, ThrustCommand> thrust, double maxStepSeconds)
        {
            if (thrust == null) throw new ArgumentNullException(nameof(thrust));

            var report = new IntegrationReport
            {
                MinStepSeconds = double.PositiveInfinity,
                MaxStepSeconds = 0.0
            };

            double span = (toJulianDate - state.JulianDate) * JulianDate.SecondsPerDay;
            report.ElapsedSeconds = span;
            if (!(span > 0.0)) return report;

            double startJulianDate = state.JulianDate;
            double elapsed = 0.0;
            int steps = 0;

            while (elapsed < span)
            {
                if (steps >= MaxSubsteps)
                {
                    report.StepLimitReached = true;
                    break;
                }

                double remaining = span - elapsed;
                _gravity.Prepare(state.PositionM, state.JulianDate);

                if (_gravity is GravityField field)
                {
                    if (field.NearestClearanceM < 0.0)
                    {
                        report.CollisionDetected = true;
                        report.CollisionBodyId = field.NearestBodyId;
                        report.Substeps = steps;
                        report.MinStepSeconds = 0.0;
                        return report;
                    }
                }

                double step = ChooseStep(state, remaining, maxStepSeconds);
                if (!(step > 0.0))
                {
                    report.StepLimitReached = true;
                    break;
                }

                // Snap the last step to the requested date exactly. Without this
                // the loop can take a run of epsilon steps chasing round-off.
                bool finalStep = step >= remaining;
                if (finalStep) step = remaining;

                double h = step;
                double jd = startJulianDate + elapsed / JulianDate.SecondsPerDay;
                double halfDays = 0.5 * h / JulianDate.SecondsPerDay;

                ShipState probe = state;
                probe.JulianDate = jd;

                Vector3d k1Position = probe.VelocityMps;
                Vector3d k1Velocity = Acceleration(probe, jd, thrust);

                ShipState stage = probe;
                stage.PositionM = probe.PositionM + k1Position * (0.5 * h);
                stage.VelocityMps = probe.VelocityMps + k1Velocity * (0.5 * h);
                stage.JulianDate = jd + halfDays;
                Vector3d k2Position = stage.VelocityMps;
                Vector3d k2Velocity = Acceleration(stage, jd + halfDays, thrust);

                stage.PositionM = probe.PositionM + k2Position * (0.5 * h);
                stage.VelocityMps = probe.VelocityMps + k2Velocity * (0.5 * h);
                stage.JulianDate = jd + halfDays;
                Vector3d k3Position = stage.VelocityMps;
                Vector3d k3Velocity = Acceleration(stage, jd + halfDays, thrust);

                stage.PositionM = probe.PositionM + k3Position * h;
                stage.VelocityMps = probe.VelocityMps + k3Velocity * h;
                stage.JulianDate = jd + h / JulianDate.SecondsPerDay;
                Vector3d k4Position = stage.VelocityMps;
                Vector3d k4Velocity = Acceleration(stage, jd + h / JulianDate.SecondsPerDay, thrust);

                state.PositionM = probe.PositionM
                                  + (k1Position + k2Position * 2.0 + k3Position * 2.0 + k4Position) * (h / 6.0);
                state.VelocityMps = probe.VelocityMps
                                    + (k1Velocity + k2Velocity * 2.0 + k3Velocity * 2.0 + k4Velocity) * (h / 6.0);

                elapsed = finalStep ? span : elapsed + h;
                state.JulianDate = startJulianDate + elapsed / JulianDate.SecondsPerDay;
                steps++;

                if (h < report.MinStepSeconds) report.MinStepSeconds = h;
                if (h > report.MaxStepSeconds) report.MaxStepSeconds = h;
            }

            report.Substeps = steps;
            if (double.IsPositiveInfinity(report.MinStepSeconds)) report.MinStepSeconds = 0.0;
            return report;
        }

        private Vector3d Acceleration(ShipState state, double julianDate,
            Func<ShipState, double, ThrustCommand> thrust)
        {
            ThrustCommand command = thrust(state, julianDate);
            return _gravity.AccelerationAt(state.PositionM, julianDate) + command.AccelerationMps2;
        }

        private double ChooseStep(ShipState state, double remaining, double maxStepSeconds)
        {
            double step = Math.Min(remaining, MaxStepSeconds);
            if (maxStepSeconds > 0.0) step = Math.Min(step, maxStepSeconds);

            double speed = state.VelocityMps.Magnitude;

            if (_gravity is GravityField field)
            {
                for (int i = 0; i < field.ActiveBodyCount; i++)
                {
                    BodyRuntime body = field.ActiveBodyAt(i);
                    double distance = Vector3d.Distance(
                        field.ActiveBodyPositionM(i), state.PositionM);

                    if (body.Gm > 0.0 && distance > body.RadiusM)
                    {
                        double orbitalTimescale = Math.Sqrt(
                            distance * distance * distance / body.Gm);
                        step = Math.Min(step, OrbitSafety * orbitalTimescale);
                    }

                    if (speed > 1e-9 && distance > 0.0)
                        step = Math.Min(step, RadiusSafety * distance / speed);
                }
            }

            if (step < MinStepSeconds) step = MinStepSeconds;
            return step;
        }
    }
}
