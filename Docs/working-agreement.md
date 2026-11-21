---
type: Guide
title: Working agreement
description: The rules that apply in every session, and where work happens.
tags: [process, agents, git, worktrees, documentation, testing, handoff]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-21T00:00:00Z }
---

# Working agreement

1. **Do all work in a linked worktree; `main` is for merging and testing.**
   Start every task — code, docs, or tools — in a git worktree on a
   `work/<slug>` branch before the first edit. The main checkout is what the
   person testing the game has open, so it takes merges of finished work and
   nothing else: no direct commits, no imports, no batch runs, no generated
   artefacts. A branch is finished when its step commit is in,
   `tools/check.sh` is green, and it has merged; then remove its worktree.
   Resolving a merge is the one job done on `main`.
2. **Update the docs as changes happen.** Keep the affected concepts and
   `Docs/log.md` current. Do not record a test count in the bundle: it drifts,
   so report the command and its result instead.
3. **Add tests for every new feature or piece of work.** Extend the Unity
   EditMode suite or `tools/validate_catalogue.py`, then run `tools/check.sh`.
4. **Keep the handoff live while a plan is in progress, and clear it when the
   plan is done.** The repository-root `HANDOFF.md` is a session handoff, not a
   historic log and not a source of design truth:

   * When you start a multi-step plan, replace it with the current state: what
     is done, what is next, the exact commands that verify the work, and the
     traps that would cost the next session time.
   * Update it as each step lands, in the same commit as that step.
   * Point to the concept docs and ADRs for reasoning. Do not restate design in
     the handoff or let it override the bundle.
   * When the plan is complete, clear it back to a short "no active handoff"
     stub in the same commit as the final step. History belongs in
     `Docs/log.md`; reasoning belongs in the concepts and ADRs.
5. **Show the result before you call it done.** Finish a task with evidence in
   the chat: screenshots of the visible change, or a short video when motion or
   interaction is what changed. A visual result the person asking cannot see is
   not finished. A plan may explicitly waive this when it changes no visible
   behaviour.
6. **Push what the person testing has confirmed, not what has merged.** Only
   `main` is published, to `origin`; the `work/*` branches stay local. Merging
   does not push: a merge waits until the person says they are happy with that
   result, and then one `git push` from the main checkout publishes every
   confirmed merge at once. Reviews do not arrive in merge order, so push
   whatever has been confirmed and leave the rest queued — never hold a merge
   back waiting for its review. A mistake that reaches the remote is fixed
   forward on a new branch, not rewritten out of the published history.
7. **Run multi-step work as a caretaker with one sub-agent per step, and commit
   each step.** The caretaker keeps `HANDOFF.md` live and dispatches one
   self-contained sub-agent per step, in order. A step is not done until its
   code, tests, concept updates, `Docs/log.md` entry, and handoff update are in
   **one commit** and the tree is clean. Do not start the next step until the
   previous commit is in. Stop after two failures on the same step, leaving the
   failure and last command in the handoff, and stop for any design decision
   the notes do not settle.

Details: [Working in a worktree](/technical/worktrees.md) for the setup, the
merge hotspots, and cleanup; [Testing and verification](/technical/testing.md)
for the commands. The full working rules are in `AGENTS.md`.
