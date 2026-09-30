---
tags: [decision]
created: 2026-09-30
updated: 2026-09-30
status: active
supersedes:
---
# 0008: One CHANGELOG.md per app feeds the release notes and the download page

**Context.** For the 0.2.0 releases the owner wanted users to see what changed between updates on
the GitHub Pages site. Release notes were fixed install text written by the workflows, and
GroceryTracker had no version number at all.

**Decision.** Each app keeps a `CHANGELOG.md` in a small fixed format (`## <version> - <date>`,
`### Added/Changed/Fixed`, bullets). `Scripts/changelog.py` parses it. The release workflows refuse a
tag without a matching section and put that section at the top of the release notes. `site/build.py`
renders every version on the download page (`#<app>-changes`), and marks versions newer than the
newest GitHub release as "not released yet". Each app's version lives in its `Directory.Build.props`,
and the tag must match it.

**Alternatives.** Rendering GitHub release bodies on the site: those are only written at release
time, can't be reviewed in a PR, and the site would depend on the API for content. Generating the list
from commit messages: commits describe how, not what a user notices.

**Consequences.** A release needs a changelog edit in the same PR as the version bump, and the tag
check enforces it. The format is deliberately narrow; the parser rejects anything else.

Related: [[0007-per-app-releases-and-prebuilt-images]], [[app-site]], [[grocerytracker]], [[launchheim]]
