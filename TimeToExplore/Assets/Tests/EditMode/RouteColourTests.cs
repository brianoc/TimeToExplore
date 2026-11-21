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
    [Category("presentation")]
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
        public void TheProfileConicPlanShadesItsConicRunsAroundItsBurns()
        {
            TrajectoryPlan plan = BuildPlan(CaptureStyle.Conic);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));

            Assert.That(RouteColours.RunIndex(plan.Arcs, 0), Is.EqualTo(0));
            Assert.That(RouteColours.RunIndex(plan.Arcs, 1), Is.EqualTo(0),
                "a fixed burn starts its own run");
            Assert.That(RouteColours.RunIndex(plan.Arcs, 2), Is.EqualTo(0),
                "the heliocentric coast starts a new conic run");
            Assert.That(RouteColours.RunIndex(plan.Arcs, 3), Is.EqualTo(0),
                "the brake starts its own run");
            Assert.That(RouteColours.RunIndex(plan.Arcs, 4), Is.EqualTo(0),
                "the inbound coast restarts the conic run after the brake");
            Assert.That(RouteColours.RunIndex(plan.Arcs, 5), Is.EqualTo(1));
            Assert.That(RouteColours.RunIndex(plan.Arcs, 6), Is.EqualTo(2));

            for (int i = 2; i < 7; i++)
            {
                if (i == 3) continue;
                Assert.That(RouteColours.KindOf(plan.Arcs[i]), Is.EqualTo(RouteArcKind.Conic));
            }

            Color coast = RouteColours.For(plan.Arcs, 4).Colour;
            Color transfer = RouteColours.For(plan.Arcs, 5).Colour;
            Color parking = RouteColours.For(plan.Arcs, 6).Colour;

            Assert.That(transfer, Is.Not.EqualTo(coast));
            Assert.That(parking, Is.Not.EqualTo(coast));
            Assert.That(parking, Is.Not.EqualTo(transfer));
            AssertSameHue(coast, transfer);
            AssertSameHue(coast, parking);
        }

        [Test]
        public void TheHybridCaptureStartsANewConicRunAfterItsBurn()
        {
            TrajectoryPlan plan = BuildPlan(CaptureStyle.FiniteBurn);
            Assert.That(plan.Arcs.Count, Is.EqualTo(8));

            int burns = 0;
            int coasts = 0;
            for (int i = 0; i < plan.Arcs.Count; i++)
            {
                RouteArcKind kind = RouteColours.KindOf(plan.Arcs[i]);
                if (kind == RouteArcKind.FiniteBurn)
                {
                    burns++;
                    Assert.That(RouteColours.For(plan.Arcs, i).Colour,
                        Is.EqualTo(RouteColours.FixedBurn));
                    Assert.That(RouteColours.For(plan.Arcs, i).Dashed, Is.False);
                }
                if (kind == RouteArcKind.Conic) coasts++;
            }

            Assert.That(burns, Is.EqualTo(3),
                "departure burn, brake, and hybrid capture burn");
            Assert.That(coasts, Is.EqualTo(4),
                "heliocentric coast, inbound, transfer, parking");

            Color inbound = RouteColours.For(plan.Arcs, 4).Colour;
            Color transfer = RouteColours.For(plan.Arcs, 6).Colour;
            Color parking = RouteColours.For(plan.Arcs, 7).Colour;

            Assert.That(inbound, Is.EqualTo(transfer),
                "the capture burn ends the run, so the transfer restarts at the base shade");
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
