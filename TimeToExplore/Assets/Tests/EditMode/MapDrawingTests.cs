using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using TimeToExplore.UI;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The map's reading rules: marker size, label density, ship label, line width.</summary>
    public class MapDrawingTests
    {
        private const float UnitsPerMeter = 1e-6f;
        private const double CloseCameraM = 2.0e6; // 2,000 km, inside the Moon's SOI

        private SolarSystemModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
        }

        // ------------------------------------------------------------ marker size

        [Test]
        public void TheLocalMarkerMinimumStaysAtFourAndAHalfPixels()
        {
            Assert.That(MarkerSizing.MinimumPixels(0f), Is.EqualTo(4.5f).Within(1e-6f));
            Assert.That(MarkerSizing.MinimumPixels(-1f), Is.EqualTo(4.5f).Within(1e-6f));
        }

        [Test]
        public void TheMapMarkerMinimumIsSixPixels()
        {
            Assert.That(MarkerSizing.MinimumPixels(1f), Is.EqualTo(6f).Within(1e-6f));
            Assert.That(MarkerSizing.MinimumPixels(2f), Is.EqualTo(6f).Within(1e-6f));
        }

        [Test]
        public void TheMarkerMinimumGrowsSmoothlyWithTheBlend()
        {
            Assert.That(MarkerSizing.MinimumPixels(0.5f), Is.EqualTo(5.25f).Within(1e-5f));

            float previous = -1f;
            for (int i = 0; i <= 100; i++)
            {
                float value = MarkerSizing.MinimumPixels(i / 100f);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous));
                previous = value;
            }
        }

        [Test]
        public void TrueScaleBypassesTheMarkerAid()
        {
            Assert.That(BodyView.TargetScaleFactor(1e-9f, 1e-4f, trueScale: true),
                Is.EqualTo(1f));
            Assert.That(BodyView.TargetScaleFactor(1e-9f, 1e-4f, trueScale: false),
                Is.GreaterThan(1f));
            Assert.That(BodyView.TargetScaleFactor(1e-4f, 1e-4f, trueScale: false),
                Is.EqualTo(1f), "a body already at the minimum is not inflated");
        }

        [Test]
        public void TheMinimumAngularSizeFollowsTheViewport()
        {
            float at1080 = MarkerSizing.MinimumAngularSize(4.5f, 60f, 1080);
            float at2160 = MarkerSizing.MinimumAngularSize(4.5f, 60f, 2160);

            Assert.That(at1080, Is.GreaterThan(0f));
            Assert.That(at2160, Is.EqualTo(at1080 * 0.5f).Within(at1080 * 1e-4f),
                "more pixels means a smaller angle for the same pixel radius");
            Assert.That(MarkerSizing.MinimumAngularSize(6f, 60f, 1080),
                Is.GreaterThan(at1080));
            Assert.That(MarkerSizing.MinimumAngularSize(4.5f, 60f, 0),
                Is.EqualTo(MarkerSizing.MinimumAngularSize(4.5f, 60f, 1)),
                "a degenerate screen height must not divide by zero");
        }

        // ------------------------------------------------------- neighbourhood aid

        [Test]
        public void TheMarkerAidIsFullInsideTheLocalNeighbourhood()
        {
            double moonSoiM = SphereOfInfluence.RadiusM(_model.Get("moon"));

            Assert.That(MarkerSizing.AidStrength(CloseCameraM, 0.5 * moonSoiM, moonSoiM, 0f),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(MarkerSizing.AidStrength(CloseCameraM, moonSoiM, moonSoiM, 0f),
                Is.EqualTo(1f).Within(1e-6f),
                "the SOI boundary itself is still the neighbourhood");
        }

        [Test]
        public void TheMarkerAidFadesToTrueScaleBeyondTheNeighbourhood()
        {
            double moonSoiM = SphereOfInfluence.RadiusM(_model.Get("moon"));

            float halfwayOut = MarkerSizing.AidStrength(CloseCameraM, 2.0 * moonSoiM, moonSoiM, 0f);
            Assert.That(halfwayOut, Is.GreaterThan(0f).And.LessThan(1f),
                "the fade is gradual, not a step at the SOI edge");

            Assert.That(
                MarkerSizing.AidStrength(CloseCameraM,
                    MarkerSizing.NeighbourhoodFadeRatio * moonSoiM, moonSoiM, 0f),
                Is.EqualTo(0f).Within(1e-6f));
            Assert.That(MarkerSizing.AidStrength(CloseCameraM, MapView.AstronomicalUnitM,
                    moonSoiM, 0f),
                Is.EqualTo(0f).Within(1e-6f),
                "a planet an AU away renders at true scale");
        }

        [Test]
        public void TheMarkerAidFallsMonotonicallyAcrossTheFadeBand()
        {
            double moonSoiM = SphereOfInfluence.RadiusM(_model.Get("moon"));
            float previous = 2f;

            for (int i = 0; i <= 100; i++)
            {
                double ratio = 1.0 + 2.0 * i / 100.0;
                float value = MarkerSizing.AidStrength(CloseCameraM, ratio * moonSoiM,
                    moonSoiM, 0f);

                Assert.That(value, Is.LessThanOrEqualTo(previous + 1e-6f));
                previous = value;
            }
        }

        [Test]
        public void TheAidNeighbourhoodGrowsAsTheCameraZoomsOut()
        {
            double moonSoiM = SphereOfInfluence.RadiusM(_model.Get("moon"));
            double bodyM = 2.0 * moonSoiM;

            float close = MarkerSizing.AidStrength(CloseCameraM, bodyM, moonSoiM, 0f);
            float wide = MarkerSizing.AidStrength(4.0 * moonSoiM, bodyM, moonSoiM, 0f);

            Assert.That(close, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(wide, Is.EqualTo(1f).Within(1e-6f),
                "a body inside the zoomed-out view keeps its marker");
        }

        [Test]
        public void TheMapRestoresTheAidEverywhere()
        {
            double moonSoiM = SphereOfInfluence.RadiusM(_model.Get("moon"));
            double bodyM = MapView.AstronomicalUnitM;

            Assert.That(MarkerSizing.AidStrength(CloseCameraM, bodyM, moonSoiM, 0f),
                Is.EqualTo(0f).Within(1e-6f));
            Assert.That(MarkerSizing.AidStrength(CloseCameraM, bodyM, moonSoiM, 0.5f),
                Is.EqualTo(0.5f).Within(1e-5f),
                "the blend eases the aid back before the map is fully engaged");
            Assert.That(MarkerSizing.AidStrength(CloseCameraM, bodyM, moonSoiM, 1f),
                Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AnUnknownNeighbourhoodKeepsTheOldFullAid()
        {
            Assert.That(MarkerSizing.AidStrength(CloseCameraM, 1e13, 0.0, 0f),
                Is.EqualTo(1f), "the shipped scene passes no local scale");
            Assert.That(MarkerSizing.AidStrength(CloseCameraM, 1e13,
                    double.PositiveInfinity, 0f),
                Is.EqualTo(1f));
        }

        [Test]
        public void TheFadedAidStillYieldsToTrueScale()
        {
            double moonSoiM = SphereOfInfluence.RadiusM(_model.Get("moon"));
            float strength = MarkerSizing.AidStrength(CloseCameraM,
                MarkerSizing.NeighbourhoodFadeRatio * moonSoiM, moonSoiM, 0f);
            float baseMinAngular = MarkerSizing.MinimumAngularSize(4.5f, 60f, 1080);

            Assert.That(strength, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(BodyView.TargetScaleFactor(1e-9f, baseMinAngular * strength,
                trueScale: false), Is.EqualTo(1f));
            Assert.That(BodyView.TargetScaleFactor(1e-9f, baseMinAngular, trueScale: true),
                Is.EqualTo(1f), "T bypasses the aid whether or not it is faded");
        }

        // --------------------------------------------------------------- labels

        [Test]
        public void TheMapAlwaysLabelsTheSunPlanetsAndStations()
        {
            Assert.That(Label(_model.Get("sun"), 1e13, MapView.MaxDistanceUnits), Is.True);
            Assert.That(Label(_model.Get("earth"), 1e13, MapView.MaxDistanceUnits), Is.True);
            Assert.That(Label(_model.Get("gateway"), 1e13, MapView.MaxDistanceUnits), Is.True);
        }

        [Test]
        public void AMoonLabelsOnlyInsideItsParentsNeighbourhood()
        {
            BodyRuntime moon = _model.Get("moon");
            BodyRuntime earth = _model.Get("earth");
            double parentSoiM = SphereOfInfluence.RadiusM(earth);

            float boundaryUnits = (float)(BodyLabels.MoonNeighbourhoodFactor * parentSoiM
                                          * UnitsPerMeter);

            Assert.That(BodyLabels.InMoonNeighbourhood(moon, boundaryUnits * 0.99f, UnitsPerMeter),
                Is.True);
            Assert.That(BodyLabels.InMoonNeighbourhood(moon, boundaryUnits * 1.01f, UnitsPerMeter),
                Is.False);

            Assert.That(Label(moon, 1e9, boundaryUnits * 0.99f), Is.True);
            Assert.That(Label(moon, 1e9, boundaryUnits * 1.01f), Is.False);
        }

        [Test]
        public void TheFullSystemMapHidesMoons()
        {
            Assert.That(Label(_model.Get("moon"), 1e12, MapView.MaxDistanceUnits), Is.False,
                "a moon one AU away must not clutter the full-system map");
        }

        [Test]
        public void AnAsteroidLabelsOnlyOnHoverOrSelectionOnTheMap()
        {
            BodyRuntime asteroid = _model.Get("ast_1");

            Assert.That(BodyLabels.ShouldLabel(asteroid, 1e8, 5_000f, 1f,
                hover: false, selected: false, UnitsPerMeter), Is.False);
            Assert.That(BodyLabels.ShouldLabel(asteroid, 1e8, 5_000f, 1f,
                hover: true, selected: false, UnitsPerMeter), Is.True);
            Assert.That(BodyLabels.ShouldLabel(asteroid, 1e8, 5_000f, 1f,
                hover: false, selected: true, UnitsPerMeter), Is.True);
        }

        [Test]
        public void TheLocalOpeningShotKeepsItsOldLabels()
        {
            // w = 0 is the shipped scene: the old distance thresholds, unchanged.
            Assert.That(Label(_model.Get("moon"), 1e8, 185f, blendWeight: 0f), Is.True);
            Assert.That(Label(_model.Get("moon"), 3e9, 185f, blendWeight: 0f), Is.False);
            Assert.That(Label(_model.Get("ast_1"), 1e8, 185f, blendWeight: 0f), Is.True);
            Assert.That(Label(_model.Get("ast_1"), 3e8, 185f, blendWeight: 0f), Is.False);
            Assert.That(Label(_model.Get("earth"), 1e11, 185f, blendWeight: 0f), Is.True);
        }

        [Test]
        public void TheShipIsLabelledOnlyOnTheMap()
        {
            Assert.That(BodyLabels.ShouldLabelShip(mapSide: true), Is.True);
            Assert.That(BodyLabels.ShouldLabelShip(mapSide: false), Is.False);
        }

        // ---------------------------------------------------------- orbit lines

        [Test]
        public void OrbitLinesCannotSwampTheNeighbourhoodAtCloseMapZoom()
        {
            // The reported trap: Earth's orbit is 149,598 render units across,
            // but the camera at the Earth–Moon neighbourhood sits ~185 units
            // away. The old orbit-sized width made a ribbon wider than the view.
            const float earthOrbitReference = 149_598f;
            const float closeMapCamera = 185f;

            float width = OrbitLine.WidthForView(earthOrbitReference, closeMapCamera, 1f);

            Assert.That(width, Is.GreaterThan(0f));
            Assert.That(width / closeMapCamera, Is.LessThan(0.01f));
        }

        [Test]
        public void TheLocalOrbitLineWidthIsUnchanged()
        {
            const float reference = 149_598f;
            float expected = Mathf.Clamp(reference * OrbitLine.WidthPerCameraDistance, 0.35f,
                OrbitLine.LocalMaxWidthUnits);

            Assert.That(OrbitLine.WidthForView(reference, 185f, 0f),
                Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void TheMapOrbitLineWidthFollowsTheCameraDistance()
        {
            float near = OrbitLine.WidthForView(1f, 1_000f, 1f);
            float far = OrbitLine.WidthForView(1f, 10_000f, 1f);

            Assert.That(near, Is.GreaterThan(0f));
            Assert.That(far / near, Is.EqualTo(10f).Within(0.01f));
        }

        // ---------------------------------------------------------------- helper

        private bool Label(BodyRuntime body, double distanceToShipM, float cameraDistanceUnits,
            float blendWeight = 1f) =>
            BodyLabels.ShouldLabel(body, distanceToShipM, cameraDistanceUnits, blendWeight,
                hover: false, selected: false, UnitsPerMeter);
    }
}
