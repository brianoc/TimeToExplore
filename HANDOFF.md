# Handoff

**Awaiting review: Unity 7 alpha 22 / CoreCLR / .NET 10 experiment
(2026-11-26).** Branch `work/unity-7-coreclr`. **Do not merge or publish.** The
comparison is in [the Unity 7 experiment](/technical/unity-7-coreclr.md); the
person testing has to approve the editor upgrade before it lands on `main`.

## What is done

- Project opened on Unity `7000.0.0a22` in this worktree. `main` still opens on
  `6000.3.28f1`.
- Upgrade fixes: removed the deleted `com.unity.modules.vr`; accepted Unity 7's
  built-in package substitution and the URP settings migration.
- Latent background-plan gravity race fixed: published plans rebind their finite
  burns to the flight's field (`TrajectoryPlan.RebindGravity`), guarded by
  `APublishedPlanBindsItsFiniteBurnsToTheFlightsField`.
- Plan lines stay at the scene root so Unity 7's Entities-world assert does not
  fire when a line is parented; `TrajectoryLine.OnDestroy` releases them.
- Runtime pins: `RuntimeProfileTests` asserts .NET 10 or later and compiles the
  C# 14 `field` keyword under `UNITY_7000_0_OR_NEWER`.
- Plain .NET 10 harness in `tools/dotnet-bench`, run by `tools/dotnet-bench.sh`;
  comparison table by `tools/compare_runtimes.py`; route drift guarded by
  `tools/test_dotnet_bench.py`.
- Measurements and method in
  [the Unity 7 experiment](/technical/unity-7-coreclr.md).

## Verification

```bash
# Unity 7, the branch gate
UNITY_BIN=/Applications/Unity/Hub/Editor/7000.0.0a22/Unity.app/Contents/MacOS/Unity \
    TTE_TEST_CACHE=$PWD/.cache/test-project-7000 tools/check.sh
```

Last result: every gate passed, both Unity suites with 0 failed and 0 skipped,
in 735 s. The branch was rebased onto `main`'s station departure split before
that run, so the gate includes the station tests.

```bash
tools/dotnet-bench.sh --out build/dotnet-bench.json
tools/compare_runtimes.py --mono <mono.json> --coreclr <coreclr.json> \
    --dotnet build/dotnet-bench.json
```

The Mono reference run is the pre-upgrade tree at `main`; its JSON is
`build/mono-benchmark-run.json` in this worktree (ignored by git).

## Next

Show the table to the person testing. If they approve the upgrade, merge the
branch, then update `AGENTS.md`, `Docs/technical/testing.md`, and the project
shape to name Unity 7. If they want to keep Unity 6, drop this branch; the
comparison doc can stay as a measurement only if the editor is reverted.

## Traps

- The committed baseline names Unity 6000.3.28f1, so `bench.sh` on the new editor
  prints its deltas as not meaningful. Compare the raw milliseconds.
- The .NET harness lands on a different certified Uranus floor; its Uranus plot
  is not a like-for-like time.
- Do not open this branch with Unity 6000: the URP assets and package lock are
  migrated.
