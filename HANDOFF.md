# Handoff

**No active handoff (2026-11-26).** The planet appearance work is complete: the
eight planets and the Moon sample an albedo map at arrival, and the atmospheric
planets carry a rim-glow shell at the real atmosphere height. The design is
[ADR-023](Docs/decisions/adr-023-planet-appearance.md) and
[Planet appearance](Docs/technical/planet-appearance.md); the close-out, with the
full gate and the five body captures, is in [the log](Docs/log.md). The work is
merged into `main`, and `main` stays unpushed until the person testing confirms
the look.

The follow-ups the plan leaves open: ray-marched single scattering as atmosphere
tier 2, albedo maps for the other moons, rings for Saturn and Uranus, and the
Sun's own bloom halo.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md). When a multi-step plan
starts, replace this file with a live handoff; when that plan finishes, clear it
back to this stub.
