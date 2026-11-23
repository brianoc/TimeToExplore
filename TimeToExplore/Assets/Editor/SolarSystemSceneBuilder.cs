#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TimeToExplore.EditorTools
{
    /// <summary>
    /// Creates a ready-to-play scene. The bootstrap also self-installs when you
    /// press Play in an empty scene, so this is only a convenience. The scene
    /// holds only the camera and the bootstrap; the rig is added at runtime.
    /// </summary>
    public static class SolarSystemSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/SolarSystem.unity";

        [MenuItem("Tools/Time to Explore/Create Solar System Scene")]
        public static void CreateScene()
        {
            Create(ScenePath);
        }

        private static void Create(string scenePath)
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

            // A bare URP camera has post-processing off, so the scene builder
            // must opt in exactly as EnsureCamera does, or a regenerated scene
            // silently loses the bloom the committed one has.
            cameraObject.AddComponent<UniversalAdditionalCameraData>()
                .renderPostProcessing = true;

            GameObject root = new GameObject("Time to Explore");
            root.AddComponent<SolarSystemBootstrap>();

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
            Debug.Log($"Created {scenePath}. Press Play.");
        }
    }
}
#endif
