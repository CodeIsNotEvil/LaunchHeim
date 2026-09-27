---
tags: [project, site, github-pages]
created: 2026-09-27
updated: 2026-09-27
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

## Open
- No custom domain. A `CNAME` file in `site/src` plus DNS would add one.
