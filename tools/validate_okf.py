#!/usr/bin/env python3
"""Validate the Time to Explore design bundle against OKF v0.2.

Checks the hard conformance rules (frontmatter, non-empty type, reserved file
structure) and a few soft rules we care about (links resolve, index coverage).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote

try:
    import yaml
except ImportError:  # pragma: no cover - dependency hint
    sys.exit(
        "PyYAML is required. On a managed Python install, use a venv:\n"
        "  python3 -m venv .venv\n"
        "  .venv/bin/pip install pyyaml\n"
        "  .venv/bin/python tools/validate_okf.py"
    )

ROOT = Path(__file__).resolve().parent.parent / "Docs"
LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
DATE_HEADING_RE = re.compile(r"^## \d{4}-\d{2}-\d{2}$")
FRONTMATTER_RE = re.compile(r"\A---\r?\n(.*?)\r?\n---\r?\n", re.DOTALL)


def split_frontmatter(text: str) -> tuple[dict | None, str]:
    m = FRONTMATTER_RE.match(text)
    if not m:
        return None, text
    return yaml.safe_load(m.group(1)) or {}, text[m.end() :]


def concept_docs() -> list[Path]:
    return [
        p
        for p in sorted(ROOT.rglob("*.md"))
        if p.name not in {"index.md", "log.md"}
    ]


def local_target(link: str) -> str | None:
    link = link.strip()
    if link.startswith("<") and link.endswith(">"):
        link = link[1:-1]
    if link.startswith(("http://", "https://", "mailto:")):
        return None
    if link.startswith("#"):
        return None
    link = link.split("#", 1)[0].split("?", 1)[0]
    return unquote(link) or None


def resolve(source: Path, link: str) -> Path | None:
    if link.startswith("/"):
        return (ROOT / link.lstrip("/")).resolve()
    return (source.parent / link).resolve()


def main() -> int:
    errors: list[str] = []
    warnings: list[str] = []

    docs = concept_docs()

    # 1-2. Concept frontmatter.
    for doc in docs:
        text = doc.read_text(encoding="utf-8")
        fm, body = split_frontmatter(text)
        rel = doc.relative_to(ROOT)
        if fm is None:
            errors.append(f"{rel}: missing YAML frontmatter block")
            continue
        if not isinstance(fm, dict):
            errors.append(f"{rel}: frontmatter is not a mapping")
            continue
        if not str(fm.get("type", "")).strip():
            errors.append(f"{rel}: missing or empty `type`")
        if not body.strip():
            warnings.append(f"{rel}: empty body")

    # 3. Reserved files.
    for name in ("index.md", "log.md"):
        for path in sorted(ROOT.rglob(name)):
            text = path.read_text(encoding="utf-8")
            rel = path.relative_to(ROOT)
            if name == "index.md":
                fm, body = split_frontmatter(text)
                if fm is not None:
                    if path != ROOT / "index.md":
                        errors.append(f"{rel}: frontmatter only permitted in root index.md")
                    elif set(fm) - {"okf_version"}:
                        errors.append(f"{rel}: root index frontmatter may only carry okf_version")
                if not body.lstrip().startswith("# "):
                    errors.append(f"{rel}: index body should start with a heading")
                if not any(l.startswith("* [") for l in body.splitlines()):
                    errors.append(f"{rel}: index has no list entries")
            else:
                if text.lstrip().startswith("---"):
                    errors.append(f"{rel}: log.md must not carry frontmatter")
                if not text.lstrip().startswith("# "):
                    errors.append(f"{rel}: log.md should start with a title heading")
                dates = [l for l in text.splitlines() if l.startswith("## ")]
                for d in dates:
                    if not DATE_HEADING_RE.match(d):
                        errors.append(f"{rel}: date heading not YYYY-MM-DD: {d!r}")
                if dates and dates != sorted(dates, reverse=True):
                    errors.append(f"{rel}: date headings not newest first")

    # 4. Links resolve (broken links are soft in OKF, but we treat as errors
    #    inside our own bundle).
    for doc in sorted(ROOT.rglob("*.md")):
        text = doc.read_text(encoding="utf-8")
        _, body = split_frontmatter(text)
        for raw in LINK_RE.findall(body):
            target = local_target(raw)
            if target is None:
                continue
            resolved = resolve(doc, target)
            if resolved is None or not resolved.exists():
                errors.append(f"{doc.relative_to(ROOT)}: broken link -> {raw}")

    # 5. Root index should enumerate every concept document.
    index_text = (ROOT / "index.md").read_text(encoding="utf-8")
    linked = set()
    for raw in LINK_RE.findall(index_text):
        target = local_target(raw)
        if target is None:
            continue
        try:
            linked.add(resolve(ROOT / "index.md", target).relative_to(ROOT))
        except ValueError:
            pass
    for doc in docs:
        if doc.relative_to(ROOT) not in linked:
            warnings.append(f"index.md does not list {doc.relative_to(ROOT)}")

    for w in warnings:
        print(f"warn: {w}")
    for e in errors:
        print(f"error: {e}")
    print(
        f"\n{len(docs)} concepts, {len(warnings)} warnings, {len(errors)} errors"
    )
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
