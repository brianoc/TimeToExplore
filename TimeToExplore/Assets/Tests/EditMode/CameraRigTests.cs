using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>How far the camera sits back when it frames a destination.</summary>
    public class CameraRigTests
    {
        private SolarSystemModel _model;
        private GameObject _root;
        private ShipCameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _root = new GameObject("Camera Rig Test");
            _rig = _root.AddComponent<ShipCameraRig>();
            _rig.UnitsPerMeter = 1e-6f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void ArrivalIsFramedAtThreeBodyRadii()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out Vector3d marsVelocity);
            ParkingOrbit.StateAt(mars, marsPosition, marsVelocity, 0.0,
                out Vector3d shipPosition, out _);

            float units = _rig.AutoDistanceUnits(shipPosition, mars);

            double metres = units / _rig.UnitsPerMeter;
            Assert.That(metres, Is.EqualTo(3.0 * mars.RadiusM).Within(mars.RadiusM * 0.01),
                "the parking orbit is only 300 km up, so three radii wins over the fraction");
        }

        [Test]
        public void CruiseOpensTheViewOut()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            var shipPosition = marsPosition + new Vector3d(7.5e10, 0.0, 0.0); // 0.5 AU
            float units = _rig.AutoDistanceUnits(shipPosition, mars);

            double metres = units / _rig.UnitsPerMeter;
            Assert.That(metres, Is.EqualTo(0.6 * 7.5e10).Within(1e6));
            Assert.That(units, Is.GreaterThan(1f), "tens of thousands of render units");
        }

        [Test]
        public void TheViewIsClampedForAVeryDistantTarget()
        {
            BodyRuntime mars = _model.Get("mars");
            _model.ComputeState(mars, _model.CurrentJulianDate,
                out Vector3d marsPosition, out _);

            var shipPosition = marsPosition + new Vector3d(0.0, 1.0e13, 0.0);
            float units = _rig.AutoDistanceUnits(shipPosition, mars);

            Assert.That(units / _rig.UnitsPerMeter, Is.LessThanOrEqualTo(5.0e11 + 1.0));
        }

        [Test]
        public void ThePlanLineStaysThinAtEveryZoom()
        {
            // The reported bug: the line's width came from the length of the
            // plan, so at a 185,000 km orbit it was 225,000 km thick and the
            // camera was inside it. It must always be a sliver of the distance.
            float[] distances =
            {
                OrbitCamera.MinDistanceUnits,
                0.185f,   // the Gateway opening shot
                1_000f,
                120_000f, // a whole inner-system plan
                OrbitCamera.MaxDistanceUnits
            };

            foreach (float distance in distances)
            {
                float width = TrajectoryLine.WidthForCameraDistance(distance);
                Assert.That(width / distance, Is.LessThan(0.01f),
                    $"a {distance} unit camera must not sit inside a {width} unit line");
                Assert.That(width, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void FramingABodyTurnsAutoOnAndClearingItHandsBack()
        {
            BodyRuntime earth = _model.Get("earth");

            _rig.Frame(earth);
            Assert.That(_rig.Auto, Is.True);
            Assert.That(_rig.AutoAim, Is.True);
            Assert.That(_rig.AutoDistance, Is.True);
            Assert.That(_rig.AutoTarget, Is.SameAs(earth));

            _rig.ClearAuto();
            Assert.That(_rig.Auto, Is.False);
            Assert.That(_rig.AutoAim, Is.False);
            Assert.That(_rig.AutoDistance, Is.False);
            Assert.That(_rig.AutoTarget, Is.Null);
        }
    }
}
