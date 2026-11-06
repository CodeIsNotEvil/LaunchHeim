# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LaunchHeim: a Valheim mod launcher desktop app (.NET 10, Qml.Net / Qt Quick 5.15) for Linux and
Windows, with LaunchHeim Companion, an Android app. Each instance is its own modpack with its own
BepInEx, mods and configs; the game install stays vanilla. It lived in CodeIsNotEvil/MyMonoRepo
(`Applications/LaunchHeim/`) until 2026-11-06 and has its own repository since, with its history
(decision 0018). Old commit messages and notes still use the monorepo paths.

- `src/Core/`: everything without a UI (instances, mods, catalogs, packs, LocalSend, updates, CLI).
  `src/Desktop/`: the Qml.Net host, view models and `qml/`. `src/GamePlugin/`: the BepInEx plugin
  LaunchHeim puts into instances (instant start, achievements). `tests/Core.Tests/`: xUnit.
- `README.md` has the build, the Qt runtime workarounds and how it works; `packaging/README.md` covers
  the pacman, deb, rpm and Windows builds. Windows builds need MSVC and come from the
  `LaunchHeim Windows` GitHub workflow.
- `android/` is LaunchHeim Companion, the Kotlin/Compose phone app that syncs mod lists with the
  desktop over the LocalSend protocol. Its README has the SDK setup and the rules it shares with Core:
  keep `ModListEditor`, the pack format and the LocalSend code in step on both sides.
- `site/`: the GitHub Pages site (https://codeisnotevil.github.io/LaunchHeim/: features, downloads,
  install guides, the privacy policy the Microsoft Store links to), built by `site/build.py` and
  deployed by `.github/workflows/site.yml`. Its README explains the templating.
- `Scripts/`: `changelog.py` (release notes and the site's version history), `text_logo.py` (the
  logo), `kodemono_static.py` (the app's static fonts from `assets/fonts/`).
- When you add or upgrade a dependency, update `THIRD-PARTY-NOTICES.txt` and `licenses/` (README
  "Licenses").

## Commands

```fish
dotnet build LaunchHeim.slnx
dotnet test tests/Core.Tests
dotnet test tests/Core.Tests --filter "FullyQualifiedName~UpdateCheckerTests"
dotnet run --project src/Desktop                # set XDG_RUNTIME_DIR to a short private dir if the installed app is open
./install.sh                                    # build and install for the current user
packaging/build-packages.sh deb rpm arch        # packages into packaging/dist/ (podman)
site/build.py && python3 -m http.server -d site/_site 8000
```

The Desktop project has no tests; the UI is checked with the `LAUNCHHEIM_SCREENSHOT` mode (README).

## Releases

Tags are `launchheim-vX.Y.Z` (the prefix stayed from the monorepo: the update reminder, the site and
the release workflow filter on it). The tag must match `<Version>` in `Directory.Build.props` and have
a section in `CHANGELOG.md`, which the release notes and the download page's version history are
built from. `.github/workflows/launchheim-release.yml` builds and publishes every download on the tag.

Copies up to 0.7.1 look for updates in MyMonoRepo's releases. Until the first release from this
repository has also been published there, that release can't reach them (decision 0018).

Don't create or push release tags, and never publish anything (GitHub releases, the Microsoft Store),
unless the owner asks.

## One repository per application

The owner keeps each application in its own repository (since 2026-11-03), so a project can be made
private or shown on its own. Nothing in here may depend on files in another repository at build time.
Knowledge from the other repositories is copied into the second brain, with a note where it came from.

## Git workflow for Claude

The owner reviews every change. Claude never commits to `main` directly:

1. Start each request on a new branch cut from an up-to-date `main`, with a short kebab-case name
   (`browse-layout-fixes`) and no `claude/` prefix, unless the session prescribes a branch.
2. Make small, focused commits.
3. Push the branch and open a pull request against `main` that says what changed and why.
4. Don't merge it. Push review follow-ups to the same branch.

**Everything in this repository appears as the owner's work.** Commit with the owner's identity
(`git config user.name CodeIsNotEvil`, `user.email codeisnotevil@gmail.com`) and without
`Co-Authored-By` lines, session links or other AI trailers. Leave "Generated with" footers out of
commit messages, PR descriptions and docs. Claude must not show up as a contributor.

## Style

Formatting comes from `.editorconfig`: two spaces, LF, UTF-8 without BOM. C# uses file-scoped
namespaces and primary constructors, even though `.editorconfig` says block-scoped namespaces. Match
the code.

Every .NET project uses the root namespace `CINE` (`CINE.LaunchHeim.Core`), set once in
`Directory.Build.props` with `<RootNamespace>CINE.$(MSBuildProjectName)</RootNamespace>`; project and
assembly names stay unprefixed (decision 0004).

Build file-system paths with the platform's path API, never by joining strings with `/` or `\`:
`Path.Combine` (or `Path.Join`) in C#, `File(parent, child)` or `resolve` in Kotlin, `Join-Path` in
PowerShell, `pathlib` in Python. LaunchHeim, its tests and its CI also run on Windows, where hand-built
paths and expected values in tests break. Forward slashes stay only where a format defines them: zip
entry names, URLs, and the instance-relative paths LaunchHeim stores (normalized by
`ModInstaller.NormalizePath`).

Comments explain *why* a decision was made, and there are many of them. Keep that density when you
edit.

## Second brain

`.claude/second-brain/` is a Markdown knowledge base (Obsidian-compatible, PARA layout plus ADR-style
`decisions/`), carried over from MyMonoRepo. It records why decisions were made, open questions and
gotchas; the project note [[launchheim]] has most of them. Start at its `index.md` before non-trivial
work. When a session settles a design question or turns up a gotcha, update the relevant note and
append to `journal/YYYY-MM-DD.md`. Its `README.md` has the conventions.
