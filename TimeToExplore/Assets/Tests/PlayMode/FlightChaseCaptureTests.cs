using System.Collections;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Player;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Evidence for the flight chase camera and the warp curve, not a gate. It
    /// begins the Gateway → Mars flight through the bootstrap, lets the chase
    /// ease finish and the curve climb, and writes a mid-cruise frame and an
    /// arrival approach frame into <c>FLIGHT_CAPTURE_DIR</c>. It carries only
    /// the <c>capture</c> category, so no gate starts it, and it needs a
    /// graphics device.
    /// </summary>
    [Category("capture")]
    public class FlightChaseCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const float FrameSeconds = 1f / 60f;

        [UnityTest]
        public IEnumerator TheChaseCameraFramesTheShipUnderWay()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindAnyObjectByType<SolarSystemBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);

            bootstrap.CancelFlight();
            bootstrap.DiscardPreview();
            bootstrap.SetFreeFlight(false);
            bootstrap.ResetToGateway();
            bootstrap.DeltaTimeSeconds = () => FrameSeconds;
            bootstrap.Clock.Paused = false;
            bootstrap.ShowHud = true;
            bootstrap.ShowLabels = true;

            BodyRuntime mars = bootstrap.Model.Get("mars");
            Assert.That(mars, Is.Not.Null);

            bootstrap.Select(mars);
            string dir = System.Environment.GetEnvironmentVariable("FLIGHT_CAPTURE_DIR")
                         ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            Capture(bootstrap.ViewCamera, Path.Combine(dir, "flight-start.png"));

            bootstrap.BeginGoTo(mars);
            Assert.That(bootstrap.Flight.IsFlying, Is.True,
                bootstrap.Flight.Failure ?? "the flight must begin");

            // The opening ease runs for 1.2 s; sample it so a body that the
            // camera sweeps through is visible in the evidence.
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
                if (frame % 10 == 0)
                    Capture(bootstrap.ViewCamera,
                        Path.Combine(dir, $"flight-start-{frame:000}.png"));
            }

            // About six real seconds: the 1.2 s chase ease is long done and the
            // warp curve is at its cruise rate.
            for (int frame = 0; frame < 360; frame++) yield return null;

            Assert.That(bootstrap.Rig.IsFramingFlight, Is.False,
                "the chase pose has landed");
            Assert.That(bootstrap.Rig.IsFollowingFlight, Is.True,
                "the flight owns the chase distance");
            Assert.That(bootstrap.Rig.Orbit.DistanceUnits,
                Is.GreaterThanOrEqualTo(FlightFraming.DistanceUnits),
                "the distance grows with the frame's travel");
            Assert.That(bootstrap.Flight.IsFlying, Is.True);
            Debug.Log($"chase capture at {bootstrap.Clock.Label}, " +
                      $"flight {bootstrap.Flight.Plan.FlightTimeDays:F2} d, " +
                      $"distance {bootstrap.Rig.Orbit.DistanceUnits:F3} units, " +
                      $"speed {bootstrap.ShipVelocityMps.Magnitude / 1000.0:F0} km/s, " +
                      $"clock {JulianDate.Format(bootstrap.Clock.JulianDate)}");
            Capture(bootstrap.ViewCamera, Path.Combine(dir, "flight-chase.png"));

            // Run to just before the arrival, where the curve is back near 1x.
            int guard = 0;
            while (bootstrap.Flight.IsFlying && guard < 4000)
            {
                double remaining = bootstrap.Flight.Plan.ArrivalJulianDate
                                   - bootstrap.Clock.JulianDate;
                if (remaining * JulianDate.SecondsPerDay < 20.0) break;
                yield return null;
                guard++;
            }

            bootstrap.Model.ComputeState(mars, bootstrap.Clock.JulianDate,
                out Vector3d marsPosition, out _);
            Vector3 marsRelative = ((marsPosition - bootstrap.ShipPositionM)
                                    * bootstrap.UnitsPerMeter).ToUnity();
            float cameraToMars = Vector3.Distance(bootstrap.Rig.PosePositionUnits,
                marsRelative);
            Debug.Log($"arrival approach at {bootstrap.Clock.Label}, " +
                      $"flight remaining " +
                      $"{(bootstrap.Flight.Plan.ArrivalJulianDate - bootstrap.Clock.JulianDate) * JulianDate.SecondsPerDay:F1} s, " +
                      $"camera {bootstrap.Rig.Orbit.DistanceUnits:F1} units, " +
                      $"camera-Mars {cameraToMars:F2} units " +
                      $"(Mars radius {mars.RadiusM * bootstrap.UnitsPerMeter:F2})");
            Capture(bootstrap.ViewCamera, Path.Combine(dir, "flight-arrival.png"));
            Debug.Log("flight captures written to " + dir);
        }

        /// <summary>Render the live camera into a linear half-float target and
        /// encode the PNG by hand, matching <c>BodyFocusCaptureTests</c>.</summary>
        private static void Capture(Camera camera, string path)
        {
            RenderTexture previousTarget = camera.targetTexture;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            target.Create();
            Texture2D image = null;
            Texture2D png = null;
            try
            {
                camera.targetTexture = target;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                try
                {
                    image = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true);
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    image.Apply();

                    Color[] pixels = image.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                            pixels[i] = pixels[i].gamma;
                        pixels[i].a = 1f;
                    }

                    png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    png.SetPixels(pixels);
                    png.Apply();
                    File.WriteAllBytes(path, png.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                if (image != null) Object.Destroy(image);
                if (png != null) Object.Destroy(png);
                target.Release();
                Object.Destroy(target);
            }
        }
    }
}
