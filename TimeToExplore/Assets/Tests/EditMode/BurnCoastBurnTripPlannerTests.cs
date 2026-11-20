using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The burn–coast–burn cruise: a quintic break out of the departure sphere of
    /// influence, a prograde finite burn at the chosen level, a heliocentric
    /// Lambert coast, a retrograde finite brake, and the existing conic or hybrid
    /// capture. The break ends on the departure clearance sphere at the local
    /// escape speed and the burn starts there; the burn's ray and duration, the
    /// break's duration, and the coast are solved together so every seam is
    /// continuous in position and velocity, and the brake spends the handover the
    /// coast plan would have made an impulse. The torch cruise stays the default.
    /// </summary>
    [Category("flight")]
    public class BurnCoastBurnTripPlannerTests
    {
        private const double TripDays = 90.0;

        /// <summary>The clearance the profile pins its break to, as a multiple of
        /// the departure body's sphere of influence.</summary>
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
        public void TheProfileIsAQuinticBreakAProgradeBurnACoastARetrogradeBrakeAndTheCapture()
        {
            TrajectoryPlan plan = PlanTo("mars");
            PrintPlan(plan);

            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);
            Assert.That(plan.Arcs.Count, Is.EqualTo(7));

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

            // coast
            Assert.That(plan.Arcs[2].Phase, Is.EqualTo(FlightPhase.Cruise));
            Assert.That(plan.Arcs[2].Path, Is.InstanceOf<ConicArc>());
            Assert.That(((ConicArc)plan.Arcs[2].Path).Centre, Is.SameAs(_model.Sun));

            // brake, retrograde about the Sun, shedding speed
            Assert.That(plan.Arcs[3].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[3].IsFiniteBurn, Is.True);
            var brake = (FiniteBurnArc)plan.Arcs[3].Path;
            Assert.That(brake.Centre, Is.SameAs(_model.Sun));
            Assert.That(brake.IsRetrograde, Is.True);
            Assert.That(plan.Arcs[3].Path.VelocityAt(1.0).Magnitude,
                Is.LessThan(plan.Arcs[3].Path.VelocityAt(0.0).Magnitude),
                "the brake sheds speed");

            // enter orbit on the existing conic capture chain
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[4].IsCoast, Is.True);
            Assert.That(plan.Arcs[5].Phase, Is.EqualTo(FlightPhase.Capture));
            Assert.That(plan.Arcs[6].Phase, Is.EqualTo(FlightPhase.Parking));
            Assert.That(plan.Arcs[6].EndJulianDate,
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
        public void TheHandoverAtTheTargetSoiIsFlownNotImpulsive()
        {
            TrajectoryPlan plan = PlanTo("mars");
            PlanArc inbound = plan.Arcs[4];

            Assert.That(inbound.Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(inbound.ReferenceBodyId, Is.EqualTo("mars"));
            Assert.That(inbound.EntryImpulseMps, Is.EqualTo(0.0));

            // The only impulses left are the capture chain's own trim and
            // circularisation, which are not at the sphere of influence.
            double chainImpulses = 0.0;
            for (int i = 4; i < plan.Arcs.Count; i++)
                chainImpulses += plan.Arcs[i].EntryImpulseMps;

            TestContext.WriteLine($"chain impulses {chainImpulses / 1000.0:F2} km/s, " +
                                  $"total {plan.ImpulseDeltaVMps / 1000.0:F2} km/s");
            Assert.That(plan.ImpulseDeltaVMps, Is.EqualTo(chainImpulses).Within(1e-6));
        }

        [Test]
        public void TheBrakeSpendsTheHandoverTheCoastPlanWouldHaveImpulsed()
        {
            TrajectoryPlan plan = PlanTo("mars");
            TrajectoryPlan coast = CoastTo("mars");

            PlanArc brake = plan.Arcs[3];
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
                Assert.That(velocityStep, Is.LessThan(1e-6), $"seam {i}");
            }
        }

        [Test]
        public void TheDepartureBurnNeverCrossesTheDepartureSphereOfInfluence()
        {
            TrajectoryPlan plan = PlanTo("mars");
            PlanArc burn = plan.Arcs[1];
            Assert.That(burn.IsFiniteBurn, Is.True);

            BodyRuntime earth = _model.Get("earth");
            double soi = SphereOfInfluence.RadiusM(earth);

            int samples = 64;
            double minimum = double.PositiveInfinity;
            for (int i = 0; i <= samples; i++)
            {
                double u = (double)i / samples;
                double julianDate = burn.StartJulianDate
                                    + burn.Path.SecondsAt(u) / JulianDate.SecondsPerDay;
                _model.ComputeState(earth, julianDate,
                    out Vector3d earthPosition, out _);
                double distance = Vector3d.Distance(burn.Path.PositionAt(u), earthPosition);
                if (distance < minimum) minimum = distance;
            }

            TestContext.WriteLine($"departure burn closest approach {minimum / 1000.0:N0} km " +
                                  $"against the {soi / 1000.0:N0} km sphere");
            Assert.That(minimum, Is.GreaterThanOrEqualTo(soi),
                "the steady burn starts outside the sphere and stays there");
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

        [Test]
        public void ASlowArrivalThatWouldNeedAnAccelerationIsRejectedWithAReason()
        {
            TrajectoryPlan plan = PlanTo("mars", 200.0);

            TestContext.WriteLine("reason: " + plan.InfeasibleReason);
            Assert.That(plan.IsFeasible, Is.False);
            Assert.That(plan.InfeasibleReason, Does.Contain("brake"));
            Assert.That(plan.InfeasibleReason, Does.Contain("acceleration"));
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
            Assert.That(plan.Arcs.Count, Is.EqualTo(8));
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
            Assert.That(plan.Arcs[4].Phase, Is.EqualTo(FlightPhase.Coast));
            Assert.That(plan.Arcs[4].ReferenceBodyId, Is.EqualTo("mars"));
            Assert.That(plan.Arcs[4].EntryImpulseMps, Is.EqualTo(0.0),
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
                    Assert.That(positionStep, Is.LessThan(1e-3), $"seam {i}");
                    Assert.That(velocityStep, Is.LessThan(1e-5), $"seam {i}");
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
            Assert.That(fastest.FlightTimeDays, Is.LessThan(3.6),
                "the pinned break moved the true floor well below the old 12.32 d");
            Assert.That(fastest.FlightTimeDays, Is.GreaterThan(3.3),
                "the floor is a real burn-coast-burn plan, not an unphysical request");
            Assert.That(fastest.PeakThrustMps2,
                Is.LessThanOrEqualTo(fastest.MaxAccelerationMps2 * (1.0 + 1e-6)));
            AssertChainCloses(fastest);

            // A little below the floor is the same geometry with the break
            // working harder than the drive allows.
            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mercury",
                3.43 * JulianDate.SecondsPerDay);
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


        /// <summary>The break search's fast model is the departure body alone;
        /// the plan reports the full field. The two peaks must stay close enough
        /// that the search's duration decision does not move the plan, and the
        /// near-floor case is the one that runs through the 5% screen.</summary>
        [TestCase("mercury", 1.0, 8.0, 3.0165, 92.906, 569.9)]
        [TestCase("mercury", 1.0, 12.3, 1.3534, 61.699, 379.9)]
        [TestCase("mars", 0.3, 90.0, 0.0334, 8.978, 45.4)]
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

            // The search input changed; the plan the caller receives did not.
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
            Assert.That(plan.PeakThrustMps2, Is.EqualTo(9.702).Within(0.005),
                "the floor peak is unchanged");
            // The floor is found to the probe model's 0.2% retry resolution, so
            // the reported delta-v tracks the returned duration to that order;
            // it is not the plan's identity. The peaks and the 8/12.3/90-day
            // anchors below are the exact ones.
            Assert.That(plan.DeltaVMps / 1000.0, Is.EqualTo(1763.0).Within(0.01 * 1763.0),
                "the floor total delta-v is unchanged within the search resolution");

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

            // 4 d sits on the drive limit, the floor is just below it, and
            // 3.43 d is the rejected just-under-floor case.
            foreach (double days in new[] { 4.0, 3.47, 3.43 })
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
                3.43 * JulianDate.SecondsPerDay);
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

        [Test]
        public void TheAdvertisedMinimumIsTheTrueMarsFloor()
        {
            _planner.BurnLevelG = 0.3;
            ShipState ship = FlightFixture.ShipAt(_model, "gateway");
            TrajectoryPlan fastest = _planner.Plan(ship, "mars");

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
            Assert.That(fastest.FlightTimeDays, Is.LessThan(9.6),
                "the true floor is below the old 9.64 d first-feasible plan");
            Assert.That(fastest.FlightTimeDays, Is.GreaterThan(7.0));
            AssertChainCloses(fastest);

            TrajectoryPlan tooFast = _planner.PlanAtDuration(ship, "mars",
                7.8 * JulianDate.SecondsPerDay);
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
