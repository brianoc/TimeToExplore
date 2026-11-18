using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Geometry of a fixed-thrust capture: coast down the inbound hyperbola,
    /// burn retrograde at a fixed acceleration across periapsis, coast up a
    /// transfer ellipse to the parking radius, then burn prograde at a fixed
    /// acceleration to circularise. It is the finite-burn answer to the conic
    /// arrival's ideal impulses ([ADR-014](/decisions/adr-014-keplerian-coast.md),
    /// [ADR-015](/decisions/adr-015-fixed-thrust-burns.md)).
    ///
    /// A finite burn cannot sit exactly at periapsis, so the burn is moved to
    /// straddle it and its duration is solved so the post-burn apoapsis is the
    /// parking radius. The burn loses some periapsis altitude; the planner uses a
    /// higher capture periapsis and parking radius than the impulsive style so
    /// the loss still leaves the ship clear of the body.
    /// </summary>
    public sealed class FiniteBurnCapture
    {
        /// <summary>Minimum altitude the burn and the post-burn orbit must keep.</summary>
        public const double MinimumClearanceAltitudeM = 100_000.0;

        /// <summary>Search samples across the burn-start offset.</summary>
        private const int SearchSamples = 21;

        /// <summary>Integration samples used while searching. The final arc is finer.</summary>
        private const int SolverSamples = 128;

        private const int BisectionIterations = 42;
        private const double ApoapsisToleranceM = 1_000.0;

        private FiniteBurnCapture()
        {
        }

        public bool IsValid { get; private set; }
        public string InvalidReason { get; private set; }

        /// <summary>Burn start relative to the inbound periapsis, in seconds.
        /// Negative means before periapsis.</summary>
        public double StartOffsetSeconds { get; private set; }

        public double BurnSeconds { get; private set; }
        public double TransferSeconds { get; private set; }
        public double CircularisationSeconds { get; private set; }
        public double ParkingSeconds { get; private set; }

        /// <summary>Estimated whole capture, from the start of the inbound coast
        /// to the end of the parking sample.</summary>
        public double TotalSeconds =>
            Math.Max(0.0, InboundSeconds + StartOffsetSeconds)
            + BurnSeconds + TransferSeconds + CircularisationSeconds + ParkingSeconds;

        public double InboundSeconds { get; private set; }
        public double ParkingRadiusM { get; private set; }
        public double PeriapsisRadiusM { get; private set; }
        public double PostBurnPeriapsisRadiusM { get; private set; }
        public double PostBurnApoapsisRadiusM { get; private set; }
        public double MinimumRadiusM { get; private set; }

        public Vector3d BurnStartLocalPositionM { get; private set; }
        public Vector3d BurnStartLocalVelocityMps { get; private set; }
        public Vector3d BurnEndLocalPositionM { get; private set; }
        public Vector3d BurnEndLocalVelocityMps { get; private set; }
        public Vector3d ApoapsisLocalPositionM { get; private set; }
        public Vector3d ApoapsisLocalVelocityMps { get; private set; }
        public Vector3d CircularisationStartLocalPositionM { get; private set; }
        public Vector3d CircularisationStartLocalVelocityMps { get; private set; }
        public Vector3d CircularisationEndLocalPositionM { get; private set; }
        public Vector3d CircularisationEndLocalVelocityMps { get; private set; }

        /// <summary>
        /// Solve the capture for one inbound hyperbola. <paramref name="parkingRadiusM"/>
        /// is the apoapsis the transfer ellipse must reach;
        /// <paramref name="periapsisRadiusM"/> is the nominal capture periapsis
        /// the incoming conic was built with.
        /// </summary>
        public static FiniteBurnCapture Build(BodyRuntime target, ConicArc inbound,
            double parkingRadiusM, double periapsisRadiusM, double accelerationMps2)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (inbound == null) throw new ArgumentNullException(nameof(inbound));
            if (!(parkingRadiusM > 0.0))
                return Invalid("the parking radius is not positive");
            if (!(periapsisRadiusM > 0.0))
                return Invalid("the capture periapsis is not positive");
            if (!(accelerationMps2 > 0.0))
                return Invalid("the fixed burn acceleration is not positive");
            if (!(target.Gm > 0.0))
                return Invalid("the target has no gravitational parameter");

            double mu = target.Gm;
            double periapsis = inbound.Elements.PeriapsisRadiusM;
            if (!(periapsis > 0.0) || !double.IsFinite(periapsis))
                return Invalid("the incoming conic has no finite periapsis");
            if (Math.Abs(periapsis - periapsisRadiusM) > 1.0)
                return Invalid("the inbound conic was built with a different periapsis");
            if (!(parkingRadiusM > periapsis))
                return Invalid("the parking radius is below the capture periapsis");
            if (periapsis <= target.RadiusM + MinimumClearanceAltitudeM)
                return Invalid("the capture periapsis is below the safety altitude");

            inbound.LocalStateAt(0.0, out Vector3d entryPosition, out Vector3d entryVelocity);
            double entryRadius = entryPosition.Magnitude;
            if (!(entryRadius > 0.0))
                return Invalid("the incoming conic has no entry state");

            double excessSquared = entryVelocity.SqrMagnitude
                                   - 2.0 * mu / entryRadius;
            double hyperbolicExcessSpeed = excessSquared > 0.0
                ? Math.Sqrt(excessSquared)
                : 0.0;

            double hyperbolicPeriapsisSpeed = Math.Sqrt(
                hyperbolicExcessSpeed * hyperbolicExcessSpeed + 2.0 * mu / periapsis);
            double transferSemiMajor = 0.5 * (periapsis + parkingRadiusM);
            double transferPeriapsisSpeed = Math.Sqrt(
                mu * (2.0 / periapsis - 1.0 / transferSemiMajor));
            double impulseDeltaV = hyperbolicPeriapsisSpeed - transferPeriapsisSpeed;
            if (!(impulseDeltaV > 0.0))
                return Invalid("the incoming orbit is already slower than the transfer ellipse");

            double nominalBurnSeconds = impulseDeltaV / accelerationMps2;
            double inboundSeconds = inbound.DurationSeconds;

            FiniteBurnCapture best = null;
            double bestPeriapsis = double.NegativeInfinity;

            // The burn must straddle periapsis, but how far before it is a real
            // choice: starting later saves periapsis altitude and starting
            // earlier spends less delta-v. Search the offset and keep the safest
            // burn that still reaches the parking radius.
            for (int i = 0; i < SearchSamples; i++)
            {
                double fraction = -1.6 + 2.0 * i / (SearchSamples - 1.0);
                double startOffset = fraction * nominalBurnSeconds;
                double startSeconds = inboundSeconds + startOffset;
                if (!(startSeconds > 1.0) || startSeconds >= inboundSeconds) continue;

                double burnSeconds = SolveBurnSeconds(target, inbound, startSeconds,
                    parkingRadiusM, accelerationMps2, nominalBurnSeconds, inboundSeconds);
                if (double.IsNaN(burnSeconds)) continue;

                double startParameter = inbound.ParameterAtSeconds(startSeconds);
                inbound.LocalStateAt(startParameter, out Vector3d startPosition,
                    out Vector3d startVelocity);

                FiniteBurnArc.Simulate(target, startPosition, startVelocity,
                    accelerationMps2, true, burnSeconds, SolverSamples,
                    out Vector3d endPosition, out Vector3d endVelocity,
                    out double minimumRadius);

                if (minimumRadius < target.RadiusM + MinimumClearanceAltitudeM)
                    continue;

                ConicElements postBurn = ConicElements.FromState(endPosition, endVelocity, mu);
                if (!postBurn.IsElliptic) continue;
                if (postBurn.PeriapsisRadiusM < target.RadiusM + MinimumClearanceAltitudeM)
                    continue;
                if (Math.Abs(postBurn.ApoapsisRadiusM - parkingRadiusM) > ApoapsisToleranceM)
                    continue;

                if (postBurn.PeriapsisRadiusM <= bestPeriapsis) continue;

                bestPeriapsis = postBurn.PeriapsisRadiusM;
                best = new FiniteBurnCapture
                {
                    IsValid = true,
                    InboundSeconds = inboundSeconds,
                    StartOffsetSeconds = startOffset,
                    BurnSeconds = burnSeconds,
                    ParkingRadiusM = parkingRadiusM,
                    PeriapsisRadiusM = periapsis,
                    PostBurnPeriapsisRadiusM = postBurn.PeriapsisRadiusM,
                    PostBurnApoapsisRadiusM = postBurn.ApoapsisRadiusM,
                    MinimumRadiusM = minimumRadius,
                    BurnStartLocalPositionM = startPosition,
                    BurnStartLocalVelocityMps = startVelocity,
                    BurnEndLocalPositionM = endPosition,
                    BurnEndLocalVelocityMps = endVelocity
                };
            }

            if (best == null)
            {
                return Invalid("no fixed-thrust burn clears the body and reaches the parking radius");
            }

            ConicElements post = ConicElements.FromState(best.BurnEndLocalPositionM,
                best.BurnEndLocalVelocityMps, mu);

            post.StateAtTrueAnomaly(Math.PI,
                out Vector3d apoapsisPosition, out Vector3d apoapsisVelocity);
            best.ApoapsisLocalPositionM = apoapsisPosition;
            best.ApoapsisLocalVelocityMps = apoapsisVelocity;

            // A burn centred on apoapsis leaves much less residual eccentricity
            // than one that starts there, so solve the duration and the start
            // together: the start is half the burn before the apsis.
            if (!SolveCenteredCircularisation(target, post, accelerationMps2,
                    out double circularisationSeconds,
                    out Vector3d circularStartPosition,
                    out Vector3d circularStartVelocity))
            {
                return Invalid("the fixed-thrust circularisation has no solution");
            }

            double timeToApoapsis = post.PeriodSeconds * 0.5
                                    - post.TimeSincePeriapsisSeconds;
            best.TransferSeconds = timeToApoapsis - 0.5 * circularisationSeconds;
            if (!(best.TransferSeconds > 1.0))
                return Invalid("the fixed-thrust circularisation would start before the transfer");

            best.CircularisationSeconds = circularisationSeconds;
            best.CircularisationStartLocalPositionM = circularStartPosition;
            best.CircularisationStartLocalVelocityMps = circularStartVelocity;

            FiniteBurnArc.Simulate(target, circularStartPosition, circularStartVelocity,
                accelerationMps2, false, circularisationSeconds, SolverSamples,
                out Vector3d circularEndPosition, out Vector3d circularEndVelocity,
                out _);
            best.CircularisationEndLocalPositionM = circularEndPosition;
            best.CircularisationEndLocalVelocityMps = circularEndVelocity;

            ConicElements parking = ConicElements.FromState(circularEndPosition,
                circularEndVelocity, mu);
            if (!parking.IsElliptic)
                return Invalid("the fixed-thrust circularisation did not leave an orbit");
            best.ParkingSeconds = Math.Clamp(parking.PeriodSeconds / 360.0, 1.0, 60.0);
            return best;
        }

        /// <summary>
        /// Solve a prograde burn centred on the transfer apoapsis. The start is
        /// half the burn before the apsis, and the duration is chosen so the end
        /// speed is the local circular speed. Centring cancels most of the
        /// residual radial velocity, so the parking orbit is much rounder than a
        /// burn that starts at the apsis.
        /// </summary>
        private static bool SolveCenteredCircularisation(BodyRuntime target,
            ConicElements transfer, double accelerationMps2,
            out double burnSeconds, out Vector3d startPosition,
            out Vector3d startVelocity)
        {
            burnSeconds = 0.0;
            startPosition = Vector3d.Zero;
            startVelocity = Vector3d.Zero;

            double timeToApoapsis = transfer.PeriodSeconds * 0.5
                                    - transfer.TimeSincePeriapsisSeconds;
            if (!(timeToApoapsis > 1.0)) return false;

            transfer.StateAtTrueAnomaly(Math.PI,
                out Vector3d apoapsisPosition, out Vector3d apoapsisVelocity);
            double apoapsisRadius = apoapsisPosition.Magnitude;
            double circularSpeed = Math.Sqrt(target.Gm / apoapsisRadius);
            double apoapsisSpeed = apoapsisVelocity.Magnitude;
            if (apoapsisSpeed >= circularSpeed)
            {
                startPosition = apoapsisPosition;
                startVelocity = apoapsisVelocity;
                return true;
            }

            double nominal = (circularSpeed - apoapsisSpeed) / accelerationMps2;
            double high = Math.Max(10.0, 2.0 * nominal);
            double maximum = Math.Min(2.0 * timeToApoapsis * 0.98, 1e6);

            double Error(double seconds)
            {
                double startSeconds = timeToApoapsis - 0.5 * seconds;
                if (!(startSeconds > 0.0)) return double.NaN;

                double anomaly = transfer.TrueAnomalyAtTime(
                    transfer.TimeSincePeriapsisSeconds + startSeconds);
                transfer.StateAtTrueAnomaly(anomaly,
                    out Vector3d startP, out Vector3d startV);

                FiniteBurnArc.Simulate(target, startP, startV, accelerationMps2,
                    false, seconds, SolverSamples,
                    out Vector3d endP, out Vector3d endV, out _);

                return endV.Magnitude - Math.Sqrt(target.Gm / endP.Magnitude);
            }

            for (int i = 0; i < 24 && high < maximum; i++)
            {
                if (Error(high) >= 0.0) break;
                high = Math.Min(high * 1.5, maximum);
            }

            if (!(Error(high) >= 0.0)) return false;

            double low = 0.0;
            for (int i = 0; i < BisectionIterations; i++)
            {
                double mid = 0.5 * (low + high);
                double error = Error(mid);
                if (double.IsNaN(error) || error < 0.0) low = mid;
                else high = mid;
            }

            burnSeconds = 0.5 * (low + high);
            double burnStartSeconds = timeToApoapsis - 0.5 * burnSeconds;
            double burnAnomaly = transfer.TrueAnomalyAtTime(
                transfer.TimeSincePeriapsisSeconds + burnStartSeconds);
            transfer.StateAtTrueAnomaly(burnAnomaly, out startPosition, out startVelocity);
            return true;
        }

        private static double SolveBurnSeconds(BodyRuntime target, ConicArc inbound,
            double startSeconds, double parkingRadiusM, double accelerationMps2,
            double nominalBurnSeconds, double inboundSeconds)
        {
            double startParameter = inbound.ParameterAtSeconds(startSeconds);
            inbound.LocalStateAt(startParameter, out Vector3d startPosition,
                out Vector3d startVelocity);

            double low = 0.0;
            double high = Math.Max(60.0, 2.0 * nominalBurnSeconds);
            double limit = Math.Min(2.0 * inboundSeconds, 10.0 * 24.0 * 3600.0);

            double highApoapsis = PostBurnApoapsis(target, startPosition, startVelocity,
                accelerationMps2, high);
            for (int i = 0; i < 24 && highApoapsis > parkingRadiusM && high < limit; i++)
            {
                high = Math.Min(high * 1.5, limit);
                highApoapsis = PostBurnApoapsis(target, startPosition, startVelocity,
                    accelerationMps2, high);
            }

            if (highApoapsis > parkingRadiusM) return double.NaN;

            for (int i = 0; i < BisectionIterations; i++)
            {
                double mid = 0.5 * (low + high);
                double apoapsis = PostBurnApoapsis(target, startPosition, startVelocity,
                    accelerationMps2, mid);
                if (apoapsis > parkingRadiusM) low = mid;
                else high = mid;
            }

            return 0.5 * (low + high);
        }

        private static double PostBurnApoapsis(BodyRuntime target, Vector3d startPosition,
            Vector3d startVelocity, double accelerationMps2, double burnSeconds)
        {
            FiniteBurnArc.Simulate(target, startPosition, startVelocity,
                accelerationMps2, true, burnSeconds, SolverSamples,
                out Vector3d endPosition, out Vector3d endVelocity,
                out double minimumRadius);

            if (minimumRadius < target.RadiusM + MinimumClearanceAltitudeM)
                return double.NegativeInfinity;

            ConicElements elements = ConicElements.FromState(endPosition, endVelocity,
                target.Gm);
            return elements.IsElliptic ? elements.ApoapsisRadiusM : double.PositiveInfinity;
        }

        private static FiniteBurnCapture Invalid(string reason)
        {
            return new FiniteBurnCapture
            {
                IsValid = false,
                InvalidReason = reason
            };
        }
    }
}
