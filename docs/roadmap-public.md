# Tesria roadmap
<!-- Read by tesria.com at build time. One line per item, written for visitors. -->

## Shipped in 0.7.3
- An MCP server: AI assistants write as suggestions a person accepts
- Real-time editing together, with a slash menu for tables, panels, charts, diagrams and more
- A REST API and webhooks, with read-only or full-access tokens and a record of what each token did
- Spaces with roles, groups and page restrictions, and public spaces anyone can read
- Export a space as a website, a PDF, or a wiki pack that imports into another Tesria
- Automated backups with one-click restore and undo, and copies to a NAS, a removable drive or the cloud
- Private access from anywhere through the optional Tailscale integration
- A tamper-evident audit log, two-factor sign-in and security alerts
- Ready-made Docker images for Intel, AMD and ARM, installed in minutes

## Next
- Devices check the server's certificate fingerprint before trusting its own HTTPS
- Default groups for every space, and guided setup for new spaces and new people
- AI assistants can comment on a page, not only edit it
- Better search ranking (BM25), light enough for the smallest server
- An Ask an agent button that tells your AI assistant what you want, and exactly where on the page
- Review mode: changes from people, scripts or AI assistants wait for approval before going live
- Published hardware requirements, and a lighter install for small servers

## Ideas
- Semantic search, using your own embedding service or a small local model if your server can run it
- Importing ZIM files, and exporting a wiki as one to read offline in Kiwix
- Turning a wiki into a dataset for training or testing AI systems
- A timeline element for planning in pages
- SAML sign-in, and accounts provisioned from your identity provider
- Donut charts in pages and on the admin dashboard
