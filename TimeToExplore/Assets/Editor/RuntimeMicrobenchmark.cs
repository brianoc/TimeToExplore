#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Reflection;
using TimeToExplore.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TimeToExplore.EditorTools
{
    /// <summary>
    /// A tiny numeric probe that runs the same loops as
    /// <c>tools/dotnet-bench --micro</c>. It separates the runtime's speed from
    /// the planner's code so a runtime comparison can name the cause.
    /// </summary>
    public static class RuntimeMicrobenchmark
    {
        private const int WarmupIterations = 1_000_000;
        private const int MeasuredIterations = 10_000_000;

        [MenuItem("Tools/Time to Explore/Run Runtime Microbenchmark")]
        public static void Run()
        {
            try
            {
                var debug = typeof(RuntimeMicrobenchmark).Assembly
                    .GetCustomAttribute<System.Diagnostics.DebuggableAttribute>();
                Debug.Log($"[micro] optimizerDisabled={debug?.IsJITOptimizerDisabled}, " +
                          $"runtime={Environment.Version}");

                foreach (System.Collections.DictionaryEntry entry in
                         Environment.GetEnvironmentVariables())
                {
                    string key = (string)entry.Key;
                    if (key.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase))
                        Debug.Log($"[micro] env {key}={entry.Value}");
                }

                double sink = 0.0;
                Trig(WarmupIterations);
                VectorLoop(WarmupIterations);

                sink += Measure("trig", Trig);
                sink += Measure("vector", VectorLoop);
                Console.WriteLine("micro sink " + sink);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static double Measure(string name, Func<int, double> body)
        {
            var watch = Stopwatch.StartNew();
            double result = body(MeasuredIterations);
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            Debug.Log($"[micro] {name}: {ms:F1} ms, " +
                      $"{ms * 1e6 / MeasuredIterations:F2} ns/op");
            return result;
        }

        internal static double Trig(int iterations)
        {
            double x = 0.1, y = 0.2, z = 0.3, acc = 0.0;
            for (int i = 0; i < iterations; i++)
            {
                double r = Math.Sqrt(x * x + y * y + z * z);
                double s = Math.Sin(x) + Math.Cos(y) + Math.Atan2(z, x);
                acc += r + s;
                x += 1e-7;
                y += 2e-7;
                z += 3e-7;
                if (x > 1.0)
                {
                    x -= 1.0;
                    y -= 1.0;
                    z -= 1.0;
                }
            }

            return acc;
        }

        internal static double VectorLoop(int iterations)
        {
            var v = new Vector3d(1.0, 2.0, 3.0);
            var w = new Vector3d(0.1, 0.2, 0.3);
            Vector3d acc = Vector3d.Zero;
            for (int i = 0; i < iterations; i++)
            {
                v = (v + w).Normalized;
                acc += Vector3d.Cross(v, w) + v * 0.5;
            }

            return acc.SqrMagnitude;
        }
    }
}
#endif
