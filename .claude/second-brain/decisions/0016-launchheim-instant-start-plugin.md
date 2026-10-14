---
tags: [decision, launchheim, bepinex, valheim]
created: 2026-10-14
updated: 2026-10-14
status: active
supersedes:
---
# 0016: "Start instantly" is a LaunchHeim BepInEx plugin, opt-in per server or world

**Context.** Direct play ([[0010-launchheim-direct-play-uses-the-games-own-options]]) leaves clicks:
a world opens with the character and world selected and the player clicks Start twice, a server stops
at the character selection. The owner asked for worlds to load instantly and servers to join without
the stop. Decompiling Valheim 1.0.17 (Unity 6000.0.75) again confirmed the client has no option for
either: `-world`/`-name` are dedicated-server options, `-joinserverwithcharacter` runs in `Awake`
before Steam and only finds local characters.

**Decision.** (2026-10-14)
- A BepInEx plugin, `src/InstantStart` (net472), presses Start by calling what the buttons call:
  `FejdStartup.OnStartGame`, `OnCharacterStart`, `OnWorldStart` (or `OnCharacterStart` alone with a
  `+connect` join queued). It first checks that the game selected the character and world from the
  PlayerPrefs LaunchHeim set, stops the intro film, waits while a `UnifiedPopup` is open, and gives up
  after 120 s (180 s for a server) leaving the menu to the player. It acts only with
  `-launchheim-instant` on the command line.
- Valheim's classes are reached by reflection; BepInEx.Core 5.4.21 and UnityEngine.Modules 6000.0.75
  come from nuget.bepinex.dev (source-mapped in `nuget.config`), so CI builds it without the game.
- Opt-in per server or world (`PlayChoice.InstantStart`, the Play dialog's switch, `--instant`),
  the owner's choice over "always for instances" or "hidden BepInEx for every vanilla launch".
- Instances get the plugin copied into `BepInEx/plugins/LaunchHeim/` before such a launch. Vanilla
  runs from a LaunchHeim-managed setup in `instant-start/` (BepInExPack from Thunderstore, downloaded
  once, and the plugin), built with a second `InstanceStore`/`ModService` on that folder.
- Never blocks a launch: if the plugin or the vanilla setup can't be got ready, the game starts as
  before and LaunchHeim says why.
- The plugin doesn't set `Game.isModded` (Iron Gate asks mods to), because that switches off Steam
  achievements (`Achievements.IsCheatedAtAll`) and the plugin changes nothing in the game. The game
  sets the flag nowhere itself, so BepInEx alone doesn't trip it.

**Alternatives.**
- A Doorstop entry assembly of LaunchHeim's own instead of BepInEx for vanilla: Unity isn't ready when
  Doorstop's entry runs, and BepInEx exists to solve exactly that.
- Simulating input (keys or clicks): depends on layout, focus and timing, and can't check what's selected.
- Compiling against `assembly_valheim.dll`: CI has no game files, and publicized game assemblies on
  NuGet would tie each build to a game version.

**Consequences.** Not yet tried in the real game when written (only checked against the decompiled
menu code and by running the launch paths against a stand-in game): check a vanilla and a modded world,
a server with and without a password, the first start with the intro film, and the "What's new"
popup after a game update. A world started instantly isn't hosted ("Start server" isn't remembered
by the game). A game update that renames the menu's members only makes the plugin give up, with a
line in the BepInEx log.

Related: [[launchheim]], [[0010-launchheim-direct-play-uses-the-games-own-options]], [[0015-launchheim-steam-launch-options-wrap-steams-command]]

**Renamed 2026-10-14** (before 0.7.0 shipped): the plugin also keeps achievements now
([[0017-launchheim-keeps-achievements-with-the-games-own-switch]]), so it is `src/GamePlugin`,
`LaunchHeim.GamePlugin.dll`, one BepInEx plugin `io.github.codeisnotevil.launchheim`, and Core's
`InstantStart` became `GamePlugin`. It is now copied into every instance launch, not only instant ones,
and clears the old `LaunchHeim.InstantStart.dll` from the folder.
