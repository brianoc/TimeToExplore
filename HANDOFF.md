# Handoff

**In progress (2026-11-27): continuous flight warp and the chase camera.**
Branch `work/follow-cam-warp`. The code, tests, ADR, concepts, log entry and
captures are in, and `tools/check.sh` passed (EditMode 727, PlayMode 11, 0
failed, 0 skipped, 951 s). The branch is committed but not merged or pushed
until the person testing confirms the look.

Done:

* `SimulationClock.Scale` is continuous; the number keys still select a fixed
  scale and take warp off the curve for that flight.
* `FlightWarp` paces a flight to `clamp(20 + 17·ln(days/6.7), 12, 60)` real
  seconds with three-second 1x ramps at both ends. `WarpPolicy` is deleted.
* `FlightFraming` + `ShipCameraRig.FrameFlight` give Begin a fixed chase pose
  (behind, 20° above, 30° aside, 20,000 km, 1.2 s ease); arrival hands the view
  to the destination framing.
* `ShipView` grows the marker from 10 px to 56 px inside 100,000 km so the chase
  shot has a subject.
* Captures: `Screenshots/after/flight-chase.png` and `flight-arrival.png`, from
  `FlightChaseCaptureTests`. The clock read `x34,052` mid-trip and `x264`
  17.8 s before the arrival.
* Tests and docs are updated; see [ADR-026](Docs/decisions/adr-026-continuous-warp-and-chase-camera.md).

Next:

1. Show the two captures in the chat and ask for the look.
2. On confirmation, merge to `main`, clear this handoff, and remove the
   worktree.

Traps:

* `tools/check.sh` runs Unity in `.cache/test-project`; the main checkout's open
  editor is untouched.
* The chase distance is fixed at 20,000 km because the ship marker has a fixed
  apparent size. Judge the framing on the capture, not the number.
* Batch-mode captures do not include the Screen Space Overlay HUD, so the warp
  label is in the log (`chase capture at x…`) rather than the PNG.

Start from [the design bundle](Docs/index.md) and
[the working agreement](Docs/working-agreement.md).
