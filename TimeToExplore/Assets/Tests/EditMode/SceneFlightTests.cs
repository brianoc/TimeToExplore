using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The milestone-2 acceptance flight, run through the scene's frame loop
    /// without a scene: set the warp from the flight's own curve, advance the
    /// clock, advance the ship to the clock, and clamp the clock to the ship.
    /// This is the same order of operations SolarSystemBootstrap.Update uses,
    /// so it proves the Go To button's flight, not just the planner.
    /// </summary>
    [Category("smoke")]
    public class SceneFlightTests
    {
        private const double FrameSeconds = 1.0 / 60.0;

        private SolarSystemModel _model;
        private GravityField _gravity;
        private FlightSession _session;
        private SimulationClock _clock;
        private TrajectoryPlan _plan;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _session = new FlightSession(_model, _gravity, new TripPlanner(_model, _gravity));
            _clock = new SimulationClock { JulianDate = _model.CurrentJulianDate };

            _session.PlaceAt("gateway", _clock.JulianDate);
            _plan = _session.Begin("mars", _clock.JulianDate);
        }

        [Test]
        public void PressGoToOnMarsAndWatchTheShipDoIt()
        {
            var run = new Run();
            run.Fly(_session, _clock, _model, double.NaN, 20_000, stopOnArrival: true);

            TestContext.WriteLine(
                $"arrival: {JulianDate.Format(_session.ArrivedJulianDate)}  " +
                $"flight {_plan.FlightTimeDays:F2} d  " +
                $"real {run.RealSeconds:F1} s over {run.Frames:N0} frames");
            TestContext.WriteLine(
                $"peak substeps/frame {run.MaxSubsteps}, " +
                $"warp peak {SimulationClock.FormatScale(run.PeakScale)}, " +
                $"final {SimulationClock.FormatScale(run.LastScale)}");

            foreach (PlanArc arc in _plan.Arcs)
                TestContext.WriteLine($"  {arc.Phase}: {arc.DurationSeconds / 3600.0:F2} h, " +
                                      $"peak {arc.PeakThrustMps2:F2} m/s², " +
                                      $"{arc.DeltaVMps / 1000.0:N0} km/s");

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived),
                "the flight must finish without a teleport");
            Assert.That(run.Collisions, Is.EqualTo(0));

            double positionError = Vector3d.Distance(_session.State.PositionM,
                _plan.ArrivalState.PositionM);
            double velocityError = (_session.State.VelocityMps - _plan.ArrivalState.VelocityMps)
                .Magnitude;
            TestContext.WriteLine($"arrival error {positionError:F1} m, {velocityError:F4} m/s");

            Assert.That(positionError, Is.LessThan(10_000.0), "matched within 10 km");
            Assert.That(velocityError, Is.LessThan(10.0), "matched within 10 m/s");

            double target = FlightWarp.TargetRealSeconds(_plan.FlightTimeSeconds);
            Assert.That(run.RealSeconds, Is.EqualTo(target).Within(Math.Max(2.0, 0.15 * target)),
                "the trip takes the real time the curve asks for");
            Assert.That(run.PeakScale, Is.GreaterThan(1_000.0),
                "a week-long trip cruises far above real time");
            Assert.That(run.LastScale, Is.LessThan(0.01 * run.PeakScale),
                "and it is back near real time at the finish");

            Assert.That(run.MaxSubsteps, Is.LessThan(FlightSession.DefaultSubstepsPerFrame),
                "the per-frame substep budget was never the limit");
            Assert.That(run.ClockAheadFrames, Is.EqualTo(0),
                "the world never ran ahead of the ship");
        }

        [Test]
        public void TheOrbitIsHeldAfterArrivalUnderTheFrameLoop()
        {
            var run = new Run();
            run.Fly(_session, _clock, _model, double.NaN, 20_000, stopOnArrival: true);

            Assert.That(_session.Status, Is.EqualTo(FlightStatus.Arrived), "arrive first");

            BodyRuntime mars = _model.Get("mars");
            double radius = ParkingOrbit.RadiusM(mars, _plan.MaxAccelerationMps2);
            double period = ParkingOrbit.PeriodSeconds(mars, _plan.MaxAccelerationMps2);
            double stop = _session.ArrivedJulianDate + 3.0 * period / JulianDate.SecondsPerDay;

            var coast = new Run();
            coast.Fly(_session, _clock, _model, stop, 20_000);

            _model.ComputeState(mars, _session.State.JulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            Vector3d relativePosition = _session.State.PositionM - marsPosition;
            Vector3d relativeVelocity = _session.State.VelocityMps - marsVelocity;
            double eccentricity = TwoBody.Eccentricity(relativePosition, relativeVelocity, mars.Gm);

            TestContext.WriteLine($"three orbits later: r={relativePosition.Magnitude / 1000.0:F0} km " +
                                  $"(target {radius / 1000.0:F0} km), e={eccentricity:E3}, " +
                                  $"{coast.Frames:N0} frames, drive " +
                                  $"{(_session.CurrentThrust.IsActive ? "on" : "off")}");

            Assert.That(coast.Collisions, Is.EqualTo(0));
            Assert.That(_session.CurrentThrust.IsActive, Is.False, "the drive stays off");
            Assert.That(Math.Abs(relativePosition.Magnitude - radius) / radius, Is.LessThan(0.02));
            Assert.That(eccentricity, Is.LessThan(0.03));
        }

        [Test]
        public void AStarvedBudgetClampsTheClockInsteadOfLettingTheWorldRunAhead()
        {
            int ahead = 0;
            int frames = 0;

            for (; frames < 400; frames++)
            {
                Step(2);
                if (_clock.JulianDate > _session.State.JulianDate + 1e-9) ahead++;
                if (_session.Status != FlightStatus.Flying) break;
            }

            TestContext.WriteLine($"{frames} starved frames reached " +
                                  $"{JulianDate.Format(_clock.JulianDate)}");

            Assert.That(ahead, Is.EqualTo(0),
                "with two substeps per frame the clock must follow the ship, not the request");
            Assert.That(_session.State.JulianDate, Is.GreaterThan(_model.Data.startJulianDate),
                "the ship still makes progress");
            Assert.That(_session.State.JulianDate, Is.LessThan(_plan.ArrivalJulianDate),
                "but nowhere near the arrival in 400 starving frames");
        }

        /// <summary>One frame of the scene loop, in the same order as the
        /// bootstrap.</summary>
        private void Step(int substepsPerFrame)
        {
            _model.Update(_clock.JulianDate);

            _clock.SetScale(FlightWarp.ScaleAt(_plan.FlightTimeSeconds, _real));
            _real += FrameSeconds;

            _clock.Advance((float)FrameSeconds);

            FlightStep step = _session.Advance(_clock.JulianDate, substepsPerFrame);
            if (step.ReachedJulianDate < _clock.JulianDate)
                _clock.JulianDate = step.ReachedJulianDate;

            _model.Update(_clock.JulianDate);
        }

        private double _real;

        /// <summary>The bootstrap's frame, counted, with the telemetry the
        /// acceptance report needs.</summary>
        private sealed class Run
        {
            public int Frames;
            public int MaxSubsteps;
            public int Collisions;
            public int ClockAheadFrames;
            public double PeakScale = 1.0;
            public double LastScale = 1.0;

            public double RealSeconds => Frames * FrameSeconds;

            public void Fly(FlightSession session, SimulationClock clock, SolarSystemModel model,
                double stopJulianDate, int maxFrames, bool stopOnArrival = false)
            {
                double realElapsed = 0.0;
                double flightSeconds = session.Plan != null ? session.Plan.FlightTimeSeconds : 0.0;

                while (Frames < maxFrames)
                {
                    if (session.Status == FlightStatus.Aborted ||
                        session.Status == FlightStatus.Rejected) break;

                    if (stopOnArrival)
                    {
                        if (session.Status == FlightStatus.Arrived) break;
                    }
                    else if (session.Status != FlightStatus.Arrived) break;

                    if (!double.IsNaN(stopJulianDate) && clock.JulianDate >= stopJulianDate) break;

                    model.Update(clock.JulianDate);

                    // The curve only drives a live flight; after arrival the
                    // clock is back at real time.
                    double scale = session.Status == FlightStatus.Flying
                        ? FlightWarp.ScaleAt(flightSeconds, realElapsed)
                        : 1.0;
                    clock.SetScale(scale);
                    if (scale > PeakScale) PeakScale = scale;
                    LastScale = scale;
                    realElapsed += FrameSeconds;

                    clock.Advance((float)FrameSeconds);
                    FlightStep step = session.Advance(clock.JulianDate,
                        FlightSession.DefaultSubstepsPerFrame);

                    if (step.Substeps > MaxSubsteps) MaxSubsteps = step.Substeps;
                    if (step.CollisionDetected) Collisions++;
                    if (step.ReachedJulianDate < clock.JulianDate)
                        clock.JulianDate = step.ReachedJulianDate;
                    if (clock.JulianDate > session.State.JulianDate + 1e-9) ClockAheadFrames++;

                    model.Update(clock.JulianDate);
                    Frames++;
                }
            }
        }
    }
}
