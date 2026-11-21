using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The first-pass fuel readout: the rocket equation at the design
    /// exhaust velocity, its share of the departure mass, and the plan-level
    /// figures the flight panel prints.</summary>
    [Category("flight")]
    public class FuelModelTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
        }

        private TrajectoryPlan MarsPlan() =>
            new TripPlanner(_model, _gravity).Plan(
                FlightFixture.ShipAt(_model, "gateway"), "mars");

        [Test]
        public void ZeroDeltaVNeedsNoPropellant()
        {
            Assert.That(FuelModel.PropellantKg(0.0, 1_000_000.0), Is.EqualTo(0.0));
            Assert.That(FuelModel.MassRatio(0.0), Is.EqualTo(1.0));
            Assert.That(FuelModel.PropellantFraction(0.0), Is.EqualTo(0.0));
        }

        [Test]
        public void TheRocketEquationSetsThePropellantAndTheMassRatio()
        {
            // A delta-v equal to the exhaust velocity arrives with m0 = e m_dry:
            // 1,000 t dry needs 1,718.28 t of propellant.
            double propellant = FuelModel.PropellantKg(
                FuelModel.ExhaustVelocityMps, 1_000_000.0);

            Assert.That(propellant, Is.EqualTo(1_000_000.0 * (Math.E - 1.0)).Within(1e-9));
            Assert.That(propellant / 1000.0, Is.EqualTo(1_718.28).Within(0.01));
            Assert.That(FuelModel.MassRatio(FuelModel.ExhaustVelocityMps),
                Is.EqualTo(Math.E).Within(1e-12));
            Assert.That(FuelModel.PropellantFraction(FuelModel.ExhaustVelocityMps),
                Is.EqualTo(1.0 - 1.0 / Math.E).Within(1e-12));
        }

        [Test]
        public void PropellantRisesFasterThanDeltaV()
        {
            double first = FuelModel.PropellantKg(1_000_000.0, 1_000_000.0);
            double second = FuelModel.PropellantKg(2_000_000.0, 1_000_000.0);

            Assert.That(first, Is.GreaterThan(0.0));
            Assert.That(second, Is.GreaterThan(2.0 * first),
                "the rocket equation is exponential, not linear in delta-v");
        }

        [Test]
        public void APlanReportsThePropellantItsDeltaVImplies()
        {
            TrajectoryPlan plan = MarsPlan();

            Assert.That(plan.DryMassKg, Is.EqualTo(ShipState.DefaultDryMassKg));
            Assert.That(plan.PropellantKg,
                Is.EqualTo(FuelModel.PropellantKg(plan.DeltaVMps, plan.DryMassKg))
                    .Within(1e-6));
            Assert.That(plan.PropellantFraction,
                Is.EqualTo(FuelModel.PropellantFraction(plan.DeltaVMps)).Within(1e-12));

            TestContext.WriteLine($"Mars: {plan.DeltaVMps / 1000.0:N0} km/s, " +
                                  $"{plan.PropellantKg / 1000.0:N0} t, " +
                                  $"{plan.PropellantFraction * 100.0:0.0}% of departure mass");
        }

        [Test]
        public void AtAFixedTripTimeTheBurnLevelBarelyMovesTheFuel()
        {
            // The burn level changes how long the fixed burns last, not the
            // velocity change the crossing needs: a harder burn finishes sooner
            // for nearly the same delta-v, so the fuel readout is a function of
            // trip time, not of command thrust.
            var planner = new TripPlanner(_model, _gravity)
            {
                CruiseStyle = CruiseStyle.BurnCoastBurn,
                ArrivalStyle = CaptureStyle.Conic
            };
            const double days = 12.0;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            planner.BurnLevelG = 0.3;
            TrajectoryPlan gentle = planner.PlanAtDuration(ship, "mars",
                days * JulianDate.SecondsPerDay);
            planner.BurnLevelG = 1.0;
            TrajectoryPlan hard = planner.PlanAtDuration(ship, "mars",
                days * JulianDate.SecondsPerDay);

            Assert.That(gentle.FlightTimeSeconds,
                Is.EqualTo(days * JulianDate.SecondsPerDay).Within(1e-3));
            Assert.That(hard.FlightTimeSeconds,
                Is.EqualTo(days * JulianDate.SecondsPerDay).Within(1e-3));

            double ratio = hard.PropellantKg / gentle.PropellantKg;
            TestContext.WriteLine($"12 d to Mars: 0.3 g {gentle.DeltaVMps / 1000.0:N1} km/s, " +
                                  $"{gentle.PropellantKg / 1000.0:N0} t; " +
                                  $"1 g {hard.DeltaVMps / 1000.0:N1} km/s, " +
                                  $"{hard.PropellantKg / 1000.0:N0} t; ratio {ratio:F4}");

            Assert.That(ratio, Is.InRange(0.95, 1.05),
                "three times the burn level, nearly the same fuel at a fixed trip time");
        }

        [Test]
        public void InvalidInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelModel.PropellantKg(-1.0, 1_000_000.0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelModel.PropellantKg(double.NaN, 1_000_000.0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelModel.PropellantKg(1.0, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FuelModel.MassRatio(-1.0));
        }
    }
}
