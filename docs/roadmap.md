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

**Fit it to the machine (suggested 2026-09-25).** BM25 is cheap and runs
fine on a small computer; embeddings are not, above all the first pass
over a large wiki, which on a modest CPU can take hours. So the person
running Tesria chooses, with the cost in front of them:

- **Off, bring your own, or local**, and for local, a choice of model
  sizes, with what each needs in memory and roughly how long the first
  pass would take on this machine for this wiki, before anything is
  downloaded or started.
- **The first pass as a background job** with progress, that can be
  paused and resumed, throttled, or kept to quiet hours, so the wiki stays
  responsive while it runs. After it, only pages that change are
  re-embedded.
- **Search never waits for it.** BM25 answers from the start; vectors join
  the results for the pages that have them, and a wiki whose embeddings
  are off or unfinished simply searches the way it does today.

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

## ZIM files: importing and exporting (suggested 2026-09-25, not scheduled)

ZIM is the offline-wiki format of openZIM and Kiwix: Wikipedia,
Wiktionary, Stack Exchange sites and many other reference collections,
each packed into one compressed file that the Kiwix apps read offline on
phones, computers and small servers.

**Importing**

- **What it would do:** import a ZIM file into a new space: its articles
  as pages, its images as attachments, its internal links and redirects
  mapped to the new pages.
- **Why:** a new wiki could start with a body of reference material, such
  as offline documentation or a subject reference, at no cost.
- **To settle:** article HTML has to be cleaned into the editor's format.
  A full Wikipedia file is around 100 GB, so imports need limits or a way
  to pick a subset (by category or a list of articles). Each file's
  license, usually CC BY-SA, has to travel with the pages it becomes.

**Exporting**

- **What it would do:** export a space, or a chosen set of spaces, as a
  ZIM file, so a wiki can be read offline in any Kiwix app: on a phone
  with no signal, on a laptop in the field, or served on a network with no
  internet through `kiwix-serve`.
- **Why:** it pairs naturally with the site export (Phase 12), which
  already turns a space into self-contained HTML with relative links;
  packing that into a ZIM is the smaller step. Useful for teams that work
  offline, and for publishing a public wiki as a download.
- **To settle:** the same audience rule as the site export (built as one
  person or as anonymous, never across permissions); the metadata ZIM
  requires (title, language, creator, publisher, date, description and a
  48-pixel icon) and a main page; the full-text index libzim builds inside
  the file, so search works offline; and pages that stay usable where a
  Kiwix reader runs little or no JavaScript, as the site export's already
  mostly are.

**Both directions:** reading and writing ZIM needs `libzim` (C++, with
Python bindings, including its writer); .NET has no mature library, so
probably one small sidecar for both, like the PDF service. A file made by
the export should import back into Tesria, which is the natural test of
both.

## BM25 ranking for search (suggested 2026-09-25, not scheduled)

Search is already available over the REST API (`GET /api/search`) and to
assistants through MCP. It ranks with PostgreSQL's `ts_rank`, which does
not weigh how rare a word is across the wiki or how long a page is.

- **What it would do:** rank by BM25 instead, through the same endpoint
  and tools, with no change to the API.
- **Why:** better ordering as a wiki grows, since rare terms and short,
  focused pages rank higher; and BM25 is the lexical half that hybrid
  search (above) merges with the vectors. It is cheap: no model, no extra
  memory to speak of, and fine on the smallest machine Tesria runs on, so
  it can be on for everyone.
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

## Agents that work with you (suggested 2026-09-26, not scheduled)

Two features meant to close the gap in an AI-first wiki workflow: agents
writing through the REST API or MCP should work *with* the people who own
the content, not around them. They share one idea, that Tesria can hand an
agent exactly the context it needs, and are best designed together.

What exists to build on: the draft and publish lifecycle, version history,
API and MCP writes arriving in a draft as tracked changes a person accepts
or rejects (dev-plan 8.6, with its block diff), comments, and API tokens
that say which script or agent wrote what.

### Review mode: approval before anything goes live

- **Turned on in permissions**, per space (with an instance-wide default),
  and separately for each kind of author: **people**, **REST API tokens**
  and **MCP agents**. A space can require review of agents only, for
  example, while people publish directly.
- **What triggers a review** is chosen too: new pages, edits to published
  pages, comments, and possibly moves, deletions and attachments.
- **Reviewers are groups** (the next entry): a built-in **Global
  Reviewers** group reviews in every space, and each space gets its own
  **Reviewers** group when it is created, holding a new space permission,
  *Review*. Publishing by a covered author creates a review request
  instead of a new version; the page stays at its last approved version,
  and the reviewers see it in a **review queue** (with notifications and a
  count, like alerts).
- **It works like a code review.** An edit shows a diff against the live
  version (8.6's block diff); a new page shows in full. Reviewers comment
  on a passage, and **approve or reject each change or the whole
  document**; approving part of it publishes a version with only the
  accepted changes.
- **Sending it back.** To a person: back into their draft with the
  reviewer's notes, and the rejected parts marked as tracked changes. To an
  agent: Tesria **writes a prompt to feed back to the agent**, with the
  page, what was accepted and rejected, why, and the reviewer's notes (the
  prompt engine below). An agent connected over MCP could also fetch its
  review result itself and try again.
- **To settle:** no approving your own work unless a space allows it; an
  owner or administrator bypass, audited; what happens when the live page
  changes while a review is open (the diff has to be against the newest
  version, as 8.6's reconcile already does for drafts); what the REST API
  and MCP return when a publish becomes a review request (a pending status
  and a review id, which is an API change); comments under review, which is
  closer to moderation; and every decision in the audit log, with the
  approver recorded on the version.

### A prompt engine: handing a request to an agent

- **A button on a page** (and on a selection): *Ask an agent*. The person
  says what they want, in their own words or from quick actions (rewrite,
  expand, fix, summarize, check the facts, add examples), and picks the
  scope: the whole page, a section, or the selection.
- **Tesria writes the prompt:** which page (title, space, address and id),
  what is asked, and **where**: the heading path, the block, and the line
  and column or character position when it matters, together with the
  quoted text around it, since positions drift as the page changes. It
  says how to reach the page (the MCP tools by name, or the REST endpoints
  with this server's address), and the ground rules: write to the draft,
  where changes arrive as tracked changes; do not publish; respect review
  mode; say what was done in the change note.
- **Getting it to the agent:** copy to the clipboard for any agent; or,
  for one connected over MCP, a request inbox the agent reads with a tool,
  picks up, and marks done, so the person sees the status in Tesria.
- **To settle:** prompt templates an administrator can edit, per space,
  and per agent (Claude Code, Cursor and others name tools differently);
  a prompt carries only what the person asking may read, and never a token
  or secret; and when no agent has access yet, the button says so and
  links to setting up an API token or MCP.

## Groups: defaults for every space, and assigned where people and spaces are made (suggested 2026-09-26, not scheduled)

A separate addition, and the groundwork review mode needs. Today there are
three built-in groups whose membership follows each account's role (Owner,
Admins, Users) and custom groups filled by hand. Spaces grant View, Edit
or Admin to people or groups, a new space gets no groups of its own,
creating a space asks only for its key, name and description, and an
invite carries no groups.

- **More built-in global groups** beside Owner, Admins and Users:
  **Global Viewers** (read every space) and **Global Reviewers** (review
  in every space). Admins already acts as global administrators.
- **Default groups for every space**, made when the space is created and
  granted the matching permission on it: **Viewers**, **Editors**,
  **Admins** and **Reviewers** (named with the space, such as "Handbook
  Viewers"). They belong to the space: renamed with it, removed with it,
  and managed by its space admins as well as by administrators.
- **Creating a space becomes a short wizard**: name and key; who may see
  it (everyone, or only its groups); who goes in each of its groups (the
  creator starts as a space admin); and, once review mode exists, whether
  it requires review.
- **Inviting or creating a user becomes a wizard too**, with groups as a
  step: the role (user or administrator), global groups (Global Viewers,
  Global Reviewers), and per-space groups (pick spaces, and Viewer, Editor,
  Admin or Reviewer in each). An invite carries its groups and applies
  them when the account is created, with each assignment audited.
- **A stronger Groups page:** search and filtering by space, member
  counts, adding many people at once, which spaces each group grants what,
  and an effective-access view for a person ("why can Sam see this
  space?").
- **To settle:** spaces that exist already (make their default groups
  empty, or turn their current grants into memberships); how the groups sit
  with page restrictions (a restricted page stays restricted to Global
  Viewers, presumably); how "everyone" and a space's own groups combine for
  a space open to all; and keeping the list usable with four groups per
  space on an instance with many spaces.
