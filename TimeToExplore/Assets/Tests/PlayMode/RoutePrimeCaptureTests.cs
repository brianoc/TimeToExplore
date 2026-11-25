using System.Collections;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Evidence for the selection-primed route plan, not a gate. The test opens
    /// Mars's details screen through the real HUD, records frames while the
    /// selection's plan builds and publishes, and records the camera move the
    /// Plot route click then asks for. The frames are written beside the test
    /// project for a clip to be assembled from them.
    ///
    /// It carries only the <c>capture</c> category so the default
    /// <c>tools/check.sh</c> run, which is headless, never starts it. Run it by
    /// hand without <c>-nographics</c>:
    /// <code>
    /// Unity -batchmode -projectPath &lt;test project&gt; -runTests \
    ///   -testPlatform PlayMode -testCategory capture -testResults ...
    /// </code>
    /// </summary>
    [Category("capture")]
    public class RoutePrimeCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;

        /// <summary>The frame step the bootstrap is given for the whole clip.
        /// Writing a PNG costs far more than a rendered frame, so real frame
        /// time would race the 1.5 s camera move past in two recorded frames;
        /// a fixed step makes each recorded frame exactly one 15 fps frame of
        /// motion, and the clip plays the move as it looks on screen.</summary>
        private const float FrameSeconds = 1f / 15f;

        /// <summary>One frame in four of the warm-up is kept: the picture does
        /// not change while the plan builds, so sampling it keeps the clip from
        /// spending a minute on a still frame.</summary>
        private const int RecordSample = 4;

        [UnityTest]
        public IEnumerator SelectingMarsPlotsTheRouteBeforeTheClick()
        {
            yield return null;

            SolarSystemBootstrap bootstrap =
                Object.FindFirstObjectByType<SolarSystemBootstrap>();
            Assert.That(bootstrap, Is.Not.Null,
                "RuntimeInitializeOnLoad must create the bootstrap in a bare play session");

            var hud = bootstrap.GetComponent<SolarSystemHud>();
            Assert.That(hud, Is.Not.Null, "the bootstrap must add the HUD");

            bootstrap.CancelFlight();
            bootstrap.DiscardPreview();
            bootstrap.ShowHud = true;
            bootstrap.ShowLabels = true;
            bootstrap.Clock.JulianDate = bootstrap.Model.Data.startJulianDate;
            bootstrap.Clock.Paused = true;
            bootstrap.SetScaleIndex(0);
            bootstrap.DeltaTimeSeconds = () => FrameSeconds;

            Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
            RenderMode renderMode = canvas.renderMode;
            Camera canvasCamera = canvas.worldCamera;
            float planeDistance = canvas.planeDistance;
            Camera camera = bootstrap.ViewCamera;
            RenderTexture previousTarget = camera.targetTexture;

            string directory = Path.GetFullPath("route-prime-frames");
            Directory.CreateDirectory(directory);
            foreach (string stale in Directory.GetFiles(directory, "*.png"))
                File.Delete(stale);

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            int frame = 0;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                camera.targetTexture = target;

                // An idle ship, before the selection.
                yield return null;
                yield return null;
                for (int i = 0; i < 8; i++)
                {
                    yield return null;
                    Record(target, image, directory, ref frame);
                }

                // The selection warms the plan and opens the screen. Nothing on
                // screen changes while it warms: no route, no flight panel, and
                // the footer still offers to plot.
                BodyRuntime mars = bootstrap.Model.Get("mars");
                bootstrap.Select(mars);
                hud.OpenDetails();
                for (int i = 0; i < 10; i++)
                {
                    yield return null;
                    Record(target, image, directory, ref frame);
                }

                // Sample the warm-up while it runs; the picture stays the same
                // because the plan is held back until the click asks for it.
                int rendered = 0;
                while (!bootstrap.Flight.HasPrimedPreview("mars") && frame < 200)
                {
                    yield return null;
                    if (rendered++ % RecordSample != 0) continue;
                    Record(target, image, directory, ref frame);
                }

                Assert.That(bootstrap.Flight.HasPreview, Is.False,
                    "a warmed plan shows nothing");
                Assert.That(bootstrap.Trajectory.IsShowing, Is.False,
                    "a warmed plan draws no route");

                // The click that asks for the view: the warmed plan is adopted
                // in the same frame, so the route appears at once and the
                // camera eases to it over the transition's own duration.
                Button route = ButtonNamed(hud, "RouteButton");
                Assert.That(route.interactable, Is.True);
                route.onClick.Invoke();
                Assert.That(bootstrap.Flight.HasPreview, Is.True,
                    "the click adopts the warmed plan in its own frame");
                Assert.That(bootstrap.Rig.RouteView.IsActive, Is.True,
                    "the click must take the camera");

                for (int i = 0; i < 24; i++)
                {
                    yield return null;
                    Record(target, image, directory, ref frame);
                }

                Assert.That(bootstrap.Rig.RouteView.IsActive, Is.False,
                    "the move must finish inside the recorded frames");
            }
            finally
            {
                canvas.renderMode = renderMode;
                canvas.worldCamera = canvasCamera;
                canvas.planeDistance = planeDistance;
                camera.targetTexture = previousTarget;
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }

            Assert.That(frame, Is.GreaterThan(20), "the capture must write a sequence");
            Debug.Log($"Route prime capture: {frame} frames in {directory}");
        }

        /// <summary>Render the live frame with the HUD composited, and write it
        /// as the next numbered PNG. The canvas is already a screen-space camera
        /// canvas for the duration of the sequence, which is how the overlay
        /// pass would draw it, so the pixels are the ones the player sees.</summary>
        private static void Record(RenderTexture target, Texture2D image, string directory,
            ref int frame)
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();

            string path = Path.Combine(directory, frame.ToString("D4") + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            frame++;
        }

        private static Button ButtonNamed(SolarSystemHud hud, string name)
        {
            foreach (Button button in hud.GetComponentsInChildren<Button>(true))
            {
                if (button.gameObject.name == name) return button;
            }

            Assert.Fail("no button named " + name);
            return null;
        }
    }
}
