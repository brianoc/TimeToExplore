using System.Collections.Generic;
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
    [Category("presentation")]
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
            // away. The old orbit-sized width made a ribbon wider than the
            // view; the camera-distance rule makes it a fine line.
            const float closeCamera = 185f;

            float width = LineWidth.WorldWidth(closeCamera, 60f, 1080f);

            Assert.That(width, Is.GreaterThan(0f));
            Assert.That(width / closeCamera, Is.LessThan(0.01f));
        }

        [Test]
        public void AnOrbitLineIsThreePixelsAtEveryZoom()
        {
            float[] depths = { 0.002f, 185f, 149_598f, 12_000_000f };

            foreach (float depth in depths)
            {
                float width = LineWidth.WorldWidth(depth, 60f, 1080f);
                float pixels = LineWidth.PixelsAt(width, depth, 60f, 1080f);
                Assert.That(pixels, Is.EqualTo(LineWidth.Pixels).Within(1e-3f),
                    $"a {depth}-unit camera should still see a fine line");
            }
        }

        [Test]
        public void TheOrbitLineWidthFollowsTheCameraDistance()
        {
            float near = LineWidth.WorldWidth(1_000f, 60f, 1080f);
            float far = LineWidth.WorldWidth(10_000f, 60f, 1080f);

            Assert.That(near, Is.GreaterThan(0f));
            Assert.That(far / near, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void TheWidthRuleIsResolutionIndependent()
        {
            float hd = LineWidth.WorldWidth(1_000f, 60f, 1080f);
            float uhd = LineWidth.WorldWidth(1_000f, 60f, 2160f);

            // Twice the pixels in the same screen height means one pixel is
            // half as wide in world units at the same depth.
            Assert.That(uhd, Is.EqualTo(hd * 0.5f).Within(1e-4f));
            Assert.That(LineWidth.PixelsAt(uhd, 1_000f, 60f, 2160f),
                Is.EqualTo(LineWidth.Pixels).Within(1e-3f));
        }

        [Test]
        public void OrbitLinesAreThreePixelsInTheLocalViewAndTenOnTheMap()
        {
            Assert.That(LineWidth.OrbitPixels(0f), Is.EqualTo(3f).Within(1e-6f));
            Assert.That(LineWidth.OrbitPixels(1f), Is.EqualTo(10f).Within(1e-6f));
            Assert.That(LineWidth.OrbitPixels(0.5f), Is.EqualTo(6.5f).Within(1e-5f));
        }

        [Test]
        public void TheOrbitWidthClampsOutsideTheBlend()
        {
            Assert.That(LineWidth.OrbitPixels(-1f), Is.EqualTo(3f).Within(1e-6f),
                "a weight below zero is the local view");
            Assert.That(LineWidth.OrbitPixels(2f), Is.EqualTo(10f).Within(1e-6f),
                "a weight above one is the map");
        }

        [Test]
        public void TheOrbitWidthGrowsSmoothlyThroughTheBlend()
        {
            float previous = -1f;
            for (int i = 0; i <= 100; i++)
            {
                float pixels = LineWidth.OrbitPixels(i / 100f);
                Assert.That(pixels, Is.GreaterThanOrEqualTo(previous));
                previous = pixels;
            }
        }

        [Test]
        public void TheThickMapOrbitLineIsStillResolutionIndependent()
        {
            float width = LineWidth.WorldWidth(1_000f, 60f, 1080f,
                LineWidth.OrbitPixels(1f));

            Assert.That(LineWidth.PixelsAt(width, 1_000f, 60f, 1080f),
                Is.EqualTo(10f).Within(1e-3f));
            Assert.That(LineWidth.WorldWidth(1_000f, 60f, 2160f,
                    LineWidth.OrbitPixels(1f)),
                Is.EqualTo(width * 0.5f).Within(1e-4f));
        }

        [Test]
        public void ADistantLineHoldsTheDistanceFloorInsteadOfVanishing()
        {
            const float lookAtDepth = 185f;
            float lookAtWidth = LineWidth.WorldWidth(lookAtDepth, 60f, 1080f);

            // A single world width would put these vertices at 1, 0.3, and
            // practically 0 pixels; the floor holds them at 2.
            foreach (float multiple in new[] { 3f, 10f, 1_000f })
            {
                float depth = lookAtDepth * multiple;
                float width = LineWidth.SampleWorldWidth(lookAtWidth, LineWidth.Pixels,
                    depth, 60f, 1080f);

                Assert.That(LineWidth.PixelsAt(width, depth, 60f, 1080f),
                    Is.EqualTo(LineWidth.DistanceFloorPixels).Within(1e-3f),
                    $"{multiple} camera distances away");
            }
        }

        [Test]
        public void ADistantLineStillNarrowsUntilItReachesTheFloor()
        {
            const float lookAtDepth = 185f;
            float lookAtWidth = LineWidth.WorldWidth(lookAtDepth, 60f, 1080f);
            float depth = lookAtDepth * 1.2f;

            float width = LineWidth.SampleWorldWidth(lookAtWidth, LineWidth.Pixels,
                depth, 60f, 1080f);

            Assert.That(LineWidth.PixelsAt(width, depth, 60f, 1080f),
                Is.EqualTo(LineWidth.Pixels / 1.2f).Within(1e-3f),
                "above the floor the natural perspective falloff still applies");
        }

        [Test]
        public void ANearVertexIsCappedAtTheLineWidth()
        {
            const float lookAtDepth = 185f;
            float lookAtWidth = LineWidth.WorldWidth(lookAtDepth, 60f, 1080f);

            // The reported trap: a route that passes close to a zoomed-in
            // camera used to grow by lookAtDepth / depth. At 1% of the look-at
            // depth that was 300 px; the ceiling holds it at the declared
            // 3 px instead.
            foreach (float depth in new[] { 1e-4f, 0.01f * lookAtDepth,
                0.5f * lookAtDepth })
            {
                float width = LineWidth.SampleWorldWidth(lookAtWidth, LineWidth.Pixels,
                    depth, 60f, 1080f);

                Assert.That(LineWidth.PixelsAt(width, depth, 60f, 1080f),
                    Is.EqualTo(LineWidth.Pixels).Within(1e-3f), $"at {depth} units");
            }
        }

        [Test]
        public void TheNearCapUsesTheLinesOwnPixelWidth()
        {
            const float lookAtDepth = 185f;
            const float mapPixels = 10f;
            float lookAtWidth = LineWidth.WorldWidth(lookAtDepth, 60f, 1080f, mapPixels);

            float width = LineWidth.SampleWorldWidth(lookAtWidth, mapPixels,
                1f, 60f, 1080f);

            Assert.That(LineWidth.PixelsAt(width, 1f, 60f, 1080f),
                Is.EqualTo(mapPixels).Within(1e-3f),
                "a bold map orbit line keeps its 10 px ceiling near the camera");
        }

        [Test]
        public void TheBoundedRuleStillNarrowsBetweenTheCapAndTheFloor()
        {
            const float lookAtDepth = 185f;
            float lookAtWidth = LineWidth.WorldWidth(lookAtDepth, 60f, 1080f);
            float previous = float.MaxValue;

            foreach (float multiple in new[] { 1f, 1.2f, 2f, 10f, 1_000f })
            {
                float depth = lookAtDepth * multiple;
                float pixels = LineWidth.PixelsAt(
                    LineWidth.SampleWorldWidth(lookAtWidth, LineWidth.Pixels, depth,
                        60f, 1080f),
                    depth, 60f, 1080f);

                Assert.That(pixels, Is.LessThanOrEqualTo(previous + 1e-3f),
                    "the width never widens as the depth grows");
                Assert.That(pixels,
                    Is.InRange(LineWidth.DistanceFloorPixels - 1e-3f,
                        LineWidth.Pixels + 1e-3f));
                previous = pixels;
            }
        }

        [Test]
        public void AVertexAtOrBehindTheCameraHasNoWidth()
        {
            float lookAtWidth = LineWidth.WorldWidth(185f, 60f, 1080f);

            Assert.That(LineWidth.SampleWorldWidth(lookAtWidth, LineWidth.Pixels,
                0f, 60f, 1080f), Is.EqualTo(0f),
                "the camera plane is a taper, not the look-at width");
            Assert.That(LineWidth.SampleWorldWidth(lookAtWidth, LineWidth.Pixels,
                -5f, 60f, 1080f), Is.EqualTo(0f));
        }

        [Test]
        public void TheFloorNeverInflatesAThinnerLine()
        {
            float onePixelWidth = LineWidth.WorldWidth(1_000f, 60f, 1080f, 1f);

            float far = LineWidth.SampleWorldWidth(onePixelWidth, 1f, 1_000_000f,
                60f, 1080f);

            Assert.That(LineWidth.PixelsAt(far, 1_000_000f, 60f, 1080f),
                Is.EqualTo(1f).Within(1e-3f),
                "a line already thinner than the floor stays its own width");
        }

        [Test]
        public void TheDistanceFloorIsResolutionIndependent()
        {
            float narrow = LineWidth.WorldWidth(185f, 60f, 1080f);
            float wide = LineWidth.WorldWidth(185f, 60f, 2160f);

            float farNarrow = LineWidth.SampleWorldWidth(narrow, 3f, 100_000f, 60f, 1080f);
            float farWide = LineWidth.SampleWorldWidth(wide, 3f, 100_000f, 60f, 2160f);

            Assert.That(LineWidth.PixelsAt(farNarrow, 100_000f, 60f, 1080f),
                Is.EqualTo(LineWidth.DistanceFloorPixels).Within(1e-3f));
            Assert.That(LineWidth.PixelsAt(farWide, 100_000f, 60f, 2160f),
                Is.EqualTo(LineWidth.DistanceFloorPixels).Within(1e-3f));
            Assert.That(farWide, Is.EqualTo(farNarrow * 0.5f).Within(1e-4f));
        }

        [Test]
        public void AnOrbitLineCarriesAPerVertexWidthCurve()
        {
            BodyRuntime earth = _model.Get("earth");
            var parent = new GameObject("Orbit Width Parent");
            var orbitObject = new GameObject("Orbit Width Orbit");
            var cameraObject = new GameObject("Orbit Width Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            LineRenderer line = null;

            try
            {
                const float unitsPerMeter = 1e-6f;
                const float cameraDistance = 185f;
                camera.transform.position = new Vector3(0f, 0f, -cameraDistance);
                camera.fieldOfView = 60f;
                int height = ViewportHeight(camera);

                OrbitLine orbit = orbitObject.AddComponent<OrbitLine>();
                orbit.Initialise(earth, _model, Color.white, parent.transform);

                float lookAtWidth = LineWidth.WorldWidth(cameraDistance,
                    camera.fieldOfView, height, LineWidth.OrbitLocalPixels);
                orbit.Refresh(earth.PositionM, camera, unitsPerMeter, true,
                    lookAtWidth, LineWidth.OrbitLocalPixels, cameraDistance);

                line = orbitObject.GetComponent<LineRenderer>();
                Assert.That(line, Is.Not.Null);
                Assert.That(line.widthCurve.length, Is.EqualTo(line.positionCount),
                    "every sampled vertex needs its own width key");

                for (int i = 0; i < line.positionCount; i++)
                {
                    float depth = Vector3.Distance(line.GetPosition(i),
                        camera.transform.position);
                    float pixels = LineWidth.PixelsAt(
                        line.widthCurve.Evaluate(i / (float)(line.positionCount - 1)),
                        depth, camera.fieldOfView, height);

                    Assert.That(pixels,
                        Is.InRange(LineWidth.DistanceFloorPixels - 1e-3f,
                            LineWidth.OrbitLocalPixels + 1e-3f),
                        $"orbit vertex {i} is {pixels} px wide");
                }
            }
            finally
            {
                if (line != null && line.sharedMaterial != null)
                    UnityEngine.Object.DestroyImmediate(line.sharedMaterial);
                UnityEngine.Object.DestroyImmediate(orbitObject);
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ATrajectoryLineCarriesAPerVertexWidthCurve()
        {
            var lineObject = new GameObject("Trajectory Width Line");
            var cameraObject = new GameObject("Trajectory Width Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            TrajectoryLine trajectory = null;

            try
            {
                const float unitsPerMeter = 1e-6f;
                const float cameraDistance = 185f;
                camera.transform.position = new Vector3(0f, 0f, -cameraDistance);
                camera.fieldOfView = 60f;
                int height = ViewportHeight(camera);

                double jd = _model.CurrentJulianDate;
                var start = Vector3d.Zero;
                var end = new Vector3d(1.0e14, 0.0, 0.0);
                var arc = new PlanArc(FlightPhase.Cruise, "sun", jd, jd + 1.0,
                    new StraightPath(start, end));
                var plan = new TrajectoryPlan("mars", 10.0,
                    new List<PlanArc> { arc },
                    new ShipState(jd, start, Vector3d.Zero),
                    new ShipState(jd + 1.0, end, Vector3d.Zero));

                trajectory = lineObject.AddComponent<TrajectoryLine>();
                trajectory.Initialise(unitsPerMeter);
                trajectory.Show(plan, ZeroGravityField.Instance);
                trajectory.Refresh(start, camera);

                LineRenderer line = lineObject.GetComponentInChildren<LineRenderer>();
                Assert.That(line, Is.Not.Null);
                Assert.That(line.positionCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(line.widthCurve.length, Is.EqualTo(line.positionCount));

                for (int i = 0; i < line.positionCount; i++)
                {
                    float depth = Vector3.Distance(line.GetPosition(i),
                        camera.transform.position);
                    float pixels = LineWidth.PixelsAt(
                        line.widthCurve.Evaluate(i / (float)(line.positionCount - 1)),
                        depth, camera.fieldOfView, height);

                    Assert.That(pixels,
                        Is.InRange(LineWidth.DistanceFloorPixels - 1e-3f,
                            LineWidth.Pixels + 1e-3f),
                        $"route vertex {i} is {pixels} px wide");
                }

                float nearPixels = PixelsAtVertex(line, 0, camera, height);
                Assert.That(nearPixels, Is.EqualTo(LineWidth.Pixels).Within(1e-2f),
                    "the route still starts at the look-at width");

                float farPixels = PixelsAtVertex(line, line.positionCount - 1, camera, height);
                Assert.That(farPixels, Is.EqualTo(LineWidth.DistanceFloorPixels).Within(1e-2f),
                    "the far end of the route holds the floor");
            }
            finally
            {
                if (trajectory != null)
                {
                    foreach (LineRenderer line in
                        lineObject.GetComponentsInChildren<LineRenderer>(true))
                        if (line.sharedMaterial != null)
                            UnityEngine.Object.DestroyImmediate(line.sharedMaterial);
                }
                UnityEngine.Object.DestroyImmediate(lineObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void AZoomedInRouteDoesNotGrowIntoARibbon()
        {
            // The reported trap, on a real renderer: a route 100,000,000 units
            // across and a camera parked one unit from its midpoint. The old
            // constant world width drew that vertex 100,000,000 / 1 times the
            // look-at width, so the route filled the screen. The ceiling holds
            // every vertex between the 2 px floor and the 3 px route width.
            var lineObject = new GameObject("Zoomed Route Line");
            var cameraObject = new GameObject("Zoomed Route Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            TrajectoryLine trajectory = null;

            try
            {
                const float unitsPerMeter = 1e-6f;
                const double radiusM = 1.0e14; // 100,000,000 render units
                float radiusUnits = (float)radiusM * unitsPerMeter;

                // The camera is 800 units from the route's midpoint, but a
                // whole radius from the ship at the origin, so the old
                // constant world width was 100,000× too wide there. The far
                // end of the route falls behind the camera.
                var routeMid = new Vector3(radiusUnits, 0f, -radiusUnits);
                camera.transform.position = routeMid + new Vector3(0f, 0f, -800f);
                camera.transform.LookAt(routeMid);
                camera.fieldOfView = 60f;
                int height = ViewportHeight(camera);

                double jd = _model.CurrentJulianDate;
                var arc = new PlanArc(FlightPhase.Cruise, "sun", jd, jd + 1.0,
                    new CircularPath(radiusM));
                var plan = new TrajectoryPlan("mars", 10.0,
                    new List<PlanArc> { arc },
                    new ShipState(jd, arc.Path.PositionAt(0.0), Vector3d.Zero),
                    new ShipState(jd + 1.0, arc.Path.PositionAt(1.0), Vector3d.Zero));

                trajectory = lineObject.AddComponent<TrajectoryLine>();
                trajectory.Initialise(unitsPerMeter);
                trajectory.Show(plan, ZeroGravityField.Instance);
                trajectory.Refresh(arc.Path.PositionAt(0.0), camera);

                LineRenderer line = lineObject.GetComponentInChildren<LineRenderer>();
                Assert.That(line, Is.Not.Null);
                Assert.That(line.positionCount, Is.GreaterThan(8),
                    "the curved route must have mid-path vertices to test");

                for (int i = 0; i < line.positionCount; i++)
                {
                    float pixels = PixelsAtVertex(line, i, camera, height);
                    Assert.That(pixels, Is.LessThanOrEqualTo(LineWidth.Pixels + 1e-3f),
                        $"route vertex {i} is {pixels} px wide");

                    // The two clipped endpoints taper at the camera plane,
                    // where 1e8-unit floats cannot resolve the 0.3-unit near
                    // clip; the rest of the route must hold the floor.
                    if (i > 0 && i < line.positionCount - 1)
                    {
                        Assert.That(pixels,
                            Is.GreaterThanOrEqualTo(LineWidth.DistanceFloorPixels - 1e-3f),
                            $"route vertex {i} is {pixels} px wide");

                        Assert.That(Vector3.Dot(line.GetPosition(i) - camera.transform.position,
                                camera.transform.forward),
                            Is.GreaterThanOrEqualTo(camera.nearClipPlane - 1e-3f),
                            $"route vertex {i} is behind the camera");
                    }
                }
            }
            finally
            {
                if (trajectory != null)
                {
                    foreach (LineRenderer line in
                        lineObject.GetComponentsInChildren<LineRenderer>(true))
                        if (line.sharedMaterial != null)
                            UnityEngine.Object.DestroyImmediate(line.sharedMaterial);
                }
                UnityEngine.Object.DestroyImmediate(lineObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ALineCrossingBehindTheCameraIsClippedAtTheNearPlane()
        {
            var cameraObject = new GameObject("Clip Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            var run = new List<Vector3>();
            var visible = new List<Vector3>();

            try
            {
                camera.nearClipPlane = 0.1f;
                camera.transform.position = Vector3.zero;

                // The line starts behind the camera, crosses its plane at
                // z = 0.1, and ends well in front.
                var points = new List<Vector3>
                {
                    new Vector3(0f, 0f, -10f),
                    new Vector3(0f, 0f, 0.05f),
                    new Vector3(0f, 0f, 5f)
                };

                int count = LineCameraClip.Clip(points, camera, run, visible);

                Assert.That(count, Is.EqualTo(2));
                Assert.That(visible[0].z, Is.EqualTo(0.1f).Within(1e-4f),
                    "the crossing is interpolated onto the near plane");
                Assert.That(visible[1].z, Is.EqualTo(5f).Within(1e-4f));

                foreach (Vector3 point in visible)
                    Assert.That(Vector3.Dot(point - camera.transform.position,
                            camera.transform.forward),
                        Is.GreaterThanOrEqualTo(camera.nearClipPlane - 1e-4f),
                        "no drawn vertex sits behind the camera");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void TheClipKeepsTheLongestVisibleRun()
        {
            var cameraObject = new GameObject("Clip Runs Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            var run = new List<Vector3>();
            var visible = new List<Vector3>();

            try
            {
                camera.nearClipPlane = 0.1f;
                camera.transform.position = Vector3.zero;

                // Two visible runs, the second longer: in front, behind, and in
                // front again. One LineRenderer cannot draw both, so the clip
                // keeps the longer one.
                var points = new List<Vector3>
                {
                    new Vector3(0f, 0f, 1f),
                    new Vector3(0f, 0f, -1f),
                    new Vector3(0f, 0f, 2f),
                    new Vector3(0f, 0f, 3f),
                    new Vector3(0f, 0f, 4f)
                };

                int count = LineCameraClip.Clip(points, camera, run, visible);

                Assert.That(count, Is.EqualTo(4),
                    "the crossing point joins the longer in-front run");
                Assert.That(visible[0].z, Is.EqualTo(0.1f).Within(1e-4f));
                Assert.That(visible[3].z, Is.EqualTo(4f).Within(1e-4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void MercuryIsWarmAndNeptuneIsCool()
        {
            Color mercury = OrbitColours.Planet(0f);
            Color neptune = OrbitColours.Planet(1f);

            Assert.That(mercury.r, Is.GreaterThan(mercury.g),
                "Mercury's orange has more red than green");
            Assert.That(mercury.g, Is.GreaterThan(mercury.b),
                "Mercury's orange has more green than blue");
            Assert.That(neptune.b, Is.GreaterThan(neptune.r),
                "Neptune's purple has more blue than red");
            Assert.That(neptune.b, Is.GreaterThan(neptune.g));
            Assert.That(mercury.r, Is.GreaterThan(neptune.r),
                "the ramp cools from Mercury to Neptune");
            Assert.That(mercury.b, Is.LessThan(neptune.b));
        }

        [Test]
        public void EveryPlanetInTheCatalogueGetsADistinctOrbitColour()
        {
            var colours = OrbitColours.ForPlanets(_model.Ordered);

            Assert.That(colours.Count, Is.EqualTo(8), "the catalogue has eight planets");
            Assert.That(colours["mercury"], Is.EqualTo(OrbitColours.Planet(0f)));
            Assert.That(colours["neptune"], Is.EqualTo(OrbitColours.Planet(1f)));

            // Orbital order, not catalogue order, drives the ramp: the four
            // inner planets must not collapse into one end of it.
            string[] outward = { "mercury", "venus", "earth", "mars", "jupiter",
                "saturn", "uranus", "neptune" };
            for (int i = 1; i < outward.Length; i++)
            {
                Color inner = colours[outward[i - 1]];
                Color outer = colours[outward[i]];
                Assert.That(inner, Is.Not.EqualTo(outer),
                    $"{outward[i - 1]} and {outward[i]} need different orbit colours");
                Assert.That(outer.r, Is.LessThan(inner.r),
                    $"{outward[i]} should be cooler than {outward[i - 1]}");
            }
        }

        [Test]
        public void TheOrbitColourDoesNotDependOnTheView()
        {
            // The palette is a function of the body's place in the ramp only;
            // nothing in the mapping takes a blend weight, so both views draw
            // the same colour. Only LineWidth.OrbitPixels reads the blend.
            Color local = OrbitColours.Planet(2, 8);
            Color map = OrbitColours.Planet(2, 8);

            Assert.That(map, Is.EqualTo(local));
            Assert.That(local.a, Is.EqualTo(OrbitColours.PlanetAlpha).Within(1e-6f));
        }

        [Test]
        public void ASinglePlanetStillGetsAWarmOrbit()
        {
            Assert.That(OrbitColours.ForPlanets(new[] { _model.Get("earth") })["earth"],
                Is.EqualTo(OrbitColours.Planet(0f)));
        }

        // ---------------------------------------------------------------- helper

        private static int ViewportHeight(Camera camera) =>
            camera != null && camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height;

        private static float PixelsAtVertex(LineRenderer line, int index, Camera camera,
            int height)
        {
            float depth = Vector3.Distance(line.GetPosition(index),
                camera.transform.position);
            float u = line.positionCount > 1
                ? index / (float)(line.positionCount - 1)
                : 0f;
            return LineWidth.PixelsAt(line.widthCurve.Evaluate(u), depth,
                camera.fieldOfView, height);
        }

        /// <summary>A half-circle in the XY plane for the renderer tests: a
        /// curve whose midpoint passes close to the camera and tessellates into
        /// many short chords.</summary>
        private sealed class CircularPath : IArcPath
        {
            private readonly double _radiusM;

            public CircularPath(double radiusM) => _radiusM = radiusM;

            public double DurationSeconds => 1.0;

            public Vector3d PositionAt(double u)
            {
                double angle = System.Math.PI * (u - 0.5);
                return new Vector3d(_radiusM * System.Math.Cos(angle),
                    _radiusM * System.Math.Sin(angle), 0.0);
            }

            public Vector3d VelocityAt(double u)
            {
                double angle = System.Math.PI * (u - 0.5);
                return new Vector3d(-_radiusM * System.Math.PI * System.Math.Sin(angle),
                    _radiusM * System.Math.PI * System.Math.Cos(angle), 0.0);
            }

            public Vector3d AccelerationAt(double u)
            {
                double angle = System.Math.PI * (u - 0.5);
                return new Vector3d(
                    -_radiusM * System.Math.PI * System.Math.PI * System.Math.Cos(angle),
                    -_radiusM * System.Math.PI * System.Math.PI * System.Math.Sin(angle),
                    0.0);
            }

            public double SecondsAt(double u) => u * DurationSeconds;

            public double ParameterAtSeconds(double seconds) =>
                DurationSeconds > 0.0
                    ? System.Math.Clamp(seconds / DurationSeconds, 0.0, 1.0)
                    : 0.0;
        }

        /// <summary>A straight arc for the renderer tests: constant velocity and
        /// no acceleration, so it tessellates to its two endpoints.</summary>
        private sealed class StraightPath : IArcPath
        {
            private readonly Vector3d _start;
            private readonly Vector3d _delta;

            public StraightPath(Vector3d start, Vector3d end)
            {
                _start = start;
                _delta = end - start;
            }

            public double DurationSeconds => 1.0;

            public Vector3d PositionAt(double u) => _start + _delta * u;

            public Vector3d VelocityAt(double u) => _delta;

            public Vector3d AccelerationAt(double u) => Vector3d.Zero;

            public double SecondsAt(double u) => u * DurationSeconds;

            public double ParameterAtSeconds(double seconds) =>
                DurationSeconds > 0.0
                    ? System.Math.Clamp(seconds / DurationSeconds, 0.0, 1.0)
                    : 0.0;
        }

        private bool Label(BodyRuntime body, double distanceToShipM, float cameraDistanceUnits,
            float blendWeight = 1f) =>
            BodyLabels.ShouldLabel(body, distanceToShipM, cameraDistanceUnits, blendWeight,
                hover: false, selected: false, UnitsPerMeter);
    }
}
