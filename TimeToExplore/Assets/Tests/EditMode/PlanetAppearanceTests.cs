using NUnit.Framework;
using TimeToExplore.Core;
using UnityEditor;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The shipped catalogue's appearance data: the nine albedo maps it names
    /// and the atmosphere records the next steps will draw. This is the data
    /// half of the appearance work; the shader, the detail gate and the
    /// atmosphere shell are separate steps and are not asserted here.
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
    }
}
