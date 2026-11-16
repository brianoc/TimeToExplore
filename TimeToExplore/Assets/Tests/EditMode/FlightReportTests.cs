using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using TimeToExplore.UI;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The flight panel's content, verified without a scene.</summary>
    public class FlightReportTests
    {
        private SolarSystemModel _model;
        private GravityField _gravity;
        private FlightSession _session;
        private double _epoch;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _gravity = FlightFixture.FullField(_model);
            _session = new FlightSession(_model, _gravity, new TripPlanner(_model, _gravity));
            _epoch = _model.CurrentJulianDate;
            _session.PlaceAt("gateway", _epoch);
        }

        [Test]
        public void ThePhaseTableHasEveryRowTheDesignAsksFor()
        {
            _session.Begin("mars", _epoch);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x10,000");

            Assert.That(Find(rows, "Target")?.Value, Is.EqualTo("Mars"));
            Assert.That(Find(rows, "Departure"), Is.Not.Null);
            Assert.That(Find(rows, "Cruise"), Is.Not.Null);
            Assert.That(Find(rows, "Capture"), Is.Not.Null);
            Assert.That(Find(rows, "Arrival")?.Value,
                Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
            Assert.That(Find(rows, "Peak thrust")?.Value, Does.Contain("m/s²"));
            Assert.That(Find(rows, "Peak thrust")?.Value, Does.Contain("%"));
            Assert.That(Find(rows, "Delta-v")?.Value, Does.Contain("km/s"));
            Assert.That(Find(rows, "Flight time"), Is.Not.Null);
            Assert.That(Find(rows, "Warp")?.Value, Is.EqualTo("x10,000"));
        }

        [Test]
        public void ALiveFlightReportsThrottleAndTheNextEvent()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            _session.Advance(_epoch + 0.05, 50_000);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x10,000");

            Assert.That(Find(rows, "Status")?.Value, Is.EqualTo("Under way"));
            Assert.That(Find(rows, "Phase")?.Value, Is.Not.Empty);
            Assert.That(Find(rows, "Throttle")?.Value, Does.Contain("%"));
            Assert.That(Find(rows, "Flip")?.Value, Does.StartWith("T"));
            Assert.That(Find(rows, "Next event")?.Value, Is.Not.Empty);

            Assert.That(_session.Plan.ArrivalJulianDate, Is.EqualTo(plan.ArrivalJulianDate));
        }

        [Test]
        public void AnArrivedShipReportsTheOrbitAndNoBurn()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            _session.Advance(plan.ArrivalJulianDate, 50_000);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000");

            Assert.That(Find(rows, "Status")?.Value, Is.EqualTo("Arrived, in orbit"));
            Assert.That(Find(rows, "Next event")?.Value, Is.EqualTo("Arrived"));
            Assert.That(Find(rows, "Flip")?.Value, Is.EqualTo("passed"));
            Assert.That(Find(rows, "Throttle"), Is.Null, "the drive is off, so there is no throttle row");
        }

        [Test]
        public void EveryStatusHasAReadableLabel()
        {
            Assert.That(FlightReport.StatusLabel(FlightStatus.Idle), Is.EqualTo("Idle"));
            Assert.That(FlightReport.StatusLabel(FlightStatus.Flying), Is.EqualTo("Under way"));
            Assert.That(FlightReport.StatusLabel(FlightStatus.Arrived), Is.EqualTo("Arrived, in orbit"));
            Assert.That(FlightReport.StatusLabel(FlightStatus.Aborted), Is.EqualTo("Aborted"));
            Assert.That(FlightReport.StatusLabel(FlightStatus.Rejected), Is.EqualTo("Plan rejected"));
        }

        [Test]
        public void NoPlanMeansNoFlightPanel()
        {
            Assert.That(FlightReport.Build(_session, _model, "x1"), Is.Empty);
        }

        [Test]
        public void CountdownsReadNaturally()
        {
            Assert.That(FlightReport.FormatCountdown(45.0), Is.EqualTo("T− 45 s"));
            Assert.That(FlightReport.FormatCountdown(125.0), Is.EqualTo("T− 2m 05s"));
            Assert.That(FlightReport.FormatCountdown(3.0 * 3600.0 + 7.0 * 60.0),
                Is.EqualTo("T− 3h 07m"));
            Assert.That(FlightReport.FormatCountdown(2.5 * JulianDate.SecondsPerDay),
                Is.EqualTo("T− 2.50 d"));
            Assert.That(FlightReport.FormatCountdown(-5.0), Is.EqualTo("T+ 5 s"));
            Assert.That(FlightReport.FormatCountdown(double.NaN), Is.EqualTo("—"));
        }

        private static DetailRow? Find(List<DetailRow> rows, string label)
        {
            foreach (DetailRow row in rows)
                if (!row.IsHeading && row.Label == label) return row;
            return null;
        }
    }
}
