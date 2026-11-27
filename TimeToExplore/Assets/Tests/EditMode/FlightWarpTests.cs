using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The continuous warp curve that paces a flight.</summary>
    [Category("flight")]
    public class FlightWarpTests
    {
        private const double Day = JulianDate.SecondsPerDay;

        [Test]
        public void TheJourneyFeelMatchesTheDesignTargets()
        {
            // Earth–Mars: the fastest crossing is about a week, the slow end
            // about three weeks. Saturn is around a month to two months.
            Assert.That(FlightWarp.TargetRealSeconds(6.67 * Day),
                Is.EqualTo(20.0).Within(1.0));
            Assert.That(FlightWarp.TargetRealSeconds(21.5 * Day),
                Is.EqualTo(40.0).Within(2.0));
            Assert.That(FlightWarp.TargetRealSeconds(20.0 * Day),
                Is.EqualTo(39.0).Within(2.0));
            Assert.That(FlightWarp.TargetRealSeconds(60.0 * Day),
                Is.EqualTo(57.0).Within(2.0));
            Assert.That(FlightWarp.TargetRealSeconds(111.0 * Day),
                Is.EqualTo(FlightWarp.MaxRealSeconds));
        }

        [Test]
        public void TheTargetIsClampedAtBothEnds()
        {
            Assert.That(FlightWarp.TargetRealSeconds(0.1 * Day),
                Is.EqualTo(FlightWarp.MinRealSeconds));
            Assert.That(FlightWarp.TargetRealSeconds(0.0),
                Is.EqualTo(FlightWarp.MinRealSeconds));
            Assert.That(FlightWarp.TargetRealSeconds(500.0 * Day),
                Is.EqualTo(FlightWarp.MaxRealSeconds));
        }

        [Test]
        public void TheCurveStartsAndEndsAtRealTime()
        {
            double flight = 6.67 * Day;
            double target = FlightWarp.TargetRealSeconds(flight);

            Assert.That(FlightWarp.ScaleAt(flight, 0.0), Is.EqualTo(1.0));
            Assert.That(FlightWarp.ScaleAt(flight, target),
                Is.EqualTo(1.0).Within(1e-9));

            // Still close to real time a moment in, so the camera can settle.
            Assert.That(FlightWarp.ScaleAt(flight, 0.5),
                Is.LessThan(0.08 * FlightWarp.CruiseScale(flight)));
        }

        [Test]
        public void TheEndsClimbSlowlyAndTheMiddleHoldsCruise()
        {
            double flight = 6.67 * Day;
            double target = FlightWarp.TargetRealSeconds(flight);
            double cruise = FlightWarp.CruiseScale(flight);

            // A long, quartic climb: the first few seconds stay near real time
            // so the departure and capture have room to play.
            Assert.That(FlightWarp.ScaleAt(flight, 1.0), Is.LessThan(0.01 * cruise));
            Assert.That(FlightWarp.ScaleAt(flight, 2.0), Is.LessThan(0.01 * cruise));
            Assert.That(FlightWarp.ScaleAt(flight, 3.0), Is.LessThan(0.05 * cruise));
            Assert.That(FlightWarp.ScaleAt(flight, target - 1.0),
                Is.LessThan(0.01 * cruise), "the arrival end is symmetric");
        }

        [Test]
        public void TheCurveRisesToCruiseInTheMiddle()
        {
            double flight = 6.67 * Day;
            double target = FlightWarp.TargetRealSeconds(flight);
            double cruise = FlightWarp.CruiseScale(flight);

            Assert.That(FlightWarp.ScaleAt(flight, 0.5 * target),
                Is.EqualTo(cruise).Within(cruise * 1e-9));
        }

        /// <summary>
        /// The whole point: integrating the curve over the real duration must
        /// advance the flight by roughly its simulated length. Each quartic ramp
        /// covers a fifth of its span, so the closed form cancels the ramps. A
        /// 60 fps walk of the curve checks the result; the left-edge integration
        /// over a falling end makes a long trip land a little early.
        /// </summary>
        [TestCase(6.67)]
        [TestCase(21.5)]
        [TestCase(60.0)]
        [TestCase(111.0)]
        public void IntegratingTheCurveFliesTheWholeTripInTheTargetTime(double days)
        {
            double flight = days * Day;
            double target = FlightWarp.TargetRealSeconds(flight);
            double frame = 1.0 / 60.0;

            double elapsed = 0.0;
            double real = 0.0;
            while (elapsed < flight && real < target * 2.0)
            {
                elapsed += FlightWarp.ScaleAt(flight, real) * frame;
                real += frame;
            }

            TestContext.WriteLine($"{days:F2} d: {real:F2} real s (target {target:F2} s), " +
                                  $"cruise {FlightWarp.CruiseScale(flight):N0}x");

            Assert.That(elapsed, Is.GreaterThanOrEqualTo(flight),
                "the trip must finish");
            Assert.That(real, Is.EqualTo(target).Within(Math.Max(0.5, 0.04 * target)),
                "and land on the target real duration");
        }

        [Test]
        public void TheScaleNeverExceedsCruise()
        {
            double flight = 21.5 * Day;
            double cruise = FlightWarp.CruiseScale(flight);

            for (double real = 0.0; real < 200.0; real += 0.25)
                Assert.That(FlightWarp.ScaleAt(flight, real),
                    Is.LessThanOrEqualTo(cruise + 1e-9));

            Assert.That(cruise, Is.GreaterThan(1.0));
        }
    }
}
