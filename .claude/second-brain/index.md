---
tags: [moc]
created: 2026-11-06
updated: 2026-11-06
status: active
---
# Index

The map of content. Keep it short: link to hub notes, not to every note. Until 2026-11-06 LaunchHeim
lived in CodeIsNotEvil/MyMonoRepo; the notes below came from its second brain
([[0018-launchheim-own-repository]]).

## Projects
- [[launchheim]]: the launcher, the companion app, key ideas, gotchas and what's open
- [[app-site]]: the GitHub Pages site with downloads, install guides and the privacy policy

## Decisions
Numbers 0001 to 0003 are GroceryTracker's and stayed in MyMonoRepo; the numbers here were kept so the
references in code and READMEs stay right.
- [[0004-cine-root-namespace]]: all .NET namespaces start with `CINE.`; assembly names do not
- [[0005-launchheim-distro-packages-use-system-qt]]: pacman/deb/rpm builds use the distro's Qt 5.15
- [[0006-launchheim-windows-keeps-qml]]: Windows keeps the QML UI via a patched QmlNet.dll, no WinUI
- [[0007-per-app-releases-and-prebuilt-images]]: tag-prefixed releases and a download page that follows them
- [[0008-changelog-per-app]]: CHANGELOG.md drives the release notes and the site's version history
- [[0009-launchheim-modpacks-are-r2z]]: modpacks are r2modman `.r2z` files plus `launchheim.json`
- [[0010-launchheim-direct-play-uses-the-games-own-options]]: joining servers and opening worlds uses `+connect` and Valheim's PlayerPrefs, no plugin
- [[0011-launchheim-companion-syncs-packs-over-localsend]]: the Android companion (Kotlin) syncs `.r2z` packs and the server list over LocalSend
- [[0012-launchheim-windows-self-signed-code-signing]]: the Windows exe and own DLLs are Authenticode-signed with a self-signed certificate
- [[0013-launchheim-windows-setup-with-inno-setup]]: the Windows setup is Inno Setup; installed copies update by running the new setup
- [[0014-launchheim-steam-overlay]]: Steam's overlay via preload on Linux and `steam.exe -applaunch` on Windows
- [[0015-launchheim-steam-launch-options-wrap-steams-command]]: `launchheim steam %command%` in Valheim's launch options starts the last play from Steam
- [[0016-launchheim-instant-start-plugin]]: "Start instantly" is a BepInEx plugin pressing Start in Valheim's menus, opt-in per server or world
- [[0017-launchheim-keeps-achievements-with-the-games-own-switch]]: "Keep Steam achievements with mods" sets the dev command's character key, and only undoes its own
- [[0018-launchheim-own-repository]]: LaunchHeim, its history and its site moved out of MyMonoRepo

## Inbox
Untriaged captures live in `inbox/`. Empty it now and then.
