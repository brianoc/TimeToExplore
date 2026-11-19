using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// A finite burn around one body: a constant thrust magnitude along the
    /// instantaneous velocity (prograde or retrograde), or along a fixed inertial
    /// direction, integrated through the body's two-body gravity. Unlike a
    /// quintic, the thrust does not ramp from zero; the arc is the fixed-thrust
    /// burn the drive can actually fly, and it meets a coast with a step in
    /// acceleration rather than a blend
    /// ([ADR-015](/decisions/adr-015-fixed-thrust-burns.md)).
    ///
    /// A fixed direction is the honest model of a real burn: the ship holds one
    /// attitude instead of tracking its velocity. The planner uses it for the
    /// burn–coast–burn brake, whose velocity change is not along the velocity.
    ///
    /// The parameter u is linear in time. Local samples are stored at a uniform
    /// step and interpolated with a cubic Hermite in position, so position and
    /// velocity are continuous at the seams. The heliocentric path adds the
    /// centre's own ephemeris, exactly as <see cref="ConicArc"/> does.
    /// </summary>
    public sealed class FiniteBurnArc : IArcPath
    {
        /// <summary>Floor on integration samples, so a short burn is still smooth.</summary>
        public const int MinimumSamples = 96;

        /// <summary>Ceiling on integration samples, so a long burn cannot stall a frame.</summary>
        public const int MaximumSamples = 2_048;

        private const double TargetStepSeconds = 4.0;

        private readonly SolarSystemModel _model;
        private readonly BodyRuntime _centre;
        private readonly double _startJulianDate;
        private readonly double _durationSeconds;
        private readonly double _accelerationMps2;
        private readonly bool _retrograde;
        private readonly Vector3d _fallbackDirection;
        private readonly Vector3d[] _localPositionsM;
        private readonly Vector3d[] _localVelocitiesMps;
        private readonly Vector3d _fixedThrustDirection;

        private FiniteBurnArc(SolarSystemModel model, BodyRuntime centre, double startJulianDate,
            double durationSeconds, double accelerationMps2, bool retrograde,
            Vector3d fallbackDirection, Vector3d[] localPositionsM,
            Vector3d[] localVelocitiesMps, Vector3d fixedThrustDirection = default)
        {
            _model = model;
            _centre = centre;
            _startJulianDate = startJulianDate;
            _durationSeconds = durationSeconds;
            _accelerationMps2 = accelerationMps2;
            _retrograde = retrograde;
            _fallbackDirection = fallbackDirection;
            _localPositionsM = localPositionsM;
            _localVelocitiesMps = localVelocitiesMps;
            _fixedThrustDirection = fixedThrustDirection;
        }

        public double StartJulianDate => _startJulianDate;
        public double DurationSeconds => _durationSeconds;
        public BodyRuntime Centre => _centre;
        public double AccelerationMps2 => _accelerationMps2;
        public bool IsRetrograde => _retrograde;

        /// <summary>The local state at the end of the burn.</summary>
        public Vector3d EndLocalPositionM => _localPositionsM[_localPositionsM.Length - 1];

        public Vector3d EndLocalVelocityMps => _localVelocitiesMps[_localVelocitiesMps.Length - 1];

        /// <summary>
        /// Integrate a constant-magnitude burn from a body-relative state. The
        /// model is used only to place the centre at each queried date, never
        /// mutated. <paramref name="samples"/> above zero overrides the automatic
        /// sample count; the solver uses a coarser table than the final arc.
        /// <paramref name="fixedThrustDirection"/>, when given, holds the thrust
        /// along that inertial direction instead of the velocity.
        /// </summary>
        public static FiniteBurnArc Create(SolarSystemModel model, BodyRuntime centre,
            double startJulianDate, double durationSeconds,
            Vector3d localStartPositionM, Vector3d localStartVelocityMps,
            double accelerationMps2, bool retrograde, int samples = 0,
            Vector3d? fixedThrustDirection = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            if (!(durationSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (!(accelerationMps2 > 0.0))
                throw new ArgumentOutOfRangeException(nameof(accelerationMps2));

            int count = SampleCount(durationSeconds, samples);
            var positions = new Vector3d[count + 1];
            var velocities = new Vector3d[count + 1];

            Vector3d position = localStartPositionM;
            Vector3d velocity = localStartVelocityMps;
            Vector3d fallback = FallbackDirection(position, velocity);
            Vector3d direction = fixedThrustDirection.HasValue
                ? Normalized(fixedThrustDirection.Value)
                : Vector3d.Zero;
            bool directed = direction.SqrMagnitude > 0.5;

            positions[0] = position;
            velocities[0] = velocity;

            double step = durationSeconds / count;
            for (int i = 0; i < count; i++)
            {
                Step(centre.Gm, accelerationMps2, retrograde, fallback, direction, directed,
                    ref position, ref velocity, step);
                positions[i + 1] = position;
                velocities[i + 1] = velocity;
            }

            return new FiniteBurnArc(model, centre, startJulianDate, durationSeconds,
                accelerationMps2, retrograde, fallback, positions, velocities,
                directed ? direction : Vector3d.Zero);
        }

        /// <summary>
        /// Advance a burn without building the interpolation table. The planner's
        /// capture solver calls this while it searches for a burn duration.
        /// </summary>
        public static void Simulate(BodyRuntime centre, Vector3d localStartPositionM,
            Vector3d localStartVelocityMps, double accelerationMps2, bool retrograde,
            double durationSeconds, int samples,
            out Vector3d endPositionM, out Vector3d endVelocityMps,
            out double minimumRadiusM)
        {
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            if (!(durationSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));

            int count = SampleCount(durationSeconds, samples);
            Vector3d position = localStartPositionM;
            Vector3d velocity = localStartVelocityMps;
            Vector3d fallback = FallbackDirection(position, velocity);

            minimumRadiusM = position.Magnitude;
            double step = durationSeconds / count;
            for (int i = 0; i < count; i++)
            {
                Step(centre.Gm, accelerationMps2, retrograde, fallback,
                    Vector3d.Zero, false, ref position, ref velocity, step);
                double radius = position.Magnitude;
                if (radius < minimumRadiusM) minimumRadiusM = radius;
            }

            endPositionM = position;
            endVelocityMps = velocity;
        }

        /// <summary>
        /// Integrate a burn until its radius first falls to
        /// <paramref name="targetRadiusM"/> and return the linearly interpolated
        /// crossing state. The hybrid capture's injection point is a radius, not
        /// a time, so this is the solver's position-conditioned step.
        /// </summary>
        public static bool SimulateUntilRadius(BodyRuntime centre,
            Vector3d localStartPositionM, Vector3d localStartVelocityMps,
            double accelerationMps2, bool retrograde, double targetRadiusM,
            double maximumSeconds, out double crossingSeconds,
            out Vector3d endPositionM, out Vector3d endVelocityMps)
        {
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            if (!(maximumSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(maximumSeconds));

            crossingSeconds = 0.0;
            endPositionM = localStartPositionM;
            endVelocityMps = localStartVelocityMps;

            Vector3d position = localStartPositionM;
            Vector3d velocity = localStartVelocityMps;
            Vector3d fallback = FallbackDirection(position, velocity);

            double previousRadius = position.Magnitude;
            if (previousRadius <= targetRadiusM) return true;

            int count = Math.Clamp((int)Math.Ceiling(maximumSeconds / 2.0), 256, 4096);
            double step = maximumSeconds / count;

            for (int i = 0; i < count; i++)
            {
                Vector3d previousPosition = position;
                Vector3d previousVelocity = velocity;

                Step(centre.Gm, accelerationMps2, retrograde, fallback,
                    Vector3d.Zero, false, ref position, ref velocity, step);

                double radius = position.Magnitude;
                if (radius > targetRadiusM)
                {
                    previousRadius = radius;
                    continue;
                }

                double drop = previousRadius - radius;
                double fraction = drop > 0.0
                    ? Math.Clamp((previousRadius - targetRadiusM) / drop, 0.0, 1.0)
                    : 0.0;

                crossingSeconds = (i + fraction) * step;
                endPositionM = previousPosition
                               + (position - previousPosition) * fraction;
                endVelocityMps = previousVelocity
                                 + (velocity - previousVelocity) * fraction;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Integrate a burn backwards from its end state and return the state it
        /// started from. The forward dynamics are unchanged: constant thrust along
        /// the instantaneous velocity (prograde, or retrograde), or along a fixed
        /// direction, through the two-body field. Running them with a negative
        /// time step is the exact reverse flow, so a planner can place a burn that
        /// must end on a state another arc already needs, without a shooting
        /// solve.
        /// </summary>
        public static void SimulateBackward(BodyRuntime centre, Vector3d localEndPositionM,
            Vector3d localEndVelocityMps, double accelerationMps2, bool retrograde,
            double durationSeconds, int samples,
            out Vector3d localStartPositionM, out Vector3d localStartVelocityMps,
            Vector3d? fixedThrustDirection = null)
        {
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            if (!(durationSeconds > 0.0))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));

            int count = SampleCount(durationSeconds, samples);
            Vector3d position = localEndPositionM;
            Vector3d velocity = localEndVelocityMps;
            Vector3d fallback = FallbackDirection(position, velocity);
            Vector3d fixedDirection = fixedThrustDirection.HasValue
                ? Normalized(fixedThrustDirection.Value)
                : Vector3d.Zero;
            bool directed = fixedDirection.SqrMagnitude > 0.5;

            double step = -durationSeconds / count;
            for (int i = 0; i < count; i++)
                Step(centre.Gm, accelerationMps2, retrograde, fallback,
                    fixedDirection, directed, ref position, ref velocity, step);

            localStartPositionM = position;
            localStartVelocityMps = velocity;
        }

        public Vector3d PositionAt(double u)
        {
            LocalStateAt(u, out Vector3d localPosition, out _);
            _model.ComputeState(_centre, JulianDateFor(u), out Vector3d centrePosition, out _);
            return centrePosition + localPosition;
        }

        public Vector3d VelocityAt(double u)
        {
            LocalStateAt(u, out _, out Vector3d localVelocity);
            _model.ComputeState(_centre, JulianDateFor(u), out _, out Vector3d centreVelocity);
            return centreVelocity + localVelocity;
        }

        public Vector3d AccelerationAt(double u)
        {
            LocalStateAt(u, out Vector3d localPosition, out Vector3d localVelocity);
            double julianDate = JulianDateFor(u);

            return _model.ComputeAcceleration(_centre, julianDate)
                   + LocalAcceleration(_centre.Gm, localPosition, localVelocity,
                       _accelerationMps2, _retrograde, _fallbackDirection,
                       _fixedThrustDirection, _fixedThrustDirection.SqrMagnitude > 0.5);
        }

        public double SecondsAt(double u) =>
            Math.Clamp(u, 0.0, 1.0) * _durationSeconds;

        public double ParameterAtSeconds(double seconds) =>
            _durationSeconds > 0.0
                ? Math.Clamp(seconds / _durationSeconds, 0.0, 1.0)
                : 0.0;

        /// <summary>Local position and velocity at parameter u.</summary>
        public void LocalStateAt(double u, out Vector3d localPositionM,
            out Vector3d localVelocityMps)
        {
            u = Math.Clamp(u, 0.0, 1.0);
            int last = _localPositionsM.Length - 1;
            if (u <= 0.0)
            {
                localPositionM = _localPositionsM[0];
                localVelocityMps = _localVelocitiesMps[0];
                return;
            }

            if (u >= 1.0)
            {
                localPositionM = _localPositionsM[last];
                localVelocityMps = _localVelocitiesMps[last];
                return;
            }

            double scaled = u * last;
            int index = (int)Math.Floor(scaled);
            double s = scaled - index;
            double step = _durationSeconds / last;

            Vector3d p0 = _localPositionsM[index];
            Vector3d v0 = _localVelocitiesMps[index];
            Vector3d p1 = _localPositionsM[index + 1];
            Vector3d v1 = _localVelocitiesMps[index + 1];

            double s2 = s * s;
            double s3 = s2 * s;

            double h00 = 2.0 * s3 - 3.0 * s2 + 1.0;
            double h10 = s3 - 2.0 * s2 + s;
            double h01 = -2.0 * s3 + 3.0 * s2;
            double h11 = s3 - s2;

            localPositionM = p0 * h00 + v0 * (h10 * step)
                             + p1 * h01 + v1 * (h11 * step);

            double dh00 = 6.0 * s2 - 6.0 * s;
            double dh10 = 3.0 * s2 - 4.0 * s + 1.0;
            double dh01 = -6.0 * s2 + 6.0 * s;
            double dh11 = 3.0 * s2 - 2.0 * s;

            localVelocityMps = (p0 * dh00 + v0 * (dh10 * step)
                                + p1 * dh01 + v1 * (dh11 * step)) / step;
        }

        private double JulianDateFor(double u) =>
            _startJulianDate + SecondsAt(u) / JulianDate.SecondsPerDay;

        private static int SampleCount(double durationSeconds, int requested)
        {
            if (requested > 0)
                return Math.Clamp(requested, MinimumSamples, MaximumSamples);

            int automatic = (int)Math.Ceiling(durationSeconds / TargetStepSeconds);
            return Math.Clamp(automatic, MinimumSamples, MaximumSamples);
        }

        private static Vector3d FallbackDirection(Vector3d position, Vector3d velocity)
        {
            if (velocity.SqrMagnitude > 1e-12) return velocity.Normalized;
            return position.SqrMagnitude > 1e-12 ? position.Normalized : Vector3d.Zero;
        }

        private static Vector3d LocalAcceleration(double mu, Vector3d position,
            Vector3d velocity, double accelerationMps2, bool retrograde,
            Vector3d fallbackDirection, Vector3d fixedDirection, bool directed)
        {
            double radiusSquared = position.SqrMagnitude;
            double radius = Math.Sqrt(radiusSquared);
            Vector3d gravity = radius > 0.0
                ? position * (-mu / (radiusSquared * radius))
                : Vector3d.Zero;

            if (directed)
                return gravity + fixedDirection * accelerationMps2;

            Vector3d direction = velocity.SqrMagnitude > 1e-12
                ? velocity.Normalized
                : fallbackDirection;
            if (retrograde) direction = -direction;

            return gravity + direction * accelerationMps2;
        }

        private static void Step(double mu, double accelerationMps2, bool retrograde,
            Vector3d fallbackDirection, Vector3d fixedDirection, bool directed,
            ref Vector3d position, ref Vector3d velocity, double h)
        {
            Vector3d k1Position = velocity;
            Vector3d k1Velocity = LocalAcceleration(mu, position, velocity,
                accelerationMps2, retrograde, fallbackDirection, fixedDirection, directed);

            Vector3d p2 = position + k1Position * (0.5 * h);
            Vector3d v2 = velocity + k1Velocity * (0.5 * h);
            Vector3d k2Position = v2;
            Vector3d k2Velocity = LocalAcceleration(mu, p2, v2,
                accelerationMps2, retrograde, fallbackDirection, fixedDirection, directed);

            Vector3d p3 = position + k2Position * (0.5 * h);
            Vector3d v3 = velocity + k2Velocity * (0.5 * h);
            Vector3d k3Position = v3;
            Vector3d k3Velocity = LocalAcceleration(mu, p3, v3,
                accelerationMps2, retrograde, fallbackDirection, fixedDirection, directed);

            Vector3d p4 = position + k3Position * h;
            Vector3d v4 = velocity + k3Velocity * h;
            Vector3d k4Position = v4;
            Vector3d k4Velocity = LocalAcceleration(mu, p4, v4,
                accelerationMps2, retrograde, fallbackDirection, fixedDirection, directed);

            position += (k1Position + 2.0 * k2Position + 2.0 * k3Position + k4Position)
                        * (h / 6.0);
            velocity += (k1Velocity + 2.0 * k2Velocity + 2.0 * k3Velocity + k4Velocity)
                        * (h / 6.0);
        }

        private static Vector3d Normalized(Vector3d value)
        {
            return value.SqrMagnitude > 1e-12 ? value.Normalized : Vector3d.Zero;
        }

        public override string ToString() =>
            $"{(IsRetrograde ? "retrograde" : "prograde")} burn about {_centre.Id}, " +
            $"{_accelerationMps2:F2} m/s² for {_durationSeconds / 60.0:F1} min";
    }
}
