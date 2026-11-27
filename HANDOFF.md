# Handoff

**No active handoff (2026-11-27).** `F` now frames the selected body by moving
the **render origin** to it, so the planet is seen at its true size instead of
as a marker at interplanetary distance; `J` remains the debug jump that moves
the ship. The camera rules are in
[Selection and camera focus](Docs/technical/selection-and-camera.md), the
reasoning about float precision and the focused frame is in
[Rendering at solar scale](Docs/technical/rendering-scale.md), and the render
origin change is in [Architecture](Docs/technical/architecture.md). `main` is
merged but not pushed until the person testing confirms the look.

Start from [the design bundle](Docs/index.md),
[the roadmap](Docs/production/roadmap.md), and
[the working agreement](Docs/working-agreement.md). When a multi-step plan
starts, replace this file with a live handoff; when that plan finishes, clear it
back to this stub.
