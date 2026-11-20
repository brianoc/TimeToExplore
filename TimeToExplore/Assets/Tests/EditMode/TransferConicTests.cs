using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The transfer-conic geometry and the cheap capture it produces. These
    /// tests pin the Oberth arithmetic, the plane, and the lower parking
    /// radius before the end-to-end flight suite exercises the whole chain.
    /// </summary>
    [Category("flight")]
    public class TransferConicTests
    {
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        private TransferConic BuildMars()
        {
            BodyRuntime mars = _model.Get("mars");
            double soi = SphereOfInfluence.RadiusM(mars);
            return TransferConic.Build(mars, new Vector3d(1.0, 0.2, 0.1), soi,
                5_000.0, ParkingOrbit.ConicRadiusM(mars),
                ParkingOrbit.PeriapsisRadiusM(mars));
        }

        [Test]
        public void TheChainEntersAtTheSphereOfInfluenceAndCoastsToPeriapsis()
        {
            TransferConic chain = BuildMars();

            TestContext.WriteLine($"e={chain.Eccentricity:F5}, " +
                                  $"r_p={chain.PeriapsisRadiusM / 1000.0:F1} km, " +
                                  $"r_a={chain.ApoapsisRadiusM / 1000.0:F1} km, " +
                                  $"inbound {chain.InboundSeconds / 86400.0:F2} d, " +
                                  $"transfer {chain.TransferSeconds / 3600.0:F2} h");

            Assert.That(chain.IsValid, Is.True, chain.InvalidReason);
            Assert.That(chain.EntryLocalPositionM.Magnitude,
                Is.EqualTo(chain.EntryRadiusM).Within(1.0));
            Assert.That(chain.PeriapsisRadiusM, Is.LessThan(chain.ApoapsisRadiusM));
            Assert.That(chain.InboundSeconds, Is.GreaterThan(0.0));
            Assert.That(chain.TransferSeconds, Is.GreaterThan(0.0));
            Assert.That(Vector3d.Dot(chain.EntryLocalPositionM, chain.EntryLocalVelocityMps),
                Is.LessThan(0.0), "the entry state is inbound");
            Assert.That(chain.PeriapsisLocalPositionM.Magnitude,
                Is.EqualTo(chain.PeriapsisRadiusM).Within(1.0));
            Assert.That(chain.ApoapsisLocalPositionM.Magnitude,
                Is.EqualTo(chain.ApoapsisRadiusM).Within(1.0));
        }

        [Test]
        public void TheCapturePlaneIsTheParkingEquatorialPlane()
        {
            TransferConic chain = BuildMars();
            BodyRuntime mars = _model.Get("mars");

            Assert.That(Math.Abs(Vector3d.Dot(chain.EntryLocalPositionM, mars.PoleEcliptic)),
                Is.LessThan(1.0));
            Assert.That(Math.Abs(Vector3d.Dot(chain.PeriapsisLocalPositionM, mars.PoleEcliptic)),
                Is.LessThan(1.0));
            Assert.That(Math.Abs(Vector3d.Dot(chain.ApoapsisLocalPositionM, mars.PoleEcliptic)),
                Is.LessThan(1.0));

            // Apoapsis is opposite periapsis, and both are prograde.
            Vector3d peri = chain.PeriapsisLocalPositionM.Normalized;
            Vector3d apo = chain.ApoapsisLocalPositionM.Normalized;
            Assert.That(Vector3d.Dot(peri, apo), Is.EqualTo(-1.0).Within(1e-9));
            Vector3d normal = Vector3d.Cross(peri, chain.TransferPeriapsisVelocityMps).Normalized;
            Assert.That(Vector3d.Dot(normal, mars.PoleEcliptic), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void TheBurnsAreRetrogradeAtPeriapsisAndProgradeAtApoapsis()
        {
            TransferConic chain = BuildMars();
            Vector3d progradeAtPeriapsis = chain.PeriapsisLocalPositionM
                .Normalized;
            Vector3d tangent = Vector3d.Cross(
                chain.Target.PoleEcliptic, progradeAtPeriapsis).Normalized;

            TestContext.WriteLine($"capture {chain.CaptureDeltaVMps / 1000.0:F2} km/s, " +
                                  $"circularise {chain.CircularisationDeltaVMps / 1000.0:F2} km/s");

            Assert.That(chain.CaptureDeltaVMps, Is.GreaterThan(0.0));
            Assert.That(Vector3d.Dot(chain.CaptureBurnVectorMps, tangent),
                Is.LessThan(0.0), "the periapsis burn removes energy");
            Assert.That(chain.CircularisationDeltaVMps, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(chain.TotalBurnDeltaVMps,
                Is.LessThan(chain.HyperbolicPeriapsisVelocityMps.Magnitude),
                "the capture costs less than the whole hyperbolic speed");
        }

        [Test]
        public void TheConicParkingRadiusIsBelowTheQuinticCeiling()
        {
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime jupiter = _model.Get("jupiter");

            Assert.That(ParkingOrbit.ConicRadiusM(earth),
                Is.LessThan(ParkingOrbit.RadiusM(earth, 10.0)));
            Assert.That(ParkingOrbit.ConicRadiusM(jupiter),
                Is.LessThan(ParkingOrbit.RadiusM(jupiter, 10.0)));

            Assert.That(ParkingOrbit.ConicRadiusM(earth),
                Is.EqualTo(7_327_000.0).Within(10_000.0));
            Assert.That(ParkingOrbit.ConicRadiusM(jupiter),
                Is.EqualTo(80_398_000.0).Within(100_000.0));
            Assert.That(ParkingOrbit.RadiusM(earth, 10.0),
                Is.EqualTo(9_983_000.0).Within(10_000.0));
            Assert.That(ParkingOrbit.RadiusM(jupiter, 10.0),
                Is.EqualTo(177_965_000.0).Within(100_000.0));
        }

        [Test]
        public void AnInvalidGeometryIsReportedRatherThanBuilt()
        {
            BodyRuntime mars = _model.Get("mars");

            TransferConic tooLow = TransferConic.Build(mars, new Vector3d(1, 0, 0),
                SphereOfInfluence.RadiusM(mars), 5_000.0,
                ParkingOrbit.ConicRadiusM(mars), mars.RadiusM * 0.5);
            Assert.That(tooLow.IsValid, Is.False);

            TransferConic noEntry = TransferConic.Build(mars, new Vector3d(1, 0, 0),
                1.0, 5_000.0,
                ParkingOrbit.ConicRadiusM(mars), ParkingOrbit.PeriapsisRadiusM(mars));
            Assert.That(noEntry.IsValid, Is.False);
        }

        private static TransferConic EntryAt(BodyRuntime target, double entryRadiusM,
            double entrySpeedMps) =>
            TransferConic.Build(target, new Vector3d(1.0, 0.2, 0.1), entryRadiusM,
                entrySpeedMps, ParkingOrbit.ConicRadiusM(target),
                ParkingOrbit.PeriapsisRadiusM(target));

        /// <summary>
        /// The inbound fall is set by where the chain is handed over and how fast
        /// it enters, not by the target's sphere of influence. Measured at the
        /// epoch: Uranus's 5 km/s fall is 117.1323 d from the 51.76 million km
        /// sphere and 0.02153 d (31 min) from the 38,643.0 km handover floor; at
        /// the floor, 50 km/s cuts it to 0.00623 d. Mercury's entry is its parking
        /// radius, because its 1 g floor is inside its own periapsis.
        /// </summary>
        [Test]
        public void TheInboundFallShortensWithTheEntryRadiusAndTheEntrySpeed()
        {
            BodyRuntime uranus = _model.Get("uranus");
            double soi = SphereOfInfluence.RadiusM(uranus);
            HandoverBand band = HandoverBand.For(uranus, 9.7);
            double floor = band.ArrivalRadiusM;

            TransferConic fromSoi = EntryAt(uranus, soi, 5_000.0);
            TransferConic fromFloor = EntryAt(uranus, floor, 5_000.0);
            TransferConic hotFromFloor = EntryAt(uranus, floor, 50_000.0);
            TransferConic hotFromSoi = EntryAt(uranus, soi, 50_000.0);

            TestContext.WriteLine($"uranus: soi {soi / 1e6:F3}e6 km, floor {floor / 1e3:F1} km; " +
                                  $"5 km/s {fromSoi.InboundSeconds / 86400.0:F4} d -> " +
                                  $"{fromFloor.InboundSeconds / 86400.0:F4} d; " +
                                  $"50 km/s {hotFromSoi.InboundSeconds / 86400.0:F4} d -> " +
                                  $"{hotFromFloor.InboundSeconds / 86400.0:F4} d");

            Assert.That(fromSoi.InboundSeconds, Is.EqualTo(117.1323 * 86400.0).Within(60.0),
                "the sphere-of-influence fall at 5 km/s");
            Assert.That(fromFloor.InboundSeconds, Is.EqualTo(0.02153 * 86400.0).Within(1.0),
                "the floor fall at 5 km/s");
            Assert.That(hotFromFloor.InboundSeconds, Is.EqualTo(0.00623 * 86400.0).Within(1.0),
                "a hot entry shortens the floor fall");
            Assert.That(hotFromSoi.InboundSeconds, Is.EqualTo(11.9776 * 86400.0).Within(60.0),
                "the sphere-of-influence fall at 50 km/s");

            Assert.That(fromFloor.EntryLocalPositionM.Magnitude,
                Is.EqualTo(floor).Within(1.0), "the chain enters at the floor");
            Assert.That(hotFromFloor.EntryLocalPositionM.Magnitude,
                Is.EqualTo(floor).Within(1.0));
            Assert.That(fromFloor.InboundSeconds, Is.LessThan(fromSoi.InboundSeconds / 1000.0),
                "the floor entry is three orders shorter than the sphere's");
            Assert.That(hotFromFloor.InboundSeconds, Is.LessThan(fromFloor.InboundSeconds));
            Assert.That(hotFromSoi.InboundSeconds, Is.LessThan(fromSoi.InboundSeconds));

            BodyRuntime mercury = _model.Get("mercury");
            HandoverBand mercuryBand = HandoverBand.For(mercury, 9.7);
            double mercuryEntry = Math.Max(mercuryBand.ArrivalRadiusM,
                ParkingOrbit.ConicRadiusM(mercury));
            TransferConic mercurySoi = EntryAt(mercury, SphereOfInfluence.RadiusM(mercury),
                5_000.0);
            TransferConic mercuryFloor = EntryAt(mercury, mercuryEntry, 5_000.0);

            TestContext.WriteLine($"mercury: entry {mercuryEntry / 1e3:F1} km " +
                                  $"(floor {mercuryBand.ArrivalRadiusM / 1e3:F1}, periapsis " +
                                  $"{ParkingOrbit.PeriapsisRadiusM(mercury) / 1e3:F1}); " +
                                  $"{mercurySoi.InboundSeconds:F1} s -> " +
                                  $"{mercuryFloor.InboundSeconds:F1} s");

            Assert.That(mercuryBand.ArrivalRadiusM,
                Is.LessThan(ParkingOrbit.PeriapsisRadiusM(mercury)),
                "Mercury's 1 g floor is inside its own capture periapsis");
            Assert.That(mercuryFloor.EntryRadiusM, Is.EqualTo(mercuryEntry).Within(1e-9));
            Assert.That(mercuryFloor.InboundSeconds, Is.EqualTo(0.00229 * 86400.0).Within(1.0),
                "Mercury's floor fall at 5 km/s");
            Assert.That(mercuryFloor.InboundSeconds, Is.LessThan(mercurySoi.InboundSeconds));
        }

        /// <summary>
        /// The periapsis capture burn is set by the entry (hyperbolic excess)
        /// speed and the periapsis radius, so at one entry speed the entry radius
        /// does not move it at all — the floor saves time, not delta-v. A hotter
        /// entry does grow it: 6.3691 km/s at 5 km/s against 39.0952 at 50 km/s at
        /// Uranus, and 3.4977 against 47.1731 at Mercury.
        /// </summary>
        [Test]
        public void TheCaptureBurnIsSetByTheEntrySpeedNotTheEntryRadius()
        {
            BodyRuntime uranus = _model.Get("uranus");
            HandoverBand band = HandoverBand.For(uranus, 9.7);
            double soi = SphereOfInfluence.RadiusM(uranus);
            double floor = band.ArrivalRadiusM;

            TransferConic fromSoi = EntryAt(uranus, soi, 5_000.0);
            TransferConic fromFloor = EntryAt(uranus, floor, 5_000.0);
            TransferConic hot = EntryAt(uranus, floor, 50_000.0);

            TestContext.WriteLine($"capture at 5 km/s: {fromSoi.CaptureDeltaVMps / 1e3:F4} km/s " +
                                  $"from the SOI, {fromFloor.CaptureDeltaVMps / 1e3:F4} from the " +
                                  $"floor; at 50 km/s {hot.CaptureDeltaVMps / 1e3:F4}");

            Assert.That(fromFloor.CaptureDeltaVMps,
                Is.EqualTo(fromSoi.CaptureDeltaVMps).Within(1e-6),
                "the entry radius does not change the periapsis burn at one speed");
            Assert.That(fromFloor.TotalBurnDeltaVMps,
                Is.EqualTo(fromSoi.TotalBurnDeltaVMps).Within(1e-6),
                "and the total chain burn is unchanged");
            Assert.That(fromFloor.CaptureDeltaVMps,
                Is.EqualTo(6.3691e3).Within(1.0));
            Assert.That(hot.CaptureDeltaVMps, Is.EqualTo(39.0952e3).Within(1.0),
                "a hotter entry costs more at periapsis");
            Assert.That(hot.CaptureDeltaVMps, Is.GreaterThan(fromFloor.CaptureDeltaVMps));

            BodyRuntime mercury = _model.Get("mercury");
            TransferConic mercurySoi = EntryAt(mercury, SphereOfInfluence.RadiusM(mercury),
                5_000.0);
            TransferConic mercuryHot = EntryAt(mercury, mercurySoi.EntryRadiusM, 50_000.0);
            Assert.That(mercurySoi.CaptureDeltaVMps, Is.EqualTo(3.4977e3).Within(1.0));
            Assert.That(mercuryHot.CaptureDeltaVMps, Is.EqualTo(47.1731e3).Within(1.0));
            Assert.That(mercuryHot.CaptureDeltaVMps,
                Is.GreaterThan(mercurySoi.CaptureDeltaVMps));
        }
    }
}
