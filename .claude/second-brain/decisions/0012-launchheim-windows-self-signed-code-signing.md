---
tags: [decision, launchheim, windows, release]
created: 2026-10-08
updated: 2026-10-08
status: active
supersedes:
---
# 0012: LaunchHeim's Windows files are signed with a self-signed certificate

**Context.** The owner wanted the LaunchHeim binaries signed "so everyone knows where they come from",
and was fine with Windows still not trusting the publisher. A key for the Android companion's APKs
already existed (`launchheim-release.jks`, backed up in `/mnt/BackupHDD/KeyBackups/LaunchHeim/`).

**Decision.** (2026-10-08)
- Authenticode on Windows with a self-signed X.509 certificate, `CN=CodeIsNotEvil`, RSA 4096, EKU
  code signing only, `CA:FALSE`, valid ten years. Thumbprint `284F5384B189984492629622EDC4A40646C5D8F6`.
- A new key, not the APK key: its passwords only exist as GitHub secrets, and one leaked key shouldn't
  compromise both channels.
- `build.ps1` signs only what it compiles (`LaunchHeim.exe`, `LaunchHeim.dll`, `LaunchHeim.Core.dll`,
  `QmlNet.dll`, `LaunchHeimAppIcon.dll`), time stamped. Qt and the VC runtime aren't ours to vouch for.
- The public certificate is committed (`packaging/windows/launchheim-codesign.cer`). The build refuses
  a `.pfx` with another certificate, and the download page and release notes print the thumbprint, so
  people compare against something that doesn't come from the zip.
- CI: secrets `LAUNCHHEIM_WINDOWS_SIGNING_PFX` / `_PASSWORD`. Tags fail without them, like the APK key.

**Alternatives.**
- A commercial OV/EV certificate or Azure Trusted Signing: the only way past SmartScreen's "unknown
  publisher", but costs money and an identity check. Not wanted for now.
- Reusing the APK key: rejected, see above.
- GPG signatures for the pacman/deb/rpm packages: a different mechanism (OpenPGP, not X.509), not part
  of this. It would also fix `pacman -U <url>` needing a `.sig`.

**Consequences.** Users can check `Get-AuthenticodeSignature` against the thumbprint, and a modified
file shows `HashMismatch`. Windows still warns. Replacing the key changes the thumbprint in three
places (packaging README "Code signing", `site/src/download.html`, `launchheim-release.yml`) plus the
committed `.cer`. Timestamps keep old signatures valid after 2036.

Related: [[launchheim]], [[0011-launchheim-companion-syncs-packs-over-localsend]]
