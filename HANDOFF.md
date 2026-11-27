# Handoff

**No active handoff (2026-11-27).** `main` carries the continuous flight warp
and the flight chase camera, merged and waiting for human testing. A flight now
follows [the warp curve](Docs/decisions/adr-026-continuous-warp-and-chase-camera.md)
from 1x up to its cruise rate and back, paced by the plan's duration; the
proximity caps are gone. Begin takes a fixed chase pose behind, above, and to
one side of the route. Evidence is `Screenshots/after/flight-chase.png` and
`flight-arrival.png`; `tools/check.sh` passed every gate. `main` is not pushed
until the result is confirmed. The work branch and its worktree are removed.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md).
