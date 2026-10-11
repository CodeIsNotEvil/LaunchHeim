---
tags: [project, site, github-pages]
created: 2026-09-27
updated: 2026-10-11
status: active
---
# App site

The public page for the owner's apps: https://codeisnotevil.github.io/MyMonoRepo/ (promotion,
downloads, install guides, Ko-fi at https://ko-fi.com/codeisnotevil). Code: `site/`, how it works:
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
- The download page shows one app at a time via `:target` + `:has()`, so the hash in the address is
  the state: front-page links must keep pointing at `download.html#<app>` (2026-10-11).
- The owner wants the site short: the front page says little, the Store is only on the download page,
  and there are no "first steps" (people know how to use the apps) (2026-10-11).

## Open
- No custom domain. A `CNAME` file in `site/src` plus DNS would add one.
