---
type: Reference
title: Unity 7 CoreCLR upgrade
description: What Unity 7000.0.0a22 changed, what the upgrade broke, and how the route planner runs on Mono, the CoreCLR Editor, a CoreCLR player, and a plain .NET 10 host.
tags: [technical, unity, coreclr, dotnet, performance, benchmark, alpha]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T19:05:00Z }
---

# Unity 7 alpha 22

**The project's editor is Unity 7000.0.0a22 on CoreCLR.** It replaced Unity
6000.3.28f1 on 2026-11-27. The comparison below was measured on the upgrade
branch before the merge; the branch is history, and `main` now carries the
version. The upgrade changes `ProjectVersion.txt`, the package
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
- **Clear of our own compile warnings.** Unity 7 deprecates
  `FindFirstObjectByType<T>` because it sorts by instance ID, and the
  `FindObjectsByType` overload that takes `FindObjectsSortMode`. The HUD's seam,
  the bootstrap and the PlayMode tests now call `FindAnyObjectByType<T>` (with
  the same `FindObjectsInactive` argument), and `SolarSystemHudTests` uses the
  overload without a sort mode; `InputSeamTests` checks for the new call. The new
  serialization analyzer also reports **UAC1001** for a public field whose type
  is not `[Serializable]`; `CameraFocusDirector.Model` and
  `FrameOrderProbe.Body` are runtime references set from code, so they carry
  `[System.NonSerialized]`. The remaining warnings in the console come from
  packages — `RNGCryptoServiceProvider` in `com.unity.pipeline`, a `bool?` field
  in URP's buffer-capture CLI, and URP shader variants the project does not
  use — and are not ours to change.
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

# The same harness as a Debug build, and the numeric probe
tools/dotnet-bench.sh -c Debug --out build/dotnet-bench-debug.json
tools/dotnet-bench.sh --micro

# Every major planet to every other one, as a dated table
tools/dotnet-bench.sh --matrix

# The CoreCLR player build and its in-player route benchmark
tools/player-bench.sh --out build/player-bench.json

# The table
tools/compare_runtimes.py --mono <mono.json> --coreclr <coreclr.json> \
    --dotnet build/dotnet-bench.json
```

`--matrix` is the wide diagnostic rather than the six-route baseline: it plots
every major planet from every other one at the same epoch and writes a dated
Markdown table beside its JSON. [Planning performance](/technical/planning-performance.md#the-planet-to-planet-plot-matrix-2026-11-27)
carries the first run.

The committed baseline was recorded on Unity 6000.3.28f1. This session's Mono
run lands about 3.5% above it across every phase, which is the machine, not the
code; the comparison below uses that run, not the committed numbers, so all
three columns share one session.

## Plot time

Gateway departures and parking-orbit reverses, at JD 2461359.5, conic capture.
Lower is better.

| Route | Mono | Unity 7 CoreCLR | .NET 10 | Unity 7 vs Mono | .NET vs Mono |
|---|---:|---:|---:|---:|---:|
| Earth → Mars | 3,595 ms | 2,007 ms | 540 ms | −44% | −85% |
| Earth → Venus | 2,889 ms | 1,645 ms | 432 ms | −43% | −85% |
| Earth → Uranus † | 4,094 ms | 2,369 ms | 610 ms | −42% | −85% |
| Mars → Earth (refused) | 827 ms | 482 ms | 137 ms | −42% | −83% |
| Venus → Earth | 10,572 ms | 6,154 ms | 1,680 ms | −42% | −84% |
| Uranus → Earth † | 40,118 ms | 23,681 ms | 616 ms | −41% | −98% |

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
| Plot | 3,595 ms | 2,007 ms | 540 ms | −44% | −85% |
| Retime, window middle | 588 ms | 330 ms | 93 ms | −44% | −84% |
| Retime, window maximum | 501 ms | 282 ms | 79 ms | −44% | −84% |
| Drag median (8 steps) | 461 ms | 256 ms | 72 ms | −45% | −84% |
| Drag worst | 1,233 ms | 704 ms | 201 ms | −43% | −84% |
| Begin (3 h later) | 2,716 ms | 1,544 ms | 391 ms | −43% | −86% |

Earth → Venus tells the same story at every phase (−42% to −43% for Unity 7).
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

- **The CoreCLR Editor is about 41–45% faster than Mono on every measured
  phase.** The win is uniform enough to be the runtime, not a cache or a
  scheduling accident: plot, both retimes, the eight-step drag, and Begin all
  move by the same fraction on every route. Two independent CoreCLR runs agreed
  within 1%; the earlier, warmer run was about 38%, so treat the exact fraction
  as the machine's state rather than a constant.
- **The plain .NET 10 Release harness is a further 5–6× faster than the
  CoreCLR Editor on the clean routes.** The Debug harness reproduces the
  Editor's numbers within 2–7%, so this is a build and JIT profile difference,
  not a different runtime or a different planner. The Editor cannot close it
  with a compiler switch; the measurements are in
  [why the Release harness is faster](#why-the-release-harness-is-faster-2026-11-27).
- **A shipping CoreCLR player gets the Release harness's numbers.** The player
  runs at 516 ms for the Earth → Mars plot against the Release harness's 536
  and the Editor's 2,007. The Editor's penalty does not follow the game into a
  build.
- **The hope that Unity 7 approaches the harness's speed is half right.**
  Unity 7 closes about a third of the Mono → optimized-.NET gap. It does not
  reach the harness's speed, and the remaining gap is the Editor's debug JIT
  profile, not the CoreCLR runtime itself.
- **Nothing about the answer changed.** A faster runtime moves the time it takes
  to certify a plan, not the plan. The near-floor basin remains the one place
  where a runtime can select a different certified result.

## Why the Release harness is faster (2026-11-27)

The 5–6× is not a property of the planner or of clean C#. It is a build and JIT
profile difference, and a Debug build of the same harness reproduces the Editor
almost exactly.

### The Editor compiles for debugging

Unity's Bee response file for every user assembly ends with:

```text
/optimize-
/debug:portable
```

The metadata agrees. `TimeToExplore.Core.dll` carries
`DebuggableAttribute(263)`, which sets `DisableOptimizations`; the Release
harness carries `2`, which does not. The same sources, same epoch, same
machine:

| Earth → Mars | Unity 7 | .NET Debug | .NET Release |
|---|---:|---:|---:|
| Plot | 2,007 ms | 2,040 ms | 536 ms |
| Retime, window middle | 330 ms | 319 ms | 92 ms |
| Begin | 1,544 ms | 1,464 ms | 385 ms |

The Debug harness is within 2–7% of the Editor on the clean routes (Earth →
Venus 1,599 against 1,645; Earth → Uranus 2,222 against 2,369; Venus → Earth
5,756 against 6,154). Unity runs **10.0.10-dev** and the SDK harness **10.0.14**,
so that difference is not the cause: the same-SDK Debug build already matches.

### The compiler switch does not recover it

Adding `csc.rsp` with `-optimize+` flips the metadata to
`DebuggableAttribute(2)` and made the Editor's vector probe 14% faster, but the
route plot moved only 5% (2,007 → 1,875 ms). The embedded CoreCLR still runs
the code at Debug speed: its vector loop matches the Debug harness, not the
Release one, and its trigonometry is about twice the SDK's. The Editor's JIT,
not the C# compiler, is the limiting profile. Do not ship the switch: it is
unsupported, it costs managed debugging, and it buys 5%.

### What the Release build adds

| Earth → Mars plot | Time |
|---|---:|
| .NET Release, tiered PGO on | 536 ms |
| .NET Release, PGO off | 725 ms |
| .NET Release, tiering off | 744 ms |
| .NET Debug | 2,040 ms |
| Unity 7 CoreCLR | 2,007 ms |

Tiered PGO is worth about a quarter on the optimized build. The other **3.7×**
is simply optimized code, and it is the part the Editor does not provide.

### The numeric probe

`RuntimeMicrobenchmark` (`Tools/Time to Explore/Run Runtime Microbenchmark`, or
`-executeMethod TimeToExplore.EditorTools.RuntimeMicrobenchmark.Run`) runs the
same loops as `tools/dotnet-bench.sh --micro`: ten million iterations of a
trigonometric loop and a `Vector3d` loop, after a warm-up. One run each:

| Loop | Unity 7 | Unity 7, `-optimize+` | .NET Debug | .NET Release |
|---|---:|---:|---:|---:|
| Trigonometry | 165 ms | 179 ms | 88 ms | 79 ms |
| `Vector3d` | 573 ms | 492 ms | 504 ms | 184 ms |

The vector loop is the JIT signal: the Editor tracks Debug. The trigonometry
row shows a second, smaller difference in the embedded runtime's math, which
survives `-optimize+`.

## A player build reaches the Release numbers (2026-11-27)

Yes. A macOS player with the CoreCLR backend runs the same route code at the
Release harness's speed. The player is not a debug build:
`optimizerDisabled=false`, `isDebugBuild=false`, runtime 10.0.10.

| Route | Unity 7 Editor | CoreCLR player | .NET 10 Release |
|---|---:|---:|---:|
| Earth → Mars plot | 2,007 ms | 516 ms | 536 ms |
| Earth → Mars retime, middle | 330 ms | 94 ms | 92 ms |
| Earth → Mars Begin | 1,544 ms | 395 ms | 385 ms |
| Earth → Venus plot | 1,645 ms | 423 ms | 425 ms |
| Earth → Uranus plot † | 2,369 ms | 613 ms | 592 ms |
| Venus → Earth plot | 6,154 ms | 1,664 ms | 1,640 ms |
| Uranus → Earth plot † | 23,681 ms | 6,346 ms | 616 ms |

† The player and the Editor find the same certified Uranus floors
(37.00540 d and 64.15472 d). The .NET harness finds an easier certified floor
(32.14971 d and 63.06636 d), so its Uranus times are not like-for-like. The
player's Uranus → Earth plot is **3.7× faster than the Editor at the same
floor**.

The player is within **2–5%** of the .NET Release harness on the routes where
the floors match. The remaining difference is the host and the 10.0.10-dev
runtime, not the code. So the Editor's 3.7× penalty is **Editor-only**: a build
does not carry it.

```bash
tools/player-bench.sh --out build/player-bench.json
```

The build is `PlayerBenchmarkBuild.Build`, with the CoreCLR backend and .NET API
level (the editor's standalone defaults). The runtime hook in
`PlayerRouteBenchmark` only runs with `-tteBench`, so the shipping player is
unaffected.

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
| Numeric probe | `TimeToExplore/Assets/Editor/RuntimeMicrobenchmark.cs` |
| Player benchmark | `TimeToExplore/Assets/Scripts/Player/PlayerRouteBenchmark.cs` |
| Player build | `TimeToExplore/Assets/Editor/PlayerBenchmarkBuild.cs`, `tools/player-bench.sh` |
| Comparison table | `tools/compare_runtimes.py` |
| Drift guard | `tools/test_dotnet_bench.py` |
