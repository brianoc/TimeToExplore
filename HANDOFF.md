# Handoff

**Active plan:** route planning (first plot, style/cruise/burn rebuilds) on the
background planner — branch `work/off-main-thread-planning` at `5edb04a`,
built from `main`.

**Done:**

* `FlightSession`: new `PlotAsync` (worker `WindowFor` + `Plan`), `RequestRebuild`
  (style/cruise/burn changes through the same queue), `TryApplyCompletedPreview`
  and `TakePreviewFailure` publishers. The worker is now `PlannerLoop`
  (retimes + preview builds); the window cache and `_windowEpoch` are
  `_retimeLock`-guarded.
* Bootstrap: `PlotRoute` calls `Flight.PlotAsync`; new `ApplyPreviewIfReady`
  pumps from HUD `Update`.
* HUD: `Update` pumps `ApplyPreviewIfReady` before `ApplyRetimeIfReady`.
* Tests: async plot/rebuild coverage in `FlightSessionTests`; `WaitForPreview` /
  `WaitForRebuild` / `PumpUntil` pumps in `BootstrapSmokeTests` and
  `SolarSystemHudTests`.

**Next:** doc pass (planning-performance, architecture, flight-session,
flight-model, navigation-and-arrival, testing, index, log), HANDOFF clear, then
merge to `main`.

**Verify:** `tools/check.sh` — last result: passed — Unity EditMode suite green,
0 failed (566 tests run); `tools/check.sh --fast`: passed, 6 s (rerun after the
doc edits below).
