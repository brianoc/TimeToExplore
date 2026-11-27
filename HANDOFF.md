# Handoff

**No active handoff (2026-11-27).** `main` carries the full-blast route and the
body render-origin focus. [ADR-025](Docs/decisions/adr-025-full-blast-route.md)
makes the time-optimal zero-coast crossing the nav minimum; the trip window
keeps its slow end at three times the **coast** floor, and the short coast fills
the range between. The evidence is `full-blast.png`, and
`tools/planning-benchmark-baseline.json` is recorded for the new floors and
windows. `main` is not pushed yet: it waits for the person testing to confirm
the focused-body look.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md).
