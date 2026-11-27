using System.Collections;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Evidence for the opening composition, not a gate: reset to Gateway and
    /// write the first frame — Earth, the Moon and the station from 20° above
    /// the ecliptic — into <c>OPENING_CAPTURE_DIR</c>. It carries only the
    /// <c>capture</c> category, so no gate starts it, and it needs a graphics
    /// device.
    /// </summary>
    [Category("capture")]
    public class OpeningCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;

        [UnityTest]
        public IEnumerator TheOpeningShotFramesEarthAndTheMoon()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindAnyObjectByType<SolarSystemBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);

            bootstrap.CancelFlight();
            bootstrap.DiscardPreview();
            bootstrap.SetFreeFlight(false);

            // Batch mode reports a 640×480 screen even when the capture target
            // is 1600×900. Frame the shot for the shape the PNG will have, so
            // the capture shows the composition the player sees.
            bootstrap.ViewCamera.aspect = (float)Width / Height;
            bootstrap.ResetToGateway();
            bootstrap.Clock.Paused = true;
            bootstrap.ShowHud = true;
            bootstrap.ShowLabels = true;

            Assert.That(bootstrap.Rig.Orbit.PitchDegrees,
                Is.EqualTo(TimeToExplore.Player.OpeningFraming.PitchDegrees)
                    .Within(1e-3f));

            // Let the HUD's opening guard clear and the labels settle.
            for (int frame = 0; frame < 8; frame++) yield return null;

            string dir = System.Environment.GetEnvironmentVariable("OPENING_CAPTURE_DIR")
                         ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "opening-view.png");

            Debug.Log($"opening capture: yaw {bootstrap.Rig.Orbit.YawDegrees:F1}°, " +
                      $"pitch {bootstrap.Rig.Orbit.PitchDegrees:F1}°, " +
                      $"distance {bootstrap.Rig.Orbit.DistanceUnits:F2} units " +
                      $"({bootstrap.Rig.Orbit.DistanceUnits * 1000.0:F0} km)");
            Capture(bootstrap.ViewCamera, path);
            Debug.Log("opening capture written to " + path);
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
