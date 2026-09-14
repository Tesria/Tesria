# Changelog

All notable changes to Tesria are recorded here.
Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Design: API and MCP writes become tracked changes in a live draft (2026-09-13)

The stale-collaborative-document gap found on 2026-09-13 has a design now,
as dev-plan **8.6** (Fable half done; Opus implements). An API or MCP write
lands in an open draft the way a second person's typing does — added text
highlighted, removed text struck through, both labelled with their
source — and publishing accepts it. A document nobody has open is
reconciled the same way when it is next loaded, and publish carries the
version it was reconciled to so a missed notification cannot overwrite.
One reconcile function, one schema, three callers. The plan entry has the
full shape, the implementation order and the verification.

### Mobile: the phone menu carries the space's pages, and the toolbar finally fits (2026-09-13)

Three things the recent chrome and theme work had left broken at phone
width, found by shooting every route at 390, 640, 700, 768, 900, 1100,
1440 and 1800 pixels with the screenshot harness.

- **The editor toolbar overlapped Update and Close on a phone and a
  tablet.** Only the thirteen formatting buttons could leave the row; the
  text-style dropdown, the two colour palettes, alignment, link and the
  `+` menu were fixed, and together they were already wider than a phone.
  Alignment, text colour and highlight are collapsible now (alignment
  becomes three items in the `+` menu; each palette unfolds in place
  under its item, so a phone still has every colour), and the text-style
  trigger shrinks to **Aa** under a container query on the editor's own
  row — a narrow reading column on a wide screen counts too. At 390px the
  row reads `Aa · B · link · +` beside Update and Close; at 640px the
  marks and both palettes are back; at 1800px everything is. The overflow
  list in the menu is in display order (italic first), not loss order.
- **The overflow measurement never counted the separators**, which is why
  the `+` chevron sat under Full width on a 1440px screen while everything
  supposedly fit. Counted now.
- **Changing page on a phone took three taps** — hamburger → Spaces → the
  space → the page — because the menu knew nothing about the space you
  were in. `SpacePage` now publishes its tree through a small context
  (`spaceNav.ts`) and the menu renders it, with `+ New page` above and
  Space settings below, phone only. The menu scrolls inside itself, since
  a tree is taller than a phone.
- **Admin tables were wider than a phone and their rows grew to fit a
  vertical stack of actions.** At phone width the cells stay on one line
  and the table scrolls sideways, so rows are rows again.

Checked and left alone: the space layout at 700px (a 260px sidebar beside
a 440px column, nothing overflowing), the settings and admin tab rows
(they already scroll sideways), and the dashboard, security, audit,
profile and search views, which stack correctly.

Found while doing this and **not fixed here**, because it is a design
decision: the collaborative document for a page is loaded by name from
`CollabDocuments` and never reconciled with the page's current version.
The API, `PageWriter` and the MCP `update_page` tool write the page and
leave that document alone, so the next person to open the editor sees the
last *editor* state, not the page — and pressing Update would write it
back over the API's changes. It showed up as a broken image in the
editor (a manual page whose screenshot attachment had been replaced by
script). See the note in `docs/architecture.md`.

### Manual: light-theme screenshots, one per feature, and the admin half (2026-09-11)

**Every screenshot retaken in the light theme with the default blue
accent**, and the count taken from 27 to **86** so that each feature has a
picture of itself. The gap was the authoring UI: the manual could render a
panel or a status lozenge live on the page, but a reader could not see the
menu that produces one. Now they can — the insert catalogue, the slash
menu, the colour palettes, the cell options, the status and date pickers,
the live-block settings panel, the image hover menu, the link popover.

**The admin section is complete**, with all eight tabs photographed and
the prose filled out: what each dashboard number is for and what it is
*not* for, the full list of audited actions by area, the leaving-checklist
for suspending an account, and why the base URL matters more than it
looks. Taking those needed an administrator, so the documentation bot was
promoted to one — at the repository owner's explicit request, recorded
here because a standing admin account is a standing risk, and it can be
demoted from **Administration → Users** whenever the docs are done.

**Section titles carry an emoji** (📘 🚀 ✏️ 🗂️ 💬 📤 👤 🛠️ 🔌). Forty-odd
leaf pages in one column gave the eye nothing to catch on; eight marked
rows break the tree into sections at a glance.

Harness changes behind all of it, in `scripts/screenshots/`:

- **Appearance is seeded before first paint** (`SHOT_THEME`, `SHOT_ACCENT`)
  rather than clicked afterwards, so the first shot of a run is not in
  whatever the last run left behind.
- **A clip is in page coordinates; `boundingBox()` is in viewport
  coordinates.** Every shot below the fold was silently clipping an empty
  region until the scroll offset was added.
- **Typing in the editor reaches the collaborative document immediately**,
  saved or not — so a shot that types has to put the document back. The
  first attempt used a blanket `Control+Z`, which walked back through the
  whole Yjs history and emptied the page, breaking every later shot of it.
  It now deletes exactly what it typed, and the typing shots run last.

### Fix: only the Details tab had a breadcrumb (2026-09-11)

The breadcrumb still matched the pre-move URLs (`/spaces/:key/permissions`
and friends), so after those became tabs of Settings the other three
matched nothing and rendered no crumb at all — and the page jumped a line
every time you changed tab. One match for the whole settings section now,
and a two-part crumb: **Space settings / Permissions**.

### Space sidebar: three bands, and settings absorbs its three neighbours (2026-09-11)

Two problems, one shape.

**The sidebar scrolled with the document.** It was an ordinary grid item,
so reading a long page carried the space's name, the + New page button,
the PAGES heading and the settings links off the top of the screen — the
navigation disappeared exactly when a reader was deepest into a page and
most likely to want it. It is now its own scroll container: pinned under
the 52px topbar, exactly as tall as the rest of the viewport, with a head
and a foot that stay put and only the tree's rows scrolling between them.
`align-self: start` is the part that is easy to miss — a grid item
stretches to its row's height by default, and a sticky element as tall as
its container has nothing to stick within.

**Permissions, webhooks and trash were siblings of settings** because all
three were built before a space had a settings page to put them in. That
left the sidebar doing two unrelated jobs, and the administrative one
crowding out page browsing as a tree grows. They are now the three tabs
beside Details under **Space settings**, in the same tabbed shell the
admin area uses, reached by one sidebar entry pinned to the bottom above
a rule. The old URLs redirect, so bookmarks and links written into pages
still land in the right place.

Walked as a signed-in member and signed out, at desktop and phone widths,
across every space route plus search, labels, profile and the admin
refusal — no uncaught errors on any of them. A real page was created,
edited, deleted and purged through the moved Trash tab; the purge stops
at the sudo-mode password prompt, which is the intended behaviour for an
irreversible action.

**Not** walked as an instance administrator: that needs an administrator's
password, which this assistant does not have and should not be typing.
Nothing under `/admin` was touched by this change — the route tree that
moved is entirely under `spaces/:key` — but the admin tabs are worth a
glance from someone who can sign in as one.

The user manual described the old arrangement, so it was corrected in the
same pass: five pages reworded and five screenshots retaken. A manual that
documents a layout the product no longer has is worse than no manual.

### A user manual, written in Tesria (2026-09-11)

A new **Tesria User Manual** space (`MANUAL`): 47 pages covering getting
started, every block in the editor, organising a wiki, working together,
sharing and exporting, accounts, administration, and the two integration
doors (REST and MCP). Written as real pages rather than as Markdown in
`docs/`, so it is searchable, labelled, exportable and editable in the
product it documents — and so it dogfoods the features it describes: the
section index pages use children displays, the labels page ends with a
labels list, the live-blocks page demonstrates a content-by-label block.

**27 screenshots**, cropped to what they are about and, where it helps,
annotated with circles and arrows. They are taken by a Playwright harness
([`scripts/screenshots/`](../scripts/screenshots/README.md)) running from
the **PDF sidecar's image**, which already carries a Chromium matched to
its Playwright — no new dependency.
Two things about that harness are worth recording, because both cost time:

- **It runs inside Caddy's network namespace** (`--network
  container:tesria-caddy-1`), so `https://tesria.localhost` is this
  instance through the real proxy. Going straight to the app container
  fails twice over: the session cookie is `Secure`, so plain HTTP silently
  drops it and every shot comes out signed-out; and `/collab` is routed by
  Caddy, so the collaborative editor never loads a page's content and
  every editor screenshot is an empty document with a toolbar over it.
  Chromium also upgrades a *named* host to HTTPS on its own and will not
  be talked out of it by `--disable-features=HttpsUpgrades` — only an IP
  literal or a real HTTPS endpoint gets past that.
- **Annotations are drawn in the DOM before the capture**, as an SVG
  overlay positioned from `getBoundingClientRect()`, not painted onto the
  PNG afterwards. Circles and arrows come out as crisp as the UI under
  them, and they are described per shot in the spec rather than by
  pixel-pushing.

Screenshots of the admin area are **not** in the manual: taking them would
have meant granting the authoring account instance-admin, and that is not
a privilege to hand out unasked. Those four pages are written without
pictures; the harness is checked in and can fill them in later.

Fixture: a **Manual Bot** account (`manual-bot@tesria.local`) authors the
space, the same arrangement as the API space's bot.

### Fix: search snippets showed their `**` markers in the browser (2026-09-11)

Regression from the retrieval work earlier the same day. `SearchSnippets`
marks the matched words with `**` because the same snippet is served to
the MCP tools and to anything else reading the API, where HTML would be
the wrong thing to send — but the search results page rendered it as text,
so a reader got `**webhook**` on the screen.

Un-marked at the edge, in `SearchPage.tsx`, into `<mark>` elements — by
splitting the string, never by `dangerouslySetInnerHTML`, because a
snippet is page content and page content is not markup this app trusts.
Styled as a weight change rather than a highlighter block: a snippet can
carry a dozen matches and a row of yellow bars is harder to read than the
sentence was.

### The space sidebar's emoji are now drawn icons (2026-09-11)

`📑 ⚙ 🔒 🪝 🗑` were the only pictures in the app the app did not draw
itself: full-colour glyphs, a different weight and shape on every
platform, and no relationship to the chosen accent.

`NavIcons.tsx` replaces them with five outline SVGs in the same language
as `BrandMark` and the editor's icon set — 24×24 box, 1.8px stroke, round
caps and joins, `fill: none`. Everything is `currentColor`, so `.nav-icon`
points them at `--primary` and they follow the theme *and* the accent for
free, the same trick `.brand__mark` already used. Verified live across all
six accents.

Two of the drawings are decisions rather than transcriptions. **Webhooks**
is one event fanning out to two subscribers, not a hook: a hook says
nothing about what a webhook does, and does not survive 16px. **Pages**
started as the brand's rhombus without its stack and was changed to a
plain sheet with a folded corner — the rhombus read as a shape, not as a
document; it means something in the logo, where the stack gives it
context, and nothing beside a page tree.

### Retrieval: snippets that show the match, sections, and a score (2026-09-11)

Groundwork for using the MCP server (8.4) as a context source, and a
straight improvement to search in the browser too.

- **A snippet is now the passage that matched.** It was the first 200
  characters of the page, so searching "webhook" and being shown a page's
  opening sentence told a reader — and an assistant — nothing about why it
  came back; the only way to judge relevance was to open every result.
  Postgres's `ts_headline` does this properly and has no EF binding, so
  `SearchSnippets` is the one place this app writes SQL by hand. The
  matched words come back marked in `**bold**` — plain text rather than
  HTML, because these snippets go to an assistant as often as to a browser.
  Shared by REST search and the MCP tool, computed *after* the permission
  filter so nothing is prepared for a page that will not be returned.
- **`get_page` returns a heading outline, and can return one section.**
  `section: "deployment"` gives that heading and everything under it, up to
  the next heading of the same or a higher level — reusing the anchors
  Phase 7 Wave A already derives, so "the section this `#link` points at"
  and "the section to fetch" are the same thing. Verified live: a 4,385-
  character page down to 2,739 for one section. Top-level headings only;
  slicing mid-panel would produce something that is not a document, so a
  nested heading is listed in the outline but refused as a section.
- **The relevance score is exposed** on MCP search hits, so a client can
  decide what is worth reading. Null where the database cannot rank, and
  omitted rather than sent as a fake `0` — "unranked" is not a score of
  zero.

Semantic search — the actual fix for synonym and paraphrase queries — is
now a written-up entry in `roadmap.md` with the evidence, the two design
decisions it cannot dodge, and the size at which it becomes worth doing.
The wiki is 58 pages and 31 KB of text today, which is why it is not worth
doing yet.

Also added to `roadmap.md`: a **roadmap planner** timeline block modelled
on Confluence's macro, with its data model and the four design questions
it raises.

### MCP server — the ten tools, one write path (dev-plan 8.4 — Opus half) (2026-09-11)

The tool surface Fable specified, built against the contract:
`list_spaces`, `get_space_tree`, `search_pages`, `get_page`,
`find_pages_by_label`, `list_labels` (read) and `create_page`,
`update_page`, `add_page_label`, `remove_page_label` (write).

**`PageWriter` is now the only place a page is created or updated.**
`PageEndpoints.Create`/`Update` became thin translations of its result into
HTTP; the MCP tools translate the same result into a tool response. That is
what makes "a page written by an assistant is indistinguishable from one
written in the browser" true rather than aspirational — the audit entry,
the watcher and mention notifications and the webhook all come from one
code path. The existing endpoint tests were the safety net for the
extraction and stayed green throughout.

**Markdown converts over exactly the subset the export emits**, so a page
survives read → edit → write. The round-trip test caught a real defect:
Markdig models `[x] done` as a task-list inline followed by the literal
`" done"`, so the separating space belongs to the marker — dropping the
marker without it made every round trip indent the text one space further,
compounding on each edit. Also learned the hard way: two `-` lists
separated only by a blank line are *one* list in CommonMark.

Errors keep the masking rule. Writing to a page you cannot see is "not
found", never "forbidden" — the latter confirms it exists. A read-only
token is refused by every write tool *before* anything runs, and a test
asserts the page is unchanged afterwards.

The API space gained a page covering connecting a client, the tools, the
Markdown contract and what the server deliberately will not do (delete,
permissions, admin, attachments).

Sixteen tests for the tools and the converter, on top of the seven from the
design half.

### MCP server — the contract, token scopes, and `/mcp` (dev-plan 8.4 — Fable half) (2026-09-11)

*The plan says to load the `claude-api` skill before designing the tool
surface; it is not enabled on this account, so the design is from the MCP
specification and the official C# SDK's conventions directly.*

The contract is in `architecture.md` ("MCP server"). The decisions that are
expensive to reverse, and why:

- **In-process, in .NET, on the official SDK, stateless.** Not a sidecar:
  a sidecar would call REST with a forwarded token — a second hop and a
  second place permissions could go wrong. In-process, a tool runs the same
  `IPermissionService` every endpoint does, so an assistant sees exactly
  what its token's owner could. Stateless, so every request stands on its
  own token and nothing pins to a session behind the proxy.
- **Token only.** `/mcp` accepts the `ApiToken` scheme and nothing else. A
  browser session is never a credential there, so a page in someone's tab
  cannot drive the assistant surface — there is no CSRF question because
  the credential cannot be ambient. Tested.
- **Token scopes, finally: `ApiToken.ReadOnly`.** Minted with `readOnly`,
  shown in the listing and the profile, defaulting to full access so nothing
  narrows silently on upgrade (existing tokens are unchanged). One claim on
  the principal; **one middleware** refuses unsafe REST methods from a
  read-only token with `403 read_only_token` — enforced by HTTP method in a
  single place rather than per endpoint, because a scope checked per
  endpoint is one forgotten on the next endpoint. MCP write tools check
  the same claim first, since their transport is all POST.
- **Markdown is the content contract.** `get_page` returns the same
  Markdown the export produces, with dynamic blocks resolved as the caller
  (a children list arrives as links, not a placeholder). Writes will accept
  Markdown converted server-side over the subset the export emits, with
  `contentJson` as the escape hatch for exact copies.
- **Errors never reveal what the caller may not see.** A page you cannot
  view is "not found" to a tool, as it is 404 to REST; a listing omits it.

Built with the spec so it is proven rather than hypothetical: the scope end
to end (domain, claim, middleware, minting, three tests) and `/mcp` with
`list_spaces` and `get_page` (four tests driving it as a client would:
JSON-RPC over Streamable HTTP, initialize, tools/list, a call, and the leak
case). The remaining tools, the `PageWriter` extraction, the Markdown
converter and the API-space page are Opus work against the contract.

### OpenAPI spec and a self-hosted API reference (dev-plan 8.3) (2026-09-10)

`GET /api/openapi.json` — OpenAPI 3.1, generated from the routes, so it
cannot describe an endpoint that does not exist. A browsable reference is
at `/api/docs`. Both are open: the shape of an API is not a secret, every
endpoint still enforces its own permissions, and an operator who disagrees
can block two paths at the proxy.

Two things the generator could not know, both now in the document:

- **Both ways of authenticating.** A token (`Authorization: Bearer`) and the
  SPA's session cookie, the latter noting that unsafe requests also need
  `X-Requested-With: Tesria` — the CSRF defence — and that everything you
  may not see answers 404 rather than 403.
- **Which endpoints actually need one.** An endpoint is open two ways here:
  an explicit `.AllowAnonymous()` (Phase 5's public routes) *and* simply
  never having asked for authorization — `/api/health` does the latter, and
  describing it as needing a token would be a lie the generator cannot
  catch. Both now report no security requirement.

**The reference is a third-party UI inside an app with a strict CSP**, which
took care. Its own JavaScript is served from this origin (no CDN), its
default web fonts are turned off rather than silently blocked, and the one
inline `<script>` on its page runs under a **fresh per-request nonce** that
`SecurityHeadersMiddleware` mints only for `/api/docs` — rather than opening
`unsafe-inline` for the whole app, which would undo the reason that policy
exists. Tests pin that the nonce appears only on that path and differs every
request.

Some of its sidebar features call the vendor's hosted service. This app's
`connect-src 'self'` blocks them, which is the behaviour we want from a
documentation page — the button leading to them is hidden so nothing broken
is put in front of a reader, and the CSP remains the backstop.

The API space gained a page describing the spec, the reference, both auth
schemes and how to generate a client, so the prose documentation and the
machine-readable one stay in step.

### PDF export, and a licence (dev-plan 8.1, 8.2) (2026-09-10)

**PDF export (8.1)** — the one claim on the brand page that was not true.
A Playwright sidecar renders the *same* print-ready HTML the html format
returns, so there is one renderer and a PDF cannot drift from the page. A
real browser engine is the only honest way to do this, and a ~400MB
Chromium has no business in the app image, so it is a sidecar like collab.

The sidecar runs with **its network switched off** — `offline: true` plus a
route handler that aborts everything but `data:`. That is only possible
because the HTML export is now genuinely self-contained: diagrams carry
their own renderer (last commit) and **images are now inlined as data
URIs**. That last part fixes a real bug in its own right — an exported HTML
page referenced `/api/attachments/…`, so its images were broken the moment
the file left the app. Inlining is bounded (4MB an image, 20MB a document);
past the budget an image keeps its URL, as before. Markdown is deliberately
unchanged: a data: URI is unreadable in a text file.

Where no renderer is configured, `?format=pdf` answers **503 with advice**
— "export as HTML and print it" — rather than a dead end or a 500. The
same if the sidecar is down.

**Caught immediately, and it is the classic one:** `playwright-core` was
declared as `^1.56.0` and resolved to 1.63.0 against a v1.56.0 image, so
every render failed with "Executable doesn't exist". The library version
and the image tag must be the *same* version; both are now pinned exactly,
with a comment saying to bump them together or not at all.

**Licence (8.2)** — the brand page says Apache 2.0 and the repo had no
`LICENSE` file. Added, with a `NOTICE` listing the third-party components,
and SPDX identifiers in both `package.json`s and the `.csproj`. Public
visibility is still the user's call; the licence file existing is a
precondition for that, not a consequence.

Verified end to end: a real PDF of a page carrying dynamic blocks, an
excerpt, page properties and a Mermaid diagram — the diagram renders as
*vector text* in the PDF, so the live blocks and the drawing both survive.

### Editor parity Waves E and F — media, embeds, diagrams, maths, charts (dev-plan 7) (2026-09-10)

**Phase 7 is complete.**

**Wave E — embeds and media.** An embed is a third-party iframe on everyone
else's page, so the allowlist is enforced **twice**: `/api/embeds/resolve`
refuses a host that is not on it, and the CSP's `frame-src` is built from
the same list, so even a client-side bug cannot frame an off-list site. The
client never decides what may be framed — it asks, and frames exactly what
it is told to.

Host matching is deliberately tiny and gets the hostile cases in tests: a
plain `EndsWith` would admit `evil-youtube.com` and
`youtube.com.attacker.net`, so matching is on a label boundary. A known
provider is also *narrowed* — a YouTube watch page becomes the no-cookie
embed player, a Google Doc becomes its preview — while an allowlisted host
with no provider rule frames as pasted, which is what makes "allowlist our
internal Grafana" work with no code.

Smart links fetch Open Graph tags through the 3.4 SSRF guard and cache them
(a week for a success, an hour for a failure) so a page of links is not a
page of outbound requests. The response body is capped at 256KB, and an
`og:image` is only used if it is absolute https. Unfurling requires an
account; resolving does not, because an embed on a public page is part of
that page.

Attachment-backed media (video, audio, PDF, file card) picks its rendering
from the file's own content type rather than an author's choice, so a .mp4
is a video wherever it appears. PDFs use the browser's own viewer in a
same-origin frame — no PDF.js in the bundle. A gallery is a *layout over
image nodes*, so every existing image affordance keeps working and the
export renders ordinary images.

**Wave F — technical content.** Mermaid is a code-block *language*, not a
node: the source stays an ordinary fenced block in every export and the
diagram is a view of it. KaTeX maths is one node with a `display` flag.
Charts read a table already on the page by its ordinal ("the second table"),
never copying the data — editing the table redraws the chart. Deliberately
not a Wave D dynamic block: the table is right here, so a round trip would
be slower, would miss unsaved edits, and would need a fourth result shape.

Both libraries load on demand — a page with no diagram never downloads
Mermaid's 500KB, and Vite splits it per diagram type. The main bundle is
unchanged. Charts are plain SVG and flexbox rather than a charting library.

Exports stay sane outside the app: an embed and a smart link become plain
links (never an iframe, and a `javascript:` URL becomes no link at all),
maths exports as `$…$`, and a chart names the table it charts rather than
duplicating it. A page with a Mermaid diagram carries **this instance's own
Mermaid bundle, inlined** — no CDN, and no dependence on this instance
still being reachable. An exported file is meant to be something you keep,
and a document that only renders while a server answers is not that. The
cost is ~3MB, only on pages that actually have a diagram; where the bundle
is missing the export ships the diagram source alone, which is still
readable. **Nothing in an exported file reaches the network**, and a test
asserts it.

**Found by upgrading a running instance, which no test could catch:** the
new `EmbedAllowlist` column defaulted to empty on an instance that already
had a settings row, silently turning embeds off on upgrade. The C# property
initialiser only runs for a *new* settings object; the default now lives on
the migration's column too. Every test creates a fresh database and so
never took that path.

38 tests for the two waves.

### Editor parity Wave D — the other eleven kinds (dev-plan 7, Wave D — Opus half) (2026-09-10)

Against the contract Fable designed: **Recently updated, Content by label,
Attachments, Change history, Contributors, Include page, Excerpt include,
Page properties report, Labels list, Task report** and **Page tree**. Each
is one class implementing `IDynamicBlockKind` — a query, and nothing else.
The contract held: no kind needed a new result shape, a renderer change, or
a line of React.

Two static container nodes came with them, because two kinds read content
rather than rows: `excerpt` (what `excerpt-include` takes) and
`pageProperties` (a two-column table `page-properties-report` collects
across pages). Both are plain containers with no node view, so an exported
page shows its excerpt and its properties as ordinary content — which is
what they are.

The permission rule held everywhere, and the leak tests are the interesting
ones:

- **Page properties report** derives its *columns* from the pages it finds,
  so a restricted page could leak a column name ("Salary band") with no row
  behind it. It does not.
- **Contributors** and **Labels list** are counts: a contributor whose only
  edits are on a restricted page, and a label whose only pages are hidden,
  must not appear — and the counts of those that do must not include the
  hidden ones.
- **Task report** filters visibility *before* reading any content, so a
  restricted page's action items are never walked at all.
- **Recently updated** over-fetches and filters, so a run of restricted
  pages makes it look further down rather than return a short list.
- **Include page** on a page you cannot see reports "nothing to show", never
  an error naming the page — an error would confirm it exists.

Nineteen tests for the kinds, on top of the mechanism's eight.

Two fixes while building:

- A **draft host** (a brand-new page being composed) was invisible to the
  block endpoint, because the global query filter hides drafts — every block
  on a new page 404'd until Publish. Now `IgnoreQueryFilters()` with the
  soft-delete half reapplied by hand, the same pattern `SetLayout` uses.
- Candidate pages are ordered **in memory, not in SQL**: SQLite cannot
  `ORDER BY` a `DateTimeOffset`, and sorting here means both providers order
  identically rather than only Postgres being exercised.

### Editor parity Wave D — the dynamic-block contract, and Children display (dev-plan 7, Wave D — Fable half) (2026-09-10)

Wave D is "a block whose content is the answer to a query" — Confluence's
Children display, Recently updated, Task report and nine more. The plan
splits it: Fable designs the mechanism, Opus adds kinds against it. This is
the Fable half: the contract, written into `architecture.md` ("Dynamic
blocks"), and the mechanism built end to end with **one** reference kind so
the contract is proven rather than hypothetical.

The decisions, each with its reason in the spec:

- **One node, `dynamicBlock { kind, params }`,** an atom that stores the
  question and never the answer. A stored copy of "children of this page"
  is wrong the moment a child is added and a permission leak the moment a
  page is restricted.
- **One result shape** (`list` / `table` / `document`) with exactly one
  renderer in the SPA and one in the exporter. A kind is therefore *only a
  query*: no React, no HTML, no Markdown. This is what makes the twelfth
  kind cost what the second did.
- **One endpoint,** `GET /api/pages/{host}/blocks/{kind}?…`, anonymous-
  capable, masking an unviewable host as 404. Unknown kind and bad params
  are 400s naming the field; out-of-range numbers are clamped so a document
  written against a looser server still renders.
- **Permission filtering is the kind's job, with one helper that makes it
  hard to get wrong** (`BlockContext.VisibleAsync`, the search/tree
  two-pass rule). A page the caller cannot view must not influence a result
  at all — not its title, not a count.
- **Export snapshots at export time, as the exporting user,** through the
  same service; a failed block is a placeholder, never a failed export.
  `document`-shaped kinds render their included page's own blocks as
  placeholders — depth 1 — so an include of an include cannot recurse, on
  either side.
- **The node view learns its host page from `editor.storage`,** the same
  stash the slash menu's upload callbacks use; history and template
  previews, where nobody sets it, show "Shown on the page".
- **Params are edited by one generic form** generated from each kind's
  declared schema in the client catalogue; the slash and + menus list that
  same catalogue.

`children` (Confluence's Children display) is the reference kind: depth
1–3, three sort orders, a hidden parent hiding its subtree. Eight tests
cover the mechanism, including the leak test every future kind owes and an
export taken as a user who cannot see one branch. Verified live on the API
space's root page — which now carries a real Children display at depth 2,
kept deliberately as documentation. Remaining kinds are Opus work; the
per-kind params and queries are tabulated in the spec.

### Editor parity Wave C — mentions, emoji, action-item assignees (dev-plan 7, Wave C) (2026-09-10)

- **@mention.** A `mention` inline node carrying the user's id *and* a
  snapshot of their display name. The id is what the server diffs; the label
  is what keeps the mention readable in an exported file, in a page version
  from last year, and after the account is deleted — none of which have a
  directory to look the name up in. The `@` popup filters a directory
  fetched once per page load: a self-hosted wiki's user list is small, and a
  request per keystroke would make the popup lag behind the typing.
- **On save, newly mentioned people are notified** (`user.mentioned`).
  *Newly*: the mentions in the version being replaced are subtracted first,
  so fixing a typo on a page that names ten people does not ping all ten
  again.
  **Each recipient is permission-checked first.** The notification carries
  the page title, so mentioning someone on a page they cannot open would be
  a way to leak that title. They are told nothing rather than told and then
  given a 404. This needed a small seam in `PermissionService`
  (`AsUser(userId)`): the service was written entirely against the request's
  own identity, and every rule now reads one `UserId` property so an
  "as user" evaluation cannot fall back to the caller's rights.
- **`:emoji` suggestion** over a curated list of ~40, inserting the literal
  character. Nothing enters the schema, the renderer or the search index — an
  emoji is text that happened to be typed with a picker. Curated rather than
  the full Unicode table, which is ~1,900 entries with several names each
  and a real payload for a feature whose job is three keystrokes. Needs two
  characters after the `:` before it opens, or every colon in a URL or a
  time would pop a menu.
- **Action-item assignees.** Typing `@name` in a task assigns it, exactly as
  Confluence does. The mention is the source of truth; `assigneeId` /
  `assigneeName` on the `taskItem` are a denormalised copy kept in step by a
  plugin, so Wave D's Task report can query "assigned to me" instead of
  walking every page's document tree. Neither the app nor the export draws
  the name a second time — the mention it came from is already in the item's
  own text.
- **Fixed while building it:** all three `@tiptap/suggestion` plugins
  (slash, mention, emoji) default to one shared plugin key, so adding the
  second threw "Adding different instances of a keyed plugin" and took the
  whole editor down with it. Each now has its own.

Verified live: the `@` popup with avatars, insertion by click and by Enter,
`:roc` → 🚀, and a task picking up its assignee from the mention typed into
it. The notification path is covered by tests, including one asserting that
a mention on a restricted page tells the recipient nothing at all.

### Editor parity Wave B — text colour, scripts, indent (dev-plan 7, Wave B) (2026-09-10)

- **Text colour**, stored as a colour *name* out of eight, not a hex.
  Highlight can afford a hex because it is a background and the ink on top
  is pinned per theme; coloured *text* has no such escape — a hex dark
  enough to read on white is invisible on this app's dark background, and no
  CSS rule can lighten a colour it cannot see. A name can be re-pointed per
  theme (`--text-color-*`), which is what makes the feature work in dark
  mode at all, and it also means nothing from the document can reach a
  `style` attribute. The export renderer inlines the light-theme ink.
- **Subscript and superscript** (`Mod-,` / `Mod-.`), exporting as
  `<sub>`/`<sup>` in HTML and as raw HTML in Markdown, which most renderers
  pass through.
- **Indent / outdent** (`Mod-]` / `Mod-[`), as a `textIndent` attribute on
  the paragraph or heading rather than a wrapper node — an indent is a
  property of the block, and a wrapper would fight list lifting. Capped at
  four levels, and the rendered `margin-left` is computed from the clamped
  integer on both sides, never echoed from the document. `Tab` is
  deliberately untouched: it already moves between table cells and nests
  list items.
- **Clear formatting** (`Mod-\`) strips marks, indent and alignment, and
  turns a heading back into body text — but deliberately *not*
  `clearNodes()`, which would also unwrap a list, a panel or a layout
  column. That is a structural edit, not a formatting one.
- **Shortcut audit against Confluence's set.** Everything it lists was
  already bound by StarterKit, TextAlign or Highlight — headings
  (`Mod-Alt-1…6`), normal text (`Mod-Alt-0`), lists (`Mod-Shift-7/8/9`),
  alignment (`Mod-Shift-l/e/r`), highlight (`Mod-Shift-h`), strike
  (`Mod-Shift-s`) — with one real gap: **`Mod-K` for links**, now bound. It
  opens the toolbar's link popover rather than editing the document, so the
  shortcut calls its subscriber directly instead of faking a transaction to
  get a React re-render (`linkShortcut.ts`).
- `@tiptap/extension-subscript` and `-superscript` added;
  `@tiptap/extension-text-style` was installed and then removed once text
  colour became a name-keyed mark of its own. `scripts/audit.sh` clean.

Verified live in both themes: every colour legible on each, indent clamping
at four levels under repeated presses, clear formatting leaving status and
date atoms and the paragraph itself intact, and `Cmd+K` opening the link
popover with the heading list.

### Editor parity Wave A — structural blocks (dev-plan 7, Wave A) (2026-09-10)

Seven of Confluence's structural elements, in the editor, the reading view
and both export formats. Started as Fable by user override and finished as
Opus (the plan tags the wave Opus).

- **Heading anchors.** Every heading gets an id derived from its text
  (lower-cased, non-alphanumerics collapsed to hyphens, duplicates suffixed
  `-2`, `-3`). *Derived, never stored* — the same choice Confluence makes:
  a stored id duplicates on paste, drifts between collaborators and needs a
  migration for every existing page. The cost is one algorithm written
  twice, in `headingAnchors.ts` and `Features/Export/HeadingAnchors.cs`,
  pinned together by `HeadingAnchorTests`. In the editor the ids are
  ProseMirror *decorations*, so they are recomputed from the document on
  every change and never serialised. `#slug` links scroll rather than
  navigate, both in the reading view and when a page is opened at
  `…/pages/{id}#slug`, and the link popover lists the page's headings to
  pick from.
- **Table of contents** — a block with no stored content. The node view
  lists headings live; the exporter builds the same nested list at export
  time. Nothing ever holds a stale copy of the page's own outline.
- **Expand** — collapsible section, title stored, open state not (it starts
  open while editing and closed for readers). Exports as `<details>`.
- **Status** — inline lozenge, one of Confluence's six colour *names*; the
  colour value never comes from the document, so a hostile `color` cannot
  reach a style attribute. **Decision** — a panel-shaped block with a fixed
  check icon. **Date** — an ISO calendar date rendered in the reader's own
  locale (parsed by hand: `new Date('2026-09-10')` is UTC midnight and shows
  the day before to anyone west of Greenwich).
- **Layouts** — `layoutSection` of two or three `layoutColumn`s, with
  Confluence's five presets and a per-section width (centred / wide / full)
  reusing the page's own `--page-pad` breakout. Sections stack but never
  nest: `layoutSection` is not in the `block` group and only the document
  admits it (`Document.extend({ content: '(block | layoutSection)+' })`),
  so no panel, expand or column can contain one. The full-width *table*
  breakout was rescoped to direct children of the content root at the same
  time, so a full-width table inside a column fills the column instead of
  bleeding out of it.
- All seven appear in the slash menu and the **+** menu from the one
  `SLASH_ITEMS` catalogue, so neither can drift.

Also in this pass, from live review:

- The **+** insert trigger is a plain "+" sitting with the other toolbar
  icons rather than a labelled button pushed to the right edge, and the
  text-style dropdown reads "Normal text" with no icon — both matching a
  Confluence screenshot the user supplied.
- **Publish/Update and Close moved onto the toolbar row**, out of the bottom
  of the form (`form=` ties the submit button to the form it now sits
  outside of).
- **The breadcrumb moved below the page action bar**, on the reading view as
  well as the editor: the bar is the top edge of the page surface, and the
  breadcrumb belongs with the content. `SpacePage` suppresses its own copy on
  those routes and `PageEditor` / `PageView` render it. Routes with no action
  bar (settings, permissions, webhooks, trash) are unchanged — the breadcrumb
  is already the first thing on the page there.
- **Fixed: floating toolbar menus were transparent.** A regression from the
  one-row toolbar rebuild — `.toolbar` stopped having a surface of its own
  (the page action bar supplies it), so every `.toolbar--bubble` copy of it
  (the selection bubble, the image hover bar, the new layout bar) let page
  content show straight through. They now paint their own background, and
  wrap again on narrow screens.

Verified live at 1000×640: every block created, styled and exported;
heading links scrolled rather than navigated; every space route rendered
exactly one breadcrumb with a clean console; a page created, published,
trashed and permanently purged (sudo re-auth included). 315 backend tests
pass.

### Editor chrome: one-row toolbar with an Insert menu, borderless page (2026-09-10)

*Scope added by the user at the start of Phase 7, modelled on Confluence's
editor.*

The page is a continuous surface: no box, border or shadow around the
body, no rule under the title, body text aligned with the title — in the
editor and the reading view alike. The toolbar runs edge to edge in a
single row and **never wraps**. Text style ("Normal text", "Heading 1"…)
and alignment are dropdowns; block elements — table, image, code block,
quote, divider, the five panels — live behind **+ Insert**, and that menu
is generated from the same catalogue the slash menu uses, so a block added
to one appears in both. When the toolbar's own width (a container query,
not the viewport's) runs short, the "Insert" and text-style labels drop
first, then formatting buttons move into the Insert menu's *Formatting*
section by measurement (`useToolbarOverflow`), losing lists first and bold
last. The link button never collapses: its popover anchors to it.

Two wrong turns on the way, both caught live: `overflow: hidden` as the
no-wrap guarantee clipped every dropdown into invisibility (nowrap alone
is the guarantee), and the overflow priority was applied backwards.

Also: the space settings page's "Use it" button sat below its input for
the same reason the token form's did (card label/button margins); fixed.

### Fix: page editing, trash, permissions and webhooks were broken for signed-in users (2026-09-10)

**A regression shipped with Phase 5 yesterday.** Nesting `ProtectedRoute`
inside the space's route put a bare `<Outlet />` between `SpacePage` and its
gated children, and a bare outlet starts a fresh context of `null` — so the
editor, trash, permissions and webhooks pages all threw on
`useSpaceContext()` and rendered a blank screen. `ProtectedRoute` now
forwards the context it was given, which is what a gate should do.

It went out unverified: Phase 5's live checks covered the anonymous reading
paths and page *viewing*, and never opened the editor as a signed-in user
after the route restructure. Found while opening the new space settings
page, which failed the same way. All five pages verified live after the fix.

### Feature: space icons (dev-plan 6) (2026-09-10)

Every space now has an icon: an uploaded picture, an emoji, or — the
default — its key's first letter on a tile coloured by a stable hash of the
key, so nothing is ever iconless. Rounded squares, where avatars are
circles: at tile size that shape is the only thing distinguishing a place
from a person. Rendered in the spaces list (including the public listing),
the sidebar head, the breadcrumb and the mobile action bar.

Pictures reuse the avatar pipeline unchanged — re-encoded to a 256px WebP,
EXIF stripped, SVG refused — and can only be set through the upload route,
never the JSON update, which would otherwise let a space be pointed at an
arbitrary stored key. Switching away from a picture deletes it rather than
orphaning the bytes. Reading an icon follows the space's own visibility, so
a private space's icon is 404 to anyone who cannot see the space, and a
public space's icon is readable with no account.

Emoji are validated by shape rather than against a list: short, no control
characters, at least one non-ASCII character. A list would go stale every
Unicode release; this admits future emoji and keycaps and refuses prose and
markup.

**`/spaces/{key}/settings` is new** — `PUT /api/spaces/{key}` had existed
since Phase 2 with nothing in the UI reaching it, so the icon picker gave
the name and description form a home at last.

Tests (fifteen): the generated default, emoji round-tripping through the
listing, four emoji shapes accepted and five kinds of prose refused, upload
re-encoded and served as WebP, switching away clears the picture, SVG
refused, pictures refused through the JSON update, only space
administrators may change any of it, and a public space's icon readable by
anyone while a private one is not. Full suite: 301 passing.

### Feature: public read mode — anonymous access per space (dev-plan 5.1–5.4) (2026-09-09)

*5.1 is a Fable item; 5.2–5.4 are tagged Opus and ran as Fable by user
override. The design (the anonymous principal, masking, what opens and
what stays closed, caching, discovery) is in architecture.md and was
written before the code; the leak matrix was written before the routes
were opened.*

A space can now be **published**: anyone can read it, no account needed —
the game-wiki case. Two switches must both be on: the instance-wide
**Allow public spaces** (Settings; also the 3.3 kill switch) and the
space's own flag, set by a site administrator from Admin → Spaces
(sudo mode, audited, always a security alert in both directions, refused
while the instance switch is off). Withdrawing keeps the flag so
re-enabling the instance restores the previous state.

An anonymous request has exactly one capability: reading a public space's
*current, unrestricted* pages. Any restriction anywhere in a page's
ancestry hides it — "not for everyone" now includes the internet. Drafts,
trash, private spaces and archived spaces are 404, never 403. Opened to
anonymous readers, each still permission-checked: space, spaces list
(public only), page tree, page, labels on a page, attachments (list and
download), search (scoped to public spaces), export, and comments only
where the space allows them (read-only). Closed: version history, drafts,
trash, the user directory, groups, labels across spaces, watches, collab,
avatars, notifications, everything that writes.

Anonymous page reads carry `Cache-Control: public, max-age=60` and an
ETag (304 on match), so unpublishing takes effect within a minute; signed-
in reads are `no-store`. Views are counted with no user. `robots.txt`
allows public space paths and disallows `/api`; `sitemap.xml` lists
public pages; a public page URL gets its title, description and Open
Graph tags injected into the SPA shell for link previews — decided as
the anonymous principal whoever asks.

The SPA renders for anonymous readers: brand, search, theme menu and a
**Sign in** button (which returns them to the page they were on); no
edit, new, watch, reorder or comment controls; the page bar collapses to
Export. The spaces index lists public spaces only; a **public** badge is
shown to everyone so nobody edits a public page thinking it is internal.
Admin → Spaces gains a Public column with Publish/Withdraw (the
confirmation names the page and attachment counts that become visible)
and a per-space comments checkbox.

Tests: the nine-test leak matrix (`PublicReadTests`), plus three existing
tests re-pointed at routes that stay closed.

### Feature: security alerts and notifications by email (dev-plan 4.3) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Notifications are now an outbox: the request that causes one writes the
row, and `NotificationEmailService` picks it up within a minute, so no
request ever waits on a mail server. **Security alerts reach every
administrator by email immediately, whatever their preference** — the
"email the admin group" requirement from Phase 3, now met. For their own
notifications, each person chooses on the profile page: Off (the
default), Immediately (one email per pass, with links), or Daily digest
(one email a day when something changed). A dead mail server produces one
audited failure per notification, not one a minute; nothing older than a
day is sent, so turning email on does not flood inboxes with history.

Tests (five): alerts to admins regardless of preference and only once;
immediate subscribers get links; digest subscribers get one message a
day; Off keeps the in-app copy and sends nothing; with email off the
outbox is left untouched.

### Feature: password recovery by email (dev-plan 4.2) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

`POST /api/auth/recover/email` emails a one-time reset link — 32 random
bytes, stored hashed, one hour, single-use, and a newer request kills the
older link. It answers **202 with the same body every time**: whether the
address has an account, whether email is on, whether the send worked —
none of it is told to the caller, who may be probing. Throttled per
address and per client. The reset page offers "Email me a link" only when
the instance sends email, with the recovery-code form one click away.

Tests (five): link arrives and resets once; unknown address gets an
identical answer and no email; email off sends nothing and is not offered;
a newer link invalidates the older; throttling.

### Feature: outbound email (dev-plan 4.1) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

`SmtpEmailSender` (MailKit) sends from the SMTP settings an administrator
fills in — no environment variables, no restart. Plain text is the
message; a minimal HTML twin adds line breaks and clickable links, nothing
else. With **Send email** off, or the settings incomplete, nothing is
attempted and the result says why; a delivery failure is an `email.failed`
audit entry (recipient and subject, never the body). Settings gained the
**Send email** toggle, **Send test email to me**, and a **Public address**
override for the links email will carry (default `https://$DOMAIN`).

Tests use a recording sender; five cover the test-email path, a refused
send, the real sender declining when off, the HTML twin, and the URL
resolution.

### Fix: creation forms inherited the editor panel's icon gutter; admin refusal is now a message (2026-09-09)

The layout's boxed-form class was `.panel` — the same class the editor's
Confluence-style panel node renders as (`.panel.panel--info`, stored in
page content). Every creation form (API tokens, groups, webhooks, spaces,
save-as-template) was getting that node's 2.75rem icon gutter, which is
why the token button looked misaligned. The layout class is now `.card`;
the editor's name cannot change without a content migration. The
one-field-one-button forms also get a two-column grid so the button sits
on the input's baseline instead of wrapping.

A member who reaches any `/admin` URL now sees a page saying the area is
for administrators and to ask one for the change or the role, instead of
being bounced to the spaces list. The server was already refusing.

### Navigation: Groups and Audit under Admin, API tokens under Profile (2026-09-09)

Groups and the audit log are instance administration and now live as
tabs under Admin (`/admin/groups`, `/admin/audit`); API tokens are personal
credentials and now sit on the profile page next to sessions and
two-factor. The old URLs redirect. The top bar for a member is just
Spaces; the More menu no longer renders when it would be empty.

The move came with the server rule it implied. Any signed-in user could
create or delete groups and change memberships, and could read the whole
audit log. Group *listing* stays open — the permission picker needs it —
but creating, editing, deleting and membership changes are administrator
operations, as is reading the audit log (administrators still do not
bypass space permissions, so the log is filtered for them too). Tests
updated accordingly; the "audit entries do not leak titles from
inaccessible spaces" test now proves it for a second administrator.

Also: tables inside profile/admin cards scroll within the card instead of
spilling past its edge, and time/address cells no longer wrap.

### Security: threat model, review, internet-readiness checklist (dev-plan 3.7) (2026-09-09)

`docs/security.md` is the page an operator reads before exposing an
instance: who attacks a self-hosted wiki and why; a table of what each
Phase 3 layer defends and — as importantly — what it does not; the known
gaps and accepted trade-offs, numbered so nobody rediscovers them as
surprises; and the internet-readiness checklist (`.env` values, ports,
accounts, operations) that Phase 5's public-spaces switch will link to.
`SECURITY.md` at the root is the disclosure path.

The review pass (manual; no review skill is available here) walked every
registered route: all `/api` routes require authorization except health,
register, both sign-in steps, recovery and OIDC status/login, each of
which is rate-limited where it takes a credential; the OIDC `returnUrl`
accepts only same-origin paths; invite and reset tokens are returned once
and never listed; the SMTP password is never returned. Nine findings
survived as documented gaps (version in `/api/health`, `img-src https:`,
unaudited registration, in-process counters, mutable alerts, reusable
TOTP challenge, trust-by-private-range, no email yet, dual-role recovery
codes).

### Security: dependency hygiene (dev-plan 3.6) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

`npm audit` on the SPA went from 38 findings (3 high) to zero:
`react-router-dom` 7.18.1 → 7.18.3 (the RSC CSRF advisory; patch-level, no
API change) and every `@tiptap/*` package 3.28.0 → 3.31.3 (a prototype-
pollution and a ReDoS advisory in `@tiptap/core`). The TipTap packages
peer-depend on each other at exact versions, so `npm audit fix` cannot
move them on its own — all thirteen ranges were raised together. Editor
verified live afterwards: toolbar, live collaboration, lowlight code
blocks, no console errors. One thing to know: `npm audit fix --omit=dev`
prunes devDependencies from `node_modules`; run a plain `npm install`
after it.

The collab sidecar now has a `package-lock.json` (zero findings) and its
image builds with `npm ci`, so it is reproducible and auditable. `.NET`
was already clean.

`scripts/audit.sh` runs all three audits (web, collab, .NET with
transitives) and exits non-zero on any finding — the release gate.
`.github/dependabot.yml` groups weekly updates per ecosystem; security
updates arrive ungrouped.

### Security: sessions, two-factor sign-in, sudo mode, pinned Argon2 (dev-plan 3.5) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Every sign-in is now a `UserSession` row the cookie points at, so one
browser can be signed out without signing out all of them. Profile →
Sessions lists them (address, browser, last activity) with revoke;
sign-out revokes the row so a copied cookie dies; sessions expire after
14 days idle and 90 days regardless. Cookies from before this change are
rejected once — the same safe direction as the security stamp.

**Two-factor sign-in** with any authenticator app: scan a QR (or type the
key), confirm with a code, done — the recovery codes from registration are
the backup, so there is nothing new to save. Sign-in becomes two steps
for enrolled accounts; a recovery code works in the code's place and is
spent. A code cannot be used twice. Enabling signs every other device
out. Wrong codes count toward the lockout. The admin **Require two-factor
for administrators** switch is now enforced: an un-enrolled admin gets 403
on every admin route, is told why, and cannot turn TOTP off while the
rule stands.

**Sudo mode**: changing who is an admin, flipping the public-spaces
switch, purging a page or removing a block re-asks for the password (or a
code) unless the session signed in within the last five minutes. The SPA
handles it transparently — a dialog appears, the action retries.

Argon2id parameters are pinned (64 MiB, 3 passes, 4 lanes) and an older,
weaker hash is upgraded in place at the next successful sign-in.

Tests (eleven): per-session revoke, sign-out kills the cookie, absolute
lifetime, two-step sign-in with reuse refused, recovery code in place of
the authenticator, enabling signs others out, disabling needs a
credential, admins forced to enrol, sudo refusal and re-auth, re-auth
extends the window, hash upgrade on sign-in. Full suite: 262 passing.

### Security: SSRF guard, attachment types, CSRF header (dev-plan 3.4) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Webhooks could target any URL the server could reach — the cloud metadata
address, the database, the collab sidecar. `EgressGuard` now refuses
private, link-local, loopback and reserved addresses, local host names,
credentials in URLs and non-http schemes, at two moments: when the webhook
is saved (checking every address the name resolves to) and again inside
the socket connect at delivery, so a name that changed its mind since
(DNS rebinding) is refused on the wire. Redirects are followed by hand,
three at most, each hop checked. `Egress:AllowedNetworks` opens a private
range deliberately. A refused attempt is still a security event.

Uploaded files are served as what their bytes say (PNG, JPEG, GIF, WebP,
PDF signatures win over the label); anything a browser might execute —
HTML, SVG, XML, scripts, or bytes that look like markup — is stored and
served as `application/octet-stream`. Downloads keep `Content-Disposition:
attachment` and `nosniff`.

Every state-changing `/api` request authenticated by the session cookie
must carry `X-Requested-With: Tesria`; the SPA sends it everywhere,
including the two multipart uploads. Bearer-token callers and sign-in are
exempt. Kestrel's body limit is set to 100 MB to match Caddy.

Tests (twenty-five, including theories): twelve refused targets, the
allow-list, the connect-time refusal, webhook creation refused and
recorded, six content-type decisions, an uploaded HTML file downloading
opaque with `nosniff`, and the CSRF header required / exempt / not needed
at sign-in. Verified live: cookie POST without the header → 403 with an
explanatory body; with it → 200; the SPA still performs state changes.

### Security: threat detection, admin alerts, blocklist (dev-plan 3.3) (2026-09-09)

*Plan tag: Fable → Opus. Both halves run as Fable by user override. The
design (signals, thresholds, alert lifecycle, what it deliberately does
not do) is in architecture.md and was written before the code.*

Thirteen detectors now watch the instance: failed-sign-in bursts and
credential stuffing per address, repeated lockouts per account, an
administrator signing in from an address never seen for that account, a
spike of 401/403s, mass page removal, API-token minting bursts,
registration bursts, every admin promotion, every flip of the
public-spaces switch, a webhook aimed at a private address, and a broken
audit chain. Bursts write one event at the threshold with the count, not
one per hit; discrete signals write every time. An alert-worthy event
creates a `SecurityAlert` (Open → Acknowledged → Resolved, with a note)
and one notification per administrator, with a one-hour cooldown per
(kind, key) so an ongoing attack is one alert an hour, not one a second.

`SecurityEvents` is append-only at the database layer — added to the
runtime role's revoke list — so the record of an attack cannot be tidied
away by the app. Alerts live in their own table precisely so acknowledging
one never needs an UPDATE on the append-only one.

**Admin → Security** now has an overview strip, the open alerts with
one-click mitigations relevant to each (block the address for 24 h, sign
the account out everywhere, revoke its tokens, suspend it), kill switches
(public spaces, registration, TOTP-for-admins), a blocklist (address or
CIDR, optional expiry, refuses to block your own address), and a recent
events timeline. The blocklist is enforced by middleware that runs right
after forwarded headers and before authentication: a blocked address gets
403 with or without a valid session. The bell links security alerts to
the page.

Tests (fourteen): each burst detector fires at its threshold and not one
below; the cooldown suppresses repeats; admins are notified; the
first-ever admin sign-in does not alert but a later new address does;
promotion and the public toggle always alert; a private webhook target
alerts; a tampered chain becomes a Critical alert through the daily
monitor; acknowledge/resolve; the blocklist refuses before auth and lifts
on removal; you cannot block yourself; members get 403 on all of it.
Verified live: five failures against distinct accounts from one host
produced a Critical credential-stuffing alert with the real client
address, a bell notification, an alert card with "Block <address>", and a
working Acknowledge; `DELETE FROM "SecurityEvents"` as `tesria_app` →
`permission denied`.

### Security: rate limiting and account lockout (dev-plan 3.2) (2026-09-09)

*Plan tag: Opus. Run as Fable by user override.*

Online brute force against `/api/auth/login` was unlimited. Now: a sliding
window per client address on sign-in, registration and recovery (default
10/min); a per-account lockout after 5 consecutive failures, doubling from
60 s up to 15 min and never permanent; a per-account limit on API-token
minting (20/h); and a global per-address limit for callers with no session
(300/min) — the one Phase 5's public-read mode will lean on. Signed-in
users are not globally limited. 429s carry `Retry-After`.

A locked account gets the same empty 401 as a wrong password, even with the
right one, and the right password does not reset the counter while locked.
Success, recovery, or an admin unlock does. Failure counts persist on the
user row, so a restart is not a fresh budget.

All six limits are site settings, editable on the new **Admin → Security**
page, which also lists active lockouts with one-click unlock and hosts
3.1's "Verify now" for the audit chain. Users shows a `locked` badge.

Tests (ten) drive every limiter through spoofed proxy addresses — including
the one that proves two addresses no longer share a bucket, which is what
3.0 was for. Verified live: ten wrong sign-ins from one host → 401 ×10 then
429 with `Retry-After: 60`; Security page renders limits and a passing
chain verification.

### Security: least-privilege database role and audit hash chain (dev-plan 3.1) (2026-09-09)

The app no longer runs as the Postgres superuser. At startup it uses the
owner connection once — unpooled — to migrate, then creates `tesria_app`
(`APP_DB_PASSWORD`) with read/write on everything except `UPDATE`/`DELETE`
on `AuditLogs` and `PageViews`, and runs as that role from then on; so
does the collab sidecar. Provisioned by the app rather than an init script
so existing installs get it too, and re-granted every start so tables from
future migrations are covered. Empty `APP_DB_PASSWORD` falls back to the
owner with a warning rather than refusing to start.

Every audit row is now a link in a SHA-256 hash chain (`Sequence`,
`PrevHash`, `Hash`), linked inside `SaveChanges` under a Postgres advisory
lock so no code path can write an unchained row and no two writers can take
the same position. The 36 rows written before this existed were linked at
first start. `POST /api/admin/audit/verify`, `scripts/verify-audit-chain.sh`
and a daily in-process monitor walk the chain and name the first broken
link — an altered row, a missing row, or (monitor only) a chain shorter
than last time. Every row is also written to stdout as JSON under the
`Tesria.Audit` log category as it commits: a copy the database password
cannot reach.

Two round-trip hazards found by verifying against the real database: jsonb
re-orders keys and normalises numbers, and `timestamptz` keeps microseconds
where .NET keeps ticks. Hashing is over a canonical form that survives
both, and all 36 stored hashes were recomputed independently in Python
from a `psql` dump to prove it. Live: `UPDATE "AuditLogs"` as `tesria_app`
→ `permission denied`.

Tests (six): contiguous sequences and a passing verify; an altered row is
named; a deleted row is named as a gap at its successor; legacy rows are
backfilled; canonical JSON is order/whitespace/number-spelling insensitive;
members cannot verify.

### Security: proxy trust, secure cookies, security headers (dev-plan 3.0) (2026-09-09)

*Plan tag: Opus. Run as Fable at the user's request — Phase 3 is security
work and the user chose to spend the larger model on all of it.*

The app now knows who the client is. `UseForwardedHeaders` runs first in
the pipeline and believes `X-Forwarded-For` / `X-Forwarded-Proto` from the
compose network's private ranges only (`Proxy:TrustedNetworks`), taking
only the nearest hop so a client cannot pick its own address by sending the
header. Before this, every request carried Caddy's container address — the
finding that made 1.3's recovery limiter key on email instead of IP, and
that would have made any per-IP limiter throttle everyone at once.

The session cookie is `Secure` unconditionally in Production
(`Security:AllowInsecureCookies` opts out, documented as unsafe). Every
response carries `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`,
`Permissions-Policy`, COOP/CORP and a Content-Security-Policy whose
`script-src` is `'self'` plus a hash of the inline theme script, computed
at startup from the `index.html` this process serves. `style-src` allows
inline styles on purpose — the editor writes them — and that trade-off is
recorded in the architecture doc.

`deploy/Caddyfile.public` is the internet-facing configuration: HSTS on,
the on-demand-TLS catch-all gone. Selected with `CADDYFILE=` in `.env`.

Tests (six) act as the proxy and as a stranger through a startup filter
that sets the connection address: forwarded address honoured from loopback,
ignored from a public address, only the last hop believed, the cookie
turns `Secure` when the proxy says HTTPS, and the headers are on every
response. Verified live: headers present, collaboration websocket connects
under the CSP, inline theme script runs under its hash, no CSP refusals on
spaces, page view, editor or admin.

### Feature: recovery codes for existing accounts (dev-plan 1.3) (2026-09-09)

Recovery codes were only ever issued at registration, so every account that
predates them — both accounts on this instance — had none and no way back in
if its password were lost. They are now offered at sign-in: an account with
zero codes gets a dialog, and one click generates a set.

**No password is asked for right after signing in.** The sign-in is the
re-authentication; asking for the same password seconds after it was typed
proves nothing and mostly trains people to type passwords into prompts. The
window is 15 minutes (`Auth:FreshLoginMinutes`, overridable), carried in an
`auth_time` cookie claim. Editing your profile re-issues the cookie with the
*original* `auth_time`, so ordinary activity cannot extend the window.

Three rules the tests pin down:

* **A supplied password is always verified**, fresh session or not. Accepting a
  wrong one because the session happens to be recent would tell someone their
  password was right when it was not.
* **Outside the window the password is required**, so a tab left open on a
  shared machine cannot mint codes that work as a permanent password reset.
* **Omitting it is the supported path only while fresh.**

Two bugs found by running it rather than by building it:

* The dismissal was stored per tab, not per user, so one person clicking
  "Not now" silenced the prompt for whoever signed in next in the same tab.
* Refreshing the profile after generating took the remaining count off zero,
  which closed the dialog — destroying the only copy of the codes that had
  just been generated. The dialog now stays open whenever codes are on screen.

A stale session used to hit a dead end telling it to go to the profile page; it
now asks for the password in the dialog. Verified live end to end: prompt after
sign-in, one-click generation, codes displayed with download/copy, and the
prompt gone afterwards.

### Feature: admin panel (dev-plan 2.1–2.5) (2026-09-09)

`/admin`, visible only to administrators, with Dashboard, Users, Spaces,
Invites and Settings. The role check in the UI is convenience — every
`/api/admin/*` route enforces it server-side, and there is a test asserting a
member gets 403 on each.

**Users** shows role, status, recovery-code count, last-seen and avatar, with
promote/demote, suspend/reactivate, revoke-sessions, revoke-tokens and issue-
reset. Three guards matter more than the listing:

* **The last administrator cannot be demoted or suspended.** An instance with
  no admin has no way back — nobody could change settings, issue invites or
  restore access without editing the database by hand.
* **You cannot suspend yourself**, checked before the last-admin rule so the
  message is the accurate one.
* **Suspension rotates the security stamp**, so existing sessions die on their
  next request rather than lingering until the cookie expires. Verified live.

**Sessions and tokens revoke separately, deliberately.** A token authenticates
through a different scheme and a session revocation does not touch it, so
"lock this account out" needs both — there is a test proving the token still
works after sessions are revoked, and stops after tokens are.

**Spaces** is metadata only — key, owner, page count, storage, archived state.
Admins do not bypass space permissions, so this must not become a way around
that; a test asserts no page content appears in the response.

**Dashboard** is one aggregate endpoint rather than a page firing a dozen
requests: the counts are cheap but the round trips are not, and a single
response means the whole dashboard is consistent with itself rather than
assembled from twelve different instants. Range is clamped to 1–365 days.

Charts are hand-rolled SVG sparklines — no charting dependency added, since
the bundle is already 1.1 MB. Written against the `dataviz` skill: one series
means no legend and no categorical palette, colour is a single token
(`--primary`, or `--danger` for failed sign-ins, which is a status signal
rather than another series), text wears text tokens rather than the series
colour, marks are 2px with a surface ring on the hover marker, and every
sparkline has a hover crosshair reading out the exact day and value.

**Empty days are included in every series.** A sparkline built only from days
with activity silently compresses a quiet week into one point and reads as
steady use when the truth is the opposite.

**Failed sign-ins are on the front page on purpose:** a spike there is the
first visible sign of a brute-force attempt. Dev-plan 3.3 turns it into an
alert; until then somebody has to be able to see it.

Nine tests in `AdminPanelTests` (187 total). Verified live against real data:
23 pages, 15 recorded views, top page "Tesria REST API", 30 daily points per
series.

### Feature: author identity on comments and version history (dev-plan 1.5) (2026-09-09)

Comments and version history both returned a bare `AuthorId` and rendered no
author at all — a threaded discussion where you could not tell who said what,
and a history that could not answer "who changed this?" despite storing the
answer since Phase 1. Both now carry `AuthorName`, `AuthorAvatarHash` and
`AuthorAvatarVariant`, projected from the joined `User`.

Projected server-side rather than resolved by the client: a per-comment lookup
is N round trips, and a client-side directory fetch would hand the whole user
list to anyone who can read one page.

This completes the render list 1.2 could not finish — avatars now appear in
comments and version history as well as the topbar and profile.

Three tests (178 total). One of them changed shape during writing: the
"deleted author" case cannot be reached by orphaning a comment, because the
foreign key forbids it. The `?? "Deleted user"` in the projection is therefore
defensive only, and the test now covers what 2.2 will actually do — anonymise
the row, keeping the id so history stays attributable while the name no longer
identifies anyone.

**One bug worth recording.** The first version set `comment.Author` to a
detached `User` so the create response would carry the name. EF treats a
detached entity on a navigation as new and tried to INSERT it, tripping the
unique-email constraint and breaking six existing comment tests. Re-reading the
comment with the author joined is one extra query on create and is obviously
correct; the update path needed the same include.

### Feature: closed registration and invites (dev-plan 1.4) (2026-09-09)

`AllowPublicRegistration` was already enforced in 0.2; this adds the other way
in. `POST /api/admin/invites` mints a single-use registration link an
administrator hands over however they already communicate — the only way to
add a user to a closed instance with no email server.

An invite may be bound to an address. A forwarded link should not become a
registration for whoever received it, so a bound invite refuses any other
address; leaving it blank is the deliberate "give this to whoever needs it"
case. Invites expire (7 days by default, 1–90 configurable), are revocable,
and are spent **inside the same transaction that creates the account**, so a
failure part-way cannot burn an invite without producing a user.

Registration accepts `?invite=` from the query string, so the person following
a link never has to know a token exists. This closed a real gap: until now
anyone who could reach `/register` could create an account, which sat awkwardly
against the brand page's "control who sees what".

Seven tests in `InviteTests` (175 total).

**Two SQLite provider limitations hit while building 1.3 and 1.4**, both the
same shape and both already known to the codebase: the test provider cannot
translate a `DateTimeOffset` comparison or use one in `ORDER BY`. Reset-token
expiry is now compared in memory, and the invite list is ordered in memory.
`AuditEndpoints` had already worked around the ordering case by branching on
provider; these sets are small enough that ordering client-side unconditionally
is simpler than a branch.

### Feature: offline password recovery (dev-plan 1.3) (2026-09-09)

Eight single-use recovery codes minted at registration and shown exactly once,
plus an administrator-issued reset link. Neither needs email, which on a
self-hosted instance is usually not configured at all.

**Codes are SHA-256, not Argon2id — a deliberate departure from the plan.**
Argon2's cost exists to make guessing a *low-entropy* secret expensive; these
are 60 bits of cryptographic randomness, where a fast hash is already
unguessable. Argon2 would instead mean up to eight deliberately-slow
verifications per attempt: bad for the user and a free denial-of-service lever.
This matches how `ApiToken` already stores its secret, including the
fixed-time comparison.

**The rate limiter is keyed on email, not IP — also a departure.** Per-IP is
the obvious choice and is wrong today: every request arrives with Caddy's
container address, so an IP limiter would throttle the whole world as one
caller. Keying on the supplied email bounds guesses against any one account,
which is the actual threat, and is immune to the proxy problem. Dev-plan 3.2
adds real per-IP limiting once 3.0 makes client addresses real.

Codes normalise on redemption — dashes and case are stripped — because they get
written on paper and typed back months later. The alphabet omits O/0, I/1/L and
U for the same reason. Every failure returns an identical response whether the
account exists, the code is wrong, or the account is SSO-only, so this cannot
be used to discover which addresses have accounts.

Both recovery paths rotate the security stamp, signing out every existing
session. Recovery exists for when somebody else has your account; leaving their
session alive would defeat it.

`POST /api/admin/users/{id}/reset-password` issues a one-time, one-hour link an
admin hands over out of band. It deliberately does not set a password: an
administrator should be able to restore access without ever knowing the
resulting credential. Issuing a second link invalidates the first.

Frontend: codes are shown after registration behind an explicit "I have saved
these" gate with copy and download — the registration redirect had to be held
back, since it otherwise fired the moment the account existed and skipped past
them. A "Forgot your password?" link on sign-in reaches `/recover`; `/reset?
token=…` is the admin link. The profile page shows how many codes remain and
warns when there are none — which is every account created before this shipped,
verified live on this instance.

Twelve tests in `AccountRecoveryTests` (168 total).

### Feature: avatars (dev-plan 1.2) (2026-09-09)

Every user has an avatar from the moment they register, with nothing stored:
an inline SVG of their initials on one of twelve backgrounds, picked by an
FNV-1a hash of their id. Not a char-code sum — user ids are hex GUIDs, sharing
an alphabet and a length, which is exactly where a weak hash clusters. All
twelve carry white text at 4.5:1 or better (measured, not judged), and all are
dark enough to read on both page grounds, so no per-theme treatment is needed.

`User.AvatarVariant` records an explicit pick; null derives one from the id.
Stored as an index rather than a colour so the set can be restyled without
rewriting rows, and kept when a picture is uploaded — so removing the picture
returns to the colour the user chose, not to the derived one.

Uploads are cropped square in the browser before sending, via
`createImageBitmap`, which decodes off the main thread and honours EXIF
orientation — without it a portrait phone photo arrives sideways. The crop is
not cosmetic: the server centre-crops too, so doing it here is what makes the
stored result match what the user was shown. Downscaled to 512px first, so a
12MP photo is not uploaded whole to produce a 256px thumbnail.

Verified live: the generated avatar rendered "AB" on the emerald variant
derived from the account id, picking swatch 3 persisted server-side and
re-rendered both the profile and topbar avatars in `#5b47ba`, and clearing it
returned to the derived one.

**Where avatars appear:** topbar and profile. Comments and version history
return only an `AuthorId` and render no author identity at all today, so
avatars there wait until they show names — adding names was outside this item.
`GET /api/users` does now carry `avatarHash`/`avatarVariant`, for the admin
users list in 2.2; the existing people picker is a `<select>`, whose options
cannot contain markup, so it cannot show them.

Three tests added (158 total), including that an uploaded picture wins over a
chosen variant while preserving it for when the picture is removed.

### Feature: edit your own profile (dev-plan 1.1) (2026-09-09)

A `/profile` page, reachable from the username in the topbar, with three
independently-submitting sections: display name, email address (requires the
current password, refuses a collision with 409) and password (requires the
current one, enforces the same minimum as registration from the same
constant). One combined form would either demand a password to rename
yourself or skip the check that protects the other two.

**The load-bearing part is `User.SecurityStamp`.** A cookie scheme is
stateless — the cookie *is* the proof — so nothing on the server can normally
take it back before it expires. The stamp is issued into the cookie as a claim
and compared against the stored column in `OnValidatePrincipal` on every
request, so rotating it invalidates every outstanding cookie for that account
on its next request. Changing a password rotates it, and the session that made
the change is re-issued with the new value so that person is not signed out
along with everyone else. Suspension (2.2), admin force-logout (3.3) and 2FA
enrolment (3.5) all reuse this rather than adding their own mechanism — the
same validation already rejects a cookie whose account has become suspended,
with a test proving it.

**Everyone is signed in once on deploy.** Cookies issued before the stamp
existed carry no claim and are rejected. That is the safe direction: treating
a missing claim as valid would mean a pre-existing cookie outliving the
password change meant to kill it. Verified on the running stack — the live
session was signed out on the first request after deploying.

**API tokens are deliberately unaffected**: they authenticate through a
different scheme and carry no cookie, so a password change does not revoke
them. A script's credential should not die because its owner rotated a
password, but it must be independently revocable, which it already is.

OIDC-provisioned accounts (no local password) can change their display name
but not their email or password — the identity provider owns those. The server
refuses with a message the UI shows, and the UI renders those sections
read-only rather than letting a submit fail.

Nine tests in `ProfileTests`, including that a second live session dies on its
next request while the one that changed the password keeps working.

### Feature: profile media storage (dev-plan 0.4) (2026-09-09)

Avatars stored through the existing `IAttachmentStorage` under their own key
namespace, so the S3 implementation that interface reserves a slot for will
cover them too. Keys are deterministic per owner, so a replacement overwrites
rather than accumulating orphans.

**Every upload is re-encoded to a fixed 256px WebP square, and that is the
security control rather than a convenience.** The stored bytes are always ones
this process produced: EXIF (often GPS) is stripped, polyglot files stop being
polyglot, and decoded dimensions are bounded — checked from the codec header
before any pixel buffer is allocated, so a decompression bomb is refused
rather than decoded first. **SVG is rejected by sniffing the bytes**, not by
trusting the declared content type, so an SVG labelled `image/png` does not
get through; there is a test for each of those framings.

**SkiaSharp (MIT) rather than ImageSharp**, because ImageSharp 3.x moved to
the Six Labors Split Licence and dev-plan 8.2 intends an Apache 2.0 release.
Verified in the runtime container and not only on the build host, since the
native-asset variant is exactly the thing that differs between them: the
container is glibc 2.39 and the package ships a matching `linux-arm64` build.
A real 101 KB 600×400 PNG uploaded to the running stack came back as a 1.2 KB
256×256 WebP.

Cache busting is a content hash on the URL, stored as `User.AvatarHash` so
serving costs no hashing and the SPA can build the URL from the session it
already has. Each version is its own URL, so the response can be cached
indefinitely without going stale.

**Scope note:** 0.4 also carries the avatar upload/delete endpoints, because
the validation, size cap and re-encode it specifies have no other home — a
storage pipeline with no way in cannot be tested end to end. Dev-plan 1.2 is
therefore the profile UI, the client-side crop, and the generated default
avatars, not the server half.

Eight tests in `ProfileMediaTests`, including that a user with no avatar and
an unknown user id are indistinguishable, so the endpoint cannot be used to
probe which ids exist.

### Feature: usage telemetry (dev-plan 0.3) (2026-09-09)

Three signals, recorded now so the admin dashboard (2.5) ships with real
history instead of an empty chart.

**`User.LastSeenAt`**, stamped by middleware after authorization and throttled
to one write per user per five minutes — "active in the last 7 days" needs
coarse resolution, so a write per request would be a lot of work for almost no
information. Update by primary key, no prior read, failures swallowed.

**Login events.** `user.login` is attributed to the account; `user.login_failed`
is attributed to nobody and records neither the user id nor the attempted
address. An audit log every admin can read should not become a list of
addresses somebody guessed, nor confirm which ones exist. This needed
`IAuditLogger.RecordAs(actorId, …)`, since sign-in happens before the request
has a principal.

**`PageView`**, one row per read, after the permission check so a refused read
is never counted, and browser sessions only — an API token is a script, and a
nightly export would otherwise dwarf every human in "most viewed pages". The
token test is the same one the Smart policy scheme uses, so the two cannot
disagree. `UserId` is nullable from day one because public read mode (Phase 5)
writes anonymous views into this table, and widening the column later would be
a migration on a table that is large by then.

**It also made a Phase 3 finding concrete.** Exercising login on the running
stack logged `172.18.0.7` for three requests from two different clients —
Caddy's container address, not the callers'. That is the forwarded-headers gap
in the security baseline, now demonstrated rather than inferred. The IP is
recorded anyway so the history exists and becomes correct when 3.0 ships;
`architecture.md` says plainly not to build per-IP logic on it before then.

Six tests in `TelemetryTests`, including that a token read and a refused read
are both uncounted, and that last-seen throttling is per user rather than
global.

### Feature: instance settings (dev-plan 0.2) (2026-09-09)

A single typed `SiteSettings` row an admin edits at runtime via
`GET`/`PUT /api/admin/settings`: instance name, `AllowPublicRegistration`,
`AllowPublicSpaces` (the Phase 5 kill switch, off by default), SMTP
host/port/username/password/from-address/TLS mode, `EmailEnabled`, and
`RequireTotpForAdmins`. Typed columns rather than key/value so EF validates
them and every shape change is a migration.

The **SMTP password is write-only over the API**: it is encrypted with the
Data Protection keys already stored in this database, responses carry only
`smtpPasswordSet`, and audit entries name which fields changed but never the
value. Every field on the update is optional — an omitted field keeps its
stored value, so changing one setting cannot clobber the rest — with one
addition for the password, where an empty string means "clear it", which
`null` cannot express.

Registration now honours `AllowPublicRegistration`, **except for the very
first account on an empty instance**. Otherwise an operator who closes
registration before anyone has signed up could never set the instance up.
There is a test for each half of that.

The cache is a singleton with a 30-second TTL while the service is scoped, so
a hot path like registration can read settings on every request. Invalidation
is in-process, which is a single-instance assumption — the short TTL bounds
how stale a second replica could get, and that is written down in
`architecture.md` rather than left implicit.

Eight tests in `SiteSettingsTests`, including that reopening registration
takes effect immediately (proving invalidate-on-save, not TTL expiry) and
that the password never appears in a response, in the stored column, or in
the audit log.

### Feature: instance roles and administrators (dev-plan 0.1) (2026-09-08)

`User.Role` (`Member = 0 | Admin = 1`) — an enum rather than a bool, so a
future Viewer/Moderator is a new value, not a migration. The first account
created on an empty instance is Admin, whether it arrives through `/register`
or OIDC provisioning; the migration backfills existing installs by promoting
the earliest-created account, so no instance is left with content and nobody
able to administer it. Verified on this instance's real data: the owner's
account was promoted, the docs bot was not.

**Admins are not a permission bypass.** This was the design question the plan
left open, and the answer is Confluence's own: a site admin sees exactly what
their grants allow. What the role confers is access to `/api/admin/*` and an
audited `POST /api/admin/spaces/{key}/recover-access`, which writes the
caller an explicit space-admin grant — after which the *existing* rules apply
unchanged, including the one that already lets explicit space admins past
page restrictions. A silent bypass would let any admin read any team's
private space with no trace, would need an "unless admin" branch in every
permission check, and could never be revoked afterwards. Recovery is
idempotent, so a retry is neither a duplicate grant nor a second audit entry.

Registration's "is this the first account?" check and its duplicate-email
check both read the table before writing, so they now share one serializable
transaction — without it two simultaneous first registrations could each see
an empty table and both become admin.

`CurrentUser.IsAdminAsync()` reads the row rather than trusting a claim: a
role claim would be stale until the user's next sign-in, so a demotion
wouldn't take effect until then. One primary-key lookup, cached per request.

Six tests in `RoleTests`, including the two that pin the decision down: an
admin gets **404** (not 403, per the existing masking rule) on a private
space they hold no grant for, and revoking the recovered grant returns them
to no access.

Spec: `architecture.md` → "Roles and administrators". Written by Fable 5.1
under the plan's model gate, implemented by Opus 5.

### Fix: the topbar is three tiers now, not two (2026-09-08)

Between --bp-mobile and --bp-tablet the bar showed its full desktop layout —
brand, four nav links, a 420px-max search box and the right-hand cluster —
with no wrap fallback. It never actually fit there; it only appeared to
because `.brand` would quietly ellipsis. Adding the logo (a fixed 20px that
cannot ellipsis) and the appearance button used up that slack, and the band
tipped into real horizontal overflow, with "API Tokens" wrapping onto two
lines.

That wrap is also what made the links look top-aligned: a two-line link makes
the nav row taller, and its single-line neighbours then sit at the top of
it. `.topbar__link` is `inline-flex`, centred, and `white-space: nowrap` now,
so it cannot recur — but the real fix is giving the pressure somewhere to go.

**641–1024px is a proper middle tier.** Spaces stays visible; Groups, Audit
and API Tokens move behind a **More ▾** menu whose trigger reads as active
when the current route is one of them; search keeps a
150px floor (unpinned it collapsed to ~2px); and the username — the least
load-bearing thing in the bar — hides. Below 640 the existing hamburger is
unchanged, and its column lists all four links itself, so More hides there.

The secondary links are rendered twice on purpose — flat, and inside More —
with CSS choosing which set shows. That is the same convention the editor
toolbar already uses for its heading/list/alignment groups
(`.toolbar__flat` vs `.toolbar-dropdown`), not an accident.

Measured rather than eyeballed, at 1280 / 1025 / 1024 / 800 / 641 / 480: no
horizontal overflow at any width, every visible link sharing one top edge and
one 27px height, and search at 175px in the worst case (641px). The bar also packs left now (`justify-content: flex-start`, with
`margin-left: auto` on the right-hand cluster) instead of `space-between`.
Space-between split leftover width evenly into every gap, which floated the
nav somewhere between the brand and the search box on desktop and, with the
collapsible out of flow on mobile, parked the brand dead centre. One rule
fixes both: the nav anchors to the brand, the brand to the hamburger, and all
the leftover sits in a single gap before the right-hand cluster — and the
search box, which had a 420px cap (260px in the middle tier), now has none,
so it is what fills that gap and grows with the viewport.

Those numbers
also showed the wordmark-hiding rule added with the logo was now dead weight
— with More absorbing the pressure there is more slack at 641px than the
word needs — so it is gone, and the brand reads "Tesria" at every width.

### Design: Tesria's brand mark in the favicon and topbar (2026-09-08)

Took the layers mark from the brand page at brianintheloop.com/tesria. It
needed no adaptation — the mark is already drawn in the same language as this
app's icon set (24x24 viewBox, 1.8 stroke, round caps and joins,
`currentColor`), so it dropped straight in.

The favicon replaces the scaffold's purple bolt. Because it is an SVG it can
carry its own `prefers-color-scheme` media query, so the mark is brand blue
(`#2496ed`) on a light browser chrome and brightens to `#6cb6f7` on a dark
one; where that isn't supported the plain `stroke` still applies, so the
fallback is the brand blue rather than nothing. Stroke is widened from 1.8 to
2 for the favicon only — at 16px, 1.8 on a 24 viewBox thins to about one
pixel and the middle layer lines start to drop out.

In the topbar the mark sits left of the wordmark and takes `--primary`, so it
follows both the light/dark theme *and* the chosen accent, while the wordmark
stays `--text`. That is the same split the brand page uses: coloured mark,
neutral wordmark.

One detail worth keeping: `.brand`'s shrink-and-ellipsis behaviour (added for
narrow phones, where the topbar has no wrap fallback) moved from the link to
the new `.brand__word` span, and the mark is `flex-shrink: 0`. Otherwise the
logo would have been the first thing squeezed out on a small screen.

Also added light/dark `theme-color` meta tags matching the two `--bg` values,
so mobile browser chrome tracks the app.

**The favicon tracks the accent too.** A favicon is a separate document that
can never read the page's custom properties, so a single themeable SVG is not
possible — the colour has to be baked in per variant. Rather than shipping six
files that would drift from the palette the first time an accent is retuned,
`applyFavicon()` renders the mark to a data URI from `ACCENT_HEX` in theme.ts
and swaps the `<link rel="icon">` href. `public/favicon.svg` stays as the
pre-JS default. Which half of each pair is used follows the *operating system*
rather than the app's theme setting: the icon lives in the browser's tab strip,
so it should match that chrome, not the page — someone running the app in
forced light on a dark desktop still wants the light-on-dark mark in their tabs.
`startFaviconSync()` runs at startup so this applies on every route, including
the sign-in pages where the appearance menu isn't mounted.

One layout consequence, found by measuring rather than by eye: between
--bp-mobile and --bp-tablet the topbar shows the full desktop layout with no
wrap fallback, and already relied on `.brand`'s ellipsis to fit. The mark is a
fixed 20px and cannot ellipsis, so adding it tipped that band into real
horizontal overflow. The wordmark is therefore hidden below --bp-tablet,
leaving the mark alone — which gives back more than the mark costs, and reads
as a deliberate logo-only brand rather than the half-word truncation that
appeared first.

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
