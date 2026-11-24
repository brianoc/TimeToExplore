using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The automatic time-warp cap that keeps the scene honest.</summary>
    [Category("flight")]
    public class WarpPolicyTests
    {
        private const double Frame = 1.0 / 60.0;

        private SolarSystemModel _model;
        private GravityField _gravity;
        private double _epoch;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _epoch = _model.CurrentJulianDate;
        }

        [Test]
        public void DeepSpaceAllowsTheTopScale()
        {
            // Half an AU out from Earth, well away from any well.
            Vector3d earth = _model.Get("earth").PositionM;
            var ship = new ShipState(_epoch, earth + Vector3d.Up * 7.5e10,
                new Vector3d(30_000.0, 0.0, 0.0));

            double maxFrame = WarpPolicy.MaxFrameSeconds(ship, _epoch, _model, _gravity, null);

            TestContext.WriteLine($"deep-space frame cap: {maxFrame:N0} s");
            Assert.That(maxFrame, Is.GreaterThan(100_000.0));
            Assert.That(WarpPolicy.ClampScaleIndex(maxFrame, Frame, 6), Is.EqualTo(6),
                "1,000,000× survives in deep space");
        }

        [Test]
        public void AParkingOrbitForcesTheWarpDown()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _epoch, out Vector3d marsPosition, out Vector3d marsVelocity);
            ParkingOrbit.StateAt(mars, marsPosition, marsVelocity, 0.0,
                out Vector3d position, out Vector3d velocity);
            var ship = new ShipState(_epoch, position, velocity);

            double maxFrame = WarpPolicy.MaxFrameSeconds(ship, _epoch, _model, _gravity, null);

            TestContext.WriteLine($"Mars-orbit frame cap: {maxFrame:N2} s");
            Assert.That(maxFrame, Is.GreaterThan(1.0));
            Assert.That(maxFrame, Is.LessThan(60.0), "an orbit must stay a curve");
            Assert.That(WarpPolicy.ClampScaleIndex(maxFrame, Frame, 6), Is.LessThanOrEqualTo(3),
                "1,000× at most in a Mars parking orbit");
        }

        /// <summary>
        /// A station has no gravitational parameter, so the gravity field never
        /// selects it as the nearest body and the ordinary clearance cap cannot
        /// see it. A station plan adds its own clearance and hold-approach caps:
        /// two hundred kilometres astern at a hundred metres per second must
        /// warp far below the field's own limit, the hold point must be pinned to
        /// the one-second floor, and both caps must be non-binding far away.
        /// </summary>
        [Test]
        public void AStationApproachCapsTheFrameAndTheHoldIsPinnedToTheFloor()
        {
            var planner = new TripPlanner(_model, _gravity);
            TrajectoryPlan plan = planner.PlanFastest(
                FlightFixture.ShipAt(_model, "ares_junction"), "gateway");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            // Thirty days before the plan departs: the event pacing is far in
            // the future, so the physical caps are the binding ones.
            double julianDate = _epoch - 30.0;
            _model.ComputeState(_model.Get("gateway"), julianDate,
                out Vector3d stationPosition, out Vector3d stationVelocity);
            Vector3d astern = stationVelocity.Normalized;

            Vector3d approachPosition = stationPosition - astern * 200_000.0;
            var near = new ShipState(julianDate, approachPosition,
                stationVelocity + (stationPosition - approachPosition).Normalized * 100.0);
            var far = new ShipState(julianDate, stationPosition - astern * 2.0e9,
                stationVelocity + astern * 100.0);
            Vector3d holdPosition =
                stationPosition - astern * TripPlanner.StationHoldDistanceM;
            var atHold = new ShipState(julianDate, holdPosition, stationVelocity);

            double nearCap = WarpPolicy.MaxFrameSeconds(near, julianDate, _model, _gravity, plan);
            double farCap = WarpPolicy.MaxFrameSeconds(far, julianDate, _model, _gravity, plan);
            double holdCap = WarpPolicy.MaxFrameSeconds(atHold, julianDate, _model, _gravity, plan);
            double nearWithoutPlan =
                WarpPolicy.MaxFrameSeconds(near, julianDate, _model, _gravity, null);
            double farWithoutPlan =
                WarpPolicy.MaxFrameSeconds(far, julianDate, _model, _gravity, null);
            double boundary = plan.NextBoundaryJulianDate(julianDate);
            double eventCap = (boundary - julianDate) * JulianDate.SecondsPerDay
                              * WarpPolicy.EventFraction;

            TestContext.WriteLine($"station cap 200 km: {nearCap:F1} s (no plan " +
                                  $"{nearWithoutPlan:N0} s); far 2e9 m: {farCap:N0} s (no plan " +
                                  $"{farWithoutPlan:N0} s, event {eventCap:N0} s); " +
                                  $"at the hold: {holdCap:F1} s");

            Assert.That(nearCap, Is.LessThan(200.0),
                "the hold approach caps the frame near the station");
            Assert.That(nearCap, Is.GreaterThan(20.0),
                "the cap is not a stall: 5% of the time to the hold point");
            Assert.That(nearCap, Is.LessThan(nearWithoutPlan),
                "the station is invisible to the gravity field, so the plan must add the cap");
            Assert.That(farCap, Is.GreaterThan(10_000.0),
                "the caps are non-binding far from the station");
            Assert.That(farCap, Is.EqualTo(eventCap).Within(1.0),
                "far away the event pacing, not the station caps, sets the limit");
            Assert.That(farWithoutPlan, Is.GreaterThan(eventCap),
                "the field's own limit is looser than the event cap at that distance");
            Assert.That(farCap, Is.GreaterThan(10.0 * nearCap));
            Assert.That(holdCap, Is.EqualTo(WarpPolicy.MinFrameSeconds),
                "at the hold point the cap never drops below one simulated second");
            Assert.That(holdCap, Is.LessThan(farCap));

            Assert.That(WarpPolicy.ClampScaleIndex(nearCap, Frame, 6), Is.LessThanOrEqualTo(3),
                "the clock drops below 1,000x on the final approach");
            Assert.That(WarpPolicy.ClampScaleIndex(holdCap, Frame, 6), Is.LessThanOrEqualTo(1),
                "and to at most 10x at the hold point");
        }

        [Test]
        public void TheNextPlanBoundaryPacesTheFrame()
        {
            var planner = new TripPlanner(_model, _gravity);
            ShipState start = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = planner.Plan(start, "mars");

            double julianDate = 0.5 * (plan.DepartureJulianDate + plan.ArrivalJulianDate);
            Vector3d position = plan.ReferencePositionAt(julianDate);
            Vector3d velocity = plan.ReferenceVelocityAt(julianDate);
            var ship = new ShipState(julianDate, position, velocity);

            double boundary = plan.NextBoundaryJulianDate(julianDate);
            double remaining = (boundary - julianDate) * JulianDate.SecondsPerDay;
            double maxFrame = WarpPolicy.MaxFrameSeconds(ship, julianDate, _model, _gravity, plan);

            TestContext.WriteLine($"mid-cruise: {remaining:N0} s to the boundary, " +
                                  $"cap {maxFrame:N0} s per frame");

            Assert.That(maxFrame, Is.GreaterThan(1_000.0), "the event cap must not stall cruise");
            Assert.That(maxFrame, Is.EqualTo(remaining * WarpPolicy.EventFraction).Within(1.0),
                "5% of the time to the boundary, so the countdown lands on it");
        }

        [Test]
        public void PastTheArrivalThePlanStopsPacingTheFrame()
        {
            var planner = new TripPlanner(_model, _gravity);
            ShipState start = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = planner.Plan(start, "mars");

            var ship = new ShipState(plan.ArrivalJulianDate + 0.5,
                plan.ArrivalState.PositionM, plan.ArrivalState.VelocityMps);

            double withPlan = WarpPolicy.MaxFrameSeconds(ship, ship.JulianDate, _model, _gravity, plan);
            double withoutPlan = WarpPolicy.MaxFrameSeconds(ship, ship.JulianDate, _model, _gravity,
                null);

            Assert.That(withPlan, Is.EqualTo(withoutPlan).Within(1e-6),
                "a complete plan has no boundary left to pace");
        }

        [Test]
        public void TheScaleNeverExceedsThePlayersRequest()
        {
            Assert.That(WarpPolicy.ClampScaleIndex(1.0e9, Frame, 2), Is.EqualTo(2));
            Assert.That(WarpPolicy.ClampScaleIndex(double.PositiveInfinity, Frame, 5), Is.EqualTo(5));
        }

        [Test]
        public void TheScaleDropsToTheLargestAllowedPowerOfTen()
        {
            // 5,000 s per frame at 60 fps is a scale of 300,000; the largest
            // clock scale at or below it is 100,000. Five seconds is 300×.
            Assert.That(WarpPolicy.ClampScaleIndex(5_000.0, Frame, 6), Is.EqualTo(5));
            Assert.That(WarpPolicy.ClampScaleIndex(5.0, Frame, 6), Is.EqualTo(2));
        }

        [Test]
        public void GoToSuggestsAWarpThatKeepsTheTripWatchable()
        {
            var planner = new TripPlanner(_model, _gravity);
            ShipState start = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = planner.Plan(start, "mars");

            int index = WarpPolicy.SuggestInitialScaleIndex(plan);
            double scale = SimulationClock.TimeScales[index];
            double realSeconds = plan.FlightTimeSeconds / scale;

            TestContext.WriteLine($"Mars: {plan.FlightTimeDays:F2} d at " +
                                  $"{SolarSystemBootstrap.ScaleLabel(index)} = {realSeconds:F1} real s");

            Assert.That(index, Is.GreaterThan(0), "a two-week trip cannot start at 1×");
            Assert.That(realSeconds, Is.GreaterThan(10.0));
            // The ladder is powers of ten, so the 16.8-day floor lands on the
            // 10,000× step (about 145 s) rather than the 100,000× step's 14.5 s.
            Assert.That(realSeconds, Is.LessThan(200.0));
        }
    }
}
