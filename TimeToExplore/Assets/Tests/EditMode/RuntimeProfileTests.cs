using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The Unity 7 experiment changed the editor's runtime and the language the
    /// project compiles against. These checks pin what the editor actually
    /// provides, so a later alpha cannot quietly fall back to .NET Standard and
    /// leave the benchmark comparing the wrong runtimes.
    /// </summary>
    [Category("core")]
    public class RuntimeProfileTests
    {
#if UNITY_7000_0_OR_NEWER
        [Test]
        public void Editor_runs_user_scripts_on_dotnet_10()
        {
            Assert.That(Environment.Version.Major, Is.GreaterThanOrEqualTo(10),
                $"Editor runtime is {Environment.Version}");
        }

        [Test]
        public void Editor_builds_user_scripts_without_optimisation()
        {
            Assembly assembly = typeof(TripPlanner).Assembly;
            var debug = assembly.GetCustomAttribute<DebuggableAttribute>();

            TestContext.WriteLine($"runtime: {Environment.Version}");
            TestContext.WriteLine($"framework: {RuntimeInformation.FrameworkDescription}");
            TestContext.WriteLine($"debuggable: {Describe(debug)}");
            TestContext.WriteLine($"tiered compilation: {Switch("System.Runtime.TieredCompilation")}");
            TestContext.WriteLine($"tiered PGO: {Switch("System.Runtime.TieredPGO")}");
            TestContext.WriteLine($"server GC: {GCSettings.IsServerGC}");
            TestContext.WriteLine($"dynamic code: compiled={RuntimeFeature.IsDynamicCodeCompiled}, " +
                                  $"supported={RuntimeFeature.IsDynamicCodeSupported}");
            TestContext.WriteLine($"DOTNET_TieredCompilation: " +
                                  $"{Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "(unset)"}");

            Assert.That(debug, Is.Not.Null, "the Editor must stamp DebuggableAttribute");
            Assert.That(debug.IsJITOptimizerDisabled, Is.True,
                "the comparison assumes the Editor's user assemblies are debug-built; " +
                "if Unity starts optimizing them, re-measure the route tables");
        }

        private static string Describe(DebuggableAttribute debug) =>
            debug == null
                ? "none"
                : $"tracking={debug.IsJITTrackingEnabled}, " +
                  $"optimizerDisabled={debug.IsJITOptimizerDisabled}";

        private static string Switch(string name) =>
            AppContext.TryGetSwitch(name, out bool value) ? value.ToString() : "(unset)";

        [Test]
        public void Editor_compiles_user_scripts_as_csharp_14()
        {
            var probe = new FieldKeywordProbe { Value = 7 };
            Assert.That(probe.Value, Is.EqualTo(7));
        }

        /// <summary>The <c>field</c> contextual keyword arrived in C# 14. This
        /// type fails to compile on an older language version, which is the
        /// point of the check.</summary>
        private sealed class FieldKeywordProbe
        {
            public int Value
            {
                get => field;
                set => field = value;
            }
        }
#endif
    }
}
