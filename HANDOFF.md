# Handoff

**No active handoff (2026-11-18).** The transfer-conic arrival is complete and
committed: universal-variable propagation, the `IArcPath`/`ConicArc` coast, the
impulsive Oberth capture, the lower conic parking orbit, coast and burn
rendering, and the phase/burn HUD rows. `tools/check.sh`: 401 tests, 0 failed,
0 skipped.

**Default versus opt-in.** Go To still flies the **quintic** capture. The conic
arrival is implemented and verified but has no input surface yet: set
`TripPlanner.ArrivalStyle = CaptureStyle.Conic` or
`FlightSession.ArrivalStyle = CaptureStyle.Conic` to build it. The next UI step
is an arrival-style choice beside the trip-time slider, so the player can see
the conic coast, the periapsis burn, and the lower parking orbit in the scene.

**Not implemented: the mid-cruise coast.** This work covers the *arrival* —
inbound hyperbola, impulsive periapsis capture, transfer ellipse, and
circularisation. A heliocentric Lambert coast through the middle of the trip is
still deferred; the cruise is a powered quintic. Finite-burn arcs and
ring/atmosphere arrival models are later fidelity work.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md),
[ADR-014](Docs/decisions/adr-014-keplerian-coast.md), and
[the working agreement](Docs/working-agreement.md). When a multi-step plan
starts, replace this file with a live handoff; when that plan finishes, clear it
back to this stub.
