#!/usr/bin/env python3
"""Map changed files to the narrowest check.sh test categories.

`tools/check.sh --changed` calls this. It reads the files a branch or working
tree has changed against a base (main by default), and prints the comma-separated
EditMode and PlayMode categories those files need. An empty answer means the
change needs the fast gates only.

The mapping is deliberately conservative per path but not per change: a small
edit under Simulation/ runs the cheap flight category, not the deep sweeps.
Pass --deep to add the sweeps for a change to the planner maths.

Anything unrecognised fails instead of defaulting to the whole suite, so an
agent has to make a decision rather than silently under-test a change.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

CATEGORY_ORDER = ("core", "flight", "deep", "presentation", "smoke", "playmode")

# Longest prefixes first so Scripts rules beat the generic Assets fallback.
PREFIX_RULES = (
    ("TimeToExplore/Assets/Scripts/Core/", {"core", "flight"}),
    ("TimeToExplore/Assets/Scripts/Simulation/", {"flight"}),
    ("TimeToExplore/Assets/Scripts/Presentation/", {"presentation"}),
    ("TimeToExplore/Assets/Scripts/Player/", {"core", "presentation"}),
    ("TimeToExplore/Assets/Scripts/UI/", {"presentation", "smoke"}),
    ("TimeToExplore/Assets/Scripts/Rendering/", {"presentation"}),
    ("TimeToExplore/Assets/Scripts/Editor/", {"core"}),
    ("TimeToExplore/Assets/Shaders/", {"presentation"}),
    ("TimeToExplore/Assets/Resources/", {"core", "presentation"}),
    ("TimeToExplore/Assets/Scenes/", {"smoke"}),
    ("TimeToExplore/Assets/Settings/", {"presentation", "smoke"}),
    ("TimeToExplore/ProjectSettings/", {"core", "smoke"}),
    ("TimeToExplore/Packages/", {"core", "smoke"}),
)

EDITMODE_PREFIX = "TimeToExplore/Assets/Tests/EditMode/"
PLAYMODE_PREFIX = "TimeToExplore/Assets/Tests/PlayMode/"

# Unity rewrites this asset on every batch import, dropping the runtime settings
# list. That is noise, not a source change, and it must not widen the gate.
GENERATED_FILES = frozenset(
    {"TimeToExplore/Assets/UniversalRenderPipelineGlobalSettings.asset"}
)

# No Unity run: the design bundle, the Python tools, editor and git settings.
FAST_PREFIXES = ("Docs/", "tools/", ".github/", ".vscode/")
FAST_SUFFIXES = (".md", ".csproj", ".sln")
FAST_NAMES = (
    "AGENTS.md",
    "HANDOFF.md",
    "README.md",
    ".gitignore",
    ".gitattributes",
    ".editorconfig",
)

CATEGORY_PATTERN = re.compile(r'\[Category\(\s*"([^"]+)"\s*\)\]')


class PlanError(Exception):
    """A changed path has no known category."""


def _read_categories(path: str) -> set[str]:
    """Return the categories declared in a C# test file."""
    try:
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
    except OSError:
        return set()
    return {match for match in CATEGORY_PATTERN.findall(text) if match in CATEGORY_ORDER}


def plan_files(files, root: str = ".", read_categories=_read_categories) -> set[str]:
    """Return the categories the given repo-relative files need."""
    categories: set[str] = set()
    unknown: list[str] = []

    for raw in files:
        path = raw.replace("\\", "/")
        if path in FAST_NAMES or path in GENERATED_FILES or path.endswith(FAST_SUFFIXES):
            continue
        if path.startswith(FAST_PREFIXES):
            continue
        if path.endswith(".asmdef"):
            categories.add("core")
            continue
        if path.startswith(EDITMODE_PREFIX):
            found = read_categories(os.path.join(root, path))
            if not found:
                raise PlanError(
                    f"{path} declares no [Category]; add one or pass --tests explicitly"
                )
            categories |= found
            continue
        if path.startswith(PLAYMODE_PREFIX):
            found = read_categories(os.path.join(root, path))
            # Probes and helpers carry no category; the PlayMode suite is their gate.
            categories |= found or {"playmode"}
            continue
        for prefix, mapped in PREFIX_RULES:
            if path.startswith(prefix):
                categories |= mapped
                break
        else:
            if path.startswith("TimeToExplore/Assets/"):
                # Materials, prefabs, and anything else under Assets: rendering and
                # the scene are the likely consumers.
                categories |= {"presentation", "smoke"}
            else:
                unknown.append(path)

    if unknown:
        listed = "\n  ".join(unknown)
        raise PlanError(
            "no category rule for:\n  "
            + listed
            + "\nPass --tests explicitly so the change is covered."
        )
    return categories


def order(categories) -> list[str]:
    """Return categories in the canonical check.sh order."""
    return [name for name in CATEGORY_ORDER if name in categories]


def changed_files(root: str, base: str) -> list[str]:
    """Return files changed on the branch and in the working tree against *base*."""

    def git(*args: str) -> str:
        result = subprocess.run(
            ["git", "-C", root, *args], capture_output=True, text=True
        )
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip() or f"git {' '.join(args)} failed")
        return result.stdout

    git("rev-parse", "--verify", f"{base}^{{commit}}")
    names: set[str] = set()
    for args in (
        ("diff", "--name-only", "--diff-filter=ACMR", f"{base}...HEAD"),
        ("diff", "--name-only", "--diff-filter=ACMR", "HEAD"),
    ):
        names.update(line for line in git(*args).splitlines() if line)
    names.update(
        line
        for line in git("ls-files", "--others", "--exclude-standard").splitlines()
        if line
    )
    return sorted(names)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        help="repository root (default: the parent of tools/)",
    )
    parser.add_argument(
        "--base",
        default="main",
        help="git revision to compare against (default: main)",
    )
    parser.add_argument(
        "--files",
        nargs="+",
        metavar="PATH",
        help="plan these paths instead of asking git (for tests and dry runs)",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="print only the category list",
    )
    args = parser.parse_args(argv)

    try:
        files = args.files if args.files else changed_files(args.root, args.base)
    except RuntimeError as error:
        print(f"check_plan: {error}", file=sys.stderr)
        return 2

    try:
        categories = plan_files(files, args.root)
    except PlanError as error:
        print(f"check_plan: {error}", file=sys.stderr)
        return 1

    names = order(categories)
    if not args.quiet:
        print(
            f"check_plan: {len(files)} changed file(s) -> "
            f"{', '.join(names) if names else 'fast gates only'}",
            file=sys.stderr,
        )
        for path in files[:20]:
            print(f"  {path}", file=sys.stderr)
        if len(files) > 20:
            print(f"  ... and {len(files) - 20} more", file=sys.stderr)

    print(",".join(names))
    return 0


if __name__ == "__main__":
    sys.exit(main())
