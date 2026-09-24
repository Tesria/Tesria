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

The spec's `setup` shot creates `TOUR` ("Getting started") with three pages,
plus `TOURENG` and `TOURHB` so the spaces list has something plausible in it,
and `teardown` deletes all three. (They were `DEMO`, `DEMOENG` and `DEMOHB`
until 2026-09-23, when `DEMO` became the Tesria Demo space's key: a failed
setup followed by the teardown would have deleted it. `setup` now carries
`abortOnFail`, so a failure ends the run before the teardown.) Both run on every invocation, so the clips
never depend on this instance's real content and cannot leak it. Deleting the
space needs the key typed back and the password in the same request (11.3),
which the `deleteSpace` step does with the harness's own credentials; the
password is never in the spec file and never on screen.

If a run dies between the two, `TOUR` is left behind and the next `setup`
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

## The setup wizard (dev-plan 10.2)

`/setup` is where a brand-new instance starts: the steps down the left, one
step's form on the right. It runs in two situations, both decided by
`SetupGate`:

- **No accounts at all.** Every visitor goes to `/setup`, because there is
  nothing else there yet. `/register` still works and still makes the first
  account the owner; the wizard is the friendlier door to the same room.
- **An owner who has not finished.** That owner is sent back to `/setup`
  until they do, and nobody else is: an administrator or a member who arrives
  meanwhile carries on as normal.

This is convenience, not enforcement. The API is not blocked while setup is
unfinished. A person who would rather use the API can, and the wizard will
notice what they did, because it checks the same evidence.

**Five steps are required** and have no Skip: the owner account, the instance
name and address, who can join, the rights matrix, and the backup retention
policy. Email, two-factor and a first space can wait. The server refuses to
record a required step as skipped, so the client cannot decide otherwise.

**Completion is checked against evidence, not clicks.** `POST
/api/setup/complete` looks at whether the owner acknowledged their recovery
codes, whether settings were actually written, whether the permissions matrix
was reviewed, and whether a backup policy was saved. Clicking through every
step without doing any of them returns 409 naming the earliest one
outstanding, and the wizard jumps there. An instance that predates the wizard
is stamped complete by `OwnerSeed` and never sees it.

### Re-running a step

There is no way back into the wizard once it is finished, and there does not
need to be: every step is a page in Administration.

| Step | Where it lives afterwards |
|---|---|
| Your account | Profile |
| This instance | Administration → Settings |
| Who can join | Administration → Settings |
| What roles may do | Administration → Roles |
| Backups | Administration → Backups |
| Email | Administration → Settings |
| Two-factor | Profile |
| A first space | Spaces → New space |

### Testing it

First-run behavior cannot be reached on an instance that already has
accounts, and faking it by editing the database is not worth the risk.
`scripts/scratch-instance.sh` brings up a throwaway Tesria on
`http://localhost:8099` under its own compose project and its own volumes,
so `down -v` there cannot touch anything real:

```sh
scripts/scratch-instance.sh up      # empty instance, needsOwner true
scripts/scratch-instance.sh reset   # destroy and start again
scripts/scratch-instance.sh down    # destroy it
```

Two things to know when driving it from a browser. **Cookies ignore the
port**, so `localhost:8099` shares a cookie jar with a real instance on
`localhost`; use `127.0.0.1:8099`, which is a different host as far as
cookies are concerned. And it runs over plain HTTP with
`Security__AllowInsecureCookies`, because a Secure cookie is silently dropped
over HTTP and every request would look signed out. That setting is documented
as unsafe and belongs nowhere else.

## The welcome tour (dev-plan 10.3)

`/welcome`, five screens, each a clip beside three sentences. A new account
is sent there once per session until it either finishes or leaves; leaving by
any route counts as skipping, including closing the tab, so nobody is asked
twice. The profile can reopen it.

| Screen | Clip | Covers |
|---|---|---|
| Spaces and pages | `spaces` | What a space is, that pages nest, where the tree is |
| Writing | `new-page` | New page, publish, drafts are private |
| Working together | `inline-comment` | Live editing, comments on a selection, @, Watch |
| Finding things | `search` | Search covers what you may see; labels |
| You | `profile` | Avatar, two-factor, email, where to turn tips off |

Accounts that existed before this shipped were marked as having skipped the
tour by the migration. Nobody is shown a tour of a product they already use.

## Tips

One at a time, **at most three a day**, each one only once, and never over a
dialog, inside the wizard, during the tour, while a menu is open, or while
the editor has a selection. A tip whose control is not on the page is passed
over rather than queued: the moment has gone, and it will come round again.

Counters (how many editing sessions, pages created, searches, visits to this
page) live in `localStorage` keyed by user id. They are deliberately not sent
to the server: losing them costs one repeated tip, and how often somebody
opens the editor is nobody's business. Dismissals do go to the server, since
they should hold across devices.

| Key | Where | Fires when |
|---|---|---|
| `slash-menu` | editor | the editor is open |
| `bubble-menu` | editor | more than three words are selected |
| `clear-formatting` | editor | text carrying formatting was pasted |
| `mention` | editor | the page has comments or other live editors |
| `link-shortcut` | editor | the second editing session |
| `page-tree-drag` | space | a space with three or more pages |
| `templates` | editor | the third page this person has created |
| `indent` | editor | a list of three or more items |
| `emoji` | editor | the tenth editing session |
| `inline-comment` | page | a page with no comments, second visit |
| `watch` | page | a page written by somebody else |
| `labels` | page | your own page with no labels |
| `full-width` | page | a page containing a table |
| `search-scope` | search | the second search |
| `two-factor` | profile | two-factor off, third profile visit |

Each tip has **Got it**, which retires it for good, and **Turn off tips**,
which stops all of them and offers ten seconds of undo. Profile → Tour and
tips has the same switch, **Show the tour again**, and **Reset dismissed
tips**.

### Adding one

`src/web/src/onboarding/tips.ts` is the catalog: a key, the context it
belongs to, a CSS anchor, the words, an optional clip, a priority, and a
predicate over `TipState`. If the predicate needs a fact nothing records yet,
add a signal in `onboarding/signals.ts` and call it where the thing actually
happens, rather than scraping it out of the DOM later.

Keep the count honest. Every tip added is one more interruption competing for
the same three-a-day budget, and the tip that gets shown is the one with the
lowest priority number.
