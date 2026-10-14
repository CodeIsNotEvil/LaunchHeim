---
tags: [decision, launchheim, valheim, steam]
created: 2026-10-14
updated: 2026-10-14
status: active
supersedes:
---
# 0017: "Keep Steam achievements with mods" sets the dev command's key, and only undoes its own

**Context.** The owner wanted a global switch so playing modded doesn't cost Steam achievements, having
read that Valheim added a dev command for it. Decompiling 1.0.17: `Achievements.IsCheatedAtAll` counts
`Game.isModded` (set by mods, never by the game) as cheated, and `CanGetAchievements` then only allows
them when `PlayerProfile.s_bypassCheatChecks` is true. That reads the local player's unique key
`bypasscheatchecks` = `1`, which the dev command `yesiuseddevcommandsbutiwantmyachievementsanyway [1|0]`
sets (hidden behind `devcommands`). Unique keys are saved with the character.

**Decision.** (2026-10-14)
- A Settings switch (`AppSettings.KeepAchievements`, off by default). Every instance launch gets
  LaunchHeim's game plugin and `-launchheim-achievements 1|0`; the plugin's `Achievements` part sets
  the key on each character as it enters a world, with the game's own `AddUniqueKeyValue`.
- It also adds a marker key `launchheimbypasscheatchecks 1`. With `0` it removes both keys, but only
  where the marker is: a key the player set with the command is left alone. A key that was already `1`
  isn't marked, so LaunchHeim never takes back what it didn't set.
- Vanilla launches get nothing: without mods nothing blocks achievements, and starting LaunchHeim's
  vanilla BepInEx just for this would be pointless.

**Alternatives.**
- Resetting `Game.isModded` to false: undoes what Iron Gate asked mods to do (the "Modded" note in the
  menu) and races with mods that set it later.
- Patching `CanGetAchievements` with Harmony: more than the game's own switch, and breaks on updates.
- Removing the key when the session ends: the game saves the character on its own schedule, and the
  plugin has no reliable hook before that; the marker makes "off" work at the next launch instead.

**Consequences.** Like the command, it also lets items and pieces made with dev commands count; the
setting's text says so. The key stays on a character played elsewhere (r2modman, another PC) until
LaunchHeim launches it with the setting off. Untested in the real game when written.

Related: [[launchheim]], [[0016-launchheim-instant-start-plugin]]
