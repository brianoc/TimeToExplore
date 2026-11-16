#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TimeToExplore.EditorTools
{
    /// <summary>
    /// Creates a ready-to-play scene. The bootstrap also self-installs when you
    /// press Play in an empty scene, so this is only a convenience.
    ///
    /// Two scenes are offered so the system map can be compared with the
    /// current camera: <c>SolarSystem</c> leaves <c>EnableMapView</c> off and
    /// <c>SystemMap</c> turns it on. The scenes hold only the camera and the
    /// bootstrap; the rig is added at runtime.
    /// </summary>
    public static class SolarSystemSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/SolarSystem.unity";
        private const string MapScenePath = SceneFolder + "/SystemMap.unity";

        [MenuItem("Tools/Time to Explore/Create Solar System Scene")]
        public static void CreateScene()
        {
            Create(ScenePath, enableMapView: false);
        }

        [MenuItem("Tools/Time to Explore/Create System Map Scene")]
        public static void CreateMapScene()
        {
            Create(MapScenePath, enableMapView: true);
        }

        private static void Create(string scenePath, bool enableMapView)
        {
            if (!AssetDatabase.IsValidFolder(SceneFolder))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20_000_000f;
            camera.fieldOfView = 60f;
            cameraObject.transform.position = Vector3.zero;

            GameObject root = new GameObject("Time to Explore");
            SolarSystemBootstrap bootstrap = root.AddComponent<SolarSystemBootstrap>();
            bootstrap.EnableMapView = enableMapView;

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
            Debug.Log($"Created {scenePath}. Press Play.");
        }
    }
}
#endif
