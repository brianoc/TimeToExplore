---
type: Decision Record
title: "ADR-012: An assembly layout that enforces the layering"
description: Split the Unity runtime into Core, Simulation, and Presentation assemblies, put editor tooling behind its own Editor assembly, and let the test assemblies prove the graph.
tags: [decision, architecture, unity, technical, testing]
status: accepted
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-22T21:26:25Z }
---

# Context

All of `Assets/Scripts` compiled into one assembly definition,
`TimeToExplore.Runtime`, with `Unity.InputSystem` as its only package
reference. The namespaces and folders already followed the intended layers —
`Core`, `Simulation`, `Player`, `Rendering`, `UI`, and the root bootstrap — but
nothing stopped a `Core` type from using a `Simulation` type, or a UI script
from reaching into the scene bootstrap. A single assembly also recompiles the
whole runtime whenever any one file changes, and it hides a dependency mistake
until review.

`Assets/Editor/SolarSystemSceneBuilder.cs` had no assembly definition at all, so
it compiled into the predefined `Assembly-CSharp-Editor`. That worked, but it
left the editor tooling outside every reference check and made the project's
compile graph partly implicit.

The design already names the dependency direction: the flight core is plain C#
over `Core`, and Unity is confined to input, rendering, and the HUD. The build
should say the same thing.

# Decision

1. **Six assemblies: one per layer and two test suites.**

   | Assembly | Folder | Project references | Platforms |
   |---|---|---|---|
   | `TimeToExplore.Core` | `Assets/Scripts/Core/` | none | all |
   | `TimeToExplore.Simulation` | `Assets/Scripts/Simulation/` | Core | all |
   | `TimeToExplore.Presentation` | `Assets/Scripts/` (covers `Player/`, `Rendering/`, `UI/`, and the root bootstrap) | Core, Simulation | all |
   | `TimeToExplore.Editor` | `Assets/Editor/` | Core, Simulation, Presentation | Editor only |
   | `TimeToExplore.Tests.EditMode` | `Assets/Tests/EditMode/` | Core, Simulation, Presentation, Editor | Editor only |
   | `TimeToExplore.Tests.PlayMode` | `Assets/Tests/PlayMode/` | Core, Simulation, Presentation | all platforms |

   The rule is **Core ← Simulation ← Presentation ← Editor/Tests**: an assembly
   may only reference an assembly to its left. `Core` has no project references,
   `Simulation` references only `Core`, `Presentation` adds `Unity.InputSystem`
   for the `PlayerControls` seam, and the editor tooling sits at the top. The
   EditMode suite also references `TimeToExplore.Editor` so it can prove the
   editor assembly compiles and resolves its scene builder. The PlayMode suite
   is all-platform because it must enter the player loop, but it still only
   references the three runtime assemblies and the test runner.

2. **The existing runtime assembly is renamed, not replaced.** The asmdef and its
   `.meta` moved from `TimeToExplore.Runtime.asmdef` to
   `TimeToExplore.Presentation.asmdef`, keeping GUID
   `ad494d39a0edb4df596e9e1e4b24e825` and `rootNamespace: TimeToExplore`. The two
   scenes' `m_EditorClassIdentifier` hints were updated to
   `TimeToExplore.Presentation::TimeToExplore.SolarSystemBootstrap`; the script
   GUIDs were untouched, so no serialized reference changed meaning.

3. **`Player`, `Rendering`, and `UI` stay together in Presentation.** They are a
   tight cluster, not three independent layers: `UI` uses both `Player` and
   `Rendering`, `Rendering` uses `Player` types, and the bootstrap uses all four.
   Splitting them would add three asmdefs and a web of references without
   protecting a boundary that matters yet. `Presentation` is the honest name for
   "the Unity-facing half of the prototype".

4. **The layer graph is a test.** `AssemblyLayoutTests` parses every `.asmdef`
   under `Assets/` as the source of truth and asserts the six-assembly set, the
   forward-reference ban, Core's empty reference list, the Input System reference
   in Presentation, the EditMode suite's Editor-only platforms and the PlayMode
   suite's all-platform setting, unique GUIDs, and the compiled
   assembly name of representative types (`SolarSystemModel`, `FlightSession`,
   `SolarSystemBootstrap`, `PlayerControls`, `SolarSystemSceneBuilder`). A stray
   or duplicate asmdef under `Assets/Scripts` fails the same suite.

5. **`TimeToExplore.Simulation` sets `noEngineReferences: true`.** The flight
   core already had no `UnityEngine` usages, so the asmdef now makes a Unity
   call a compile error rather than a review miss. `Core` cannot do the same:
   `SolarSystemModel` uses `JsonUtility` and `Vector3d` converts to and from
   `UnityEngine.Vector3`.

# Consequences

**Positive**

* The dependency direction is enforced at compile time, not by convention. A
  `Core` → `Simulation` reference is an assembly-level error, and the tests
  catch it even before Unity's compiler reports the chain.
* Incremental compiles get smaller: a `Simulation` change no longer recompiles
  the renderer, HUD, and camera; a UI-only change no longer recompiles the
  Kepler solver.
* `Simulation` compiles with no UnityEngine reference at all, so the "plain C#
  flight core" claim is enforced by the compiler.
* The editor scene builder is now a real assembly with explicit references, so
  editor-only code cannot leak into a player build.
* The test assemblies are consumers of the graph, not a special case: if the
  layout drifts, the suite says which assembly broke the rule.

**Negative**

* Six asmdefs and six `.meta` files to keep in sync, and any new folder of
  runtime code has to choose a home. The test's exact-set assertion makes that
  choice explicit.
* Unity does not make assembly references transitive, so each consumer lists
  every project assembly it uses. That is deliberate duplication, but it is
  duplication a reader can follow.
* Renaming the runtime assembly invalidates any external build script or
  document that still says `TimeToExplore.Runtime`. The docs and scenes were
  updated in the same change; external tooling would need the new name.
* The presentation cluster is still one compilation unit, so a change to the HUD
  recompiles the camera rig and renderer. Splitting it later is a local change
  with this test as the guard.

# Alternatives considered

**Keep one runtime assembly.** Lowest churn and already green, but it enforces
nothing and recompiles everything. The layering would remain a review comment.

**Split Presentation into Player, Rendering, and UI.** It looks tidier on a
diagram, but `UI` needs `Player` and `Rendering`, `Rendering` needs `Player`, and
the bootstrap needs all of them. The result is four assemblies and three edges
for a cluster that changes together. Revisit when one of those folders grows an
independent consumer.

**Split Simulation into planning and flight assemblies.** `TripPlanner`,
`Autopilot`, and `FlightSession` share `PlanArc`, `TrajectoryPlan`, the gravity
field, and the integrator. There is no seam to cut yet, and the boundary would be
guesswork.

**Leave editor code in `Assembly-CSharp-Editor`.** It compiles, but it is outside
the reference graph and the tests cannot prove it compiled against the runtime.
The explicit `TimeToExplore.Editor` assembly costs one asmdef and closes that
gap.

**Reference assemblies by GUID instead of name.** Unity supports both. Names are
what the project already uses, are readable in review, and produce a better
error when a layer is deleted. GUID references are more rename-proof but opaque.

# Revisit when

* `UI`, `Player`, or `Rendering` gains a consumer that does not need the other
  two, which would justify splitting Presentation,
* the flight core splits into a planning assembly and an execution assembly with
  a real interface between them,
* a player build needs to exclude part of the runtime (for example, editor-like
  diagnostics) and an assembly boundary is the cheapest way to do it,
* assembly reload time becomes a measured problem and a finer split buys a
  noticeable iteration win.
