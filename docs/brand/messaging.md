# Tesria messaging

What the website, the launch video and announcements say about Tesria, and
the proof behind each claim. Everything described as available is true of
the current release (0.7.5). Anything planned comes from
[`roadmap-public.md`](../roadmap-public.md) and is always called planned.
When a claim here stops being true, or a planned item ships, this file
changes first.

## The promise

**Your team's knowledge, on your own server, and you stay in control of it,
including the AI agents that work on it.**

Short form: **The self-hosted wiki built for people and AI agents.**

## Positioning

Hosted wikis bring real-time editing and AI, and keep your knowledge on
their servers. Self-hosted wikis keep it on yours, and few are built for AI
agents. Tesria is both: a real-time, self-hosted wiki with its own MCP
server, where every change an agent makes shows up for a person to keep or
reject.

Tesria's case is the combination, not any single feature. Other
self-hosted wikis have real-time editing or a REST API too, so comparisons
stay on the whole: self-hosted, real-time and built for agents, together.

## Hero

- **Headline:** The self-hosted wiki built for people and AI agents.
- **Subheadline:** Write, keep and share your team's knowledge on your own
  server. Connect any AI assistant through MCP, and see every change it
  makes.
- **Buttons:** Get Tesria (the install page) · How agents connect (the MCP
  docs page)

## The four pillars

The mark's four layers, top to bottom, in their colors (see this folder's
`README.md`).

### Write: writing together, fast

- Edit the same page at the same time, with everyone's cursor and changes
  live.
- A slash menu for tables, panels, charts, Mermaid diagrams, task lists,
  page properties, embeds and more; Markdown shortcuts as you type.
- Drafts, version history with restore, templates, labels, inline
  comments and mentions, search across every space.
- Works on a phone: reading, editing and the page tree.

### Keep: yours, safe and recoverable

- Runs on your own hardware from ready-made Docker images for Intel, AMD
  and ARM, installed in minutes.
- Automated backups that are tested, one-click restore **and undo** from
  the admin page, and point-in-time recovery.
- Encrypted offsite copies to a NAS, a removable drive or S3-compatible
  cloud storage.
- Reach it privately from anywhere through the optional Tailscale
  integration, with no port opened to the internet.
- Open source under Apache 2.0.

### Share: the right people, the right pages

- Roles, groups, space permissions and page restrictions; open sign-up,
  invite links, or single sign-on through OpenID Connect (in beta).
- Public spaces anyone can read, without an account.
- Export a space as a static website, a PDF, or a wiki pack that imports
  into another Tesria. The docs on tesria.com are a Tesria export.
- Your own name, logo and colors on your instance.

### Automate: agents that work with you

- **A built-in MCP server:** Claude, Cursor or any MCP client can read,
  search, create and update pages, limited to what the token's owner may
  see.
- **Agents never silently overwrite you:** every page an agent changes is
  a new version in the history, and the next person to edit it sees the
  change as tracked changes to keep or reject.
- **You can see what agents are doing:** read-only or full-access tokens,
  and a record of what each token did.
- A REST API with an OpenAPI reference, and webhooks when pages change.

## The strongest claims, in order

Lead with these; each is available now and easy to show.

1. **An agent's edits show up as tracked changes you keep or reject.** Every
   change is visible and reversible, not buried.
2. **A built-in MCP server that respects permissions.** Connect an
   assistant in minutes; it sees only what its token's owner sees, and a
   read-only token cannot change anything.
3. **Restore and undo from the admin page.** Backups that are tested, and a
   restore you can take back.
4. **On your own server, reachable privately through Tailscale.** Nothing
   has to be exposed to the internet.
5. **Real-time editing, open source, installed in minutes.**

Worth a line, not a headline: a tamper-evident audit log, two-factor
sign-in, security alerts, and an app that runs as a database role unable to
alter its own audit log.

## Claims we don't make

Tesria's marketing says only what the software does today. These are the
easy overstatements, and what is true instead.

| Not this | Why | Instead |
|---|---|---|
| Approve an agent's changes before they go live | Planned (review mode), not available | Agents' changes show as tracked changes to keep or reject |
| Built-in AI, live AI suggestions | No AI model is built in; Tesria works with the assistant you connect | Connect any AI assistant through MCP |
| AI reviews your docs | Planned, not available | On the roadmap: review mode |
| Tamper-evident backups | The audit log is tamper-evident; backups are tested and restorable | Tested backups with restore and undo; a tamper-evident audit log |
| RAG-ready, semantic search | Search is full-text; semantic search is an idea on the roadmap | Full-text search, and API and MCP access for your own tools |
| One-click publishing | Public spaces are one setting; the website export is a download you host | Public spaces, and a static website export |
| Minimal footprint, runs on anything | Tesria runs PostgreSQL, the app, live editing, PDF rendering and backups | Installs in minutes with Docker |
| Plugins, extensible commands | There is no plugin system | A slash menu with tables, charts, diagrams and more |
| Other self-hosted wikis lack real-time editing or APIs | Several have one or both | Self-hosted, real-time and built for agents, together |
| Zero-trust, sovereign | "Zero-trust" is a specific security claim; both are jargon | On your own server, reachable privately through Tailscale |

## Who it is for

| Who | What they worry about | What Tesria answers |
|---|---|---|
| People who run infrastructure | Lock-in, compliance, where the data lives | Their own server, Docker images, tested backups with undo, Tailscale |
| Developers using AI assistants | Giving an assistant the team's docs without giving it the keys | MCP limited to a token's rights, read-only tokens, every change visible |
| Team leads | Docs that go stale; knowing what changed and who changed it | Real-time editing, full history, agents that help keep pages current |

## Voice

Plain, specific and honest, like the docs. "On your own server", not
"sovereign"; "you see every change", not "human-in-the-loop". Say what
something does and prove it with a detail. US English, and no em dashes:
use a period, comma, colon or parentheses.

## The launch video, in beats

About 60 to 90 seconds, one beat per pillar, each shown in the real app:

1. **The problem (5 s):** your knowledge lives on someone else's servers,
   and AI tools want the keys to it.
2. **Write (15 s):** two people editing one page live; the slash menu drops
   in a chart and a diagram.
3. **Automate (20 s):** an assistant connected over MCP updates a page;
   opening it shows the change as tracked changes; a person keeps part and
   rejects the rest. The token's activity shows what it did.
4. **Keep (15 s):** Back up now; the copy lands on a NAS; restore, then
   undo. Opened from a phone over Tailscale.
5. **Share (10 s):** a space made public; the same space exported as a
   website.
6. **Close (5 s):** the mark, "The self-hosted wiki built for people and AI
   agents", tesria.com.

Planned features appear only as planned, if at all.
