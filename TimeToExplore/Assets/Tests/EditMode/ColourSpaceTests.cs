using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using UnityEditor;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Linear colour space is load-bearing, not a preference: atmospheric
    /// scattering, the sky's tonemapping, and a physical terminator all assume
    /// linear light. These checks pin the project setting and the rule for where
    /// a colour converts, which differs by consumer: Unity converts a material
    /// colour property, a <c>LineRenderer</c> vertex colour is converted by the
    /// two line renderers, and a uGUI tint converts inside the UI shader. Every
    /// source value stays authored in sRGB.
    /// </summary>
    [Category("presentation")]
    public class ColourSpaceTests
    {
        [Test]
        public void TheProjectRendersInLinearColourSpace()
        {
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear),
                "the pipeline, the sky's bloom, and every scattering formula assume linear light");
        }

        [Test]
        public void TheCatalogueBodyColourReachesTheMaterialUnchanged()
        {
            var data = new BodyData { color = new[] { 0.98f, 0.32f, 0.35f } };

            Color shader = BodyView.ShaderColourFrom(data);

            Assert.That(shader, Is.EqualTo(new Color(0.98f, 0.32f, 0.35f)),
                "Unity converts a material colour property from sRGB to linear on " +
                "submit; converting here as well would convert twice");
        }

        [Test]
        public void AMissingCatalogueColourFallsBackToTheNeutralGrey()
        {
            Color shader = BodyView.ShaderColourFrom(new BodyData { color = null });

            Assert.That(shader, Is.EqualTo(new Color(0.7f, 0.7f, 0.7f)));
        }

        [Test]
        public void TheRoutePaletteDescribesTheLookInSrgb()
        {
            Assert.That(RouteColours.For(RouteArcKind.Conic, 0).Colour,
                Is.EqualTo(RouteColours.Conic),
                "the palette is an sRGB description, so the HUD legend can use it directly");
        }

        [Test]
        public void TheTwoLineRenderersConvertTheAuthoredPaletteToLinearLight()
        {
            string rendering = Path.Combine(Application.dataPath, "Scripts", "Rendering");

            StringAssert.Contains("colour.linear", File.ReadAllText(
                    Path.Combine(rendering, "OrbitLine.cs")),
                "an orbit's authored colour must reach the line as linear light");

            StringAssert.Contains("style.Colour.linear", File.ReadAllText(
                    Path.Combine(rendering, "TrajectoryLine.cs")),
                "a route arc's authored colour must reach the line as linear light");
        }

        [Test]
        public void TheOrbitRampKeepsItsSharedAlpha()
        {
            Color mercury = OrbitColours.Planet(0f);
            Color expected = Color.HSVToRGB(
                OrbitColours.MercuryHueDegrees / 360f,
                OrbitColours.MercurySaturation,
                OrbitColours.MercuryValue);
            expected.a = OrbitColours.PlanetAlpha;

            Assert.That(mercury, Is.EqualTo(expected),
                "the ramp stays an sRGB description of the hue, at the shared alpha");
        }

        [Test]
        public void TheBodyShaderConvertsTheAuthoredAmbientFloor()
        {
            Shader shader = Shader.Find(RenderingShaders.BodyLit);
            Assert.That(shader, Is.Not.Null, "TimeToExplore/BodyLit must be under Resources");

            string source = File.ReadAllText(AssetDatabase.GetAssetPath(shader));

            StringAssert.Contains("SRGBToLinear(_Ambient)", source,
                "the 5.5% night-side floor is an sRGB value and must convert to linear light");
        }
    }
}
