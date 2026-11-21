using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>The orbit camera's limits, zoom curve, and auto-framing slew.</summary>
    [Category("presentation")]
    public class OrbitCameraTests
    {
        [Test]
        public void TheDefaultViewSitsBehindAndAboveTheShip()
        {
            var orbit = new OrbitCamera();

            Vector3 offset = orbit.OffsetUnits;

            Assert.That(offset.magnitude, Is.EqualTo(OrbitCamera.DefaultDistanceUnits).Within(1e-5f));
            Assert.That(offset.z, Is.GreaterThan(0f), "behind the ship at yaw 0");
            Assert.That(offset.y, Is.GreaterThan(0f), "above the ecliptic");

            Vector3 forward = orbit.Rotation * Vector3.forward;
            Assert.That(Vector3.Angle(forward, -offset.normalized), Is.LessThan(0.01f),
                "the camera looks back at the ship");
        }

        [Test]
        public void TurningClampsThePitch()
        {
            var orbit = new OrbitCamera();

            orbit.Turn(0f, 500f);
            Assert.That(orbit.PitchDegrees, Is.EqualTo(OrbitCamera.MaxPitchDegrees));

            orbit.Turn(0f, -1000f);
            Assert.That(orbit.PitchDegrees, Is.EqualTo(OrbitCamera.MinPitchDegrees));
        }

        [Test]
        public void TurningWrapsTheYaw()
        {
            var orbit = new OrbitCamera { YawDegrees = 10f };

            orbit.Turn(-20f, 0f);
            Assert.That(orbit.YawDegrees, Is.EqualTo(350f).Within(1e-3f));

            orbit.Turn(360f, 0f);
            Assert.That(orbit.YawDegrees, Is.EqualTo(350f).Within(1e-3f));
        }

        [Test]
        public void ZoomRequestsMultiplicativeStepsAndClamps()
        {
            var orbit = new OrbitCamera { DistanceUnits = 100f };

            orbit.Zoom(1f, 2f);
            Assert.That(orbit.DistanceUnits, Is.EqualTo(100f),
                "a zoom requests a distance; it does not teleport");
            Assert.That(orbit.TargetDistanceUnits, Is.EqualTo(200f).Within(1e-3f));

            orbit.Zoom(1f, 2f);
            Assert.That(orbit.TargetDistanceUnits, Is.EqualTo(400f).Within(1e-3f),
                "rapid notches compound on the pending request");

            orbit.Zoom(100f, 2f);
            Assert.That(orbit.TargetDistanceUnits, Is.EqualTo(OrbitCamera.MaxDistanceUnits));

            orbit.Zoom(-200f, 2f);
            Assert.That(orbit.TargetDistanceUnits, Is.EqualTo(OrbitCamera.MinDistanceUnits));
        }

        [Test]
        public void AZoomRequestEasesInsteadOfSnapping()
        {
            var orbit = new OrbitCamera { DistanceUnits = 100f };
            orbit.RequestDistance(200f);

            Assert.That(orbit.IsDistanceSettled, Is.False);
            Assert.That(orbit.DistanceUnits, Is.EqualTo(100f),
                "the request alone does not move the camera");

            Assert.That(orbit.EaseDistance(1f / 60f, 7f), Is.True);
            Assert.That(orbit.DistanceUnits, Is.GreaterThan(100f));
            Assert.That(orbit.DistanceUnits, Is.LessThan(200f), "the first frame is part-way");

            float previous = orbit.DistanceUnits;
            int frames = 1;
            while (!orbit.IsDistanceSettled && frames < 600)
            {
                orbit.EaseDistance(1f / 60f, 7f);
                Assert.That(orbit.DistanceUnits, Is.GreaterThanOrEqualTo(previous),
                    "the tween never runs backwards");
                previous = orbit.DistanceUnits;
                frames++;
            }

            Assert.That(frames, Is.GreaterThan(1), "the zoom takes more than one frame");
            Assert.That(frames, Is.LessThan(600));
            Assert.That(orbit.DistanceUnits, Is.EqualTo(200f), "the tween lands exactly");
            Assert.That(orbit.EaseDistance(1f / 60f, 7f), Is.False,
                "a settled camera reports no motion");
        }

        [Test]
        public void PlacingTheDistanceCancelsAPendingRequest()
        {
            var orbit = new OrbitCamera { DistanceUnits = 100f };
            orbit.RequestDistance(1_000f);

            orbit.DistanceUnits = 0.5f;

            Assert.That(orbit.DistanceUnits, Is.EqualTo(0.5f));
            Assert.That(orbit.TargetDistanceUnits, Is.EqualTo(0.5f));
            Assert.That(orbit.IsDistanceSettled, Is.True);
        }

        [Test]
        public void AZeroLengthFrameCompletesTheZoom()
        {
            var orbit = new OrbitCamera { DistanceUnits = 100f };
            orbit.RequestDistance(200f);

            Assert.That(orbit.EaseDistance(0f, 7f), Is.False);

            Assert.That(orbit.DistanceUnits, Is.EqualTo(200f));
            Assert.That(orbit.IsDistanceSettled, Is.True);
        }

        [Test]
        public void AimingAlongADirectionPutsTheCameraBehindTheViewer()
        {
            var orbit = new OrbitCamera();

            orbit.AimAlong(new Vector3d(0.0, 0.0, 1.0));

            Assert.That(orbit.PitchDegrees, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(orbit.OffsetUnits.z, Is.LessThan(0f),
                "looking toward +z means sitting at -z");
            Assert.That(Vector3.Dot(orbit.OffsetUnits.normalized, new Vector3(0f, 0f, 1f)),
                Is.EqualTo(-1f).Within(1e-4f));
        }

        [Test]
        public void SlewTurnsAtTheRateLimitAndThenSettles()
        {
            var orbit = new OrbitCamera { PitchDegrees = 0f };

            float remaining = orbit.SlewTo(90f, 0f, 45f, 1f);
            Assert.That(orbit.YawDegrees, Is.EqualTo(45f).Within(0.01f));
            Assert.That(remaining, Is.EqualTo(45f).Within(0.01f));

            remaining = orbit.SlewTo(90f, 0f, 45f, 1f);
            Assert.That(orbit.YawDegrees, Is.EqualTo(90f).Within(0.01f));
            Assert.That(remaining, Is.EqualTo(0f));
        }

        [Test]
        public void AShortSlewSnapsInOneFrame()
        {
            var orbit = new OrbitCamera();

            float remaining = orbit.SlewTo(5f, 3f, 45f, 1f);

            Assert.That(orbit.YawDegrees, Is.EqualTo(5f).Within(1e-3f));
            Assert.That(orbit.PitchDegrees, Is.EqualTo(3f).Within(1e-3f));
            Assert.That(remaining, Is.EqualTo(0f));
        }

        [Test]
        public void AZeroLengthFrameDoesNotTurn()
        {
            var orbit = new OrbitCamera();

            orbit.SlewTo(90f, 0f, 45f, 0f);

            Assert.That(orbit.YawDegrees, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TheSlewTakesTheShortWayRound()
        {
            var orbit = new OrbitCamera { YawDegrees = 350f, PitchDegrees = 0f };

            float remaining = orbit.SlewTo(10f, 0f, 5f, 1f);

            Assert.That(orbit.YawDegrees, Is.EqualTo(355f).Within(0.01f),
                "20 degrees of travel, not 340");
            Assert.That(remaining, Is.EqualTo(15f).Within(0.01f));
        }

        [Test]
        public void DistanceEasesRatherThanSnapping()
        {
            var orbit = new OrbitCamera { DistanceUnits = 10f };

            orbit.SmoothDistance(20f, 0.1f, 2.5f);
            Assert.That(orbit.DistanceUnits, Is.GreaterThan(10f));
            Assert.That(orbit.DistanceUnits, Is.LessThan(20f));

            for (int i = 0; i < 200; i++) orbit.SmoothDistance(20f, 0.1f, 2.5f);
            Assert.That(orbit.DistanceUnits, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void FramingKeepsTheDistanceAndTurnsTheView()
        {
            var orbit = new OrbitCamera { DistanceUnits = 0.2f };
            var ship = new Vector3d(1.0e11, 0.0, 0.0);

            orbit.Frame(ship, ship + new Vector3d(0.0, 1.0e10, 0.0));

            Assert.That(orbit.DistanceUnits, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(orbit.PitchDegrees, Is.LessThan(0f), "look up at the target");
        }

        [Test]
        public void TheHomeTransitionEasesBothEndpointsOverOneSecond()
        {
            var home = new HomeTransition();
            var startPan = new Vector3d(1.0e10, -2.0e9, 3.0e9);
            home.Begin(100f, 10f, startPan);

            Assert.That(home.IsActive, Is.True);
            Assert.That(home.DistanceUnitsAt, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(home.LookAtOffsetMAt.Magnitude, Is.EqualTo(startPan.Magnitude).Within(1.0));
            Assert.That(home.Progress, Is.EqualTo(0f));

            // Half the duration is smoothstep's half: the endpoints move together,
            // and the distance is the geometric mean of the two ends.
            home.Advance(0.5f);
            Assert.That(home.EasedProgress, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(home.DistanceUnitsAt, Is.EqualTo(Mathf.Sqrt(1000f)).Within(1e-3f));
            Assert.That(home.LookAtOffsetMAt.Magnitude,
                Is.EqualTo(0.5 * startPan.Magnitude).Within(1.0));

            int frames = 1;
            while (home.IsActive && frames < 600)
            {
                home.Advance(1f / 60f);
                frames++;
            }

            Assert.That(frames, Is.InRange(30, 32), "the remaining half second");
            Assert.That(home.EasedProgress, Is.EqualTo(1f));
            Assert.That(home.DistanceUnitsAt, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(home.LookAtOffsetMAt.Magnitude, Is.EqualTo(0.0));
        }

        [Test]
        public void AHomeTransitionAtZeroDurationLandsImmediately()
        {
            var home = new HomeTransition { DurationSeconds = 0f };
            home.Begin(100f, 10f, new Vector3d(1.0e10, 0.0, 0.0));

            Assert.That(home.Advance(0f), Is.True);
            Assert.That(home.IsActive, Is.False);
            Assert.That(home.DistanceUnitsAt, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(home.LookAtOffsetMAt.Magnitude, Is.EqualTo(0.0));
        }

        [Test]
        public void CancellingTheHomeTransitionStopsItWhereItIs()
        {
            var home = new HomeTransition();
            home.Begin(100f, 10f, new Vector3d(1.0e10, 0.0, 0.0));
            home.Advance(0.25f);
            float midway = home.DistanceUnitsAt;

            home.Cancel();

            Assert.That(home.IsActive, Is.False);
            Assert.That(home.Advance(1f), Is.False);
            Assert.That(home.DistanceUnitsAt, Is.EqualTo(midway));
        }
    }
}
