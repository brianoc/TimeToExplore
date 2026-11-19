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
