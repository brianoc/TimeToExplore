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

        [Test]
        public void OnlyTheMapSidePans()
        {
            Assert.That(MapView.UsesPan(1f), Is.True);
            Assert.That(MapView.UsesPan(0.51f), Is.True);
            Assert.That(MapView.UsesPan(0.5f), Is.False, "the threshold itself still orbits");
            Assert.That(MapView.UsesPan(0.49f), Is.False);
            Assert.That(MapView.UsesPan(0f), Is.False);
        }

        [Test]
        public void TheMapPoseLooksStraightDownWithAHorizontalUpThatTurnsWithYaw()
        {
            Vector3 upAtZero = MapView.MapScreenUp(0f);
            Assert.That(upAtZero.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(upAtZero.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(upAtZero.z, Is.EqualTo(-1f).Within(1e-6f),
                "the limit of the local view's projected up as its pitch reaches the pole");

            foreach (float yaw in new[] { 0f, 37f, 90f, 180f, 359f })
            {
                Quaternion rotation = MapView.MapRotation(yaw);
                AssertFinite(rotation);

                Vector3 forward = rotation * Vector3.forward;
                Assert.That(Vector3.Angle(forward, Vector3.down), Is.LessThan(1e-3f),
                    $"yaw {yaw}: the map looks straight down");

                Vector3 up = rotation * Vector3.up;
                Assert.That(up.y, Is.EqualTo(0f).Within(1e-5f),
                    $"yaw {yaw}: the up vector is horizontal, never parallel to forward");
                Assert.That(Vector3.Angle(up, MapView.MapScreenUp(yaw)), Is.LessThan(0.05f));
            }

            Vector3 upAtNinety = MapView.MapScreenUp(90f);
            Assert.That(Vector3.Angle(upAtZero, upAtNinety), Is.EqualTo(90f).Within(1e-3f),
                "the up vector turns with yaw, so the map keeps the player's orientation");
            Assert.That(Vector3.Dot(upAtZero, upAtNinety), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void TheBlendedRotationMatchesTheEffectivePitch()
        {
            var orbit = new OrbitCamera { YawDegrees = 37f, PitchDegrees = 14f };

            foreach (float weight in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                Quaternion blended = MapView.BlendRotation(orbit.Rotation, orbit.YawDegrees,
                    weight);
                float pitch = Mathf.Lerp(orbit.PitchDegrees, MapView.TopDownPitchDegrees,
                    weight);
                Vector3 expected = -OrbitCamera.Direction(orbit.YawDegrees, pitch);
                Vector3 actual = blended * Vector3.forward;

                Assert.That(Vector3.Angle(actual, expected), Is.LessThan(0.01f),
                    $"w = {weight}: the rotation and the effective pitch agree");
            }

            Assert.That(MapView.BlendRotation(orbit.Rotation, orbit.YawDegrees, 0f),
                Is.EqualTo(orbit.Rotation), "w = 0 is exactly the player's rotation");
        }

        [Test]
        public void TheNearPlaneIsLocalAtZeroAndFollowsTheMapRule()
        {
            float[] distances = { 0f, 0.002f, 0.185f, 1f, 1_000f, OrbitCamera.MaxDistanceUnits };
            foreach (float distance in distances)
            {
                float local = MapView.NearClipUnits(distance, 0f);
                Assert.That(local, Is.EqualTo(Mathf.Clamp(distance * 0.02f, 0.0002f, 0.05f)),
                    "w = 0 is exactly today's near plane");
                Assert.That(local, Is.GreaterThan(0f));
                Assert.That(float.IsNaN(local) || float.IsInfinity(local), Is.False);
            }

            float atCeiling = MapView.NearClipUnits(MapView.MaxDistanceUnits, 1f);
            Assert.That(atCeiling, Is.EqualTo(MapView.MaxNearClipUnits));
            Assert.That(atCeiling, Is.EqualTo(0.001f * MapView.MaxDistanceUnits).Within(1e-3f),
                "one thousandth of the map distance");
            Assert.That(atCeiling, Is.LessThan(MapView.FarClipUnits),
                "the near plane stays well inside the far plane");
            Assert.That(MapView.FarClipUnits, Is.GreaterThan(MapView.MaxDistanceUnits),
                "the far plane clears the zoom ceiling");

            float halfway = MapView.NearClipUnits(MapView.MaxDistanceUnits, 0.5f);
            Assert.That(halfway, Is.GreaterThan(MapView.LocalNearClipUnits(MapView.MaxDistanceUnits)));
            Assert.That(halfway, Is.LessThan(atCeiling));
        }

        [Test]
        public void ThePanScaleFollowsTheCameraDistance()
        {
            float near = MapView.UnitsPerPixel(100f, 60f, 1000f);
            float far = MapView.UnitsPerPixel(200f, 60f, 1000f);
            Assert.That(far, Is.EqualTo(2f * near).Within(near * 1e-4f),
                "twice the distance covers twice the units per pixel");
            Assert.That(near, Is.EqualTo(2f * 100f * Mathf.Tan(30f * Mathf.Deg2Rad) / 1000f)
                .Within(1e-3f));

            Vector3 panRight = MapView.PanDeltaForDrag(10f, 0f, 100f, 60f, 1000f, 0f);
            Vector3 panUp = MapView.PanDeltaForDrag(0f, 10f, 100f, 60f, 1000f, 0f);
            Quaternion rotation = MapView.MapRotation(0f);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            Assert.That(panRight.magnitude, Is.EqualTo(10f * near).Within(near * 1e-4f),
                "one pixel of drag is one pixel of content");
            Assert.That(Vector3.Dot(panRight, right), Is.LessThan(0f),
                "the look-at moves against the drag, so the content follows the cursor");
            Assert.That(panRight.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(Vector3.Dot(panUp, up), Is.LessThan(0f),
                "dragging up moves the content up, not the camera");
            Assert.That(panUp.magnitude, Is.EqualTo(10f * near).Within(near * 1e-4f));
        }

        [Test]
        public void ThePanEasesToTheShipRatherThanSnapping()
        {
            const float deltaTime = 1f / 60f;
            _map.PanReturnRatePerSecond = 3f;
            _map.SetPanM(new Vector3d(1.0e12, 0.0, 0.0));
            double previous = _map.PanOffsetM.Magnitude;

            _map.EasePan(deltaTime);
            double afterOneFrame = _map.PanOffsetM.Magnitude;
            Assert.That(afterOneFrame, Is.LessThan(previous), "the pan starts easing");
            Assert.That(afterOneFrame, Is.GreaterThan(0.9 * previous),
                "and does not snap in a single frame");

            for (int i = 0; i < 600; i++)
            {
                _map.EasePan(deltaTime);
                Assert.That(_map.PanOffsetM.Magnitude, Is.LessThanOrEqualTo(previous),
                    "the return is monotonic");
                previous = _map.PanOffsetM.Magnitude;
            }

            Assert.That(_map.PanOffsetM.Magnitude, Is.LessThan(1.0),
                "ten seconds at 3/s settles well inside a metre");
        }

        private void AssertFinite(Quaternion rotation)
        {
            Assert.That(float.IsNaN(rotation.x) || float.IsInfinity(rotation.x), Is.False);
            Assert.That(float.IsNaN(rotation.y) || float.IsInfinity(rotation.y), Is.False);
            Assert.That(float.IsNaN(rotation.z) || float.IsInfinity(rotation.z), Is.False);
            Assert.That(float.IsNaN(rotation.w) || float.IsInfinity(rotation.w), Is.False);
            Assert.That(Quaternion.Angle(rotation, Quaternion.identity), Is.LessThan(180.001f));
        }

        private float DistanceUnitsAtRatio(double radiusM, double ratio)
        {
            return (float)(radiusM * ratio * _map.UnitsPerMeter);
        }
    }
}
