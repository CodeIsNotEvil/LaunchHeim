# LaunchHeim Companion

The Android app for LaunchHeim. It keeps your instances' mod lists on the phone, browses Thunderstore
and adds mods with their dependencies, and shows how many players are on your Valheim servers. Mod
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
so a `launchheim-v*` release describes both apps. AGP 9 compiles Kotlin itself; the Kotlin version is
the one of the Compose compiler plugin in `gradle/libs.versions.toml`.

The *LaunchHeim Android* workflow (`.github/workflows/launchheim-android.yml`) runs the tests and keeps
the release APK as an artifact on every change.

## Releases

Android only installs an update signed with the same key as the installed app. Without a release key
the build falls back to the debug key, which differs per machine and per CI run, so those APKs are for
trying, not for handing out. To sign releases, create a key once and keep it safe; losing it means
users have to uninstall to update:

```fish
keytool -genkeypair -v -keystore launchheim-release.jks -alias launchheim -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 launchheim-release.jks   # the LAUNCHHEIM_ANDROID_KEYSTORE secret
```

and add the repository secrets `LAUNCHHEIM_ANDROID_KEYSTORE`, `LAUNCHHEIM_ANDROID_KEYSTORE_PASSWORD`,
`LAUNCHHEIM_ANDROID_KEY_ALIAS` and `LAUNCHHEIM_ANDROID_KEY_PASSWORD`. Locally the same values go in the
environment variables `LAUNCHHEIM_KEYSTORE` (the file's path), `LAUNCHHEIM_KEYSTORE_PASSWORD`,
`LAUNCHHEIM_KEY_ALIAS` and `LAUNCHHEIM_KEY_PASSWORD`. The APK isn't part of the `launchheim-v*` release
yet.

## Layout

| Package | Holds |
|---|---|
| `packs` | `launchheim.json` and `export.r2x` (ports of Core's `PackManifest` and `R2x`), reading and writing `.r2z`, and `ModListEditor`, which adds, removes, updates and switches mods the way `ModService` does. |
| `thunderstore` | The Valheim package list, downloaded, trimmed and cached like Core's `ThunderstoreIndex`, and the same search and ordering. |
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
