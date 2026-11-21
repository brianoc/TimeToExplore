# Handoff

**Active plan:** route planning (first plot, style/cruise/burn rebuilds) on the
background planner — branch `work/off-main-thread-planning` at `5edb04a`,
built from `main`.

**Done:**

* `FlightSession`: new `PlotAsync` (worker `WindowFor` + `Plan`), `RequestRebuild`
  (style/cruise/burn changes through the same queue), `TryApplyCompletedPreview`
  and `TakePreviewFailure` publishers. The worker is now `PlannerLoop`
  (retimes + preview builds); the window cache and `_windowEpoch` are
  `_retimeLock`-guarded. — commit `1874e27`.
* Bootstrap: `PlotRoute` calls `Flight.PlotAsync`; new `ApplyPreviewIfReady`.
* HUD: `Update` pumps `ApplyPreviewIfReady` before `ApplyRetimeIfReady`. — commit `2716aea`.
* Tests: async plot/rebuild coverage in `FlightSessionTests`; `WaitForPreview` /
  `WaitForRebuild` / `PumpUntil` pumps in `BootstrapSmokeTests` and
  `SolarSystemHudTests`.

**Next:** merge to `main`, remove the worktree, clear HANDOFF. Verification:
`tools/check.sh` green on the branch — last result: passed, Unity EditMode
suite 0 failed; `tools/check.sh --fast`: passed, 6 s.

**Verify:** `tools/check.sh` — last result: passed — Unity EditMode suite green,
0 failed (566 tests run); `tools/check.sh --fast`: passed, 6 s (rerun after the
doc edits below).
