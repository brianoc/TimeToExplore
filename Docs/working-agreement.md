---
type: Guide
title: Working agreement
description: The three rules that apply in every session.
tags: [process, agents, documentation, testing, handoff]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-15T00:00:00Z }
---

# Working agreement

1. **Update the docs as changes happen.** Keep the affected concepts and
   `Docs/log.md` current. Do not record a test count in the bundle: it drifts,
   so report the command and its result instead.
2. **Add tests for every new feature or piece of work.** Extend the Unity
   EditMode suite or `tools/validate_catalogue.py`, then run `tools/check.sh`.
3. **Keep the handoff live while a plan is in progress, and clear it when the
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
4. **Show the result before you call it done.** Finish a task with evidence in
   the chat: screenshots of the visible change, or a short video when motion or
   interaction is what changed. A visual result the person asking cannot see is
   not finished. A plan may explicitly waive this when it changes no visible
   behaviour.
5. **Run multi-step work as a caretaker with one sub-agent per step, and commit
   each step.** The caretaker keeps `HANDOFF.md` live and dispatches one
   self-contained sub-agent per step, in order. A step is not done until its
   code, tests, concept updates, `Docs/log.md` entry, and handoff update are in
   **one commit** and the tree is clean. Do not start the next step until the
   previous commit is in. Stop after two failures on the same step, leaving the
   failure and last command in the handoff, and stop for any design decision
   the notes do not settle.

Details: [Testing and verification](/technical/testing.md). Working in a linked
git worktree, and the files that always conflict when two branches merge, are in
[Working in a worktree](/technical/worktrees.md). The full working rules are in
`AGENTS.md`.
