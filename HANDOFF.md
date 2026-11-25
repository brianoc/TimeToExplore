# Handoff

No active handoff. The route-planning cost plan — measure a build, benchmark the
routes, warm-start the brake/departure pin, and prefetch the slider's neighbours
— is closed on `work/route-warm-start`, ready for human testing. History is in
the [update log](Docs/log.md); the reasoning and the numbers are in
[Planning performance](Docs/technical/planning-performance.md) and
[The flight session](Docs/technical/flight-session.md). `tools/check.sh` is the
gate, and `tools/bench.sh` is the timing instrument.

**Next work item, parked out of this plan:** the three reverse routes do not plot
at the epoch. Mars→Earth, Venus→Earth and Uranus→Earth stop short of their floor
in the coast floor search; Mars→Earth builds at a fixed 90 d, so the search's
twelve-step 20% growth cap is the first thing to question, while the other two
refuse for their own reasons and need separate diagnosis. Evidence and the
refusal messages are in `tools/bench.sh` and
[Planning performance](Docs/technical/planning-performance.md#the-route-benchmark-2026-11-24).
Do not fold it into the warm start: seeding the fast probe search moves the
floors.
