using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The planetocentric geometry of a conic arrival: an inbound hyperbola from
    /// the sphere of influence to a low periapsis, a transfer ellipse from that
    /// periapsis to the parking radius, and the circular state the plan ends on.
    /// The burns are impulses at the two apsides, which is the Oberth-optimal
    /// capture ([ADR-014](/decisions/adr-014-keplerian-coast.md)).
    ///
    /// Everything here is local to the body and in its equatorial plane, prograde
    /// about the IAU north pole. The planner re-adds the body's ephemeris. The
    /// capture plane is the parking plane by construction, so the transfer
    /// ellipse can circularise without a plane change.
    /// </summary>
    public sealed class TransferConic
    {
        private const double MinimumParkingSeconds = 1.0;
        private const double MaximumParkingSeconds = 60.0;

        private TransferConic()
        {
        }

        public BodyRuntime Target { get; private set; }
        public bool IsValid { get; private set; }
        public string InvalidReason { get; private set; }

        public double SphereOfInfluenceM { get; private set; }
        public double EntrySpeedMps { get; private set; }
        public double Eccentricity { get; private set; }
        public double PeriapsisRadiusM { get; private set; }
        public double ApoapsisRadiusM { get; private set; }
        public double PeriapsisDirectionRad { get; private set; }
        public double ParkingPhaseRad { get; private set; }

        public double InboundSeconds { get; private set; }
        public double TransferSeconds { get; private set; }
        public double ParkingSeconds { get; private set; }
        public double TotalSeconds => InboundSeconds + TransferSeconds + ParkingSeconds;

        public Vector3d EntryLocalPositionM { get; private set; }
        public Vector3d EntryLocalVelocityMps { get; private set; }
        public Vector3d EntryLocalAccelerationMps2 { get; private set; }

        public Vector3d PeriapsisLocalPositionM { get; private set; }
        public Vector3d HyperbolicPeriapsisVelocityMps { get; private set; }
        public Vector3d TransferPeriapsisVelocityMps { get; private set; }

        public Vector3d ApoapsisLocalPositionM { get; private set; }
        public Vector3d TransferApoapsisVelocityMps { get; private set; }
        public Vector3d CircularApoapsisVelocityMps { get; private set; }

        /// <summary>Velocity change at periapsis: hyperbola to transfer ellipse.</summary>
        public Vector3d CaptureBurnVectorMps { get; private set; }

        /// <summary>Velocity change at apoapsis: transfer ellipse to circle.</summary>
        public Vector3d CircularisationBurnVectorMps { get; private set; }

        public double CaptureDeltaVMps => CaptureBurnVectorMps.Magnitude;
        public double CircularisationDeltaVMps => CircularisationBurnVectorMps.Magnitude;
        public double TotalBurnDeltaVMps => CaptureDeltaVMps + CircularisationDeltaVMps;

        /// <summary>
        /// Build the capture chain. <paramref name="approachDirection"/> points
        /// from the target towards the arriving ship; it only chooses the
        /// azimuth of the capture plane about the target's pole.
        /// </summary>
        public static TransferConic Build(BodyRuntime target, Vector3d approachDirection,
            double sphereOfInfluenceM, double entrySpeedMps,
            double parkingRadiusM, double periapsisRadiusM)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var chain = new TransferConic
            {
                Target = target,
                SphereOfInfluenceM = sphereOfInfluenceM,
                EntrySpeedMps = entrySpeedMps,
                PeriapsisRadiusM = periapsisRadiusM,
                ApoapsisRadiusM = parkingRadiusM
            };

            double mu = target.Gm;
            if (!(mu > 0.0))
                return Invalid(chain, "the target has no gravitational parameter");
            if (!(sphereOfInfluenceM > 0.0) || !double.IsFinite(sphereOfInfluenceM))
                return Invalid(chain, "the target has no finite sphere of influence");
            if (!(entrySpeedMps > 0.0))
                return Invalid(chain, "the arrival needs a positive hyperbolic excess speed");
            if (!(periapsisRadiusM > target.RadiusM))
                return Invalid(chain, "the capture periapsis is inside the body");
            if (!(parkingRadiusM >= periapsisRadiusM))
                return Invalid(chain, "the parking orbit is below the capture periapsis");
            if (!(sphereOfInfluenceM > periapsisRadiusM))
                return Invalid(chain, "the sphere of influence is smaller than the periapsis");

            double rp = periapsisRadiusM;
            double ra = parkingRadiusM;
            double vInfinity = entrySpeedMps;

            double periapsisSpeed = Math.Sqrt(vInfinity * vInfinity + 2.0 * mu / rp);
            double eccentricity = 1.0 + rp * vInfinity * vInfinity / mu;
            double semiLatus = rp * (1.0 + eccentricity);

            double cosNu = (semiLatus / sphereOfInfluenceM - 1.0) / eccentricity;
            if (cosNu >= 1.0)
                return Invalid(chain, "the arrival hyperbola does not reach the sphere of influence");
            double entryAnomaly = Math.Acos(Math.Clamp(cosNu, -1.0, 1.0));
            if (!(entryAnomaly > 0.0))
                return Invalid(chain, "the arrival hyperbola has no inbound branch");

            // Where the ship enters, measured from the body's equatorial x axis.
            double projectedX = Vector3d.Dot(approachDirection, target.EquatorX);
            double projectedY = Vector3d.Dot(approachDirection, target.EquatorY);
            double entryAzimuth = projectedX * projectedX + projectedY * projectedY > 1e-12
                ? Math.Atan2(projectedY, projectedX)
                : 0.0;

            // The periapsis is one entry anomaly ahead of the entry point, so
            // rotating the whole plane by this offset puts the entry on the
            // approach azimuth.
            double periapsisAzimuth = entryAzimuth + entryAnomaly;
            Vector3d periapsisDirection = target.EquatorX * Math.Cos(periapsisAzimuth)
                                          + target.EquatorY * Math.Sin(periapsisAzimuth);
            Vector3d transverseDirection = target.EquatorX * -Math.Sin(periapsisAzimuth)
                                           + target.EquatorY * Math.Cos(periapsisAzimuth);

            double cosEntry = Math.Cos(entryAnomaly);
            double sinEntry = -Math.Sin(entryAnomaly);
            double velocityScale = Math.Sqrt(mu / semiLatus);

            Vector3d entryPosition = (periapsisDirection * cosEntry
                                      + transverseDirection * sinEntry)
                                     * sphereOfInfluenceM;
            Vector3d entryVelocity = (periapsisDirection * -sinEntry
                                      + transverseDirection * (eccentricity + cosEntry))
                                     * velocityScale;

            double entryRadius = entryPosition.Magnitude;
            Vector3d entryAcceleration = entryPosition
                                         * (-mu / (entryRadius * entryRadius * entryRadius));

            Vector3d periapsisPosition = periapsisDirection * rp;
            Vector3d hyperbolicPeriapsisVelocity = transverseDirection * periapsisSpeed;

            double transferSemiMajor = 0.5 * (rp + ra);
            double transferEccentricity = (ra - rp) / (ra + rp);
            double transferPeriapsisSpeed = Math.Sqrt(
                mu * (2.0 / rp - 1.0 / transferSemiMajor));
            double transferApoapsisSpeed = Math.Sqrt(
                mu * (2.0 / ra - 1.0 / transferSemiMajor));
            double circularApoapsisSpeed = Math.Sqrt(mu / ra);

            Vector3d transferPeriapsisVelocity = transverseDirection * transferPeriapsisSpeed;
            Vector3d apoapsisPosition = periapsisDirection * -ra;
            Vector3d transferApoapsisVelocity = transverseDirection * -transferApoapsisSpeed;
            Vector3d circularApoapsisVelocity = transverseDirection * -circularApoapsisSpeed;

            double negativeA = semiLatus / (eccentricity * eccentricity - 1.0);
            double hyperbolicMeanMotion = Math.Sqrt(mu / Math.Pow(negativeA, 3.0));
            double entryDenominator = 1.0 + eccentricity * cosEntry;
            double sinhH = Math.Sqrt(eccentricity * eccentricity - 1.0)
                           * sinEntry / entryDenominator;
            double hyperbolicAnomaly = Math.Asinh(sinhH);
            double hyperbolicMean = eccentricity * Math.Sinh(hyperbolicAnomaly)
                                    - hyperbolicAnomaly;
            double inboundSeconds = -hyperbolicMean / hyperbolicMeanMotion;

            double transferMeanMotion = Math.Sqrt(
                mu / Math.Pow(transferSemiMajor, 3.0));
            double transferSeconds = Math.PI / transferMeanMotion;

            double parkingPeriod = OrbitMath.TwoPi / Math.Sqrt(mu / Math.Pow(ra, 3.0));
            double parkingSeconds = Math.Clamp(parkingPeriod / 360.0,
                MinimumParkingSeconds, MaximumParkingSeconds);

            if (!(inboundSeconds > 0.0) || !double.IsFinite(inboundSeconds))
                return Invalid(chain, "the inbound coast has no positive duration");

            chain.IsValid = true;
            chain.Eccentricity = eccentricity;
            chain.PeriapsisDirectionRad = periapsisAzimuth;
            chain.ParkingPhaseRad = periapsisAzimuth + Math.PI;
            chain.InboundSeconds = inboundSeconds;
            chain.TransferSeconds = transferSeconds;
            chain.ParkingSeconds = parkingSeconds;
            chain.EntryLocalPositionM = entryPosition;
            chain.EntryLocalVelocityMps = entryVelocity;
            chain.EntryLocalAccelerationMps2 = entryAcceleration;
            chain.PeriapsisLocalPositionM = periapsisPosition;
            chain.HyperbolicPeriapsisVelocityMps = hyperbolicPeriapsisVelocity;
            chain.TransferPeriapsisVelocityMps = transferPeriapsisVelocity;
            chain.ApoapsisLocalPositionM = apoapsisPosition;
            chain.TransferApoapsisVelocityMps = transferApoapsisVelocity;
            chain.CircularApoapsisVelocityMps = circularApoapsisVelocity;
            chain.CaptureBurnVectorMps = transferPeriapsisVelocity
                                         - hyperbolicPeriapsisVelocity;
            chain.CircularisationBurnVectorMps = circularApoapsisVelocity
                                                 - transferApoapsisVelocity;
            return chain;
        }

        private static TransferConic Invalid(TransferConic chain, string reason)
        {
            chain.IsValid = false;
            chain.InvalidReason = reason;
            return chain;
        }
    }
}
