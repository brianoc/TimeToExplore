#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TimeToExplore.EditorTools
{
    /// <summary>Builds the standalone player the player-side route benchmark
    /// runs in. The default backend on this editor is already CoreCLR.</summary>
    public static class PlayerBenchmarkBuild
    {
        private const string Scene = "Assets/Scenes/SolarSystem.unity";

        [MenuItem("Tools/Time to Explore/Build Player Benchmark")]
        public static void Build()
        {
            int exitCode = 1;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
                {
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                            BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                        throw new InvalidOperationException(
                            "Could not switch to the macOS standalone target.");
                }

                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
                    ScriptingImplementation.CoreCLR);
                PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,
                    ApiCompatibilityLevel.NET);

                string output = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../../build/player/TimeToExplore.app"));
                Directory.CreateDirectory(Path.GetDirectoryName(output));

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                Debug.Log($"[player-build] {report.summary.result}, " +
                          $"errors {report.summary.totalErrors}, " +
                          $"size {report.summary.totalSize} bytes, {output}");
                exitCode = report.summary.result == BuildResult.Succeeded
                           && report.summary.totalErrors == 0
                    ? 0
                    : 1;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                if (Application.isBatchMode) EditorApplication.Exit(exitCode);
            }
        }
    }
}
#endif
