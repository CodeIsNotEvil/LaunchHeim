---
tags: [decision, launchheim, android]
created: 2026-10-05
updated: 2026-10-05
status: active
supersedes:
---
# 0011: LaunchHeim Companion syncs .r2z packs over LocalSend, written in Kotlin

**Context.** The owner asked for an Android companion for [[launchheim]] that browses mods and adds them
to instances, shows the servers with their player counts, and shares instances "via local send". The
app never runs Valheim, so it can only edit mod lists for the desktop to install. Syncing had to work
without a server or an account.

**Decision.** (Owner's choices on 2026-10-05: Kotlin + Compose, the LocalSend protocol in both apps.)
- The phone app is native Kotlin with Jetpack Compose in `Applications/LaunchHeim/android/`, carrying
  LaunchHeim's version.
- The unit of sync is the existing `.r2z` pack ([[0009-launchheim-modpacks-are-r2z]]), plus
  `launchheim-servers.json` for the server list. `launchheim.json` gains `instanceId`, so a pack coming
  back is matched to its instance; LaunchHeim shows the changes and applies them (mods only, configs
  stay).
- Transfer is the LocalSend protocol v2, implemented in both apps (`Core/LocalSend`,
  `android/.../localsend`), HTTP only, over a small hand-written HTTP server. Both put `LaunchHeim` in
  `deviceModel`. Every transfer is accepted by a person; only packs (and on the phone the server list)
  are taken.
- Desktop receiving is off until switched on in Settings; the phone listens only while the app is open.

**Alternatives.**
- .NET (Avalonia or MAUI) reusing `LaunchHeim.Core`: one copy of the pack, index and A2S code. Rejected
  by the owner in favour of a native Kotlin app; the price is two implementations to keep in step.
- Relying on the LocalSend app to move files (share sheet only): no desktop changes, but no automatic
  instance matching and two apps to juggle.
- Kestrel or `HttpListener` for the desktop server: Kestrel adds the ASP.NET runtime to every package,
  `HttpListener` needs admin rights on Windows for non-localhost prefixes.
- HTTPS like LocalSend's default: needs a certificate per device (no X.509 builder on Android without a
  library). LocalSend clients use whatever protocol a peer announces, so HTTP interoperates.

**Consequences.**
- `ModListEditor.kt` must keep doing what `ModService` does (newest dependencies, orphan removal,
  enabling cascades, BepInEx always present), and the Thunderstore search must match
  `ThunderstoreCatalog.Search`. Changing one side means changing the other.
- `launchheim.json` stays readable by both: kotlinx.serialization with unknown keys ignored on the
  phone, System.Text.Json on the desktop. The desktop reads `exportedAt` as a `DateTimeOffset` and
  rejects the whole pack over an empty string, so the phone always stamps it.
- LAN traffic is unencrypted and unauthenticated apart from the accept dialog. Fine for mod lists; no
  passwords are sent (the server list has none).
- The APK is published with the `launchheim-v*` release and must be signed with the release key, so a
  tag fails without it. Losing the key strands every installed copy.

Related: [[launchheim]], [[0009-launchheim-modpacks-are-r2z]], [[0010-launchheim-direct-play-uses-the-games-own-options]]
