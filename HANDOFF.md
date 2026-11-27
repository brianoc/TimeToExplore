# Handoff

**In progress (2026-11-27): warp and chase camera tuning.** Branch
`work/warp-camera-tweaks`, cut from `main` after the first merge.

Done:

* The warp holds near 1x for 40% of the target at each end and climbs along a
  quartic, so the ends are much slower and the middle cruises higher (Mars
  about 80,000x); the total stays about 20 s. `FlightWarp.EndFraction = 0.4`,
  `EndExponent = 4`.
* The chase distance now grows with the simulated distance one frame covers,
  capped at 8% of the view, so the route slides instead of jumping at high
  warp. `FlightFraming.FollowDistanceUnits`, `ShipCameraRig.FollowFlight`.
* The ship marker stays 56 px while the flight owns the view.

Next:

1. The targeted gate passed and the capture pair was regenerated; show them and
   merge to `main` for the look.
2. On confirmation, clear this handoff and push.

Traps:

* `main` is still 4 commits ahead of `origin` (the previous warp/camera merge
  was not pushed). This branch is on top of it; a merge adds to that queue.
* The 40% quartic ramp means a second of real time is about 20x; if the ends are
  still too fast, raise `EndFraction` before touching the target durations.
* The distance rule uses a nominal 60 fps for the per-frame travel, so it does
  not change with the display refresh.
* `1 unit = 1,000 km`. The first pass's `0.02` chase was 20 km, not 20,000 km;
  the base is now 20 units. Do not read a `0.1` threshold as 100,000 km.

Start from [ADR-027](Docs/decisions/adr-027-continuous-warp-and-chase-camera.md)
and [the working agreement](Docs/working-agreement.md).
