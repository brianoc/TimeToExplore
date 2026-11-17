using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The regression that protects the input migration: the legacy
    /// <c>UnityEngine.Input</c> API may appear only in the seam's documentation,
    /// and the Input System may be touched only by the seam. A new call site
    /// outside <c>PlayerControls</c> fails here before it can reintroduce a
    /// backend dependency.
    /// </summary>
    public class InputSeamTests
    {
        private const string SeamFileName = "PlayerControls.cs";

        private static readonly Regex LegacyInputCall = new Regex(
            @"(?<![\w.])Input\.(GetKey|GetKeyDown|GetKeyUp|GetAxis|GetAxisRaw|" +
            @"GetMouseButton|GetMouseButtonDown|GetMouseButtonUp|" +
            @"mousePosition|mouseScrollDelta|anyKey|inputString)\b",
            RegexOptions.Compiled);

        private static readonly Regex LegacyInputType = new Regex(
            @"UnityEngine\.Input(?!System)\b",
            RegexOptions.Compiled);

        private static readonly Regex InputSystemReference = new Regex(
            @"UnityEngine\.InputSystem\b",
            RegexOptions.Compiled);

        [Test]
        public void NoRuntimeScriptOutsideTheSeamCallsTheLegacyInputApi()
        {
            var offenders = new List<string>();

            foreach (string file in RuntimeScripts())
            {
                if (Path.GetFileName(file) == SeamFileName) continue;

                string source = File.ReadAllText(file);
                if (LegacyInputCall.IsMatch(source) || LegacyInputType.IsMatch(source))
                    offenders.Add(Relative(file));
            }

            Assert.That(offenders, Is.Empty,
                "every legacy Input call belongs in PlayerControls: " + string.Join(", ", offenders));
        }

        [Test]
        public void OnlyTheSeamReferencesTheInputSystem()
        {
            var users = new List<string>();

            foreach (string file in RuntimeScripts())
            {
                if (InputSystemReference.IsMatch(File.ReadAllText(file)))
                    users.Add(Relative(file));
            }

            Assert.That(users, Is.EqualTo(new[] { "Scripts/Player/PlayerControls.cs" }),
                "the Input System is reached through one seam, not scattered through the runtime");
        }

        [Test]
        public void TheSeamExistsAndUsesDevicesRatherThanActions()
        {
            string seam = Path.Combine(Application.dataPath, "Scripts", "Player", SeamFileName);
            Assert.That(File.Exists(seam), Is.True, seam);

            string source = File.ReadAllText(seam);
            Assert.That(source, Does.Contain("Keyboard.current"));
            Assert.That(source, Does.Contain("Mouse.current"));
            Assert.That(source, Does.Not.Contain("InputActionAsset"),
                "the scenes and HUD are built at runtime, so direct device polling is the seam");
        }

        [Test]
        public void ActiveInputHandlingStaysOnBothWhileTheHudIsImGui()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset"));
            Assert.That(File.Exists(path), Is.True, path);

            StringAssert.Contains("activeInputHandler: 2", File.ReadAllText(path),
                "IMGUI needs the legacy backend for OnGUI events; see ADR-011");
        }

        private static IEnumerable<string> RuntimeScripts()
        {
            string root = Path.Combine(Application.dataPath, "Scripts");
            Assert.That(Directory.Exists(root), Is.True, root);
            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        }

        private static string Relative(string file) =>
            file.Substring(Application.dataPath.Length).TrimStart('/', '\\')
                .Replace(Path.DirectorySeparatorChar, '/');
    }
}
