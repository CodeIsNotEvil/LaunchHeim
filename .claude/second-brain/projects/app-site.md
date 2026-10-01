---
tags: [project, site, github-pages]
created: 2026-09-27
updated: 2026-10-01
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

## Open
- No custom domain. A `CNAME` file in `site/src` plus DNS would add one.
