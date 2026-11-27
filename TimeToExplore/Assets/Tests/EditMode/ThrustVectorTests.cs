using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using TimeToExplore.Simulation;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The thrust vector at the ship: direction, visibility, and size.</summary>
    [Category("presentation")]
    public class ThrustVectorTests
    {
        private GameObject _shipObject;
        private ShipView _ship;
        private GameObject _cameraObject;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _shipObject = new GameObject("Ship View");
            _ship = _shipObject.AddComponent<ShipView>();
            _ship.Initialise();

            _cameraObject = new GameObject("Test Camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.fieldOfView = 60f;
            _cameraObject.transform.position = new Vector3(0f, 0f, 5f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_cameraObject);
            Object.DestroyImmediate(_shipObject);
        }

        [Test]
        public void TheMarkerPointsAlongTheDirectionOfTravel()
        {
            var velocity = new Vector3d(1.0, 2.0, -3.0).Normalized;
            _ship.Refresh(_camera, ThrustCommand.None, velocity);

            Assert.That(Vector3.Dot(_ship.MarkerDirection, velocity.ToUnity()),
                Is.GreaterThan(0.999f),
                "the capsule's long axis follows the ship, not the screen");
        }

        [Test]
        public void TheMarkerGrowsWhenTheCameraIsClose()
        {
            Assert.That(ShipView.MarkerPixelsAt(1f), Is.EqualTo(ShipView.MarkerPixels));
            Assert.That(ShipView.MarkerPixelsAt(ShipView.CloseMarkerDistanceUnits),
                Is.EqualTo(ShipView.MarkerPixels));
            Assert.That(ShipView.MarkerPixelsAt(
                    TimeToExplore.Player.OrbitCamera.MinDistanceUnits),
                Is.EqualTo(ShipView.CloseMarkerPixels));
            Assert.That(ShipView.MarkerPixelsAt(
                    TimeToExplore.Player.FlightFraming.DistanceUnits),
                Is.InRange(40f, 52f), "the chase view makes the ship the subject");
        }

        [Test]
        public void TheArrowPointsAlongTheThrustNotTheVelocity()
        {
            // Ecliptic north is Unity +Y; ecliptic +Y is Unity -Z.
            var thrust = new Vector3d(0.0, 0.0, 1.0);
            var velocity = new Vector3d(0.0, 1.0, 0.0);

            _ship.Refresh(_camera, new ThrustCommand(thrust), velocity);

            Assert.That(_ship.ThrustArrowVisible, Is.True);
            Assert.That(Vector3.Dot(_ship.ThrustArrowDirection, thrust.Normalized.ToUnity()),
                Is.GreaterThan(0.999f), "the arrow follows the commanded acceleration");
            Assert.That(Vector3.Dot(_ship.ThrustArrowDirection, velocity.Normalized.ToUnity()),
                Is.LessThan(0.01f), "a retrograde thrust must not follow the velocity");
        }

        [Test]
        public void TheArrowHidesWhenTheDriveIsOff()
        {
            _ship.Refresh(_camera, ThrustCommand.None, new Vector3d(0.0, 1.0, 0.0));

            Assert.That(_ship.ThrustArrowVisible, Is.False);
        }

        [Test]
        public void AThrustBelowTheThresholdDoesNotDrawTheArrow()
        {
            var tiny = new Vector3d(ShipView.MinimumThrustMps2 * 0.9, 0.0, 0.0);

            _ship.Refresh(_camera, new ThrustCommand(tiny), Vector3d.Zero);

            Assert.That(_ship.ThrustArrowVisible, Is.False);
        }

        [Test]
        public void TheArrowKeepsAFixedApparentLength()
        {
            var thrust = new ThrustCommand(new Vector3d(1.0, 0.0, 0.0));

            _cameraObject.transform.position = new Vector3(0f, 0f, 5f);
            _ship.Refresh(_camera, thrust, Vector3d.Zero);
            float near = _ship.ThrustArrowLength;

            _cameraObject.transform.position = new Vector3(0f, 0f, 10f);
            _ship.Refresh(_camera, thrust, Vector3d.Zero);
            float far = _ship.ThrustArrowLength;

            Assert.That(near, Is.GreaterThan(0f));
            Assert.That(far, Is.EqualTo(near * 2f).Within(near * 1e-3f),
                "twice the camera distance is twice the world length for the same pixels");
        }

        [Test]
        public void TheArrowReturnsWhenTheShipIsVisibleAgain()
        {
            var thrust = new ThrustCommand(new Vector3d(1.0, 0.0, 0.0));
            _ship.Refresh(_camera, thrust, Vector3d.Zero);
            Assert.That(_ship.ThrustArrowVisible, Is.True);

            _ship.SetVisible(false);
            Assert.That(_ship.ThrustArrowVisible, Is.False);

            _ship.SetVisible(true);
            Assert.That(_ship.ThrustArrowVisible, Is.True,
                "the arrow comes back with the marker");
        }
    }
}
