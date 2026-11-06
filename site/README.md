# Site

LaunchHeim's GitHub Pages site at https://codeisnotevil.github.io/LaunchHeim/: a front page with the
features and screenshots (`src/index.html`), a download page with install guides (`src/download.html`)
and the privacy policy (`src/privacy.html`, which the Microsoft Store listing links to; keep it true to
the code), in the app's own look (Kode Mono, `#DE5833`, light and dark with the system).
Plain HTML and CSS, no JavaScript and nothing loaded from other sites.

Until 2026-11-06 the site lived in MyMonoRepo and showed GroceryTracker too; that site now only points
here (its old `privacy.html` and `download.html#launchheim` addresses forward to this one).

The logo glints like the owner's animated profile pictures (MyMonoRepo's `Assets/c-logo/`), in CSS only
and at the GIF's pace: about every four seconds, and not at all with reduced motion. The app does the
same with its sidebar logo. The glint layer is masked to the logo's shape, which browsers only allow for images from the
same origin. Preview over HTTP (`python3 -m http.server -d site/_site`), because from `file://`
the masks don't load and no glint shows.

```fish
site/build.py                                   # -> site/_site (git-ignored), then:
python3 -m http.server -d site/_site 8000       # open http://localhost:8000
site/build.py --releases-json sample.json       # preview the download page with made-up releases
```

`build.py` fills in `{{ placeholders }}` and `<!-- if:key --> … <!-- else --> … <!-- end -->` blocks
(which can nest), copies the logo, the screenshots and the font from their real homes (the app,
`assets/`), and asks the GitHub API for the newest release, including the SHA-256 GitHub stores for
every file (`digest`). Download links and checksums therefore follow releases without editing the
pages. Without a release, the download section shows build instructions instead.

The download section is a picker: one tab per way to get it (Microsoft Store, Windows setup,
Arch, Debian, Fedora, Android), and only the picked one's download and instructions show. The tabs are
radio buttons styled in CSS, so the site still runs no JavaScript; a new tab needs its lines in the three
selector lists under "Download picker" in `site.css`. The Store link is `LAUNCHHEIM_STORE_URL` in
`build.py` (the publisher page is https://apps.microsoft.com/search/publisher?name=CodeIsNotEvil).

The screenshots on the front page are the ones made for the Store listing, kept in
`assets/screenshots/` as 1600 px WebP (about 400 KB for all five rather than
2.6 MB of PNG). For new ones:

```fish
magick 01-library.png -resize '1600x>' -strip -define webp:method=6 -quality 85 01-library.webp
```

The build fails when a rendered page links to an `#anchor` it doesn't have, or opens more `<section>`s
than it closes. Balanced `if`/`end` markers alone didn't catch a template that lost a stretch in the
middle (2026-10-01), and the download page went live mixing the two apps it showed then.

Deployments also carry LaunchHeim's Windows setups of the three newest releases, at
`downloads/launchheim/<version>/LaunchHeim-<version>-win-x64-setup.exe` (`--mirror-installers`, checked
against GitHub's SHA-256). The Microsoft Store takes the installer by URL and refuses one that redirects,
which GitHub's release links always do. After a release, enter the new version's URL in Partner
Center; the file at a versioned URL never changes.

`.github/workflows/site.yml` builds it on pull requests and deploys it from `main`, after every
release (the release workflow starts it, because releases made by a workflow trigger nothing on
their own) and on demand.
