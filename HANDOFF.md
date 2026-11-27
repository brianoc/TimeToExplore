# Handoff

**No active handoff (2026-11-27).** `main` carries the reverse-route window probe
([ADR-026](Docs/decisions/adr-026-window-probes-the-coast-profile.md), pushed)
and the continuous flight warp with the chase camera
([ADR-027](Docs/decisions/adr-027-continuous-warp-and-chase-camera.md), local and
waiting for human testing). The warp holds near 1x for 40% of the trip at each
end and climbs along a quartic, so the local departure and capture read while
the middle crosses faster; the chase starts at 20,000 km and pulls back with the
frame's travel so the route slides instead of jumping at high warp. `main` is
not pushed past the reverse-route commit until the flight feel is confirmed.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md).
