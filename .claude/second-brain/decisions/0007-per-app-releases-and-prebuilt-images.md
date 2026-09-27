---
tags: [decision, releases, ci, grocerytracker, launchheim]
created: 2026-09-27
updated: 2026-09-27
status: active
supersedes:
---
# 0007: Per-app releases by tag prefix, prebuilt images, and a download page that follows them

**Context.** The owner wants a public page that promotes the apps and offers downloads, and doesn't want
people who self-host GroceryTracker to build `main` to install or update it. The repo is a monorepo, so
one repository holds several apps with independent versions.

**Decision.**
- Releases are tagged per app: `grocerytracker-vX.Y.Z` and `launchheim-vX.Y.Z`. Each tag starts its
  own workflow, which builds, tests and creates a GitHub release. Neither is marked "latest", because
  that is one flag per repository.
- GroceryTracker ships as multi-arch images (amd64 and arm64) on GHCR, plus a release carrying
  `deploy/release/compose.yaml` and `env.example`. Users update with `compose pull`. `main` publishes
  `:edge`. The Dockerfiles run the .NET SDK on `$BUILDPLATFORM` and publish portable IL without an
  apphost, so arm64 needs QEMU only for the tiny runtime layers.
- LaunchHeim's release attaches the Windows zip, a prebuilt Arch package, the .deb and the .rpm.
- The site (`site/`, GitHub Pages) resolves each app's newest release through the GitHub API at build
  time. The release workflows re-run it via `workflow_dispatch`, because releases made with
  GITHUB_TOKEN don't trigger other workflows.

**Alternatives.** GitHub's `releases/latest/download/...` links can't tell the apps apart in a
monorepo. Hard-coding versions in the pages means editing HTML on every release. Client-side
JavaScript against the API hits anonymous rate limits and breaks without JavaScript. Building the
.NET stages under QEMU for arm64 is slow and known to crash.

**Consequences.** Releasing is "bump, merge, tag, push", and the owner does it (Claude doesn't push
release tags unasked). One-time manual setup: GitHub Pages source "GitHub Actions", and making the
three GHCR packages public after their first push. `deploy/release/compose.yaml` mirrors
`docker-compose.yml` and has to be kept in step with it.

Related: [[grocerytracker]], [[launchheim]], [[app-site]]
