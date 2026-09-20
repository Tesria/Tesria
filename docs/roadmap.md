# Roadmap / future features

Forward-looking ideas that haven't been scheduled or designed yet — distinct
from [`PLAN.md`](../PLAN.md) (the original founding design doc, phases 1–5,
now historical/complete) and [`CHANGELOG.md`](./CHANGELOG.md) (what's
actually shipped). Add to this list as new ideas come up; move an entry to
the CHANGELOG once it's actually built.

> **Status:** the first four entries below were all scheduled in
> [`dev-plan.md`](./dev-plan.md) on 2026-09-08 and are now shipped except
> wiki packs (8.5, deliberately last): MCP (8.4 ✅), the API surface
> (8.3 ✅, as OpenAPI + a reference), Mermaid (7.F ✅). They stay here as
> the idea record. Public read mode (plan Phase 5) is the natural partner
> to wiki packs — build a wiki, export it, host it.
>
> Entries added after that date are **unscheduled**: they are ideas, not
> commitments, and each says what would make it worth doing.

## Known bugs

Reported by the owner, not yet investigated. Fix these before the next
release; each should land with a regression test.

- **Resolve does nothing on a security alert (reported 2026-09-20; cause
  found the same day).** On Administration -> Security, pressing
  **Resolve** has no effect, on acknowledged alerts too. The console says
  `Uncaught Error: prompt() is not supported` from the button's onClick:
  `AdminSecurityPage` opens `window.prompt` for the optional resolution
  note, and where the browser refuses prompts (the in-app browser always,
  and Chrome after someone ticks "prevent this page from creating
  additional dialogs") the handler throws before it ever calls
  `POST /admin/security/alerts/{id}/resolve`. Acknowledge does not prompt,
  which is why only Resolve looks dead.

  The fix is to stop using `window.prompt`: a small dialog with a note
  field and Resolve/Cancel, like the backup policy's confirmation panel,
  or resolve with no note and let the note be added afterwards. The same
  pattern appears in one other place worth checking, the Roles tab's
  "Reset" confirmation, which uses `window.confirm` (supported, but the
  in-app browser auto-dismisses it). Regression test: the endpoint already
  has coverage, so the test belongs in whatever replaces the prompt.

## MCP support

Expose the knowledge base as an [MCP](https://modelcontextprotocol.io) server
so AI agents/tools can query spaces and pages as a context source, not just
humans through the browser. Positions the product as a hub both people and
agents read from.

## API support

Broader/more complete public API surface. Note: a token-authenticated REST
API + webhooks already ships (Phase 5, see `docs/architecture.md`) — clarify
whether this means expanding that existing surface (more endpoints, better
docs, GraphQL, etc.) or something specifically new.

## Mermaid diagrams

Render [Mermaid](https://mermaid.js.org) diagram-as-code blocks in the
editor (flowcharts, sequence diagrams, etc.), alongside the existing
syntax-highlighted code blocks.

## Portable space/site export ("wiki packs")

Export a single space — or the entire site — as one downloadable file that
bundles all its pages, structure, and content, which anyone else can import
to stand up their own fully-populated instance from scratch. E.g. someone
builds out a complete World of Warcraft knowledge wiki, exports it as a
single file, and shares it — anyone else downloads that file and imports it
to get the whole wiki, ready to go, on their own instance.

## Semantic search over the wiki (hybrid retrieval)

Search today is PostgreSQL full-text: lexical, English-stemmed, and very
good at the words a page actually contains. It cannot match meaning.
Measured against the real content on 2026-09-11:

| Query | Hits |
|---|---|
| `authentication` / `authenticating` | 11 each — stemming works |
| `how do I prove who I am` | **0** |
| `credentials for scripts` | **0** |
| `log in` | 4, top hit *"Audit log"* — matched the token, not the meaning |

That gap matters most for the MCP server (8.4), because an assistant
phrases queries as questions rather than keywords.

**The shape.** Embed each page (or chunk) with a small local embedding
model, store the vectors in Postgres via `pgvector`, and run *hybrid*
retrieval — lexical and vector together, merged — rather than replacing
full-text. Lexical stays better for exact terms: error codes, endpoint
names, `page.updated`. An embedding model is not a chat model and talks to
nobody: text in, a vector out, at save time and at query time.

**What it would cost.** A model file of roughly 100–150MB, CPU-only, so
materially smaller than the PDF sidecar already in the stack. `pgvector` is
**not** in the `postgres:18` image, so the database image changes too. The
real ongoing cost is an index that can go stale: re-embedding on save is a
new failure mode the app does not have today.

**Two decisions it cannot dodge**, both of which make this a design task
before an implementation one:

- **Where embeddings come from.** A hosted API contradicts the self-hosted
  posture held everywhere else (a CDN `<script>` was removed from exports
  for less). A local model means another sidecar and a model file in the
  image. This is the decision.
- **Permission filtering.** A single shared vector index leaks across
  permission boundaries, which is exactly what every tool in this codebase
  is careful not to do. It has to keep the established two-pass shape:
  over-fetch candidates, then filter with `IPermissionService`.

**Revisit when** either is true — neither is, as of 2026-09-11:

- the wiki passes roughly **500–1,000 pages**. It has **58**, totalling
  **31 KB** of text: the entire corpus fits in one model context several
  times over, so an assistant can navigate (`get_space_tree`,
  `list_labels`) and read what it needs. Semantic search solves
  needle-in-a-haystack retrieval, and there is no haystack yet;
- or people who do not know the wiki's vocabulary start searching it —
  new joiners, or a public space — where the synonym gap bites regardless
  of size.

Cheaper things that help first are already done (2026-09-11): search
snippets are the matching passage rather than the page's opening,
`get_page` returns a heading outline and can fetch one section, and the
relevance score is exposed.

## Roadmap planner (timeline macro)

A visual timeline block for the editor, modelled on Confluence's
[Roadmap Planner macro](https://confluence.atlassian.com/doc/roadmap-planner-macro-704578202.html):
the thing people reach for when a wiki has to show *when*, not just *what*.

Its four elements, in Confluence's terms:

- **Timeline** — the date axis, shown in days, weeks or months.
- **Lanes** — horizontal rows separating teams, products or workstreams.
- **Bars** — a phase or block of work in a lane, with text, colour, start
  and end dates, an optional description, and an optional link to a page.
- **Markers** — vertical lines calling out a significant date.

Notable in the original: everything is edited **directly on the diagram**
(drag to move or resize, click to edit inline) rather than through a
configuration dialog — Atlassian's own documentation says the macro "does
not use the macro browser to set parameters" and cannot be added by editing
storage format. Bars can also link to a page that does not exist yet,
creating it.

**How it would fit here.** A static node, not a Wave D `dynamicBlock`: the
author draws the roadmap, so the content *is* the data — there is no query
to run. That means it stores its own lanes, bars and markers as node
attributes, and the export renderer needs a case for it.

**The design questions**, none of them obvious:

- **The data model.** Bars carry dates, so the node stores dates —
  the first content in this schema that does. Time zones, all-day
  semantics, and what "today" means to a reader in another zone.
- **Export.** An SVG timeline in HTML is achievable; Markdown has no
  timeline, so it degrades to a table of lanes, bars and dates (the same
  honesty the chart node already applies, naming its source table).
- **Editing.** Drag-to-resize inside ProseMirror is the most interactive
  node this editor would have — meaningfully more than the table controls.
  Worth deciding whether the first version is form-edited (a bar list with
  date pickers, like the dynamic-block params menu) and dragging comes
  later.
- **Whether bars should link to pages** the way Confluence's do, which
  pulls in the permission-masking rule: a bar linking to a page the reader
  cannot see must not reveal its title.
