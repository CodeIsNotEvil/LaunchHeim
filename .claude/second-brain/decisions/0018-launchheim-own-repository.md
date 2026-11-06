---
tags: [decision, conventions, releases, site, launchheim]
created: 2026-11-06
updated: 2026-11-06
status: active
supersedes:
---
# 0018: LaunchHeim moves to its own repository

**Context.** LaunchHeim grew up in CodeIsNotEvil/MyMonoRepo (`Applications/LaunchHeim/`), next to
GroceryTracker, configs and experiments, with a shared site, shared scripts and one second brain. On
2026-11-03 the owner decided that every application gets a repository of its own (RcinelTranslate and
RcinelLevel already have one): a single project can then be made private or shown on its own. On
2026-11-06 LaunchHeim followed, into CodeIsNotEvil/LaunchHeim.

**Decision.**
- The history moved with it: MyMonoRepo's commits that touched LaunchHeim, rewritten so the app sits
  at the root (`git filter-branch`, keeping only LaunchHeim's paths). The rewrite also dropped the
  `Co-Authored-By`/`Claude-Session` trailers and gave the one Claude-authored commit to the owner, at
  the owner's request. The `launchheim-v*` tags moved with it, pointing at the rewritten commits; the
  GitHub releases of 0.1.0 to 0.7.1 stay in MyMonoRepo.
- Release tags keep the `launchheim-v` prefix. The update reminder, `site/build.py` and the release
  workflow already filter on it, and the old tags in this repository use it too.
- The site moved to this repository's own GitHub Pages, https://codeisnotevil.github.io/LaunchHeim/
  (owner's choice over keeping it on MyMonoRepo's site). MyMonoRepo's site forwards its LaunchHeim
  addresses here.
- What came from MyMonoRepo was copied, not linked: `Scripts/changelog.py`, `kodemono_static.py` and
  `text_logo.py`, the variable Kode Mono (`assets/fonts/`, from GroceryTracker), `.editorconfig`, and
  the second-brain notes and decisions 0004 to 0017, which keep their numbers so references in code
  comments and READMEs stay right. The journal entries that mention LaunchHeim came along whole, so
  they also mention GroceryTracker work of the same days.

**Alternatives.**
- *Keep the site on MyMonoRepo*, reading releases and files from here: no Store listing changes, but
  a build-time dependency between repositories, which the one-repository rule rules out.
- *Plain `vX.Y.Z` tags* like the Rcinel repositories: tidier, but it splits the tag history and needs
  code changes in three places for no gain to users.
- *A fresh repository without history*: loses `git blame` and the reasons in the commit messages.

**Consequences.**
- Installed copies up to 0.7.1 ask MyMonoRepo's releases for updates. The first release from here has
  to be published in MyMonoRepo too (same tag, same files), or they never hear of it. From that
  version on, copies ask this repository.
- The Arch package's `pkgver` counts commits, and this history is shorter (0.7.1 was r240 in
  MyMonoRepo, about r195 here). The app version comes first, so newer versions still sort higher; only
  a rebuild of 0.7.1 itself from here would look like a downgrade to pacman.
- The Microsoft Store listing has to point at the new site (privacy policy, website, the installer URL
  under `downloads/`). MyMonoRepo's site keeps mirroring the setups of the releases it has, so
  submissions in review keep working until then.
- GitHub Actions secrets (Windows signing, Android release key) don't move with the code; they have to
  be set again here before the next tag.
- Obtainium users added the app with MyMonoRepo's URL and the `^LaunchHeim` filter; they have to add it
  again with this repository.

Related: [[launchheim]], [[app-site]], [[0007-per-app-releases-and-prebuilt-images]]
