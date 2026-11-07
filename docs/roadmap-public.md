# Tesria roadmap
<!-- Read by tesria.com at build time. One line per item, written for visitors. -->

## Shipped

### 0.8.8 (November 7, 2026)
- A security update: every library brought up to date, including DOMPurify, which cleans diagrams, and a library email uses

### 0.8.7 (October 1, 2026)
- Fixed: the page filter in an exported site has a clear button again, like the one in the app
- Dragging pages in the page tree uses the newest version of its library, checked on phones and desktops

### 0.8.6 (October 1, 2026)
- Space exports keep going when you leave the page, with progress and downloads in the notifications
- A new page is kept on your device until you publish it, so a browser crash no longer loses it
- The setup wizard picks up where you left off and keeps the answers you gave
- Export as PDF shows that it is being prepared, and colored text on a highlight is readable in every theme
- Fixed: an older Tesria no longer starts on a database a newer one has updated, and live editing survives a database restart
- Fixed: requiring two-factor for administrators can no longer lock out whoever turns it on

### 0.8.5 (September 30, 2026)
- Fixed: every medium-severity issue from the final pre-launch test, across live editing, the API, backups, installs and webhooks
- Fixed: an AI assistant's change right after someone closes a page is no longer shown as a change to review
- Fixed: in Safari, typing /table and pressing Enter inserts a table, whatever the pointer rests on
- Fixed: colored text on a highlight is readable in the dark theme

### 0.8.4 (September 30, 2026)
- Every library brought up to date, including a security update the vulnerability check flagged
- Fixed: undoing a restore to a chosen moment works again, and puts the wiki back exactly as it was

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
