using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.UI;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    [Category("core")]
    public class SelectionAndFocusTests
    {
        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        // ---------------------------------------------------------------- details

        [Test]
        public void KindLabelsAreReadable()
        {
            Assert.That(BodyDetails.KindLabel(_model.Get("sun")), Is.EqualTo("Star"));
            Assert.That(BodyDetails.KindLabel(_model.Get("earth")), Is.EqualTo("Planet"));
            Assert.That(BodyDetails.KindLabel(_model.Get("moon")), Is.EqualTo("Moon"));
            Assert.That(BodyDetails.KindLabel(_model.Get("ast_1")), Is.EqualTo("Asteroid"));
            Assert.That(BodyDetails.KindLabel(_model.Get("gateway")), Is.EqualTo("Station"));
        }

        [Test]
        public void EarthDetailsIncludeLivePhysicalAndOrbitRows()
        {
            BodyRuntime earth = _model.Get("earth");
            List<DetailRow> rows = BodyDetails.Build(earth, _model, earth.PositionM);

            Assert.That(Find(rows, "Distance from ship"), Is.Not.Null);
            Assert.That(Find(rows, "Radius"), Is.Not.Null);
            Assert.That(Find(rows, "Surface gravity"), Is.Not.Null);
            Assert.That(Find(rows, "Escape velocity"), Is.Not.Null);
            Assert.That(Find(rows, "Semi-major axis"), Is.Not.Null);
            Assert.That(Find(rows, "Orbital period"), Is.Not.Null);
            Assert.That(Find(rows, "Parent"), Is.Not.Null);
            Assert.That(Find(rows, "Parent")?.Value, Is.EqualTo("Sun"));
        }

        [Test]
        public void EveryBodyProducesACompleteDetailsList()
        {
            foreach (BodyRuntime body in _model.Ordered)
            {
                List<DetailRow> rows = BodyDetails.Build(body, _model, body.PositionM);
                Assert.That(rows.Count, Is.GreaterThan(5), body.Id);

                foreach (DetailRow row in rows)
                {
                    if (row.IsHeading) continue;
                    Assert.That(row.Label, Is.Not.Empty, body.Id);
                    Assert.That(row.Value, Is.Not.Empty, body.Id);
                    Assert.That(row.Value, Does.Not.Contain("NaN"), body.Id);
                    Assert.That(row.Value, Does.Not.Contain("Infinity"), body.Id);
                }
            }
        }

        [Test]
        public void EarthPhysicsMatchesReferenceValues()
        {
            BodyRuntime earth = _model.Get("earth");

            Assert.That(BodyDetails.MassKg(earth.Gm), Is.EqualTo(5.972e24).Within(1e24 * 0.02));
            Assert.That(BodyDetails.SurfaceGravityMps2(earth), Is.EqualTo(9.82).Within(0.05));
            Assert.That(BodyDetails.EscapeVelocityMps(earth), Is.EqualTo(11_186.0).Within(50.0));
        }

        [Test]
        public void LightTimeAtOneAstronomicalUnitIsAboutEightMinutes()
        {
            double seconds = BodyDetails.LightTimeSeconds(1.495978707e11);
            Assert.That(seconds, Is.EqualTo(499.0).Within(1.0));
            Assert.That(BodyDetails.FormatLightTime(seconds), Is.EqualTo("8.3 min"));
        }

        [Test]
        public void SubtitleNamesTheParentAndTheLagrangePoint()
        {
            Assert.That(BodyDetails.Subtitle(_model.Get("moon"), _model), Is.EqualTo("Moon · orbits Earth"));

            string station = BodyDetails.Subtitle(_model.Get("gateway"), _model);
            Assert.That(station, Does.Contain("Earth"));
            Assert.That(station, Does.Contain("Moon"));
            Assert.That(station, Does.Contain("L1"));
        }

        [Test]
        public void RetrogradeRotationIsLabelled()
        {
            Assert.That(BodyDetails.FormatRotation(-20_997_000.0), Does.StartWith("retrograde"));
        }

        [Test]
        public void FormatDistanceUsesSensibleUnits()
        {
            Assert.That(SolarSystemHud.FormatDistance(3.8e8), Does.EndWith("km"));
            Assert.That(SolarSystemHud.FormatDistance(3.8e8), Does.Not.Contain("million"));
            Assert.That(SolarSystemHud.FormatDistance(1.495978707e11), Does.EndWith("AU"));
        }

        // ----------------------------------------------------------------- picking

        [Test]
        public void PickingReturnsTheBodyUnderThePointer()
        {
            BodyRuntime a = _model.Get("earth");
            BodyRuntime b = _model.Get("mars");

            var targets = new List<ScreenTarget>
            {
                Target(a, new Vector2(100f, 100f), 12f, 30f),
                Target(b, new Vector2(300f, 200f), 12f, 30f)
            };

            Assert.That(BodyPicker.Pick(targets, new Vector2(300f, 200f)), Is.SameAs(b));
        }

        [Test]
        public void PickingReturnsNullInEmptySpace()
        {
            var targets = new List<ScreenTarget>
            {
                Target(_model.Get("earth"), new Vector2(100f, 100f), 12f, 30f)
            };

            Assert.That(BodyPicker.Pick(targets, new Vector2(400f, 400f)), Is.Null);
            Assert.That(BodyPicker.Pick(targets, new Vector2(400f, 400f), out int none), Is.Null);
            Assert.That(none, Is.Zero);
            Assert.That(BodyPicker.Pick(new List<ScreenTarget>(), new Vector2(1f, 1f)), Is.Null);
            Assert.That(BodyPicker.Pick(null, new Vector2(1f, 1f)), Is.Null);
        }

        [Test]
        public void TheNearestBodyWinsWithinOneRank()
        {
            BodyRuntime near = _model.Get("earth");
            BodyRuntime far = _model.Get("mars");

            var targets = new List<ScreenTarget>
            {
                Target(far, new Vector2(200f, 200f), 14f, 900f),
                Target(near, new Vector2(204f, 200f), 14f, 40f)
            };

            Assert.That(BodyPicker.Pick(targets, new Vector2(202f, 200f)), Is.SameAs(near));
        }

        [Test]
        public void TinyMarkersStillGetADecentHitRadius()
        {
            BodyRuntime asteroid = _model.Get("ast_1");
            var targets = new List<ScreenTarget> { Target(asteroid, new Vector2(50f, 50f), 0.5f, 100f) };

            Assert.That(BodyPicker.Pick(targets, new Vector2(55f, 50f)), Is.SameAs(asteroid));
            Assert.That(BodyPicker.Pick(targets, new Vector2(55f, 50f),
                minimumHitRadiusPixels: 0f), Is.Null);
        }

        [Test]
        public void ImportanceRunsStarPlanetStationMoonAsteroid()
        {
            Assert.That(BodyPicker.PriorityOf(_model.Get("sun")),
                Is.EqualTo(BodyPicker.StarBodyPriority));
            Assert.That(BodyPicker.PriorityOf(_model.Get("earth")),
                Is.EqualTo(BodyPicker.PlanetBodyPriority));
            Assert.That(BodyPicker.PriorityOf(_model.Get("gateway")),
                Is.EqualTo(BodyPicker.StationBodyPriority));
            Assert.That(BodyPicker.PriorityOf(_model.Get("moon")),
                Is.EqualTo(BodyPicker.MoonBodyPriority));
            Assert.That(BodyPicker.PriorityOf(_model.Get("ast_1")),
                Is.EqualTo(BodyPicker.AsteroidBodyPriority));
            Assert.That(BodyPicker.PriorityOf(null),
                Is.EqualTo(BodyPicker.UnknownBodyPriority));

            Assert.That(BodyPicker.StarBodyPriority, Is.GreaterThan(BodyPicker.PlanetBodyPriority));
            Assert.That(BodyPicker.PlanetBodyPriority, Is.GreaterThan(BodyPicker.StationBodyPriority));
            Assert.That(BodyPicker.StationBodyPriority, Is.GreaterThan(BodyPicker.MoonBodyPriority));
            Assert.That(BodyPicker.MoonBodyPriority, Is.GreaterThan(BodyPicker.AsteroidBodyPriority));
            Assert.That(BodyPicker.AsteroidBodyPriority, Is.GreaterThan(BodyPicker.UnknownBodyPriority));
        }

        [Test]
        public void APlanetOutranksANearerStation()
        {
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime station = _model.Get("gateway");

            // The station's marker is much nearer, but clicking a planet-shaped
            // stack is how the player expects to reach the planet first.
            var targets = new List<ScreenTarget>
            {
                Target(station, new Vector2(200f, 200f), 6f, 40f),
                Target(earth, new Vector2(200f, 200f), 6f, 900f)
            };

            Assert.That(BodyPicker.Pick(targets, new Vector2(200f, 200f)), Is.SameAs(earth));
            Assert.That(BodyPicker.Pick(targets, new Vector2(200f, 200f), out int count), Is.SameAs(earth));
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void AStationOutranksANearerMoon()
        {
            BodyRuntime station = _model.Get("gateway");
            BodyRuntime moon = _model.Get("moon");

            var targets = new List<ScreenTarget>
            {
                Target(moon, new Vector2(200f, 200f), 6f, 40f),
                Target(station, new Vector2(200f, 200f), 6f, 900f)
            };

            Assert.That(BodyPicker.Pick(targets, new Vector2(200f, 200f)), Is.SameAs(station));
        }

        [Test]
        public void AMoonSeparatedFromItsPlanetIsStillSelectable()
        {
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime moon = _model.Get("moon");

            // Zoomed into the neighbourhood the markers no longer overlap, so
            // the Moon is the only hit and rank has nothing to overrule.
            var targets = new List<ScreenTarget>
            {
                Target(moon, new Vector2(100f, 100f), 6f, 40f),
                Target(earth, new Vector2(140f, 100f), 6f, 900f)
            };

            Assert.That(BodyPicker.Pick(targets, new Vector2(100f, 100f)), Is.SameAs(moon));
        }

        [Test]
        public void OrderedHitsRunFromImportanceToDepth()
        {
            var targets = new List<ScreenTarget>
            {
                Target(_model.Get("ast_1"), new Vector2(200f, 200f), 12f, 5f),
                Target(_model.Get("moon"), new Vector2(200f, 200f), 12f, 40f),
                Target(_model.Get("gateway"), new Vector2(200f, 200f), 12f, 10f),
                Target(_model.Get("earth"), new Vector2(200f, 200f), 12f, 900f),
                Target(_model.Get("sun"), new Vector2(200f, 200f), 12f, 1e9f)
            };
            var hits = new List<ScreenTarget>();

            BodyPicker.OrderedHits(targets, new Vector2(200f, 200f), hits);

            Assert.That(hits.Count, Is.EqualTo(5));
            Assert.That(hits[0].Body.Id, Is.EqualTo("sun"));
            Assert.That(hits[1].Body.Id, Is.EqualTo("earth"));
            Assert.That(hits[2].Body.Id, Is.EqualTo("gateway"));
            Assert.That(hits[3].Body.Id, Is.EqualTo("moon"));
            Assert.That(hits[4].Body.Id, Is.EqualTo("ast_1"));
        }

        [Test]
        public void OrderedHitsBreakATieInRankByDepth()
        {
            BodyRuntime near = _model.Get("earth");
            BodyRuntime far = _model.Get("mars");
            var targets = new List<ScreenTarget>
            {
                Target(far, new Vector2(200f, 200f), 14f, 900f),
                Target(near, new Vector2(204f, 200f), 14f, 40f)
            };
            var hits = new List<ScreenTarget>();

            BodyPicker.OrderedHits(targets, new Vector2(202f, 200f), hits);

            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits[0].Body, Is.SameAs(near));
            Assert.That(hits[1].Body, Is.SameAs(far));
        }

        [Test]
        public void RepeatedClicksCycleThroughTheStackAndWrap()
        {
            var hits = Stack(_model.Get("earth"), _model.Get("gateway"), _model.Get("moon"));
            var pointer = new Vector2(200f, 200f);
            var cycle = new PickCycle();

            Assert.That(cycle.Peek(hits, pointer), Is.Zero);
            Assert.That(cycle.Advance(hits, pointer), Is.Zero);
            Assert.That(cycle.Peek(hits, pointer), Is.EqualTo(1));
            Assert.That(cycle.Advance(hits, pointer), Is.EqualTo(1));
            Assert.That(cycle.Advance(hits, pointer), Is.EqualTo(2));
            Assert.That(cycle.Advance(hits, pointer), Is.Zero, "the fourth click wraps to the top");
        }

        [Test]
        public void ASingleHitStaysAtTheTop()
        {
            var hits = Stack(_model.Get("earth"));
            var pointer = new Vector2(200f, 200f);
            var cycle = new PickCycle();

            Assert.That(cycle.Advance(hits, pointer), Is.Zero);
            Assert.That(cycle.Advance(hits, pointer), Is.Zero);
            Assert.That(cycle.Peek(hits, pointer), Is.Zero);
        }

        [Test]
        public void MovingThePointerStartsAtTheTopAgain()
        {
            Assert.That(PickCycle.PointerSlopPixels, Is.EqualTo(PlayerControls.ClickSlopPixels),
                "the cycle must use the same slop as click-versus-drag");

            var hits = Stack(_model.Get("earth"), _model.Get("gateway"), _model.Get("moon"));
            var cycle = new PickCycle();

            Assert.That(cycle.Advance(hits, new Vector2(200f, 200f)), Is.Zero);
            Assert.That(cycle.Advance(hits, new Vector2(201f, 200f)), Is.EqualTo(1));

            // Four pixels is still the same spot; a larger move starts a new one.
            Assert.That(cycle.Peek(hits, new Vector2(205f, 200f)), Is.EqualTo(2));
            Assert.That(cycle.Peek(hits, new Vector2(206f, 200f)), Is.Zero);
            Assert.That(cycle.Advance(hits, new Vector2(206f, 200f)), Is.Zero);
        }

        [Test]
        public void AChangedStackStartsAtTheTopAgain()
        {
            BodyRuntime earth = _model.Get("earth");
            BodyRuntime station = _model.Get("gateway");
            var pointer = new Vector2(200f, 200f);
            var cycle = new PickCycle();

            Assert.That(cycle.Advance(Stack(earth, station), pointer), Is.Zero);
            Assert.That(cycle.Advance(Stack(earth, station), pointer), Is.EqualTo(1));

            // The camera moved and the station is no longer under the pointer.
            Assert.That(cycle.Advance(Stack(earth), pointer), Is.Zero);
        }

        [Test]
        public void ClickingEmptySpaceResetsTheCycle()
        {
            var hits = Stack(_model.Get("earth"), _model.Get("gateway"));
            var pointer = new Vector2(200f, 200f);
            var cycle = new PickCycle();
            var empty = new List<ScreenTarget>();

            Assert.That(cycle.Advance(hits, pointer), Is.Zero);
            Assert.That(cycle.Advance(hits, pointer), Is.EqualTo(1));

            Assert.That(cycle.Advance(empty, pointer), Is.EqualTo(-1));
            Assert.That(cycle.Peek(hits, pointer), Is.Zero);
            Assert.That(cycle.Advance(hits, pointer), Is.Zero);
        }

        private static List<ScreenTarget> Stack(params BodyRuntime[] bodies)
        {
            var hits = new List<ScreenTarget>();
            for (int i = 0; i < bodies.Length; i++)
                hits.Add(Target(bodies[i], new Vector2(200f, 200f), 12f, 10f * (i + 1)));
            return hits;
        }

        private static ScreenTarget Target(BodyRuntime body, Vector2 centre, float radius, float depth) =>
            new ScreenTarget
            {
                Body = body,
                Centre = centre,
                RadiusPixels = radius,
                Depth = depth
            };

        // -------------------------------------------------------------- transition

        [Test]
        public void TransitionStartsAtTheRecordedPose()
        {
            var transition = new FocusTransition { DurationSeconds = 3f };
            var start = new Vector3d(1e9, -2e9, 3e9);

            transition.Begin(start);

            Assert.That(transition.IsActive, Is.True);
            Assert.That(transition.Progress, Is.EqualTo(0f));
            Assert.That(transition.EasedProgress, Is.EqualTo(0f));
            MathAssert.AreClose(transition.PositionAt(new Vector3d(5e9, 5e9, 5e9)), start, 1e-6);
        }

        [Test]
        public void TransitionArrivesAtTheEndAfterTheDuration()
        {
            var transition = new FocusTransition { DurationSeconds = 3f };
            var start = Vector3d.Zero;
            var end = new Vector3d(300.0, 0.0, 0.0);
            transition.Begin(start);

            Assert.That(transition.Advance(1.5f), Is.False);
            Assert.That(transition.Advance(1.5f), Is.True);
            Assert.That(transition.IsActive, Is.False);
            Assert.That(transition.Progress, Is.EqualTo(1f).Within(1e-5f));
            MathAssert.AreClose(transition.PositionAt(end), end, 1e-6);

            // Further steps do nothing once finished.
            Assert.That(transition.Advance(10f), Is.False);
            Assert.That(transition.Progress, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void TransitionIsHalfwayAtHalfTime()
        {
            var transition = new FocusTransition { DurationSeconds = 3f };
            var end = new Vector3d(300.0, 0.0, 0.0);
            transition.Begin(Vector3d.Zero);

            transition.Advance(1.5f);

            Assert.That(transition.Progress, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(transition.EasedProgress, Is.EqualTo(0.5f).Within(1e-5f));
            MathAssert.AreClose(transition.PositionAt(end), new Vector3d(150.0, 0.0, 0.0), 1e-6);
        }

        [Test]
        public void EaseAcceleratesThenDeceleratesAndNeverOvershoots()
        {
            Assert.That(FocusTransition.Ease(0f), Is.EqualTo(0f));
            Assert.That(FocusTransition.Ease(1f), Is.EqualTo(1f));
            Assert.That(FocusTransition.Ease(0.1f), Is.LessThan(0.1f));
            Assert.That(FocusTransition.Ease(0.9f), Is.GreaterThan(0.9f));

            float previous = -1f;
            for (int i = 0; i <= 100; i++)
            {
                float value = FocusTransition.Ease(i / 100f);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous));
                previous = value;
            }

            float startSpeed = FocusTransition.Ease(0.01f) - FocusTransition.Ease(0f);
            float midSpeed = FocusTransition.Ease(0.51f) - FocusTransition.Ease(0.5f);
            float endSpeed = FocusTransition.Ease(1f) - FocusTransition.Ease(0.99f);

            Assert.That(startSpeed, Is.LessThan(midSpeed));
            Assert.That(endSpeed, Is.LessThan(midSpeed));
        }

        [Test]
        public void CancelStopsTheTransitionWhereItIs()
        {
            var transition = new FocusTransition { DurationSeconds = 3f };
            transition.Begin(Vector3d.Zero);
            transition.Advance(1.5f);
            float eased = transition.EasedProgress;

            transition.Cancel();

            Assert.That(transition.IsActive, Is.False);
            Assert.That(transition.EasedProgress, Is.EqualTo(eased));
            Assert.That(transition.Advance(2f), Is.False);
            Assert.That(transition.EasedProgress, Is.EqualTo(eased));
        }

        [Test]
        public void ZeroDurationFinishesOnTheNextStep()
        {
            var transition = new FocusTransition { DurationSeconds = 0f };
            transition.Begin(Vector3d.Zero);

            Assert.That(transition.Advance(0f), Is.True);
            Assert.That(transition.IsActive, Is.False);
            Assert.That(transition.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void ApproachOffsetStopsEightRadiiOut()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d offset = CameraFocusDirector.ApproachOffset(earth, _model.Sun);

            Assert.That(offset.Magnitude, Is.EqualTo(earth.RadiusM * 8.0).Within(earth.RadiusM * 0.001));
        }

        [Test]
        public void ApproachOffsetHasAFiveThousandKilometreFloor()
        {
            BodyRuntime gateway = _model.Get("gateway");
            Vector3d offset = CameraFocusDirector.ApproachOffset(gateway, _model.Sun);

            Assert.That(offset.Magnitude, Is.EqualTo(5.0e6).Within(1.0));
        }

        [Test]
        public void ApproachOffsetSitsOnTheLitSide()
        {
            BodyRuntime earth = _model.Get("earth");
            Vector3d toSun = (_model.Sun.PositionM - earth.PositionM).Normalized;
            Vector3d offset = CameraFocusDirector.ApproachOffset(earth, _model.Sun);

            Assert.That(Vector3d.Dot(offset.Normalized, toSun), Is.GreaterThan(0.3));
        }

        // ------------------------------------------------------------------ helper

        private static DetailRow? Find(List<DetailRow> rows, string label)
        {
            foreach (DetailRow row in rows)
            {
                if (!row.IsHeading && row.Label == label) return row;
            }

            return null;
        }
    }
}
