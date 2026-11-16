using NUnit.Framework;
using TimeToExplore;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimeToExplore.Tests.EditMode
{
    public class SceneSmokeTests
    {
        private const string ScenePath = "Assets/Scenes/SolarSystem.unity";

        [Test]
        public void ShippedSceneLoadsWithTheBootstrapAttached()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.That(scene.isLoaded, Is.True);

                var bootstrap = Object.FindFirstObjectByType<SolarSystemBootstrap>();
                Assert.That(bootstrap, Is.Not.Null,
                    "the scene lost its reference to SolarSystemBootstrap");
                Assert.That(bootstrap.Model, Is.Null,
                    "Awake should not run in edit mode");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void CatalogueResourceIsImportable()
        {
            TextAsset catalogue = Resources.Load<TextAsset>("solar_system");

            Assert.That(catalogue, Is.Not.Null);
            Assert.That(catalogue.text, Does.Contain("\"bodies\""));
        }
    }
}
