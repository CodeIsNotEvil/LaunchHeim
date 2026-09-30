---
tags: [decision, launchheim]
created: 2026-09-30
updated: 2026-09-30
status: active
supersedes:
---
# 0009: LaunchHeim modpacks are r2modman `.r2z` files with a `launchheim.json` beside `export.r2x`

**Context.** The owner wanted instances exportable and importable so people can build and share
modpacks. Valheim players already pass r2modman / Thunderstore Mod Manager profile exports (`.r2z`:
a zip with `export.r2x` in YAML plus the configs) around, but that format only knows Thunderstore
mods and their versions. LaunchHeim also installs Nexus, CurseForge and local mods, and tracks which
mods were only dependencies.

**Decision.** Export writes an `.r2z` that r2modman can import unchanged: `export.r2x` with the
Thunderstore mods, configs under `BepInEx/config/`, and local mods' files at their instance paths.
Next to it goes `launchheim.json` (`PackManifest`, with a `format` number) holding every mod with its
source, id, file id, enabled and dependency state, plus the launch arguments. Import prefers
`launchheim.json` and falls back to `export.r2x`, so r2modman's exports import too. Mods are
re-downloaded at the pinned version; only local mods travel as files. `export.r2x` is read and written
by a small line-based reader for r2modman's fixed shape instead of a YAML library.

**Alternatives.** A LaunchHeim-only format (`.lhpack`): no reach into the r2modman crowd, and their
packs wouldn't import. Packing every mod's files: large files, and Nexus/CurseForge files may not be
redistributed. A Thunderstore modpack package (manifest + icon + README) for uploading: needs a 256 px
icon and only covers Thunderstore; left as a possible follow-up. A YAML library: one more NuGet
dependency with notices for a five-key document.

**Consequences.** r2modman extracts `launchheim.json` into its profile folder, harmless but visible.
r2modman skips Nexus and CurseForge mods, so the export toast names them. Importing Nexus mods without
Premium, or CurseForge opt-outs, can't be automatic; they end up in one warning to install from
Browse. Raising `PackManifest.CurrentFormat` makes older LaunchHeims refuse newer packs. Import
resolves all mods first and installs in dependency order, because otherwise a dependent pulls the
newest library and the pinned version is skipped as visited.

Related: [[launchheim]]
