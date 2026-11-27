using System;
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
    /// Evidence for the camera-only focus key: the ship sits three radii from
    /// Mars, the view is turned thirty degrees off the planet, and F eases the
    /// camera back onto it without moving the ship. Writes
    /// <c>focus-before.png</c> and <c>focus-after.png</c> into
    /// <c>BODY_CAPTURE_DIR</c>. It carries only the <c>capture</c> category, so
    /// no gate starts it, and it needs a graphics device.
    /// </summary>
    [Category("capture")]
    public class BodyFocusCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const float FrameSeconds = 1f / 60f;

        [UnityTest]
        public IEnumerator FocusEasesTheCameraOntoTheSelectedBody()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindFirstObjectByType<SolarSystemBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);

            bootstrap.CancelFlight();
            bootstrap.DiscardPreview();
            bootstrap.SetFreeFlight(false);
            bootstrap.SetScaleIndex(0);
            bootstrap.Clock.JulianDate = bootstrap.Model.Data.startJulianDate;
            bootstrap.Clock.Paused = true;
            bootstrap.DeltaTimeSeconds = () => FrameSeconds;
            bootstrap.ShowHud = false;
            bootstrap.ShowLabels = false;
            bootstrap.Renderer.ShowOrbits = false;
            bootstrap.ShipMarker.SetVisible(false);

            BodyRuntime mars = bootstrap.Model.Get("mars");
            Assert.That(mars, Is.Not.Null);
            bootstrap.Select(mars);

            // Park three radii from Mars on its sunward side, then let the
            // marker-size aid settle so the planet is drawn at true scale.
            Vector3d toSun = (bootstrap.Model.Get("sun").PositionM - mars.PositionM).Normalized;
            Vector3d shipM = mars.PositionM + toSun * (3.0 * mars.RadiusM);
            bootstrap.Flight.Teleport(shipM, bootstrap.Clock.JulianDate);

            // The marker-size aid eases at 8/s on real time, not the
            // bootstrap's fixed step. Two seconds leaves exp(-16) of it.
            yield return new WaitForSeconds(2f);

            // The opening view is still auto-framing the old target. Take the
            // camera back before setting the off-centre pose, or the follow
            // will slew and zoom under the test.
            bootstrap.Rig.ClearAuto();

            // Aim thirty degrees away so the before frame has Mars off-centre.
            OrbitCamera.AnglesForLookDirection(mars.PositionM - bootstrap.ShipPositionM,
                out float yaw, out float pitch);
            bootstrap.Rig.Orbit.YawDegrees = yaw + 30f;
            bootstrap.Rig.Orbit.PitchDegrees = pitch;
            bootstrap.Rig.Orbit.DistanceUnits =
                bootstrap.Rig.AutoDistanceUnits(bootstrap.ShipPositionM, mars);
            yield return null;
            yield return null;

            string dir = Environment.GetEnvironmentVariable("BODY_CAPTURE_DIR")
                         ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);

            Vector3d before = bootstrap.ShipPositionM;
            Capture(bootstrap.ViewCamera, Path.Combine(dir, "focus-before.png"));

            bootstrap.FocusSelected();
            for (int i = 0; i < 90; i++) yield return null;
            Capture(bootstrap.ViewCamera, Path.Combine(dir, "focus-after.png"));

            Assert.That(Vector3d.Distance(before, bootstrap.ShipPositionM),
                Is.LessThan(1e-6), "the focus must not move the ship");
            Assert.That(bootstrap.Rig.IsFocusingBody, Is.False);
            Assert.That(bootstrap.Rig.AutoTarget, Is.SameAs(mars));
            Debug.Log("focus captures written to " + dir);
        }

        /// <summary>Render the live camera into a linear half-float target and
        /// encode the PNG by hand, matching <c>BodyCaptureTests</c>.</summary>
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
