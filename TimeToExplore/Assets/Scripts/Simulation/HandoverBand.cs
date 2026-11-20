using System;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The radial band a fixed burn hands over to a shaped end inside: a hard
    /// <b>floor</b> where the body's own gravity is still a small fraction of the
    /// drive, and a hard <b>ceiling</b> at the clearance sphere the
    /// burn–coast–burn profile used to pin its break to. See
    /// [Full-field burns and the powered-leg floor](/simulation/powered-leg-floor.md).
    ///
    /// The floor is <c>sqrt(μ / (κ a_drive))</c> with the parking ceiling's
    /// <c>κ = 0.4</c>, so it sits where local gravity is 40% of the commanded
    /// acceleration and moves outward as the level falls. It is clamped up to the
    /// body-clearance radius — the surface plus
    /// <see cref="MinimumClearanceAltitudeM"/> — because the gravity floor is
    /// below the surface on several body and level pairs (Mercury and Mars at
    /// 1 g, the Moon and the Galilean moons; measured in the concept). The
    /// ceiling is <see cref="CeilingSoiFraction"/> times the body's sphere of
    /// influence, which is the same <c>1.02 × SOI</c> the profile already used.
    ///
    /// A band whose clamped floor is above its ceiling is <b>empty</b>: the body
    /// is too small, or its sphere of influence too tight, for the handover to
    /// exist at that level. <see cref="For"/> reports that with the numbers rather
    /// than returning radii the planner would then build a nonsense plan from.
    ///
    /// The two ends of a trip want different radii, so the band names both
    /// endpoints: the departure handover is the <b>ceiling</b> (the break's job is
    /// escape, and the burn starts at the clearance sphere and moves outward) and
    /// the arrival handover is the <b>floor</b> (the fall shortens as the radius
    /// comes in). Whether that policy gives way to a solved interior radius is a
    /// measurement's question, not this type's.
    /// </summary>
    public sealed class HandoverBand
    {
        /// <summary>Ceiling radius as a multiple of the body's sphere of
        /// influence. The margin keeps the handover clear while the body moves
        /// under it.</summary>
        public const double CeilingSoiFraction = 1.02;

        /// <summary>Fraction of the drive the local gravity may reach at the
        /// floor. Deliberately the parking orbit's constant: the arrival end of
        /// the trip is the same "thrust must still dominate the field" rule
        /// ([the parking orbit](/simulation/navigation-and-arrival.md#the-parking-orbit)).</summary>
        public const double FloorGravityFraction =
            ParkingOrbit.GravityFractionOfMaxAcceleration;

        /// <summary>Minimum altitude the floor keeps above the surface, and the
        /// minimum every fixed burn must keep. It is the hybrid capture's own
        /// clearance: the same 100 km the capture's burn and transfer already
        /// hold, and below the parking orbit's 300 km altitude.</summary>
        public const double MinimumClearanceAltitudeM =
            HybridCapture.MinimumClearanceAltitudeM;

        private HandoverBand()
        {
        }

        public bool IsValid { get; private set; }

        /// <summary>Why the band is empty, with the radii that produced it.</summary>
        public string InvalidReason { get; private set; }

        public BodyRuntime Body { get; private set; }

        /// <summary>The commanded fixed-burn acceleration the band was built
        /// for, in m/s². This is the planning level, capped by the drive.</summary>
        public double DriveAccelerationMps2 { get; private set; }

        /// <summary>Radius where the body's gravity is
        /// <see cref="FloorGravityFraction"/> of the drive, before the surface
        /// clearance is applied.</summary>
        public double GravityFloorRadiusM { get; private set; }

        /// <summary>The floor the band actually uses: the gravity floor clamped
        /// up to <see cref="ClearanceRadiusM"/>.</summary>
        public double FloorRadiusM { get; private set; }

        /// <summary>The radius the floor is held above: the surface plus
        /// <see cref="MinimumClearanceAltitudeM"/>.</summary>
        public double ClearanceRadiusM { get; private set; }

        /// <summary>The ceiling: <see cref="CeilingSoiFraction"/> times the
        /// body's sphere of influence.</summary>
        public double CeilingRadiusM { get; private set; }

        /// <summary>True when the surface clearance raised the floor above the
        /// gravity floor.</summary>
        public bool FloorWasClamped => FloorRadiusM > GravityFloorRadiusM;

        /// <summary>Band width in metres; negative on an empty band.</summary>
        public double WidthM => CeilingRadiusM - FloorRadiusM;

        /// <summary>The departure endpoint: the band's ceiling.</summary>
        public double DepartureRadiusM => CeilingRadiusM;

        /// <summary>The arrival endpoint: the band's floor. A capture chain that
        /// must start deeper than this (a periapsis or a parking orbit inside it)
        /// still sets its own entry radius; the floor is a lower bound on the
        /// handover, not a promise that the chain can be handed the state there.</summary>
        public double ArrivalRadiusM => FloorRadiusM;

        public bool Contains(double radiusM) =>
            IsValid && radiusM >= FloorRadiusM && radiusM <= CeilingRadiusM;

        /// <summary>
        /// The handover band for one body at one commanded fixed-burn
        /// acceleration. Returns an invalid band, with the numbers, when the
        /// floor exceeds the ceiling.
        /// </summary>
        public static HandoverBand For(BodyRuntime body, double driveAccelerationMps2)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            double soi = SphereOfInfluence.RadiusM(body);
            var band = new HandoverBand
            {
                Body = body,
                DriveAccelerationMps2 = driveAccelerationMps2,
                CeilingRadiusM = soi * CeilingSoiFraction,
                ClearanceRadiusM = body.RadiusM + MinimumClearanceAltitudeM
            };

            if (!(driveAccelerationMps2 > 0.0))
                return Invalid(band,
                    $"the handover band at {body.Name} has no floor: the drive commands " +
                    "no fixed-burn acceleration");

            if (!(body.Gm > 0.0))
                return Invalid(band,
                    $"the handover band at {body.Name} has no floor: the body has no " +
                    "gravitational parameter");

            if (!double.IsFinite(soi))
                return Invalid(band,
                    $"the handover band at {body.Name} has no ceiling: the body has no " +
                    "finite sphere of influence");

            band.GravityFloorRadiusM = Math.Sqrt(
                body.Gm / (FloorGravityFraction * driveAccelerationMps2));
            band.FloorRadiusM = Math.Max(band.GravityFloorRadiusM, band.ClearanceRadiusM);

            if (band.FloorRadiusM > band.CeilingRadiusM)
            {
                string clamp = band.FloorWasClamped
                    ? $" (the {Km(band.GravityFloorRadiusM)} gravity floor is inside the " +
                      $"body, so it is clamped up to the {Km(band.ClearanceRadiusM)} " +
                      "surface clearance)"
                    : "";
                return Invalid(band,
                    $"the handover band at {body.Name} is empty at " +
                    $"{driveAccelerationMps2:F2} m/s²: the floor is " +
                    $"{Km(band.FloorRadiusM)}{clamp}, above the " +
                    $"{Km(band.CeilingRadiusM)} ceiling ({CeilingSoiFraction:F2} × the " +
                    $"{Km(soi)} sphere of influence) by " +
                    Km(band.FloorRadiusM - band.CeilingRadiusM));
            }

            band.IsValid = true;
            return band;
        }

        public override string ToString() =>
            IsValid
                ? $"{Body.Name} handover band {Km(FloorRadiusM)} to {Km(CeilingRadiusM)} " +
                  $"at {DriveAccelerationMps2:F2} m/s²"
                : $"empty handover band at {Body?.Name}: {InvalidReason}";

        private static string Km(double metres) => $"{metres / 1000.0:F1} km";

        private static HandoverBand Invalid(HandoverBand band, string reason)
        {
            band.IsValid = false;
            band.InvalidReason = reason;
            return band;
        }
    }
}
