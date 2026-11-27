# Handoff

**Investigation complete; branch awaiting review (2026-11-27).**
Branch `work/unity-7-coreclr`, still **unmerged**. The comparison and the
answer are in [the Unity 7 experiment](/technical/unity-7-coreclr.md).

## Question

Unity 7 CoreCLR is 41–45% faster than Mono, but the plain .NET 10 Release
harness is a further 5–6× faster. Find out how much of that is the build
configuration, the exact runtime, or the host, and whether a Debug harness
reproduces the Editor's number.

## Findings

- Unity compiles user assemblies with `/optimize- /debug:portable`; the
  Release harness has `/optimize+` and `DebuggableAttribute(2)` against the
  Editor's `263`.
- A Debug harness matches the Editor within 2–7% on the clean routes
  (Earth → Mars plot 2,040 against 2,007), so the runtime patch (10.0.10-dev
  against 10.0.14) is not the cause.
- `csc.rsp -optimize+` flips the metadata and makes the vector probe 14%
  faster, but the plot only 5%: the Editor's JIT still runs at Debug speed
  (vector 492–573 ms against Release 184 ms).
- Tiered PGO is ~26% of the optimized build; the other 3.7× is optimized code.
- The Editor's trigonometry is ~2× the SDK's, a second, smaller runtime gap.
- A **macOS CoreCLR player build reaches the Release numbers**:
  `optimizerDisabled=false`, `isDebugBuild=false`, Earth → Mars plot 516 ms
  against the Release harness's 536 and the Editor's 2,007. At the same
  certified Uranus floor, the player's reverse plot is 6,346 ms against the
  Editor's 23,681 (3.7×). The Editor penalty is Editor-only.

## State

- [x] Harness `-c Debug|Release`, `--micro`, and metadata in the run JSON.
- [x] `RuntimeMicrobenchmark` probe and `RuntimeProfileTests` profile logging.
- [x] `TTE_BENCH_ROUTE` for a one-route diagnostic.
- [x] Player hook, player build, and `tools/player-bench.sh`; player measured.
- [x] Docs section and log entry.
- [x] Full `tools/check.sh` passed with the `csc.rsp` experiment removed:
      both Unity suites, 0 failed, 0 skipped, in 762 s.
- [x] Committed; still unmerged and unpushed.

## Verification

The branch gate:

```bash
UNITY_BIN=/Applications/Unity/Hub/Editor/7000.0.0a22/Unity.app/Contents/MacOS/Unity \
  TTE_TEST_CACHE=$PWD/.cache/test-project-7000 tools/check.sh
```

Last result: every gate passed, both Unity suites with 0 failed and 0 skipped,
in 762 s.

```bash
tools/dotnet-bench.sh -c Release --out build/dotnet-bench-release.json
tools/dotnet-bench.sh -c Debug   --out build/dotnet-bench-debug.json
tools/dotnet-bench.sh --micro
DOTNET_TieredPGO=0 tools/dotnet-bench.sh -c Release ...
tools/player-bench.sh --out build/player-bench.json
```

The Editor probe:

```bash
/Applications/Unity/Hub/Editor/7000.0.0a22/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "$PWD/.cache/bench-coreclr/TimeToExplore" \
  -executeMethod TimeToExplore.EditorTools.RuntimeMicrobenchmark.Run \
  -logFile "$PWD/build/micro-default.log"
```

## Traps

- The `csc.rsp` experiment has been removed. If it is present, the profile test
  fails by design.
- The route benchmark warms each case and reports a median of three. The
  machine's thermal state moved the CoreCLR number by ~10% between sessions;
  run matrix entries back to back.
- The .NET harness lands on a different certified Uranus floor; its Uranus plot
  is not like-for-like.


