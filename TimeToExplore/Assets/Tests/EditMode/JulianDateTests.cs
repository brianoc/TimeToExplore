using System;
using NUnit.Framework;
using TimeToExplore.Core;

namespace TimeToExplore.Tests.EditMode
{
    public class JulianDateTests
    {
        [Test]
        public void GameStartIsTheDocumentedEpoch()
        {
            var epoch = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            Assert.That(JulianDate.FromUtc(epoch), Is.EqualTo(2461359.5).Within(1e-9));
        }

        [Test]
        public void J2000IsTheDefinedNoonConstant()
        {
            var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(JulianDate.FromUtc(j2000), Is.EqualTo(JulianDate.J2000).Within(1e-9));
        }

        [Test]
        public void UtcRoundTripsThroughJulianDate()
        {
            var utc = new DateTime(2031, 7, 4, 6, 30, 15, DateTimeKind.Utc);
            Assert.That(JulianDate.ToUtc(JulianDate.FromUtc(utc)), Is.EqualTo(utc));
        }

        [Test]
        public void FormattingIsUtcAndUnambiguous()
        {
            const double jd = 2461359.5;
            Assert.That(JulianDate.FormatShort(jd), Is.EqualTo("2026-11-15 00:00"));
            Assert.That(JulianDate.Format(jd), Is.EqualTo("2026-11-15 00:00:00 UTC"));
        }

        [Test]
        public void DaysSinceUsesJulianDays()
        {
            Assert.That(JulianDate.DaysSince(2461360.5, 2461359.5), Is.EqualTo(1.0));
        }

        [Test]
        public void SecondsPerDayIsAstronomical()
        {
            Assert.That(JulianDate.SecondsPerDay, Is.EqualTo(86400.0));
        }
    }
}
