using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The handover band a fixed burn hands over to a shaped end inside: the floor
    /// where the body's gravity is 40% of the drive, the ceiling at
    /// <c>1.02 × SOI</c>, the surface clamp on the floor, the empty-band rejection
    /// when the clamp or the ceiling leaves no band, and the body-clearance gate
    /// that replaced the sphere-of-influence rule on the fixed burns. Everything
    /// here is cheap: the band is arithmetic and the gate is a sampled arc, not a
    /// plan build.
    /// </summary>
    [Category("flight")]
    public class HandoverBandTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private TripPlanner _planner;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _planner = new TripPlanner(_model, _gravity);
        }

        private double DriveAt(double levelG)
        {
            _planner.BurnLevelG = levelG;
            return _planner.FixedBurnAccelerationMps2;
        }

        [Test]
        public void TheFloorScalesAsTheInverseSquareRootOfTheLevel()
        {
            // Halving the level asks for the same gravity fraction at a radius
            // sqrt(2) larger. Earth and Jupiter are both far enough from their
            // surfaces that the floor is the gravity floor at either level.
            foreach (string id in new[] { "earth", "jupiter" })
            {
                BodyRuntime body = _model.Get(id);
                HandoverBand strong = HandoverBand.For(body,
                    1.0 * TripPlanner.StandardGravityMps2);
                HandoverBand weak = HandoverBand.For(body,
                    0.5 * TripPlanner.StandardGravityMps2);

                TestContext.WriteLine($"{id}: 1 g floor {strong.FloorRadiusM / 1000.0:N1} km, " +
                                      $"0.5 g floor {weak.FloorRadiusM / 1000.0:N1} km, " +
                                      $"ratio {weak.FloorRadiusM / strong.FloorRadiusM:F9}");

                Assert.That(strong.IsValid, Is.True, strong.InvalidReason);
                Assert.That(weak.IsValid, Is.True, weak.InvalidReason);
                Assert.That(weak.FloorRadiusM / strong.FloorRadiusM,
                    Is.EqualTo(Math.Sqrt(2.0)).Within(1e-9), id);
            }

            // Mercury at 1 g has the floor inside the body, so the *band* floor is
            // the clearance and does not scale; the gravity floor under it still
            // does, which is what the clamp is applied to.
            BodyRuntime mercury = _model.Get("mercury");
            HandoverBand strongMercury = HandoverBand.For(mercury,
                1.0 * TripPlanner.StandardGravityMps2);
            HandoverBand weakMercury = HandoverBand.For(mercury,
                0.5 * TripPlanner.StandardGravityMps2);
            TestContext.WriteLine($"mercury gravity floor: 1 g " +
                                  $"{strongMercury.GravityFloorRadiusM / 1000.0:N1} km " +
                                  $"(clamped to {strongMercury.FloorRadiusM / 1000.0:N1}), " +
                                  $"0.5 g {weakMercury.GravityFloorRadiusM / 1000.0:N1} km");
            Assert.That(strongMercury.FloorWasClamped, Is.True);
            Assert.That(weakMercury.GravityFloorRadiusM / strongMercury.GravityFloorRadiusM,
                Is.EqualTo(Math.Sqrt(2.0)).Within(1e-9));
        }

        [Test]
        public void TheCeilingIsTheClearanceSphereOfEachBody()
        {
            BodyRuntime mercury = _model.Get("mercury");
            BodyRuntime uranus = _model.Get("uranus");
            HandoverBand inner = HandoverBand.For(mercury, DriveAt(1.0));
            HandoverBand outer = HandoverBand.For(uranus, DriveAt(1.0));

            TestContext.WriteLine($"mercury ceiling {inner.CeilingRadiusM / 1000.0:N1} km " +
                                  $"(SOI {SphereOfInfluence.RadiusM(mercury) / 1000.0:N1}), " +
                                  $"uranus ceiling {outer.CeilingRadiusM / 1000.0:N1} km " +
                                  $"(SOI {SphereOfInfluence.RadiusM(uranus) / 1000.0:N1})");

            Assert.That(inner.CeilingRadiusM / SphereOfInfluence.RadiusM(mercury),
                Is.EqualTo(HandoverBand.CeilingSoiFraction).Within(1e-12));
            Assert.That(outer.CeilingRadiusM / SphereOfInfluence.RadiusM(uranus),
                Is.EqualTo(HandoverBand.CeilingSoiFraction).Within(1e-12));

            // The ceiling is per body, by orders of magnitude, and the departure
            // endpoint is the ceiling.
            Assert.That(inner.CeilingRadiusM / 1000.0, Is.EqualTo(114_658.2).Within(1.0));
            Assert.That(outer.CeilingRadiusM / 1000.0, Is.EqualTo(52_795_223.9).Within(1.0));
            Assert.That(outer.CeilingRadiusM, Is.GreaterThan(inner.CeilingRadiusM * 100.0));
            Assert.That(inner.DepartureRadiusM, Is.EqualTo(inner.CeilingRadiusM));
            Assert.That(outer.ArrivalRadiusM, Is.EqualTo(outer.FloorRadiusM));
        }

        [Test]
        public void TheClampedFloorStaysAboveTheSurfaceOnEveryAcceptedBody()
        {
            var empty = new List<string>();
            int checkedBodies = 0;

            foreach (double level in new[] { 0.3, 1.0 })
            {
                double drive = DriveAt(level);
                foreach (BodyRuntime body in _model.Ordered)
                {
                    if (body.Kind == "star" || body.Kind == "station") continue;
                    if (body.Gm <= 0.0 || body.RadiusM <= 0.0) continue;

                    HandoverBand band = HandoverBand.For(body, drive);
                    if (!band.IsValid)
                    {
                        empty.Add($"{body.Id} at {level:F1} g");
                        continue;
                    }

                    double clearance = body.RadiusM + HandoverBand.MinimumClearanceAltitudeM;
                    Assert.That(band.FloorRadiusM, Is.GreaterThanOrEqualTo(clearance - 1e-6),
                        $"{body.Id} at {level:F1} g");
                    Assert.That(band.FloorRadiusM, Is.LessThanOrEqualTo(band.CeilingRadiusM),
                        $"{body.Id} at {level:F1} g");
                    Assert.That(band.Contains(band.FloorRadiusM), Is.True,
                        $"{body.Id} at {level:F1} g holds its own floor");
                    Assert.That(band.Contains(band.CeilingRadiusM), Is.True,
                        $"{body.Id} at {level:F1} g holds its own ceiling");
                    checkedBodies++;
                }
            }

            Assert.That(checkedBodies, Is.GreaterThan(80), "planets, moons, and asteroids");

            // The three bodies whose whole band is empty at every level the game
            // offers: their spheres of influence are barely outside their own
            // surfaces (Phobos and Deimos even sit inside them), so the 100 km
            // clearance alone is above the ceiling.
            TestContext.WriteLine("empty bands: " + string.Join(", ", empty));
            Assert.That(empty, Is.EquivalentTo(new[]
            {
                "phobos at 0.3 g", "phobos at 1.0 g",
                "deimos at 0.3 g", "deimos at 1.0 g",
                "proteus at 0.3 g", "proteus at 1.0 g"
            }));

            // The named cases: at 1 g the gravity floor is below the surface on
            // both, so the floor is the clearance and nothing else.
            BodyRuntime mercury = _model.Get("mercury");
            HandoverBand mercuryBand = HandoverBand.For(mercury, DriveAt(1.0));
            BodyRuntime mars = _model.Get("mars");
            HandoverBand marsBand = HandoverBand.For(mars, DriveAt(1.0));
            TestContext.WriteLine($"mercury 1 g floor {mercuryBand.FloorRadiusM / 1000.0:N1} km " +
                                  $"(surface {mercury.RadiusM / 1000.0:N1}, gravity floor " +
                                  $"{mercuryBand.GravityFloorRadiusM / 1000.0:N1}); " +
                                  $"mars 1 g floor {marsBand.FloorRadiusM / 1000.0:N1} km " +
                                  $"(surface {mars.RadiusM / 1000.0:N1}, gravity floor " +
                                  $"{marsBand.GravityFloorRadiusM / 1000.0:N1})");

            Assert.That(mercuryBand.FloorWasClamped, Is.True);
            Assert.That(mercuryBand.GravityFloorRadiusM, Is.LessThan(mercury.RadiusM));
            Assert.That(mercuryBand.FloorRadiusM,
                Is.EqualTo(mercury.RadiusM + HandoverBand.MinimumClearanceAltitudeM).Within(1e-6));
            Assert.That(marsBand.FloorWasClamped, Is.True);
            Assert.That(marsBand.GravityFloorRadiusM, Is.LessThan(mars.RadiusM));
            Assert.That(marsBand.FloorRadiusM,
                Is.EqualTo(mars.RadiusM + HandoverBand.MinimumClearanceAltitudeM).Within(1e-6));
        }

        [Test]
        public void AnEmptyBandIsReportedWithItsNumbersAndThePlannerRefusesTheTrip()
        {
            BodyRuntime phobos = _model.Get("phobos");
            HandoverBand band = HandoverBand.For(phobos, DriveAt(1.0));

            TestContext.WriteLine("reason: " + band.InvalidReason);
            Assert.That(band.IsValid, Is.False);
            Assert.That(band.InvalidReason, Does.Contain("empty"));
            Assert.That(band.InvalidReason, Does.Contain("Phobos"));
            Assert.That(band.InvalidReason, Does.Contain("111.3 km"), "the clamped floor");
            Assert.That(band.InvalidReason, Does.Contain("7.4 km"), "the ceiling");
            Assert.That(band.InvalidReason, Does.Contain("surface clearance"),
                "the clamp is named, not left as a mystery");
            Assert.That(band.FloorRadiusM, Is.GreaterThan(band.CeilingRadiusM));
            Assert.That(band.Contains(band.CeilingRadiusM), Is.False);

            // The planner refuses the trip at that target with the same reason,
            // rather than building a handover with no band. The check runs before
            // the coast skeleton, so this costs one cheap build.
            _planner.ArrivalStyle = CaptureStyle.Conic;
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = _planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "phobos", 100.0 * JulianDate.SecondsPerDay);

            TestContext.WriteLine("plan: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.False);
            Assert.That(plan.InfeasibleReason, Does.Contain("handover band"));
            Assert.That(plan.InfeasibleReason, Does.Contain("Phobos"));
            Assert.That(plan.Arcs.Count, Is.EqualTo(1), "a refusal, not a chain");
        }

        [Test]
        public void TheGateAcceptsABurnThatCrossesTheSphereOfInfluence()
        {
            BodyRuntime earth = _model.Get("earth");
            double soi = SphereOfInfluence.RadiusM(earth);

            // A fixed-direction burn started 1.5 spheres out and pushed straight
            // at the body: it comes inside the sphere, and stops well outside the
            // planet. That combination used to be refused.
            double minutes = 3.0 * 60.0;
            double seconds = minutes * 60.0;
            FiniteBurnArc arc = FiniteBurnArc.Create(_model, earth, _model.CurrentJulianDate,
                seconds, new Vector3d(1.5 * soi, 0.0, 0.0), Vector3d.Zero,
                DriveAt(1.0), retrograde: false, samples: 256,
                fixedThrustDirection: new Vector3d(-1.0, 0.0, 0.0));
            var planArc = new PlanArc(FlightPhase.Cruise, earth.Id, _model.CurrentJulianDate,
                _model.CurrentJulianDate + seconds / JulianDate.SecondsPerDay, arc);

            bool clears = _planner.FixedBurnClearsBody(planArc, earth, out double closest);
            TestContext.WriteLine($"crossing burn: closest {closest / 1000.0:N0} km from Earth " +
                                  $"(SOI {soi / 1000.0:N0} km, clearance " +
                                  $"{(earth.RadiusM + HandoverBand.MinimumClearanceAltitudeM) / 1000.0:N0} km), " +
                                  $"clears {clears}");

            Assert.That(clears, Is.True);
            Assert.That(closest, Is.LessThan(soi),
                "the burn does cross the sphere of influence");
            Assert.That(closest, Is.GreaterThanOrEqualTo(
                earth.RadiusM + HandoverBand.MinimumClearanceAltitudeM));
        }

        [Test]
        public void TheGateRejectsABurnThatGrazesTheBody()
        {
            BodyRuntime earth = _model.Get("earth");
            double startRadius = earth.RadiusM + 60_000.0;

            // A short burn 60 km up, pushed at the surface: it never reaches the
            // 100 km clearance, but it does not reach the ground either.
            const double seconds = 30.0;
            FiniteBurnArc arc = FiniteBurnArc.Create(_model, earth, _model.CurrentJulianDate,
                seconds, new Vector3d(startRadius, 0.0, 0.0), Vector3d.Zero,
                DriveAt(1.0), retrograde: false, samples: 128,
                fixedThrustDirection: new Vector3d(-1.0, 0.0, 0.0));
            var planArc = new PlanArc(FlightPhase.Cruise, earth.Id, _model.CurrentJulianDate,
                _model.CurrentJulianDate + seconds / JulianDate.SecondsPerDay, arc);

            bool clears = _planner.FixedBurnClearsBody(planArc, earth, out double closest);
            double clearance = earth.RadiusM + HandoverBand.MinimumClearanceAltitudeM;
            TestContext.WriteLine($"grazing burn: closest {closest / 1000.0:N3} km from Earth, " +
                                  $"altitude {(closest - earth.RadiusM) / 1000.0:N1} km, " +
                                  $"clearance {clearance / 1000.0:N3} km, clears {clears}");

            Assert.That(clears, Is.False);
            Assert.That(closest, Is.LessThan(clearance));
            Assert.That(closest, Is.GreaterThan(earth.RadiusM),
                "it grazes the body's clearance, it does not cut it");
        }
    }
}
