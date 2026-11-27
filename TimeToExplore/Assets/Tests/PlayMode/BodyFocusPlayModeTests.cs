using System.Collections;
using NUnit.Framework;
using TimeToExplore.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// F frames the selected body through the real frame loop by making it the
    /// render origin. The ship must not move: the focus is a camera and origin
    /// change, not the debug jump. The body sits at zero, the camera orbits it
    /// at four radii, and Space returns the origin to the ship.
    /// </summary>
    [Category("playmode")]
    public class BodyFocusPlayModeTests
    {
        private const float FrameSeconds = 1f / 60f;

        private SolarSystemBootstrap _bootstrap;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<SolarSystemBootstrap>();
            Assert.That(_bootstrap, Is.Not.Null,
                "RuntimeInitializeOnLoad must create the bootstrap in a bare play session");

            _bootstrap.CancelFlight();
            _bootstrap.DiscardPreview();
            _bootstrap.SetFreeFlight(false);
            _bootstrap.SetScaleIndex(0);
            _bootstrap.ResetToGateway();
            _bootstrap.Clock.Paused = true;
            _bootstrap.DeltaTimeSeconds = () => FrameSeconds;

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _bootstrap?.CancelFlight();
            _bootstrap?.DiscardPreview();
            if (_bootstrap != null) _bootstrap.Clock.Paused = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator FocusSelectedFramesTheBodyInItsOwnRenderOrigin()
        {
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            Assert.That(mars, Is.Not.Null);

            _bootstrap.Select(mars);
            Vector3d shipBefore = _bootstrap.ShipPositionM;
            Vector3d marsBefore = mars.PositionM;

            _bootstrap.FocusSelected();
            Assert.That(_bootstrap.Rig.IsFocusingBody, Is.True,
                "F starts the framing move, not the ship teleport");
            Assert.That(_bootstrap.FreeFlight, Is.False,
                "focusing the camera does not hand the ship to free flight");

            for (int i = 0; i < 90; i++) yield return null;

            Assert.That(Vector3d.Distance(shipBefore, _bootstrap.ShipPositionM),
                Is.LessThan(1e-6), "the camera frames; the ship stays at Gateway");
            Assert.That(_bootstrap.Rig.IsFocusingBody, Is.False);
            Assert.That(_bootstrap.Rig.IsFocused, Is.True);
            Assert.That(_bootstrap.Rig.FocusTarget, Is.SameAs(mars));
            Assert.That(_bootstrap.Rig.AutoTarget, Is.Null,
                "the body is the origin, so there is no follow target");
            Assert.That(Vector3d.Distance(_bootstrap.RenderOriginM, marsBefore),
                Is.LessThan(1e-6), "Mars is the render origin");

            float bodyOffset = _bootstrap.Renderer.GetView(mars)
                .transform.localPosition.magnitude;
            Assert.That(bodyOffset, Is.LessThan(1e-4),
                "the focused body sits at (0,0,0)");

            float focusDistance = _bootstrap.Rig.FocusDistanceUnits(mars);
            Assert.That(_bootstrap.Rig.Orbit.DistanceUnits,
                Is.EqualTo(focusDistance).Within(focusDistance * 1e-3f));
            Assert.That(_bootstrap.Rig.LookAtOffsetM.Magnitude,
                Is.EqualTo(0.0).Within(1e-6));

            var view = _bootstrap.Renderer.GetView(mars);
            Assert.That(view.Renderer.sharedMaterial.GetFloat("_HasAlbedo"),
                Is.EqualTo(1f), "the framed body samples its map");
            Assert.That(view.AtmosphereRenderer, Is.Not.Null);
            Assert.That(view.AtmosphereRenderer.enabled, Is.True,
                "the framed body draws its atmosphere");
        }

        [UnityTest]
        public IEnumerator SpaceReturnsTheOriginToTheShip()
        {
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);
            Vector3d shipBefore = _bootstrap.ShipPositionM;

            _bootstrap.FocusSelected();
            for (int i = 0; i < 90; i++) yield return null;
            Assert.That(_bootstrap.Rig.IsFocused, Is.True);

            _bootstrap.Rig.HomeToShip(_bootstrap.ShipPositionM);
            for (int i = 0; i < 90; i++) yield return null;

            Assert.That(_bootstrap.Rig.IsFocused, Is.False,
                "home leaves the focused origin");
            Assert.That(_bootstrap.Rig.IsHoming, Is.False,
                "the origin cut lands on the home view without a cross-system zoom");
            Assert.That(Vector3d.Distance(_bootstrap.RenderOriginM, shipBefore),
                Is.LessThan(1e-6), "the ship is the render origin again");
            Assert.That(Vector3d.Distance(shipBefore, _bootstrap.ShipPositionM),
                Is.LessThan(1e-6), "the ship never moved");
            Assert.That(_bootstrap.Rig.LookAtOffsetM.Magnitude,
                Is.EqualTo(0.0).Within(1e-6));
        }
    }
}
