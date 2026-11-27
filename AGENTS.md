# Agent Instructions — Time to Explore

**Every session:** read [the working agreement](Docs/working-agreement.md)
first. The rules, in short: do all work in a linked git worktree and leave
`main` for merges and human testing; update the docs as changes happen; add
tests for every new feature or piece of work; keep `HANDOFF.md` live only while
a plan is in progress — clear it when the plan is done; publish `main` only
once the person confirms the result; and finish with screenshots of the result,
or a short video when motion or interaction is what changed, in the chat.

## Project shape

- `Docs/` is the design knowledge base. It is an **OKF v0.2 bundle**: Markdown
  files with YAML frontmatter, a root `index.md`, and a root `log.md`.
- `TimeToExplore/` is the Unity 7000.0.0a22 project (CoreCLR Editor, user code
  on .NET 10 / C# 14; Universal Render Pipeline 17.7.0, Input System 7.0.0 with
  Active Input Handling **New** only, and uGUI 7.0.0 for the runtime HUD). Code
  is split into `TimeToExplore.Core`,
  `TimeToExplore.Simulation`, `TimeToExplore.Presentation`, and
  `TimeToExplore.Editor` assemblies; the rule is Core ← Simulation ←
  Presentation ← Editor/Tests ([ADR-012](Docs/decisions/adr-012-assembly-layout.md)).
- `tools/generate_solar_system.py` regenerates
  `TimeToExplore/Assets/Resources/solar_system.json`.
- `tools/generate_skybox.py` regenerates the Milky Way skybox texture at
  `TimeToExplore/Assets/Resources/Sky/milky_way.png`.
- `tools/validate_okf.py` checks the design bundle.

## Documentation is part of done

Before finishing any task that changes behaviour, data, scene setup, or design
intent:

1. Update the affected concept under `Docs/`. Do not leave a stale statement in
   a design document because the code moved on.
2. Add a dated entry to `Docs/log.md` under the current `YYYY-MM-DD` heading.
   Use the existing convention (`**Creation**`, `**Update**`, `**Fix**`,
   `**Decision**`).
3. Add or update links from `Docs/index.md` when a concept is added or renamed.
4. Run the bundle validator:

   ```bash
   python3 tools/validate_okf.py
   ```

   If PyYAML is missing, use a virtual environment:

   ```bash
   python3 -m venv .venv
   .venv/bin/pip install pyyaml
   .venv/bin/python tools/validate_okf.py
   ```

5. If the catalogue changed, regenerate it and confirm the body count:

   ```bash
   python3 tools/generate_solar_system.py --offline
   ```

A pure refactor still gets a short log entry when it changes how the prototype
is built, run, or verified. Implementation details that a reader of the design
bundle does not need may stay out of the concept docs, but they belong in the
log.

Do not record a test count in a concept, the roadmap, or a log entry. It drifts
with every commit and is busywork to maintain; report the command and its result
instead.

## Handoff

The repository-root `HANDOFF.md` is a live session handoff, not a historic log
and not a source of design truth. While a multi-step plan is in progress:

- Keep it current: what is done, what is next, the verification command and its
  last result, and the traps that would cost the next session time.
- Update it in the same commit as the work it describes.
- Link to the concept docs and ADRs for the reasoning rather than restating
  design in the handoff.

When the plan is complete, clear it back to a short "no active handoff" stub in
the same commit as the final step. History stays in `Docs/log.md`; reasoning
stays in the concepts and ADRs.

## OKF rules to preserve

- Every non-reserved `.md` file needs frontmatter with a non-empty `type`.
- Only the bundle-root `index.md` may carry frontmatter, and only
  `okf_version`.
- `log.md` has no frontmatter; date headings are ISO `YYYY-MM-DD`, newest first.
- Prefer absolute bundle-relative links (`/world/solar-system.md`) so documents
  survive being moved.
- Keep frontmatter `title`, `description`, `tags`, `status`, and `generated`
  consistent with the surrounding concepts.

## Verification expectations

- Runtime and editor C# must compile against Unity 6.3.
- Read gameplay input through `Assets/Scripts/Player/PlayerControls.cs` and uGUI
  input through `Assets/Scripts/UI/UiInput.cs`. No other runtime script may call
  `UnityEngine.Input` or name `UnityEngine.InputSystem`; `InputSeamTests` scans
  the sources and fails the suite if one does. Do not change Active Input
  Handling away from "Input System Package (New)" (`activeInputHandler: 1`):
  the HUD is uGUI, not IMGUI, and the legacy backend has no consumer.
- A change to orbital maths or catalogue data should be checked numerically
  against known values at the epoch (2026-11-15, JD 2461359.5).
- Check in proportion to the change, and name the gate you ran:
  - Docs, Python tools, or catalogue data: `tools/check.sh --fast` (a few
    seconds). It validates the OKF bundle, the catalogue schema, both
    generators' reproducibility, and the tooling tests.
  - One C# area: `tools/check.sh --changed`, which reads the changed files and
    runs only the matching EditMode category (about two minutes). It stops
    after the fast gates for a docs-only change, and it refuses a path it has
    no rule for rather than running everything.
  - Planner maths or the scene: `tools/check.sh --changed --deep` (about
    thirteen minutes).
  - Before a merge: a bare `tools/check.sh` (about sixteen minutes). A small
    tweak does not need the merge gate.
  - One fixture: add `--filter <ClassName>` to narrow the run further.
- The Unity suites run in `.cache/test-project` with `Assets` symlinked to the
  real folder and their own warm `Library`, so the open editor is untouched and
  the import is paid once. `--tests core|flight|presentation|smoke` still works
  when the mapping is not obvious; `--tests playmode` runs the PlayMode suite;
  `--reset` rebuilds the cache.
- Do not force-close a Unity editor that has the project open. Use a temporary
  copy of the project for other batch-mode work; `check.sh` already isolates its
  own runs.
- Report the exact command and result used to verify a change.
