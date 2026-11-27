using System.Collections;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TimeToExplore.Tests.PlayMode
{
    /// <summary>
    /// The albedo detail gate in the real frame loop. A body close enough that
    /// its disc is readable loads and samples its catalogue map; a distant body
    /// stays a flat marker with no texture on the material. The framing maths
    /// follows <c>BodyCaptureTests</c>: park the ship at radius over the wanted
    /// apparent radius, on the terminator's side.
    /// <para>
    /// The atmosphere shell rides the same two regimes: an atmospheric body
    /// close up gains its additive rim, a distant one hides it, and an airless
    /// body has no shell at all.
    /// </para>
    /// </summary>
    [Category("playmode")]
    public class PlanetDetailPlayModeTests
    {
        /// <summary>The same close framing the capture suite uses.</summary>
        private const float CloseAngularRadius = 0.4f;

        /// <summary>Far enough below the 0.025 rad gate to be unambiguous.</summary>
        private const float FarAngularRadius = 0.001f;

        private SolarSystemBootstrap _bootstrap;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return null;

            _bootstrap = Object.FindAnyObjectByType<SolarSystemBootstrap>();
            Assert.That(_bootstrap, Is.Not.Null,
                "RuntimeInitializeOnLoad must create the bootstrap in a bare play session");

            _bootstrap.CancelFlight();
            _bootstrap.DiscardPreview();
            _bootstrap.SetFreeFlight(false);
            _bootstrap.SetScaleIndex(0);
            _bootstrap.Clock.JulianDate = _bootstrap.Model.Data.startJulianDate;
            _bootstrap.ResetToGateway();
            _bootstrap.DeltaTimeSeconds = () => 1f;

            yield return null;

            // The gate reads the body's distance every frame, so freeze the
            // clock before the teleport: a running warp would carry the target
            // away between the teleport and the two frames the test needs.
            _bootstrap.Clock.Paused = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _bootstrap?.CancelFlight();
            _bootstrap?.DiscardPreview();
            if (_bootstrap != null) _bootstrap.Clock.Paused = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ACloseBodySamplesItsAlbedoAndADistantOneStaysAFlatMarker()
        {
            BodyRuntime earth = _bootstrap.Model.Get("earth");
            BodyView view = _bootstrap.Renderer.GetView(earth);
            Assert.That(view, Is.Not.Null, "the renderer must build a view for Earth");

            Assert.That(AlbedoState(view), Is.EqualTo(0f),
                "Gateway is far enough from Earth that it starts as a flat marker");

            TeleportAtApparentRadius(earth, CloseAngularRadius);
            yield return null;
            yield return null;

            Material material = view.Renderer.sharedMaterial;
            Assert.That(material.GetFloat("_HasAlbedo"), Is.EqualTo(1f),
                "at 0.4 rad Earth must sample its albedo map");

            Texture2D map = Resources.Load<Texture2D>(earth.Data.albedoTexture);
            Assert.That(map, Is.Not.Null,
                "the catalogue names " + earth.Data.albedoTexture +
                ", which Resources must hold");
            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(map),
                "the close Earth must wear the Earth map");

            // Pull back until the true disc is far below the gate; the map
            // stays cached on the material, but the marker switch turns off.
            TeleportAtApparentRadius(earth, FarAngularRadius);
            yield return null;
            yield return null;

            Assert.That(AlbedoState(view), Is.EqualTo(0f),
                "a distant Earth is a flat marker again");
        }

        [UnityTest]
        public IEnumerator EarthShowsItsAtmosphereShellOnlyWhenCloseEnough()
        {
            BodyRuntime earth = _bootstrap.Model.Get("earth");
            BodyView view = _bootstrap.Renderer.GetView(earth);
            Assert.That(view, Is.Not.Null, "the renderer must build a view for Earth");
            Assert.That(view.AtmosphereRenderer, Is.Not.Null,
                "Earth has an atmosphere record, so it gets a shell");
            Assert.That(view.AtmosphereRenderer.enabled, Is.False,
                "Gateway is far below the atmosphere gate");

            TeleportAtApparentRadius(earth, CloseAngularRadius);
            yield return null;
            yield return null;

            Assert.That(view.AtmosphereRenderer.enabled, Is.True,
                "at 0.4 rad the rim shell must be drawing");
            Assert.That(view.AtmosphereRenderer.transform.localScale.x,
                Is.EqualTo(1.0157f).Within(1e-4f),
                "the shell sits at Earth's real 100 km atmosphere height");

            TeleportAtApparentRadius(earth, FarAngularRadius);
            yield return null;
            yield return null;

            Assert.That(view.AtmosphereRenderer.enabled, Is.False,
                "a distant Earth is a bare marker again");
        }

        [Test]
        public void MercuryHasNoAtmosphereShell()
        {
            BodyRuntime mercury = _bootstrap.Model.Get("mercury");
            BodyView view = _bootstrap.Renderer.GetView(mercury);
            Assert.That(view, Is.Not.Null, "the renderer must build a view for Mercury");
            Assert.That(view.AtmosphereRenderer, Is.Null,
                "airless Mercury must not get an atmosphere shell");
        }

        private static float AlbedoState(BodyView view) =>
            view.Renderer.sharedMaterial.GetFloat("_HasAlbedo");

        /// <summary>
        /// Park the ship where the body subtends the given true angular radius,
        /// on the terminator's side so the day and night halves both show.
        /// </summary>
        private void TeleportAtApparentRadius(BodyRuntime body, float angularRadius)
        {
            Vector3d bodyPosition = body.PositionM;
            Vector3d sunPosition = _bootstrap.Model.Get("sun").PositionM;
            Vector3d awayFromSun = bodyPosition - sunPosition;
            Vector3d side = Vector3d.Cross(awayFromSun.Normalized, new Vector3d(0, 1, 0)).Normalized;

            _bootstrap.Flight.Teleport(
                bodyPosition + side * (body.RadiusM / angularRadius),
                _bootstrap.Clock.JulianDate);
        }
    }
}
