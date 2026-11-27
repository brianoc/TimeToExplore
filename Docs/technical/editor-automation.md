---
type: Reference
title: Driving the Editor from the CLI
description: Act on the Unity Editor a person has open, through the Pipeline package and the unity CLI, without going near the check.sh batch copy.
tags: [technical, process, agents, unity, cli, tools]
status: active
generated: { by: opencode/deepseek-v4.1-flash, at: 2026-11-27T19:20:00Z }
---

# Driving the Editor from the CLI

`tools/check.sh` runs copies of the project in batch mode and gates a merge —
see [Testing and verification](/technical/testing.md). This is the other half:
acting on the **Editor a person already has open**, so an agent can read live
state, recompile, and run one fixture without waiting for a full gate or
stealing the window. It is an inspection and iteration tool, not a replacement
for the gate.

Two questions it answers well: *what is the Editor doing right now* (scene, Play
Mode, compile state, console errors), and *does this one test pass* after an
edit. Neither needs a 1.7 GB test project and a two-minute wait.

## The two pieces

The feature is a package on the project side and a binary on the machine side.

- `com.unity.pipeline` is a normal UPM dependency in
  `TimeToExplore/Packages/manifest.json`. It has no project code to call: the
  package starts an HTTP server with the Editor and registers the built-in
  commands. An Editor that was already open picks it up on its next refresh,
  usually when its window regains focus.
- The CLI is `~/.unity/bin/unity`. It is **not on `PATH`**, so call it by full
  path, or add the directory yourself.
- Editor commands listen on **7800–7849**, one port per instance. A development
  Player build, if one is running, takes **7900–7949** and needs `--runtime
  <process name>` to select it.

Installing:

```bash
unity pipeline install            # add the package to the current project
curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh \
  | UNITY_CLI_CHANNEL=beta bash   # the CLI itself
unity skill install opencode      # the Unity agent skills, into ~/.config/opencode/skills
```

The skill install is worth doing once. It adds `unity-cli` and `unity-pipeline`
plus roughly thirty area skills, and the pipeline one documents the command
surface in more detail than this concept needs to.

## Finding the Editor, then talking to it

`unity status` lists every instance it can see:

```
port	state	project	version	pid
7801	unreachable	.../worktree/6479b2/opening-view/.cache/test-project/TimeToExplore	7000.0.0a22	36322
7800	ready	/Users/brian.oconnor/Personal/TimeToExplore/TimeToExplore	7000.0.0a22	67683
```

Then address one instance:

```bash
unity command --project-path ~/Personal/TimeToExplore/TimeToExplore editor status
unity command --project-path <project> editor status
unity command --project-path <project>      # list the command groups
unity command --project-path <project> test # list one group's commands
```

`--project-path`, `--port`, `--pid` and `--instance` all select an Editor, and
so does running the command from inside the project directory.
`editor status` answers the useful questions directly: `status`, `playMode`,
`compiling`, `domainReloadInProgress`, and a `lastHeartbeat` you can compare
against the clock. `--json` exists on every command when the output is going
somewhere other than a reader.

## The two instances, and the trap

A `check.sh` run is a real Unity Editor with its own Pipeline server, so while
one is going there are **two instances called `TimeToExplore`**, and a bare
`unity command` from the main checkout refuses:

```
Error: Multiple Unity Editors are running with Pipeline servers
```

That is not a fault. It is the normal state whenever a test run is happening,
which on this project is often. So never rely on auto-discovery for the
person's Editor: pass `--project-path` (stable) or read the port from
`unity status` (assigned from the range, and the main checkout has read 7800).
Auto-discovery is fine only for a command whose target does not matter.

Do not tidy the second instance away. Leave the run alone, and do not open the
main project in batch mode — the rules in
[Working in a worktree](/technical/worktrees.md) cover why.

## The edit loop

The loop an agent actually uses, from the Unity skill:

```bash
# 1. Keep the Editor ticking when it is not the front window. Unity throttles
#    updates otherwise, and a compile can sit unfinished with no error.
unity command --project-path <project> editor set_auto_tick --enabled true

# 2. Edit C# on disk as usual.

# 3. Recompile, then poll: the call returns before the domain reload finishes.
unity command --project-path <project> script recompile
unity command --project-path <project> script recompile_status

# 4. Run one fixture, not the suite.
unity command --project-path <project> test run --mode editor --filter <Fixture>.<Test>
```

Connection errors during a recompile are expected, not a failure: the server is
down while the domain reloads. Poll until `recompile_status` reports `completed`
or `up_to_date`. If it reports `failed`, read its `errors` array and fix the
code before running anything.

`test run` also takes `--filter-type` (`TestName`, `Assembly`, `Category`),
`--mode playmode`, and `--async-tests true` with `test status` and `test cancel`
for a long run. A failing fixture sometimes comes back opaque; re-running with a
narrower `--filter` is usually faster than reading the aggregate.

Other groups earn their place: `console` for the Editor's own log, `capture` for
a screenshot of the live window, `scene` and `gameobject` for the open scene,
`script eval` for a one-line answer about live state, and `editor focus` to
bring the window forward.

## Exit codes carry the diagnosis

| Exit | Meaning | What to do |
|------|---------|------------|
| `2` | Bad arguments. Nothing ran. | Fix the command line; the message names the flag. Do not retry unchanged. |
| `6` | The command ran and failed, or no Editor answered. | Read the error. If no instance was found, wait with `unity status --until-ready`, then retry. Otherwise retrying will not help. |

## What it is not

- **Not the gate.** A green fixture in the open Editor does not replace
  `tools/check.sh --changed` or the fuller gates in
  [Testing and verification](/technical/testing.md). The Editor you are driving
  is the person's, with their scene, their uncommitted state, and whatever
  packages they have imported.
- **Not private.** A recompile, a Play Mode entry, a scene change or a reimport
  is visible to whoever is using that window, and a forced quit loses their
  unsaved work. `editor quit` refuses while scenes are dirty for that reason;
  do not pass `--discard` to get past it.
- **Not a substitute for a worktree.** The CLI acts on a project path, so it can
  drive the main checkout without editing it; the work still happens on a
  branch (rule 1 of [the working agreement](/working-agreement.md)).
