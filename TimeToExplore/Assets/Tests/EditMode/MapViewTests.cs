using System;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Simulation;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The map blend, the dominant-body choice, the extended zoom range, and
    /// the capped pan offset. Plain C#, so no scene is needed.
    /// </summary>
    public class MapViewTests
    {
        private SolarSystemModel _model;
        private MapView _map;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _map = new MapView();
        }

        [Test]
        public void TheBlendIsLocalAtTheRadiusAndTopDownAtThreeTimes()
        {
            const double radiusM = MapView.DeepSpaceRadiusM;
            float radiusUnits = (float)(radiusM * _map.UnitsPerMeter);

            Assert.That(_map.BlendWeight(radiusUnits, radiusM), Is.EqualTo(0f).Within(1e-6f),
                "d = r is exactly the local view");
            Assert.That(_map.BlendWeight(radiusUnits * 0.5f, radiusM), Is.EqualTo(0f),
                "below the local scale is still the local view");
            Assert.That(_map.BlendWeight(radiusUnits * 3f, radiusM), Is.EqualTo(1f).Within(1e-6f),
                "d = 3r is fully top-down");
            Assert.That(_map.BlendWeight(radiusUnits * 100f, radiusM), Is.EqualTo(1f),
                "the band is clamped, not extrapolated");
        }

        [Test]
        public void TheBlendFollowsTheSmoothstepCurve()
        {
            const double radiusM = MapView.DeepSpaceRadiusM;

            float atMidpoint = _map.BlendWeight(DistanceUnitsAtRatio(radiusM, Math.Sqrt(3.0)),
                radiusM);
            Assert.That(atMidpoint, Is.EqualTo(0.5f).Within(1e-4f),
                "the geometric midpoint of the band is half blended");

            float atQuarter = _map.BlendWeight(DistanceUnitsAtRatio(radiusM, Math.Pow(3.0, 0.25)),
                radiusM);
            Assert.That(atQuarter, Is.EqualTo(0.15625f).Within(1e-4f),
                "x²(3 − 2x) at x = 0.25");
            Assert.That(atQuarter, Is.LessThan(0.25f), "smoothstep eases in below linear");

            float atThreeQuarters = _map.BlendWeight(
                DistanceUnitsAtRatio(radiusM, Math.Pow(3.0, 0.75)), radiusM);
            Assert.That(atThreeQuarters, Is.EqualTo(0.84375f).Within(1e-4f),
                "x²(3 − 2x) at x = 0.75");
        }

        [Test]
        public void ZoomingOutAndBackRestoresTheRememberedPose()
        {
            var orbit = new OrbitCamera
            {
                YawDegrees = 123.5f,
                PitchDegrees = -22.25f,
                DistanceUnits = OrbitCamera.DefaultDistanceUnits
            };
            float rememberedYaw = orbit.YawDegrees;
            float rememberedPitch = orbit.PitchDegrees;
            const double radiusM = MapView.DeepSpaceRadiusM;

            // Zoom out through the band to the map; MapView only reads the pose.
            foreach (float distance in new[] { 2f, 20f, 200f, 20_000f, MapView.MaxDistanceUnits })
            {
                _map.EffectivePitchDegrees(orbit.PitchDegrees, distance, radiusM);
                Assert.That(orbit.PitchDegrees, Is.EqualTo(rememberedPitch),
                    "the player's pitch is never overwritten");
                Assert.That(orbit.YawDegrees, Is.EqualTo(rememberedYaw));
            }

            float mapPitch = _map.EffectivePitchDegrees(orbit.PitchDegrees,
                MapView.MaxDistanceUnits, radiusM);
            Assert.That(mapPitch, Is.EqualTo(MapView.TopDownPitchDegrees).Within(1e-4f),
                "the far end looks straight down");

            // Zoom back in: w returns to 0 and the exact pose is still there.
            orbit.DistanceUnits = (float)(radiusM * _map.UnitsPerMeter);
            float restored = _map.EffectivePitchDegrees(orbit.PitchDegrees,
                orbit.DistanceUnits, radiusM);

            Assert.That(restored, Is.EqualTo(rememberedPitch),
                "the round trip returns the exact remembered pitch");
            Assert.That(orbit.YawDegrees, Is.EqualTo(rememberedYaw));
        }

        [Test]
        public void TheMoonWinsOverEarthAtGateway()
        {
            BodyRuntime gateway = _model.Get("gateway");
            BodyRuntime moon = _model.Get("moon");
            BodyRuntime earth = _model.Get("earth");
            Assert.That(gateway, Is.Not.Null);

            Vector3d shipM = gateway.PositionM;
            double moonSoiM = SphereOfInfluence.RadiusM(moon);

            Assert.That(Vector3d.Distance(shipM, moon.PositionM), Is.LessThan(moonSoiM),
                "Gateway sits inside the Moon's SOI");
            Assert.That(SphereOfInfluence.Contains(earth, shipM, earth.PositionM), Is.True,
                "and inside Earth's larger SOI");

            Assert.That(MapView.DominantBody(_model, shipM), Is.SameAs(moon),
                "the smallest containing SOI wins");
            Assert.That(MapView.LocalRadiusM(_model, shipM), Is.EqualTo(moonSoiM).Within(1e-6),
                "the local scale is the Moon's SOI radius");
            Assert.That(moonSoiM / 1e3, Is.EqualTo(66_200.0).Within(100.0),
                "about 66,200 km, as the design states");
        }

        [Test]
        public void DeepSpaceFallsBackToOneMillionKilometres()
        {
            var shipM = new Vector3d(1.5e13, 0.0, 0.0); // about 100 AU out

            Assert.That(MapView.DominantBody(_model, shipM), Is.Null,
                "outside every finite SOI");
            Assert.That(MapView.LocalRadiusM(_model, shipM),
                Is.EqualTo(MapView.DeepSpaceRadiusM));
            Assert.That(MapView.DeepSpaceRadiusM / 1e3, Is.EqualTo(1.0e6),
                "1 million km");
        }

        [Test]
        public void ThePanIsCappedAtSixtyAstronomicalUnits()
        {
            Assert.That(MapView.MaxPanM,
                Is.EqualTo(60.0 * MapView.AstronomicalUnitM).Within(1.0));

            _map.SetPanM(new Vector3d(1.0e15, 0.0, 0.0));
            Assert.That(_map.PanOffsetM.Magnitude, Is.EqualTo(MapView.MaxPanM).Within(1.0));
            Assert.That(_map.PanOffsetM.x, Is.EqualTo(MapView.MaxPanM).Within(1.0));

            _map.SetPanM(new Vector3d(-7.0e12, 6.0e12, 0.0)); // 9.2e12 m, beyond the cap
            Assert.That(_map.PanOffsetM.Magnitude, Is.EqualTo(MapView.MaxPanM).Within(1.0),
                "the cap holds in every direction");

            Assert.That(_map.PanOffsetUnits.magnitude,
                Is.EqualTo((float)(MapView.MaxPanM * 1e-6)).Within(1f));
        }

        [Test]
        public void ASmallPanIsHeldAndCleared()
        {
            var offsetM = new Vector3d(1.0e12, -2.0e12, 3.0e12);

            _map.SetPanM(offsetM);
            Assert.That(_map.PanOffsetM.x, Is.EqualTo(offsetM.x));
            Assert.That(_map.PanOffsetM.y, Is.EqualTo(offsetM.y));
            Assert.That(_map.PanOffsetM.z, Is.EqualTo(offsetM.z));

            _map.Pan(new Vector3(1.0e6f, 0f, 0f)); // 1e6 units = 1e12 m along ecliptic +x
            Assert.That(_map.PanOffsetM.x, Is.EqualTo(2.0e12).Within(1.0e7),
                "the render-unit delta is converted through UnitsPerMeter");

            _map.ClearPan();
            Assert.That(_map.PanOffsetM.SqrMagnitude, Is.EqualTo(0.0));
        }

        [Test]
        public void TheMapZoomReachesTwelveBillionKilometres()
        {
            Assert.That(MapView.MaxDistanceUnits, Is.GreaterThan(OrbitCamera.MaxDistanceUnits),
                "the map extends the local camera's range");
            Assert.That(MapView.MaxDistanceUnits * 1.0e3, Is.EqualTo(1.2e10).Within(1.0),
                "12,000,000 units at 1 unit = 1,000 km is 12 billion km");

            Assert.That(MapView.ZoomDistanceUnits(1.0e5f, 30f, 1.25f),
                Is.EqualTo(MapView.MaxDistanceUnits));
            Assert.That(MapView.ZoomDistanceUnits(1.0e5f, -100f, 1.25f),
                Is.EqualTo(MapView.MinDistanceUnits));
            Assert.That(MapView.ClampDistanceUnits(1.0e9f),
                Is.EqualTo(MapView.MaxDistanceUnits));
            Assert.That(MapView.ClampDistanceUnits(0f),
                Is.EqualTo(OrbitCamera.MinDistanceUnits));
        }

        private float DistanceUnitsAtRatio(double radiusM, double ratio)
        {
            return (float)(radiusM * ratio * _map.UnitsPerMeter);
        }
    }
}
