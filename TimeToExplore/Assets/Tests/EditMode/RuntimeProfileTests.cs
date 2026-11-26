using System;
using NUnit.Framework;

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
