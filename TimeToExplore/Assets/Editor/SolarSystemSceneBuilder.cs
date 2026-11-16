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
    /// </summary>
    public static class SolarSystemSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePath = SceneFolder + "/SolarSystem.unity";

        [MenuItem("Tools/Time to Explore/Create Solar System Scene")]
        public static void CreateScene()
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
            root.AddComponent<SolarSystemBootstrap>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log($"Created {ScenePath}. Press Play.");
        }
    }
}
#endif
