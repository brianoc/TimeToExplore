using System;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    public class SimulationClockTests
    {
        [Test]
        public void OneRealSecondAtOneXAdvancesOneSimulatedSecond()
        {
            var clock = new SimulationClock { JulianDate = 2461359.5 };
            clock.Advance(1.0f);

            Assert.That(clock.JulianDate,
                Is.EqualTo(2461359.5 + 1.0 / JulianDate.SecondsPerDay).Within(1e-12));
        }

        [Test]
        public void WarpMultipliesTheAdvance()
        {
            var clock = new SimulationClock { JulianDate = 0.0 };
            clock.SetScaleIndex(3); // 1,000x
            clock.Advance(2.0f);

            Assert.That(clock.JulianDate,
                Is.EqualTo(2000.0 / JulianDate.SecondsPerDay).Within(1e-12));
        }

        [Test]
        public void PausingStopsTimeAndUnpausingRestoresIt()
        {
            var clock = new SimulationClock { JulianDate = 10.0 };
            clock.TogglePause();
            clock.Advance(5.0f);

            Assert.That(clock.Paused, Is.True);
            Assert.That(clock.JulianDate, Is.EqualTo(10.0));
            Assert.That(clock.Label, Is.EqualTo("PAUSED"));

            clock.TogglePause();
            clock.Advance(1.0f);
            Assert.That(clock.JulianDate, Is.GreaterThan(10.0));
        }

        [Test]
        public void ScaleIndexIsClampedAndUnpauses()
        {
            var clock = new SimulationClock();
            clock.SetScaleIndex(999);
            Assert.That(clock.ScaleIndex, Is.EqualTo(SimulationClock.TimeScales.Length - 1));
            Assert.That(clock.Paused, Is.False);

            clock.Paused = true;
            clock.SetScaleIndex(-10);
            Assert.That(clock.ScaleIndex, Is.EqualTo(0));
            Assert.That(clock.Paused, Is.False);
        }

        [Test]
        public void ScalesArePositiveAndStrictlyIncreasing()
        {
            double previous = 0.0;
            foreach (double scale in SimulationClock.TimeScales)
            {
                Assert.That(scale, Is.GreaterThan(previous));
                previous = scale;
            }
        }

        [Test]
        public void OneXLabelIsReadable()
        {
            var clock = new SimulationClock();
            Assert.That(clock.Label, Is.EqualTo("1x"));
        }

        [Test]
        public void ZeroDeltaTimeDoesNotMoveTheClock()
        {
            var clock = new SimulationClock { JulianDate = 100.0 };
            clock.Advance(0.0f);
            Assert.That(clock.JulianDate, Is.EqualTo(100.0));
        }

        [Test]
        public void TimeScaleReportsZeroWhilePaused()
        {
            var clock = new SimulationClock();
            clock.SetScaleIndex(6);
            Assert.That(clock.TimeScale, Is.EqualTo(1_000_000.0));
            clock.TogglePause();
            Assert.That(clock.TimeScale, Is.EqualTo(0.0));
        }
    }
}
