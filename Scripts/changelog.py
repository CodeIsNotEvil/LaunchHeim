#!/usr/bin/env python3
"""Reads an app's CHANGELOG.md, for the release workflows and the download page.

  Scripts/changelog.py notes Applications/LaunchHeim/CHANGELOG.md 0.2.0   # that release's notes (Markdown)
  Scripts/changelog.py check Applications/LaunchHeim/CHANGELOG.md 0.2.0   # fails unless 0.2.0 is listed

The changelog is the one place a release's changes are written down. The release workflows put a
version's section at the top of its GitHub release notes, and site/build.py shows every version on the
download page, so users can see what changed between the version they have and the newest one.

The format is a small, fixed subset of Markdown (see the header of each CHANGELOG.md):

  ## 0.2.0 - 2026-09-30
  ### Added
  - One change per bullet. Continuation lines are indented.

Inline `code`, **bold** and [links](https://...) are allowed in bullets.
"""
import html
import re
import sys
from dataclasses import dataclass, field
from datetime import date
from pathlib import Path

RELEASE = re.compile(r"^## (?P<version>\d+\.\d+\.\d+) - (?P<date>\d{4}-\d{2}-\d{2})\s*$")
GROUP = re.compile(r"^### (?P<name>.+?)\s*$")


@dataclass
class Release:
  version: str
  date: date
  # Group name ("Added", "Changed", "Fixed") -> bullets, in file order.
  groups: dict[str, list[str]] = field(default_factory=dict)


def parse(path: Path) -> list[Release]:
  releases: list[Release] = []
  group: list[str] | None = None
  for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
    if match := RELEASE.match(line):
      releases.append(Release(match["version"], date.fromisoformat(match["date"])))
      group = None
    elif line.startswith("## "):
      raise SystemExit(f"{path}:{number}: expected '## <version> - <yyyy-mm-dd>', got {line!r}")
    elif releases and (match := GROUP.match(line)):
      group = releases[-1].groups.setdefault(match["name"], [])
    elif releases and line.startswith("- "):
      if group is None:
        raise SystemExit(f"{path}:{number}: a bullet needs a '### Added/Changed/Fixed' heading above it")
      group.append(line[2:].strip())
    elif releases and line.startswith("  ") and group:
      group[-1] += " " + line.strip()
    elif releases and line.strip():
      raise SystemExit(f"{path}:{number}: unexpected line in a release section: {line!r}")
  return releases


def find(releases: list[Release], version: str) -> Release | None:
  return next((r for r in releases if r.version == version), None)


def markdown(release: Release) -> str:
  parts = []
  for name, bullets in release.groups.items():
    parts.append(f"### {name}\n" + "\n".join(f"- {b}" for b in bullets))
  return "\n\n".join(parts)


_CODE = re.compile(r"`([^`]+)`")
_BOLD = re.compile(r"\*\*([^*]+)\*\*")
_LINK = re.compile(r"\[([^\]]+)\]\((https?://[^)\s]+)\)")


def inline_html(text: str) -> str:
  """The allowed inline Markdown as HTML. Everything else is escaped."""
  text = html.escape(text, quote=False)
  text = _CODE.sub(r"<code>\1</code>", text)
  text = _BOLD.sub(r"<strong>\1</strong>", text)
  return _LINK.sub(lambda m: f'<a href="{html.escape(m[2])}">{m[1]}</a>', text)


def main() -> None:
  if len(sys.argv) != 4 or sys.argv[1] not in ("notes", "check"):
    raise SystemExit(__doc__)
  command, path, version = sys.argv[1], Path(sys.argv[2]), sys.argv[3]
  release = find(parse(path), version)
  if release is None or not release.groups:
    raise SystemExit(f"{path} has no entries for {version}. Add a '## {version} - <date>' section first.")
  if command == "notes":
    print(markdown(release))


if __name__ == "__main__":
  main()
