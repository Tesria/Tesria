# Changelog

All notable changes to Tesria are recorded here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Feature: appearance menu — theme popup + accent colours (2026-09-08)

The theme control is a popup now rather than a cycling button, with two
sections: **Theme** (System / Light / Dark, each with a one-line hint, System
showing what it currently resolves to) and **Accent colour** (blue, teal,
green, purple, orange, magenta).

System remains the default for new users — nothing is written to storage until
an explicit choice is made, and re-picking System clears the key rather than
pinning today's resolved value. Same for the accent: blue stores nothing.

**Each accent is defined twice, for light and dark, rather than derived from
one value.** A hue dark enough to pass 4.5:1 as link text on white is far too
dark to read on a dark ground, and the reverse. Both sets were measured against
WCAG AA — light values against `#ffffff`, dark values against `--bg` — and
`--on-primary` (text on a filled accent button) is chosen by computed contrast:
white in light mode, dark ink in dark. Green is the clearest illustration:
`#1a6c45` in light, `#4bce97` in dark.

A subtlety worth knowing if you add a seventh accent: `:root[data-accent="x"]`
and the dark base `:root:not([data-theme="light"])` have *identical*
specificity. Blocks are therefore emitted for every accent including the
default blue, so a higher-specificity dark block always exists to win — without
it, choosing an accent explicitly would drag the light palette into dark mode.

The picker's own swatches read themed `--accent-dot-*` tokens, so each dot
previews the colour that accent will actually produce right now, and the whole
row changes when the theme does.

The accent deliberately drives only the chrome. Panel colours are semantic
(a warning is yellow regardless), and table cell / highlight colours belong to
the document's author — neither follows the accent.

Known limitation: in light mode, orange is necessarily a deep rust (`#9a4d00`).
A brighter orange cannot reach 4.5:1 as link text on white, and `--primary` is
used for body-sized link text, so the accessible value is the one that ships.

### Feature: light/dark theme toggle (2026-09-08)

Three preferences, not two: **system** (the default, following the OS),
**light** and **dark**. A plain on/off switch loses "just follow my machine"
permanently the first time it is pressed, so the topbar control cycles
system → light → dark and shows the OS's current resolution while on system.

Mechanically it is the standard three-state pattern. `:root` carries the light
palette; `@media (prefers-color-scheme: dark)` applies the dark one *unless*
`[data-theme="light"]` is set; and a `:root[data-theme="dark"]` block lets an
explicit choice win in both directions — including dark-while-the-OS-is-light,
which the media query alone cannot express. `theme.ts` owns the attribute and
localStorage; `index.html` re-applies the stored value in an inline,
synchronous script before first paint, without which the page renders light
for one frame and then flips.

Getting there meant tokenising the stylesheet: every colour now resolves
through a custom property. `--surface` is new and carries the weight — it is
identical to `--bg` in light mode and deliberately lighter in dark, which is
what separates a card, the paper sheet or a popover from the page behind it.

Two things that needed more than a token swap:

- **Panel icons** were `background-image` data URIs with the stroke colour
  baked in, which would have meant carrying a second full set for dark mode.
  They are `mask-image` now: the SVG supplies the shape, `--panel-icon`
  supplies the colour, so one token per type re-tints all five.
- **Author-chosen colours** (a table cell's `backgroundColor`, a highlight
  mark's `color`) are stored *in the document* and are always light tints from
  `palette.ts`. A theme cannot restyle them without discarding the author's
  choice — but left alone in dark mode they are a light patch carrying light
  `--text`, i.e. invisible. Dark mode pins the ink dark on exactly those
  elements instead, so a coloured cell reads identically in both themes.
  Verified against the API space's status-code table, where tinted and
  untinted cells sit side by side in one row.

The code block is deliberately **not** themed — it stays dark in both, the way
most editors and docs sites treat code.

Known gap: the toggle lives in the authenticated topbar, so it is not reachable
from the sign-in and registration pages. The *theme* still applies there (the
pre-paint script is route-independent); only the control is missing.

### Feature: panels, colour palettes, and a toolbar alignment fix (2026-09-08)

Four editor gaps against Confluence, closed together.

**Panels** (`panelExtension.ts`) — coloured callouts, with `panelType` taken
from ADF's own set: info, note, warning, success, error. Confluence's legacy
Info/Tip/Note/Warning macros all map onto that set (the old Tip macro is
today's `success` panel), so all four names the request asked for have a home
without inventing a sixth type. Available from a toolbar picker and from the
slash menu, both generated from one exported `PANEL_TYPES`/`PANEL_LABELS` so
they can't drift. Colour and icon live in `index.css` keyed off the rendered
`data-panel-type`, which keeps the icon a `::before` pseudo-element rather
than a child node ProseMirror would fight over — and gets read-only rendering
the icon for free.

**Table cell / row / column backgrounds** (`TableCellMenu.tsx`) — Confluence's
per-cell chevron in the top-right of the cell holding the cursor, opening a
"Background colour" palette. Cursor-driven, so deliberately not sharing
`useHoveredTable` with the hover-driven row/column and width controls. The
Cell/Row/Column scope buttons widen the written rect via
`TableMap.cellsInRect()` and apply the whole scope in one transaction, rather
than replacing the user's selection with a `CellSelection` — the cursor stays
put after colouring a row.

**Highlight colours** — `Highlight` is now `multicolor`, and the toolbar
button is a palette instead of an on/off toggle. Highlights stored before
this have no `color` attr and still render as a plain `<mark>`.

Both palettes are Atlassian's own light/medium/bold values, matching the
fixed palette Confluence offers rather than a hex input, and are stored *in
the document* so they survive export. The export renderer now whitelists a
colour to plain hex before it reaches a `style` attribute — document JSON is
stored as given, so an unvalidated colour was a CSS-injection route into
exported HTML.

**Fix: the insert-image icon sat 4.8px above every other toolbar button.**
That button is a `<label>` (it wraps a hidden file input), so the global
`label { margin-bottom: 0.6rem }` applied to it and to nothing else in the
row. `.toolbar` centres its children with `align-items`, which centres each
item's *margin* box — so 9.6px of phantom margin below the label lifted its
border box by exactly half. Measured before and after against the real
stylesheet: 4.80px of spread, now 0.00px. Fixed with `margin: 0` on
`.toolbar__btn` rather than on the one label, so any element type used as a
toolbar button is immune.

Export coverage for all of it (panels in HTML and Markdown, cell backgrounds
on both cell kinds, highlight colour plus the legacy no-colour case, and the
hostile-colour rejection) is in `ProseMirrorRendererTests`.

### Fix: the full-width toggle did nothing on a brand-new page (2026-09-08)

`PUT /api/pages/{id}/layout` looked the page up through the default query
filter (`DeletedAt == null && Status != Draft`), so on an unpublished draft
it found nothing and returned 404. Every other draft-aware endpoint —
Publish, DeleteDraft, attachment upload — already opts out with
`IgnoreQueryFilters()`; SetLayout was the one that didn't.

The user-visible symptom was a toggle that flipped and immediately snapped
back: `PageEditor.toggleFullWidth` updates optimistically and rolls back on
error, and since layout is display metadata the rollback is silent. Full
width worked fine on any already-published page, which is why this survived
this long.

SetLayout now reads through `IgnoreQueryFilters()` with the soft-delete half
of the filter reapplied by hand, so a draft is reachable but a trashed page
still isn't. Publish never touched `FullWidth`, so the choice made while
composing now carries through to the published page unchanged. Covered by
`DraftPageTests.Full_width_can_be_toggled_on_a_draft_and_survives_publish`,
which asserts both halves (the 204 and the survival through publish).

### Design: page tree Reorder mode is now a batch edit (2026-08-19)

Feedback on Reorder mode: it committed each drag immediately and left
edit mode, which was fine for moving one page but tedious for reorganizing
several — reparenting a page is very often the first of several related
moves, and re-entering Reorder mode before each one added it up fast.

Discussed batch-editing (stay in Reorder mode, pile up changes, Save or
Cancel) against the existing immediate-commit-per-drag model before
building anything — batch editing means real complexity (a draft that
diverges from the server, conflict risk if the tree changes elsewhere
mid-session, partial-failure handling on save) that immediate-commit
doesn't have. Went with batch editing anyway, since it's what was asked
for.

Drags now apply to a local draft tree — `applyMove()` removes the dragged
page (with its subtree intact) and reinserts it under its new parent,
letting the existing `flatten()` recompute depths for the whole moved
subtree for free — and each drag also appends to an ordered
`pendingMoves` queue instead of calling the API. **Save** replays that
queue as sequential `PUT /api/pages/{id}/move` calls in the order the
moves were made; **Cancel** discards the draft without ever contacting
the server. Rows are no longer links while editing (a stray click could
otherwise navigate away and abandon an unsaved reorganization) — dragging
is the only thing a row does in Reorder mode now. See
docs/architecture.md's "Page tree drag-and-drop" section for why replaying
moves in original order is safe without diffing the draft against the
original tree.

### Feature: show/hide toggle on every password field (2026-08-03)

Added a `PasswordInput` component (`components/PasswordInput.tsx`) — a
password `<input>` with a flat eye/eye-off toggle button overlaid on the
right, same stroke-icon language as the rest of the app. Audited the whole
frontend for `type="password"` fields: there were exactly two, sign-in and
create-account, both now using it. No shared input component existed
before this, so `PasswordInput` is also where any future password field
(e.g. a change-password form) should start, rather than a bare
`<input type="password">`.

### Design: page tree Reorder toggle made icon-only (2026-08-03)

The "✏️ Reorder" button (see the previous entry) still read as heavier than
it needed to. Dropped the "Reorder" label — the pencil now stands alone —
and swapped the platform's own full-color pencil emoji for a flat
`currentColor` stroke icon (`PencilIcon` in `PageTree.tsx`), matching the
same icon language already used by the editor toolbar and the topbar bell
(`docs/CHANGELOG.md`'s notification-bell entry). The "✓ Done" label stays
as text once toggled on — a lone checkmark reads as ambiguous where "Done"
doesn't — so the button is icon-only at rest and label-plus-icon while
active, rather than jumping between two different visual languages.

### Fix: page tree dragging gated behind a "Reorder" mode (2026-08-03)

Reported after the drop-indicator redesign: on mobile it was too easy to
reorder a page by accident. Root cause was that every row was a drag
source all the time, and `touch-action: none` on the row (needed so a
touch-drag isn't raced by the browser's own scroll gesture) meant an
ordinary swipe-to-scroll starting on a page title got captured as a drag
instead — the exact ambiguity that makes "ends up moved when you didn't
mean to" so easy.

Added a compact `✏️ Reorder` / `✓ Done` toggle next to the tree's "📑 Pages"
heading (desktop sidebar and mobile alike, kept deliberately small per
request). Outside Reorder mode, rows are plain links with no dnd-kit hooks
and no `touch-action` override — scrolling through the tree behaves like
scrolling anything else, and there is no way to start a drag by accident
because nothing is listening for one. Reorder mode renders the draggable
version from the previous entry unchanged. The two tree instances (desktop
sidebar, mobile `SpaceHome` inline copy) hold this state independently,
which needs no special handling — they're never both visible at once.

### Design: page tree drag handle removed, real drop-indicator line added (2026-08-03)

Feedback on the initial drag-and-drop tree: the always-visible grip-icon
handle was "ugly" and ate row space, and Confluence's own tree shows a
horizontal line (with an indent preview) for where a drag would land,
instead of live-shuffling the rest of the list. Checked Atlassian's own
drag-and-drop design guidelines and a real Confluence sidebar recording
before redesigning
([atlassian.design/components/pragmatic-drag-and-drop/design-guidelines](https://atlassian.design/components/pragmatic-drag-and-drop/design-guidelines)).

Removed the separate handle entirely — the row (title) is now the drag
source itself, same as Confluence's own "implied draggable" sidebar rows;
dnd-kit's `distance: 4` activation constraint is what tells a tap-to-navigate
from a drag, so plain clicks still work. Replaced the live-reordering
sortable-list behavior with a static list plus a drop-indicator line (2px,
8px circular terminal bleeding 4px past its own left edge) rendered in the
gap where the row would land, whose horizontal offset also conveys the
target nesting depth — matching Atlassian's own drop-indicator spec.

This also fixed a real regression reported separately: mobile had gone back
to horizontal-scrolling on an iPhone. Root cause was the handle itself — a
fixed-width button nested in a new inner flex row per tree item, which was
enough to break the mobile-safe flex-shrink behavior this app had already
been bitten by once before (see the topbar/`.brand` fix earlier in this
changelog). Removing the wrapper and the handle brought the DOM back down
to one link per row — closer to the pre-drag-and-drop structure — which
resolved it; verified at both 375px and 320px viewports with long,
deeply-nested titles, with no horizontal overflow.

### Feature: drag-and-drop page tree reordering and reparenting (2026-08-03)

The page tree — desktop sidebar and the mobile `SpaceHome` inline copy alike
— now supports Confluence-style drag-and-drop: drag a row by its handle to
reorder it among siblings, or drag it horizontally over another row to
reparent it at a new nesting depth. This is now the only way to change a
page's place in the hierarchy; there's no separate move dialog.

Backend: `PUT /api/pages/{id}/move` changed from a raw `Position` int (which
the caller had to compute exactly, with no protection against colliding
with or leaving a gap relative to other siblings) to an `Index` — a slot
among the destination's current siblings — with the endpoint itself
resolving that sibling group and renumbering it densely. Added tests for
sibling reordering, reparenting, cross-space rejection, and the edit-rights
check (`Move_reorders_siblings_by_index`,
`Move_reparents_a_page_and_appends_to_the_new_siblings_by_default`,
`Move_rejects_a_different_space`,
`Move_requires_edit_rights_on_both_the_page_and_the_destination_parent`),
alongside the existing cycle-rejection test.

Frontend: added `@dnd-kit/core` + `@dnd-kit/sortable` (native touch support
via Pointer Events, so the same `PageTree`/`TreeItem` code drives both the
mouse-driven desktop tree and the touch-driven mobile one). A dragged row's
own descendants are excluded from the working list during the drag, so a
subtree can't be dropped inside itself client-side; the backend's existing
cycle check remains the authoritative guard. See
`docs/architecture.md`'s new "Page tree drag-and-drop" section for the
depth-projection algorithm.

### Fix: search couldn't find slash-joined words like "Hocuspocus/Yjs" or "OIDC/SSO" (2026-07-31)

Found while dogfooding the App Design space: searching "Hocuspocus" or "OIDC"
returned nothing even though both words were right there in page content
("Hocuspocus/Yjs sidecar", "OIDC/SSO — optional"). Root cause: Postgres's
`to_tsvector('english', ...)` parses `word/word` as a single compound
lexeme (`'hocuspocus/yjs'`) instead of splitting it, so only the exact
compound — never either half alone — was searchable. Fixed by normalizing
slashes to spaces in `SearchText` before it's indexed
(`PageEndpoints.BuildSearchText`), so `to_tsvector` tokenizes both halves
normally; backfilled the 8 existing pages whose indexed text contained a
slash. Added a regression test (`SearchTests.Slash_joined_words_are_indexed_as_separate_terms`)
that asserts directly on the stored `SearchText`, since the SQLite test
provider's plain-`LIKE` fallback can't reproduce a tsvector-specific bug.

### Design: replace the notification bell emoji with a flat stroke icon (2026-07-30)

The topbar bell used the platform's own 🔔 emoji — rendered in full color
(yellow) by the OS/browser, the one spot of color in an otherwise flat,
monochrome icon set (the editor toolbar's custom SVGs, `editor/icons.tsx`),
so it stood out against everything around it. Replaced with a small inline
SVG bell in the same visual language as those toolbar icons (24x24 viewBox,
1.8px stroke, `currentColor`, round caps) — `var(--muted)` by default,
darkening on hover, same treatment as `.toolbar__btn`. Not added to
`editor/icons.tsx` itself since that module is explicitly scoped to the
editor toolbar's consumers; defined locally in `NotificationBell.tsx`
instead, being the only place it's used.

### Design: drop the space bar when viewing a page on mobile — the breadcrumb is the title now (2026-07-30)

`.space-actionbar` (space name / + New / ⋮) stayed visible even once you'd
navigated into an actual page, stacked right above that page's own
breadcrumb and Edit/⋮ row — a second, redundant header once you're that far
in. On mobile, viewing or editing a page now hides it entirely
(`.space-actionbar--hidden-on-page`, gated to `--bp-mobile` — desktop is
unaffected, that bar is already `display: none` there regardless); the
breadcrumb (already there) becomes the de facto title, immediately followed
by `PageView`'s Edit/+New/⋮ row.

`+ New` moves into that row, right after Edit — both `.btn--primary` now.
Mobile-only (`.page-actionbar__new-subpage`): desktop already has "+ New
page" permanently in the sidebar, so showing it a second time next to Edit
would just be noise there.

Considered folding Permissions/Webhooks/Trash into that page's `⋮` (as
Page/Space sections) so they'd stay reachable without the now-hidden space
bar. Went the other way — dropped them from the page menu entirely. They're
rare, admin-level actions; the page menu (Export, Watch, Save as template,
Delete) is opened far more often, and mixing an admin section into it adds
noise to the common case for the sake of an uncommon one. They're still one
tap further away (breadcrumb → space home → ⋮), which is a fair cost for
something used rarely — and matches real Confluence, which keeps space
administration in space-level screens rather than on every page's menu.

### Design: tree heading, mobile link color, and Edit promoted to a primary CTA (2026-07-30)

Polish pass on the space nav redesign above:

- The page tree (`PageTree.tsx`, shared by the desktop sidebar and the
  mobile inline copy on the space landing page) had no label at all — just
  a bare list, easy to lose track of what you're looking at. Added a
  `📑 Pages` heading above it in both places, since both render the same
  component.
- On mobile, tree links used the same near-black `.tree__link` color as
  desktop, but the two contexts mean different things: on mobile the tree
  only ever appears on the space landing page, so every link is purely "tap
  to go there" — same as any other link, and should read as blue. On
  desktop the tree stays visible after you've navigated into a page, so
  black-by-default-with-blue-when-active means "here's where you are,"
  not "here's what's clickable" — changing that would lose information,
  not add clarity. Scoped with `.space-home-tree .tree__link` rather than
  a prop, since the mobile/desktop distinction is already which wrapper
  renders it, not anything about the data.
- `PageView`'s Edit button was the only left-anchored control in a bar
  where everything else sits on the right, and its `.btn--ghost` styling
  made it recede next to actual secondary actions (Full width, ⋮) despite
  being the single most common thing to do with a page. Moved it into the
  right-anchored group as the first (leftmost) button there, and restyled
  it `.btn--primary` (the same blue as "+ New page"/"Post") — a real call
  to action instead of a ghost button no more prominent than "Full width."
  `.page-actionbar__secondary` gained `margin-left: auto` to anchor right
  correctly now that `PageView` has nothing left of it (a no-op for
  `PageEditor`, which still has its Toolbar in `__primary`).

### Design: real breadcrumb trail, one contextual create button, and a second sticky-bar collision fixed (2026-07-30)

Third round of feedback on the same-day space nav redesign: the single-level
"space name" link wasn't a breadcrumb at all once a page had its own
subpages, and a second sticky bar (`PageView`'s Edit/+Subpage row) was
fighting `.space-actionbar` for the same sticky slot while scrolling on
mobile — one visibly sliding over the other.

- **Real breadcrumb.** New `SpaceBreadcrumb.tsx`, rendered once in
  `SpacePage.tsx` right below `.space-actionbar` (non-sticky — the first
  thing in the normal scrolling content, on both mobile and desktop). Walks
  the already-loaded page tree (new `findTreePath` in `PageTree.tsx`) to
  build the full ancestor chain — `Space Name / Parent / Current Page`, only
  the current page non-clickable — rather than a single link back to the
  space. Falls back to a route label (`Permissions`/`Webhooks`/`Trash`/`New
  page`) on non-page routes; renders nothing on the space landing page
  itself, which already says where you are via its own heading.
- **One create button, not two.** Confluence's actual behavior: create from
  an open page makes a subpage of it; create from anywhere else makes a
  top-level page. `+ Subpage` (`PageView.tsx`) is gone — `.space-actionbar`'s
  `+ New` (mobile) and the sidebar's `+ New page` (desktop) now both compute
  the same contextual href in `SpacePage.tsx` (`?parent={currentPageId}` when
  viewing/editing an existing page, none otherwise), so both places behave
  identically instead of two different buttons doing two different things.
- **The second sticky-bar collision**: `.page-actionbar` (Edit/+Subpage/
  fullwidth-toggle/export, shared by `PageView.tsx` and `PageEditor.tsx`) was
  sticky at the same `top: 52px` as `.space-actionbar` — both mobile-only,
  both fighting for the same slot. Rather than hand-computing a second
  stacked offset (fragile: it'd need `.space-actionbar`'s exact rendered
  height kept in sync), `.page-actionbar` is simply `position: static` under
  `--bp-mobile` now — one sticky bar below the app topbar at this width,
  full stop. Unchanged on desktop, where `.space-actionbar` is hidden and
  there's nothing for it to collide with.
- Dropped the now-redundant " — {space name}" from the Permissions/Webhooks
  page headings and the "Space: {name}" footer on `PageView` — between the
  action bar and the new breadcrumb, the space name was appearing a third
  time on these pages.

### Design: follow-up pass on the space nav redesign — no icon, inline tree, breadcrumb, and a scroll-restoration fix (2026-07-30)

Three refinements to the same-day space-nav redesign below, from a second
round of real-device feedback:

- Dropped the 📄 emoji from the mobile action bar's space-name segment —
  just the title now, per feedback that page/space titles shouldn't carry
  a decorative icon (a user who wants one can put an emoji in the title
  itself).
- The mobile "open the page tree" toggle wasn't discoverable as a toggle at
  all — nothing about "📄 space name" read as "tap to see your pages."
  Replaced it with two things: `SpaceHome` (the space landing page) now
  renders the page tree **inline in the body** on mobile (new
  `PageTree` component, extracted from what was inline `TreeItem` code in
  `SpacePage.tsx`, shared with the desktop sidebar), so pages are visible
  the moment you land, no toggle to find. Every other space route now shows
  the space name in `.space-actionbar` as a plain breadcrumb link back to
  that landing page instead. Net effect: the mobile off-canvas drawer,
  `sidebarOpen` state, and backdrop are gone entirely — `.sidebar` is just
  `display: none` under `--bp-mobile` now, full stop.
- **The actual bug behind "the bar disappears until I scroll up when
  switching spaces"**: `<BrowserRouter>` (as opposed to the data-router
  APIs, `createBrowserRouter` + `<ScrollRestoration>`) never resets scroll
  position on navigation — the browser keeps whatever `scrollY` the
  previous page had. Landing on a shorter page already scrolled past its
  own height hides everything, sticky topbar included, since there's
  nothing left to stick to below the fold; you only see it again once you
  scroll back up into the new page's actual content. Not reproducible
  in-browser at this desk (this environment's Chromium happens to reset
  scroll on pushState on its own), but real Mobile Safari does not, and the
  symptom otherwise matches exactly. Fixed with a small `ScrollToTop`
  component (`useLocation` + `window.scrollTo(0, 0)` on every `pathname`
  change), mounted once at the router root in `main.tsx` — the standard
  fix for this well-known gap when not using a data router.

### Fix: shared topbar overflowed horizontally on real phones; redesigned space nav to drop the double-hamburger (2026-07-30)

Found via testing on a real iPhone (not the simulator) over LAN — Spaces,
Groups, Audit, API Tokens, Permissions, Webhooks, and Trash all failed to
fit at mobile widths: Sign out was clipped and the page scrolled
horizontally. Not reproducible in-browser at the same viewport width, which
pointed at a font-metrics difference rather than a layout bug per page.

- **Root cause**: `.topbar`'s `.brand` ("Tesria") is a flex item
  with no `min-width` override. Flex items default to `min-width: auto` —
  they refuse to shrink below their own text's intrinsic width no matter
  what `flex-shrink` says, a common flexbox trap. With no wrap fallback on
  `.topbar`, that one unshrinkable node was pushing the whole bar (shared by
  every page) past the viewport. The margin was only ~16-20px, thin enough
  that real iOS Safari's `-apple-system` Bold rendering (measurably wider
  per character than the sans-serif fallback available in-browser here)
  tips it over while desktop testing doesn't. Fixed: `.brand` now shrinks
  and ellipsizes instead of refusing to; `.topbar__right` and
  `.topbar__hamburger` got `flex-shrink: 0` so the controls themselves never
  get squeezed. Stress-tested down to a 240px viewport with zero overflow.
- Added the missing `-webkit-text-size-adjust: 100%` reset — iOS Safari
  auto-inflates text size in narrow columns it judges "readable"; standard
  practice, simply absent before.
- `.version__num`/`.version__comment` (Groups/Audit/API Tokens/Permissions/
  Webhooks/Trash list rows) gained `overflow-wrap: anywhere` defensively —
  `.version`'s `flex-wrap` only breaks between list items, not within one
  item's own unbroken text (a long group/page name, raw audit metadata).

### Design: single hamburger for space navigation, replacing a hidden double-menu (2026-07-30)

`SpacePage.tsx` had its own mobile hamburger (page tree + New page +
Permissions/Webhooks/Trash, all in one off-canvas drawer) stacked directly
under the app-level hamburger (`Layout.tsx`: Spaces/Groups/Audit/API
Tokens) — two unrelated "☰" affordances on screen at once, and every space
action except Watch buried a tap deeper than necessary.

Replaced the space-level hamburger with an always-visible mobile action bar
(`.space-actionbar`, sticky under the topbar): a `📄 {space name}` button
that still opens the page-tree drawer (now holding *only* the tree), plus
an inline `+ New` button and a `⋮` overflow menu (reusing the existing
`OverflowMenu` component from `PageView.tsx`) for Permissions/Webhooks/
Trash. Only one hamburger exists anywhere in the app now. The desktop
sidebar (always visible, unaffected by any of this) keeps its own copies of
these links — new `.sidebar__quicklink` marker class hides just the
mobile-drawer duplicates so they're not offered in two places on a phone.

### Design: collapse editor toolbar's Heading/List/Alignment groups into dropdowns on mobile (2026-07-26)

The mobile toolbar (see the icon redesign entry below) still wrapped to 3
rows at 402pt — better than before, but still a lot of chrome above the
actual writing area. Grouped the three runs of related buttons users don't
need to see all at once — Heading (H1/H2/H3), list type (bullet/ordered/
task), and alignment (left/center/right) — into a single dropdown trigger
each, collapsing the toolbar to ~2 rows on mobile.

- Added `src/web/src/editor/ToolbarDropdown.tsx`: a trigger button (current
  selection's icon + a small caret) that reveals a vertical menu of the
  full option set on click, dismissed via the existing `useDismissable`
  hook (same outside-click/Escape pattern as `OverflowMenu`).
- Desktop keeps the flat button rows — both forms are always mounted
  (`.toolbar__flat` / `.toolbar-dropdown`), and CSS picks one via
  `display: none` at `--bp-mobile`, following this codebase's established
  CSS-only responsive convention (no JS viewport check) rather than
  introducing one.
- The dropdown menu flips from left- to right-anchored when the trigger
  sits too far right for a left-aligned menu to fit — found by testing:
  the Heading/Alignment triggers land near the toolbar's right edge on
  mobile, and a naive `left: 0` pushed the menu off-screen.

### Fix: stale `index.html` served indefinitely due to missing Cache-Control (2026-07-26)

Flagged but not fixed in an earlier pass (see the 320px sweep entry below) —
turned out to be actively biting: the iOS Simulator kept rendering the
*pre-icon-redesign* toolbar (plain text labels, "iOS blue") minutes after
the fix had shipped and the container had restarted, because the browser's
heuristic cache never re-checked `index.html`. Root-caused and fixed for
real this time: `Program.cs`'s `UseStaticFiles`/`MapFallbackToFile` now set
`Cache-Control: no-cache` on `index.html` specifically and
`public, max-age=31536000, immutable` on everything else (the
content-hashed `/assets/*` bundles, safe to cache forever since a content
change gives them a new filename). Verified via `curl -I` against the
rebuilt container.

### Fix: three real overflow/positioning bugs found in a full-route mobile audit (2026-07-26)

The user reported the login page and space-home view still looked broken
on mobile despite the earlier "audit all pages at 320px" pass — turned out
to be two separate things: (1) the Cache-Control bug above, serving a
stale pre-fix build, and (2) three genuine bugs a route-by-route sweep with
a scripted `scrollWidth > clientWidth` check (not eyeballing) turned up,
none of which the earlier pass had covered:

- **Notification bell dropdown opened mostly off the left edge of the
  screen.** `.notif__dropdown`'s `right: 0` was anchored to `.notif` — a
  wrapper sized to just the ~28px bell button — not to the actual
  right-hand button cluster (bell + username + sign-out) it visually sits
  in. Once the dropdown (288px wide) tried to right-align against a box
  that small, it necessarily spilled left past the viewport. Fixed by
  moving `position: relative` from `.notif` to `.topbar__right` (the whole
  cluster), so `right: 0` resolves against the cluster's actual right edge
  instead.
- **A brand-new, never-touched page showed the floating selection toolbar
  even with no text selected**, overlapping the title field and forcing
  ~14px of horizontal overflow. `SelectionBubbleMenu`'s `shouldShow` is
  only re-evaluated on `selectionUpdate`/`focus`/`blur`; an empty document
  that's never been focused never fires any of those, so the menu's
  floating-ui "open" state was stuck at its default (visible) instead of
  ever being told to hide. Fixed by adding an explicit `editor.isFocused`
  guard to `shouldShow` — semantically correct regardless of the root
  cause (a selection menu shouldn't show without focus) and immediately
  false right after mount, before any focus event.
- **The toolbar's link/comment popovers and the selection bubble menu
  itself could spill past either viewport edge on narrow screens.** Their
  anchor button lives inside a flex-wrap toolbar, so it can land anywhere
  horizontally, unlike a normal trailing "overflow menu" that's always at
  a row's end. Added `src/web/src/hooks/useEdgeAlign.ts`: measures the
  popover's actual rendered position via `useLayoutEffect` once it opens
  and applies a corrective inline `left` offset, clamping both edges to a
  16px margin. Also gave `.toolbar--bubble` its own `max-width` so it
  genuinely wraps on narrow screens — its floating-ui-managed wrapper sizes
  itself to the menu's *unwrapped* natural width and never shrinks, which
  otherwise defeats `flex-wrap` (the flex container is never actually
  narrower than its content). One residual, accepted gap: the bubble menu
  itself doesn't self-correct its own floating-ui-assigned position after
  the width cap makes it narrower (only the popovers nested inside it do) —
  an early attempt to add that via a transform + `useLayoutEffect` created
  a feedback loop with floating-ui's own `autoUpdate` repositioning and
  hard-crashed the whole app (blank root, no console error) the moment any
  text was ever selected. Reverted; the width cap alone shrinks the
  overflow from ~55px to a single-digit-pixels edge case, worth trading
  for stability. All four `document.documentElement.clientWidth`-based
  measurements (not `window.innerWidth`) — an early version used
  `innerWidth` and it read back an inflated value once something on the
  page was *already* overflowing, undercorrecting the very shift meant to
  fix that overflow.
- Also defensively hardened `.attachment` (shared by `AttachmentsPanel` and
  `GroupsPage`'s member list — no attachments/members long enough to
  reproduce it existed to test against, but the CSS gap was real): added
  `flex-wrap` and `overflow-wrap: anywhere` so a long filename or email
  can't force the row wider than the viewport.
- Verified via a full route-by-route sweep at 320px (login, register,
  spaces list, space home, new-page editor, trash, permissions, webhooks,
  page view/edit, all its tabs, search, audit, groups, api-tokens, plus the
  notification dropdown and every toolbar dropdown) with the scripted
  overflow check — all clean.

### Fix: `.row-between` header rows overflowed the viewport at mobile widths (2026-07-26)

Missed by the mobile/responsive overhaul below — found on the iOS Simulator
(iPhone 17 Pro, 402pt) navigating to a space with no page selected (the
"Select a page from the tree, or create a new one." empty state, e.g.
`SpaceHome.tsx`). `.row-between` (title + action button, shared by the space
header, the spaces-list header, and the history-preview header) neither
wraps nor lets its children shrink, so a long-enough title/button pair
forces the row — and the whole `.page-wrap`/topbar above it — wider than the
viewport, producing horizontal scroll and edge-clipped text. Fixed with a
`--bp-mobile` override adding `flex-wrap: wrap`, stacking the button below
the title when they don't both fit. Verified no `document.documentElement`
horizontal overflow at 320/375/402px.

### Design: touch-friendly icon toolbar, replacing text-label buttons (2026-07-26)

The editor toolbar (both the sticky top toolbar and the floating selection
bubble menu) used text-label buttons (`Highlight`, `Table`, `Link`, arrow
glyphs for alignment) with no explicit color — they inherited the browser's
default anchor-like blue, which read as "iOS blue links" rather than
deliberate UI, and had small, cramped hit targets (padding-only sizing, no
minimum touch target).

- Added `src/web/src/editor/icons.tsx`: a small set of custom 24x24 SVG
  icons (stroke-based, `currentColor`, consistent 1.8px stroke/round caps)
  for inline code, highlight (an actual highlighter-pen shape, not the
  word "Highlight"), bullet/ordered/task list, blockquote, code block,
  table, image, text alignment (left/center/right), link, and comment.
  Bold/Italic/Underline/Strikethrough deliberately keep their literal
  B/I/U/S glyph treatment — that's the actual standard for those four
  (Google Docs, Word, Notion), not a placeholder.
- `Toolbar.tsx` and `SelectionBubbleMenu.tsx` now render these icons
  instead of text/glyph labels, so the sticky toolbar and the floating
  bubble menu present the same visual language.
- `.toolbar__btn` in `index.css` now has an explicit `min-width`/
  `min-height: 36px` (was padding-driven, effectively ~28px), and a global
  `button { font: inherit; color: inherit; }` reset — the real root cause
  of the "blue" look was that no button anywhere set its own `color`, so
  unstyled buttons fell back to the browser/OS default link-blue tint.
  Buttons now default to `var(--muted)` and use the existing
  `.toolbar__btn.is-active` blue tint only when a mark/block is actually
  active, matching how the rest of the app already uses that color.
- Verified: computed button size is 36x36px for every toolbar and bubble
  menu button (including the image-upload `<label>`) at both desktop and
  mobile widths; confirmed on the iOS Simulator (iPhone 17 Pro) that the
  toolbar wraps to multiple rows at 402pt width and a real touch tap
  reliably lands on the intended button without mis-hitting its neighbors.

### Fix: two real mobile layout bugs the 320px width class of devices hit (2026-07-26)

Found via a full page-by-page sweep at 320px (iPhone SE-class width — the
earlier responsive pass had mostly been checked at 375px+, which happened to
mask both of these):

- **Login/Register pages rendered edge-to-edge with no margin, clipped on
  the right.** `.center` used `display: grid; place-items: center` to
  center the auth card. A CSS Grid item's percentage sizing (the card's
  `max-width: 100%`, meant to let it shrink on narrow screens) resolves
  against its own **auto-sized grid track** — which itself sizes to the
  item's intrinsic width. That's a circular reference: the track becomes as
  wide as the 340px card wants, so `max-width: 100%` of a 340px track is
  still 340px, never actually constraining anything. It happened to look
  fine at 375px+ purely because 340px + padding was still narrower than the
  viewport there. Fixed by switching to `display: flex` — flex containers
  resolve child percentages against the actual content box, not an
  auto-sized track, so the same `max-width: 100%` now works as intended.
- **Groups/API Tokens/Webhooks/Spaces-creation forms were unreadable** —
  `.form-inline`'s 4-column grid (`120px 1fr 1fr auto`) has no room at
  narrow widths; fields and the submit button overlapped/clipped. Fixed
  with a `--bp-mobile` override stacking it to a single column.

Also worth knowing about but **not** fixed in this pass, found in passing:
`index.html` has no `Cache-Control` header, so browsers apply heuristic
caching to it — after a deploy, a client can keep using a stale
`index.html` (with old content-hashed asset URLs) until that heuristic
expires. Worth an explicit `Cache-Control: no-cache` on `index.html`
specifically (content-hashed assets under `/assets/` can stay
long-lived/immutable) in a future pass.

### Fix: `/ca.crt` over plain HTTP redirected instead of serving the file, for {$DOMAIN} specifically (2026-07-26)

Found via iOS Simulator testing (installing the CA into the simulator's trust
store needs to fetch it first) — `http://localhost/ca.crt` 308-redirected to
HTTPS instead of serving the cert, even though the Caddyfile clearly showed
the right route and a full container recreation didn't help. Root cause:
Caddy's automatic HTTPS inserts its own HTTP→HTTPS redirect for whatever
hostname it manages certs for ({$DOMAIN}) — and that auto-inserted route
wins over routes in our own `:80` block for that specific hostname,
regardless of the more specific `/ca.crt` exception there. Confirmed by
testing the same request with a different `Host` header, which reached our
route fine — only requests for {$DOMAIN} itself were intercepted first.
Fixed with `auto_https disable_redirects` in the global options block: we
already redirect everything else ourselves in `:80`, so Caddy doesn't need
to add its own (cert automation for {$DOMAIN} is unaffected, only the
redirect route). Regression-tested: plain HTTP still redirects to HTTPS for
every other path, and LAN/mDNS access is unaffected.

### Mobile/responsive overhaul + per-table width (2026-07-25)

The app had zero responsive CSS before this — a fixed 260px sidebar, a
fixed-width page card, and hover-only editor chrome that doesn't exist on
touch. Alongside it, tables gained real Confluence-style width control:
previously only column-border dragging (stock prosemirror-tables) existed,
with no way to make a table itself wider than the page, or size it to a
specific width independent of the page's own full-width setting.

Added:
- **Responsive breakpoints** (`--bp-mobile: 640px`, `--bp-tablet: 1024px`,
  documented in `index.css`'s `:root`). Below `--bp-mobile`: the topbar's nav
  links collapse behind a hamburger dropdown; the space sidebar becomes an
  off-canvas drawer (hamburger toggle + backdrop, dismiss-on-outside-click/
  Escape via a new shared `useDismissable` hook, also now used by
  `OverflowMenu`); the page/editor "paper" card tightens its padding and the
  full-width toggle button hides (full vs. normal width is a distinction
  without a difference once the reading column already fills the viewport).
- **Per-table width**, matching real Confluence's model (researched against
  the live product): a `width` (px, from a new edge-drag handle) and
  `layout: 'default' | 'full-width'` (from a new toggle button) attribute on
  the `table` node, independent of the page's own full-width setting and of
  column-border dragging (unaffected). A full-width table bleeds out of the
  page's own padding via `TableWidthControls.tsx`, a floating overlay
  alongside the existing `TableControls.tsx` (same convention, not a
  NodeView — see architecture.md). Export renderer parity in
  `ProseMirrorRenderer.cs` (HTML gets the inline style; Markdown degrades
  silently, same as other display-only attrs).
- **Touch-adaptive editor chrome**: table controls (row/column insert-delete,
  width/full-width) now reveal via tap-to-place-cursor instead of
  hover-proximity on touch/no-hover input (`useHoveredTable.ts`, shared by
  both components), detected via `matchMedia('(hover: none) and
  (pointer: coarse)')` rather than viewport width — a touchscreen laptop at
  desktop width has the same no-hover problem a phone does. (Selection-driven
  UI — the slash command, selection bubble menu, and image hover menu despite
  its name — already worked on touch with no changes needed.)
- Default page reading width bumped 860px → 900px (round number, evokes a
  sheet of paper, requested alongside this work).
- Fixed-width UI sweep: notification dropdown, overflow-menu dropdown, and
  the link-editor URL field now clamp to the viewport instead of overflowing
  it; the page tabs row (Comments/Attachments/History/Restrictions) scrolls
  within itself instead of pushing the whole page wider (found via testing —
  a real ~40px page-level overflow existed on every page with this tab row,
  independent of anything table-related).

Fixed (found via testing, not pre-existing per se — introduced and caught in
the same pass):
- prosemirror-tables' `TableView` (active whenever a table is `resizable`,
  in both edit and read-only rendering) only applies a node's rendered
  `style`/`data-*` attributes once, in its constructor — its own `update()`
  (used for every subsequent attribute change on an already-mounted table,
  e.g. toggling full-width live) recalculates the colgroup but never
  re-touches them. A plain `renderHTML`-based approach alone isn't enough to
  keep the DOM in sync live; `TableWidthControls.tsx` also applies the same
  effect directly to the DOM right after the transaction commits. (Fresh
  mounts — a page load, an export — are unaffected and already correct via
  the schema alone.)
- The full-width breakout math reads a `--page-pad` custom property shared
  with `.paper`'s own padding — they'd briefly drifted apart during this
  work (mobile tightened `.paper`'s padding via a separate hardcoded value
  instead of the same variable), causing a small but real viewport overflow.
  Fixed by having `.paper` derive its padding from `--page-pad` too, and
  overriding the variable itself at the mobile breakpoint rather than
  hardcoding parallel values — keeps them impossible to drift apart again.

Known gaps, not addressed in this pass:
- The touch-reveal path (tap-to-show table controls) is verified correct by
  direct testing of its resolution logic; a live end-to-end confirmation on
  a real touch device wasn't completed (the iOS Simulator was unavailable —
  crash-looping — for the rest of this session).
- Several admin/settings pages (Groups' create-group form, likely Webhooks/
  API Tokens/Permissions too) use fixed-width multi-column form layouts not
  covered by this pass — found in passing, out of scope here.
- The topbar's nav links wrap awkwardly at exactly ~768px (between the two
  breakpoints) — cosmetic, no overflow, not fixed in this pass.

### LAN/mobile HTTPS access + local CA trust scripts (2026-07-25)

Added, while setting up the Mac dev environment and looking ahead to open-
sourcing this project — a deployment with no real domain (the common case
for individuals/small teams evaluating it) previously only worked over
`https://localhost`; anything else (LAN IP, another local hostname) failed
the TLS handshake outright, since Caddy only had a certificate for the one
configured `DOMAIN`.

- **`deploy/Caddyfile`**: added a catch-all `:443` block using Caddy's
  On-Demand TLS with its internal CA, so any address the server answers on
  (LAN IP, `.local` hostname, `127.0.0.1`, ...) gets a certificate minted on
  first request — no need to enumerate hostnames, and it keeps working
  through DHCP IP changes. The original `{$DOMAIN}` block is untouched, so
  real-domain Let's Encrypt deployments are unaffected.
- **`/ca.crt` route** (both plain HTTP and HTTPS): serves the internal CA's
  public root certificate, so a device that hasn't trusted anything yet can
  still fetch it.
- **`deploy/scripts/trust-ca.sh`** (macOS/Linux) and **`trust-ca.ps1`**
  (Windows): one-time, per-device scripts that fetch `/ca.crt` and install it
  into the OS trust store, removing the self-signed warning everywhere that
  device reaches this server. Firefox and mobile need a short manual step
  instead (separate certificate stores) — documented, not scripted.
- **`docs/tls-and-lan-access.md`**: user-facing guide covering both the
  real-domain (Let's Encrypt, including a DNS-01/no-public-exposure option)
  and no-domain/LAN paths.

### Editor UX overhaul (2026-07-25)

A ground-up pass on the block editor, going beyond the original PLAN.md
roadmap (which this repeats, this is not one of the numbered phases above).
Full details and file-level references are in the session's saved plan;
summarized here for the changelog record.

Added:
- **Draft/publish page lifecycle.** A brand-new page is now backed by a real
  (but invisible) `Page` row from the moment the editor opens — reusing the
  `PageStatus.Draft` enum value that existed unused since Phase 2, so no
  migration was needed. This lets image uploads work on an unsaved page
  (the attachment API needs a real page id); the draft becomes visible and
  fires its "page created" side effects (audit/notification/webhook) only
  when the user clicks "Create page" (`POST /pages/draft`,
  `POST /pages/{id}/publish`, `DELETE /pages/{id}/draft`).
- **Syntax-highlighted code blocks** (`@tiptap/extension-code-block-lowlight`)
  with a language picker, copy button, and optional per-block line numbers.
- **Tables** (resizable) and **task lists**, with hover-triggered
  Confluence-style row/column insert (+) and delete (×) controls on the
  table itself (researched against real Confluence's UX) rather than a
  persistent toolbar strip.
- **Images**: paste/drag-drop/toolbar upload (using the draft page id when
  the page is new), plus a dedicated hover menu (border, drop-shadow,
  comment) — not the text-formatting bubble, which made no sense for images.
- **Underline, real link editing UI, highlight, text-align.**
- **Inline/anchored commenting**: a `comment` mark highlights the selected
  text and links it to a real `Comment` row (the backend's
  `AnchorJson`/`isInline` support existed since the comments feature landed,
  but the frontend never created anchored comments until now). Images get a
  comment without an in-document highlight (they can't carry text marks).
- **A floating selection bubble menu** and a Notion-style **"/" slash-command
  menu** for inserting blocks, built on `@tiptap/suggestion`.
- **A "paper" redesign**: title flows into the body inside one card instead
  of a boxed title above a bordered editor. The formatting toolbar and the
  page's primary actions (Edit/+Subpage, plus a new "⋮" overflow menu for
  exports/watch/save-as-template/delete) now live in one shared, full-width,
  sticky action bar below the app's topbar — consistent between view and
  edit mode, instead of a toolbar wedged between the title and the document.
- **Per-page full-width toggle** (`Page.FullWidth`, `PUT /pages/{id}/layout`),
  matching real Confluence's normal/full-width reading-width preference
  (researched: it's a per-page setting, not a session/URL setting).
- Every new node/mark type got export-renderer parity in the same phase it
  was added (`ProseMirrorRenderer.cs`), so exports never silently degrade.

Fixed:
- The code block's syntax highlighting was rendering as flat, uncolored
  text — lowlight was already producing `hljs-*` token spans, there was
  just no CSS coloring them.
- The table column-resize cursor never appeared — prosemirror-tables
  applies a `resize-cursor` class to the editor root while a column border
  is draggable, but nothing consumed it in CSS.
- A real bug affecting every popover rendered inside the editor (the link
  popovers, the link-edit form, the new comment popovers): submitting one
  also submitted the page's own outer save `<form>` and silently navigated
  away, because React bubbles synthetic events through the component tree
  regardless of BubbleMenu's DOM portal. Fixed with `stopPropagation()` on
  every affected popover's submit handler.

### Phase 5 — Advanced (2026-07-24)

Added:
- **Real-time collaborative editing.** A Node + Hocuspocus/Yjs sidecar
  (`collab/`) lets several people edit a page simultaneously, with live remote
  carets showing who is where. The editor engine is JS-only, so this is isolated
  in a small sidecar rather than reshaping the .NET stack (PLAN §1).
  - **Authorisation:** the sidecar cannot evaluate our permission model, so the
    API is the gatekeeper — it issues a short-lived HMAC-signed token only to
    users who may *edit* that page, and binds the token to that page id. The
    sidecar verifies signature, expiry, and document match.
  - **Persistence:** Yjs document state is stored in the main PostgreSQL
    database, so in-flight edits survive a restart and are covered by the
    existing backups. Saving still creates a normal `PageVersion`, preserving
    history and rollback.
  - **Optional:** with no `COLLAB_SHARED_SECRET` set, the API reports
    collaboration as disabled and the editor falls back to single-user mode.
  - Caddy proxies `/collab` websockets to the sidecar; Vite mirrors this in dev.
- **Page templates (blueprints).** Reusable starting points for new pages,
  either instance-wide or scoped to one space. Space-scoped templates require
  edit rights on the space (they affect everyone creating pages there);
  instance-wide templates can be deleted only by their author, matching the
  existing comment-ownership pattern. The new-page screen offers a "start from
  a template" picker, and any page can be saved as a template from its actions.
- **Notifications and watches.** Watch a page or a space to get notified about
  page edits, new comments, and (for spaces) new pages created in it. Shaped
  like the audit log (same Action/TargetType/MetadataJson convention) plus a
  recipient and read state, and queued on the same unit of work as the change
  that triggers it, so notifications commit atomically with it. Notifications
  never go to the person who made the change, and — reusing the same fix
  already applied to the audit log — are hidden if the recipient's access to
  the target is later revoked. SPA: a watch toggle on pages and spaces, and a
  bell in the top bar with unread count, a dropdown, and mark-as-read.
- **API tokens and webhooks — the public REST API.** Personal access tokens
  (`Authorization: Bearer <token>`) let scripts and integrations call the same
  REST API the SPA uses, without a browser session. A policy auth scheme picks
  cookie vs. bearer per request and populates the same claims either way, so
  every existing endpoint's permission checks work unchanged for token callers.
  Tokens are shown once at creation; only their SHA-256 hash is stored.
  Space-scoped, admin-managed webhooks POST an HMAC-SHA256-signed JSON payload
  (`X-Webhook-Signature`) to a URL for one or more events (`page.created`,
  `page.updated`, `comment.created`, or `*`). Delivery is queued onto an
  in-process channel and sent by a background service with retry/backoff, so a
  slow or unreachable receiver never blocks the request that triggered it.
  Verified with a real listener: signature checked valid, and an
  unsubscribed event correctly produced no delivery. SPA: an API Tokens page
  and a per-space Webhooks page.
- **OIDC / SSO — the last Phase 5 item.** Sign in via any standards-compliant
  OpenID Connect provider (Keycloak, Authentik, Google, ...) alongside local
  accounts, configured generically via `Oidc:Authority`/`ClientId`/
  `ClientSecret` (PLAN §1: "architected for OIDC/SSO later", pluggable). The
  `Smart` policy scheme now spans three auth methods (cookie / API token /
  OIDC-issued cookie), all converging on the same internal claim shape so every
  existing endpoint keeps working unchanged.
  - **Account resolution** (`IOidcUserProvisioner`, independently unit-tested):
    a returning subject signs in; a verified-email match links to an existing
    local account; an **unverified-email match is refused** — auto-linking it
    would let anyone claiming that address at the IdP take over an existing
    account; no match provisions a new passwordless account.
  - Optional: no `Oidc:Authority` means the app behaves exactly as
    local-accounts-only, unchanged.
  - SPA: a "Sign in with …" option on the login page, shown only when enabled;
    a full-page redirect (not a fetch), since the identity provider needs the
    browser's own address bar.
  - **Verified against a real Keycloak instance**, not mocks: the full
    authorization-code + PKCE redirect dance end-to-end (challenge → Keycloak
    login → callback → authenticated session), a second login resolving to the
    same account (idempotent), and — critically — an attacker registering at
    the IdP with a victim's email but *unverified* was cleanly refused with no
    session established. That run caught a real bug: `ctx.Fail()` inside
    `OnTicketReceived` didn't reliably stop sign-in from completing with the
    provider's raw, unmapped claims; fixed by writing the rejection response
    and calling `HandleResponse()` explicitly, the same pattern already used
    for provider-side failures. Migration: OidcSubjectIndex.

### Phase 4 — Fast-follow (2026-07-23)

Added:
- Labels/tags: instance-wide labels (names normalised to lower case) applied to
  pages, with add/remove per page, browse-by-label, and usage counts. Trashed
  pages drop out of label listings. SPA shows label chips on a page and a
  browse-by-label view.
- Page export (`GET /api/pages/{id}/export?format=…`) to Markdown or standalone,
  print-ready HTML, via a ProseMirror renderer that covers the editor's node and
  mark types and HTML-escapes all content. PDF is produced by printing the HTML
  export from the browser, avoiding a headless-browser dependency in the image.
  Download links added to the page view.
- Audit log: append-only record of who did what and when, written in the same
  transaction as the change it describes. Covers the page lifecycle
  (created / updated / trashed / restored / purged) and space create/archive,
  with `GET /api/audit` (filter by target, newest first) and an audit view.

- Groups: named sets of users (case-insensitive unique names) with membership
  management, plus a user directory endpoint for picking principals.
- Space permissions and page restrictions. Grants are made to a user *or* a
  group, for View / Edit / Admin on a space (higher implies lower) and
  View / Edit on a page.
  - **Default-open:** a space with no permission rows stays open to every
    authenticated user, so existing content keeps working; the first grant is
    what makes a space private.
  - **Page restrictions are inherited** by descendant pages, and only holders of
    an *explicit* space-admin grant bypass them.
  - **Anti-lockout:** whoever first restricts a space or page is guaranteed
    continued access, and the last space admin cannot be removed.
  - Enforced across spaces, pages, versions, tree, trash, comments,
    attachments, labels, export, and search — restricted content is hidden
    (404) rather than merely refused, so it is not discoverable.

- Management UI for the above: a Groups page (create/delete groups, manage
  membership from the user directory), a per-space Permissions page reached from
  the space sidebar, and a Restrictions tab on each page. Both grant flows share
  one principal picker, and each explains its current state — an open space says
  so, and warns that the first grant makes it private.

### Phase 3 — Search + backup system (2026-07-23)

Added:
- Data Protection keys are now persisted in the database (via `AppDbContext`)
  instead of the container filesystem, so signed auth cookies survive redeploys
  and work across replicas. Bumped .NET 10 packages to 10.0.10 to clear a
  critical Data Protection advisory (GHSA-9mv3-2cwr-p262).
- Soft-delete / trash for pages: deleting a page trashes it and its whole
  subtree (a global query filter hides trashed pages everywhere). New endpoints
  to list a space's trash, restore a trashed subtree, and permanently purge it,
  plus a Trash view in the SPA with restore / delete-permanently.
- Full-text search over page titles and content (`GET /api/search`). Pages keep
  a plain-text `SearchText` (title + extracted content) maintained on every
  save; on PostgreSQL this feeds a generated `tsvector` column with a GIN index
  and relevance ranking, with a portable `LIKE` fallback for the test provider.
  Trashed pages are excluded. SPA gains a top-bar search box and results page.
- pgBackRest physical backups + point-in-time recovery (backup Layer 1). The
  `db` image now bundles pgBackRest with continuous WAL archiving to an
  encrypted repository; a `pgbackrest` sidecar creates the stanza and runs
  scheduled full/incremental backups. Scripts for verification, an end-to-end
  PITR self-test, and disaster-recovery/PITR restore. Verified in Docker: a real
  recovery to an exact target time excluded post-target changes.
- Attachment (`uploads`) file backups added to the logical-backup sidecar
  (Layer 3), on the same schedule and retention as the `pg_dump` layer.
- `docs/backup-recovery.md` rewritten as a full runbook: in-app restore, PITR,
  disaster recovery, verification, and enabling S3 offsite. New
  `BACKUP_ENCRYPTION_KEY` in `.env`.

### Phase 2 — Core content (2026-07-22)

Added:
- EF Core 10 + Npgsql data layer. Domain entities (`User`, `Space`, `Page`,
  `PageVersion`, `Attachment`, `Comment`) per PLAN §4, with page content stored
  as ProseMirror JSON in `jsonb` columns and every save creating a new version.
  Initial migration `InitialCreate`; migrations run automatically on startup.
- Local authentication: register / login / logout / me endpoints, cookie-based
  sessions, and Argon2id password hashing. Unauthenticated API calls get 401
  instead of a login redirect.
- Database health check wired into `GET /api/health`.
- Test project (`tests/Api.Tests`) running the API in-process against SQLite
  in-memory (no Docker needed); covers the password hasher and the full auth
  flow.
- Spaces: create / list / get / update, archive / unarchive, with unique,
  validated space keys.
- Pages: create / read / update with a hierarchical page tree, plus the full
  version model — every save appends a `PageVersion`, with version-history
  listing, single-version fetch, and restore (rollback appends a new version so
  nothing is lost). Move/reparent with cycle detection; delete guarded against
  orphaning child pages (soft-delete/trash arrives in Phase 3).
- Integration tests for spaces and pages (create/version/restore/tree/move/
  delete flows).
- Attachments: multipart upload, per-page listing, metadata, download, and
  delete. Bytes stored on the uploads volume behind a pluggable storage
  interface (local disk now, S3-compatible later); 25 MB per-file limit.
- Comments: footer and inline (anchored) comments with threaded replies. Edit
  and soft-delete restricted to the author; soft-delete keeps the row so reply
  threads survive.
- Integration tests for attachments (upload/download/delete) and comments
  (threads, validation, soft-delete, author-only edits).
- React 19 + TypeScript SPA (React Router 7): sign in / register, a spaces list
  with create, and a space view with a hierarchical page-tree sidebar.
- TipTap v3 block editor with a formatting toolbar for creating and editing
  pages; content round-trips as ProseMirror JSON. Read-only rendering reuses the
  same editor.
- Page view with tabbed footer: threaded comments (post/reply/edit/delete own),
  attachments (upload/download/delete), and version history (preview any version
  and restore it). Fixed the dev proxy port to match the API (5291).

### Phase 1 — Foundation (2026-07-22)

Added:
- Project plan (`PLAN.md`) covering stack, feature scope, data model,
  backup/recovery strategy, and phased roadmap.
- ASP.NET Core (.NET 10) API scaffold with a vertical-slice layout and a
  `GET /api/health` liveness endpoint. Serves the built React SPA same-origin
  in production with SPA-route fallback.
- React 19 + TypeScript + Vite frontend that displays live API health; Vite dev
  server proxies `/api` to the backend.
- Multi-stage `Dockerfile` (build SPA → publish API → slim non-root runtime
  with a container health check).
- `docker-compose.yml` full stack: PostgreSQL 18, the app, Caddy reverse proxy
  with automatic HTTPS, and a scheduled backup sidecar. All state on named
  volumes.
- Logical backup system: scheduled `pg_dump` with retention, plus on-demand
  `backup.sh`, `restore.sh`, and `verify-backup.sh`.
- Developer docs: `docs/architecture.md`, `docs/backup-recovery.md`, and this
  changelog. `.env.example`, `.gitignore`, and `README.md`.

Notes:
- EF Core / PostgreSQL wiring, and pgBackRest point-in-time recovery, are
  scheduled for Phase 2 and Phase 3 respectively.
