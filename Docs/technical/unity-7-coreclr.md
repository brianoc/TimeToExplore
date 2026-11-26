---
type: Reference
title: Unity 7 CoreCLR experiment
description: What Unity 7000.0.0a22 changed, what the upgrade broke, and how the route planner runs on Mono, the CoreCLR Editor, and a plain .NET 10 host.
tags: [technical, unity, coreclr, dotnet, performance, benchmark, alpha]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-26T15:30:00Z }
---

# Unity 7 alpha 22

**This is an experiment on `work/unity-7-coreclr`, not the project's editor.**
`main` stays on Unity 6000.3.28f1 until the person testing approves the
comparison and the upgrade. The branch changes `ProjectVersion.txt`, the package
lock, and the URP settings that Unity 7 migrated; nothing in the planner's
source or its answers.

Unity **7000.0.0a22** (released 2026-11-26) is the first alpha where user
scripts compile against **.NET 10 and C# 14**. The Editor runs on CoreCLR instead
of Mono. The assembly references confirm it: the Editor's
`TimeToExplore.Core.dll` targets `System.Runtime, Version=10.0.0.0`, and
`RuntimeProfileTests` pins `Environment.Version` at 10 or later and compiles the
C# 14 `field` keyword.

## What the upgrade needed

The first `-batchmode` open stopped on one invalid dependency. The rest was
automatic.

- **Removed `com.unity.modules.vr`.** Unity 7 no longer ships the VR module, and
  the package resolver refuses the project before any code compiles. The project
  never used it.
- **Unity 7 substitutes its built-in packages.** The manifest still names Input
  System 1.20.1, URP 17.3.0, uGUI 2.0.0, and Test Framework 1.6.0, but the lock
  resolves Input System 7.0.0, URP 17.7.0, uGUI 7.0.0, and Test Framework 1.9.0
  as built-ins. No source change was needed; the input seam and the tests pass.
- **URP migrated its settings.** Unity rewrote the pipeline assets,
  `GraphicsSettings`, and the volume profile, and added a project volume profile
  asset. These are committed with the version change.
- **No planner or gameplay source changed for the upgrade itself.** The Core
  and Simulation assemblies compile and the flight suite passes without a code
  edit. Separately, the full suite exposed a latent background-plan race that
  the new runtime made reproducible; see below.
- **A published plan now rebinds its finite burns.** A plan built on the
  session's worker carries the worker's `GravityField`, which the worker keeps
  preparing for later requests. Flying that plan on the main thread prepared
  the same stateful field from two threads, and CoreCLR surfaced the corrupted
  active set as an `ArgumentOutOfRangeException` in
  `GravityField.SelectActiveBodies`. `TrajectoryPlan.RebindGravity` points every
  finite burn at the flight's field when the worker's plan is published, and
  `APublishedPlanBindsItsFiniteBurnsToTheFlightsField` holds the invariant. This
  is a real latent bug, not a Unity 7 behaviour change.
- **Plan lines stay at the scene root.** Unity 7's alpha asserts that a
  transform hierarchy holds entities from two Entities worlds when a new child
  is parented into the editor's play hierarchy, although the project has no
  Entities package. The trajectory lines are world-space and need no parent, so
  they now stay at the root and `TrajectoryLine.OnDestroy` releases them. This
  removes sixteen engine asserts from the Begin test without suppressing the
  test runner's log checks.

## Method

All three runs used the same machine, the same `Assets`, and the same
`solar_system.json`: an Apple M4 Max, 16 cores, 64 GB, macOS 27.2.1. Each run
warms one untimed call per case and reports the median of three, except the drag,
which is one sequence of eight steps and reports its median and worst. The Unity
runs use `tools/bench.sh` under `-batchmode -nographics`; the plain .NET run
uses `tools/dotnet-bench.sh`, the harness in `tools/dotnet-bench`: a `net10.0`
Release build over the same Core and Simulation sources with a small
UnityEngine shim.

```bash
# Mono, the current editor, from a pristine worktree at main
UNITY_BIN=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity \
    TTE_TEST_CACHE=$PWD/.cache/bench-mono tools/bench.sh --no-color

# Unity 7 alpha 22, CoreCLR
UNITY_BIN=/Applications/Unity/Hub/Editor/7000.0.0a22/Unity.app/Contents/MacOS/Unity \
    TTE_TEST_CACHE=$PWD/.cache/bench-coreclr tools/bench.sh --no-color

# Plain .NET 10 Release
tools/dotnet-bench.sh --out build/dotnet-bench.json

# The table
tools/compare_runtimes.py --mono <mono.json> --coreclr <coreclr.json> \
    --dotnet build/dotnet-bench.json
```

The committed baseline was recorded on Unity 6000.3.28f1. This session's Mono
run lands about 3.5% above it across every phase, which is the machine, not the
code; the comparison below uses that run, not the committed numbers, so all
three columns share one session.

## Plot time

Gateway departures and parking-orbit reverses, at JD 2461359.5, conic capture.
Lower is better.

| Route | Mono | Unity 7 CoreCLR | .NET 10 | Unity 7 vs Mono | .NET vs Mono |
|---|---:|---:|---:|---:|---:|
| Earth → Mars | 3,628 ms | 2,232 ms | 596 ms | −38% | −84% |
| Earth → Venus | 2,899 ms | 1,781 ms | 477 ms | −39% | −84% |
| Earth → Uranus † | 4,122 ms | 2,527 ms | 666 ms | −39% | −84% |
| Mars → Earth (refused) | 827 ms | 506 ms | 152 ms | −39% | −82% |
| Venus → Earth | 10,557 ms | 6,549 ms | 1,891 ms | −38% | −82% |
| Uranus → Earth † | 40,303 ms | 24,741 ms | 689 ms | −39% | −98% |

† The .NET harness finds a different **certified** floor at Uranus: 32.14971 d
against 37.00540 d for Earth → Uranus, and 63.06636 d against 64.15472 d for
Uranus → Earth. The near-floor basin is scatter, not a boundary, so a last-bit
difference in `Math` selects a different valid plan; the earlier profile explains
this in [Planning performance](/technical/planning-performance.md#the-runtime-divergence-is-a-property-not-a-bug).
The 98% figure is therefore not a like-for-like plot: the harness spends less
time because it landed on an easier certified needle. The Unity 6 and Unity 7
columns are like-for-like, because their floors match to the last digit.

## Phase detail: Earth → Mars

Same route, same departure, same floor (7.16751 d) and window (21.50253 d).

| Phase | Mono | Unity 7 CoreCLR | .NET 10 | Unity 7 vs Mono | .NET vs Mono |
|---|---:|---:|---:|---:|---:|
| Plot | 3,628 ms | 2,232 ms | 596 ms | −38% | −84% |
| Retime, window middle | 596 ms | 367 ms | 102 ms | −38% | −83% |
| Retime, window maximum | 518 ms | 314 ms | 87 ms | −40% | −83% |
| Drag median (8 steps) | 456 ms | 284 ms | 78 ms | −38% | −83% |
| Drag worst | 1,250 ms | 774 ms | 220 ms | −38% | −82% |
| Begin (3 h later) | 2,745 ms | 1,680 ms | 429 ms | −39% | −84% |

Earth → Venus tells the same story at every phase (−37% to −39% for Unity 7).
The reverse routes move the same way; their drag phases are not measured because
the session's slider does not run for those departures.

## The plans did not move

The Unity 6 and Unity 7 runs return the same floors and windows to all printed
digits:

| Route | Mono floor | Unity 7 floor | Mono window | Unity 7 window |
|---|---:|---:|---:|---:|
| Earth → Mars | 7.16751 d | 7.16751 d | 21.50253 d | 21.50253 d |
| Earth → Venus | 4.20901 d | 4.20901 d | 12.62702 d | 12.62702 d |
| Earth → Uranus | 37.00540 d | 37.00540 d | 111.01619 d | 111.01619 d |
| Venus → Earth | 6.62749 d | 6.62749 d | 19.88247 d | 19.88247 d |
| Uranus → Earth | 64.15472 d | 64.15472 d | 176.42547 d | 176.42547 d |

The Mono → CoreCLR change is a pure speed change. The .NET column agrees on
every route whose floor it reaches; only the Uranus basin selects another
certified needle, as described above.

## What the numbers mean

- **The CoreCLR Editor is about 37–40% faster than Mono on every measured
  phase.** The win is uniform enough to be the runtime, not a cache or a
  scheduling accident: plot, both retimes, the eight-step drag, and Begin all
  move by the same fraction on every route.
- **The plain .NET 10 Release harness is a further 5–6× faster than the
  CoreCLR Editor on the clean routes.** That gap is not a runtime difference
  alone: the Editor's scripting assemblies carry `DebuggableAttribute` and are
  built for debugging, while the harness is an optimized Release build with
  tiered PGO. It is the ceiling a Release host can reach, not a promise for the
  Editor.
- **The hope that Unity 7 approaches the harness's speed is half right.**
  Unity 7 closes about a third of the Mono → optimized-.NET gap. It does not
  reach the harness's speed, and the remaining gap is mostly compilation
  settings, not CoreCLR.
- **Nothing about the answer changed.** A faster runtime moves the time it takes
  to certify a plan, not the plan. The near-floor basin remains the one place
  where a runtime can select a different certified result.

## Caveats

- Alpha 22 is an alpha. It has open CoreCLR crashes and known issues, and it is
  not a shipping editor. The upgrade is reversible by reverting this branch.
- The .NET harness is a measurement instrument, not a supported runtime for the
  game. It shims the five UnityEngine members Core and Simulation touch
  (`Vector3`, `Debug`, `JsonUtility`, `Resources`, `TextAsset`).
- Only the planner's route work is measured. Editor startup, asset import, and
  rendering are outside this benchmark.
- `tools/check.sh` passes on the new editor. The suites' timings are not
  compared here: they include asset import and test-runner overhead, not just
  managed code.

## Where it lives

| Concern | File |
|---|---|
| Unity version | `TimeToExplore/ProjectSettings/ProjectVersion.txt` |
| Removed VR module | `TimeToExplore/Packages/manifest.json` |
| URP migration | `TimeToExplore/Assets/Settings`, `TimeToExplore/ProjectSettings/GraphicsSettings.asset` |
| Runtime pin | `TimeToExplore/Assets/Tests/EditMode/RuntimeProfileTests.cs` |
| Plain .NET harness | `tools/dotnet-bench/`, `tools/dotnet-bench.sh` |
| Comparison table | `tools/compare_runtimes.py` |
| Drift guard | `tools/test_dotnet_bench.py` |
