# Handoff

**No active handoff (2026-11-27).** `main` carries the reverse-route window probe
([ADR-026](Docs/decisions/adr-026-window-probes-the-coast-profile.md), pushed)
and the continuous flight warp with the chase camera
([ADR-027](Docs/decisions/adr-027-continuous-warp-and-chase-camera.md), merged
and waiting for human testing). A flight follows the warp curve from 1x up to
its cruise rate and back, paced by the plan's duration, and Begin takes a fixed
chase pose behind, above, and to one side of the route. Evidence is
`Screenshots/after/flight-chase.png` and `flight-arrival.png`; `tools/check.sh`
passed every gate. `main` is not pushed past the reverse-route commit until the
chase camera is confirmed.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md).
