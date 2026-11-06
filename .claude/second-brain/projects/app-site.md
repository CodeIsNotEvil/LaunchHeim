---
tags: [project, site, github-pages]
created: 2026-09-27
updated: 2026-11-06
status: active
---
# App site

LaunchHeim's public page: https://codeisnotevil.github.io/LaunchHeim/ (features, screenshots,
downloads, install guides, the privacy policy, Ko-fi at https://ko-fi.com/codeisnotevil). Until
2026-11-06 it was part of MyMonoRepo's site for all of the owner's apps,
https://codeisnotevil.github.io/MyMonoRepo/, which now forwards LaunchHeim's addresses here
([[0018-launchheim-own-repository]]). Notes below from before that date describe the shared site. Code: `site/`, how it works:
`site/README.md`. Why it's built this way: [[0007-per-app-releases-and-prebuilt-images]].

## Gotchas
- Releases created by a workflow's GITHUB_TOKEN don't fire `release: published` elsewhere, so the
  release workflows start `site.yml` explicitly (2026-09-27).
- GHCR packages pushed by Actions start out private. Each one has to be made public once in its
  package settings, or `compose pull` fails for everyone else.
- GitHub's `paths` filters are ignored for tag pushes, so the release tags always build.
- `sudo pacman -U <url>` fails for the unsigned LaunchHeim package: pacman fetches `<url>.sig` because
  `RemoteFileSigLevel` defaults to `Required`, and GitHub answers 404. Local files use
  `LocalFileSigLevel = Optional`, so the page says download, `sha256sum -c`, then `pacman -U ./file`.
  The redirected `release-assets.githubusercontent.com` link fails too ("File name too long", pacman
  keeps the query string in the file name) (2026-10-01).
- The release API gives every asset a `digest` (`sha256:<hex>`), so the page shows checksums without a
  SHA256SUMS file in the release (2026-10-01).
- LaunchHeim is in the Microsoft Store as a Win32 (EXE) app: https://apps.microsoft.com/detail/xpfpghrfcl4sfl
  (ID `XPFPGHRFCL4SFL`). The Store runs the same Inno setup, so a Store install updates through
  LaunchHeim's own "Update now" like a downloaded one. Leave `?hl=&gl=` off the link so the Store
  follows the visitor's locale (2026-10-11).
- The download picker is CSS-only (radio buttons + `~`), because the site has no JavaScript. The radios
  must come before the tabs and panels in the markup, since CSS only reaches later siblings (2026-10-11).
- The download page showed one app at a time via `:target` + `:has()` (2026-10-11). With only
  LaunchHeim on it that picker went (2026-11-06); the section keeps its `#launchheim` id, so old links
  to `download.html#launchheim` still land on it.
- The owner wants the site short: the front page says little, the Store is only on the download page,
  and there are no "first steps" (people know how to use the apps) (2026-10-11).

## Open
- No custom domain. A `CNAME` file in `site/src` plus DNS would add one.
