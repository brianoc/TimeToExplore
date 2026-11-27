# Handoff

**In progress (2026-11-27): follow-cam polish.** Branch
`work/follow-cam-polish`, cut from `main` after the warp/camera tuning.

Done:

* A wheel in the flight chase keeps the ship centred: the flight view stays
  active, the look-at stays the ship, and only the automatic distance follow is
  handed over. A drag keeps the distance follow as before.
* The destination framing starts at **ten planet radii**, so the fixed chase
  angle cannot point back at Mars and park the camera on the surface. Measured:
  camera–Mars went from 3.56 units at the same point (radius 3.39) to 19.24.
* The status clock drops the warp while the flight panel is open, so there is
  one warp line.

Next:

1. Show the capture pair (`Screenshots/polish/`) and merge to `main` on
   confirmation.

Traps:

* The early handover uses `target.RadiusM > 100_000` so stations and small
  bodies keep the chase; do not lower it without checking the station arrival.
* `1 unit = 1,000 km`; the arrival camera is 19.24 units (19,240 km) from Mars.

Start from [ADR-027](Docs/decisions/adr-027-continuous-warp-and-chase-camera.md).
