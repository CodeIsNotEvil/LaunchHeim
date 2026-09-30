#!/usr/bin/env python3
"""Builds the GitHub Pages site into a folder (default: site/_site).

  site/build.py                  # uses GH_TOKEN or GITHUB_TOKEN when set, anonymous API otherwise
  site/build.py _site --require-releases-api

What it does:
- Copies the pages from site/src and fills in {{placeholders}}.
- Copies the logos and the font from where they already live (Assets/, the apps), so the site never
  holds its own drifting copies.
- Renders each app's CHANGELOG.md (through Scripts/changelog.py) into a version history, so users see
  what changed between the version they run and the newest one.
- Asks the GitHub API for each app's newest release (tags grocerytracker-v* and launchheim-v*) and
  links its files directly. The repository holds several apps, so GitHub's single "latest release"
  link can't be used. Before an app has a release, <!-- if:key --> ... <!-- else --> ... <!-- end -->
  blocks show build instructions instead.

The workflow .github/workflows/site.yml runs this on every change to the site or a changelog and
whenever a release is published, so the download links follow new releases without editing the pages.

Placeholders: {{ key }} is escaped text, {{{ key }}} is HTML that build.py generated itself.
"""
import argparse
import html
import json
import os
import re
import shutil
import sys
import urllib.request
from datetime import datetime
from pathlib import Path

REPO = "CodeIsNotEvil/MyMonoRepo"
ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "site" / "src"

sys.path.insert(0, str(ROOT / "Scripts"))
import changelog  # noqa: E402 - lives in Scripts/, shared with the release workflows

CHANGELOGS = {
  "gt": ROOT / "Applications/GroceryTracker/CHANGELOG.md",
  "lh": ROOT / "Applications/LaunchHeim/CHANGELOG.md",
}

# Files the site shows, taken from their real homes.
ASSETS = {
  "img/c-logo.svg": ROOT / "Assets/C-logo.svg",
  "img/grocerytracker.svg": ROOT / "Applications/GroceryTracker/assets/GT-logo-optimized.svg",
  "img/launchheim.svg": ROOT / "Applications/LaunchHeim/src/Desktop/packaging/launchheim.svg",
  "fonts/KodeMono.woff2": ROOT / "Applications/GroceryTracker/src/UI/GroceryTracker.UI.Blazor/wwwroot/fonts/KodeMono.woff2",
  "fonts/OFL.txt": ROOT / "Applications/GroceryTracker/src/UI/GroceryTracker.UI.Blazor/wwwroot/fonts/OFL.txt",
}

# Which release file is which download, by how its name ends.
LAUNCHHEIM_FILES = {
  "windows": "-win-x64.zip",
  "arch": ".pkg.tar.zst",
  "deb": "_amd64.deb",
  "rpm": ".x86_64.rpm",
}
GROCERYTRACKER_FILES = {
  "compose": "compose.yaml",
  "env": "env.example",
}


def fetch_releases(require: bool) -> list[dict]:
  token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
  request = urllib.request.Request(
    f"https://api.github.com/repos/{REPO}/releases?per_page=100",
    headers={"Accept": "application/vnd.github+json", "User-Agent": "CodeIsNotEvil-site-build"},
  )
  if token:
    request.add_header("Authorization", f"Bearer {token}")
  try:
    with urllib.request.urlopen(request, timeout=30) as response:
      return json.load(response)
  except Exception as error:  # noqa: BLE001 - any failure means "no release data"
    if require:
      raise SystemExit(f"Could not read the releases from GitHub: {error}")
    print(f"warning: no release data ({error}); the download page shows build instructions", file=sys.stderr)
    return []


def newest(releases: list[dict], prefix: str) -> dict | None:
  # The API lists newest first. Drafts and pre-releases are not offered as downloads.
  return next((r for r in releases if r["tag_name"].startswith(prefix) and not r["draft"] and not r["prerelease"]), None)


def release_values(release: dict | None, key: str, prefix: str, files: dict[str, str]) -> dict[str, str]:
  if release is None:
    return {}
  values = {
    f"{key}_release": "yes",
    f"{key}_version": release["tag_name"].removeprefix(prefix),
    f"{key}_tag": release["tag_name"],
    f"{key}_date": datetime.fromisoformat(release["published_at"].replace("Z", "+00:00")).strftime("%-d %B %Y"),
    f"{key}_release_url": release["html_url"],
  }
  for name, suffix in files.items():
    asset = next((a for a in release["assets"] if a["name"].endswith(suffix)), None)
    if asset:
      values[f"{key}_{name}_url"] = asset["browser_download_url"]
      values[f"{key}_{name}_file"] = asset["name"]
      values[f"{key}_{name}_size"] = f"{asset['size'] / 1_000_000:.1f} MB"
  return values


def version_key(version: str) -> tuple[int, ...]:
  return tuple(int(part) for part in version.split("."))


def changelog_html(path: Path, released: str | None) -> str:
  """Every release in the changelog as a <details> block, newest open.

  A version newer than the newest GitHub release is marked, because the changelog is merged before
  the release tag is pushed and the page rebuilds in between.
  """
  blocks = []
  for index, release in enumerate(changelog.parse(path)):
    upcoming = released is not None and version_key(release.version) > version_key(released)
    tag = ' <span class="tag">not released yet</span>' if upcoming else ""
    groups = "".join(
      f"<h4>{html.escape(name)}</h4><ul>" + "".join(f"<li>{changelog.inline_html(b)}</li>" for b in bullets) + "</ul>"
      for name, bullets in release.groups.items())
    blocks.append(
      f'<details{" open" if index == 0 else ""}>'
      f'<summary><strong>{release.version}</strong> <span class="version">{release.date.day} {release.date:%B %Y}</span>{tag}</summary>'
      f"{groups}</details>")
  return "\n".join(blocks)


# A block whose body holds no other "if", so nested blocks resolve innermost first, one level per pass.
_BODY = r"((?:(?!<!-- if:).)*?)"
CONDITIONAL = re.compile(r"<!-- if:(\w+) -->" + _BODY + r"(?:<!-- else -->" + _BODY + r")?<!-- end -->", re.S)
PLACEHOLDER = re.compile(r"\{\{\s*(\w+)\s*\}\}")
RAW_PLACEHOLDER = re.compile(r"\{\{\{\s*(\w+)\s*\}\}\}")


def render(text: str, values: dict[str, str]) -> str:
  while True:
    resolved = CONDITIONAL.sub(lambda m: m.group(2) if values.get(m.group(1)) else (m.group(3) or ""), text)
    if resolved == text:
      break
    text = resolved
  if "<!-- if:" in text or "<!-- end -->" in text:
    raise SystemExit("Unbalanced <!-- if --> / <!-- end --> in a page.")

  def fill(match: re.Match) -> str:
    key = match.group(1)
    if key not in values:
      raise SystemExit(f"{{{{{key}}}}} has no value. Wrap it in <!-- if:... --> or add it to build.py.")
    return html.escape(values[key])

  def fill_raw(match: re.Match) -> str:
    key = match.group(1)
    if key not in values:
      raise SystemExit(f"{{{{{{{key}}}}}}} has no value. Add it to build.py.")
    return values[key]

  # Raw first, or {{ }} would match inside {{{ }}}.
  return PLACEHOLDER.sub(fill, RAW_PLACEHOLDER.sub(fill_raw, text))


def main() -> None:
  parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
  parser.add_argument("output", nargs="?", type=Path, default=ROOT / "site" / "_site")
  parser.add_argument("--require-releases-api", action="store_true", help="fail instead of falling back when GitHub can't be reached")
  parser.add_argument("--releases-json", type=Path, help="read releases from this file (the API's format) instead, to preview the page")
  args = parser.parse_args()

  releases = json.loads(args.releases_json.read_text()) if args.releases_json else fetch_releases(args.require_releases_api)
  values = {
    "repo_url": f"https://github.com/{REPO}",
    "year": str(datetime.now().year),
    **release_values(newest(releases, "grocerytracker-v"), "gt", "grocerytracker-v", GROCERYTRACKER_FILES),
    **release_values(newest(releases, "launchheim-v"), "lh", "launchheim-v", LAUNCHHEIM_FILES),
  }
  for key, path in CHANGELOGS.items():
    values[f"{key}_changes"] = changelog_html(path, values.get(f"{key}_version"))

  if args.output.exists():
    shutil.rmtree(args.output)
  for path in SOURCE.rglob("*"):
    target = args.output / path.relative_to(SOURCE)
    if path.is_dir():
      continue
    target.parent.mkdir(parents=True, exist_ok=True)
    if path.suffix == ".html":
      target.write_text(render(path.read_text(encoding="utf-8"), values), encoding="utf-8")
    else:
      shutil.copy2(path, target)
  for name, source in ASSETS.items():
    target = args.output / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
  # Served as-is: no Jekyll processing of the output.
  (args.output / ".nojekyll").touch()

  found = ", ".join(f"{k.removesuffix('_version')} {v}" for k, v in values.items() if k.endswith("_version")) or "none"
  print(f"Built {args.output} (releases: {found})")


if __name__ == "__main__":
  main()
