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
        private const string MapScenePath = "Assets/Scenes/SystemMap.unity";

        [Test]
        public void ShippedSceneLoadsWithTheBootstrapAttached()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.That(scene.isLoaded, Is.True);

                SolarSystemBootstrap bootstrap = FindBootstrap(scene);
                Assert.That(bootstrap, Is.Not.Null,
                    "the scene lost its reference to SolarSystemBootstrap");
                Assert.That(bootstrap.Model, Is.Null,
                    "Awake should not run in edit mode");
                Assert.That(bootstrap.EnableMapView, Is.False,
                    "the shipped scene stays on the current camera");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheComparisonSceneTurnsTheMapSwitchOn()
        {
            Scene scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.That(scene.isLoaded, Is.True);

                SolarSystemBootstrap bootstrap = FindBootstrap(scene);
                Assert.That(bootstrap, Is.Not.Null,
                    "the comparison scene lost its reference to SolarSystemBootstrap");
                Assert.That(bootstrap.EnableMapView, Is.True,
                    "the comparison scene opts in to the system map");
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

        private static SolarSystemBootstrap FindBootstrap(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SolarSystemBootstrap bootstrap = root.GetComponentInChildren<SolarSystemBootstrap>();
                if (bootstrap != null) return bootstrap;
            }

            return null;
        }
    }
}
