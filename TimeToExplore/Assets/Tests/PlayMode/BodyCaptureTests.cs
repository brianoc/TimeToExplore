using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Evidence for the way a body renders, not a gate: the albedo map, the
    /// atmosphere rim, the terminator, and the sky behind them. It parks the
    /// ship beside Earth, Mars, Jupiter, Saturn, and the Moon, frames each one
    /// through the live view camera, and writes <c>body-*.png</c> into
    /// <c>BODY_CAPTURE_DIR</c>. The same shots are the before/after evidence for
    /// a colour-space or lighting change.
    ///
    /// It carries only the <c>capture</c> category, so the default headless
    /// <c>tools/check.sh</c> run never starts it, and it needs a graphics device:
    /// run it without <c>-nographics</c>, as
    /// <c>Docs/technical/rendering-scale.md</c> describes.
    /// </summary>
    [Category("capture")]
    public class BodyCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;

        /// <summary>Each body is framed at this apparent radius: about 70% of
        /// the half-height at the 60° field of view.</summary>
        private const float AngularRadius = 0.40f;

        [UnityTest]
        public IEnumerator TheBodiesRenderWithTheShippedShaders()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindFirstObjectByType<SolarSystemBootstrap>();
            Assert.That(bootstrap, Is.Not.Null,
                "RuntimeInitializeOnLoad must create the bootstrap in a bare play session");

            bootstrap.CancelFlight();
            bootstrap.DiscardPreview();
            bootstrap.Clock.JulianDate = bootstrap.Model.Data.startJulianDate;

            // Freeze the clock: the settle time below is real time, and a
            // running warp would carry each planet out of frame between the
            // teleport and the shot.
            bootstrap.Clock.Paused = true;

            bootstrap.ShowHud = false;
            bootstrap.ShowLabels = false;
            bootstrap.Renderer.ShowOrbits = false;
            bootstrap.ShipMarker.SetVisible(false);

            string dir = Environment.GetEnvironmentVariable("BODY_CAPTURE_DIR")
                         ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);

            yield return Shoot(bootstrap, "earth", dir);
            yield return Shoot(bootstrap, "mars", dir);
            yield return Shoot(bootstrap, "jupiter", dir);
            yield return Shoot(bootstrap, "saturn", dir);
            yield return Shoot(bootstrap, "moon", dir);
        }

        private static IEnumerator Shoot(SolarSystemBootstrap bootstrap, string bodyId, string dir)
        {
            BodyRuntime body = bootstrap.Model.Get(bodyId);
            Assert.That(body, Is.Not.Null, "no body '" + bodyId + "' in the catalogue");

            // Stand off the body at the distance that gives the target
            // apparent radius, on the terminator's side so the day and night
            // halves both show.
            Vector3d bodyPosition = body.PositionM;
            Vector3d sunPosition = bootstrap.Model.Get("sun").PositionM;
            Vector3d awayFromSun = bodyPosition - sunPosition;
            Vector3d side = Vector3d.Cross(awayFromSun.Normalized, new Vector3d(0, 1, 0)).Normalized;

            bootstrap.Flight.Teleport(bodyPosition + side * (body.RadiusM / AngularRadius),
                bootstrap.Clock.JulianDate);

            // The views refresh in LateUpdate and the marker-size aid eases at
            // 8/s from whatever size the body had at the previous stop. A
            // teleport changes the apparent size by orders of magnitude, so the
            // shot waits until the ease has settled: two seconds leaves
            // exp(-16) of it.
            yield return new WaitForSeconds(2f);

            Camera camera = bootstrap.ViewCamera;
            Vector3 target = ((bodyPosition - bootstrap.ShipPositionM) * 1e-6).ToUnity();
            camera.transform.position = Vector3.zero;
            camera.transform.LookAt(target, Vector3.up);
            camera.nearClipPlane = 0.0002f;

            string path = Path.Combine(dir, "body-" + bodyId + ".png");
            Capture(camera, path);
            Debug.Log("body capture written to " + path);
        }

        /// <summary>
        /// Render the live camera into a texture and write a PNG. Batch mode
        /// has no backbuffer, so this is the only path that works there.
        ///
        /// The target is a linear half-float buffer and the PNG is encoded by
        /// hand. With HDR and post-processing on, URP blits its own intermediate
        /// buffer into the camera target without an sRGB write, so an 8-bit
        /// target would store linear values, read back dark, and band the
        /// terminators on the way through the encode. A gamma project already
        /// holds display values and skips the encode.
        /// </summary>
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
