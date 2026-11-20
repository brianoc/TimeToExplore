using System;
using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The burn–coast–burn cruise: a quintic break out of the departure sphere of
    /// influence, a prograde finite burn at the chosen level, a heliocentric
    /// Lambert coast, a planetocentric approach about the target, a retrograde
    /// finite brake, and the existing conic or hybrid capture. The break ends on
    /// the departure clearance sphere at the local escape speed and the burn
    /// starts there; the burn's ray and duration, the break's duration, and the
    /// coast are solved together so every seam is continuous in position and
    /// velocity. The coast ends on the target's handover ceiling and the approach
    /// falls from there to the brake's start, so a Sun-only conic no longer
    /// describes the deep part of the final approach; the brake spends the rest of
    /// the handover. The torch cruise stays the default.
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
        private TripPlanner _coastPlanner;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _integrator = new ShipIntegrator(_gravity);
            _planner = new TripPlanner(_model, _gravity)
            {
                CruiseStyle = CruiseStyle.BurnCoastBurn,
                ArrivalStyle = CaptureStyle.Conic
            };
            _coastPlanner = new TripPlanner(_model, _gravity)
            {
                CruiseStyle = CruiseStyle.Coast,
                ArrivalStyle = CaptureStyle.Conic
            };
        }

        private TrajectoryPlan PlanTo(string targetId, double days = TripDays) =>
            _planner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"), targetId,
                days * JulianDate.SecondsPerDay);

        private TrajectoryPlan CoastTo(string targetId, double days = TripDays) =>
            _coastPlanner.PlanAtDuration(FlightFixture.ShipAt(_model, "gateway"), targetId,
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
        public void TheProfileIsAQuinticBreakABurnACoastAnApproachABrakeAndTheCapture()
        {
            TrajectoryPlan plan = PlanTo("mars");
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(8));

            // leave orbit
            Assert.That(plan.Arcs[0].Phase, Is.EqualTo(FlightPhase.Departure));
            Assert.That(plan.Arcs[0].Path, Is.InstanceOf<Quintic>());

            // burn hard, prograde about the Sun
            Assert.That(plan.Arcs[1].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[1].IsFiniteBurn, Is.True);
            var departureBurn = (FiniteBurnArc)plan.Arcs[1].Path;
            Assert.That(departureBurn.Centre, Is.SameAs(_model.Sun));
            Assert.That(departureBurn.IsRetrograde, Is.False);
            Assert.That(plan.Arcs[1].Path.VelocityAt(1.0).Magnitude,
                Is.GreaterThan(plan.Arcs[1].Path.VelocityAt(0.0).Magnitude),
                "the departure burn builds speed");

            // coast, about the Sun, ending on the target's handover ceiling
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Path, Is.InstanceOf<ConicArc>());
            Assert.That(((ConicArc)plan.Arcs[2].Path).Centre, Is.SameAs(_model.Sun));

            // the planetocentric approach, about the target
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Approach));
            Assert.That(plan.Arcs[3].IsCoast, Is.True);
            Assert.That(((ConicArc)plan.Arcs[3].Path).Centre, Is.SameAs(_model.Get("mars")));
            Assert.That(plan.Arcs[3].ReferenceBodyId, Is.EqualTo("mars"));

            // brake, retrograde about the Sun, shedding speed
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[4].IsFiniteBurn, Is.True);
            var brake = (FiniteBurnArc)plan.Arcs[4].Path;
            Assert.That(brake.Centre, Is.SameAs(_model.Sun));
            Assert.That(brake.IsRetrograde, Is.True);
            Assert.That(plan.Arcs[4].Path.VelocityAt(1.0).Magnitude,
                Is.LessThan(plan.Arcs[4].Path.VelocityAt(0.0).Magnitude),
                "the brake sheds speed");

            // enter orbit on the existing conic capture chain
            Assert.That(plan.Arcs[5].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[5].IsCoast, Is.True);
            Assert.That(plan.Arcs[6].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[7].Phase, Is.EqualTo(FlightPhase.Parking));
            Assert.That(plan.Arcs[7].EndJulianDate,
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

            Assert.That(weakBurn.DeltaVMps,
                Is.EqualTo(strongBurn.DeltaVMps).Within(0.001 * strongBurn.DeltaVMps),
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
                Is.EqualTo(368_111_400.0).Within(5_000.0),
                "measured: the brake starts 368,111.4 km out");

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
        public void TheFastTripsHaveNoApproachBecauseTheBrakeSpansTheSphere()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("mercury", 8.0);
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
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));

            _model.ComputeState(mercury, coast.EndJulianDate, out Vector3d mercuryAtEnd, out _);
            double coastEndRadius = Vector3d.Distance(coast.Path.PositionAt(1.0), mercuryAtEnd);
            TestContext.WriteLine($"coast end {coastEndRadius / 1000.0:F1} km, " +
                                  $"ceiling {band.CeilingRadiusM / 1000.0:F1} km");
            Assert.That(coastEndRadius, Is.GreaterThan(band.CeilingRadiusM),
                "the coast ends outside the ceiling here");
            Assert.That(coastEndRadius, Is.EqualTo(2_378_949_700.0).Within(10_000_000.0),
                "measured: the brake starts 2,378,950 km out");
            Assert.That(Vector3d.Distance(brake.Path.PositionAt(0.0),
                coast.Path.PositionAt(1.0)), Is.LessThan(1_000.0),
                "and the coast runs straight to the brake's start");
        }

        /// <summary>
        /// The coast has no brake to hand over, so it keeps its sphere-of-
        /// influence entry and its anchors: at the epoch from Gateway to Mars at
        /// 200 days the entry is the 577,241 km sphere and the fall is 1.3187
        /// days. The burn–coast–burn enters at the floor instead — the split is
        /// deliberate, not an omission.
        /// </summary>
        [Test]
        public void TheCoastProfileKeepsItsSphereOfInfluenceEntry()
        {
            TrajectoryPlan coast = CoastTo("mars", 200.0);
            Assert.That(coast.IsFeasible, Is.True, coast.InfeasibleReason);

            BodyRuntime mars = _model.Get("mars");
            double soi = SphereOfInfluence.RadiusM(mars);
            TestContext.WriteLine($"coast entry {coast.CaptureEntryRadiusM / 1000.0:F1} km " +
                                  $"(SOI {soi / 1000.0:F1} km), fall " +
                                  $"{coast.CaptureInboundSeconds / 86400.0:F4} d");

            Assert.That(coast.CaptureEntryRadiusM, Is.EqualTo(soi).Within(1.0),
                "the coast enters at the sphere of influence");
            Assert.That(coast.CaptureEntryRadiusM,
                Is.GreaterThan(ParkingOrbit.ConicRadiusM(mars) * 10.0),
                "which is far above the parking radius");
            Assert.That(coast.CaptureInboundSeconds,
                Is.EqualTo(1.3187 * JulianDate.SecondsPerDay).Within(60.0),
                "measured: the 1.3187-day fall is unchanged");
            Assert.That(coast.Arcs.Count, Is.EqualTo(5));
        }

        [Test]
        public void TheBrakeSpendsTheHandoverTheCoastPlanWouldHaveImpulsed()
        {
            TrajectoryPlan plan = PlanTo("mars");
            TrajectoryPlan coast = CoastTo("mars");

            PlanArc brake = null;
            foreach (PlanArc arc in plan.Arcs)
                if (arc.Phase == FlightPhase.Capture && arc.IsFiniteBurn) brake = arc;
            Assert.That(brake, Is.Not.Null);
            PlanArc coastHandover = null;
            foreach (PlanArc arc in coast.Arcs)
            {
                if (arc.Phase == FlightPhase.Coast && arc.ReferenceBodyId == "mars"
                    && arc.EntryImpulseMps > 0.0)
                {
                    coastHandover = arc;
                    break;
                }
            }

            Assert.That(coastHandover, Is.Not.Null);
            TestContext.WriteLine($"brake {brake.DeltaVMps / 1000.0:F2} km/s against the " +
                                  $"coast handover {coastHandover.EntryImpulseMps / 1000.0:F2} km/s");

            Assert.That(brake.DeltaVMps,
                Is.EqualTo(coastHandover.EntryImpulseMps).Within(0.05 * brake.DeltaVMps));
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

        [Test]
        public void ZeroLevelIsTheCoastPlan()
        {
            _planner.BurnLevelG = 0.0;
            TrajectoryPlan plan = PlanTo("mars");
            TrajectoryPlan coast = CoastTo("mars");
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FiniteBurnAccelerationMps2, Is.EqualTo(0.0));
            Assert.That(plan.FiniteBurnDeltaVMps, Is.EqualTo(0.0));
            Assert.That(plan.Arcs.Count, Is.EqualTo(coast.Arcs.Count));
            Assert.That(plan.ArrivalJulianDate,
                Is.EqualTo(coast.ArrivalJulianDate).Within(1e-9));
            Assert.That(plan.ImpulseDeltaVMps,
                Is.EqualTo(coast.ImpulseDeltaVMps).Within(1e-6));

            for (int i = 0; i < plan.Arcs.Count; i++)
            {
                Assert.That(plan.Arcs[i].Phase, Is.EqualTo(coast.Arcs[i].Phase));
                Assert.That(plan.Arcs[i].IsFiniteBurn, Is.False);
            }
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
            Assert.That(plan.DeltaVMps / 1000.0, Is.EqualTo(11.86).Within(0.5),
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
                CruiseStyle = CruiseStyle.BurnCoastBurn,
                ArrivalStyle = CaptureStyle.FiniteBurn,
                BurnLevelG = 0.3
            };
            TrajectoryPlan plan = planner.PlanAtDuration(
                FlightFixture.ShipAt(_model, "gateway"), "mars",
                TripDays * JulianDate.SecondsPerDay);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(9));
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
        public void ThePoweredCruiseIsStillTheDefault()
        {
            var planner = new TripPlanner(_model, _gravity);
            Assert.That(planner.CruiseStyle, Is.EqualTo(CruiseStyle.Powered));

            TrajectoryPlan plan = planner.Plan(FlightFixture.ShipAt(_model, "gateway"),
                "mars");
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FiniteBurnDeltaVMps, Is.EqualTo(0.0));
        }

        [Test]
        public void TheBreakEndsOnTheClearanceSphereAtTheLocalEscapeSpeed()
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("mercury", 8.0);
            PrintPlan(plan);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            PlanArc breakArc = plan.Arcs[0];
            Assert.That(breakArc.Phase, Is.EqualTo(FlightPhase.Departure));

            BodyRuntime earth = _model.Get("earth");
            double clearance = ClearanceSoi * SphereOfInfluence.RadiusM(earth);
            _model.ComputeState(earth, breakArc.EndJulianDate,
                out Vector3d earthPosition, out Vector3d earthVelocity);
            Vector3d relativePosition = breakArc.Path.PositionAt(1.0) - earthPosition;
            Vector3d relativeVelocity = breakArc.Path.VelocityAt(1.0) - earthVelocity;
            double escapeSpeed = Math.Sqrt(2.0 * earth.Gm / clearance);

            TestContext.WriteLine($"break end {relativePosition.Magnitude / clearance:F6} of the " +
                                  $"clearance, {relativeVelocity.Magnitude / 1000.0:F6} km/s " +
                                  $"against escape {escapeSpeed / 1000.0:F6} km/s, " +
                                  $"radial fraction {Vector3d.Dot(relativeVelocity, relativePosition.Normalized) / relativeVelocity.Magnitude:F9}");

            Assert.That(relativePosition.Magnitude, Is.EqualTo(clearance).Within(1.0),
                "the break ends on the clearance sphere");
            Assert.That(relativeVelocity.Magnitude, Is.EqualTo(escapeSpeed).Within(1e-6 * escapeSpeed),
                "the break ends at the local escape speed");
            Assert.That(Vector3d.Dot(relativeVelocity, relativePosition.Normalized),
                Is.EqualTo(relativeVelocity.Magnitude).Within(1e-6 * escapeSpeed),
                "the escape speed is along the escape ray");

            PlanArc burn = plan.Arcs[1];
            Assert.That(burn.IsFiniteBurn, Is.True);
            Assert.That(Vector3d.Distance(breakArc.Path.PositionAt(1.0), burn.Path.PositionAt(0.0)),
                Is.LessThan(1e-3), "the burn starts where the break ends");
            Assert.That((breakArc.Path.VelocityAt(1.0) - burn.Path.VelocityAt(0.0)).Magnitude,
                Is.LessThan(1e-5), "the burn starts at the break's velocity");
            Assert.That(breakArc.PeakThrustMps2,
                Is.LessThanOrEqualTo(plan.MaxAccelerationMps2 * (1.0 + 1e-6)),
                "the break is inside the drive's limit");
        }

        [TestCase(8.0)]
        [TestCase(6.0)]
        [TestCase(4.0)]
        public void TheFastMercuryTripsCloseAtOneG(double days)
        {
            _planner.BurnLevelG = 1.0;
            TrajectoryPlan plan = PlanTo("mercury", days);
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.FlightTimeDays, Is.EqualTo(days).Within(1e-3));
            Assert.That(plan.PeakThrustMps2,
                Is.LessThanOrEqualTo(plan.MaxAccelerationMps2 * (1.0 + 1e-6)));
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));

            PlanArc breakArc = plan.Arcs[0];
            BodyRuntime earth = _model.Get("earth");
            double clearance = ClearanceSoi * SphereOfInfluence.RadiusM(earth);
            _model.ComputeState(earth, breakArc.EndJulianDate, out Vector3d earthPosition, out _);
            Assert.That(Vector3d.Distance(breakArc.Path.PositionAt(1.0), earthPosition),
                Is.EqualTo(clearance).Within(1.0),
                "a fast trip's break still ends on the clearance sphere");
        }

        [Test]
        public void AFastMercuryFlightArrivesOnTheParkingOrbit()
        {
            _planner.BurnLevelG = 1.0;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan plan = _planner.PlanAtDuration(ship, "mercury",
                8.0 * JulianDate.SecondsPerDay);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            var autopilot = new Autopilot(_gravity, plan);
            IntegrationReport report = autopilot.Fly(ref ship, _integrator);

            double positionError = Vector3d.Distance(ship.PositionM, plan.ArrivalState.PositionM);
            double velocityError = (ship.VelocityMps - plan.ArrivalState.VelocityMps).Magnitude;
            TestContext.WriteLine($"8 d Mercury arrival error: {positionError:F2} m, " +
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
            TrajectoryPlan mercury = PlanTo("mercury", 12.3);
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
                    // point's residual and the coast's Lambert correction show
                    // up in these seams: measured worst 7.5e-2 m and 3.7e-4 m/s,
                    // still three orders inside the 1 km / 1 m/s gate.
                    Assert.That(positionStep, Is.LessThan(1.0), $"seam {i}");
                    Assert.That(velocityStep, Is.LessThan(1e-3), $"seam {i}");
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
            Assert.That(fastest.FlightTimeDays, Is.LessThan(3.4),
                "the floor entry moved the true floor below the old 3.46 d");
            Assert.That(fastest.FlightTimeDays, Is.GreaterThan(3.1),
                "the floor is a real burn-coast-burn plan, not an unphysical request");
            Assert.That(fastest.PeakThrustMps2,
                Is.LessThanOrEqualTo(fastest.MaxAccelerationMps2 * (1.0 + 1e-6)));
            AssertChainCloses(fastest);

            // A little below the floor is the same geometry with the break
            // working harder than the drive allows.
            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mercury",
                3.19 * JulianDate.SecondsPerDay);
            TestContext.WriteLine($"just under: {tooFast}, reason: {tooFast.InfeasibleReason}");

            Assert.That(tooFast.IsFeasible, Is.False);
            Assert.That(tooFast.InfeasibleReason, Is.Not.Empty,
                "a max-thrust rejection carries a cause");
            Assert.That(tooFast.InfeasibleReason, Does.Contain("departure break"),
                "the reason names the arc that is over the drive");
            Assert.That(tooFast.InfeasibleReason,
                Does.Contain(tooFast.PeakThrustMps2.ToString("F1")),
                "the reason names the peak the arc needs");
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
                CruiseStyle = CruiseStyle.BurnCoastBurn,
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
                CruiseStyle = CruiseStyle.BurnCoastBurn,
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
        /// The six fixed-duration plans are the profile's reference answers after
        /// the planetocentric approach leg, and a cheaper build must not move any
        /// of them. Measured at the epoch from Gateway with the conic capture, with
        /// the departure solve integrating its trial burns at 128 samples, the
        /// reported burn still at 512, and the coast re-solved from the reported
        /// burn's own end. The delta-v is pinned to four decimals against the
        /// approach-leg values — Mars 90 d is 44.3543 km/s, not the pre-approach
        /// 44.3564, and Uranus 400 d is 182.3124, not 182.3393.
        /// </summary>
        [TestCase("mars", 0.3, 90.0, 44.3543)]
        [TestCase("mercury", 1.0, 8.0, 557.0380)]
        [TestCase("uranus", 1.0, 400.0, 182.3124)]
        [TestCase("mars", 0.3, 20.0, 270.8500)]
        [TestCase("mars", 0.3, 10.0, 607.8259)]
        [TestCase("uranus", 1.0, 35.0, 1984.4479)]
        public void TheFixedDurationDeltaVAnchorsDoNotMove(string targetId,
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
                "the fixed-duration delta-v anchor is unchanged to four decimals");
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
            TrajectoryPlan plan = PlanTo("uranus", 35.0);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            PlanArc burn = plan.Arcs[1];
            PlanArc coast = plan.Arcs[2];
            Assert.That(burn.IsFiniteBurn, Is.True, "the departure burn is second");
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
        /// near-floor case is the one that runs through the 5% screen.</summary>
        [TestCase("mercury", 1.0, 8.0, 2.8471, 90.213, 557.038)]
        [TestCase("mercury", 1.0, 12.3, 1.3046, 60.547, 376.941)]
        [TestCase("mars", 0.3, 90.0, 0.0327, 8.845, 44.356)]
        public void TheFastBreakPeakTracksTheFullFieldPlan(string targetId,
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
            // it is not the plan's identity. The peaks and the 8/12.3/90-day
            // anchors below are the exact ones.
            Assert.That(plan.DeltaVMps / 1000.0, Is.EqualTo(1765.5).Within(0.01 * 1765.5),
                "the floor total delta-v moved by the shortened fall");

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

            // 4 d sits on the drive limit, the floor is just below it (3.22 d
            // with the floor entry), and 3.19 d is the rejected just-under case.
            foreach (double days in new[] { 4.0, 3.25, 3.19 })
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
                3.19 * JulianDate.SecondsPerDay);
            Assert.That(refused.IsFeasible, Is.False,
                "the just-below-floor trip still rejects");
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
            Assert.That(probe.Arcs.Count, Is.EqualTo(floor.Arcs.Count));
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

            // The fast floor is optimistic here. The certificate refuses it at the
            // chain gate because the brake/departure fixed point hits its
            // twelve-pass cap with a 2 km residual; the certified build retries
            // that same fixed point at twenty-four passes and certifies the same
            // duration. No tolerance and no gate moves, and the continuation
            // search is not needed.
            int retries = 0;
            bool continued = false;
            foreach (string probe in probes)
            {
                if (probe.StartsWith("pin retry")) retries++;
                if (probe.StartsWith("floor probe")) continued = true;
            }
            Assert.That(retries, Is.EqualTo(1),
                "the stalled pin was retried exactly once");
            Assert.That(continued, Is.False,
                "the retry certified the fast answer, so no probe continuation ran");

            TestContext.WriteLine($"mars floor: {fastest}, " +
                                  $"peak {fastest.PeakThrustMps2:F3} m/s²");
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
            Assert.That(fastest.FlightTimeDays, Is.LessThan(6.735),
                "the retry settles the brake at 6.73042 d, below the old 6.73715 d");
            Assert.That(fastest.FlightTimeDays, Is.GreaterThan(6.0));
            AssertChainCloses(fastest);

            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mars",
                6.6 * JulianDate.SecondsPerDay);
            TestContext.WriteLine($"just under: {tooFast}, reason: {tooFast.InfeasibleReason}");

            Assert.That(tooFast.IsFeasible, Is.False);
            Assert.That(tooFast.InfeasibleReason, Does.Contain("departure break"));
            Assert.That(tooFast.InfeasibleReason,
                Does.Contain(tooFast.PeakThrustMps2.ToString("F1")));
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
