using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The regression that protects the input and UI migration: the legacy
    /// <c>UnityEngine.Input</c> API may not appear outside the seam, the Input
    /// System may be touched only by the seam and the uGUI input module, no
    /// runtime script draws immediate-mode GUI, and Active Input Handling is
    /// pinned to the new backend.
    /// </summary>
    [Category("core")]
    public class InputSeamTests
    {
        private const string SeamFileName = "PlayerControls.cs";
        private const string UiInputFileName = "UiInput.cs";

        private static readonly string[] InputSystemFiles =
        {
            "Scripts/Player/PlayerControls.cs",
            "Scripts/UI/UiInput.cs"
        };

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

        private static readonly Regex ImGuiMethod = new Regex(
            @"\bvoid\s+OnGUI\s*\(", RegexOptions.Compiled);

        private static readonly Regex ImGuiCall = new Regex(
            @"(?<![\w.])GUI\.(Label|Button|DrawTexture|Box|TextField|Toggle|" +
            @"color|enabled|skin|BeginGroup|EndGroup)\b",
            RegexOptions.Compiled);

        private static readonly Regex ImGuiStyle = new Regex(
            @"\bnew\s+GUIStyle\b", RegexOptions.Compiled);

        private static readonly Regex ImGuiTexture = new Regex(
            @"\bTexture2D\.whiteTexture\b", RegexOptions.Compiled);

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
        public void OnlyTheSeamAndTheUiInputModuleNameTheInputSystem()
        {
            var users = new List<string>();

            foreach (string file in RuntimeScripts())
            {
                if (InputSystemReference.IsMatch(File.ReadAllText(file)))
                    users.Add(Relative(file));
            }

            users.Sort();
            Assert.That(users, Is.EqualTo(InputSystemFiles),
                "gameplay input stays behind PlayerControls and uGUI input behind UiInput");
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
        public void TheUiInputModuleExistsAndAssignsDefaultActions()
        {
            string file = Path.Combine(Application.dataPath, "Scripts", "UI", UiInputFileName);
            Assert.That(File.Exists(file), Is.True, file);

            string source = File.ReadAllText(file);
            Assert.That(source, Does.Contain("InputSystemUIInputModule"));
            Assert.That(source, Does.Contain("AssignDefaultActions"));
            Assert.That(source, Does.Contain("FindFirstObjectByType<EventSystem>"),
                "an authored EventSystem must not be double-equipped");
        }

        [Test]
        public void NoRuntimeScriptDrawsImmediateModeGui()
        {
            var offenders = new List<string>();

            foreach (string file in RuntimeScripts())
            {
                string source = File.ReadAllText(file);
                if (ImGuiMethod.IsMatch(source) || ImGuiCall.IsMatch(source) ||
                    ImGuiStyle.IsMatch(source) || ImGuiTexture.IsMatch(source))
                    offenders.Add(Relative(file));
            }

            Assert.That(offenders, Is.Empty,
                "the HUD is uGUI; no runtime script may fall back to IMGUI: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void ActiveInputHandlingIsTheNewBackendOnly()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset"));
            Assert.That(File.Exists(path), Is.True, path);

            string settings = File.ReadAllText(path);
            StringAssert.Contains("activeInputHandler: 1", settings,
                "uGUI receives Input System events; see ADR-013");
            Assert.That(settings, Does.Not.Contain("activeInputHandler: 2"),
                "the legacy backend is no longer needed by the IMGUI HUD");
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
