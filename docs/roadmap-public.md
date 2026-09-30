# Tesria roadmap
<!-- Read by tesria.com at build time. One line per item, written for visitors. -->

## Shipped

### 0.8.3 (September 30, 2026)
- Charts with axes, titles, value labels, stacking, and a color of your own for each series
- Code blocks in XML, and in color schemes such as GitHub, Dracula and Solarized
- The Audit tab filters by action, person and date, and reads back to the first entry
- A new sign-in email waits for a confirmation link, and single sign-on asks for your two-factor code
- Fixed: every remaining issue of medium severity from the pre-launch test, across backups, editing, exports and the API
- Fixed: dragging a chart or an embed no longer drops stray text into the page
- Fixed: a trial copy of Tesria can no longer take over an install from before 0.8.2

### 0.8.2 (September 30, 2026)
- Fixed: backups to the cloud now happen, and restoring to a chosen moment works from the admin page, with attachments
- Fixed: a missing network drive or a wrong cloud setting no longer stops backups; each card says what is wrong
- Fixed: a slow webhook receiver no longer restarts the wiki, and Tesria restarts itself if it starts before its database
- Fixed: Close asks before leaving unpublished changes, and a shared draft shows whose changes it holds, with Discard
- Fixed: an AI assistant's change no longer overwrites what you are typing in the same paragraph
- Fixed: a second Tesria on the same computer can no longer take over the first
- Fixed: imported wiki packs keep their page order, and Markdown keeps statuses, dates and mentions in tables
- Fixed: five security issues found in testing, including webhooks and password reset links

### 0.8.1 (September 28, 2026)
- Two looks, chosen by each person: Minimal, or Glass, a frosted design with a floating sidebar
- Reduce Motion for Glass, and a Minimal, Glass or Theme Default style for statuses, charts, code blocks and diagrams
- A tidier top bar that folds into one menu when space runs short, and tabs that never scroll sideways
- The breadcrumb stays at the top of a page, and a deep trail folds into a menu
- Exported sites offer Glass too, and keep their folder names short enough for Windows
- Fixed: the page list in the phone menu scrolls, and keeps its place when the keyboard closes

### 0.8.0 (September 27, 2026)
- One command installs Tesria with no settings file: it makes its own passwords and keys
- Donut charts, in pages and on the Backups page
- Trusting a device's certificate takes a minute, with an optional fingerprint check
- Fixed: Safari on an iPhone or iPad no longer hangs on a black page
- Fixed: issues from an independent review, including device trust, third-party notices and restores

## Next
- Default groups for every space, and guided setup for new spaces and new people
- AI assistants can comment on a page, not only edit it
- Better search ranking (BM25), light enough for the smallest server
- An Ask an agent button that tells your AI assistant what you want, and exactly where on the page
- Review mode: changes from people, scripts or AI assistants wait for approval before going live
- Importing a space from Confluence, with its page tree, attachments and comments
- Published hardware requirements, and a lighter install for small servers

## Ideas
- Semantic search, using your own embedding service or a small local model if your server can run it
- Importing ZIM files, and exporting a wiki as one to read offline in Kiwix
- Turning a wiki into a dataset for training or testing AI systems
- A timeline element for planning in pages
- SAML sign-in, and accounts provisioned from your identity provider
