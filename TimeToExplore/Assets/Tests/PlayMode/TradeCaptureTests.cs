using System.Collections;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using TimeToExplore.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// Evidence for the trade panel, not a gate. The test flies the cheap
    /// Ares Junction → Gateway route through the real session, opens the details
    /// screen and the market through their real buttons, buys a couple of units,
    /// and writes a screenshot of the game view.
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
    public class TradeCaptureTests
    {
        private const int Width = 1600;
        private const int Height = 900;

        [UnityTest]
        public IEnumerator TheTradePanelShowsAnArrivedMarketWithCargo()
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
            bootstrap.SetScaleIndex(0);

            // The cheap station-to-station route: Ares Junction → Gateway.
            bootstrap.Flight.PlaceAt("ares_junction", bootstrap.Clock.JulianDate);
            TrajectoryPlan preview = bootstrap.Flight.Plot("gateway", bootstrap.Clock.JulianDate);
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.IsFeasible, Is.True, preview.InfeasibleReason);

            bootstrap.BeginPlottedFlight();
            Assert.That(bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Flying),
                bootstrap.Flight.Failure);

            bootstrap.Flight.Advance(bootstrap.Flight.Plan.ArrivalJulianDate, 50_000);
            Assert.That(bootstrap.Flight.Status, Is.EqualTo(FlightStatus.Arrived),
                "the station hold point must be reached");

            // Open the details screen and the market through the real buttons.
            BodyRuntime gateway = bootstrap.Model.Get("gateway");
            bootstrap.Select(gateway);
            hud.OpenDetails();

            // The HUD clears its one-frame opening guards at the end of the
            // Update that opened the panel, so the live state only exists two
            // frames later.
            yield return null;
            yield return null;

            Button trade = ButtonNamed(hud, "TradeButton");
            Assert.That(trade.interactable, Is.True, "the arrived ship may trade");
            trade.onClick.Invoke();
            yield return null;
            yield return null;

            Assert.That(hud.TradeOpen, Is.True);
            Assert.That(hud.TradeRowCount, Is.EqualTo(4), "Gateway offers four commodities");

            // A couple of buys, so the cargo line and the held counts are not empty.
            ButtonNamed(hud, "TradeRow0BuyOne").onClick.Invoke();
            ButtonNamed(hud, "TradeRow0BuyOne").onClick.Invoke();
            ButtonNamed(hud, "TradeRow1BuyOne").onClick.Invoke();
            yield return null;

            Assert.That(bootstrap.Trade.Hold.UsedM3, Is.GreaterThan(0.0));

            string path = Path.GetFullPath("trade-panel.png");
            Debug.Log($"Trade capture: cwd {Directory.GetCurrentDirectory()}, writing {path}");

            Screen.SetResolution(Width, Height, false);

            // Batch mode has no end-of-frame phase to wait on: the test runner
            // refuses WaitForEndOfFrame there, and an ordinary frame is enough.
            if (Application.isBatchMode)
                yield return null;
            else
                yield return new WaitForEndOfFrame();

            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;

            if (!File.Exists(path))
            {
                // Batch mode has no on-screen backbuffer, so the screen
                // capture writes nothing there. Render the same HUD canvas
                // through the game camera into a texture instead.
                Debug.Log("Trade capture: screen capture wrote nothing; " +
                          "rendering the canvas through the view camera");
                yield return CaptureThroughCamera(bootstrap.ViewCamera, hud, path);
            }

            Assert.That(File.Exists(path), Is.True, "the capture must write " + path);
        }

        /// <summary>
        /// Render the live HUD canvas with the game camera into a texture. The
        /// canvas is temporarily a screen-space camera canvas, which is how the
        /// overlay pass would composite it, so the panel pixels are the ones the
        /// player sees. Both the camera target and the canvas are restored.
        /// </summary>
        private static IEnumerator CaptureThroughCamera(Camera camera, SolarSystemHud hud,
            string path)
        {
            Canvas canvas = hud.GetComponentInChildren<Canvas>(true);
            RenderMode renderMode = canvas.renderMode;
            Camera canvasCamera = canvas.worldCamera;
            float planeDistance = canvas.planeDistance;
            RenderTexture previousTarget = camera.targetTexture;

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            Texture2D image = null;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                camera.targetTexture = target;

                // Two real frames, so the canvas rebuilds for the camera and
                // the pipeline draws the world behind it.
                yield return null;
                yield return null;

                RenderTexture.active = target;
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = previousTarget;
                canvas.renderMode = renderMode;
                canvas.worldCamera = canvasCamera;
                canvas.planeDistance = planeDistance;
                if (image != null) Object.Destroy(image);
                target.Release();
                Object.Destroy(target);
            }
        }

        private static Button ButtonNamed(SolarSystemHud hud, string name)
        {
            foreach (Button button in hud.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == name) return button;

            Assert.Fail("no HUD button named " + name);
            return null;
        }
    }
}
