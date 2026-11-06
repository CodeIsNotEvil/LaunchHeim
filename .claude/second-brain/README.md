# Second Brain

A plain-Markdown knowledge base for LaunchHeim, shared by the owner and Claude Code, in the same
layout as the ones in MyMonoRepo (where it started), RcinelTranslate and RcinelLevel. It holds what
the code and git history *can't* tell you: why decisions were made, what's in flight, open questions,
gotchas found the hard way, and reference notes worth keeping.

It's Obsidian-compatible: open `.claude/second-brain/` as a vault and the `[[wikilinks]]` resolve.
Any editor works too, since it's only Markdown.

## Layout (PARA plus decisions)

| Folder | Holds | Example |
|---|---|---|
| `inbox/` | Quick, unsorted captures. Triage them into another folder later | a half-formed idea |
| `projects/` | Work with a goal and an end | [[launchheim]] |
| `areas/` | Ongoing responsibilities with no end date | |
| `resources/` | Reusable knowledge that isn't tied to one project | |
| `decisions/` | ADR-style records: context, decision, consequences. Never rewritten, only superseded | [[0018-launchheim-own-repository]] |
| `journal/` | Dated session logs (`YYYY-MM-DD.md`) | [[2026-11-06]] |
| `archive/` | Finished or abandoned things, kept for context | |
| `templates/` | Starting points for new notes | |

Start at [[index]], the map of content.

## Conventions

- **One idea per note.** File names are lowercase kebab-case, and decisions are numbered (`NNNN-slug.md`).
- **Frontmatter** on every note: `tags`, `created`, `updated`, and `status` (`active` | `done` | `superseded` | `idea`).
- **Link liberally** with `[[note-name]]`. A link to a note that doesn't exist yet is fine: it marks
  something worth writing.
- **Point, don't copy.** Link to repo files (`src/Core/...`) instead of pasting
  code, and don't duplicate `CLAUDE.md` or the READMEs. Notes cover the *why* and the *what next*.
- **Absolute dates only** (`2026-09-25`, never "last week").
- **Decisions are append-only.** To change one, write a new record and mark the old one `superseded`.
- **Say where copied knowledge came from.** Notes taken over from another of the owner's repositories
  name the source, so a later correction can be made in both places. Everything up to 2026-11-06
  came from MyMonoRepo ([[0018-launchheim-own-repository]]).

## How Claude uses it

- Read [[index]] and the relevant project note before non-trivial work.
- After a session that settled a design question or found a gotcha, add or update a note and append
  to that day's journal entry.
- Record a new architectural choice in `decisions/` from `templates/decision.md`.
- Keep the owner's and Claude's notes in the same place. There's no separate "AI notes" section.
