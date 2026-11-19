---
type: Reference
title: Working in a worktree
description: "One agent, one branch, one worktree: setup, verification, merge hotspots, and cleanup."
tags: [technical, process, git, worktrees, agents]
status: draft
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-19T00:00:00Z }
---

# Working in a worktree

`main` belongs to the person testing the game. Agents work in a linked git
worktree on their own branch, so nothing an agent edits, imports, or generates
shows up in the checkout with the open editor.

## One agent, one branch, one worktree

- Create the worktree before starting work. OpenCode's worktree tool places it
  under `~/.local/share/opencode/worktree/<hash>/<name>` and checks out the
  commit it was created from, detached; make a branch immediately.
- Name the branch for the work (`work/<slug>`) rather than the agent, so the
  branch still means something when it merges.
- Never check out `main` in a worktree: the main checkout has it, and git
  refuses. Rebase onto `main` when you need what landed there.
- Do not force-close or interfere with an editor running in another checkout,
  and do not run batch Unity against a project path another process has open.
  A worktree avoids that by construction: every worktree is a different path.

## Setup

The system `python3` has no PyYAML, numpy, or Pillow, so the tooling cannot run
without a virtualenv. Reuse the main checkout's instead of reinstalling:

```bash
ln -s ~/Personal/TimeToExplore/.venv .venv
```

`.gitignore` covers `.venv` (a symlink is not matched by the older `.venv/`
pattern). One shared venv means a `pip install` for one branch changes it for
every worktree; that is the trade for not duplicating 62 MB per agent.

## Verifying

`tools/check.sh` needs no configuration in a worktree: it derives its root from
its own path, so the Unity suite runs in `<worktree>/.cache/test-project` with a
symlinked `Assets` and a private warm `Library`. The first run pays the import
(about 30 s, ~1.7 GB); later runs are warm. `--fast` stays a few seconds.

Worktrees have separate `.run-lock`s, so runs no longer queue behind each
other. They still share one machine: parallel Unity instances contend for CPU
and the license, so two full suites at once are slower than one after the other.

## Merge hotspots

Resolve these by keeping **both** sides, not by choosing one.

- `Docs/log.md` — every branch adds a `YYYY-MM-DD` heading and appends under it.
  Keep one heading per date with the merged entries newest first.
- `Docs/index.md` — link bullets land in the same sections; keep both and check
  the ordering still reads sensibly.
- `HANDOFF.md` — the handoff is **per branch** and describes that branch's plan.
  After a merge the tree must carry the handoff of whichever plan is still in
  progress, or the no-active-handoff stub. Never resurrect a finished plan's
  handoff, and never let one branch's handoff describe another's work.
- Generated artefacts — `solar_system.json` and `milky_way.png` are committed
  outputs. If both branches regenerated, take one side and re-run the generator;
  `check.sh` compares the result byte for byte and pixel for pixel.
- Frontmatter `generated` — every touched doc updates it, so expect trivial
  conflicts there. Take either side and keep it accurate.

## What does not travel

`.venv`, `.cache/`, and `Screenshots/` are ignored, so a new worktree starts
without them: no warm Unity cache, no captures. Recreate what you need; do not
copy another agent's `.cache`.

## Cleaning up

From the main checkout, once the branch is merged:

```bash
git worktree remove <path>
```

Then delete that worktree's `.cache` to reclaim about 1.7 GB. Removing a
worktree with unmerged work needs `--force`, so confirm with the person first.
