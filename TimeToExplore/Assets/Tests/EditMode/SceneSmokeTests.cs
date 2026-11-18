using NUnit.Framework;
using TimeToExplore;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TimeToExplore.Tests.EditMode
{
    [Category("presentation")]
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

        [TestCase(ScenePath)]
        [TestCase(MapScenePath)]
        public void SceneCamerasHaveUniversalAdditionalCameraData(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Camera camera = FindCamera(scene);
                Assert.That(camera, Is.Not.Null, "the scene lost its camera");

                UniversalAdditionalCameraData data =
                    camera.GetComponent<UniversalAdditionalCameraData>();
                Assert.That(data, Is.Not.Null,
                    "the camera needs UniversalAdditionalCameraData under URP");
                Assert.That(data.renderType, Is.EqualTo(CameraRenderType.Base));
                Assert.That(data.renderPostProcessing, Is.False,
                    "the project renders without post-processing");
                Assert.That(data.requiresDepthTexture, Is.False);
                Assert.That(data.requiresColorTexture, Is.False);

                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(camera.backgroundColor, Is.EqualTo(Color.black));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Camera FindCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera camera = root.GetComponentInChildren<Camera>();
                if (camera != null) return camera;
            }

            return null;
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
