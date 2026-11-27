# Handoff

**No active handoff (2026-11-27).** `main` carries the reverse-route window probe
([ADR-026](Docs/decisions/adr-026-window-probes-the-coast-profile.md), pushed),
the continuous flight warp with the chase camera
([ADR-027](Docs/decisions/adr-027-continuous-warp-and-chase-camera.md)), and the
follow-cam polish: a wheel keeps the ship centred, the destination framing starts
ten planet radii out, and the HUD shows one warp line. The warp and camera work
is local, waiting for human testing; `main` is not pushed past the reverse-route
commit until the flight feel is confirmed.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md).
