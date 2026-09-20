# Onboarding media

The stills and clips that the setup wizard (dev-plan 10.2) and the welcome
tour (10.3) embed. They are recorded from the running app by the screenshot
harness, committed to `src/web/public/onboarding/`, and served from the SPA
build.

```sh
scripts/screenshots/onboarding.sh              # everything, both themes
scripts/screenshots/onboarding.sh editor-slash # re-record one
```

Credentials come from `.debug-credentials` at the repo root (gitignored),
which sets `SHOT_EMAIL` and `SHOT_PASSWORD`:

```sh
SHOT_EMAIL=you@example.com
SHOT_PASSWORD='…'
```

The account needs `spaces.create` and `spaces.delete`, because the spec
builds the space it films and deletes it again, and `dashboard.view` and
`backups.view` for the two admin stills. An administrator or the owner has
all four.

## The media

Every name below exists four times: `<name>.light.webm`, `<name>.dark.webm`
and a poster PNG beside each. The poster is the clip's final frame, captured
in the same run, so the two cannot drift apart. The stills at the bottom are
PNG only.

| File | Shows | Used by | Shot |
|---|---|---|---|
| `spaces` | The spaces list, opening a space, the page tree | 10.3 tour | `spaces` |
| `new-page` | New page, a title, typing a paragraph, Publish | 10.3 tour | `new-page` |
| `editor-slash` | Typing `/`, the menu filtering, inserting a table | 10.3 tour, tip | `editor-slash` |
| `editor-toolbar` | Selecting text, the bubble menu, a heading from the toolbar | 10.3 tour | `editor-toolbar` |
| `mention` | Typing `@`, picking a person | tip | `mention` |
| `inline-comment` | Selecting text, Comment, writing one | 10.3 tour, tip | `inline-comment` |
| `page-tree-drag` | Reorder mode, dragging a page to a new position, saving | tip | `page-tree-drag` |
| `search` | The search box, results, a snippet | 10.3 tour | `search` |
| `templates` | Save as template from the page menu | tip | `templates` |
| `link-shortcut` | Ctrl/Cmd+K on a selection | tip | `link-shortcut` |
| `watch` | Watch this page, then the bell | tip | `watch` |
| `profile` | Avatar, two-factor, notifications | 10.3 tour | `profile` |
| `admin-overview` | The Administration dashboard (still) | 10.2 Done screen | `admin-overview` |
| `admin-backups` | The Backups tab (still) | 10.2 Done screen | `admin-backups` |

## The demo space

The spec's `setup` shot creates `DEMO` ("Getting started") with three pages,
plus `DEMOENG` and `DEMOHB` so the spaces list has something plausible in it,
and `teardown` deletes all three. Both run on every invocation, so the clips
never depend on this instance's real content and cannot leak it. Deleting the
space needs the key typed back and the password in the same request (11.3),
which the `deleteSpace` step does with the harness's own credentials; the
password is never in the spec file and never on screen.

If a run dies between the two, `DEMO` is left behind and the next `setup`
fails loudly rather than filming a space full of the last run's leftovers.
`scripts/screenshots/onboarding.sh teardown` clears it.

**Every run raises two Critical `space.deleted` alerts**, one per theme.
That is correct: a space really was destroyed, and 11.3 says so whoever did
it. Recording the media is therefore something to do deliberately and then
resolve the alerts for, not something to leave on a loop.

## Keeping the instance out of the picture

These files ship inside the product, so nothing in them should come from the
instance that recorded them. Two places needed work for that, and both are
worth preserving if the spec is edited:

- **The spaces list shows every space you can see.** The `spaces` clip hides
  every card that is not one of the three demo spaces, through the shot's
  `css`, which is applied before the first held frame rather than as a step.
  Without it the clip opens on a list of somebody's real, private space
  names.
- **A mention picks a real person.** The `mention` clip types `@Demo` rather
  than a bare `@`, so it resolves to the recording account itself instead of
  whoever happens to sort first in the directory.

The search query is chosen to match only demo content, and the notification
bell belongs to the recording account. Check the posters after a re-record:
they are each clip's final frame, so a leak that crept in usually shows
there.

## Budget

`onboarding.sh` fails if any clip exceeds **600 KB** or the whole set exceeds
**8 MB**. These are fetched by people on their first visit, often before they
have decided they like the place.

The page renders at **1280×800**, the layout people actually use, and the
video is scaled down to **864×540** on the way out (`RECORD_VIDEO` in
`shot.mjs`). Scaling the video rather than shrinking the viewport keeps the
layout honest while paying for half the pixels. Posters are captured from
the same context at the full 1280×800, so the stills stay sharp. The whole
set is shot at 1× rather than the documentation harness's 2×
(`deviceScaleFactor` in the spec).

If the set is over budget, resolution is the lever with the most give:
every clip here is already inside the 6-to-10-second window, and shortening
them costs legibility. Motion is the other big cost. A smooth scroll makes
every frame different and is the most expensive thing a clip can contain,
which is why `profile` jumps between positions and holds instead.

## When a clip goes stale

A UI change dates a clip silently: nothing fails, the tour just shows
something that no longer exists. The table above names the shot for each
file so one can be re-recorded on its own. Re-record when you change the
editor toolbar, the slash menu, the page tree, the spaces list, the page
action bar, or the profile page.

## Writing a clip

Beyond the still harness's fields (see `scripts/screenshots/README.md`), a
recording adds:

| Field | Does |
|---|---|
| `record` | Makes this shot a clip. `{ "seconds": 8 }` documents the intent; the real length is the steps. |
| `lead` / `tail` | Held frames at each end, so a loop does not start or stop mid-motion. Default 700 / 900 ms. |
| `typeSlowly` | Types at a readable pace (default 55 ms per character) rather than instantly. |
| `moveTo` | Moves the pointer to an element over several steps, so hover states actually fire. A recording has no visible cursor, so movement reads through what lights up. |
| `dragTo` | `{ "from": …, "to": … }`, pressed and moved slowly enough to see the drop indicator. |
| `deleteSpace` | Teardown. Supplies the confirmation key and the harness's password itself. |
| `skipCapture` | Runs the steps and produces no file. `setup` and `teardown` use it. |

Annotate nothing: a circle drawn over a moving picture reads as part of the
UI. The clips rely on hover states and focus rings instead.
