# Site

The GitHub Pages site at https://codeisnotevil.github.io/MyMonoRepo/: a page promoting the apps
(`src/index.html`) and a download page with install guides (`src/download.html`), in the apps' own
look (Kode Mono, `#DE5833`, light and dark with the system). Plain HTML and CSS, no JavaScript and
nothing loaded from other sites.

```fish
site/build.py                                   # -> site/_site (git-ignored), then:
python3 -m http.server -d site/_site 8000       # open http://localhost:8000
site/build.py --releases-json sample.json       # preview the download page with made-up releases
```

`build.py` fills in `{{ placeholders }}` and `<!-- if:key --> … <!-- else --> … <!-- end -->` blocks
(which can nest), copies the logos and the font from their real homes (`Assets/`, the apps), and asks
the GitHub API for each app's newest release. Download links therefore follow releases without editing
the pages. Before an app's first release, its section shows build instructions instead.

`.github/workflows/site.yml` builds it on pull requests and deploys it from `main`, after every
release (the release workflows start it, because releases made by a workflow trigger nothing on
their own) and on demand.
