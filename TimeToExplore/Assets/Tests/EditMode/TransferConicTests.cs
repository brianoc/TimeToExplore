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
                Is.EqualTo(chain.SphereOfInfluenceM).Within(1.0));
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
    }
}
