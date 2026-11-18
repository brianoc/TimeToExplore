using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// Geometry of the hybrid fixed-thrust capture: coast down the inbound
    /// hyperbola, fly one long retrograde finite burn at the chosen acceleration
    /// to an injection radius, apply a terminal impulse that puts the ship on the
    /// exact transfer ellipse, then coast and circularise as the conic arrival
    /// does ([ADR-015](/decisions/adr-015-fixed-thrust-burns.md)).
    ///
    /// The finite burn does the long part of the capture and the impulse corrects
    /// its end state. That split is what lets the parking radius stay at the
    /// conic <c>1.15 R</c>: the burn no longer has to end on the transfer
    /// ellipse by itself, so its periapsis loss is paid in the trim instead of in
    /// a higher orbit.
    ///
    /// The search keeps the burn whose trim is smallest. A burn that starts too
    /// early over-brakes and the trim has to add the speed back; a burn that
    /// starts too late leaves too much tangential speed. The smallest trim is the
    /// split where the fixed burn has done as much of the capture as it usefully
    /// can.
    /// </summary>
    public sealed class HybridCapture
    {
        /// <summary>Minimum altitude the burn and the transfer must keep.</summary>
        public const double MinimumClearanceAltitudeM = 100_000.0;

        /// <summary>Extra altitude above the minimum, so a sampled burn cannot
        /// dip below the clearance between two solver samples.</summary>
        public const double InjectionMarginM = 5_000.0;

        /// <summary>Burn-start offsets searched, from just before periapsis to
        /// well before it.</summary>
        private const int SearchSamples = 41;

        /// <summary>How many times the solver may raise the injection radius
        /// when the chosen level cannot reach the nominal one.</summary>
        private const int InjectionRaiseSteps = 4;

        private const double InjectionRaiseFactor = 0.02;

        private HybridCapture()
        {
        }

        public bool IsValid { get; private set; }
        public string InvalidReason { get; private set; }

        /// <summary>The fixed acceleration the burn commands.</summary>
        public double AccelerationMps2 { get; private set; }

        /// <summary>Inbound coast from the sphere of influence to the nominal
        /// capture periapsis, in seconds.</summary>
        public double InboundSeconds { get; private set; }

        /// <summary>Burn start relative to the inbound periapsis, in seconds.
        /// Negative means before periapsis.</summary>
        public double StartOffsetSeconds { get; private set; }

        public double BurnSeconds { get; private set; }

        /// <summary>Fixed-burn delta-v, <c>a T</c>.</summary>
        public double FiniteDeltaVMps => AccelerationMps2 * BurnSeconds;

        /// <summary>Radius where the fixed burn ends and the trim is applied.</summary>
        public double InjectionRadiusM { get; private set; }

        public double ParkingRadiusM { get; private set; }

        /// <summary>The conic capture periapsis the inbound hyperbola was built
        /// with. The injection radius starts here and may be raised.</summary>
        public double PeriapsisRadiusM { get; private set; }

        /// <summary>Minimum radius over the whole fixed burn.</summary>
        public double MinimumRadiusM { get; private set; }

        public Vector3d BurnStartLocalPositionM { get; private set; }
        public Vector3d BurnStartLocalVelocityMps { get; private set; }
        public Vector3d BurnEndLocalPositionM { get; private set; }
        public Vector3d BurnEndLocalVelocityMps { get; private set; }

        /// <summary>The terminal impulse that turns the burn's end state into the
        /// exact transfer ellipse.</summary>
        public Vector3d TrimVectorMps { get; private set; }

        public double TrimDeltaVMps => TrimVectorMps.Magnitude;

        /// <summary>Velocity after the trim, at the transfer periapsis.</summary>
        public Vector3d TransferStartLocalVelocityMps { get; private set; }

        public double TransferSeconds { get; private set; }
        public Vector3d ApoapsisLocalPositionM { get; private set; }
        public Vector3d TransferApoapsisVelocityMps { get; private set; }

        /// <summary>The prograde impulse at the transfer apoapsis that turns the
        /// ellipse into the circular parking orbit.</summary>
        public Vector3d CircularisationBurnVectorMps { get; private set; }

        public double CircularisationDeltaVMps => CircularisationBurnVectorMps.Magnitude;

        public Vector3d ParkingLocalVelocityMps { get; private set; }
        public double ParkingSeconds { get; private set; }

        /// <summary>Whole capture, from sphere-of-influence entry to the end of
        /// the parking sample.</summary>
        public double TotalSeconds =>
            Math.Max(0.0, InboundSeconds + StartOffsetSeconds)
            + BurnSeconds + TransferSeconds + ParkingSeconds;

        /// <summary>Total capture delta-v: fixed burn, trim, and the
        /// circularisation impulse.</summary>
        public double TotalBurnDeltaVMps =>
            FiniteDeltaVMps + TrimDeltaVMps + CircularisationDeltaVMps;

        /// <summary>
        /// Solve a hybrid capture for one inbound hyperbola. The fixed burn ends
        /// at <paramref name="periapsisRadiusM"/> or, if the level cannot reach
        /// it safely, at a slightly raised injection radius below
        /// <paramref name="parkingRadiusM"/>.
        /// </summary>
        public static HybridCapture Build(BodyRuntime target, ConicArc inbound,
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

            double periapsis = inbound.Elements.PeriapsisRadiusM;
            if (!(periapsis > 0.0) || !double.IsFinite(periapsis))
                return Invalid("the incoming conic has no finite periapsis");
            if (Math.Abs(periapsis - periapsisRadiusM) > 1.0)
                return Invalid("the inbound conic was built with a different periapsis");
            if (!(parkingRadiusM > periapsis))
                return Invalid("the parking radius is below the capture periapsis");
            if (periapsis <= target.RadiusM + MinimumClearanceAltitudeM)
                return Invalid("the capture periapsis is below the safety altitude");

            double mu = target.Gm;
            inbound.LocalStateAt(0.0, out Vector3d entryPosition, out Vector3d entryVelocity);
            double entryRadius = entryPosition.Magnitude;
            if (!(entryRadius > 0.0))
                return Invalid("the incoming conic has no entry state");

            double excessSquared = entryVelocity.SqrMagnitude - 2.0 * mu / entryRadius;
            double hyperbolicExcessSpeed = excessSquared > 0.0
                ? Math.Sqrt(excessSquared)
                : 0.0;

            double hyperbolicPeriapsisSpeed = Math.Sqrt(
                hyperbolicExcessSpeed * hyperbolicExcessSpeed + 2.0 * mu / periapsis);
            double nominalTransferSemiMajor = 0.5 * (periapsis + parkingRadiusM);
            double nominalTransferSpeed = Math.Sqrt(
                mu * (2.0 / periapsis - 1.0 / nominalTransferSemiMajor));
            double impulseDeltaV = hyperbolicPeriapsisSpeed - nominalTransferSpeed;
            if (!(impulseDeltaV > 0.0))
                return Invalid("the incoming orbit is already slower than the transfer ellipse");

            double nominalBurnSeconds = Math.Max(1.0, impulseDeltaV / accelerationMps2);
            double inboundSeconds = inbound.DurationSeconds;
            if (!(inboundSeconds > 1.0))
                return Invalid("the inbound coast is too short for a finite burn");

            double baseInjection = Math.Max(periapsis,
                target.RadiusM + MinimumClearanceAltitudeM + InjectionMarginM);

            for (int raise = 0; raise < InjectionRaiseSteps; raise++)
            {
                double injectionRadius = baseInjection
                                         * (1.0 + InjectionRaiseFactor * raise);
                if (!(injectionRadius < parkingRadiusM)) break;

                HybridCapture candidate = Search(target, inbound, parkingRadiusM,
                    injectionRadius, accelerationMps2, nominalBurnSeconds,
                    inboundSeconds);
                if (candidate != null) return candidate;
            }

            return Invalid("no fixed-thrust burn reaches a safe injection radius");
        }

        private static HybridCapture Search(BodyRuntime target, ConicArc inbound,
            double parkingRadiusM, double injectionRadiusM, double accelerationMps2,
            double nominalBurnSeconds, double inboundSeconds)
        {
            HybridCapture best = null;

            for (int i = 0; i < SearchSamples; i++)
            {
                double fraction = 0.05 + 1.45 * i / (SearchSamples - 1.0);
                double startOffset = -fraction * nominalBurnSeconds;
                double startSeconds = inboundSeconds + startOffset;
                if (!(startSeconds > 1.0) || startSeconds >= inboundSeconds) continue;

                if (!SolveBurn(target, inbound, startSeconds, injectionRadiusM,
                        accelerationMps2, nominalBurnSeconds, inboundSeconds,
                        out double burnSeconds, out Vector3d startPosition,
                        out Vector3d startVelocity, out Vector3d endPosition,
                        out Vector3d endVelocity))
                    continue;

                double minimumRadius = endPosition.Magnitude;
                if (minimumRadius < target.RadiusM + MinimumClearanceAltitudeM)
                    continue;

                if (!TryTransfer(target, endPosition, endVelocity, parkingRadiusM,
                        out Vector3d trim, out Vector3d transferVelocity,
                        out ConicElements transfer, out Vector3d apoapsisPosition,
                        out Vector3d apoapsisVelocity, out Vector3d parkingVelocity,
                        out Vector3d circularisation))
                    continue;

                if (best != null && trim.Magnitude >= best.TrimDeltaVMps) continue;

                double apoapsisRadius = apoapsisPosition.Magnitude;
                double transferSeconds = transfer.PeriodSeconds * 0.5;
                if (!(transferSeconds > 1.0) || !double.IsFinite(transferSeconds))
                    continue;

                double parkingSeconds = Math.Clamp(
                    TwoBody.PeriodSeconds(target.Gm, apoapsisRadius) / 360.0,
                    1.0, 60.0);

                best = new HybridCapture
                {
                    IsValid = true,
                    AccelerationMps2 = accelerationMps2,
                    InboundSeconds = inboundSeconds,
                    StartOffsetSeconds = startOffset,
                    BurnSeconds = burnSeconds,
                    InjectionRadiusM = endPosition.Magnitude,
                    ParkingRadiusM = parkingRadiusM,
                    PeriapsisRadiusM = inbound.Elements.PeriapsisRadiusM,
                    MinimumRadiusM = minimumRadius,
                    BurnStartLocalPositionM = startPosition,
                    BurnStartLocalVelocityMps = startVelocity,
                    BurnEndLocalPositionM = endPosition,
                    BurnEndLocalVelocityMps = endVelocity,
                    TrimVectorMps = trim,
                    TransferStartLocalVelocityMps = transferVelocity,
                    TransferSeconds = transferSeconds,
                    ApoapsisLocalPositionM = apoapsisPosition,
                    TransferApoapsisVelocityMps = apoapsisVelocity,
                    CircularisationBurnVectorMps = circularisation,
                    ParkingLocalVelocityMps = parkingVelocity,
                    ParkingSeconds = parkingSeconds
                };
            }

            return best;
        }

        /// <summary>
        /// Find the burn that first brings the path down to the injection
        /// radius, and return the interpolated crossing state. The radius falls
        /// monotonically from the start to that first crossing, so the injection
        /// point is also the burn's closest approach.
        /// </summary>
        private static bool SolveBurn(BodyRuntime target, ConicArc inbound,
            double startSeconds, double injectionRadiusM, double accelerationMps2,
            double nominalBurnSeconds, double inboundSeconds,
            out double burnSeconds, out Vector3d startPosition,
            out Vector3d startVelocity, out Vector3d endPosition,
            out Vector3d endVelocity)
        {
            burnSeconds = 0.0;
            endPosition = Vector3d.Zero;
            endVelocity = Vector3d.Zero;

            double startParameter = inbound.ParameterAtSeconds(startSeconds);
            inbound.LocalStateAt(startParameter, out startPosition, out startVelocity);

            double startRadius = startPosition.Magnitude;
            if (!(startRadius > injectionRadiusM)) return false;

            double maximum = Math.Max(60.0, 1.6 * nominalBurnSeconds);
            double limit = Math.Max(60.0, Math.Min(2.0 * inboundSeconds,
                30.0 * 24.0 * 3600.0));

            bool found = FiniteBurnArc.SimulateUntilRadius(target, startPosition,
                startVelocity, accelerationMps2, true, injectionRadiusM, maximum,
                out double crossing, out endPosition, out endVelocity);

            for (int i = 0; i < 24 && !found && maximum < limit; i++)
            {
                maximum = Math.Min(maximum * 1.5, limit);
                found = FiniteBurnArc.SimulateUntilRadius(target, startPosition,
                    startVelocity, accelerationMps2, true, injectionRadiusM, maximum,
                    out crossing, out endPosition, out endVelocity);
            }

            if (!found || !(crossing > 0.0)) return false;

            // Refine the crossing with a shorter integration span, so the
            // interpolation step is a small fraction of the burn.
            double refined = Math.Min(maximum, crossing * 1.05 + 1.0);
            if (FiniteBurnArc.SimulateUntilRadius(target, startPosition, startVelocity,
                    accelerationMps2, true, injectionRadiusM, refined,
                    out double refinedCrossing, out endPosition, out endVelocity))
                crossing = refinedCrossing;

            burnSeconds = crossing;
            return burnSeconds > 1.0;
        }

        /// <summary>
        /// Public form of the transfer solve for the planner: the trim vector,
        /// the post-trim transfer-periapsis velocity, and the transfer time. The
        /// planner rebuilds these from its own burn end so the flown seams are
        /// exact.
        /// </summary>
        public static bool TryTrim(BodyRuntime target, Vector3d position,
            Vector3d velocity, double parkingRadiusM, out Vector3d trimVector,
            out Vector3d transferVelocity, out double transferSeconds)
        {
            bool valid = TryTransfer(target, position, velocity, parkingRadiusM,
                out trimVector, out transferVelocity, out ConicElements transfer,
                out _, out _, out _, out _);
            transferSeconds = valid ? transfer.PeriodSeconds * 0.5 : 0.0;
            return valid;
        }

        /// <summary>
        /// The terminal impulse and the transfer ellipse it puts the ship on.
        /// The transfer periapsis is the burn's end radius; its apoapsis is the
        /// parking radius.
        /// </summary>
        private static bool TryTransfer(BodyRuntime target, Vector3d position,
            Vector3d velocity, double parkingRadiusM,
            out Vector3d trim, out Vector3d transferVelocity,
            out ConicElements transfer, out Vector3d apoapsisPosition,
            out Vector3d apoapsisVelocity, out Vector3d parkingVelocity,
            out Vector3d circularisation)
        {
            trim = Vector3d.Zero;
            transferVelocity = Vector3d.Zero;
            transfer = null;
            apoapsisPosition = Vector3d.Zero;
            apoapsisVelocity = Vector3d.Zero;
            parkingVelocity = Vector3d.Zero;
            circularisation = Vector3d.Zero;

            double radius = position.Magnitude;
            if (!(radius > target.RadiusM + MinimumClearanceAltitudeM)) return false;
            if (!(parkingRadiusM > radius)) return false;

            Vector3d radial = position / radius;
            Vector3d tangential = velocity - radial * Vector3d.Dot(velocity, radial);
            if (tangential.SqrMagnitude < 1e-6) return false;

            double semiMajor = 0.5 * (radius + parkingRadiusM);
            double transferSpeed = Math.Sqrt(
                target.Gm * (2.0 / radius - 1.0 / semiMajor));

            transferVelocity = tangential.Normalized * transferSpeed;
            trim = transferVelocity - velocity;

            transfer = ConicElements.FromState(position, transferVelocity, target.Gm);
            if (!transfer.IsElliptic) return false;
            if (!(transfer.PeriodSeconds > 0.0) || !double.IsFinite(transfer.PeriodSeconds))
                return false;

            transfer.StateAtTrueAnomaly(Math.PI,
                out apoapsisPosition, out apoapsisVelocity);

            double apoapsisRadius = apoapsisPosition.Magnitude;
            double circularSpeed = Math.Sqrt(target.Gm / apoapsisRadius);
            parkingVelocity = apoapsisVelocity.Normalized * circularSpeed;
            circularisation = parkingVelocity - apoapsisVelocity;
            return true;
        }

        private static HybridCapture Invalid(string reason)
        {
            return new HybridCapture
            {
                IsValid = false,
                InvalidReason = reason
            };
        }
    }
}
