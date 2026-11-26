using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The burn–coast–burn cruise with the physical departure: a short quintic
    /// break out of the parking orbit onto the ship's own prograde escape state,
    /// a ballistic planetocentric conic from the parking radius to the departure
    /// handover ceiling (parabolic when the trip allows it, hyperbolic when it does
    /// not), a prograde finite burn at the chosen level, a heliocentric Lambert
    /// coast, a planetocentric approach about the target, a retrograde finite
    /// brake, and the existing conic or hybrid capture. The break is always the
    /// commanded-level burn; the escape conic carries the climb, not the break.
    /// The profile is the planner's only route; a trip that starts inside the
    /// target's sphere of influence is a single quintic capture.
    /// </summary>
    [Category("flight")]
    public class BurnCoastBurnTripPlannerTests
    {
        private const double TripDays = 90.0;

        /// <summary>The clearance the profile pins its break to — the handover
        /// band's ceiling — as a multiple of the departure body's sphere of
        /// influence.</summary>
        private const double ClearanceSoi = 1.02;

        private SolarSystemModel _model;
        private GravityField _gravity;
        private ShipIntegrator _integrator;
        private TripPlanner _planner;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _integrator = new ShipIntegrator(_gravity);
            _planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
        }

        private TrajectoryPlan PlanTo(string targetId, double days = TripDays) =>
            _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"), targetId,
                days * JulianDate.SecondsPerDay);

        private static void PrintPlan(TrajectoryPlan plan)
        {
            TestContext.WriteLine(plan.ToString());
            foreach (PlanArc arc in plan.Arcs)
            {
                string burn = arc.IsFiniteBurn
                    ? $" [{(FiniteBurnArc)arc.Path}.{((FiniteBurnArc)arc.Path).AccelerationMps2:F3} m/s²]"
                    : arc.IsCoast ? " [coast]" : "";
                TestContext.WriteLine($"  {arc.Phase,-10} {arc.Path.GetType().Name,-14} " +
                                      $"{arc.DurationSeconds / JulianDate.SecondsPerDay,9:F4} d  " +
                                      $"peak {arc.PeakThrustMps2,8:F4}  " +
                                      $"dv {arc.DeltaVMps / 1000.0,8:F3} km/s  " +
                                      $"imp {arc.EntryImpulseMps,8:F3} m/s{burn}");
            }
        }

        private static PlanArc FindArc(TrajectoryPlan plan, FlightPhase phase,
            bool finiteBurn)
        {
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase == phase && arc.IsFiniteBurn == finiteBurn) return arc;
            }

            return null;
        }

        [Test]
        public void TheProfileIsAShortBreakAnEscapeConicABurnACoastAnApproachABrakeAndTheCapture()
        {
            TrajectoryPlan plan = PlanTo("mars");
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(9));

            // leave orbit: a short commanded-level break
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[0].Path, Is.InstanceOf<Quintic>());

            // climb out of the well: a ballistic conic about the departure body
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(plan.Arcs[1].Path, Is.InstanceOf<ConicArc>());
            Assert.That(((ConicArc)plan.Arcs[1].Path).Centre, Is.SameAs(_model.Get("earth")));
            Assert.That(plan.Arcs[1].ReferenceBodyId, Is.EqualTo("earth"));

            // burn hard, prograde about the Sun
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].IsFiniteBurn, Is.True);
            var departureBurn = (FiniteBurnArc)plan.Arcs[2].Path;
            Assert.That(departureBurn.Centre, Is.SameAs(_model.Sun));
            Assert.That(departureBurn.IsRetrograde, Is.False);
            Assert.That(plan.Arcs[2].Path.VelocityAt(1.0).Magnitude,
                Is.GreaterThan(plan.Arcs[2].Path.VelocityAt(0.0).Magnitude),
                "the departure burn builds speed");

            // coast, about the Sun, ending on the target's handover ceiling
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[3].Path, Is.InstanceOf<ConicArc>());
            Assert.That(((ConicArc)plan.Arcs[3].Path).Centre, Is.SameAs(_model.Sun));

            // the planetocentric approach, about the target
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Approach));
            Assert.That(plan.Arcs[4].IsCoast, Is.True);
            Assert.That(((ConicArc)plan.Arcs[4].Path).Centre, Is.SameAs(_model.Get("mars")));
            Assert.That(plan.Arcs[4].ReferenceBodyId, Is.EqualTo("mars"));

            // brake, retrograde about the Sun, shedding speed
            Assert.That(plan.Arcs[5].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[5].IsFiniteBurn, Is.True);
            var brake = (FiniteBurnArc)plan.Arcs[5].Path;
            Assert.That(brake.Centre, Is.SameAs(_model.Sun));
            Assert.That(brake.IsRetrograde, Is.True);
            Assert.That(plan.Arcs[5].Path.VelocityAt(1.0).Magnitude,
                Is.LessThan(plan.Arcs[5].Path.VelocityAt(0.0).Magnitude),
                "the brake sheds speed");

            // enter orbit on the existing conic capture chain
            Assert.That(plan.Arcs[6].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[6].IsCoast, Is.True);
            Assert.That(plan.Arcs[7].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[8].Phase, Is.EqualTo(FlightPhase.Parking));
            Assert.That(plan.Arcs[8].EndJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-12));
        }

        [Test]
        public void TheCommandedBurnLevelIsRespectedOnBothBurns()
        {
            foreach (double level in new[] { 0.1, 0.3, 0.6 })
            {
                _planner.BurnLevelG = level;
                TrajectoryPlan plan = PlanTo("mars");
                Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

                double expected = Math.Min(level * TripPlanner.StandardGravityMps2,
                    _planner.MaxAccelerationMps2 * _planner.ThrustMargin);
                TestContext.WriteLine($"{level:F2} g: expected {expected:F3} m/s², " +
                                      $"plan {plan.FiniteBurnAccelerationMps2:F3}");

                Assert.That(plan.FiniteBurnAccelerationMps2,
                    Is.EqualTo(expected).Within(1e-6));

                foreach (PlanArc arc in plan.Arcs)
                {
                    if (!arc.IsFiniteBurn) continue;
                    Assert.That(((FiniteBurnArc)arc.Path).AccelerationMps2,
                        Is.EqualTo(expected).Within(1e-6), $"{arc.Phase} burn level");
                    Assert.That(arc.PeakThrustMps2, Is.EqualTo(expected).Within(0.05),
                        $"{arc.Phase} measured thrust");
                }
            }
        }

        [Test]
        public void TheDepartureBurnIsSizedFromTheVelocityChangeNotTheTimeShare()
        {
            _planner.BurnLevelG = 0.3;
            TrajectoryPlan strong = PlanTo("mars");
            _planner.BurnLevelG = 0.15;
            TrajectoryPlan weak = PlanTo("mars");

            PlanArc strongBurn = FindArc(strong, FlightPhase.Cruise, true);
            PlanArc weakBurn = FindArc(weak, FlightPhase.Cruise, true);
            Assert.That(strongBurn, Is.Not.Null);
            Assert.That(weakBurn, Is.Not.Null);

            TestContext.WriteLine($"0.30 g: {strongBurn.DeltaVMps / 1000.0:F3} km/s in " +
                                  $"{strongBurn.DurationSeconds / 60.0:F1} min; " +
                                  $"0.15 g: {weakBurn.DeltaVMps / 1000.0:F3} km/s in " +
                                  $"{weakBurn.DurationSeconds / 60.0:F1} min");

            // The burn's start state is fixed by the climb, so its path at the two
            // levels differs by a hair and the spend follows by about a tenth of a
            // percent. Half a percent bounds that measured level dependence.
            Assert.That(weakBurn.DeltaVMps,
                Is.EqualTo(strongBurn.DeltaVMps).Within(0.005 * strongBurn.DeltaVMps),
                "the burn spends the same delta-v at either level");
            Assert.That(weakBurn.DurationSeconds / strongBurn.DurationSeconds,
                Is.EqualTo(2.0).Within(0.05), "half the level is twice the burn");
        }

        [Test]
        public void TheHandoverAtTheArrivalFloorIsFlownNotImpulsive()
        {
            TrajectoryPlan plan = PlanTo("mars");
            PlanArc inbound = null;
            int chainStart = plan.Arcs.Count;
            for (int i = 0; i < plan.Arcs.Count; i++)
            {
                if (plan.Arcs[i].Phase == FlightPhase.Coast
                    && plan.Arcs[i].ReferenceBodyId == "mars")
                {
                    inbound = plan.Arcs[i];
                    chainStart = i;
                    break;
                }
            }

            Assert.That(inbound, Is.Not.Null);
            Assert.That(inbound.EntryImpulseMps, Is.EqualTo(0.0));

            // The only impulses left are the capture chain's own trim and
            // circularisation, which are not at the handover point.
            double chainImpulses = 0.0;
            for (int i = chainStart; i < plan.Arcs.Count; i++)
                chainImpulses += plan.Arcs[i].EntryImpulseMps;

            TestContext.WriteLine($"chain impulses {chainImpulses / 1000.0:F2} km/s, " +
                                  $"total {plan.ImpulseDeltaVMps / 1000.0:F2} km/s");
            Assert.That(plan.ImpulseDeltaVMps, Is.EqualTo(chainImpulses).Within(1e-6));
        }

        /// <summary>
        /// The burn–coast–burn's capture chain enters at the arrival handover
        /// band's floor, not the target's sphere of influence: the brake ends
        /// there, so the fall starts there. Measured at the epoch from Gateway to
        /// Uranus at 1 g and 400 days, the entry is 38,643.0 km (the band's floor,
        /// above the 29,166 km conic parking radius), the fall is 1,860.2 s
        /// against the old 117.13 days from the 51.76 million km sphere, and every
        /// seam closes inside the 1 km / 1 m/s gate.
        /// </summary>
        [Test]
        public void TheChainEntersAtTheArrivalHandoverFloor()
        {
            _planner.BurnLevelG = 1.0;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "uranus",
                400.0 * JulianDate.SecondsPerDay);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            PrintPlan(plan);

            BodyRuntime uranus = _model.Get("uranus");
            double acceleration = _planner.FixedBurnAccelerationMps2;
            HandoverBand band = HandoverBand.For(uranus, acceleration);
            double entry = TripPlanner.ArrivalEntryRadiusM(band, uranus);

            TestContext.WriteLine($"entry {plan.CaptureEntryRadiusM / 1000.0:F1} km, " +
                                  $"band floor {band.ArrivalRadiusM / 1000.0:F1} km, " +
                                  $"parking {ParkingOrbit.ConicRadiusM(uranus) / 1000.0:F1} km, " +
                                  $"fall {plan.CaptureInboundSeconds:F1} s");
            Assert.That(plan.CaptureEntryRadiusM, Is.EqualTo(band.ArrivalRadiusM).Within(1.0),
                "the entry is the band's arrival endpoint");
            Assert.That(plan.CaptureEntryRadiusM, Is.EqualTo(entry).Within(1.0));
            Assert.That(plan.CaptureEntryRadiusM,
                Is.EqualTo(38_643_000.0).Within(1_000.0),
                "measured: 38,643.0 km at 1 g");
            Assert.That(plan.CaptureInboundSeconds,
                Is.EqualTo(0.02153 * JulianDate.SecondsPerDay).Within(1.0),
                "measured: a 31-minute fall, not 117 days");
            Assert.That(plan.CaptureInboundSeconds,
                Is.LessThan(3_600.0), "the fall is under an hour");

            // The capture chain's own burn is the chain's, unchanged by the
            // radius: 6.369 km/s at the 5 km/s entry.
            double captureImpulse = 0.0;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase == FlightPhase.Capture && !arc.IsFiniteBurn)
                    captureImpulse = arc.EntryImpulseMps;
            }

            Assert.That(captureImpulse, Is.EqualTo(6_369.1).Within(1.0),
                "the periapsis capture burn is the chain's");

            AssertChainCloses(plan);
        }

        /// <summary>
        /// The arrival leg is planetocentric from the handover ceiling down. At the
        /// epoch from Gateway to Uranus at 1 g and 400 days the heliocentric coast
        /// now ends on the 52,795,223.9 km ceiling (1.02 × the sphere) and a
        /// 7.544-day conic about Uranus falls to the brake's start, which is
        /// 368,111.4 km out. The approach is a free fall — no burn, and its own
        /// third-body correction is 0.007 km/s against the old coast's 0.221 — and
        /// it never reaches the 38,643.0 km floor: its periapsis is 84,354.2 km, so
        /// the brake still spans the last part of the descent. Both seams close
        /// inside the 1 km / 1 m/s gate.
        /// </summary>
        [Test]
        public void TheArrivalLegIsAPlanetocentricApproachFromTheCeiling()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = _planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "uranus",
                400.0 * JulianDate.SecondsPerDay);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            PrintPlan(plan);

            BodyRuntime uranus = _model.Get("uranus");
            HandoverBand band = HandoverBand.For(uranus, _planner.FixedBurnAccelerationMps2);
            double ceiling = band.CeilingRadiusM;

            PlanArc coast = null;
            PlanArc approach = null;
            PlanArc brake = null;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase == FlightPhase.Cruise && arc.IsCoast) coast = arc;
                if (arc.Phase == FlightPhase.Approach) approach = arc;
                if (arc.Phase == FlightPhase.Capture && arc.IsFiniteBurn) brake = arc;
            }

            Assert.That(coast, Is.Not.Null);
            Assert.That(approach, Is.Not.Null, "the slow approach has its own arc");
            Assert.That(brake, Is.Not.Null);

            // The coast's final radius is the ceiling, not the deep well.
            _model.ComputeState(uranus, coast.EndJulianDate,
                out Vector3d uranusAtCoastEnd, out Vector3d uranusVelocityAtCoastEnd);
            double coastEndRadius = Vector3d.Distance(
                coast.Path.PositionAt(1.0), uranusAtCoastEnd);
            TestContext.WriteLine($"coast end {coastEndRadius / 1000.0:F1} km from Uranus, " +
                                  $"ceiling {ceiling / 1000.0:F1} km, " +
                                  $"approach {approach.DurationSeconds / 3600.0:F3} h, " +
                                  $"coast third-body {coast.DeltaVMps / 1000.0:F4} km/s");

            Assert.That(coastEndRadius, Is.EqualTo(ceiling).Within(10.0),
                "the coast ends on the handover ceiling");
            Assert.That(coastEndRadius, Is.EqualTo(52_795_223_900.0).Within(1_000.0),
                "measured: 1.02 × Uranus's 51,760,023 km sphere");
            Assert.That(coast.DeltaVMps / 1000.0, Is.LessThan(0.01),
                "the coast's own third-body correction is the ceiling's, not the well's");

            // The approach starts on the ceiling and ends where the brake begins.
            _model.ComputeState(uranus, approach.StartJulianDate,
                out Vector3d uranusAtStart, out Vector3d uranusVelocityAtStart);
            _model.ComputeState(uranus, approach.EndJulianDate,
                out Vector3d uranusAtEnd, out Vector3d uranusVelocityAtEnd);
            Vector3d startPosition = approach.Path.PositionAt(0.0) - uranusAtStart;
            Vector3d startVelocity = approach.Path.VelocityAt(0.0) - uranusVelocityAtStart;
            Vector3d endPosition = approach.Path.PositionAt(1.0) - uranusAtEnd;
            Assert.That(startPosition.Magnitude, Is.EqualTo(ceiling).Within(1.0),
                "the approach's start radius is the ceiling");
            Assert.That(endPosition.Magnitude,
                Is.EqualTo(361_329_160.6).Within(5_000.0),
                "measured: the brake starts 374,026.9 km out");

            Assert.That(approach.IsCoast, Is.True, "the approach is a free fall");
            Assert.That(approach.DeltaVMps / 1000.0, Is.LessThan(0.01),
                "a conic needs no burn");
            Assert.That(approach.PeakThrustMps2, Is.LessThan(0.1),
                "only the third-body correction is commanded");

            // It clears the body: its periapsis is far above the handover floor.
            ConicElements elements = ConicElements.FromState(startPosition, startVelocity,
                uranus.Gm);
            TestContext.WriteLine($"approach e {elements.Eccentricity:F3}, " +
                                  $"periapsis {elements.PeriapsisRadiusM / 1000.0:F1} km, " +
                                  $"v {startVelocity.Magnitude / 1000.0:F3} -> " +
                                  $"{(approach.Path.VelocityAt(1.0) - uranusVelocityAtEnd).Magnitude / 1000.0:F3} km/s");
            Assert.That(elements.PeriapsisRadiusM, Is.GreaterThan(band.ArrivalRadiusM),
                "the free fall's periapsis is above the floor: the brake spans the rest");
            Assert.That(elements.PeriapsisRadiusM,
                Is.GreaterThan(uranus.RadiusM + HandoverBand.MinimumClearanceAltitudeM),
                "and well above the body");

            // The two new seams are inside the gate, and the brake hands the chain
            // its entry state as before.
            int index = -1;
            for (int i = 0; i < plan.Arcs.Count; i++)
                if (plan.Arcs[i] == approach) index = i;

            Assert.That(index, Is.GreaterThan(0));
            Assert.That(plan.Arcs[index - 1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[index + 1].Phase, Is.EqualTo(FlightPhase.Capture));

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                double step = Vector3d.Distance(
                    plan.Arcs[i].Path.PositionAt(1.0), plan.Arcs[i + 1].Path.PositionAt(0.0));
                double velocityStep = (plan.Arcs[i].Path.VelocityAt(1.0)
                                       + plan.Arcs[i + 1].EntryImpulseVectorMps
                                       - plan.Arcs[i + 1].Path.VelocityAt(0.0)).Magnitude;
                Assert.That(step, Is.LessThan(1_000.0),
                    $"seam {i} ({plan.Arcs[i].Phase}->{plan.Arcs[i + 1].Phase})");
                Assert.That(velocityStep, Is.LessThan(1.0),
                    $"seam {i} ({plan.Arcs[i].Phase}->{plan.Arcs[i + 1].Phase})");
            }

            AssertChainCloses(plan);
        }

        /// <summary>
        /// The fast trips have no planetocentric leg to fly. At the epoch from
        /// Gateway to Mercury at 1 g and 8 days the brake starts 2,378,950 km out —
        /// 20.7 ceilings — and spans the sphere itself, so the coast runs to the
        /// brake's start and the plan keeps its original seven arcs. A conic about
        /// Mercury at 20 spheres would be a worse model than the heliocentric coast
        /// it replaced.
        /// </summary>
        [Test]
        public void TheShortTripsHaveNoApproachBecauseTheBrakeSpansTheSphere()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("mercury", 14.1);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            PrintPlan(plan);

            BodyRuntime mercury = _model.Get("mercury");
            HandoverBand band = HandoverBand.For(mercury, _planner.FixedBurnAccelerationMps2);

            int approaches = 0;
            PlanArc coast = null;
            PlanArc brake = null;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase == FlightPhase.Approach) approaches++;
                if (arc.Phase == FlightPhase.Cruise && arc.IsCoast) coast = arc;
                if (arc.Phase == FlightPhase.Capture && arc.IsFiniteBurn) brake = arc;
            }

            Assert.That(approaches, Is.EqualTo(0), "no planetocentric leg on a fast trip");
            Assert.That(plan.Arcs.Count, Is.EqualTo(8));

            _model.ComputeState(mercury, coast.EndJulianDate, out Vector3d mercuryAtEnd, out _);
            double coastEndRadius = Vector3d.Distance(coast.Path.PositionAt(1.0), mercuryAtEnd);
            TestContext.WriteLine($"coast end {coastEndRadius / 1000.0:F1} km, " +
                                  $"ceiling {band.CeilingRadiusM / 1000.0:F1} km");
            Assert.That(coastEndRadius, Is.GreaterThan(band.CeilingRadiusM),
                "the coast ends outside the ceiling here");
            Assert.That(Vector3d.Distance(brake.Path.PositionAt(0.0),
                coast.Path.PositionAt(1.0)), Is.LessThan(1_000.0),
                "and the coast runs straight to the brake's start");
        }

        [Test]
        public void TheChainIsContinuousEverywhereItIsNotImpulsive()
        {
            TrajectoryPlan plan = PlanTo("mars");

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];

                Assert.That(left.EndJulianDate, Is.EqualTo(right.StartJulianDate));
                double positionStep = Vector3d.Distance(
                    left.Path.PositionAt(1.0), right.Path.PositionAt(0.0));
                double velocityStep = (left.Path.VelocityAt(1.0)
                                       + right.EntryImpulseVectorMps
                                       - right.Path.VelocityAt(0.0)).Magnitude;

                TestContext.WriteLine($"seam {i} {left.Phase}->{right.Phase}: " +
                                      $"{positionStep:E2} m, {velocityStep:E2} m/s");
                Assert.That(positionStep, Is.LessThan(0.1), $"seam {i}");
                // The ceiling seam carries the approach's fixed point and the
                // brake's carries its own: measured worst 3.7e-5 m/s on this
                // trip, four orders inside the gate.
                Assert.That(velocityStep, Is.LessThan(1e-4), $"seam {i}");
            }
        }

        /// <summary>
        /// Every fixed burn in the plan clears the body it is working near by the
        /// named minimum altitude. The rule is body clearance, not the sphere of
        /// influence: a burn may cross the sphere (it integrates the summed
        /// field, and one of the tests in <c>HandoverBandTests</c> flies such an
        /// arc), but it may not cut a body. This build's departure burn starts on
        /// the band's ceiling and climbs, so it also happens to stay outside the
        /// sphere — a consequence of the pinned endpoint, not the gate.
        /// </summary>
        [Test]
        public void EveryFixedBurnClearsTheBodyItWorksNear()
        {
            TrajectoryPlan plan = PlanTo("mars");
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime mars = _model.Get("mars");
            double earthSoi = SphereOfInfluence.RadiusM(earth);

            int burns = 0;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (!arc.IsFiniteBurn) continue;
                burns++;
                BodyRuntime near = arc.Phase == FlightPhase.Capture ? mars : earth;
                bool clears = _planner.FixedBurnClearsBody(arc, near, out double closest);
                double required = near.RadiusM + HandoverBand.MinimumClearanceAltitudeM;
                TestContext.WriteLine($"{arc.Phase} burn: closest {closest / 1000.0:N0} km from " +
                                      $"{near.Id}, clearance {required / 1000.0:N0} km, " +
                                      $"clears {clears}");

                Assert.That(clears, Is.True, $"{arc.Phase} burn clears {near.Id}");
                Assert.That(closest, Is.GreaterThanOrEqualTo(required));

                if (near == earth)
                    Assert.That(closest, Is.GreaterThanOrEqualTo(earthSoi),
                        "this build's departure burn starts on the ceiling and climbs");
            }

            Assert.That(burns, Is.EqualTo(2), "the departure burn and the brake");
        }

        [Test]
        public void AFlightArrivesOnTheParkingOrbitAndHoldsIt()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "mars",
                TripDays * JulianDate.SecondsPerDay);
            PrintPlan(plan);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double positionError = Vector3d.Distance(ship.PositionM,
                plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"arrival error: {positionError:F2} m, " +
                                  $"{velocityError:E2} m/s, {report.Substeps} substeps");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(ship.JulianDate, Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(positionError, Is.LessThan(50_000.0));
            Assert.That(velocityError, Is.LessThan(20.0));

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.ConicRadiusM(mars);
            AssertOrbitAround(mars, ship, radius, 0.02, 0.02);

            double period = ParkingOrbit.PeriodSeconds(mars);
            IntegrationReport coast = _integrator.Advance(ref ship,
                ship.JulianDate + 3.0 * period / JulianDate.SecondsPerDay,
                (state, date) => ThrustCommand.None);

            Assert.That(coast.CollisionDetected, Is.False);
            AssertOrbitAround(mars, ship, radius, 0.03, 0.03);
        }

        /// <summary>
        /// The slow crossing now closes. With the capture entering at the handover
        /// floor, the 1.32-day fall is minutes and the coast absorbs it, so the
        /// 200-day Mars build that used to be refused for needing an acceleration
        /// is a cheap plan: 11.86 km/s (the brake is 2.70 km/s), 0.60 km/s less
        /// than the deep-coast build, because the final 27.3 hours are a
        /// planetocentric approach instead of a Sun-only conic commanding 0.90 km/s
        /// of solar tide. The slow end is still bounded — at 305 days the prograde
        /// short-way coast has no solution (179.4 deg) — and the acceleration
        /// branch remains in the solver; it did not trigger at Mars between 200 and
        /// 300 days after this change.
        /// </summary>
        [Test]
        public void ASlowCrossingClosesCheaplyAndTheSlowEndIsTheCoast()
        {
            TrajectoryPlan plan = PlanTo("mars", 200.0);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FlightTimeDays, Is.EqualTo(200.0).Within(1e-3));
            Assert.That(plan.DeltaVMps / 1000.0, Is.EqualTo(11.21).Within(0.5),
                "measured: the planetocentric approach makes the 200-day crossing cheaper");
            Assert.That(plan.CaptureEntryRadiusM,
                Is.EqualTo(6_032_700.0).Within(1_000.0),
                "and it enters at the Mars 0.3 g floor");
            bool hasApproach = false;
            foreach (PlanArc arc in plan.Arcs)
                if (arc.Phase == FlightPhase.Approach) hasApproach = true;
            Assert.That(hasApproach, Is.True,
                "the slow approach is long enough to have its own arc");

            TrajectoryPlan refused = PlanTo("mars", 305.0);
            TestContext.WriteLine("reason: " + refused.InfeasibleReason);
            Assert.That(refused.IsFeasible, Is.False);
            Assert.That(refused.InfeasibleReason, Does.Contain("short-way"));
            Assert.That(refused.InfeasibleReason, Does.Contain("180"));
        }

        [Test]
        public void ATripTimeWithoutAProgradeShortTransferIsRejectedWithAReason()
        {
            TrajectoryPlan plan = PlanTo("mars", 400.0);

            TestContext.WriteLine("reason: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.False);
            Assert.That(plan.InfeasibleReason, Does.Contain("short-way"));
            Assert.That(plan.InfeasibleReason, Does.Contain("180"));
        }

        [Test]
        public void TheHybridArrivalKeepsTheBrakeAndTheCaptureBurnAtTheLevel()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.FiniteBurn,
                BurnLevelG = 0.3
            };
            TrajectoryPlan plan = planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "mars",
                TripDays * JulianDate.SecondsPerDay);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(10));
            Assert.That(plan.Arcs[plan.Arcs.Count - 1].Phase,
                Is.EqualTo(FlightPhase.Parking));

            int burns = 0;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (!arc.IsFiniteBurn) continue;
                burns++;
                Assert.That(((FiniteBurnArc)arc.Path).AccelerationMps2,
                    Is.EqualTo(plan.FiniteBurnAccelerationMps2).Within(1e-6));
            }

            Assert.That(burns, Is.EqualTo(3),
                "the departure burn, the brake, and the capture burn");

            // The first target-centred coast is the inbound leg; the transfer
            // ellipse later in the chain is also a Coast arc.
            PlanArc inbound = null;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase == FlightPhase.Coast && arc.ReferenceBodyId == "mars")
                {
                    inbound = arc;
                    break;
                }
            }

            Assert.That(inbound, Is.Not.Null);
            Assert.That(inbound.EntryImpulseMps, Is.EqualTo(0.0),
                "the brake hands the capture chain its entry state");
        }

        [Test]
        public void TheBreakEndsAtTheParkingRadiusAndTheEscapeConicReachesTheCeiling()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("mercury", 14.1);
            PrintPlan(plan);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            BodyRuntime earth = _model.Get("earth");
            Vector3d gateway = FlightFixture.ShipAt(_model, "gateway").PositionM;
            _model.ComputeState(earth, plan.DepartureJulianDate,
                out Vector3d earthAtStart, out Vector3d earthVelocityAtStart);
            double parkingRadius = Vector3d.Distance(gateway, earthAtStart);

            PlanArc breakArc = plan.Arcs[0];
            Assert.That(breakArc.Phase, Is.EqualTo(FlightPhase.Departure));
            Vector3d breakLocalPosition = breakArc.Path.PositionAt(1.0)
                                          - BodyState(earth, breakArc.EndJulianDate);
            Vector3d breakLocalVelocity = breakArc.Path.VelocityAt(1.0)
                                          - BodyVelocity(earth, breakArc.EndJulianDate);
            double parabolicSpeed = Math.Sqrt(2.0 * earth.Gm / parkingRadius);
            double vInfinity = plan.DepartureVInfinityMps;
            double periapsisSpeed = Math.Sqrt(parabolicSpeed * parabolicSpeed
                                              + vInfinity * vInfinity);

            TestContext.WriteLine($"break end r {breakLocalPosition.Magnitude / 1000.0:F1} km " +
                                  $"against {parkingRadius / 1000.0:F1} km, speed " +
                                  $"{breakLocalVelocity.Magnitude:F3} against " +
                                  $"{periapsisSpeed:F3} m/s (v∞ {vInfinity / 1000.0:F3} km/s)");

            Assert.That(breakLocalPosition.Magnitude,
                Is.EqualTo(parkingRadius).Within(1.0),
                "the break ends at the parking radius");
            Assert.That(breakLocalVelocity.Magnitude,
                Is.EqualTo(periapsisSpeed).Within(1e-6 * periapsisSpeed),
                "the break ends at the escape speed the chosen split carries");
            Assert.That(Vector3d.Dot(breakLocalVelocity, breakLocalPosition.Normalized),
                Is.EqualTo(0.0).Within(1e-3 * periapsisSpeed),
                "the escape state is on the prograde tangent, not a radial ray");
            Assert.That(breakArc.DurationSeconds,
                Is.LessThan(0.25 * JulianDate.SecondsPerDay),
                "the break is a short burn, not a multi-day hover");

            PlanArc escape = plan.Arcs[1];
            Assert.That(escape.Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(escape.IsCoast, Is.True);
            double clearance = ClearanceSoi * SphereOfInfluence.RadiusM(earth);
            _model.ComputeState(earth, escape.EndJulianDate,
                out Vector3d earthPosition, out Vector3d earthVelocity);
            Vector3d escapeRelativePosition = escape.Path.PositionAt(1.0) - earthPosition;
            Vector3d escapeRelativeVelocity = escape.Path.VelocityAt(1.0) - earthVelocity;
            double escapeSpeed = Math.Sqrt(2.0 * earth.Gm / clearance
                                           + vInfinity * vInfinity);

            TestContext.WriteLine($"escape end {escapeRelativePosition.Magnitude / clearance:F9} " +
                                  $"of the clearance, {escapeRelativeVelocity.Magnitude:F6} " +
                                  $"m/s against escape {escapeSpeed:F6} m/s");

            Assert.That(escapeRelativePosition.Magnitude, Is.EqualTo(clearance).Within(1.0),
                "the escape conic ends on the clearance sphere");
            Assert.That(escapeRelativeVelocity.Magnitude,
                Is.EqualTo(escapeSpeed).Within(1e-6 * escapeSpeed),
                "the escape conic ends at the escape speed the split carries");

            PlanArc burn = plan.Arcs[2];
            Assert.That(burn.IsFiniteBurn, Is.True);
            Assert.That(Vector3d.Distance(escape.Path.PositionAt(1.0), burn.Path.PositionAt(0.0)),
                Is.LessThan(1e-3), "the burn starts where the escape ends");
            Assert.That((escape.Path.VelocityAt(1.0) - burn.Path.VelocityAt(0.0)).Magnitude,
                Is.LessThan(1e-5), "the burn starts at the escape's velocity");
            Assert.That(breakArc.PeakThrustMps2,
                Is.EqualTo(_planner.FixedBurnAccelerationMps2).Within(0.05),
                "the break peaks at the commanded level");
            Assert.That(breakArc.PeakThrustMps2,
                Is.LessThanOrEqualTo(plan.MaxAccelerationMps2 * (1.0 + 1e-6)),
                "the break is inside the drive's limit");
        }

        private Vector3d BodyState(BodyRuntime body, double julianDate)
        {
            _model.ComputeState(body, julianDate, out Vector3d position, out _);
            return position;
        }

        private Vector3d BodyVelocity(BodyRuntime body, double julianDate)
        {
            _model.ComputeState(body, julianDate, out _, out Vector3d velocity);
            return velocity;
        }

        [TestCase(14.1)]
        [TestCase(16.0)]
        [TestCase(20.0)]
        public void TheShortMercuryTripsCloseAtOneG(double days)
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("mercury", days);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FlightTimeDays, Is.EqualTo(days).Within(1e-3));
            Assert.That(plan.PeakThrustMps2,
                Is.LessThanOrEqualTo(plan.MaxAccelerationMps2 * (1.0 + 1e-6)));
            Assert.That(plan.Arcs.Count, Is.EqualTo(8),
                "the fast trips' brakes start outside the ceiling, so there is no approach");
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));

            BodyRuntime earth = _model.Get("earth");
            double clearance = ClearanceSoi * SphereOfInfluence.RadiusM(earth);
            PlanArc escape = plan.Arcs[1];
            _model.ComputeState(earth, escape.EndJulianDate, out Vector3d earthPosition, out _);
            Assert.That(Vector3d.Distance(escape.Path.PositionAt(1.0), earthPosition),
                Is.EqualTo(clearance).Within(1.0),
                "the escape conic ends on the clearance sphere");
        }

        [Test]
        public void AFastMercuryFlightArrivesOnTheParkingOrbit()
        {
            _planner.BurnLevelG = 1.0;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "mercury",
                14.1 * JulianDate.SecondsPerDay);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"14.1 d Mercury arrival error: {positionError:F2} m, " +
                                  $"{velocityError:E3} m/s, {report.Substeps} substeps");

            Assert.That(report.CollisionDetected, Is.False);
            Assert.That(report.StepLimitReached, Is.False);
            Assert.That(ship.JulianDate, Is.EqualTo(plan.ArrivalJulianDate).Within(1e-6));
            Assert.That(positionError, Is.LessThan(10.0));
            Assert.That(velocityError, Is.LessThan(0.1));

            BodyRuntime mercury = _model.Get("mercury");
            AssertOrbitAround(mercury, ship, ParkingOrbit.ConicRadiusM(mercury), 0.02, 0.02);
        }

        [Test]
        public void TheOlderFastAndSlowPlansKeepContinuousSeams()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan mercury = PlanTo("mercury", 14.1);
            _planner.BurnLevelG = 0.3;
            TrajectoryPlan mars = PlanTo("mars", TripDays);

            foreach (TrajectoryPlan plan in new[] { mercury, mars })
            {
                PrintPlan(plan);
                Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
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
                                          $"{positionStep:E2} m, {velocityStep:E2} m/s");
                    // The brake now ends at the handover floor, so the fixed
                    // point's residual and the coast's Lambert correction show up
                    // in these seams: measured worst 55.8 m and 8.6 × 10⁻² m/s at
                    // the short Mercury trip, still well inside the profile's
                    // 1 km / 1 m/s gate. The century-metre and 0.2 m/s bounds are
                    // the regression guard, not a relaxed gate.
                    Assert.That(positionStep, Is.LessThan(100.0), $"seam {i}");
                    Assert.That(velocityStep, Is.LessThan(0.2), $"seam {i}");
                }

                Assert.That(plan.Arcs[plan.Arcs.Count - 1].EndJulianDate,
                    Is.EqualTo(plan.ArrivalJulianDate).Within(1e-12));
            }
        }

        [Test]
        public void ALevelTooLowForTheFastTripIsRejected()
        {
            _planner.BurnLevelG = 0.01;
            TrajectoryPlan plan = PlanTo("mercury", 8.0);

            TestContext.WriteLine("reason: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.False);
            Assert.That(plan.InfeasibleReason, Does.Contain("burn-coast-burn"));
        }

        [Test]
        public void TheAdvertisedMinimumIsTheTrueMercuryFloor()
        {
            _planner.BurnLevelG = 1.0;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.Plan(ship, "mercury");

            TestContext.WriteLine($"mercury floor: {fastest}, " +
                                  $"peak {fastest.PeakThrustMps2:F3} m/s²");
            Assert.That(fastest.IsFeasible, Is.True, fastest.InfeasibleReason);
            Assert.That(fastest.FlightTimeDays, Is.EqualTo(3.67616).Within(0.01),
                "measured: the Mercury 1 g floor with the split-aware climb");
            Assert.That(fastest.PeakThrustMps2,
                Is.LessThanOrEqualTo(fastest.MaxAccelerationMps2 * (1.0 + 1e-6)));
            AssertChainCloses(fastest);

            // A genuinely sub-floor request is still refused: the climb, the two
            // burns and the coast cannot fit. (The old 8-day anchor is now a
            // feasible plan, because the split's short climb frees the coast.)
            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mercury",
                2.0 * JulianDate.SecondsPerDay);
            TestContext.WriteLine($"2 d: {tooFast}, reason: {tooFast.InfeasibleReason}");

            Assert.That(tooFast.IsFeasible, Is.False);
            Assert.That(tooFast.InfeasibleReason, Is.Not.Empty,
                "a rejection carries a cause");
        }

        /// <summary>
        /// The reported plan's burns are full-field burns, not the two-body
        /// search model: every fixed burn integrates the summed catalogue field
        /// and its measured acceleration is the field plus the thrust. Rebuilding
        /// the same trip through a field that holds only the Sun moves the plan,
        /// which is what a burn that quietly used one body could not do.
        /// </summary>
        [Test]
        public void EveryBurnInAReportedPlanIntegratesTheFullField()
        {
            TrajectoryPlan plan = PlanTo("mars");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var sunOnly = FlightFixture.SunOnlyField(_model);
            int burns = 0;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (!(arc.Path is FiniteBurnArc burn)) continue;
                burns++;
                Assert.That(burn.UsesFullField, Is.True, $"{arc.Phase} burn");
                Assert.That(burn.Gravity, Is.SameAs(_gravity), $"{arc.Phase} field");

                // The arc's own acceleration is the summed field at its state
                // plus the commanded thrust.
                const double u = 0.5;
                Vector3d position = arc.Path.PositionAt(u);
                double julianDate = arc.StartJulianDate
                                    + arc.Path.SecondsAt(u) / JulianDate.SecondsPerDay;
                _gravity.Prepare(position, julianDate);
                Vector3d fieldAcceleration = _gravity.AccelerationAt(position, julianDate);
                Vector3d thrust = arc.Path.AccelerationAt(u) - fieldAcceleration;
                Assert.That(thrust.Magnitude,
                    Is.EqualTo(plan.FiniteBurnAccelerationMps2).Within(1e-6),
                    $"{arc.Phase} thrust");

                sunOnly.Prepare(position, julianDate);
                Vector3d sunAcceleration = sunOnly.AccelerationAt(position, julianDate);
                double nonSun = (fieldAcceleration - sunAcceleration).Magnitude;
                TestContext.WriteLine($"{arc.Phase} burn: |thrust| {thrust.Magnitude:F4}, " +
                                      $"non-Sun term {nonSun:E3} m/s²");
                Assert.That(nonSun, Is.GreaterThan(1e-6),
                    $"{arc.Phase} burn is not a one-body burn");
            }

            Assert.That(burns, Is.EqualTo(2), "the departure burn and the brake");

            // A different field is a different plan.
            var sunPlanner = new TripPlanner(_model, sunOnly)
            {
                ArrivalStyle = CaptureStyle.Conic,
                BurnLevelG = _planner.BurnLevelG
            };
            TrajectoryPlan weak = sunPlanner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "mars",
                TripDays * JulianDate.SecondsPerDay);
            Assert.That(weak.IsFeasible, Is.True, weak.InfeasibleReason);
            TestContext.WriteLine($"full {plan.DeltaVMps / 1000.0:F1} km/s against " +
                                  $"the Sun alone {weak.DeltaVMps / 1000.0:F1} km/s");
            Assert.That(Math.Abs(weak.DeltaVMps - plan.DeltaVMps),
                Is.GreaterThan(0.01 * plan.DeltaVMps),
                "the plan is built in the field it reports");
        }

        /// <summary>
        /// The hybrid arrival's injection burn is deep in the target's well, where
        /// the summed field is the target plus a small tide. It is still a burn in
        /// a full-field plan, so it is integrated full-field too — the capture
        /// solver and the arc that is reported use the same field.
        /// </summary>
        [Test]
        public void TheHybridCaptureInjectionBurnAlsoIntegratesTheFullField()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.FiniteBurn,
                BurnLevelG = 0.3
            };
            TrajectoryPlan plan = planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "mars",
                TripDays * JulianDate.SecondsPerDay);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            int burns = 0;
            double captureEndRadius = 0.0;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (!(arc.Path is FiniteBurnArc burn)) continue;
                burns++;
                Assert.That(burn.UsesFullField, Is.True, $"{arc.Phase} burn");
                if (burn.Centre == _model.Get("mars"))
                {
                    captureEndRadius = burn.EndLocalPositionM.Magnitude;
                    TestContext.WriteLine($"capture burn: {burn.DurationSeconds:F1} s, " +
                                          $"injection {captureEndRadius / 1000.0:N0} km");
                }
            }

            Assert.That(burns, Is.EqualTo(3),
                "the departure burn, the brake, and the capture burn");
            Assert.That(captureEndRadius,
                Is.LessThan(ParkingOrbit.ConicRadiusM(_model.Get("mars"))),
                "the full-field capture burn ends below the parking radius");
            Assert.That(captureEndRadius,
                Is.GreaterThan(_model.Get("mars").RadiusM
                               + HybridCapture.MinimumClearanceAltitudeM),
                "the full-field capture burn still ends at a safe injection radius");
        }

        /// <summary>
        /// The fixed-duration plans are the profile's reference answers with the
        /// physical departure and the split-aware climb: the short break, the
        /// escape conic at the chosen excess speed, the departure burn, the coast,
        /// the approach, the brake, and the capture, measured at the epoch from
        /// Gateway with the conic capture. The step-2 anchors are re-baselined
        /// again by the split, which shortens every trip and lets the old sub-floor
        /// times build (<see cref="TheOldSubFloorDurationsAreNowBuildable"/>).
        /// </summary>
        [TestCase("mars", 0.3, 90.0, 38.5411)]
        [TestCase("uranus", 1.0, 400.0, 178.7042)]
        [TestCase("mars", 0.3, 20.0, 244.9177)]
        [TestCase("mars", 0.3, 18.0, 276.0994)]
        [TestCase("mercury", 1.0, 16.0, 282.7939)]
        [TestCase("uranus", 1.0, 60.0, 1110.4293)]
        public void TheFixedDurationDeltaVAnchorsAreReMeasured(string targetId,
            double level, double days, double expectedKmPerSecond)
        {
            _planner.BurnLevelG = level;
            TrajectoryPlan plan = PlanTo(targetId, days);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            TestContext.WriteLine($"{targetId} {days} d at {level} g: " +
                                  $"{plan.DeltaVMps / 1000.0:F4} km/s " +
                                  $"(anchor {expectedKmPerSecond:F4})");
            Assert.That(Math.Round(plan.DeltaVMps / 1000.0, 4),
                Is.EqualTo(expectedKmPerSecond),
                "the fixed-duration delta-v anchor is the measured value");
        }

        /// <summary>
        /// The three times that the physical departure pushed below its floor are
        /// **buildable again** with the split: a faster climb frees the coast, so
        /// Mercury 1 g 8 d, Mars 0.3 g 10 d and Uranus 1 g 35 d are real plans,
        /// not refusals. The step-1 / step-2 refusals are recorded as the measured
        /// anchors they became.
        /// </summary>
        [TestCase("mercury", 1.0, 8.0, 526.7435)]
        [TestCase("mars", 0.3, 10.0, 567.7518)]
        [TestCase("uranus", 1.0, 35.0, 1951.8556)]
        public void TheOldSubFloorDurationsAreNowBuildable(string targetId,
            double level, double days, double expectedKmPerSecond)
        {
            _planner.BurnLevelG = level;
            TrajectoryPlan plan = PlanTo(targetId, days);

            TestContext.WriteLine($"{targetId} {days} d at {level} g: feasible " +
                                  $"{plan.IsFeasible}, {plan.DeltaVMps / 1000.0:F4} km/s");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FlightTimeDays, Is.EqualTo(days).Within(1e-3));
            Assert.That(plan.DeltaVMps / 1000.0,
                Is.EqualTo(expectedKmPerSecond).Within(0.005 * expectedKmPerSecond),
                "the rebuilt anchor is the measured value");
            AssertChainCloses(plan);
        }

        /// <summary>
        /// The departure solve integrates its trial burns at 128 samples while the
        /// reported burn stays at 512, so the coast is re-solved from the reported
        /// burn's own end. Uranus at 35 days and 1 g is the measured fast,
        /// long-burn case: the burn-to-coast seam is 2.0 × 10⁻⁴ m and 3.4 × 10⁻⁶
        /// m/s, and every seam in the chain stays inside the 1 km / 1 m/s gate.
        /// </summary>
        [Test]
        public void TheDepartureBurnHandsACoastSolvedFromItsOwnEnd()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("uranus", 60.0);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            PlanArc burn = plan.Arcs[2];
            PlanArc coast = plan.Arcs[3];
            Assert.That(burn.IsFiniteBurn, Is.True, "the departure burn follows the climb");
            Assert.That(coast.Path, Is.InstanceOf<ConicArc>());

            double positionStep = Vector3d.Distance(
                burn.Path.PositionAt(1.0), coast.Path.PositionAt(0.0));
            double velocityStep = (burn.Path.VelocityAt(1.0)
                                   + coast.EntryImpulseVectorMps
                                   - coast.Path.VelocityAt(0.0)).Magnitude;
            TestContext.WriteLine($"departure burn -> coast: {positionStep:E3} m, " +
                                  $"{velocityStep:E3} m/s");
            Assert.That(positionStep, Is.LessThan(10.0),
                "the coast starts where the reported burn ends");
            Assert.That(velocityStep, Is.LessThan(1e-2));

            AssertChainCloses(plan);
        }

        /// <summary>The break search's fast model is the departure body alone;
        /// the plan reports the full field. The two peaks must stay close enough
        /// that the search's duration decision does not move the plan, and the
        /// near-limit case is the one that runs through the 5% screen. The new
        /// break is the short commanded-level burn, so its peak is a level, not a
        /// trip-dependent residue.</summary>
        [TestCase("mercury", 1.0, 14.1, 9.7000, 18.6418, 314.9973)]
        [TestCase("mercury", 1.0, 16.0, 9.7000, 14.6597, 282.7939)]
        [TestCase("mars", 0.3, 90.0, 2.9467, 1.9823, 38.5411)]
        [TestCase("mars", 0.3, 20.0, 2.9467, 14.6578, 244.9177)]
        public void TheShortBreakPeakTracksTheFullFieldPlan(string targetId,
            double level, double days, double expectedBreakPeak,
            double expectedBreakDeltaVKm, double expectedTotalDeltaVKm)
        {
            _planner.BurnLevelG = level;
            TrajectoryPlan plan = PlanTo(targetId, days);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            _planner.MeasureBreakPeak(plan,
                out double fast, out double full, out double screened);
            TestContext.WriteLine($"{targetId} {days} d: fast {fast:F6}, " +
                                  $"full {full:F6}, screened {screened:F6} m/s²");
            PrintPlan(plan);

            // The departure body dominates; the Sun and the other bodies are
            // the omitted term. Ten percent, or 0.02 m/s² on the very gentle
            // 90-day Mars break, bounds it. Near the drive limit the screen
            // does not use this number at all.
            Assert.That(Math.Abs(fast - full),
                Is.LessThanOrEqualTo(Math.Max(0.02, 0.1 * full)),
                "the fast break peak tracks the full field");

            // The search input is unchanged; the plan the caller receives moved
            // when the capture started entering at the handover floor (the fall
            // is minutes rather than hours, so the trip re-times). Measured.
            PlanArc breakArc = plan.Arcs[0];
            Assert.That(breakArc.PeakThrustMps2,
                Is.EqualTo(expectedBreakPeak).Within(
                    Math.Max(1e-4, 0.005 * expectedBreakPeak)),
                "the reported break peak is unchanged");
            Assert.That(breakArc.DeltaVMps / 1000.0,
                Is.EqualTo(expectedBreakDeltaVKm).Within(
                    0.005 * expectedBreakDeltaVKm),
                "the reported break delta-v is unchanged");
            Assert.That(plan.DeltaVMps / 1000.0,
                Is.EqualTo(expectedTotalDeltaVKm).Within(
                    0.005 * expectedTotalDeltaVKm),
                "the reported total delta-v is unchanged");
        }

        [Test]
        public void TheFastBreakPeakTracksTheFullFieldAtTheMercuryFloor()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = _planner.Plan(
                FlightFixture.ShipAt(_model, "gateway"), "mercury");

            _planner.MeasureBreakPeak(plan,
                out double fast, out double full, out double screened);
            TestContext.WriteLine($"mercury floor: fast {fast:F6}, full {full:F6}, " +
                                  $"screened {screened:F6} m/s²");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(Math.Abs(fast - full),
                Is.LessThanOrEqualTo(Math.Max(0.02, 0.1 * full)),
                "the fast break peak tracks the full field at the floor");
            Assert.That(plan.PeakThrustMps2, Is.EqualTo(9.700).Within(0.005),
                "the floor peak is the drive limit");
            // The floor is found to the probe model's 0.2% retry resolution, so
            // the reported delta-v tracks the returned duration to that order;
            // it is not the plan's identity. The peaks and the anchors above are
            // the exact ones.
            Assert.That(plan.DeltaVMps / 1000.0, Is.EqualTo(1649.3504).Within(0.01 * 1649.3504),
                "the floor total delta-v with the split-aware climb");

            // The floor sits on the drive limit, so its break goes through the
            // full-field branch of the screen.
            Assert.That(fast,
                Is.GreaterThan(_planner.MaxAccelerationMps2 * _planner.ThrustMargin
                               * (1.0 - TripPlanner.BreakScreenMargin)),
                "the near-floor break is inside the screen band");
        }

        /// <summary>The 5% screen: when the fast peak is inside the band the
        /// break search certifies with the full field, so no fast estimate can
        /// talk an over-thrust break past the drive.</summary>
        [Test]
        public void TheFivePercentScreenCertifiesNearLimitBreaksWithTheFullField()
        {
            _planner.BurnLevelG = 1.0;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            double limit = _planner.MaxAccelerationMps2 * _planner.ThrustMargin;

            // 14.1 d sits just above the floor, 14.5 and 16 d are gentler trips,
            // and 2.0 d is the rejected just-under case below the split-aware
            // floor (the old 13-day case became buildable when the split landed).
            foreach (double days in new[] { 14.1, 14.5, 16.0 })
            {
                TrajectoryPlan plan = _planner.PlanAtDuration(ship, "mercury",
                    days * JulianDate.SecondsPerDay);
                _planner.MeasureBreakPeak(plan,
                    out double fast, out double full, out double screened);
                double cheapFull = _planner.MeasureBreakPeakFullField(plan);
                TestContext.WriteLine($"{days} d: fast {fast:F4}, full {full:F4}, " +
                                      $"cheap full {cheapFull:F4}, screened {screened:F4}, " +
                                      $"limit {limit:F4}, feasible {plan.IsFeasible}");

                Assert.That(cheapFull, Is.EqualTo(full).Within(1e-3),
                    "the screen's one-prepare field matches the plan's measurement");

                if (fast > limit * (1.0 - TripPlanner.BreakScreenMargin))
                {
                    Assert.That(screened, Is.EqualTo(full).Within(1e-12),
                        "a near-limit fast peak is certified by the full field");
                }

                if (plan.IsFeasible)
                {
                    Assert.That(full, Is.LessThanOrEqualTo(limit * (1.0 + 1e-6)),
                        "the screen never accepts a full-field over-thrust break");
                }
            }

            TrajectoryPlan refused = _planner.PlanAtDuration(ship, "mercury",
                2.0 * JulianDate.SecondsPerDay);
            Assert.That(refused.IsFeasible, Is.False,
                "a trip below the split-aware floor still rejects");
        }

        /// <summary>The floor search may probe with the cheap model, but the
        /// plan it returns is built and measured with the full field, and no
        /// fast estimate is allowed to stand in at the drive limit.</summary>
        [Test]
        public void TheFloorSearchReturnsAFullFieldPlanThatFitsTheDrive()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan floor = _planner.Plan(
                FlightFixture.ShipAt(_model, "gateway"), "mercury");

            Assert.That(floor.IsFeasible, Is.True, floor.InfeasibleReason);
            Assert.That(floor.Arcs.Count, Is.GreaterThan(1),
                "the floor is a real chain, not a rejected placeholder");
            Assert.That(floor.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(floor.PeakThrustMps2,
                Is.LessThanOrEqualTo(floor.MaxAccelerationMps2 * (1.0 + 1e-6)),
                "the returned plan is inside the drive");

            _planner.MeasureBreakPeak(floor,
                out _, out double fullBreakPeak, out _);
            Assert.That(fullBreakPeak,
                Is.EqualTo(floor.Arcs[0].PeakThrustMps2).Within(1e-9),
                "the reported break is the full-field measurement");
            Assert.That(fullBreakPeak,
                Is.LessThanOrEqualTo(_planner.MaxAccelerationMps2
                                     * _planner.ThrustMargin * (1.0 + 1e-6)),
                "the full field certifies the break at the floor");
        }

        /// <summary>
        /// The floor search's probes are search inputs, never answers. A plan
        /// that leaves <see cref="TripPlanner.Plan"/> is certified: every arc's
        /// clearance and delta-v were measured in the summed field, and every
        /// seam closes inside the 1 km / 1 m/s gate. The probe quality the search
        /// uses reports an infinite clearance and no measured delta-v on a
        /// coasted arc, so the two are distinguishable. Mars at 0.3 g is the case
        /// where the certificate refuses the fast answer and the stalled pin is
        /// retried; Uranus is an unchanged low-cost case, and Jupiter's editor
        /// floor takes the same retry.
        /// </summary>
        [TestCase("uranus", 0.3)]
        [TestCase("mars", 0.3)]
        [TestCase("jupiter", 0.3)]
        public void TheFloorSearchReturnsACertifiedPlanNotAProbe(string targetId,
            double level)
        {
            _planner.BurnLevelG = level;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan floor = _planner.Plan(ship, targetId);
            Assert.That(floor.IsFeasible, Is.True, floor.InfeasibleReason);

            foreach (PlanArc arc in floor.Arcs)
            {
                Assert.That(double.IsFinite(arc.MinimumClearanceM), Is.True,
                    $"{arc.Phase} clearance is measured in the full field");
                Assert.That(arc.DeltaVMps, Is.GreaterThan(0.0),
                    $"{arc.Phase} delta-v is measured in the full field");
            }

            AssertChainCloses(floor);

            // A trip half again as long is a legal probe: it is feasible, and
            // its coasts carry no clearance and no measured delta-v.
            TrajectoryPlan probe = _planner.BuildProbeAtDuration(ship, targetId,
                floor.FlightTimeSeconds * 1.5);
            Assert.That(probe.IsFeasible, Is.True, probe.InfeasibleReason);
            Assert.That(probe.Arcs.Count,
                Is.InRange(floor.Arcs.Count, floor.Arcs.Count + 1),
                "a longer trip may add the planetocentric approach");
            foreach (PlanArc arc in probe.Arcs)
            {
                Assert.That(double.IsPositiveInfinity(arc.MinimumClearanceM), Is.True,
                    $"{arc.Phase} probe does not measure clearance");
                if (!arc.IsFiniteBurn)
                    Assert.That(arc.DeltaVMps, Is.EqualTo(0.0),
                        $"{arc.Phase} probe does not measure a coast's delta-v");
            }
        }

        [Test]
        public void TheAdvertisedMinimumIsTheTrueMarsFloor()
        {
            _planner.BurnLevelG = 0.3;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            var probes = new List<string>();
            _planner.Trace = probes.Add;
            TrajectoryPlan fastest = _planner.Plan(ship, "mars");

            // The certified floor is built and measured with the full field; the
            // trace records whether the near-floor fixed point needed its retry.
            int retries = 0;
            foreach (string probe in probes)
            {
                if (probe.StartsWith("pin retry")) retries++;
            }
            TestContext.WriteLine($"mars floor: {fastest}, " +
                                  $"peak {fastest.PeakThrustMps2:F3} m/s², " +
                                  $"pin retries {retries}");
            PrintPlan(fastest);
            for (int i = 0; i < fastest.Arcs.Count - 1; i++)
            {
                PlanArc left = fastest.Arcs[i];
                PlanArc right = fastest.Arcs[i + 1];
                TestContext.WriteLine($"floor seam {i}: " +
                    $"{Vector3d.Distance(left.Path.PositionAt(1.0), right.Path.PositionAt(0.0)):E2} m, " +
                    $"{(left.Path.VelocityAt(1.0) + right.EntryImpulseVectorMps - right.Path.VelocityAt(0.0)).Magnitude:E2} m/s");
            }
            Assert.That(fastest.IsFeasible, Is.True, fastest.InfeasibleReason);
            Assert.That(fastest.FlightTimeDays, Is.EqualTo(7.16751).Within(0.01),
                "measured: the Mars 0.3 g floor with the split-aware climb");
            Assert.That(fastest.PeakThrustMps2,
                Is.LessThanOrEqualTo(fastest.MaxAccelerationMps2 * (1.0 + 1e-6)));
            AssertChainCloses(fastest);

            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mars",
                5.0 * JulianDate.SecondsPerDay);
            TestContext.WriteLine($"just under: {tooFast}, reason: {tooFast.InfeasibleReason}");

            Assert.That(tooFast.IsFeasible, Is.False,
                "5.0 d is below the coast floor of 7.17 d");
            Assert.That(tooFast.InfeasibleReason, Is.Not.Empty);
        }

        /// <summary>
        /// The break commands the level the fixed burns command, by construction:
        /// its quintic is solved for the commanded acceleration and no longer keeps
        /// a reference coast's departure share. The reported peak is the full-field
        /// measurement of that same curve, so the departure body's field plus the
        /// Sun's small term lands within a few percent.
        /// </summary>
        [TestCase("mars", 0.3, 30.0)]
        [TestCase("mercury", 1.0, 14.1)]
        public void TheBreakPeaksAtTheCommandLevel(string targetId, double level,
            double days)
        {
            _planner.BurnLevelG = level;
            TrajectoryPlan plan = PlanTo(targetId, days);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            PlanArc breakArc = plan.Arcs[0];
            Assert.That(breakArc.Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(breakArc.Path, Is.InstanceOf<Quintic>());

            double target = _planner.FixedBurnAccelerationMps2;
            double residual = breakArc.PeakThrustMps2 - target;
            TestContext.WriteLine($"{targetId} {days} d at {level} g: break " +
                                  $"{breakArc.DurationSeconds / JulianDate.SecondsPerDay:F5} d, " +
                                  $"peak {breakArc.PeakThrustMps2:F4} against target {target:F4} " +
                                  $"m/s², residual {residual:+0.0000;-0.0000} " +
                                  $"({100.0 * residual / target:+0.00;-0.00}%)");

            Assert.That(Math.Abs(residual), Is.LessThanOrEqualTo(0.05 * target),
                "the reported break peaks at the commanded level to within 5%");
        }

        /// <summary>
        /// The break is its own commanded-level solve, not a share of the trip.
        /// With the split its duration also follows the chosen excess speed — a
        /// faster climb spends longer at the level — but it never moves with the
        /// *trip*: at 30, 40 and 50 days the break is minutes to hours, the peak
        /// is the commanded level, and the available-time cap is not binding.
        /// </summary>
        [Test]
        public void TheBreakStaysShortAcrossTripTimesAndIsNotTheTripShare()
        {
            _planner.BurnLevelG = 0.3;
            double[] days = { 30.0, 40.0, 50.0 };
            var durations = new double[days.Length];
            var available = new double[days.Length];

            for (int i = 0; i < days.Length; i++)
            {
                TrajectoryPlan plan = PlanTo("mars", days[i]);
                Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
                durations[i] = plan.Arcs[0].DurationSeconds;
                available[i] = BreakAvailableSeconds(plan);

                TestContext.WriteLine($"{days[i]} d: break " +
                                      $"{durations[i] / JulianDate.SecondsPerDay:F6} d, " +
                                      $"escape {plan.Arcs[1].DurationSeconds / JulianDate.SecondsPerDay:F4} d, " +
                                      $"peak {plan.Arcs[0].PeakThrustMps2:F4} m/s², " +
                                      $"dv {plan.Arcs[0].DeltaVMps / 1000.0:F3} km/s, " +
                                      $"total {plan.DeltaVMps / 1000.0:F2} km/s, " +
                                      $"v∞ {plan.DepartureVInfinityMps / 1000.0:F3} km/s, " +
                                      $"available {available[i] / JulianDate.SecondsPerDay:F3} d");
                Assert.That(durations[i], Is.LessThan(0.25 * JulianDate.SecondsPerDay),
                    $"{days[i]} d: the break is a short burn, not a trip share");
                Assert.That(durations[i], Is.LessThan(0.9 * available[i]),
                    $"{days[i]} d: the available-time cap is not binding");
                Assert.That(plan.Arcs[0].PeakThrustMps2,
                    Is.EqualTo(_planner.FixedBurnAccelerationMps2).Within(0.01),
                    $"{days[i]} d: the break peaks at the commanded level");
            }

            // The break follows the split: the 30-day plan's faster climb spends
            // longer at the level than the gentler 40-day climb.
            Assert.That(durations[0], Is.GreaterThan(durations[1]),
                "the faster climb at 30 d spends longer at the level");
        }

        [TestCase("mars", 0.3, 30.0)]
        [TestCase("mercury", 1.0, 14.1)]
        public void TheBreakChainStillCloses(string targetId, double level, double days)
        {
            _planner.BurnLevelG = level;
            TrajectoryPlan plan = PlanTo(targetId, days);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            AssertChainCloses(plan);
        }

        /// <summary>
        /// The physical departure's measured trade at the epoch from Gateway: the
        /// floor, the trip-time window, and the arc breakdown at Mercury, Mars,
        /// Jupiter and Uranus at 1 g and 0.3 g. The break is the commanded-level
        /// short burn on every row; the escape conic is the climb, and the coast is
        /// what is left. This is the table the caretaker reviews.
        /// </summary>
        [Test]
        public void TheDepartureSweepIsRecorded()
        {
            var cases = new[]
            {
                ("mercury", 1.0), ("mercury", 0.3),
                ("mars", 1.0), ("mars", 0.3),
                ("jupiter", 1.0), ("jupiter", 0.3),
                ("uranus", 1.0), ("uranus", 0.3)
            };

            TestContext.WriteLine("target  level  floor d   window d      break d  " +
                                  "break peak  break dv   burn d  burn dv  coast d  " +
                                  "approach d  brake d  brake dv  capture dv  total dv  worst seam");
            foreach ((string targetId, double level) in cases)
            {
                _planner.BurnLevelG = level;
                ShipState ship = FlightFixture.ShipAt(_model, "gateway");
                TrajectoryPlan plan = _planner.Plan(ship, targetId);
                if (!plan.IsFeasible)
                {
                    TestContext.WriteLine($"{targetId,-7} {level,5:F1}  refused: " +
                                          plan.InfeasibleReason);
                    Assert.That(plan.IsFeasible, Is.True,
                        $"{targetId} {level} g: {plan.InfeasibleReason}");
                }

                double floorDays = plan.FlightTimeDays;
                double maximumDays = MaximumWindowDays(ship, targetId, floorDays);

                PlanArc breakArc = plan.Arcs[0];
                double burnSeconds = 0.0;
                double burnDeltaV = 0.0;
                double coastSeconds = 0.0;
                double approachSeconds = 0.0;
                double brakeSeconds = 0.0;
                double brakeDeltaV = 0.0;
                foreach (PlanArc arc in plan.Arcs)
                {
                    if (arc.Phase == FlightPhase.Cruise && arc.IsFiniteBurn)
                    {
                        burnSeconds = arc.DurationSeconds;
                        burnDeltaV = arc.DeltaVMps;
                    }
                    else if (arc.Phase == FlightPhase.Cruise && arc.IsCoast)
                    {
                        coastSeconds = arc.DurationSeconds;
                    }
                    else if (arc.Phase == FlightPhase.Approach)
                    {
                        approachSeconds = arc.DurationSeconds;
                    }
                    else if (arc.Phase == FlightPhase.Capture && arc.IsFiniteBurn)
                    {
                        brakeSeconds = arc.DurationSeconds;
                        brakeDeltaV = arc.DeltaVMps;
                    }
                }

                double worstPosition = 0.0;
                double worstVelocity = 0.0;
                for (int i = 0; i < plan.Arcs.Count - 1; i++)
                {
                    PlanArc left = plan.Arcs[i];
                    PlanArc right = plan.Arcs[i + 1];
                    worstPosition = Math.Max(worstPosition, Vector3d.Distance(
                        left.Path.PositionAt(1.0), right.Path.PositionAt(0.0)));
                    worstVelocity = Math.Max(worstVelocity, (left.Path.VelocityAt(1.0)
                        + right.EntryImpulseVectorMps - right.Path.VelocityAt(0.0)).Magnitude);
                }

                TestContext.WriteLine(
                    $"{targetId,-7} {level,5:F1}  {floorDays,7:F3}  " +
                    $"{floorDays,7:F3}-{maximumDays,-7:F3}  " +
                    $"{breakArc.DurationSeconds / JulianDate.SecondsPerDay,7:F4}  " +
                    $"{breakArc.PeakThrustMps2,10:F4}  " +
                    $"{breakArc.DeltaVMps / 1000.0,8:F3}  " +
                    $"{burnSeconds / JulianDate.SecondsPerDay,6:F4}  " +
                    $"{burnDeltaV / 1000.0,7:F2}  " +
                    $"{coastSeconds / JulianDate.SecondsPerDay,7:F3}  " +
                    $"{approachSeconds / JulianDate.SecondsPerDay,9:F4}  " +
                    $"{brakeSeconds / 60.0,7:F1}  " +
                    $"{brakeDeltaV / 1000.0,8:F2}  " +
                    $"{plan.ImpulseDeltaVMps / 1000.0,9:F2}  " +
                    $"{plan.DeltaVMps / 1000.0,8:F1}  " +
                    $"{worstPosition,8:F1} m / {worstVelocity:E2} m/s");
            }
        }

        /// <summary>
        /// Step 2 research spike: a **zero-coast** crossing built from the physical
        /// departure (short break + escape conic), a departure burn, and a brake
        /// that meet at the flip with no coast. The total crossing time is an
        /// output — the sum of the solved burn and brake durations plus the climb
        /// and capture — so <see cref="TripPlanner.PlanBurnOnly"/> takes a fixed
        /// date for the capture chain, not a trip time, and never calls
        /// <see cref="TripPlanner.PlanAtDuration"/>. Measured at the epoch from
        /// Gateway to Mercury at 1 g on the date of the certified burn-only
        /// minimum: the seam closes inside the gate, and the crossing is shorter
        /// than the coast floor at a much higher delta-v.
        /// </summary>
        [Test]
        public void ABurnOnlyCrossingClosesWithoutACoast()
        {
            _planner.BurnLevelG = 1.0;

            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            const double floorDays = 14.06070;
            double arrivalJd = ship.JulianDate + 13.86287;

            var trace = new List<string>();
            _planner.Trace = trace.Add;
            TrajectoryPlan plan = _planner.PlanBurnOnly(ship, "mercury", arrivalJd);
            foreach (string line in trace) TestContext.WriteLine("trace: " + line);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            // The crossing is break → escape → departure burn → brake → capture,
            // with no coast between the burns at all.
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[0].Path, Is.InstanceOf<Quintic>());
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(plan.Arcs[1].IsCoast, Is.True);
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[3].IsFiniteBurn, Is.True);
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[5].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[6].Phase, Is.EqualTo(FlightPhase.Parking));

            foreach (PlanArc arc in plan.Arcs)
            {
                Assert.That(arc.Phase == FlightPhase.Cruise && arc.IsCoast, Is.False,
                    "the burn-only crossing leaves no cruise coast");
            }

            // Dates and states both run continuously, so the seam is one instant
            // as well as one state.
            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                Assert.That(plan.Arcs[i].EndJulianDate,
                    Is.EqualTo(plan.Arcs[i + 1].StartJulianDate).Within(1e-12),
                    $"seam {i} date");
            }

            AssertChainCloses(plan);

            // The break is the commanded-level short burn, and both fixed burns
            // command the same level.
            double level = _planner.FixedBurnAccelerationMps2;
            Assert.That(plan.Arcs[0].PeakThrustMps2, Is.EqualTo(level).Within(0.05),
                "the break peaks at the commanded acceleration");
            Assert.That(((FiniteBurnArc)plan.Arcs[2].Path).AccelerationMps2,
                Is.EqualTo(level).Within(1e-6));
            Assert.That(((FiniteBurnArc)plan.Arcs[3].Path).AccelerationMps2,
                Is.EqualTo(level).Within(1e-6));

            // This is the fast case: the brake starts far outside the ceiling, so
            // no planetocentric approach leg is needed.
            BodyRuntime mercury = _model.Get("mercury");
            _model.ComputeState(mercury, plan.Arcs[3].StartJulianDate,
                out Vector3d mercuryAtBrakeStart, out _);
            double brakeStartRadius = Vector3d.Distance(plan.Arcs[3].Path.PositionAt(0.0),
                mercuryAtBrakeStart);
            HandoverBand band = HandoverBand.For(mercury, level);

            double captureSeconds = 0.0;
            for (int i = 4; i < plan.Arcs.Count; i++)
                captureSeconds += plan.Arcs[i].DurationSeconds;

            double worstPosition = 0.0;
            double worstVelocity = 0.0;
            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                PlanArc left = plan.Arcs[i];
                PlanArc right = plan.Arcs[i + 1];
                worstPosition = Math.Max(worstPosition, Vector3d.Distance(
                    left.Path.PositionAt(1.0), right.Path.PositionAt(0.0)));
                worstVelocity = Math.Max(worstVelocity, (left.Path.VelocityAt(1.0)
                    + right.EntryImpulseVectorMps - right.Path.VelocityAt(0.0)).Magnitude);
            }

            double totalDays = plan.FlightTimeDays;
            double burnDeltaV = plan.Arcs[2].DeltaVMps / 1000.0;
            double brakeDeltaV = plan.Arcs[3].DeltaVMps / 1000.0;

            TestContext.WriteLine(
                $"break {plan.Arcs[0].DurationSeconds / JulianDate.SecondsPerDay:F5} d, " +
                $"escape {plan.Arcs[1].DurationSeconds / JulianDate.SecondsPerDay:F5} d, " +
                $"burn {plan.Arcs[2].DurationSeconds / JulianDate.SecondsPerDay:F5} d, " +
                $"brake {plan.Arcs[3].DurationSeconds / JulianDate.SecondsPerDay:F5} d, " +
                $"capture {captureSeconds / JulianDate.SecondsPerDay:F5} d");
            TestContext.WriteLine(
                $"burn {burnDeltaV:F1} km/s, brake {brakeDeltaV:F1} km/s, " +
                $"total {plan.DeltaVMps / 1000.0:F1} km/s; " +
                $"brake starts {brakeStartRadius / 1000.0:N0} km out " +
                $"against the {band.CeilingRadiusM / 1000.0:N0} km ceiling");
            TestContext.WriteLine($"worst seam {worstPosition:F1} m / {worstVelocity:E2} m/s");
            TestContext.WriteLine(
                $"total {totalDays:F5} d against the {floorDays:F5} d coast floor: " +
                $"{(totalDays < floorDays ? "shorter" : "not shorter")} by " +
                $"{Math.Abs(floorDays - totalDays):F5} d");

            Assert.That(brakeStartRadius, Is.GreaterThan(band.CeilingRadiusM),
                "the fast crossing's brake starts outside the handover ceiling");
            Assert.That(totalDays, Is.LessThan(floorDays),
                "the burn-only crossing is shorter than the certified 1 g coast floor");
            Assert.That(totalDays, Is.EqualTo(13.86287).Within(0.05),
                "measured: the burn-only crossing's output length");
            Assert.That(plan.DeltaVMps / 1000.0, Is.GreaterThan(1821.1),
                "and it buys that time with delta-v, above the floor's 1,821 km/s");
            Assert.That(plan.DeltaVMps / 1000.0, Is.LessThan(3000.0));
        }

        /// <summary>
        /// Step 3: the moving-target loop solves the arrival date as well as the
        /// seam. The chain is rebuilt at the total each pass solves, so a settled
        /// plan's arrival date is the date its chain was built at, and a second
        /// call returns the same total. Building again at the plan's own arrival
        /// date (the fixed-date overload) reproduces that total, which is the
        /// self-consistency check: the date is a fixed point, not a seed that
        /// happened to be close.
        /// </summary>
        [Test]
        public void TheBurnOnlyMovingTargetSettles()
        {
            _planner.BurnLevelG = 1.0;

            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            var trace = new List<string>();
            _planner.Trace = trace.Add;

            var watch = Stopwatch.StartNew();
            TrajectoryPlan plan = _planner.PlanBurnOnly(ship, "mercury");
            watch.Stop();
            foreach (string line in trace) TestContext.WriteLine("trace: " + line);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));

            foreach (PlanArc arc in plan.Arcs)
            {
                Assert.That(arc.Phase == FlightPhase.Cruise && arc.IsCoast, Is.False,
                    "the burn-only crossing leaves no cruise coast");
            }

            for (int i = 0; i < plan.Arcs.Count - 1; i++)
            {
                Assert.That(plan.Arcs[i].EndJulianDate,
                    Is.EqualTo(plan.Arcs[i + 1].StartJulianDate).Within(1e-12),
                    $"seam {i} date");
            }

            AssertChainCloses(plan);

            // The plan's arrival reproduces its own total when the chain is built
            // there: the arrival date is the chain's own build date, not a date
            // the seam happened to land near. The tolerance is the loop's.
            TrajectoryPlan rebuilt = _planner.PlanBurnOnly(ship, "mercury",
                plan.ArrivalJulianDate);
            Assert.That(rebuilt.IsFeasible, Is.True, rebuilt.InfeasibleReason);
            TestContext.WriteLine(
                $"arrival-date rebuild: {rebuilt.FlightTimeDays:F6} d against " +
                $"{plan.FlightTimeDays:F6} d");

            Assert.That(rebuilt.FlightTimeSeconds,
                Is.EqualTo(plan.FlightTimeSeconds).Within(10.0),
                "the chain built at the plan's arrival reproduces the settled total");

            // Deterministic: a second call from the same state settles on the
            // same plan.
            TrajectoryPlan second = _planner.PlanBurnOnly(ship, "mercury");
            Assert.That(second.IsFeasible, Is.True, second.InfeasibleReason);
            Assert.That(second.FlightTimeSeconds,
                Is.EqualTo(plan.FlightTimeSeconds).Within(1e-6),
                "the moving-target total is stable across calls");
            Assert.That(second.ArrivalJulianDate,
                Is.EqualTo(plan.ArrivalJulianDate).Within(1e-9));

            double totalDays = plan.FlightTimeDays;
            double coastFloorDays = _planner.Plan(ship, "mercury").FlightTimeDays;
            TestContext.WriteLine(
                $"moving-target total {totalDays:F5} d in {watch.Elapsed.TotalSeconds:F1} s; " +
                $"coast floor {coastFloorDays:F5} d");

            Assert.That(totalDays, Is.GreaterThan(coastFloorDays),
                "the burn-only crossing is the longer plan at Mercury 1 g");
        }

        /// <summary>
        /// <see cref="TripPlanner.PlanFastest"/> is the nav computer's minimum.
        /// Since the split, the coast floor is much shorter than the zero-coast
        /// crossing (Mars 0.3 g: 7.17 d against 16.49 d), so the coast floor is
        /// the minimum and the burn-only crossing is **dominated**: it survives as
        /// a research entry point and as the short coast's seed, not as the
        /// fastest plan. The comparison is the two plans', not an assumption.
        /// </summary>
        [Test]
        public void PlanFastestReturnsTheCoastFloorWhenTheBurnOnlyCrossingIsLonger()
        {
            _planner.BurnLevelG = 0.3;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");

            TrajectoryPlan coast = _planner.Plan(ship, "mars");
            Assert.That(coast.IsFeasible, Is.True, coast.InfeasibleReason);

            var watch = Stopwatch.StartNew();
            TrajectoryPlan fastest = _planner.PlanFastest(ship, "mars");
            watch.Stop();
            PrintPlan(fastest);

            TrajectoryPlan burnOnly = _planner.PlanBurnOnly(ship, "mars");
            TestContext.WriteLine($"coast {coast.FlightTimeDays:F5} d, fastest " +
                                  $"{fastest.FlightTimeDays:F5} d, burn-only " +
                                  $"{burnOnly.FlightTimeDays:F5} d " +
                                  $"(feasible {burnOnly.IsFeasible})");

            Assert.That(fastest.IsFeasible, Is.True, fastest.InfeasibleReason);
            Assert.That(fastest.FlightTimeSeconds,
                Is.EqualTo(coast.FlightTimeSeconds).Within(1.0),
                "the coast floor is the fastest feasible plan");
            Assert.That(fastest.FlightTimeDays, Is.EqualTo(7.16751).Within(0.05));
            Assert.That(burnOnly.IsFeasible, Is.True, burnOnly.InfeasibleReason);
            Assert.That(burnOnly.FlightTimeSeconds,
                Is.GreaterThan(coast.FlightTimeSeconds),
                "the zero-coast crossing is now the longer plan at Mars 0.3 g");

            // The minimum is the coast shape: break → escape → departure burn →
            // coast → approach → brake → capture.
            Assert.That(fastest.Arcs.Count, Is.EqualTo(8));
            Assert.That(fastest.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(fastest.Arcs[0].Path, Is.InstanceOf<Quintic>());
            Assert.That(fastest.Arcs[1].Phase, Is.EqualTo(FlightPhase.Escape));
            Assert.That(fastest.Arcs[1].IsCoast, Is.True);
            Assert.That(fastest.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(fastest.Arcs[2].IsFiniteBurn, Is.True);
            Assert.That(fastest.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise
                                                && arc.IsCoast),
                Is.True, "the minimum cruises; it is not the zero-coast shape");

            AssertChainCloses(fastest);

            TestContext.WriteLine(
                $"fastest {fastest.FlightTimeDays:F5} d / " +
                $"{fastest.DeltaVMps / 1000.0:F0} km/s against the burn-only " +
                $"{burnOnly.FlightTimeDays:F5} d / {burnOnly.DeltaVMps / 1000.0:F0} km/s; " +
                $"planned in {watch.Elapsed.TotalSeconds:F1} s");
        }

        /// <summary>
        /// The available-time budget <see cref="TripPlanner"/> gives the break, read
        /// from the plan's own arcs: the coast's end minus the departure time, less
        /// the departure burn, the reserved brake when there is no approach leg, and
        /// the minimum coast. A returned break at or above this is a break the cap
        /// shortened.
        /// </summary>
        private static double BreakAvailableSeconds(TrajectoryPlan plan)
        {
            double breakSeconds = plan.Arcs[0].DurationSeconds;
            double escapeSeconds = 0.0;
            double burnSeconds = 0.0;
            double coastSeconds = 0.0;
            double brakeSeconds = 0.0;
            bool hasApproach = false;
            foreach (PlanArc arc in plan.Arcs)
            {
                if (arc.Phase == FlightPhase.Escape)
                    escapeSeconds = arc.DurationSeconds;
                else if (arc.Phase == FlightPhase.Cruise && arc.IsFiniteBurn)
                    burnSeconds = arc.DurationSeconds;
                else if (arc.Phase == FlightPhase.Cruise && arc.IsCoast)
                    coastSeconds = arc.DurationSeconds;
                else if (arc.Phase == FlightPhase.Approach)
                    hasApproach = true;
                else if (arc.Phase == FlightPhase.Capture && arc.IsFiniteBurn)
                    brakeSeconds = arc.DurationSeconds;
            }

            double coastEndFromDeparture = breakSeconds + escapeSeconds
                                          + burnSeconds + coastSeconds;
            return coastEndFromDeparture - burnSeconds
                   - (hasApproach ? 0.0 : brakeSeconds) - 3_600.0;
        }

        /// <summary>
        /// The slowest trip the nav computer would offer, by the same probe the
        /// flight session uses: three times the floor, bisected six times when the
        /// cap itself is refused.
        /// </summary>
        private double MaximumWindowDays(ShipState ship, string targetId, double floorDays)
        {
            double floorSeconds = floorDays * JulianDate.SecondsPerDay;
            double capSeconds = TripPlanner.MaxDurationScale * floorSeconds;
            if (_planner.PlanAtDuration(ship, targetId, capSeconds).IsFeasible)
                return TripPlanner.MaxDurationScale * floorDays;

            double feasible = floorSeconds;
            double infeasible = capSeconds;
            for (int probe = 0; probe < 6; probe++)
            {
                double middle = 0.5 * (feasible + infeasible);
                if (_planner.PlanAtDuration(ship, targetId, middle).IsFeasible)
                    feasible = middle;
                else
                    infeasible = middle;
            }

            return feasible / JulianDate.SecondsPerDay;
        }

        /// <summary>
        /// Since the split, the coast floor is **below** the burn-only crossing's
        /// own minimum (Mars 0.3 g: 7.17 d against 16.49 d), so the short-coast
        /// gap is empty at the current epoch: there is no duration between a
        /// shorter burn-only minimum and a later coast floor for the short coast
        /// to fill. The family stays in the planner for a route whose burn-only
        /// crossing still wins; this test records that the gap is closed here.
        /// </summary>
        [TestCase("mercury", 1.0)]
        [TestCase("mars", 0.3)]
        [TestCase("jupiter", 0.3)]
        public void TheShortCoastGapIsEmptyBecauseTheCoastFloorIsFaster(
            string targetId, double level)
        {
            _planner.BurnLevelG = level;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.PlanFastest(ship, targetId);
            TrajectoryPlan coast = _planner.Plan(ship, targetId);
            TrajectoryPlan burnOnly = _planner.PlanBurnOnly(ship, targetId);
            TestContext.WriteLine($"{targetId} {level} g: coast " +
                                  $"{coast.FlightTimeDays:F5} d, fastest " +
                                  $"{fastest.FlightTimeDays:F5} d, burn-only " +
                                  $"{burnOnly.FlightTimeDays:F5} d " +
                                  $"(feasible {burnOnly.IsFeasible})");

            Assert.That(fastest.IsFeasible, Is.True, fastest.InfeasibleReason);
            Assert.That(coast.IsFeasible, Is.True, coast.InfeasibleReason);
            Assert.That(fastest.FlightTimeSeconds,
                Is.EqualTo(coast.FlightTimeSeconds).Within(1.0),
                "the minimum is the coast floor");
            if (burnOnly.IsFeasible)
            {
                Assert.That(burnOnly.FlightTimeSeconds,
                    Is.GreaterThan(coast.FlightTimeSeconds),
                    "the burn-only crossing is longer, so there is no gap");
            }

            // The minimum is a normal coast plan, not a short-coast shape.
            Assert.That(fastest.Arcs.Count, Is.EqualTo(8));
            Assert.That(fastest.Arcs[0].Path, Is.InstanceOf<Quintic>());
            Assert.That(fastest.Arcs[3].Path, Is.InstanceOf<ConicArc>());
            Assert.That(fastest.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise
                                                && arc.IsCoast),
                Is.True);
            AssertChainCloses(fastest);
        }

        /// <summary>
        /// The coast family is continuous above the minimum: every duration from
        /// the floor up builds, the requested total is the plan's total, every
        /// seam closes, and delta-v falls as the trip lengthens. (Since the split
        /// the old burn-only-to-floor gap is empty; the minimum is the floor.)
        /// </summary>
        [TestCase("mercury", 1.0)]
        [TestCase("mars", 0.3)]
        [TestCase("jupiter", 0.3)]
        public void TheCoastFamilyIsContinuousAboveTheMinimum(string targetId,
            double level)
        {
            _planner.BurnLevelG = level;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.PlanFastest(ship, targetId);
            TrajectoryPlan coast = _planner.Plan(ship, targetId);
            Assert.That(fastest.IsFeasible, Is.True, fastest.InfeasibleReason);
            Assert.That(coast.IsFeasible, Is.True, coast.InfeasibleReason);

            double minimum = coast.FlightTimeSeconds;
            double[] probes =
            {
                minimum,
                minimum * 1.05,
                minimum * 1.20,
                minimum * 1.50,
                minimum * 2.00
            };

            var deltas = new List<double>();
            foreach (double seconds in probes)
            {
                TrajectoryPlan plan = _planner.PlanAtDuration(ship, targetId, seconds);
                TestContext.WriteLine(
                    $"{targetId} {level} g at {seconds / JulianDate.SecondsPerDay:F6} d: " +
                    $"feasible {plan.IsFeasible}, total {plan.FlightTimeDays:F6} d, " +
                    $"dv {plan.DeltaVMps / 1000.0:F1} km/s, arcs {plan.Arcs.Count}");

                Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
                Assert.That(plan.FlightTimeSeconds,
                    Is.EqualTo(seconds).Within(1.0),
                    "the plan's total is the requested total");
                Assert.That(double.IsFinite(plan.DeltaVMps), Is.True);
                AssertChainCloses(plan);
                deltas.Add(plan.DeltaVMps);
            }

            for (int i = 1; i < deltas.Count; i++)
            {
                Assert.That(deltas[i], Is.LessThan(deltas[i - 1]),
                    "a longer trip buys delta-v back");
            }
        }

        /// <summary>
        /// Step 5's "no hole" test: every duration from the advertised minimum to
        /// the coast floor and a little above it builds, and each plan's total is
        /// the duration asked for. The probes are the burn-only minimum, the
        /// minute above it (where the coast is still a hair), the gaps's
        /// quartiles, the old coast floor, and a duration above it — the slider's
        /// whole range, with no exception between the two old branches.
        /// </summary>
        [TestCase("mercury", 1.0)]
        [TestCase("mars", 0.3)]
        [TestCase("jupiter", 0.3)]
        public void EverySliderDurationFromTheMinimumToTheCoastFloorBuilds(
            string targetId, double level)
        {
            _planner.BurnLevelG = level;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.PlanFastest(ship, targetId);
            TrajectoryPlan coast = _planner.Plan(ship, targetId);
            double minimum = fastest.FlightTimeSeconds;
            double floor = coast.FlightTimeSeconds;

            double[] probes =
            {
                minimum,
                minimum + 1.0,
                minimum + 60.0,
                minimum + 0.25 * (floor - minimum),
                minimum + 0.50 * (floor - minimum),
                minimum + 0.75 * (floor - minimum),
                floor - 1.0,
                floor,
                floor + 0.05 * JulianDate.SecondsPerDay
            };

            foreach (double seconds in probes)
            {
                TrajectoryPlan plan = _planner.PlanAtDuration(ship, targetId, seconds);
                TestContext.WriteLine(
                    $"{targetId} {level} g at {seconds / JulianDate.SecondsPerDay:F6} d: " +
                    $"feasible {plan.IsFeasible}, total {plan.FlightTimeDays:F6} d, " +
                    $"dv {plan.DeltaVMps / 1000.0:F1} km/s" +
                    (plan.IsFeasible ? "" : " | " + plan.InfeasibleReason));

                Assert.That(plan.IsFeasible, Is.True,
                    $"{targetId} {level} g at {seconds / JulianDate.SecondsPerDay:F6} d: " +
                    plan.InfeasibleReason);
                double tolerance = seconds - minimum <= 3.0 ? 5.0 : 1.0;
                Assert.That(plan.FlightTimeSeconds, Is.EqualTo(seconds).Within(tolerance),
                    $"{targetId} {level} g at {seconds / JulianDate.SecondsPerDay:F6} d: " +
                    "the total is the request");
                AssertChainCloses(plan);
            }
        }

        /// <summary>
        /// What speed costs in **propellant**, not in delta-v alone. The flight
        /// panel's fuel readout is the rocket equation at the design exhaust
        /// velocity, so it is monotone in delta-v by construction; the question
        /// this table answers is where the delta-v goes. Two comparisons, both
        /// measured at the epoch (2026-11-15) from Gateway with the conic capture:
        ///
        /// 1. each family's coast floor and a trip at twice its duration, where
        ///    the faster end is the expensive one;
        /// 2. fixed Mars trips at 20, 30, 90 and 200 days, where the propellant
        ///    falls as the trip lengthens.
        ///
        /// The answer is the same in every row: **the fast plans spend more
        /// propellant, not less.** The burn-only crossing is no longer a faster
        /// plan at these routes — the split's coast floor beats it on time — so
        /// the fast/cheap trade now lives on the coast path itself: the floor
        /// spends 1,070 km/s (112.9 t) at 7.17 d to Mars and a 21.5-day trip
        /// spends 225.6 km/s (20 t).
        /// </summary>
        [Test]
        public void TheFastPlansSpendMorePropellantNotLess()
        {
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            var rows = new List<(string Family, string Label, double LevelG,
                TrajectoryPlan Plan)>();
            var watch = Stopwatch.StartNew();

            TrajectoryPlan Record(string family, string label, double level,
                TrajectoryPlan plan)
            {
                Assert.That(plan.IsFeasible, Is.True, $"{label}: {plan.InfeasibleReason}");
                rows.Add((family, label, level, plan));
                return plan;
            }

            TrajectoryPlan At(string targetId, double level, double days)
            {
                _planner.BurnLevelG = level;
                return _planner.PlanAtDuration(ship, targetId,
                    days * JulianDate.SecondsPerDay);
            }

            var familyCases = new[] { ("mercury", 1.0), ("mars", 0.3), ("jupiter", 0.3) };
            var floors = new Dictionary<string, TrajectoryPlan>();
            var slower = new Dictionary<string, TrajectoryPlan>();
            foreach ((string targetId, double level) in familyCases)
            {
                string key = $"{targetId} {level:F1} g";
                _planner.BurnLevelG = level;

                floors[key] = Record("the family", $"{key} coast floor", level,
                    _planner.Plan(ship, targetId));
                slower[key] = Record("the family", $"{key} double the floor", level,
                    At(targetId, level, 2.0 * floors[key].FlightTimeDays));
            }

            var marsTimes = new List<TrajectoryPlan>();
            foreach (double days in new[] { 20.0, 30.0, 90.0, 200.0 })
            {
                marsTimes.Add(Record("fixed Mars trips", $"Mars 0.3 g {days:F0} d",
                    0.3, At("mars", 0.3, days)));
            }

            TestContext.WriteLine(
                $"dry mass {ShipState.DefaultDryMassKg / 1000.0:N0} t, exhaust velocity " +
                $"{FuelModel.ExhaustVelocityMps / 1000.0:N0} km/s; propellant from the " +
                $"plan's own PropellantKg and PropellantFraction (the rocket equation).");
            TestContext.WriteLine(
                $"{"family",-17} {"case",-30} {"lvl",4} {"trip d",10} " +
                $"{"dv km/s",9} {"prop t",8} {"prop/dry",9} {"prop/m0",8} {"m0 t",7}");
            foreach ((string family, string label, double level, TrajectoryPlan plan)
                     in rows)
            {
                TestContext.WriteLine(
                    $"{family,-17} {label,-30} {level,3:F1}g {plan.FlightTimeDays,10:F5} " +
                    $"{plan.DeltaVMps / 1000.0,9:F1} {plan.PropellantKg / 1000.0,8:F1} " +
                    $"{plan.PropellantKg / plan.DryMassKg,9:F4} " +
                    $"{plan.PropellantFraction * 100.0,7:F2}% " +
                    $"{(plan.DryMassKg + plan.PropellantKg) / 1000.0,7:F1}");
            }

            foreach ((string targetId, double level) in familyCases)
            {
                string key = $"{targetId} {level:F1} g";
                Assert.That(floors[key].PropellantKg,
                    Is.GreaterThan(slower[key].PropellantKg),
                    $"{key}: the faster floor spends more propellant");
                Assert.That(floors[key].DeltaVMps,
                    Is.GreaterThan(slower[key].DeltaVMps),
                    $"{key}: delta-v is ordered the same way");
            }

            for (int i = 0; i < marsTimes.Count - 1; i++)
            {
                Assert.That(marsTimes[i].DeltaVMps,
                    Is.GreaterThan(marsTimes[i + 1].DeltaVMps),
                    "a longer trip spends less delta-v");
                Assert.That(marsTimes[i].PropellantKg,
                    Is.GreaterThan(marsTimes[i + 1].PropellantKg),
                    "and less propellant");
            }

            // The planning cost of the trade is printed, not asserted.
            TestContext.WriteLine($"measured in {watch.Elapsed.TotalSeconds:F0} s");
        }

        /// <summary>
        /// A departure from a **Mars 300 km circular orbit** to Earth and the Moon
        /// at 60, 90 and 120 days. This is the low-orbit case the physical
        /// departure was designed for: the break is a short commanded-level burn
        /// (1,832 s at 0.3 g), the escape conic is a ballistic Mars-centred
        /// `ConicArc` with only the third-body correction, the totals are the
        /// measured ~81/50/36 (Earth) and ~83/50/35 (Moon) km/s, and the trade is
        /// monotone — a longer trip costs less. The arrival is the requested date
        /// and every seam is inside the profile's gate.
        /// </summary>
        [Test]
        public void AMarsOrbitDepartureClimbsBallisticallyToEarthAndTheMoon()
        {
            var expected = new Dictionary<(string Target, double Days), double>
            {
                { ("earth", 60.0), 73.8191 },
                { ("earth", 90.0), 47.5165 },
                { ("earth", 120.0), 34.5091 },
                { ("moon", 60.0), 75.9672 },
                { ("moon", 90.0), 47.2227 },
                { ("moon", 120.0), 33.1853 }
            };
            var days = new[] { 60.0, 90.0, 120.0 };
            BodyRuntime mars = _model.Get("mars");
            double level = _planner.FixedBurnAccelerationMps2;

            foreach (string target in new[] { "earth", "moon" })
            {
                var totals = new double[days.Length];
                for (int i = 0; i < days.Length; i++)
                {
                    ShipState ship = FlightFixture.ShipInEquatorialOrbit(
                        _model, "mars", 300e3);
                    double requested = days[i] * JulianDate.SecondsPerDay;
                    TrajectoryPlan plan = _planner.PlanAtDuration(ship, target, requested);
                    PrintPlan(plan);

                    Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
                    Assert.That(plan.FlightTimeSeconds, Is.EqualTo(requested).Within(1e-3),
                        $"{target} {days[i]} d: the requested total is the plan's total");
                    Assert.That(plan.Arcs[plan.Arcs.Count - 1].EndJulianDate,
                        Is.EqualTo(plan.ArrivalJulianDate).Within(1e-12),
                        "the last arc ends at the arrival date");

                    // The break: short, a quintic, at the commanded level.
                    PlanArc breakArc = plan.Arcs[0];
                    Assert.That(breakArc.Phase, Is.EqualTo(FlightPhase.Departure));
                    Assert.That(breakArc.Path, Is.InstanceOf<Quintic>());
                    Assert.That(breakArc.DurationSeconds,
                        Is.LessThan(0.1 * JulianDate.SecondsPerDay),
                        "the break is well under a day, not a multi-day hover");
                    Assert.That(breakArc.DurationSeconds, Is.GreaterThan(600.0),
                        "the break is a real burn, not a seam");
                    Assert.That(breakArc.PeakThrustMps2, Is.EqualTo(level).Within(0.05),
                        "the break peaks at the commanded level");

                    // The climb: a ballistic Mars-centred conic.
                    PlanArc escape = plan.Arcs[1];
                    Assert.That(escape.Phase, Is.EqualTo(FlightPhase.Escape));
                    Assert.That(escape.IsCoast, Is.True);
                    Assert.That(escape.Path, Is.InstanceOf<ConicArc>());
                    Assert.That(((ConicArc)escape.Path).Centre, Is.SameAs(mars));
                    Assert.That(escape.ReferenceBodyId, Is.EqualTo("mars"));
                    Assert.That(escape.DeltaVMps, Is.LessThan(50.0),
                        "the escape is ballistic; only the third-body correction is commanded");
                    Assert.That(escape.DurationSeconds,
                        Is.GreaterThan(1.0 * JulianDate.SecondsPerDay),
                        "the parabolic climb takes days");

                    AssertChainCloses(plan);

                    double anchor = expected[(target, days[i])];
                    TestContext.WriteLine($"mars 300 km -> {target} {days[i]} d: " +
                                          $"{plan.DeltaVMps / 1000.0:F3} km/s " +
                                          $"(band {anchor:F3} ± 4%)");
                    Assert.That(plan.DeltaVMps / 1000.0,
                        Is.EqualTo(anchor).Within(0.04 * anchor),
                        $"{target} {days[i]} d: measured total is the band's centre");

                    totals[i] = plan.DeltaVMps;
                }

                // The time/fuel trade is monotone: the slow end costs less.
                Assert.That(totals[2], Is.LessThan(totals[1]),
                    $"{target}: 120 d costs less than 90 d");
                Assert.That(totals[1], Is.LessThan(totals[0]),
                    $"{target}: 90 d costs less than 60 d");
            }
        }

        /// <summary>
        /// The break is a short escape burn, not a powered well crossing. For the
        /// Mars 300 km orbit and Gateway, the integrated break delta-v must sit
        /// between the impulsive escape `v_esc − v_circ` and four times it: the
        /// measured Mars spend is 2.854 km/s against 1.411 km/s (2.02×), Gateway
        /// 1.404 against 0.471 (2.98×). It must not be hundreds of kilometres per
        /// second, which is what the old multi-day radial quintic spent.
        /// </summary>
        [Test]
        public void TheBreakSpendsAnImpulsiveEscapeNotHundredsOfKilometresPerSecond()
        {
            var cases = new (string Label, ShipState Ship, string Target, double Days)[]
            {
                ("Mars 300 km -> Earth",
                    FlightFixture.ShipInEquatorialOrbit(_model, "mars", 300e3), "earth", 60.0),
                ("Gateway -> Mars",
                    FlightFixture.ShipAt(_model, "gateway"), "mars", 90.0)
            };

            foreach ((string label, ShipState ship, string target, double days) in cases)
            {
                _planner.BurnLevelG = 0.3;
                TrajectoryPlan plan = _planner.PlanAtDuration(ship, target,
                    days * JulianDate.SecondsPerDay);
                Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

                PlanArc breakArc = plan.Arcs[0];
                BodyRuntime departure = _model.Get(breakArc.ReferenceBodyId);
                _model.ComputeState(departure, breakArc.StartJulianDate,
                    out Vector3d departurePosition, out _);
                double radius = Vector3d.Distance(
                    breakArc.Path.PositionAt(0.0), departurePosition);
                double escapeSpeed = Math.Sqrt(2.0 * departure.Gm / radius);
                double circularSpeed = Math.Sqrt(departure.Gm / radius);
                double vInfinity = plan.DepartureVInfinityMps;
                double periapsisSpeed = Math.Sqrt(escapeSpeed * escapeSpeed
                                                  + vInfinity * vInfinity);
                double impulsive = periapsisSpeed - circularSpeed;
                double spent = breakArc.DeltaVMps + breakArc.EntryImpulseMps;

                TestContext.WriteLine($"{label}: r0 {radius / 1000.0:F0} km, " +
                                      $"v_esc {escapeSpeed:F1}, v_circ {circularSpeed:F1}, " +
                                      $"v∞ {vInfinity / 1000.0:F3} km/s, " +
                                      $"impulsive {impulsive:F1} m/s, break {spent:F1} m/s, " +
                                      $"ratio {spent / impulsive:F3}");

                Assert.That(spent, Is.GreaterThan(impulsive),
                    $"{label}: the break exceeds the impulsive escape");
                Assert.That(spent, Is.LessThanOrEqualTo(4.0 * impulsive),
                    $"{label}: the break is within 4× the impulsive escape " +
                    $"(measured {spent / impulsive:F3}×)");
                Assert.That(spent, Is.LessThan(100_000.0),
                    $"{label}: the break is not hundreds of km/s " +
                    $"(measured {spent / 1000.0:F3} km/s)");
            }
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

                Assert.That(positionStep, Is.LessThan(1e3),
                    $"seam {i} ({left.Phase}->{right.Phase}) position");
                Assert.That(velocityStep, Is.LessThan(1.0),
                    $"seam {i} ({left.Phase}->{right.Phase}) velocity");
            }
        }

        private void AssertOrbitAround(BodyRuntime body, ShipState ship,
            double expectedRadius, double eccentricityTolerance, double radiusTolerance)
        {
            _model.ComputeState(body, ship.JulianDate,
                out Vector3d bodyPosition, out Vector3d bodyVelocity);

            Vector3d relativePosition = ship.PositionM - bodyPosition;
            Vector3d relativeVelocity = ship.VelocityMps - bodyVelocity;

            double eccentricity = TwoBody.Eccentricity(relativePosition, relativeVelocity,
                body.Gm);
            double radius = relativePosition.Magnitude;

            TestContext.WriteLine($"{body.Id}: r={radius / 1000.0:F0} km " +
                                  $"(target {expectedRadius / 1000.0:F0} km), e={eccentricity:E3}");

            Assert.That(Math.Abs(radius - expectedRadius) / expectedRadius,
                Is.LessThan(radiusTolerance), "parking radius");
            Assert.That(eccentricity, Is.LessThan(eccentricityTolerance), "eccentricity");
        }
    }
}
