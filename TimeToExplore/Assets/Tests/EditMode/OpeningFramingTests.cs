using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The opening composition: Earth and the Moon together, from 20° above
    /// the ecliptic, on the sunward side, with both discs inside the frame.
    /// </summary>
    [Category("presentation")]
    public class OpeningFramingTests
    {
        private const float FieldOfViewDegrees = 60f;
        private const float Aspect = 16f / 9f;
        private const float UnitsPerMeter = 1e-6f;

        private SolarSystemModel _model;
        private GameObject _root;
        private ShipCameraRig _rig;
        private Vector3d _shipM;
        private Vector3d _earthM;
        private Vector3d _moonM;
        private Vector3d _sunM;

        [SetUp]
        public void SetUp()
        {
            _model = CatalogueFixture.LoadModel();
            _root = new GameObject("Opening Framing Test");
            _rig = _root.AddComponent<ShipCameraRig>();
            _rig.Initialise(null, _model, UnitsPerMeter);

            double date = _model.CurrentJulianDate;
            _model.ComputeState(_model.Get("gateway"), date, out _shipM, out _);
            _model.ComputeState(_model.Get("earth"), date, out _earthM, out _);
            _model.ComputeState(_model.Get("moon"), date, out _moonM, out _);
            _model.ComputeState(_model.Sun, date, out _sunM, out _);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void TheOpeningLooksDownTwentyDegreesFromAboveTheEcliptic()
        {
            RouteFraming.Frame frame = Compute();

            Assert.That(frame.IsValid, Is.True);
            Assert.That(frame.PitchDegrees,
                Is.EqualTo(OpeningFraming.PitchDegrees).Within(1e-3f),
                "the shot looks 20° down at the pair");

            Vector3d offset = Vector3d.FromUnity(
                OrbitCamera.Direction(frame.YawDegrees, frame.PitchDegrees));
            Assert.That(offset.z, Is.GreaterThan(0.0),
                "the camera is above the ecliptic, not below it");
        }

        [Test]
        public void EarthAndTheMoonAreBothInsideTheFittedMargin()
        {
            RouteFraming.Frame frame = Compute();

            Assert.That(frame.IsValid, Is.True);
            AssertInsideMargin(frame, _earthM, "Earth");
            AssertInsideMargin(frame, _moonM, "the Moon");

            Vector3d centre = (_earthM + _moonM) * 0.5;
            Assert.That(Vector3d.Distance(frame.LookAtOffsetM, centre - _shipM),
                Is.LessThan(1.0),
                "the look-at is the midpoint of the pair, so the shot is balanced");
        }

        [Test]
        public void TheCameraSitsOnTheSunwardSideSoBothDiscsAreLit()
        {
            RouteFraming.Frame frame = Compute();

            Vector3 look = OrbitCamera.Direction(frame.YawDegrees, frame.PitchDegrees);
            Vector3d viewEcliptic = Vector3d.FromUnity(-look);
            Vector3d toSun = _sunM - _shipM;

            Assert.That(Vector3d.Dot(viewEcliptic, toSun), Is.LessThan(0.0),
                "the camera looks away from the Sun, so the pair is lit from behind it");
        }

        [Test]
        public void AStationAtThePairIsInShotToo()
        {
            RouteFraming.Frame frame = Compute();
            AssertInsideMargin(frame, _shipM, "the ship");
        }

        [Test]
        public void CoincidentBodiesLeaveTheViewAlone()
        {
            RouteFraming.Frame frame = OpeningFraming.Compute(_shipM, _earthM, _earthM,
                _sunM, UnitsPerMeter, FieldOfViewDegrees, Aspect, 0f,
                OrbitCamera.MaxDistanceUnits);

            Assert.That(frame.IsValid, Is.False);
        }

        [Test]
        public void TheRigSnapsToTheOpeningPoseAndHandsTheViewBack()
        {
            _rig.Frame(_model.Get("earth"));
            Assert.That(_rig.AutoAim, Is.True);

            bool shown = _rig.ShowOpeningPose(_shipM, _earthM, _moonM, _sunM);

            Assert.That(shown, Is.True);
            Assert.That(_rig.Auto, Is.False, "the opening pose is the player's view");
            Assert.That(_rig.Orbit.PitchDegrees,
                Is.EqualTo(OpeningFraming.PitchDegrees).Within(1e-3f));
            Assert.That(Vector3d.Distance(_rig.LookAtOffsetM,
                (_earthM + _moonM) * 0.5 - _shipM), Is.LessThan(1.0));

            _rig.Apply(_shipM, 1f / 60f);
            Assert.That(_rig.Orbit.PitchDegrees,
                Is.EqualTo(OpeningFraming.PitchDegrees).Within(1e-3f),
                "no body follow pulls the camera off the composition after one frame");
        }

        private RouteFraming.Frame Compute() =>
            OpeningFraming.Compute(_shipM, _earthM, _moonM, _sunM, UnitsPerMeter,
                FieldOfViewDegrees, Aspect, 0f, OrbitCamera.MaxDistanceUnits);

        private static void AssertInsideMargin(RouteFraming.Frame frame, Vector3d pointM,
            string what)
        {
            Vector3 offset = OrbitCamera.Direction(frame.YawDegrees, frame.PitchDegrees);
            Vector3 camera = (frame.LookAtOffsetM * UnitsPerMeter).ToUnity()
                             + offset * frame.DistanceUnits;
            Vector3 target = ((pointM - frame.CentreM) * UnitsPerMeter).ToUnity()
                             + (frame.LookAtOffsetM * UnitsPerMeter).ToUnity();

            Vector3 toPoint = target - camera;
            Quaternion rotation = Quaternion.LookRotation(-offset, Vector3.up);
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            float depth = Vector3.Dot(toPoint, forward);
            float halfHeight = Mathf.Tan(0.5f * FieldOfViewDegrees * Mathf.Deg2Rad);
            float ndcX = Vector3.Dot(toPoint, right) / (depth * halfHeight * Aspect);
            float ndcY = Vector3.Dot(toPoint, up) / (depth * halfHeight);
            float limit = 1f / OpeningFraming.FitMargin + 1e-3f;

            Assert.That(depth, Is.GreaterThan(0f), $"{what} is in front of the camera");
            Assert.That(Mathf.Abs(ndcX), Is.LessThanOrEqualTo(limit),
                $"{what} is inside the fitted margin horizontally");
            Assert.That(Mathf.Abs(ndcY), Is.LessThanOrEqualTo(limit),
                $"{what} is inside the fitted margin vertically");
        }
    }
}
