using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using TimeToExplore.UI;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The flight panel's content, verified without a scene.</summary>
    [Category("deep")]
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
            BeginAtALongerTrip(_session, _epoch);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x10,000");

            Assert.That(Find(rows, "Target")?.Value, Is.EqualTo("Mars"));
            Assert.That(Find(rows, "Departure"), Is.Not.Null);
            Assert.That(Find(rows, "Cruise"), Is.Not.Null);
            Assert.That(Find(rows, "Capture"), Is.Not.Null);
            Assert.That(Find(rows, "Arrival")?.Value,
                Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
            Assert.That(Find(rows, "Peak thrust"), Is.Null,
                "the profile reports its burns, not a torch peak");
            Assert.That(Find(rows, "Departure burn")?.Value, Does.Contain("km/s"));
            Assert.That(Find(rows, "Brake")?.Value, Does.Contain("km/s"));
            Assert.That(rows.Any(row => row.Label == "Coast" &&
                                        row.Value.StartsWith("drive off")), Is.True,
                "the profile's coast reports the drive off");
            Assert.That(Find(rows, "Delta-v")?.Value, Does.Contain("km/s"));
            Assert.That(Find(rows, "Fuel")?.Value, Does.Contain("t"));
            Assert.That(Find(rows, "Fuel")?.Value, Does.Contain("% of departure mass"));
            Assert.That(Find(rows, "Flight time"), Is.Not.Null);
            Assert.That(Find(rows, "Warp")?.Value, Is.EqualTo("x10,000"));
        }

        [Test]
        public void ALiveFlightReportsThrottleAndTheNextEvent()
        {
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            // The beginning flies the burn-only minimum, whose first ten days are
            // the break and the ballistic escape: advance into the departure burn
            // to see a commanded throttle.
            PlanArc burn = plan.Arcs.First(arc => arc.IsFiniteBurn);
            _session.Advance(burn.StartJulianDate + 0.01, 50_000);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x10,000");

            Assert.That(Find(rows, "Status")?.Value, Is.EqualTo("Under way"));
            Assert.That(Find(rows, "Phase")?.Value, Is.Not.Empty);
            Assert.That(Find(rows, "Throttle")?.Value, Does.Contain("%"));
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
            Assert.That(Find(rows, "Throttle"), Is.Null, "the drive is off, so there is no throttle row");
        }

        [Test]
        public void APreviewReportsItsPlanAndTheTimeLeftBeforeItGoesStale()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000", _epoch);

            Assert.That(Find(rows, "Target")?.Value, Is.EqualTo("Mars"));
            Assert.That(Find(rows, "Status")?.Value, Is.EqualTo("Preview"));
            Assert.That(Find(rows, "Plotted")?.Value, Does.StartWith("T−"));
            Assert.That(Find(rows, "Arrival")?.Value,
                Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
            Assert.That(Find(rows, "Flight time"), Is.Not.Null);
            Assert.That(Find(rows, "Delta-v")?.Value, Does.Contain("km/s"));
            Assert.That(Find(rows, "Fuel")?.Value, Does.Contain("% of departure mass"));
            Assert.That(Find(rows, "Throttle"), Is.Null, "a preview is not flying");
            Assert.That(Find(rows, "Note"), Is.Null);

            Assert.That(FlightReport.StatusLabel(_session, _epoch), Is.EqualTo("Preview"));
            Assert.That(FlightReport.PreviewNextEvent(_session, _epoch),
                Does.StartWith("Arrival  T−"));
            Assert.That(preview.FlightTimeDays, Is.GreaterThan(0.0));
        }

        [Test]
        public void ARetimedPreviewReportsTheLongerFlightTime()
        {
            TrajectoryPlan fastest = _session.Plot("mars", _epoch);
            string fastestFuel = FlightReport.FuelLabel(fastest);
            double chosen = 2.0 * _session.PreviewMinimumSeconds;
            _session.SetPreviewDuration(chosen);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000", _epoch);

            Assert.That(Find(rows, "Flight time")?.Value,
                Is.EqualTo(SolarSystemHud.FormatDuration(chosen)));
            Assert.That(Find(rows, "Arrival")?.Value,
                Is.Not.EqualTo(JulianDate.FormatShort(fastest.ArrivalJulianDate)),
                "the arrival date slides with the chosen time");
            Assert.That(Find(rows, "Departure burn")?.Value, Does.Contain("km/s"));
            Assert.That(Find(rows, "Fuel")?.Value, Is.Not.EqualTo(fastestFuel),
                "a longer trip burns less fuel, so the readout moves");
            Assert.That(_session.Preview.PropellantKg,
                Is.LessThan(fastest.PropellantKg));
        }

        [Test]
        public void AStalePreviewIsFlaggedButStillReportsItsPlan()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            double staleDate = _epoch + 1.01 * FlightSession.PreviewStaleFraction *
                                       preview.FlightTimeDays;

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000", staleDate);

            Assert.That(Find(rows, "Status")?.Value, Is.EqualTo("Preview, stale"));
            Assert.That(Find(rows, "Plotted")?.Value, Does.StartWith("T+"));
            Assert.That(Find(rows, "Note")?.Value, Does.Contain("stale"));
            Assert.That(Find(rows, "Target")?.Value, Is.EqualTo("Mars"));
            Assert.That(FlightReport.StatusLabel(_session, staleDate), Is.EqualTo("Preview, stale"));
        }

        [Test]
        public void AConicArrivalShowsCoastParkingAndTheImpulsiveBurns()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.Conic
            };
            var session = new FlightSession(_model, _gravity, planner);
            session.PlaceAt("gateway", _epoch);
            BeginAtALongerTrip(session, _epoch);

            List<DetailRow> rows = FlightReport.Build(session, _model, "x10,000");

            Assert.That(Find(rows, "Coast"), Is.Not.Null);
            Assert.That(Find(rows, "Capture"), Is.Not.Null);
            Assert.That(Find(rows, "Parking"), Is.Not.Null);
            Assert.That(Find(rows, "Departure burn")?.Value, Does.Contain("0.3 g"));
            Assert.That(Find(rows, "Brake")?.Value, Does.Contain("0.3 g"));
            Assert.That(Find(rows, "Peak thrust"), Is.Null);
            Assert.That(Find(rows, "Delta-v")?.Value, Does.Contain("km/s"));
        }

        [Test]
        public void AHybridArrivalShowsTheFixedBurnAndTheTrimSplit()
        {
            var planner = new TripPlanner(_model, _gravity)
            {
                ArrivalStyle = CaptureStyle.FiniteBurn
            };
            var session = new FlightSession(_model, _gravity, planner);
            session.PlaceAt("gateway", _epoch);
            BeginAtALongerTrip(session, _epoch);

            List<DetailRow> rows = FlightReport.Build(session, _model, "x10,000");

            Assert.That(Find(rows, "Coast"), Is.Not.Null);
            Assert.That(Find(rows, "Capture"), Is.Not.Null);
            Assert.That(Find(rows, "Parking"), Is.Not.Null);
            Assert.That(Find(rows, "Departure burn")?.Value, Does.Contain("0.3 g"));
            Assert.That(Find(rows, "Brake")?.Value, Does.Contain("0.3 g"));
            Assert.That(Find(rows, "Peak thrust"), Is.Null);
            Assert.That(Find(rows, "Delta-v")?.Value, Does.Contain("km/s"));
        }

        /// <summary>
        /// A station plan has no capture chain and its one terminal finite burn
        /// is a rendezvous velocity match, so the phase row and the drive
        /// summary both read <b>Match</b>, never <b>Capture</b> or
        /// <b>Brake</b>, and the chain row is absent.
        /// </summary>
        [Test]
        public void AStationPlanShowsAVelocityMatchAndNoCaptureChain()
        {
            _session.PlaceAt("ares_junction", _epoch);
            TrajectoryPlan plan = _session.Begin("gateway", _epoch);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x10,000");

            Assert.That(Find(rows, "Target")?.Value, Is.EqualTo("Gateway Station"));
            Assert.That(Find(rows, "Capture"), Is.Null,
                "a station's terminal phase and chain row are both the match");
            Assert.That(Find(rows, "Brake"), Is.Null,
                "a rendezvous may add speed; the burn is not a retrograde brake");

            List<DetailRow> matches = rows
                .Where(row => !row.IsHeading && row.Label == "Match")
                .ToList();
            Assert.That(matches.Count, Is.EqualTo(2),
                "the phase row and the drive row both say Match");
            Assert.That(matches[0].Value, Does.Contain("match"),
                "the terminal arc's drive label is the match");
            Assert.That(matches[1].Value, Does.Contain("km/s"),
                "the drive row reports the burn's cost");

            Assert.That(Find(rows, "Approach"), Is.Null);
            Assert.That(Find(rows, "Parking"), Is.Null);
            Assert.That(Find(rows, "Cruise")?.Value, Does.Contain("prograde burn"),
                "the heliocentric departure burn is still the cruise burn");
            Assert.That(Find(rows, "Delta-v")?.Value, Does.Contain("km/s"));
        }

        /// <summary>
        /// A station's arrival is the hold point, not an orbit: the header and
        /// the phase row say so, and the countdown lands on the arrival with no
        /// parking orbit to report.
        /// </summary>
        [Test]
        public void AnArrivedStationShipReportsTheHoldPoint()
        {
            _session.PlaceAt("ares_junction", _epoch);
            TrajectoryPlan plan = _session.Begin("gateway", _epoch);
            _session.Advance(plan.ArrivalJulianDate, 50_000);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000");

            Assert.That(Find(rows, "Status")?.Value,
                Is.EqualTo("Arrived, at the hold point"));
            Assert.That(Find(rows, "Phase")?.Value, Is.EqualTo("Match"));
            Assert.That(Find(rows, "Next event")?.Value, Is.EqualTo("Arrived"));
            Assert.That(Find(rows, "Throttle"), Is.Null,
                "the drive is off, so there is no throttle row");
        }

        /// <summary>
        /// Before a station's terminal match the countdown names the match, not
        /// a capture. Approaching the last arc's start means every boundary the
        /// countdown can land on from here is that arc's.
        /// </summary>
        [Test]
        public void AStationFlightsNextEventIsTheMatch()
        {
            _session.PlaceAt("ares_junction", _epoch);
            TrajectoryPlan plan = _session.Begin("gateway", _epoch);
            PlanArc match = plan.Arcs[plan.Arcs.Count - 1];
            Assert.That(match.Phase, Is.EqualTo(FlightPhase.Capture));

            _session.Advance(match.StartJulianDate - 0.01, 50_000);

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000");

            Assert.That(Find(rows, "Next event")?.Value, Does.StartWith("Match  T−"));
            Assert.That(Find(rows, "Capture"), Is.Null,
                "the station phase table never says Capture");
        }

        /// <summary>
        /// Begin consumes the preview and flies it, so the panel must show the
        /// plan it is flying — the full-blast minimum that was drawn — not the
        /// coast floor the old Begin built.
        /// </summary>
        [Test]
        public void TheFuelReadoutFollowsTheFlyingPlanAfterBegin()
        {
            TrajectoryPlan preview = _session.Plot("mars", _epoch);
            TrajectoryPlan plan = _session.Begin("mars", _epoch);
            var planner = new TripPlanner(_model, _gravity);
            TrajectoryPlan coastFloor = planner.Plan(
                _session.State.WithJulianDate(_epoch), "mars");

            List<DetailRow> rows = FlightReport.Build(_session, _model, "x1,000");
            string fuel = Find(rows, "Fuel")?.Value;

            TestContext.WriteLine($"flying fuel {fuel}; " +
                                  $"plan {plan.PropellantKg / 1000.0:F1} t, " +
                                  $"coast floor {coastFloor.PropellantKg / 1000.0:F1} t");

            Assert.That(plan.Arcs.Any(arc => arc.Phase == FlightPhase.Cruise && arc.IsCoast),
                Is.False, "Begin flies the no-coast full-blast plan the preview drew");
            Assert.That(plan.FlightTimeSeconds,
                Is.EqualTo(preview.FlightTimeSeconds).Within(1e-6));
            Assert.That(plan.PropellantKg, Is.EqualTo(preview.PropellantKg).Within(1.0),
                "the flight and the preview are the same plan");
            Assert.That(plan.PropellantKg,
                Is.GreaterThan(coastFloor.PropellantKg),
                "the full-blast minimum buys its time with fuel");
            Assert.That(fuel, Is.EqualTo(FlightReport.FuelLabel(plan)),
                "the panel's fuel row follows the flying plan, not the old preview or floor");
            Assert.That(fuel, Is.Not.EqualTo(FlightReport.FuelLabel(coastFloor)));
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

        /// <summary>Begin a plan long enough to leave the zero-coast minimum, so
        /// the coast, approach, and a chosen capture style are visible in the
        /// flown rows. The preview is retimed first, and Begin flies it.</summary>
        private static TrajectoryPlan BeginAtALongerTrip(FlightSession session, double epoch)
        {
            session.Plot("mars", epoch);
            session.SetPreviewDuration(2.0 * session.PreviewMinimumSeconds);
            return session.Begin("mars", epoch);
        }

        private static DetailRow? Find(List<DetailRow> rows, string label)
        {
            foreach (DetailRow row in rows)
                if (!row.IsHeading && row.Label == label) return row;
            return null;
        }
    }
}
