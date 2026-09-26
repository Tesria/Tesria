# Roadmap / future features

Forward-looking ideas that haven't been scheduled or designed yet: distinct
from [`PLAN.md`](../PLAN.md) (the original founding design doc, phases 1–5,
now historical/complete) and [`CHANGELOG.md`](./CHANGELOG.md) (what's
actually shipped). Add to this list as new ideas come up; move an entry to
the CHANGELOG once it's actually built.

> **Status:** the first four entries below were all scheduled in
> [`dev-plan.md`](./dev-plan.md) on 2026-09-08 and are now shipped except
> wiki packs (8.5, deliberately last): MCP (8.4 ✅), the API surface
> (8.3 ✅, as OpenAPI + a reference), Mermaid (7.F ✅). They stay here as
> the idea record. Public read mode (plan Phase 5) is the natural partner
> to wiki packs: build a wiki, export it, host it.
>
> Entries added after that date are **unscheduled**: they are ideas, not
> commitments, and each says what would make it worth doing.

## MCP support

Expose the knowledge base as an [MCP](https://modelcontextprotocol.io) server
so AI agents/tools can query spaces and pages as a context source, not just
humans through the browser. Positions the product as a hub both people and
agents read from.

## API support

Broader/more complete public API surface. Note: a token-authenticated REST
API + webhooks already ships (Phase 5, see `docs/architecture.md`): clarify
whether this means expanding that existing surface (more endpoints, better
docs, GraphQL, etc.) or something specifically new.

## Mermaid diagrams

Render [Mermaid](https://mermaid.js.org) diagram-as-code blocks in the
editor (flowcharts, sequence diagrams, etc.), alongside the existing
syntax-highlighted code blocks.

## Portable space/site export ("wiki packs")

> **Split 2026-09-20, both halves shipped.** The *website* half of this idea
> (export a space as static HTML and host it anywhere) became dev-plan
> **Phase 12**; the *portable archive* half (import into another Tesria)
> became **8.5**, shipped 2026-09-21 as wiki packs. What is left below is the
> part neither covers: a whole *instance* in one file rather than a space.


Export a single space, or the entire site, as one downloadable file that
bundles all its pages, structure, and content, which anyone else can import
to stand up their own fully-populated instance from scratch. E.g. someone
builds out a complete World of Warcraft knowledge wiki, exports it as a
single file, and shares it: anyone else downloads that file and imports it
to get the whole wiki, ready to go, on their own instance.

## Instance branding

*Asked for on 2026-09-20, while reviewing the HTML export's new
top bar: "in the future I want to enable branding where users can replace the
icon and Tesria title with their own icon and name for their instance. This
should be exported also."*

An instance replaces the mark and the wordmark in the top bar with its own,
and both travel with an export.

**Half of it already works.** The wordmark is the instance name, which an
administrator sets in Administration → Settings and which defaults to
"Tesria". Exports already carry it: `SiteChrome.Brand` is built from it, so a
site published by an instance called "Acme Wiki" says Acme Wiki.

**What is left is the mark.** An upload, stored the way space icons and
avatars already are (`IProfileMediaService`, webp, content-hashed), a setting
pointing at it, and the admin UI to put it there. The export side is already
built for it: set `SiteChrome.Brand.LogoPath` to the file's path inside the
export and copy the file in beside `assets/site.css`, exactly as a space's
uploaded icon is copied today. Nothing else in the exporter changes.

Worth deciding when it is scheduled: whether the favicon follows the uploaded
mark (today it is generated from the accent in `theme.ts`), whether the mark
gets a separate dark-theme variant, and whether removing Tesria's own name
from an instance is something the license should have an opinion about.

## Semantic search over the wiki (hybrid retrieval)

Search today is PostgreSQL full-text: lexical, English-stemmed, and very
good at the words a page actually contains. It cannot match meaning.
Measured against the real content on 2026-09-11:

| Query | Hits |
|---|---|
| `authentication` / `authenticating` | 11 each (stemming works |
| `how do I prove who I am` | **0** |
| `credentials for scripts` | **0** |
| `log in` | 4, top hit *"Audit log"*) matched the token, not the meaning |

That gap matters most for the MCP server (8.4), because an assistant
phrases queries as questions rather than keywords.

**The shape.** Embed each page (or chunk) with a small local embedding
model, store the vectors in Postgres via `pgvector`, and run *hybrid*
retrieval (lexical and vector together, merged) rather than replacing
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

**Where embeddings come from: two options (suggested 2026-09-25).** Either
or both, chosen by the operator, and off until switched on:

- **Bring your own endpoint.** The operator gives an OpenAI-compatible
  embeddings URL and key. It can be something they run themselves, such as
  Ollama on the same machine, or a hosted service if they accept that the
  text leaves the server. Nothing extra ships with Tesria.
- **A local model, pulled on request.** Where the machine can take it,
  Tesria downloads a small sentence-embedding model (roughly 100 MB to
  1 GB) and runs it on the CPU in a sidecar. Such models are fast on an
  ordinary processor.

Either way the vectors live in `pgvector`, search stays hybrid (BM25
below, plus vectors), and results are filtered by permission as above.

**Revisit when** either is true: neither is, as of 2026-09-11:

- the wiki passes roughly **500–1,000 pages**. It has **58**, totalling
  **31 KB** of text: the entire corpus fits in one model context several
  times over, so an assistant can navigate (`get_space_tree`,
  `list_labels`) and read what it needs. Semantic search solves
  needle-in-a-haystack retrieval, and there is no haystack yet;
- or people who do not know the wiki's vocabulary start searching it (
  new joiners, or a public space) where the synonym gap bites regardless
  of size.

Cheaper things that help first are already done (2026-09-11): search
snippets are the matching passage rather than the page's opening,
`get_page` returns a heading outline and can fetch one section, and the
relevance score is exposed.

## Roadmap planner (timeline macro)

A visual timeline block for the editor, modeled on Confluence's
[Roadmap Planner macro](https://confluence.atlassian.com/doc/roadmap-planner-macro-704578202.html):
the thing people reach for when a wiki has to show *when*, not just *what*.

Its four elements, in Confluence's terms:

- **Timeline**: the date axis, shown in days, weeks or months.
- **Lanes**: horizontal rows separating teams, products or workstreams.
- **Bars**: a phase or block of work in a lane, with text, color, start
  and end dates, an optional description, and an optional link to a page.
- **Markers**: vertical lines calling out a significant date.

Notable in the original: everything is edited **directly on the diagram**
(drag to move or resize, click to edit inline) rather than through a
configuration dialog: Atlassian's own documentation says the macro "does
not use the macro browser to set parameters" and cannot be added by editing
storage format. Bars can also link to a page that does not exist yet,
creating it.

**How it would fit here.** A static node, not a Wave D `dynamicBlock`: the
author draws the roadmap, so the content *is* the data: there is no query
to run. That means it stores its own lanes, bars and markers as node
attributes, and the export renderer needs a case for it.

**The design questions**, none of them obvious:

- **The data model.** Bars carry dates, so the node stores dates:
  the first content in this schema that does. Time zones, all-day
  semantics, and what "today" means to a reader in another zone.
- **Export.** An SVG timeline in HTML is achievable; Markdown has no
  timeline, so it degrades to a table of lanes, bars and dates (the same
  honesty the chart node already applies, naming its source table).
- **Editing.** Drag-to-resize inside ProseMirror is the most interactive
  node this editor would have: meaningfully more than the table controls.
  Worth deciding whether the first version is form-edited (a bar list with
  date pickers, like the dynamic-block params menu) and dragging comes
  later.
- **Whether bars should link to pages** the way Confluence's do, which
  pulls in the permission-masking rule: a bar linking to a page the reader
  cannot see must not reveal its title.

## Enterprise features (from the 2026-09-24 readiness review)

Added 2026-09-24, when enterprise-grade features were asked for. The
security gaps from that review are dev-plan 14.3; these are the features a
larger organization would ask for next. Unscheduled, and each depends on an
organization actually wanting it.

- **User provisioning from the identity provider (SCIM 2.0).** Accounts
  created, updated and suspended by Okta, Microsoft Entra ID or similar, so
  leaving the company removes wiki access without anyone touching Tesria.
  Pairs with single sign-on, which exists; group sync would map provider
  groups to Tesria groups. Worth doing when a team with a provider asks.
- **Audit log streaming to a SIEM.** The audit chain is tamper-evident but
  lives only in Tesria. Streaming each entry (syslog, or HTTPS in a common
  shape such as JSON lines or CEF) to Splunk, Elastic or Sentinel lets a
  security team watch it with everything else. Also a scheduled export for
  retention beyond the database.
- **SAML 2.0 sign-in.** OpenID Connect covers most providers; some
  organizations only offer SAML. A library, a settings screen, and tests
  against a real provider.
- **High availability.** More than one app container behind a load
  balancer: shared state (settings cache, rate limits, detection counters,
  export progress, the render token key) moved to a shared store, sticky
  collaboration sessions or a Hocuspocus cluster, and a managed or
  replicated PostgreSQL. A feature, not a fix: one container is the
  supported shape today (`docs/security.md`, gap 13).
- **Data retention and legal hold.** Policies that remove old versions,
  trash and audit entries after a period, and a hold that suspends them for
  a space under investigation.
- **Encryption at rest for attachments** with a key the operator holds
  (the database already relies on the host's disk encryption).

## Donut charts (saved 2026-09-25, not scheduled)

Saved at the project's request; it says when. The one pie in the product
(`src/web/src/components/PieChart.tsx`) draws both the editor's pie chart
(`chartExtension.ts`, type `pie`) and the admin backups page's disk usage
(`DiskSpace.tsx`, `StorageTargets.tsx`). The disk charts should look like the
donut on tesria.com's front page instead of a pie, and the editor's chart
element should gain **donut** as its own chart type beside pie, not as a
replacement, so existing pages keep their pies.

Notes for whoever builds it: a new entry in `CHART_TYPES` is a new stored
value in the chart node's `data-chart-type` attribute, which the site export
and PDF render through the same component, so both should follow for free;
check them all the same (an export change is tested in a real export). The
collaboration service's bundled schema (`collab/vendor/collab-schema.js`) is
built from `src/web`, so the image is rebuilt with it. The donut's hole is a
good place for the total (the disk's free space, or the chart's sum).

## Importing ZIM files (suggested 2026-09-25, not scheduled)

ZIM is the offline-wiki format of openZIM and Kiwix: Wikipedia,
Wiktionary, Stack Exchange sites and many other reference collections,
each packed into one compressed file.

- **What it would do:** import a ZIM file into a new space: its articles
  as pages, its images as attachments, its internal links and redirects
  mapped to the new pages.
- **Why:** a new wiki could start with a body of reference material, such
  as offline documentation or a subject reference, at no cost.
- **To settle:** reading ZIM needs `libzim` (C++, with Python bindings);
  .NET has no mature library, so probably a small import sidecar like the
  PDF service. Article HTML has to be cleaned into the editor's format. A
  full Wikipedia file is around 100 GB, so imports need limits or a way to
  pick a subset (by category or a list of articles). Each file's license,
  usually CC BY-SA, has to travel with the pages it becomes.

## BM25 ranking for search (suggested 2026-09-25, not scheduled)

Search is already available over the REST API (`GET /api/search`) and to
assistants through MCP. It ranks with PostgreSQL's `ts_rank`, which does
not weigh how rare a word is across the wiki or how long a page is.

- **What it would do:** rank by BM25 instead, through the same endpoint
  and tools, with no change to the API.
- **Why:** better ordering as a wiki grows, since rare terms and short,
  focused pages rank higher; and BM25 is the lexical half that hybrid
  search (above) merges with the vectors.
- **Options:** a PostgreSQL extension that provides BM25 (such as
  ParadeDB's `pg_search`), which changes the database image; or keep the
  full-text index to find matches and compute BM25 scores over them in the
  app.

## Turning a wiki into a dataset (suggested 2026-09-25, deliberately later)

Not an export of pages but a structured, machine-ready dataset from a wiki
or chosen spaces: for training or fine-tuning a model, evaluating a
retrieval (RAG) system, or analysis.

- **What it would produce:** pages split into clean text chunks at their
  headings, each with its metadata (space, place in the tree, labels,
  author, dates, version), in the formats machine-learning tools read:
  JSONL, Parquet, and a layout a Hugging Face dataset loads. Optionally,
  pairs derived from the content, such as a question with the passage that
  answers it, or a title with its summary: the part that makes it more
  than an export, and the part that needs the most design.
- **Why:** most useful to companies, whose wiki is the best record of how
  they work; turning it into training or evaluation data today means
  custom scripts. Most people will not need it, so it waits behind the
  common features.
- **To settle:** permissions (a dataset is built as one person or
  audience, never across everything, as the site export already is);
  sensitive content such as emails, names and secrets in pages, to be
  scrubbed or at least flagged, with a record of what the dataset holds;
  whether page history is included (revisions as examples of edits);
  freshness (each dataset records the wiki version and date it came from);
  and generated pairs need a language model, the same bring-your-own or
  local choice as embeddings above.
- **Revisit when** someone asks for it for real use, or after hybrid search
  lands, since the chunking and metadata would be shared.
