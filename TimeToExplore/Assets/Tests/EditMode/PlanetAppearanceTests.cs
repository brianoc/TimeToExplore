using System;
using System.IO;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Rendering;
using UnityEditor;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The shipped catalogue's appearance data — the nine albedo maps it names
    /// and the atmosphere records the next step will draw — plus the detail
    /// gate and the body shader's marker-versus-map switch. The atmosphere
    /// shell is the last step and is not asserted here.
    /// </summary>
    [Category("presentation")]
    public class PlanetAppearanceTests
    {
        private static readonly string[] TexturedBodies =
        {
            "mercury", "venus", "earth", "mars",
            "jupiter", "saturn", "uranus", "neptune", "moon"
        };

        [Test]
        public void TheEightPlanetsAndTheMoonHaveAlbedoMaps()
        {
            foreach (string id in TexturedBodies)
            {
                BodyData body = CatalogueFixture.Find(id);
                Assert.That(body.albedoTexture, Is.EqualTo($"Planets/{id}_albedo"),
                    $"{id} should name its albedo map by the Planets/<id>_albedo convention");
            }
        }

        [Test]
        public void EveryNamedAlbedoTextureResolvesUnderResources()
        {
            foreach (BodyData body in CatalogueFixture.Data.bodies)
            {
                if (string.IsNullOrEmpty(body.albedoTexture))
                    continue;

                string key = body.albedoTexture;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"Assets/Resources/{key}.jpg");
                if (texture == null)
                {
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"Assets/Resources/{key}.png");
                }

                Assert.That(texture, Is.Not.Null,
                    $"{body.id} names {key}, but Assets/Resources holds no .jpg or .png for it");
                Assert.That(texture.width, Is.GreaterThan(0));
                Assert.That(texture.height, Is.GreaterThan(0));
            }
        }

        [Test]
        public void EveryAtmosphereIsAPlausibleRim()
        {
            foreach (BodyData body in CatalogueFixture.Data.bodies)
            {
                // JsonUtility turns a null object field into a default instance,
                // so "no atmosphere" reads as heightM 0 rather than null.
                if (!body.HasAtmosphere)
                    continue;

                AtmosphereData atmosphere = body.atmosphere;
                Assert.That(atmosphere.heightM, Is.GreaterThan(0.0),
                    $"{body.id} has a non-positive atmosphere height");
                Assert.That(atmosphere.heightM, Is.LessThan(0.06 * body.radiusM),
                    $"{body.id}'s atmosphere is more than 6% of its radius");

                Assert.That(atmosphere.color, Is.Not.Null, $"{body.id} has no atmosphere colour");
                Assert.That(atmosphere.color.Length, Is.EqualTo(3),
                    $"{body.id}'s atmosphere colour must have three components");
                foreach (float channel in atmosphere.color)
                {
                    Assert.That(channel, Is.InRange(0f, 1f),
                        $"{body.id}'s atmosphere colour is outside [0, 1]");
                }

                Assert.That(atmosphere.intensity, Is.GreaterThan(0f),
                    $"{body.id}'s atmosphere intensity must be positive");
                Assert.That(atmosphere.intensity, Is.LessThanOrEqualTo(2f),
                    $"{body.id}'s atmosphere intensity must not exceed 2");
            }
        }

        [Test]
        public void MercuryHasNoAtmosphereAndEarthsIsOneHundredKilometres()
        {
            Assert.That(CatalogueFixture.Find("mercury").HasAtmosphere, Is.False,
                "airless Mercury must not get an atmosphere shell");

            AtmosphereData earth = CatalogueFixture.Find("earth").atmosphere;
            Assert.That(earth, Is.Not.Null);
            Assert.That(earth.heightM, Is.EqualTo(100_000.0).Within(1e-6),
                "Earth's visible atmosphere is 100 km");
        }

        [Test]
        public void TheDetailGateOpensAtTheTrueAngularRadius()
        {
            Assert.That(BodyAppearance.AlbedoMinAngularRadius, Is.EqualTo(0.025f));

            Assert.That(BodyAppearance.UsesAlbedo(0f), Is.False,
                "a body below the gate is a flat marker");
            Assert.That(BodyAppearance.UsesAlbedo(0.0249f), Is.False,
                "just below the threshold is still a marker");
            Assert.That(BodyAppearance.UsesAlbedo(BodyAppearance.AlbedoMinAngularRadius),
                Is.True, "the threshold itself opens the gate");
            Assert.That(BodyAppearance.UsesAlbedo(0.026f), Is.True);
            Assert.That(BodyAppearance.UsesAlbedo(0.4f), Is.True,
                "a close pass samples the map");
        }

        [Test]
        public void TheBodyShaderDeclaresTheAlbedoSwitch()
        {
            Shader shader = Shader.Find(RenderingShaders.BodyLit);
            Assert.That(shader, Is.Not.Null, RenderingShaders.BodyLit + " was not found");

            var material = new Material(shader);
            try
            {
                Assert.That(material.HasProperty("_Color"), Is.True,
                    "the catalogue tint stays the marker colour");
                Assert.That(material.HasProperty("_Ambient"), Is.True);
                Assert.That(material.HasProperty("_MainTex"), Is.True,
                    "the body shader must declare the albedo map");
                Assert.That(material.HasProperty("_HasAlbedo"), Is.True,
                    "the body shader must declare the marker-versus-map switch");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        /// <summary>
        /// The map imports sRGB, so the sampler already delivers linear light.
        /// A second conversion in the shader would darken and over-saturate
        /// every planet, so the only <c>SRGBToLinear</c> left is the ambient
        /// literal's.
        /// </summary>
        [Test]
        public void TheBodyShaderSamplesTheMapWithoutConvertingItAgain()
        {
            Shader shader = Shader.Find(RenderingShaders.BodyLit);
            Assert.That(shader, Is.Not.Null, RenderingShaders.BodyLit + " was not found");

            string source = File.ReadAllText(AssetDatabase.GetAssetPath(shader));

            StringAssert.Contains("SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex", source,
                "the body shader must sample the albedo map");
            Assert.That(CountCodeOccurrences(source, "SRGBToLinear"), Is.EqualTo(1),
                "only _Ambient converts; the sRGB albedo map arrives linear");
            StringAssert.Contains("SRGBToLinear(_Ambient)", source,
                "the one conversion is the ambient literal");
        }

        /// <summary>Count a token outside <c>//</c> comments, so a comment that
        /// names the function cannot fool the assertion.</summary>
        private static int CountCodeOccurrences(string source, string token)
        {
            int found = 0;
            foreach (string line in source.Split('\n'))
            {
                string code = line;
                int comment = code.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) code = code.Substring(0, comment);

                int index = 0;
                while ((index = code.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
                {
                    found++;
                    index += token.Length;
                }
            }

            return found;
        }
    }
}
