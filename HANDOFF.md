# Handoff

**No active handoff (2026-11-17).** The route preview now carries a **trip-time
slider**: the fastest feasible plan (Gateway→Mars is 4.44 d at 9.76 m/s² and
2,345 km/s) out to three times that (8.87 d at 2.71 m/s² and 1,177 km/s). Each
value re-solves the route and slides the arrival date and target position with
it; Begin flies the chosen time.

Coasting in the middle is deliberately **not** offered: it depends on proper
transfer conics, because a coast is a Keplerian arc with zero commanded thrust.
The dependency is recorded in the roadmap, navigation, and flight-model docs.

Last verified: `tools/check.sh` — 365 tests, 0 failed, 0 skipped. Screenshots:
`Screenshots/12-trip-time-fast.png` and `13-trip-time-slow.png`.

Start from [the design bundle](Docs/index.md),
[the glossary](Docs/glossary.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md). When a multi-step plan
starts, replace this file with a live handoff; when that plan finishes, clear it
back to this stub.
