# Export fidelity

What each element of a page looks like once it leaves the editor (dev-plan
12.1). The complaint this answers was that exports "flatten elements and make
them look nothing like the rendered page", tables worst of all.

## Why it used to be wrong

Three causes, and none of them was a table bug.

1. **The exported stylesheet was fifteen lines.** `pre`, `code`, `blockquote`,
   and nothing else. The page renders through about two thousand lines of
   `index.css`. There was no table rule in the export at all, so every
   exported table was a browser-default table: no column widths, no header
   styling, no cell colours.
2. **There were two renderers.** `ProseMirrorRenderer.cs` rendered
   thirty-five node types a second time, in C#, copying colours out of
   `index.css` by hand (its own comments said "matching index.css"). Two
   renderers drift; that is what renderers do, and the plan was already
   paying for it: every editor change needed "a matching case in
   `ProseMirrorRenderer`".
3. **Eight node types are React node views** whose output nothing but a
   browser can reproduce: charts, Mermaid, maths, embeds, dynamic blocks,
   expand, the table of contents and media.

## What it does now

PDF and HTML are **captured from the real page**. The sidecar loads
`/export/pages/{id}`, a chrome-free route rendering the same
`<Editor editable={false}>` in the same `.paper` under the same stylesheet
the reading view uses, waits for that page to signal it has finished
rendering, and then prints it or serialises its DOM. There is one renderer.
An export cannot drift from the page without the page breaking too.

Markdown is still rendered from the document by `ProseMirrorRenderer`,
because Markdown is a genuinely different target rather than a picture of
this one.

## The matrix

Measured by counting each element in the DOM the reader sees, the DOM the PDF
is printed from, and the exported site file, on the fixture page
(`tests/fixtures/every-element.json`). A tick means the count matched what
the page shows.

The counts are of the *page*, so the site column excludes the chrome around
it (the top bar and the sidebar, added 2026-09-20). The chrome contributes
only links: the brand and one tree row per page. It used to contribute a list
as well, when the navigation was a `<ul>`; the tree is anchors now, which is
what the application's own sidebar uses.

| Element | On the page | PDF | HTML file | Site |
|---|---|---|---|---|
| Headings | 11 | ✓ | ✓ | ✓ |
| Bold / italic / underline | 3 | ✓ | ✓ | ✓ |
| Strikethrough | 1 | ✓ | ✓ | ✓ |
| Inline code | 3 | ✓ | ✓ | ✓ |
| Highlight | 1 | ✓ | ✓ | ✓ |
| Text colour | 3 | ✓ | ✓ | ✓ |
| Sub / superscript | 2 | ✓ | ✓ | ✓ |
| Link | 3 | ✓ | ✓ | ✓ |
| Text alignment | 11 | ✓ | ✓ | ✓ |
| Bullet list | 20 | ✓ | ✓ | ✓ |
| Ordered list | 2 | ✓ | ✓ | ✓ |
| Task list | 2 | ✓ | ✓ | ✓ |
| Blockquote | 1 | ✓ | ✓ | ✓ |
| Code block | 4 | ✓ | ✓ | ✓ |
| Divider | 1 | ✓ | ✓ | ✓ |
| Panel | 5 | ✓ | ✓ | ✓ |
| Expand | 1 | ✓ | ✓ | ✓ |
| Decision | 1 | ✓ | ✓ | ✓ |
| Status chip | 2 | ✓ | ✓ | ✓ |
| Date chip | 1 | ✓ | ✓ | ✓ |
| Mention | 1 | ✓ | ✓ | ✓ |
| Layout columns | 2 | ✓ | ✓ | ✓ |
| Table | 3 | ✓ | ✓ | ✓ |
| Table header cells | 8 | ✓ | ✓ | ✓ |
| Cell background | 1 | ✓ | ✓ | ✓ |
| Table of contents | 1 | ✓ | ✓ | ✓ |
| Mermaid diagram | 1 | ✓ | ✓ | ✓ |
| Maths (KaTeX) | 2 | ✓ | ✓ | ✓ |
| Chart | 1 | ✓ | ✓ | ✓ |
| Dynamic blocks | 4 | ✓ | ✓ | ✓ |
| Page properties | 1 | ✓ | ✓ | ✓ |
| Excerpt | 1 | ✓ | ✓ | ✓ |
| Smart link | 1 | ✓ | ✓ | ✓ |
| Embed | 1 | ✓ | ✓ | ✓ |

**Editor**: every element above round-trips through the editor's schema
unchanged (stored, reloaded, re-serialised with no node or attribute lost),
which is checked by seeding the fixture and comparing the document back. The
fixture is the page to open when asking whether an element still works.

## Known limits, on purpose

- **An embed prints as its card.** An iframe has nothing to show on paper, so
  print rules hide the frame and keep the card and its link.
- **An expand prints open.** There is nothing to click on paper.
- **Comment marks and tracked-change marks print as plain text.** They mean
  "somebody is working on this", which is not part of the document.
- **PDFs are always light.** A PDF is paper. The single-file HTML export and
  the site keep light, dark and system.
- **Live blocks are frozen** at the moment of export, and the site's footer
  says when that was.

## Re-checking it

Seed the fixture and compare:

```sh
scripts/screenshots/run.sh scripts/screenshots/fidelity.json
```

That creates or updates the `FIXTURE` space and its **Every element** page
from `tests/fixtures/every-element.json`, idempotently. Then export that page
as a PDF and as HTML, export the space as a site, and look at all three.
Anything that renders wrongly in an export renders wrongly on the page first,
which is the property capture buys and the reason this document is short.

## Bugs this found

Building the fixture turned up four real ones, all fixed:

1. **A mention of a user id with no account 500'd the save.** Any document
   arriving from the API, MCP or an import could carry one; the notification
   insert then broke the whole write on a foreign key.
2. **A half-committed page create left a page that could never be edited
   again.** The two saves were not in one transaction, so a failure in the
   second left a page with a version and no `CurrentVersionId`: invisible to
   every read, and 500 on every future edit because the next version number
   collided with the one already there.
3. **Version numbers came from the current-version pointer**, so a page
   missing that pointer numbered its next version from zero and collided
   forever. They come from the versions themselves now, which makes such a
   page repair itself on its next save.
4. **The Markdown export did not sanitise link hrefs.** A stored
   `javascript:` URL came out of an exported file as a working link. Found
   when deleting the HTML renderer left the Markdown path as the only link
   handling to look at.

And one the chrome turned up, which only a phone width showed: the app hides
its sidebar under `--bp-mobile` because the space action bar covers the same
jobs, and an export has no action bar, so a published site would have had no
navigation at all on a phone.
