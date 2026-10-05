# LaunchHeim Companion

The Android app for LaunchHeim. It keeps your instances' mod lists on the phone, browses Thunderstore,
Nexus Mods and CurseForge and adds mods with their dependencies, and shows how many players are on
your Valheim servers. Mod
lists travel between the phone and LaunchHeim over the local network with the
[LocalSend protocol](https://github.com/localsend/protocol), so there is no account and no server in
between. The LocalSend app sees the phone too and can send it modpacks.

Kotlin with Jetpack Compose and Material 3, Android 8 (API 26) and newer. MIT-licensed like LaunchHeim;
the libraries it ships are listed in [`app/src/main/assets/THIRD-PARTY-NOTICES.txt`](app/src/main/assets/THIRD-PARTY-NOTICES.txt),
which Settings → Third-party licenses shows.

## Use

1. In LaunchHeim on the PC: Settings → Phone sync, switch on receiving (only needed for the way back).
2. Open the app on the phone, on the same Wi-Fi.
3. In LaunchHeim: Library → Send to phone (or an instance's Send to phone), pick the phone, and accept
   on the phone. Instances arrive as mod lists, Valheim's server list comes along.
4. On the phone, add, remove, update or switch off mods, then the send button on the list. LaunchHeim
   shows what changed and installs it into the instance when you click Update instance.

Lists can also be started on the phone (New list) or opened from an `.r2z` file (the open button, or
"Open with" from a file manager or messenger), and shared as a file through Android's share sheet.

Away from the PC (on the bus, say) nothing needs the PC: the Thunderstore list is cached on the phone,
and Nexus and CurseForge are searched over mobile data. Back home, send the list. A list that came from
an instance offers **Update instance** on the PC (or **Import as a copy**); a list started on the phone,
or one made with **Duplicate as a new list**, becomes a new instance.

## Build

Needs JDK 17 or newer and the Android SDK with platform 37 (OkHttp 5.5 requires compileSdk 37). With
the SDK in `~/.local/share/android-sdk`:

```fish
echo "sdk.dir=$HOME/.local/share/android-sdk" > local.properties
./gradlew :app:testDebugUnitTest          # the unit tests, plain JVM
./gradlew :app:assembleDebug              # app/build/outputs/apk/debug/app-debug.apk
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

The SDK command-line tools come from developer.android.com (`commandlinetools-linux-*_latest.zip`,
unpacked to `cmdline-tools/latest`), then
`sdkmanager --sdk_root=$HOME/.local/share/android-sdk "platforms;android-37.0" "build-tools;37.0.0" platform-tools`.
For an emulator add `emulator` and `"system-images;android-36;google_apis;x86_64"`, create one with
`avdmanager create avd -n lh-test -k "system-images;android-36;google_apis;x86_64" -d pixel_7` and start it
with `emulator -avd lh-test`. Multicast doesn't leave the emulator's NAT, so LaunchHeim can't find it;
`adb forward tcp:53630 tcp:53317` makes its LocalSend port reachable as `127.0.0.1:53630` on the PC.

The version is LaunchHeim's: `app/build.gradle.kts` reads `<Version>` from `../Directory.Build.props`,
so a `launchheim-v*` release describes both apps. Next to it sits `<AndroidVersionCode>` (major ×
10000 + minor × 100 + patch), written out for F-Droid, which reads it with a regex; the build fails
when the two disagree. The package name is `io.github.codeisnotevil.launchheim` (the same id as the
desktop app's AppStream metadata), the code's package `local.cine.launchheim`, so the activity is
`adb shell am start -n io.github.codeisnotevil.launchheim/local.cine.launchheim.MainActivity`. AGP 9 compiles Kotlin itself; the Kotlin version is
the one of the Compose compiler plugin in `gradle/libs.versions.toml`.

The *LaunchHeim Android* workflow (`.github/workflows/launchheim-android.yml`) runs the tests, builds
the release APK the way F-Droid does (unsigned, minified) and keeps a debug APK to try as an artifact.

## Releases

F-Droid builds the companion from source and signs it with its own key; there is no APK on the GitHub
releases. Since it shares LaunchHeim's version and tags, a desktop release is a companion release:

1. Bump `<Version>` and `<AndroidVersionCode>` in `../Directory.Build.props` (the Android build fails if
   they disagree).
2. Write `app/fastlane/metadata/android/en-US/changelogs/<versionCode>.txt`, what changed in the
   companion in at most 500 characters. F-Droid shows it as "What's new".
3. Tag `launchheim-v<version>` as usual. The release workflow refuses the tag when either is missing,
   because F-Droid reads both from the tagged source and nothing can be added afterwards.

F-Droid's checkupdates finds the tag (`UpdateCheckMode: Tags`), reads the two versions from
`Directory.Build.props` (`UpdateCheckData`), adds a build and publishes it, usually within a few days.
The store texts, icon and screenshots come from `app/fastlane/metadata/android/en-US/`; F-Droid only
looks for them under the build's subdir.

**Getting listed (once).** The build recipe is [`fdroid/io.github.codeisnotevil.launchheim.yml`](fdroid/io.github.codeisnotevil.launchheim.yml),
formatted the way `fdroid rewritemeta` writes it (fdroiddata's CI rejects anything else, comments
included). After the first `launchheim-v*` tag with the companion in it (the recipe expects 0.5.0;
change `versionName`, `versionCode`, `commit` and `Current*` if it's another one):

1. Fork https://gitlab.com/fdroid/fdroiddata, add the file as `metadata/io.github.codeisnotevil.launchheim.yml`
   on a branch, and open a merge request with the "App inclusion" template.
2. Its CI builds the app and runs the scanner. A reviewer may ask about the anti-feature or the
   category; `NonFreeNet` is declared because Nexus Mods and CurseForge are proprietary services.
3. Once merged, the app appears on F-Droid with the next index update, and the download page links it
   by itself (`site/build.py` asks F-Droid's API).

To try the recipe locally: `pip install fdroidserver`, put the file into `metadata/` of an fdroiddata
checkout with `sdk_path` set in its `config.yml`, then `fdroid lint`, `fdroid rewritemeta` (must change
nothing) and `fdroid build -v -l io.github.codeisnotevil.launchheim`, which clones the repo and builds
the tagged commit with the scanner. Before the tag exists, point `commit` at a pushed commit.

Points that matter for F-Droid, and why the build looks the way it does:
- The release build is unsigned (F-Droid signs it), and `dependenciesInfo` is off, because AGP would
  otherwise embed a dependency list encrypted for Google, which F-Droid refuses.
- No JVM toolchain is requested, so any JDK 17 or newer on the build server works.
- Everything the APK contains is open source (`app/src/main/assets/THIRD-PARTY-NOTICES.txt`), and the
  only binary in the whole repository is the Gradle wrapper jar, which the scanner knows.
- Google's developer verification for apps from outside Play (enforced in some countries since
  2026-09-30, everywhere in 2027) applies to F-Droid's apps too, and how F-Droid will handle it isn't
  settled; F-Droid has objected to it publicly. Watch their announcements before 2027.
- Should a GitHub APK ever be wanted again, it needs a signing key of its own, and it can't update an
  F-Droid install or the other way round (different signatures).

## Layout

| Package | Holds |
|---|---|
| `packs` | `launchheim.json` and `export.r2x` (ports of Core's `PackManifest` and `R2x`), reading and writing `.r2z`, and `ModListEditor`, which adds, removes, updates and switches mods the way `ModService` does. |
| `thunderstore` | The Valheim package list, downloaded, trimmed and cached like Core's `ThunderstoreIndex`, and the same search and ordering. |
| `catalogs` | Nexus Mods (anonymous GraphQL) and CurseForge (API key), ports of Core's catalogs for browsing only, and Nexus's BBCode. |
| `servers` | A2S player counts (a port of Core's `ServerQuery`) and the server list file. |
| `localsend` | The LocalSend node: discovery, a small HTTP server, sending. |
| `data` | The phone's storage (mod lists, servers, settings) and `SyncService`, which turns received files into lists and lists back into packs. |
| `ui` | The Compose screens and their view models. |

Everything outside `ui` and `CompanionApp.kt` is plain JVM code and covered by the tests in
`app/src/test`.

## How it works

**Same rules as the desktop.** The phone never installs anything; it edits a mod list, and LaunchHeim
installs it. So the list must end up as the desktop would have it. `ModListEditor` follows
`ModService`: dependencies come along at the newest version (an installed one that is new enough is
kept), removing a mod also removes the dependencies only it needed, turning a mod on turns on what it
needs, and BepInExPack is always there and never removable. Search scoring and order match
`ThunderstoreCatalog.Search`. When one side changes, change the other.

**Nexus and CurseForge.** Searched on their servers a page at a time. A mod is added with a file id,
which is how Core pins those sites (`PackMod.FileId`); LaunchHeim downloads it when the list comes
back, with its own API keys, and names what it can't download (Nexus without Premium, CurseForge
opt-outs) to install from Browse mods. CurseForge lists required dependencies per file, and the phone
adds them like Thunderstore's; Nexus lists none. LaunchHeim keeps a dependency that a returning list
doesn't mention while a mod in it still needs it, so what it pulled in itself survives. The phone
needs its own CurseForge key (Settings), kept in the app's private storage; Nexus browsing needs none.

**Thunderstore.** As on the desktop, the whole Valheim list is downloaded (about 17 MB) because
Thunderstore has no search API for apps. Each gzipped chunk is parsed as a stream and trimmed right
away, so the 170 MB of JSON never sits in memory. The trimmed list is cached in the app's cache folder
for six hours and used offline when it's older.

**Packs.** A mod list is the `.r2z` it came in: `launchheim.json` (with the new `instanceId` of the
desktop instance), `export.r2x` and the configs. The phone keeps the original file and writes the two
lists into a copy when it sends one, so configs and local mod files go back unchanged. LaunchHeim
matches a returning pack to its instance by `instanceId`, lists the changes, and on Update instance
removes, installs and switches mods; configs and launch options on the PC stay as they are. A list
made on the phone has no `instanceId` and becomes a new instance. When the PC later sends that
instance back, the phone replaces its own list of the same name instead of keeping two.

**LocalSend.** Each side announces itself by multicast (`224.0.0.167:53317`), answers others with
`POST /register`, and receives with `/prepare-upload` and `/upload`. If nothing answers within a few
seconds, every address in the local /24 is asked directly, as LocalSend does for Wi-Fi that drops
multicast. Only HTTP is offered: HTTPS would need a certificate per device, and LocalSend clients use
whatever protocol a device announces. Talking to a LocalSend app in HTTPS mode works; its self-signed
certificate is accepted without pinning, because the protocol doesn't say how its fingerprint is
computed. Both apps put `LaunchHeim` in `deviceModel` to find each other among other LocalSend devices.
Every transfer is accepted by a person, and the phone only takes `.r2z` files and
`launchheim-servers.json`; LaunchHeim only `.r2z`. If the LocalSend app already holds port 53317, the
next free port is used and announced. The phone runs the node only while the app is open (and holds a
`WifiManager.MulticastLock` meanwhile, without which Android drops multicast).

**Servers.** LaunchHeim sends `launchheim-servers.json` (Valheim's Favorites and Recent dedicated
servers, no passwords) with every sync, which replaces the phone's "From your PC" list. Servers added
on the phone stay. Player counts come from A2S on the game port + 1, every 30 seconds while the page
is open, like the Play page.
