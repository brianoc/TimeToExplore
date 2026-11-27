# Handoff

**In progress (2026-11-27): remove the camera's body avoidance.** Branch
`work/remove-camera-avoidance`.

Done:

* `ShipCameraRig` places the camera at the orbit offset without testing the
  ray for bodies, so a body between the camera and the ship no longer pulls the
  camera forward.
* The ten-radii arrival framing is kept; it is what stops the chase sitting on
  a planet near arrival.
* The avoidance test and the current-state doc references are removed.

Next:

1. Run `tools/check.sh --tests presentation,smoke` and
   `--tests playmode`.
2. Merge to `main` and clear this handoff.

Traps:

* `ApplyToCamera` no longer takes the ship position; do not reintroduce a
  placement path that needs it.
