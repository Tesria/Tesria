# Roadmap / future features

Forward-looking ideas that haven't been scheduled or designed yet — distinct
from [`PLAN.md`](../PLAN.md) (the original founding design doc, phases 1–5,
now historical/complete) and [`CHANGELOG.md`](./CHANGELOG.md) (what's
actually shipped). Add to this list as new ideas come up; move an entry to
the CHANGELOG once it's actually built.

> **Scheduled:** as of 2026-09-08 all four entries below are sequenced in
> [`dev-plan.md`](./dev-plan.md) — MCP (8.4), API (8.3, as OpenAPI first),
> Mermaid (7.F), wiki packs (8.5, deliberately last). They stay here as the
> idea record; the plan owns the order. Public read mode (plan Phase 5) is
> the natural partner to wiki packs — build a wiki, export it, host it.

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
