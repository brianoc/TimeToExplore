using System.Collections;
using NUnit.Framework;
using TimeToExplore.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// F focuses the camera on the selected body through the real frame loop.
    /// The ship must not move: the focus is a camera transition, not the debug
    /// jump, and the view follows the body afterwards until Space or a drag.
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
        public IEnumerator FocusSelectedMovesTheCameraAndLeavesTheShipWhereItIs()
        {
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            Assert.That(mars, Is.Not.Null);

            _bootstrap.Select(mars);
            Vector3d before = _bootstrap.ShipPositionM;

            _bootstrap.FocusSelected();
            Assert.That(_bootstrap.Rig.IsFocusingBody, Is.True,
                "F starts the camera move, not the ship teleport");
            Assert.That(_bootstrap.FreeFlight, Is.False,
                "focusing the camera does not hand the ship to free flight");

            for (int i = 0; i < 90; i++) yield return null;

            Assert.That(Vector3d.Distance(before, _bootstrap.ShipPositionM),
                Is.LessThan(1e-6), "the camera focuses; the ship stays at Gateway");
            Assert.That(_bootstrap.Rig.IsFocusingBody, Is.False);
            Assert.That(_bootstrap.Rig.AutoTarget, Is.SameAs(mars),
                "the view follows the focused body after the move");
            Assert.That(_bootstrap.Rig.LookAtOffsetM.Magnitude,
                Is.EqualTo(0.0).Within(1e-6), "the camera looks past the ship at Mars");
        }

        [UnityTest]
        public IEnumerator SpaceTakesTheViewHomeFromABodyFocus()
        {
            BodyRuntime mars = _bootstrap.Model.Get("mars");
            _bootstrap.Select(mars);

            _bootstrap.FocusSelected();
            for (int i = 0; i < 90; i++) yield return null;
            Assert.That(_bootstrap.Rig.AutoTarget, Is.SameAs(mars));

            _bootstrap.Rig.HomeToShip(_bootstrap.ShipPositionM);
            for (int i = 0; i < 90; i++) yield return null;

            Assert.That(_bootstrap.Rig.Auto, Is.False,
                "home hands the view back from the body follow");
            Assert.That(_bootstrap.Rig.IsHoming, Is.False);
            Assert.That(_bootstrap.Rig.LookAtOffsetM.Magnitude,
                Is.EqualTo(0.0).Within(1e-6));
        }
    }
}
