using NUnit.Framework;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The route palette: one colour per arc type, with a shade change inside a
    /// run of the same type so back-to-back arcs stay legible. The mapping is
    /// pure C#, so it is checked without a scene; <c>BootstrapSmokeTests</c>
    /// proves the line renderer applies it and <c>SolarSystemHudTests</c>
    /// proves the legend lists the same types.
    /// </summary>
    public class RouteColourTests
    {
        [Test]
        public void ThereAreThreeArcTypesToColour()
        {
            Assert.That(System.Enum.GetValues(typeof(RouteArcKind)).Length, Is.EqualTo(3),
                "the project has exactly the quintic, conic, and finite-burn paths");
        }

        [Test]
        public void EveryArcTypeHasItsOwnColour()
        {
            Color quintic = RouteColours.BaseColour(RouteArcKind.Quintic);
            Color conic = RouteColours.BaseColour(RouteArcKind.Conic);
            Color burn = RouteColours.BaseColour(RouteArcKind.FiniteBurn);

            Assert.That(quintic, Is.EqualTo(RouteColours.Quintic));
            Assert.That(conic, Is.EqualTo(RouteColours.Conic));
            Assert.That(burn, Is.EqualTo(RouteColours.FixedBurn));
            Assert.That(quintic, Is.Not.EqualTo(conic));
            Assert.That(quintic, Is.Not.EqualTo(burn));
            Assert.That(conic, Is.Not.EqualTo(burn));
        }

        [Test]
        public void TheDriveStateChoosesSolidOrDashed()
        {
            Assert.That(RouteColours.For(RouteArcKind.Quintic, 0).Dashed, Is.False);
            Assert.That(RouteColours.For(RouteArcKind.FiniteBurn, 0).Dashed, Is.False);
            Assert.That(RouteColours.For(RouteArcKind.Conic, 0).Dashed, Is.True);
        }

        [Test]
        public void ConsecutiveArcsOfOneTypeTakeDistinctShadesOfTheSameHue()
        {
            RouteArcStyle first = RouteColours.For(RouteArcKind.Conic, 0);
            RouteArcStyle second = RouteColours.For(RouteArcKind.Conic, 1);
            RouteArcStyle third = RouteColours.For(RouteArcKind.Conic, 2);

            Assert.That(first.Colour, Is.EqualTo(RouteColours.Conic));
            Assert.That(second.Colour, Is.Not.EqualTo(first.Colour),
                "the second conic must not merge with the first");
            Assert.That(third.Colour, Is.Not.EqualTo(first.Colour));
            Assert.That(third.Colour, Is.Not.EqualTo(second.Colour));

            AssertSameHue(first.Colour, second.Colour);
            AssertSameHue(first.Colour, third.Colour);
        }

        [Test]
        public void ARealQuinticPlanShadesItsThreeQuinticArcs()
        {
            TrajectoryPlan plan = BuildPlan(CaptureStyle.Quintic);

            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
            foreach (PlanArc arc in plan.Arcs)
                Assert.That(RouteColours.KindOf(arc), Is.EqualTo(RouteArcKind.Quintic));

            Color first = RouteColours.For(plan.Arcs, 0).Colour;
            Color second = RouteColours.For(plan.Arcs, 1).Colour;
            Color third = RouteColours.For(plan.Arcs, 2).Colour;

            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(third, Is.Not.EqualTo(first));
            Assert.That(third, Is.Not.EqualTo(second));
            AssertSameHue(first, second);
            AssertSameHue(first, third);
        }

        [Test]
        public void ARealConicPlanShadesTheThreeConicsAfterTheTwoQuintics()
        {
            TrajectoryPlan plan = BuildPlan(CaptureStyle.Conic);

            Assert.That(RouteColours.RunIndex(plan.Arcs, 0), Is.EqualTo(0));
            Assert.That(RouteColours.RunIndex(plan.Arcs, 1), Is.EqualTo(1),
                "the cruise is the second quintic in a row");
            Assert.That(RouteColours.RunIndex(plan.Arcs, 2), Is.EqualTo(0),
                "the coast starts a new run");
            Assert.That(RouteColours.RunIndex(plan.Arcs, 3), Is.EqualTo(1));
            Assert.That(RouteColours.RunIndex(plan.Arcs, 4), Is.EqualTo(2));

            for (int i = 2; i < 5; i++)
                Assert.That(RouteColours.KindOf(plan.Arcs[i]), Is.EqualTo(RouteArcKind.Conic));

            Color coast = RouteColours.For(plan.Arcs, 2).Colour;
            Color transfer = RouteColours.For(plan.Arcs, 3).Colour;
            Color parking = RouteColours.For(plan.Arcs, 4).Colour;

            Assert.That(transfer, Is.Not.EqualTo(coast));
            Assert.That(parking, Is.Not.EqualTo(coast));
            Assert.That(parking, Is.Not.EqualTo(transfer));
            AssertSameHue(coast, transfer);
            AssertSameHue(coast, parking);
        }

        [Test]
        public void TheHybridStartsANewConicRunAfterItsBurn()
        {
            TrajectoryPlan plan = BuildPlan(CaptureStyle.FiniteBurn);

            int burn = -1;
            int coasts = 0;
            for (int i = 0; i < plan.Arcs.Count; i++)
            {
                if (RouteColours.KindOf(plan.Arcs[i]) == RouteArcKind.FiniteBurn) burn = i;
                if (RouteColours.KindOf(plan.Arcs[i]) == RouteArcKind.Conic) coasts++;
            }

            Assert.That(burn, Is.EqualTo(3), "the fixed burn is the fourth arc");
            Assert.That(coasts, Is.EqualTo(3), "inbound coast, transfer coast, parking");
            Assert.That(RouteColours.For(plan.Arcs, burn).Colour,
                Is.EqualTo(RouteColours.FixedBurn));
            Assert.That(RouteColours.For(plan.Arcs, burn).Dashed, Is.False);

            Color inbound = RouteColours.For(plan.Arcs, 2).Colour;
            Color transfer = RouteColours.For(plan.Arcs, 4).Colour;
            Color parking = RouteColours.For(plan.Arcs, 5).Colour;

            Assert.That(inbound, Is.EqualTo(transfer),
                "the burn ends the run, so the transfer coast restarts at the base shade");
            Assert.That(parking, Is.Not.EqualTo(transfer),
                "the parking coast is the second conic in the new run");
            AssertSameHue(inbound, parking);
        }

        private static void AssertSameHue(Color a, Color b)
        {
            Color.RGBToHSV(a, out float hueA, out _, out _);
            Color.RGBToHSV(b, out float hueB, out _, out _);
            Assert.That(Mathf.Abs(hueA - hueB), Is.LessThan(0.01f),
                "a shade change must not leave the type's colour family");
        }

        private static TrajectoryPlan BuildPlan(CaptureStyle style)
        {
            var model = CatalogueFixture.LoadModel();
            GravityField gravity = FlightFixture.FullField(model);
            var planner = new TripPlanner(model, gravity) { ArrivalStyle = style };
            return planner.Plan(FlightFixture.ShipAt(model, "gateway"), "mars");
        }
    }
}
