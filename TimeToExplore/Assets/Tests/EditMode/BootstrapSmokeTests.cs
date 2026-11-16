using System.Reflection;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Runs the real composition root in edit mode: the same <c>Awake</c>,
    /// <c>Update</c>, and <c>LateUpdate</c> the scene would call. Edit mode does
    /// not call them itself, so they are invoked by reflection. This catches
    /// wiring mistakes that the pure-C# suites cannot see — a missing camera, a
    /// null field, or a renderer created without the ship.
    /// </summary>
    public class BootstrapSmokeTests
    {
        private GameObject _root;
        private GameObject _cameraObject;
        private SolarSystemBootstrap _bootstrap;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
        }

        [Test]
        public void TheBootstrapComposesItselfAndStartsWithTheTestedTrip()
        {
            Build();

            Assert.That(_bootstrap.Model, Is.Not.Null);
            Assert.That(_bootstrap.Clock, Is.Not.Null);
            Assert.That(_bootstrap.Flight, Is.Not.Null);
            Assert.That(_bootstrap.Ship, Is.Not.Null);
            Assert.That(_bootstrap.Renderer, Is.Not.Null);
            Assert.That(_bootstrap.Trajectory, Is.Not.Null);
            Assert.That(_bootstrap.Rig, Is.Not.Null);
            Assert.That(_bootstrap.ShipMarker, Is.Not.Null);
            Assert.That(_bootstrap.ViewCamera, Is.Not.Null);

            _bootstrap.Model.ComputeState(_bootstrap.Model.Get("gateway"),
                _bootstrap.Clock.JulianDate, out Vector3d gateway, out _);
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, gateway), Is.LessThan(1e-6),
                "the ship must start at Gateway");
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);
            Assert.That(_bootstrap.Selected?.Id, Is.EqualTo("earth"));
            Assert.That(_bootstrap.Rig.AutoTarget?.Id, Is.EqualTo("earth"),
                "the opening shot frames Earth");
        }

        [Test]
        public void PressingGoToDrawsThePlanAndStartsTheFlight()
        {
            Build();

            _bootstrap.BeginGoTo(_bootstrap.Model.Get("mars"));

            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying));
            Assert.That(_bootstrap.Flight.TargetId, Is.EqualTo("mars"));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.True);
            Assert.That(_bootstrap.RequestedScaleIndex, Is.GreaterThan(0),
                "a four-day trip must not start at 1×");

            // One frame of the real frame order. Time.deltaTime is zero in edit
            // mode, so this checks the wiring rather than the propagation.
            Invoke("Update");
            Invoke("LateUpdate");

            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
        }

        [Test]
        public void CancelAndResetLeaveTheShipWhereTheDesignSays()
        {
            Build();
            _bootstrap.BeginGoTo(_bootstrap.Model.Get("mars"));

            _bootstrap.CancelFlight();
            Assert.That(_bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Idle));
            Assert.That(_bootstrap.Trajectory.IsShowing, Is.False);

            _bootstrap.ResetToGateway();
            _bootstrap.Model.ComputeState(_bootstrap.Model.Get("gateway"),
                _bootstrap.Clock.JulianDate, out Vector3d gateway, out _);
            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, gateway), Is.LessThan(1e-6));
            Assert.That(_bootstrap.Clock.ScaleIndex, Is.EqualTo(0));
        }

        /// <summary>A flight owns the ship, so the debug jump must refuse.</summary>
        [Test]
        public void TheDebugJumpIsRefusedWhileFlying()
        {
            Build();
            _bootstrap.BeginGoTo(_bootstrap.Model.Get("mars"));
            Vector3d before = _bootstrap.ShipPositionM;

            _bootstrap.FocusSelected();

            Assert.That(Vector3d.Distance(_bootstrap.ShipPositionM, before), Is.LessThan(1e-6));
            Assert.That(_bootstrap.Flight.IsFlying, Is.True);
            Assert.That(_bootstrap.FreeFlight, Is.False);
        }

        private void Build()
        {
            _cameraObject = new GameObject("Test Camera");
            _cameraObject.tag = "MainCamera";
            _cameraObject.AddComponent<Camera>();

            _root = new GameObject("Test Bootstrap");
            _bootstrap = _root.AddComponent<SolarSystemBootstrap>();
            Invoke("Awake");
        }

        private void Invoke(string method)
        {
            MethodInfo info = typeof(SolarSystemBootstrap).GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, $"SolarSystemBootstrap.{method} is missing");
            info.Invoke(_bootstrap, null);
        }
    }
}
