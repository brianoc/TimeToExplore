using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TimeToExplore;
using TimeToExplore.Core;
using TimeToExplore.EditorTools;
using TimeToExplore.Player;
using TimeToExplore.Simulation;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The assembly graph is part of the design: Core ← Simulation ←
    /// Presentation ← Editor/Tests. These tests read the <c>.asmdef</c> files as
    /// the source of truth, so a stray reference or a duplicate runtime assembly
    /// fails here instead of quietly re-coupling the layers. See
    /// <c>Docs/decisions/adr-012-assembly-layout.md</c>.
    /// </summary>
    public class AssemblyLayoutTests
    {
        private const string Core = "TimeToExplore.Core";
        private const string Simulation = "TimeToExplore.Simulation";
        private const string Presentation = "TimeToExplore.Presentation";
        private const string EditorAssembly = "TimeToExplore.Editor";
        private const string Tests = "TimeToExplore.Tests.EditMode";

        /// <summary>Layer order; an assembly may only reference an earlier one.</summary>
        private static readonly string[] Layers =
        {
            Core, Simulation, Presentation, EditorAssembly, Tests
        };

        private static readonly string[] PresentationFolders =
        {
            "Scripts/Player",
            "Scripts/Rendering",
            "Scripts/UI"
        };

        private static readonly Regex GuidLine = new Regex(
            @"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

        [Test]
        public void CoreHasNoProjectReferences()
        {
            AsmdefFile core = Load("Scripts/Core/TimeToExplore.Core.asmdef");

            Assert.That(core.Json.name, Is.EqualTo(Core));
            Assert.That(ProjectReferences(core.Json), Is.Empty,
                "Core is the bottom layer and must not depend on another project assembly");
            Assert.That(References(core.Json), Is.Empty,
                "Core has no package or project references at all");
        }

        [Test]
        public void SimulationReferencesCoreAndNoOtherProjectAssembly()
        {
            AsmdefFile simulation = Load("Scripts/Simulation/TimeToExplore.Simulation.asmdef");

            Assert.That(simulation.Json.name, Is.EqualTo(Simulation));
            Assert.That(ProjectReferences(simulation.Json), Is.EqualTo(new[] { Core }),
                "Simulation is plain C# over Core and must not reach into Presentation");
            Assert.That(simulation.Json.noEngineReferences, Is.True,
                "the flight core is plain C#: it must not take a UnityEngine reference");
        }

        [Test]
        public void PresentationReferencesCoreAndSimulationAndTheInputSystem()
        {
            AsmdefFile presentation = Load("Scripts/TimeToExplore.Presentation.asmdef");

            Assert.That(presentation.Json.name, Is.EqualTo(Presentation));
            Assert.That(presentation.Json.rootNamespace, Is.EqualTo("TimeToExplore"),
                "the presentation keeps the project root namespace");
            Assert.That(ProjectReferences(presentation.Json),
                Is.EquivalentTo(new[] { Core, Simulation }),
                "Presentation depends on Core and Simulation and on no other project assembly");
            CollectionAssert.Contains(References(presentation.Json), "Unity.InputSystem",
                "PlayerControls is the one gameplay seam and lives in Presentation");
            CollectionAssert.Contains(References(presentation.Json), "UnityEngine.UI",
                "the HUD is uGUI and needs the Unity UI assembly");
            CollectionAssert.DoesNotContain(References(presentation.Json), EditorAssembly);
            CollectionAssert.DoesNotContain(References(presentation.Json), Tests);
        }

        [Test]
        public void EditorAssemblyIsEditorOnlyAndReferencesTheRuntime()
        {
            AsmdefFile editor = Load("Editor/TimeToExplore.Editor.asmdef");

            Assert.That(editor.Json.name, Is.EqualTo(EditorAssembly));
            Assert.That(ProjectReferences(editor.Json),
                Is.EquivalentTo(new[] { Core, Simulation, Presentation }),
                "the editor tools may use the runtime but the runtime must not use them");
            CollectionAssert.AreEquivalent(new[] { "Editor" }, IncludePlatforms(editor.Json),
                "editor tooling must never enter a player build");
            Assert.That(editor.Json.excludePlatforms ?? Array.Empty<string>(), Is.Empty);
            CollectionAssert.DoesNotContain(References(editor.Json), Tests);
        }

        [Test]
        public void TestAssemblyIsEditorOnlyAndReferencesTheRuntime()
        {
            AsmdefFile tests = Load("Tests/EditMode/TimeToExplore.Tests.EditMode.asmdef");

            Assert.That(tests.Json.name, Is.EqualTo(Tests));
            CollectionAssert.AreEquivalent(new[] { "Editor" }, IncludePlatforms(tests.Json),
                "the EditMode suite is editor-only");
            CollectionAssert.IsSubsetOf(new[] { Core, Simulation, Presentation },
                ProjectReferences(tests.Json),
                "the suite compiles against all three runtime assemblies");
            CollectionAssert.Contains(References(tests.Json), "Unity.InputSystem");
            CollectionAssert.Contains(References(tests.Json), "UnityEngine.UI");
            CollectionAssert.Contains(References(tests.Json), "Unity.InputSystem.TestFramework");
            CollectionAssert.Contains(References(tests.Json), "UnityEngine.TestRunner");
            CollectionAssert.Contains(References(tests.Json), "UnityEditor.TestRunner");

            Assert.That(tests.Json.overrideReferences, Is.True,
                "the test assembly opts out of automatic precompiled references");
            CollectionAssert.Contains(tests.Json.precompiledReferences ?? Array.Empty<string>(),
                "nunit.framework.dll");
            CollectionAssert.Contains(tests.Json.defineConstraints ?? Array.Empty<string>(),
                "UNITY_INCLUDE_TESTS");
        }

        [Test]
        public void NoAssemblyReferencesALaterLayer()
        {
            foreach (AsmdefFile definition in Assemblies())
            {
                int rank = Rank(definition.Json.name);

                foreach (string reference in ProjectReferences(definition.Json))
                {
                    Assert.That(Rank(reference), Is.LessThan(rank),
                        $"{definition.Json.name} references the later layer {reference}");
                }
            }
        }

        [Test]
        public void EveryProjectReferenceNamesAKnownAssembly()
        {
            foreach (AsmdefFile definition in Assemblies())
            {
                foreach (string reference in References(definition.Json))
                {
                    if (!reference.StartsWith("TimeToExplore.", StringComparison.Ordinal)) continue;

                    Assert.That(Layers, Does.Contain(reference),
                        $"{definition.Path} references the unknown project assembly '{reference}'");
                }
            }
        }

        [Test]
        public void EveryLayerHasExactlyOneAssembly()
        {
            string[] expected =
            {
                "Editor/TimeToExplore.Editor.asmdef",
                "Scripts/Core/TimeToExplore.Core.asmdef",
                "Scripts/Simulation/TimeToExplore.Simulation.asmdef",
                "Scripts/TimeToExplore.Presentation.asmdef",
                "Tests/EditMode/TimeToExplore.Tests.EditMode.asmdef"
            };

            Assert.That(AllAsmdefPaths(), Is.EquivalentTo(expected),
                "one assembly definition per layer; a duplicate or stray one changes the compile graph");

            foreach (string folder in PresentationFolders)
            {
                string path = Path.Combine(Application.dataPath, folder.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(Directory.Exists(path), Is.True, path);
                Assert.That(Directory.GetFiles(path, "*.asmdef", SearchOption.AllDirectories), Is.Empty,
                    $"{folder} belongs to {Presentation}; it must not grow its own assembly");
            }
        }

        [Test]
        public void EveryAssemblyDefinitionHasAUniqueGuid()
        {
            var guids = new Dictionary<string, string>();

            foreach (AsmdefFile definition in Assemblies())
            {
                string metaPath = Path.GetFullPath(definition.AbsolutePath + ".meta");
                Assert.That(File.Exists(metaPath), Is.True, metaPath);

                Match match = GuidLine.Match(File.ReadAllText(metaPath));
                Assert.That(match.Success, Is.True, $"{metaPath} has no guid");

                string guid = match.Groups[1].Value;
                Assert.That(guids.TryGetValue(guid, out string owner), Is.False,
                    $"{definition.Path} shares guid {guid} with {owner}");
                guids.Add(guid, definition.Path);
            }
        }

        [Test]
        public void TheCompiledAssembliesMatchTheLayout()
        {
            Assert.That(typeof(SolarSystemModel).Assembly.GetName().Name, Is.EqualTo(Core));
            Assert.That(typeof(FlightSession).Assembly.GetName().Name, Is.EqualTo(Simulation));
            Assert.That(typeof(SolarSystemBootstrap).Assembly.GetName().Name, Is.EqualTo(Presentation));
            Assert.That(typeof(PlayerControls).Assembly.GetName().Name, Is.EqualTo(Presentation),
                "Player is part of the presentation cluster, not a fourth runtime assembly");
            Assert.That(typeof(SolarSystemSceneBuilder).Assembly.GetName().Name, Is.EqualTo(EditorAssembly));
            Assert.That(typeof(AssemblyLayoutTests).Assembly.GetName().Name, Is.EqualTo(Tests));
        }

        private static IEnumerable<AsmdefFile> Assemblies() =>
            AllAsmdefPaths().Select(path => Load(path)).ToArray();

        private static AsmdefFile Load(string relativePath)
        {
            string path = Path.Combine(
                Application.dataPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, path);

            AsmdefJson json = JsonUtility.FromJson<AsmdefJson>(File.ReadAllText(path));
            Assert.That(json, Is.Not.Null, path);
            Assert.That(json.name, Is.Not.Null.And.Not.Empty, path + " has no assembly name");

            return new AsmdefFile { Path = relativePath, AbsolutePath = path, Json = json };
        }

        private static string[] AllAsmdefPaths()
        {
            string root = Application.dataPath;
            return Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories)
                .Select(file => file.Substring(root.Length).TrimStart('/', '\\')
                    .Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] References(AsmdefJson json) =>
            json.references ?? Array.Empty<string>();

        private static string[] IncludePlatforms(AsmdefJson json) =>
            json.includePlatforms ?? Array.Empty<string>();

        private static string[] ProjectReferences(AsmdefJson json) =>
            References(json).Where(reference => Layers.Contains(reference)).ToArray();

        private static int Rank(string assemblyName)
        {
            int rank = Array.IndexOf(Layers, assemblyName);
            Assert.That(rank, Is.GreaterThanOrEqualTo(0),
                $"'{assemblyName}' is not one of the five project assemblies");
            return rank;
        }

        private sealed class AsmdefFile
        {
            public string Path;
            public string AbsolutePath;
            public AsmdefJson Json;
        }

        [Serializable]
        public sealed class AsmdefJson
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public string[] excludePlatforms;
            public bool allowUnsafeCode;
            public bool overrideReferences;
            public string[] precompiledReferences;
            public bool autoReferenced;
            public string[] defineConstraints;
            public bool noEngineReferences;
        }
    }
}
