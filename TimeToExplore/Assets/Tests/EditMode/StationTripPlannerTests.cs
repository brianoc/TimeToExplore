using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Trips to a **station** and trips that **depart from outside every sphere
    /// of influence**. A station is a massless point target: its terminal state is
    /// the station's own state at the arrival date, offset
    /// <see cref="TripPlanner.StationHoldDistanceM"/> astern along the station's
    /// velocity direction, with the ship's velocity matched to the station's.
    /// There is no sphere, no parking orbit, no capture chain, no handover band
    /// and no planetocentric approach; the final arc is the velocity-match burn.
    /// A ship outside every sphere has no break and no escape conic: its
    /// departure burn starts from its own state at t0.
    /// </summary>
    [Category("flight")]
    public class StationTripPlannerTests
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

        private TrajectoryPlan PlanAt(ShipState ship, string targetId, double days) =>
            _planner.PlanAtDuration(ship, targetId, days * JulianDate.SecondsPerDay);

        private static void PrintPlan(TrajectoryPlan plan)
        {
            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs)
            {
                TestContext.WriteLine($"  {arc.Phase,-10} {arc.Path.GetType().Name,-14} " +
                                      $"{arc.DurationSeconds / JulianDate.SecondsPerDay,9:F5} d  " +
                                      $"peak {arc.PeakThrustMps2,8:F4}  " +
                                      $"dv {arc.DeltaVMps / 1000.0,7:F3} km/s");
            }
        }

        /// <summary>The documented hold state: the station's own state at the
        /// date, 500 m astern along its velocity direction, with its velocity.</summary>
        private Vector3d HoldPosition(BodyRuntime station, double julianDate,
            out Vector3d stationVelocity)
        {
            _model.ComputeState(station, julianDate, out Vector3d position,
                out stationVelocity);
            return position - stationVelocity.Normalized * TripPlanner.StationHoldDistanceM;
        }

        /// <summary>
        /// The station arrival is a point-target rendezvous: the plan ends on the
        /// hold state at exactly the requested date, and every seam is inside the
        /// chain gate. From a Mars 300 km orbit to Gateway at 120 days the plan is
        /// the departure climb, the cruise burn, the coast and the velocity-match
        /// burn — five arcs — with no approach, no parking orbit and no capture
        /// chain. Measured at the epoch: 25.648 km/s total, and the brake's own end
        /// is 0.0006 m and 7.6 × 10⁻⁸ m/s from the hold state.
        /// </summary>
        [Test]
        public void AStationArrivalEndsOnTheHoldPointWithNoCaptureChain()
        {
            ShipState ship = FlightFixture.ShipInEquatorialOrbit(_model, "mars", 300e3);
            const double days = 120.0;
            TrajectoryPlan plan = PlanAt(ship, "gateway", days);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(5),
                "climb, cruise burn, coast, velocity-match burn");

            BodyRuntime gateway = _model.Get("gateway");
            double arrivalJd = ship.JulianDate + days;
            Assert.That(plan.ArrivalJulianDate, Is.EqualTo(arrivalJd).Within(1e-9),
                "the plan arrives at exactly the requested date");

            Vector3d hold = HoldPosition(gateway, plan.ArrivalJulianDate,
                out Vector3d stationVelocity);
            Assert.That(Vector3d.Distance(plan.ArrivalState.PositionM, hold),
                Is.LessThan(1.0), "the arrival state is the hold point");
            Assert.That((plan.ArrivalState.VelocityMps - stationVelocity).Magnitude,
                Is.LessThan(1e-3), "the arrival velocity matches the station's");

            // The last arc itself — not only the assigned arrival state — ends on
            // the hold state, so the brake is the plan's final leg.
            PlanArc last = plan.Arcs[plan.Arcs.Count - 1];
            Assert.That(last.Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(last.IsFiniteBurn, Is.True, "the final leg is the velocity match");
            Assert.That(Vector3d.Distance(last.Path.PositionAt(1.0), hold),
                Is.LessThan(1.0));
            Assert.That((last.Path.VelocityAt(1.0) - stationVelocity).Magnitude,
                Is.LessThan(1e-3));

            // A station has no capture chain and no planetocentric region.
            foreach (PlanArc arc in plan.Arcs)
            {
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Approach));
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Parking));
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Coast));
                if (arc.Phase == FlightPhase.Capture)
                    Assert.That(arc.IsFiniteBurn, Is.True,
                        "the station capture phase is the finite velocity match, not a conic");
                Assert.That(arc.ReferenceBodyId, Is.Not.EqualTo("gateway"),
                    "no arc is built about the massless station");
            }

            Assert.That(plan.CaptureEntryRadiusM, Is.EqualTo(0.0),
                "no handover radius is used");
            Assert.That(plan.CaptureInboundSeconds, Is.EqualTo(0.0),
                "no inbound capture fall is flown");

            AssertChainCloses(plan);
        }

        /// <summary>
        /// Both halves compose: a station-to-station trip (Ares Junction to
        /// Gateway at 160 days) starts from the ship's own state outside every
        /// sphere and ends on the destination's hold point. Measured at the epoch:
        /// three arcs, 12.239 km/s, and the last arc ends 0.00004 m and
        /// 1.9 × 10⁻⁸ m/s from the hold state.
        /// </summary>
        [Test]
        public void AStationToStationTripUsesBothNewHalves()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "ares_junction");
            TrajectoryPlan plan = PlanAt(ship, "gateway", 160.0);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(3),
                "no climb: cruise burn, coast, velocity-match burn");

            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[0].IsFiniteBurn, Is.True);
            Assert.That(Vector3d.Distance(plan.Arcs[0].Path.PositionAt(0.0),
                ship.PositionM), Is.LessThan(1e-6), "the burn starts where the ship is");
            Assert.That((plan.Arcs[0].Path.VelocityAt(0.0) - ship.VelocityMps).Magnitude,
                Is.LessThan(1e-6), "the burn starts on the ship's velocity");
            foreach (PlanArc arc in plan.Arcs)
            {
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Departure));
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Escape));
            }

            Assert.That(plan.ArrivalJulianDate,
                Is.EqualTo(ship.JulianDate + 160.0).Within(1e-9));
            BodyRuntime gateway = _model.Get("gateway");
            Vector3d hold = HoldPosition(gateway, plan.ArrivalJulianDate,
                out Vector3d stationVelocity);
            Assert.That(Vector3d.Distance(plan.ArrivalState.PositionM, hold),
                Is.LessThan(1.0));
            Assert.That((plan.ArrivalState.VelocityMps - stationVelocity).Magnitude,
                Is.LessThan(1e-3));

            AssertChainCloses(plan);
        }

        /// <summary>
        /// A ship outside every planet's sphere has a **heliocentric start**: no
        /// break arc, no escape conic, and a departure burn that begins on the
        /// ship's own state at t0. Ares Junction (Mars L1) to Earth at 230 days is
        /// the measured case: seven arcs — the planet arrival still builds its
        /// approach and conic capture — 24.091 km/s, and the plan arrives at
        /// exactly 230 days.
        /// </summary>
        [Test]
        public void AHeliocentricStartBurnsFromTheShipsOwnStateToEarth()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "ares_junction");
            TrajectoryPlan plan = PlanAt(ship, "earth", 230.0);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7),
                "cruise burn, coast, approach, brake, and the conic capture");

            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[0].IsFiniteBurn, Is.True);
            Assert.That(Vector3d.Distance(plan.Arcs[0].Path.PositionAt(0.0),
                ship.PositionM), Is.LessThan(1e-6));
            Assert.That((plan.Arcs[0].Path.VelocityAt(0.0) - ship.VelocityMps).Magnitude,
                Is.LessThan(1e-6));
            foreach (PlanArc arc in plan.Arcs)
            {
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Departure),
                    "there is nothing to break out of");
                Assert.That(arc.Phase, Is.Not.EqualTo(FlightPhase.Escape),
                    "there is no departure conic to climb");
            }

            Assert.That(plan.ArrivalJulianDate, Is.EqualTo(ship.JulianDate + 230.0)
                .Within(1e-9), "the requested date is met exactly");
            AssertChainCloses(plan);
        }

        /// <summary>
        /// The same heliocentric start to Mars at 200 days, from Helios Watch
        /// (Sun–Earth L1): seven arcs, 10.747 km/s, arriving exactly. Measured at
        /// the epoch.
        /// </summary>
        [Test]
        public void AHeliocentricStartReachesMars()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "helios_watch");
            TrajectoryPlan plan = PlanAt(ship, "mars", 200.0);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[0].IsFiniteBurn, Is.True);
            Assert.That((plan.Arcs[0].Path.VelocityAt(0.0) - ship.VelocityMps).Magnitude,
                Is.LessThan(1e-6));
            Assert.That(plan.ArrivalJulianDate, Is.EqualTo(ship.JulianDate + 200.0)
                .Within(1e-9));
            AssertChainCloses(plan);
        }

        /// <summary>
        /// The planet anchor re-baselines with the split: Gateway to Mars at
        /// 90 days and 0.3 g is 38.5411 km/s across nine arcs (the station target
        /// keeps the unforced climb, so this test and the station path are
        /// independent). A point-target station must not change the planet path.
        /// </summary>
        [Test]
        public void ThePlanetAnchorIsUnmovedByTheStationTarget()
        {
            TrajectoryPlan plan = PlanAt(FlightFixture.ShipAt(_model, "gateway"),
                "mars", 90.0);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(9));
            TestContext.WriteLine($"Gateway -> Mars 90 d: " +
                                  $"{plan.DeltaVMps / 1000.0:F4} km/s");
            Assert.That(Math.Round(plan.DeltaVMps / 1000.0, 4), Is.EqualTo(38.5411),
                "the re-baselined planet anchor is the split-aware value");
        }

        /// <summary>
        /// The nav computer's fastest station route. The zero-coast burn-only
        /// branch needs a capture chain, so it is skipped for a station and the
        /// coast floor is returned; from Ares Junction to Gateway at the epoch that
        /// is 54.16210 days and 69.541 km/s, and it still arrives on the hold state.
        /// </summary>
        [Test]
        public void TheFastestStationRouteIsTheCoastFloor()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "ares_junction");
            TrajectoryPlan plan = _planner.PlanFastest(ship, "gateway");
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(3));
            BodyRuntime gateway = _model.Get("gateway");
            Vector3d hold = HoldPosition(gateway, plan.ArrivalJulianDate,
                out Vector3d stationVelocity);
            Assert.That(Vector3d.Distance(plan.ArrivalState.PositionM, hold),
                Is.LessThan(1.0));
            Assert.That((plan.ArrivalState.VelocityMps - stationVelocity).Magnitude,
                Is.LessThan(1e-3));
            AssertChainCloses(plan);
        }

        /// <summary>
        /// A station is a valid point target; a genuinely massless body that is
        /// not a station is still refused with a reason naming the missing
        /// gravitational parameter. The catalogue has no such body, so the station
        /// kind is removed from a fresh model's copy to make one.
        /// </summary>
        [Test]
        public void AStationIsAcceptedAndAMasslessNonStationIsRefused()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "helios_watch");
            Assert.DoesNotThrow(() => PlanAt(ship, "ares_junction", 200.0),
                "a station point target is a valid Go To target");

            SolarSystemModel broken = CatalogueFixture.LoadModel();
            BodyRuntime massless = broken.Get("ares_junction");
            massless.Data.kind = "moon";
            massless.Data.gm = 0.0;
            var planner = new TripPlanner(broken, FlightFixture.FullField(broken));

            ArgumentException refusal = Assert.Throws<ArgumentException>(() =>
                planner.PlanAtDuration(FlightFixture.ShipAt(broken, "helios_watch"),
                    "ares_junction", 200.0 * JulianDate.SecondsPerDay));
            TestContext.WriteLine(refusal.Message);
            Assert.That(refusal.Message, Does.Contain("gravitational parameter"));
        }

        private static void AssertChainCloses(TrajectoryPlan plan)
        {
            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];
                double positionStep = Vector3d.Distance(
                    left.Path.PositionAt(1.0), right.Path.PositionAt(0.0));
                double velocityStep = (left.Path.VelocityAt(1.0)
                                       + right.EntryImpulseVectorMps
                                       - right.Path.VelocityAt(0.0)).Magnitude;

                TestContext.WriteLine($"seam {i} {left.Phase}->{right.Phase}: " +
                                      $"{positionStep:F3} m, {velocityStep:E3} m/s");
                Assert.That(positionStep, Is.LessThan(1e3),
                    $"seam {i} ({left.Phase}->{right.Phase}) position");
                Assert.That(velocityStep, Is.LessThan(1.0),
                    $"seam {i} ({left.Phase}->{right.Phase}) velocity");
            }
        }
    }
}
