---
tags: [decision, launchheim]
created: 2026-10-04
updated: 2026-10-04
status: active
supersedes:
---
# 0010: LaunchHeim's direct play uses Valheim's own options, not a helper plugin

**Context.** The owner wanted to start Valheim straight into a server or an existing world, with a
character picked per server or world, and the character and setup (vanilla or an instance)
remembered for each. Valheim keeps owning saves and servers. It has to work for vanilla launches too,
which can't load any plugin. Decompiling `assembly_valheim.dll` (2026-10-04) showed what the client
offers: `+connect host:port` (what Steam's "Join game" passes) joins a dedicated server after the
character selection, `-password` answers the server's prompt, and the menus preselect whatever the
PlayerPrefs `profile` and `world` hold. There is no client option to load a world.

**Decision.** Use only those. A server launch passes `+connect`/`-password` and sets `profile`, so one
click on Start joins. A world launch sets `profile` and `world`, so the player clicks through Start
with everything already selected. LaunchHeim reads Valheim's own lists with the game's rules and stores
its choices in `settings.json` (`PlayChoice`). Code: `Applications/LaunchHeim/src/Core/Saves/`,
`GameLauncher.JoinArguments`, `src/Desktop/ViewModels/PlayViewModel.cs`.

**Alternatives.**
- `-joinserverwithcharacter <address> <character> <x>` skips the character selection, but it runs in
  `FejdStartup.Awake` before Steam is initialised and loads the character from the local folder only.
  Most characters are in Steam Cloud, so it would fail for them, and probably crash before Steam is up.
- A LaunchHeim BepInEx plugin that clicks through the menus would make worlds fully direct, but only
  for modded instances, and it means building against game assemblies in CI and shipping a mod.
- Copying saves or keeping LaunchHeim's own server list would duplicate what Valheim already manages.

**Consequences.** Works the same for vanilla and every instance, and nothing in the game folder or
the saves changes. Worlds still need the player to click Start. If that matters later, a helper
plugin for modded launches can add on top without changing this. Writing PlayerPrefs only works while
the game is closed, because Unity writes them all on quit, so it happens right before LaunchHeim
starts the game. A server password sits in `settings.json`, which is mode 0600 for the API keys anyway.

Related: [[launchheim]]
