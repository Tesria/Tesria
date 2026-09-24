// REST API and MCP (dev-plan 10.5), rewritten to the owner's rules of
// 2026-09-23 (WRITING.md, the pilot pages). A fuller developer section is
// planned (dev-plan Phase 17); these pages are the beginner's way in.
//
// Facts from src/Api, checked 2026-09-24: ApiTokenEndpoints and
// ApiTokenService (the cct_ prefix, shown once, stored as a hash), the token
// handler (the Use API tokens right, suspended accounts), TokenScope and
// TokenScopeMiddleware (read_only_token), CsrfHeaderMiddleware, RequireSudo
// (reauth_required), OpenApiSetup (/api/docs, /api/openapi.json, Scalar's
// Test Request), SearchEndpoints (pageId, snippet), PageEndpoints and
// PageWriter, WebhookEndpoints, WebhookDispatcher and
// WebhookDeliveryBackgroundService (three attempts, 2 and 4 seconds apart, a
// 5 second timeout, an in-memory queue), EgressGuard, Program.cs (/mcp,
// tokens only, stateless), TesriaTools and McpAccess (the ten tools),
// MarkdownToProseMirror, CollabNotifier (the MCP source in tracked changes),
// and the web app's ApiTokensSection and SpaceWebhooksPage for the names of
// buttons.
//
// Pictures: where a token is made (and its Read-only box), the Webhooks tab
// of a space, and the reference at /api/docs. The rest is words: an API is
// typed, not clicked.

const NARROW = { width: 480, height: 900 }

export const shots = () => [
  // ---- Where a token is made: Profile, API tokens. Only the form: the list
  // under it names the signed-in account's own tokens, so it is hidden.
  {
    name: 'token-form', url: '/profile', viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 },
      // The other sections hidden, so nothing scrolls: scrolled, the boxes
      // were measured before the page moved and landed beside their targets.
      { css: '.profile__section:not(#api-tokens) { display: none !important; }' },
      { type: 'Weekly report script', selector: '#api-tokens form label:first-of-type input' },
      { eval: 'document.activeElement && document.activeElement.blur()' },
      { css: '#api-tokens form ~ * { visibility: hidden !important; }' },
    ],
    clipTo: '#api-tokens form', clipPad: 12,
    annotate: [
      { type: 'box', target: '#api-tokens .api-tokens__expiry', pad: 4 },
      { type: 'box', target: '#api-tokens .api-tokens__scope', pad: 4 },
      { type: 'box', target: '#api-tokens form button[type="submit"]', pad: 4 },
    ],
  },

  // ---- A space's Webhooks tab, filled in. Tesria Demo has no webhooks, and
  // anything under the form is hidden in case one was added by hand.
  {
    name: 'webhook-form', url: '/spaces/DEMO/settings/webhooks', viewport: NARROW, phone: false,
    steps: [
      { wait: 2500 },
      { type: 'https://example.com/hooks/tesria', selector: 'form.card.form-inline input[type="url"]' },
      { type: 'page.created,page.updated', selector: 'form.card.form-inline label:nth-of-type(2) input' },
      { eval: 'document.activeElement && document.activeElement.blur()' },
      { css: '.tab-panel form ~ * { visibility: hidden !important; }' },
    ],
    clipTo: ['.tabs', 'form.card.form-inline'], clipPad: 12,
    annotate: [
      { type: 'box', target: '.tabs .tab.is-active', pad: 4 },
      { type: 'box', target: 'form.card.form-inline button[type="submit"]', pad: 4 },
    ],
  },

  // ---- The reference, as someone signed out sees it: it needs no session.
  { name: 'api-docs', url: '/api/docs', anon: true, settle: 2500, steps: [{ wait: 4000 }], phone: false },
]

/**
 * REST API and MCP were top-level pages until the Developers section
 * (dev-plan 17) was made to hold them. Moved rather than rewritten, so their
 * ids, and every link to them, stay the same.
 */
export async function prepare({ author }) {
  const space = await author.call('GET', '/api/spaces/SUPPORT')
  const tree = await author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const developers = tree.find((n) => n.title === 'Developers')
  if (!developers) return {}
  for (const title of ['REST API', 'MCP']) {
    const node = tree.find((n) => n.title === title)
    if (!node) continue
    const index = developers.children.length
    await author.call('PUT', `/api/pages/${node.id}/move`, { parentPageId: developers.id, index })
    developers.children.push(node)
    console.log(`  moved ${title} under Developers`)
  }
  return {}
}

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, codeBlock, live, picture, pageLink, adminAt, profileAt }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  /** A numbered step: a heading that says what to do, then how. */
  const step = (n, title) => h(3, `Step ${n}: ${title}`)
  /** The placeholder, explained where it first appears on a page. */
  const yourServer = () => panel('note',
    p(b('Throughout this page, '), c('your-server'), b(' stands for your server’s address:'), ' whatever you type into the browser to open Tesria, without ', c('https://'), '. For example, if you open Tesria at ', c('https://wiki-server.local'), ', then ', c('https://your-server/api/spaces'), ' means ', c('https://wiki-server.local/api/spaces'), '.'))

  // ================================================================ REST API
  const developers = top['Developers']
  const api = await ensure('REST API', developers)
  await page('REST API', developers, doc(
    p('Everything you do in Tesria, from opening a page to adding a label, the app does by sending a request to your server and reading the answer. That set of requests is the ', b('REST API'), ', and you can use it too: from a script, a scheduled job, or another program, with no browser involved.'),
    p('You do not need it to use Tesria. It is for the moment you think “I wish this happened by itself”. Some things people use it for:'),
    ul(
      li(p(b('Publishing from another system.'), ' A build pipeline writes each release’s notes as a new page, so nobody has to copy them in.')),
      li(p(b('Reports.'), ' A script searches every morning for pages labeled ', i('needs-review'), ' and posts the list to your team’s chat.')),
      li(p(b('Tidying up in bulk.'), ' Adding a label to fifty pages is one short loop instead of fifty visits.')),
      li(p(b('Being told when something changes.'), ' A ', b('webhook'), ' has Tesria call your program the moment a page is published, updated or commented on.')),
    ),
    p('A script signs in with an ', b('API token'), ': a long secret, made on your profile, that lets it act as you. It sees exactly what you can see and can change only what you can change.'),
    panel('info', p(b('Want an AI assistant to use your wiki instead?'), ' That has its own, simpler door, made for assistants: see ', pageLink('MCP'), '.')),
    h(2, 'In this section'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ----------------------------------------------- Getting started with the API
  const start = await ensure('Getting started with the API', api)
  await page('Getting started with the API', api, doc(
    p('This page takes you from nothing to a working request in a few minutes: you make a token, send your first request with ', b('curl'), ', read the answer, and then find your way around the full reference that every Tesria serves.'),
    p(b('curl'), ' is a small program that sends a web request from a terminal and prints the answer. It is already installed on Macs, on Windows 10 and 11, and on almost every Linux. Nothing else is needed.'),
    yourServer(),

    h(2, 'Before you start'),
    ul(
      li(p(b('An account that may use API tokens.'), ' Everyone may by default. If your profile says ', i('Your role does not allow API tokens'), ', an administrator can turn on ', b('Use API tokens'), ' for your role in ', ...adminAt('Roles'), '.')),
      li(p(b('A terminal.'), ' Terminal on a Mac, PowerShell on Windows, or any Linux terminal.')),
    ),

    step(1, 'Create a token'),
    p('Open ', ...profileAt('API tokens'), '.'),
    ol(
      li(p('Give the token a name that says what will use it, such as ', i('Weekly report script'), '. You will see the name in the list later, next to when the token was last used.')),
      li(p('Under ', b('Expires'), ', choose how long it lasts: 90 days unless you have a reason. You are told a week before it runs out, so the script does not stop by surprise.')),
      li(p('Tick ', b('Read-only'), ' if the script only needs to look things up. A read-only token can read pages and search, and is refused anything that changes something. For this page’s last step, leave it unticked.')),
      li(p('Choose ', b('Create token'), '.')),
    ),
    ...(await picture(start, 'token-form', 'The API tokens form on the profile', 'Name the token, choose when it expires and whether it is read-only, then Create token.')),
    p('Tesria shows the token once, in a box that says ', i('Copy this token now, it won’t be shown again'), '. Copy it, then choose ', b('Done'), '. It starts with ', c('cct_'), '. Tesria keeps only a fingerprint of it, which is why it cannot show it to you again: if you lose it, revoke it and make a new one.'),
    panel('warning', p(b('Treat a token like a password.'), ' Anyone who has it can do what you can do, from anywhere that reaches your server, until it expires or you revoke it. Do not paste it into chat, email or a file you share. The one thing a token can never do is manage your account (tokens, sessions, password, two-factor, profile): those answer ', c('token_not_allowed'), '.')),

    step(2, 'Keep the token handy in your terminal'),
    p('Rather than pasting the token into every command, put it in a variable for this terminal window. It is forgotten when you close the window.'),
    codeBlock('bash', '# Mac or Linux\nexport TESRIA_TOKEN="cct_paste-your-token-here"'),
    codeBlock('powershell', '# Windows PowerShell\n$env:TESRIA_TOKEN = "cct_paste-your-token-here"'),

    step(3, 'Send your first request'),
    p('Ask for the list of spaces you can see. The token goes in a header called ', c('Authorization'), ', after the word ', c('Bearer'), ':'),
    codeBlock('bash', '# Mac or Linux\ncurl -H "Authorization: Bearer $TESRIA_TOKEN" https://your-server/api/spaces'),
    codeBlock('powershell', '# Windows PowerShell: type curl.exe, because plain curl there is something else\ncurl.exe -H "Authorization: Bearer $env:TESRIA_TOKEN" https://your-server/api/spaces'),
    p('The answer is JSON: a list, with one entry for each space. Shortened, it looks like this:'),
    codeBlock('json', '[\n  {\n    "id": "8c2e61f0-…",\n    "key": "DEMO",\n    "name": "Tesria Demo",\n    "description": "Every element, with examples.",\n    "isPublic": false,\n    …\n  }\n]'),
    p('If you see a list, everything works: your server, your token and your terminal. The ', c('id'), ' is how the API refers to a space when it needs a number rather than a key, for example when you create a page in it.'),
    panel('note', p(b('curl says “SSL certificate problem”?'), ' Your Tesria uses its own certificate, and curl keeps its own list of certificates it trusts, separate from your browser’s. Download the certificate once and tell curl to use it:')),
    codeBlock('bash', 'curl -o tesria-ca.crt http://your-server/ca.crt\ncurl --cacert tesria-ca.crt -H "Authorization: Bearer $TESRIA_TOKEN" https://your-server/api/spaces'),
    p('The same certificate is behind the browser warning some people see; ', pageLink('Trusting the local certificate'), ' explains it.'),

    step(4, 'Search, and read a page'),
    p('Search works the way the search box does. Put the words after ', c('?q='), ', and quote the whole address so the terminal leaves the ', c('?'), ' alone:'),
    codeBlock('bash', 'curl -H "Authorization: Bearer $TESRIA_TOKEN" "https://your-server/api/search?q=kickoff"'),
    p('Each result has the page’s ', c('pageId'), ', its space, its title, and a ', c('snippet'), ': the passage that matched, with the matching words between ', c('**'), '. Up to 50 results come back, best first, and only from pages you can read.'),
    p('To read a page as Markdown, plain text that is easy for a script to work with, export it by its id:'),
    codeBlock('bash', 'curl -H "Authorization: Bearer $TESRIA_TOKEN" "https://your-server/api/pages/<page-id>/export?format=markdown"'),
    p('A page’s id is also in its address in the browser: it is the long part after ', c('/pages/'), '.'),

    step(5, 'Change something'),
    p('A first change that is easy to see and easy to undo: add a label to a page. Sending data takes two more options: ', c('-X POST'), ' says what kind of request it is, and ', c('-d'), ' carries the data, as JSON.'),
    codeBlock('bash', 'curl -X POST \\\n  -H "Authorization: Bearer $TESRIA_TOKEN" \\\n  -H "Content-Type: application/json" \\\n  -d \'{"name":"from-the-api"}\' \\\n  https://your-server/api/pages/<page-id>/labels'),
    p('Open the page in the browser and the label is there. Remove it from the page as usual, or with the same address and ', c('-X DELETE'), ' plus ', c('/from-the-api'), ' at the end.'),
    p('If the answer is ', c('{"code":"read_only_token", …}'), ', the token was made read-only. Make another without ', b('Read-only'), ' ticked.'),

    h(2, 'The reference: every request, in your browser'),
    p('Every Tesria describes its whole API at ', c('https://your-server/api/docs'), '. It is generated from the code of the version you are running, so it is always right for your server. It lists every request, what to send, and what comes back, with a ready-made curl command for each.'),
    ...(await picture(start, 'api-docs', 'The API reference at /api/docs', 'The reference. Paste a token under Authentication to try requests from the page.')),
    ul(
      li(p(b('Trying a request:'), ' paste your token into ', b('Bearer Token'), ' under ', b('Authentication'), ', then choose ', b('Test Request'), ' beside any request and ', b('Send'), '. It runs against your own server, as you, so a request that changes something really changes it.')),
      li(p(b('For tools that generate code:'), ' the same description, in the standard OpenAPI format, is at ', c('https://your-server/api/openapi.json'), '.')),
    ),
    p('The reference needs no signing in to read: the shape of the API is not a secret, and every request still checks who is asking.'),

    h(2, 'What a token can and cannot do'),
    ul(
      li(p(b('It acts as you.'), ' It sees the spaces and pages you can see, and a page you cannot see answers “not found”, exactly as if it did not exist.')),
      li(p(b('A read-only token changes nothing.'), ' Every request that would change something is refused with ', c('403'), ' and ', c('"code":"read_only_token"'), '.')),
      li(p(b('Some actions always refuse a token,'), ' read-only or not: the ones that ask you to confirm your password in the browser, such as permanently deleting a page from the trash, changing roles, or changing the branding. The answer is ', c('403'), ' with ', c('"code":"reauth_required"'), '. Do those in the browser.')),
      li(p(b('It follows your account.'), ' If your account is suspended, or your role loses ', b('Use API tokens'), ', every token you made stops working. They start working again if the right comes back.')),
    ),

    h(2, 'Looking after your tokens'),
    ul(
      li(p(b('One token for each script or tool.'), ' Then you can revoke one without breaking the others, and the list shows which one was last used, and when.')),
      li(p(b('Read-only whenever you can.'), ' A script that only reads cannot do damage if its token leaks.')),
      li(p(b('Tokens do not expire.'), ' When a script is retired, choose ', b('Revoke'), ' next to its token. Anything using it stops working at once, and a revoked token cannot be brought back.')),
    ),
    p('Next: ', pageLink('Requests and responses'), ' explains the answers you get back, and how to write a whole page.'),
  ))

  // --------------------------------------------------- Requests and responses
  await page('Requests and responses', api, doc(
    p('Once your first request works (see ', pageLink('Getting started with the API'), '), this page explains what you send, what comes back, and what to do when something is refused. The ', c('/api/docs'), ' reference on your server has every field of every request; this is the part worth understanding first.'),
    yourServer(),

    h(2, 'JSON in, JSON out'),
    ul(
      li(p(b('Every answer is JSON,'), ' except downloads such as an exported page or an attachment.')),
      li(p(b('When you send data,'), ' send JSON too, with the header ', c('Content-Type: application/json'), '.')),
      li(p(b('Things are named by id,'), ' a long code such as ', c('8c2e61f0-6a1b-4f0e-9d51-2b7c0e4a9f13'), '. Spaces also have their short key, such as ', c('DEMO'), ', which most space requests take instead.')),
      li(p(b('A choice from a fixed list is a number.'), ' In permissions, for example, ', c('operation'), ' is 0 for View, 1 for Edit and 2 for Admin. The reference lists each one.')),
    ),

    h(2, 'How a page’s content is stored'),
    p('A page is not stored as text or HTML but as a ', b('document'), ': a tree of blocks (headings, paragraphs, tables, panels) in the editor’s own format, which is called ProseMirror. The API sends and receives it as ', c('contentJson'), ', which is that document written as JSON and then put in a string.'),
    p('The easiest way to learn the format is to write something in the editor and read it back:'),
    codeBlock('bash', 'curl -H "Authorization: Bearer $TESRIA_TOKEN" https://your-server/api/pages/<page-id>'),
    p('The smallest possible page, one paragraph, looks like this as a document:'),
    codeBlock('json', '{\n  "type": "doc",\n  "content": [\n    { "type": "paragraph", "content": [ { "type": "text", "text": "Written by a script." } ] }\n  ]\n}'),
    panel('success', p(b('Would rather write Markdown?'), ' The MCP server takes Markdown and converts it into the editor’s elements for you. It is made for AI assistants, but anything that speaks MCP can use it. See ', pageLink('MCP'), '.')),

    h(2, 'Writing a page'),
    p('To create a page, send the space’s ', c('id'), ' (from ', c('/api/spaces'), '), a title and the content. Note how the document is a string inside the JSON, so its own quotes are written ', c('\\"'), ':'),
    codeBlock('bash', 'curl -X POST https://your-server/api/pages \\\n  -H "Authorization: Bearer $TESRIA_TOKEN" \\\n  -H "Content-Type: application/json" \\\n  -d \'{"spaceId":"<space-id>","title":"Hello from a script","contentJson":"{\\"type\\":\\"doc\\",\\"content\\":[{\\"type\\":\\"paragraph\\",\\"content\\":[{\\"type\\":\\"text\\",\\"text\\":\\"Written by a script.\\"}]}]}"}\''),
    p('The page is published straight away, at the top level of the space. Add ', c('"parentPageId":"<page-id>"'), ' to put it under another page. The answer is the new page, with its ', c('id'), '. Everyone watching the space is told, as if you had published it in the browser.'),
    p('Building those strings by hand is fiddly. In a real script, build the document as an object and let your language turn it into a string: ', c('JSON.stringify'), ' in JavaScript, ', c('json.dumps'), ' in Python.'),

    h(2, 'Updating without overwriting someone'),
    p(c('PUT https://your-server/api/pages/<page-id>'), ' changes a page. It takes a ', c('title'), ', a ', c('contentJson'), ' and a ', c('changeComment'), ' (what changed, shown in the page’s history). Leave out ', c('contentJson'), ' to rename a page without touching what it says. Every update adds a new version, so nothing is lost: the history can always bring back the old one.'),
    p('If a person might be changing the same page, send ', c('baseVersion'), ' too: the ', c('currentVersionNumber'), ' you read before making your change. If the page has moved on since, Tesria refuses with ', c('409'), ' and sends the page as it is now, instead of writing over the other person’s work. Read it again, make your change to the new version, and send that.'),
    p('Anyone who has the page open in the editor when your update arrives sees it highlighted, to accept or reject. See ', pageLink('Changes from assistants and the API'), '.'),

    h(2, 'When a request is refused'),
    p('The status code says what kind of problem it is. Where there is more to say, the body explains, often with a ', c('code'), ' a script can check.'),
    ul(
      li(p(b('400: something in the request is wrong.'), ' The body names the field and why, for example a label with a space in it.')),
      li(p(b('401: you are not signed in.'), ' The token is missing, mistyped or revoked.')),
      li(p(b('403: you may not do this.'), ' You can see the thing but not change it, or the token is read-only (', c('read_only_token'), '), or the action needs a password in the browser (', c('reauth_required'), ').')),
      li(p(b('404: not found.'), ' It does not exist, or you are not allowed to know that it does. A restricted page answers 404 rather than 403, so nobody can find out it is there by guessing.')),
      li(p(b('409: a conflict.'), ' A space key already taken, or a page that changed since your ', c('baseVersion'), '.')),
      li(p(b('413: too large.'), ' An attachment can be up to 25 MB, a wiki pack up to 500 MB, and anything else up to 100 MB.')),
      li(p(b('429: too many requests.'), ' Wait the number of seconds in the ', c('Retry-After'), ' header, then try again.')),
      li(p(b('503: not right now.'), ' A backup is being restored and the wiki is read-only for a few minutes, or the export you asked for needs a service this server does not run.')),
    ),

    h(2, 'Limits worth knowing'),
    ul(
      li(p(b('Requests with a token are not rate-limited.'), ' Requests with no token or session at all are limited to 300 a minute from one address, which an administrator can change.')),
      li(p(b('Lists come back whole,'), ' except search (the best 50) and notifications and the audit log (the most recent, up to 200).')),
      li(p(b('Browser sessions need one more header.'), ' If you call the API from a web page using someone’s signed-in session rather than a token, every change must carry ', c('X-Requested-With: Tesria'), '. It stops other websites from acting with that person’s session. Scripts with a token do not need it.')),
    ),
  ))

  // --------------------------------------------------------- What the API covers
  await page('What the API covers', api, doc(
    p('Nearly everything you can do in the browser, a script can do through the API, because the browser uses the same requests. Here is what there is, by area, so you know where to look in the full reference at ', c('/api/docs'), ' on your server.'),
    ul(
      li(p(b('Spaces:'), ' list, create, read, rename, archive, delete, choose which exports are allowed, and import a wiki pack.')),
      li(p(b('Pages:'), ' the page tree, create, read, update, move (also to another space), copy, full width, delete, restore from the trash, and delete permanently.')),
      li(p(b('Drafts:'), ' start a new page as a draft, publish it, or throw it away.')),
      li(p(b('Versions:'), ' a page’s history, any one version, and restoring one.')),
      li(p(b('Attachments:'), ' upload, list, download and delete a page’s files.')),
      li(p(b('Comments:'), ' list, add, edit, delete, resolve and reopen.')),
      li(p(b('Labels:'), ' the labels in use, the pages with a label, and adding and removing a page’s labels.')),
      li(p(b('Search:'), ' full-text search, everywhere or in one space.')),
      li(p(b('Exports:'), ' a page as Markdown, HTML or PDF; a space as a website or a wiki pack.')),
      li(p(b('Permissions:'), ' who can view, edit and administer a space, and restrictions on a page.')),
      li(p(b('Groups and people:'), ' groups and their members, and the list of accounts.')),
      li(p(b('Watching and notifications:'), ' watch a page or a space, read your notifications and mark them read.')),
      li(p(b('Templates:'), ' list, create, rename and delete.')),
      li(p(b('Webhooks:'), ' list, add and delete a space’s webhooks. See ', pageLink('Webhooks'), '.')),
      li(p(b('Live content:'), ' what a live content block on a page shows, worked out for whoever asks.')),
      li(p(b('Your account:'), ' profile, picture, email, password, sessions, two-factor, recovery codes and API tokens.')),
      li(p(b('Administration:'), ' settings, users, spaces, invites, security, roles, backups, branding, the audit log and the dashboard. Each needs the right for it, the same as in the browser.')),
      li(p(b('Health:'), ' ', c('/api/health'), ' says whether Tesria is running, without signing in; with a token or signed in, it also names the version.')),
    ),
    panel('info', p(b('What a token cannot reach.'), ' A few administrative actions ask for your password in the browser and always refuse a token. See ', pageLink('Getting started with the API'), ', under ', i('What a token can and cannot do'), '.')),
  ))

  // ------------------------------------------------------------------ Webhooks
  const hooks = await ensure('Webhooks', api)
  await page('Webhooks', api, doc(
    p('A ', b('webhook'), ' is Tesria calling another program the moment something happens in a space. Instead of your program asking Tesria every few minutes “has anything changed?”, Tesria tells it, straight away, by sending it a short message over the web.'),
    p('Some things people do with one:'),
    ul(
      li(p('Post “Launch plan was updated by Sam” to a team chat, through a small program that receives the webhook and passes it on.')),
      li(p('Rebuild a public help site whenever a page in its space is published.')),
      li(p('Keep a record elsewhere of every comment in a space.')),
    ),
    p('You need something at the other end that can receive a web request: a small program you run, or an automation service that gives you a web address to call. Webhooks belong to a space, and only people who can administer that space can see or set them up.'),

    h(2, 'Setting one up'),
    step(1, 'Open the space’s Webhooks tab'),
    p('In the space, choose ', b('Space settings'), ', then the ', b('Webhooks'), ' tab.'),
    ...(await picture(hooks, 'webhook-form', 'The Webhooks tab of Space settings, filled in', 'The address to call, the events to send, and Add webhook.')),
    step(2, 'Enter the address and the events'),
    ul(
      li(p(b('URL:'), ' the web address of the program that will receive the messages, starting with ', c('https://'), ' (or ', c('http://'), ').')),
      li(p(b('Events:'), ' which happenings to send, separated by commas, such as ', c('page.created,page.updated'), '. Leave ', c('*'), ', the default, for all of them. The events are listed below.')),
    ),
    step(3, 'Choose Add webhook, and copy the secret'),
    p('Tesria shows the webhook’s ', b('signing secret'), ' once. Copy it into the receiving program’s settings, then choose ', b('Done'), '. The program uses it to check that each message really came from your Tesria (see below). If you lose it, delete the webhook and add it again.'),
    p('To change a webhook, delete it (', b('Delete'), ' beside it in the list) and add a new one. The receiving program is not told either way.'),

    h(2, 'The events'),
    ul(
      li(p(c('page.created'), ': a page is published for the first time, from the editor, the API or an assistant.')),
      li(p(c('page.updated'), ': a page is updated, or restored to an earlier version.')),
      li(p(c('comment.created'), ': someone comments on a page.')),
      li(p(c('*'), ': all of the above.')),
    ),
    p('Drafts send nothing: a page is only news once it is published. Neither does moving, reordering or deleting a page.'),

    h(2, 'What arrives'),
    p('Each message is a ', c('POST'), ' request with a JSON body like this:'),
    codeBlock('json', '{\n  "event": "page.updated",\n  "targetType": "page",\n  "targetId": "6f1c2a90-…",\n  "metadata": { "Title": "Launch plan" },\n  "timestamp": "2026-09-23T14:30:00Z"\n}'),
    ul(
      li(p(c('targetId'), ' is the page’s id, for comments too. Ask the API for the page (', c('/api/pages/<id>'), ') if you need more than the title.')),
      li(p('For a comment, ', c('metadata'), ' holds ', c('Body'), ', the comment’s first 140 characters, instead of ', c('Title'), '.')),
      li(p('The names inside ', c('metadata'), ' start with a capital letter, unlike the rest.')),
    ),

    h(2, 'Checking a message came from Tesria'),
    p('Anyone who knows your program’s address could send it a fake message. So every real one carries a header, ', c('X-Webhook-Signature: sha256=…'), ', which is a fingerprint of the message made with the signing secret. Only something that knows the secret can make it. Your program works out the same fingerprint and compares; if they differ, it ignores the message.'),
    p('Compute it over the body exactly as it arrived, before any JSON parsing, using the secret as text. In JavaScript (Node):'),
    codeBlock('javascript', "import { createHmac, timingSafeEqual } from 'node:crypto'\n\nfunction fromTesria(rawBody, header, secret) {\n  const expected = 'sha256=' + createHmac('sha256', secret).update(rawBody).digest('hex')\n  return header.length === expected.length && timingSafeEqual(Buffer.from(header), Buffer.from(expected))\n}"),
    p('In Python:'),
    codeBlock('python', "import hashlib, hmac\n\ndef from_tesria(raw_body: bytes, header: str, secret: str) -> bool:\n    expected = 'sha256=' + hmac.new(secret.encode(), raw_body, hashlib.sha256).hexdigest()\n    return hmac.compare_digest(header, expected)"),

    h(2, 'How delivery works'),
    ul(
      li(p(b('Sent after the change is saved,'), ' one message at a time.')),
      li(p(b('Tried up to three times.'), ' If the receiver answers with an error, or not within 5 seconds, Tesria waits 2 seconds and tries again, then 4 seconds and tries once more, then gives up on that message.')),
      li(p(b('Not kept across a restart.'), ' Messages waiting to be sent are held in memory, so restarting Tesria loses them. If your program must not miss anything, have it also check the API now and then.')),
      li(p(b('Your own network is off limits.'), ' A webhook cannot call an address inside a private network, including this server and other machines on your home or office network, nor a name ending in ', c('.local'), '. Tesria refuses it when you add it, and checks again each time it sends. This stops a webhook from being used to reach machines the internet cannot.')),
    ),
    panel('note', p(b('Nothing arriving?'), ' Tesria records every attempt in its log. On the server, ', c('docker compose logs app | grep Webhook'), ' shows each delivery, failure and refusal, with the reason.')),
  ))

  // ===================================================================== MCP
  const mcp = await ensure('MCP', developers)
  await page('MCP', developers, doc(
    p('An AI assistant, such as Claude, is far more useful when it can look things up in your own wiki: “What did we decide about the launch date?”, “Summarize the onboarding checklist”, “Draft release notes from the pages labeled ', i('v2'), '”. Tesria lets it, through ', b('MCP'), '.'),
    p(b('MCP'), ', the Model Context Protocol, is a common way for AI assistants to use other software. Tesria has an MCP server built in: an assistant that supports MCP connects to it, and can then search your wiki, read pages, and, if you allow it, write them.'),
    h(2, 'Safe by design'),
    ul(
      li(p(b('It acts as you, never more.'), ' The assistant signs in with an API token you make. It sees only the spaces and pages you can see, and a page you cannot see does not exist as far as it is concerned.')),
      li(p(b('You choose whether it can write.'), ' With a read-only token it can search and read, and nothing else.')),
      li(p(b('Its changes are visible.'), ' Every page it writes gets a new version in the page’s history, and anyone editing that page at the time sees the change highlighted, to accept or reject.')),
      li(p(b('Some things are always left to people:'), ' deleting pages, permissions, creating spaces, attachments and administration. There are simply no tools for them.')),
    ),
    p('The server is always on, at ', c('/mcp'), ' on your Tesria’s address. There is nothing for an administrator to switch on.'),
    h(2, 'In this section'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // -------------------------------------------------- Connecting an assistant
  const connect = await ensure('Connecting an assistant', mcp)
  await page('Connecting an assistant', mcp, doc(
    p('Connecting an assistant takes two things: a token, which is how the assistant signs in as you, and your Tesria’s MCP address, which is ', c('https://your-server/mcp'), '. This page walks through both, with Claude Code as the example, and then shows how to check it worked.'),
    yourServer(),

    step(1, 'Make a token for the assistant'),
    p('Open ', ...profileAt('API tokens'), '.'),
    ol(
      li(p('Name the token after the assistant, such as ', i('Claude Code on my laptop'), ', so you know which one to revoke later.')),
      li(p('Decide whether it may write (see ', b('Read-only or not'), ' below). Tick ', b('Read-only'), ' if it only needs to look things up.')),
      li(p('Choose ', b('Create token'), ', copy the token (it starts with ', c('cct_'), ' and is shown only once), and choose ', b('Done'), '.')),
    ),
    ...(await picture(connect, 'token-form', 'The API tokens form on the profile', 'Make one token for each assistant. Read-only is the safe choice.')),

    step(2, 'Add Tesria to the assistant'),
    p(b('In Claude Code,'), ' run this in a terminal, with your address and the token you copied:'),
    codeBlock('bash', 'claude mcp add --transport http tesria https://your-server/mcp --header "Authorization: Bearer cct_paste-your-token-here"'),
    p('That makes Tesria available when you use Claude Code in the folder you ran it in. To have it everywhere, add ', c('--scope user'), ' after ', c('add'), '.'),
    p(b('In another assistant,'), ' look for a setting to add a remote MCP server, sometimes called a connector or an MCP server by URL. Tesria needs a client that can do two things:'),
    ul(
      li(p(b('Connect over HTTP'), ' (the transport called Streamable HTTP) to ', c('https://your-server/mcp'), '.')),
      li(p(b('Send a header with every request:'), ' ', c('Authorization'), ' set to ', c('Bearer'), ', a space, and your token.')),
    ),
    p('Tesria’s MCP server accepts API tokens only. Signing in with a browser, or a client that only offers to sign you in through a web page, will not work.'),

    step(3, 'Check that it works'),
    p('In Claude Code, run ', c('claude mcp list'), ': Tesria should be listed as connected. Then ask the assistant something only your wiki knows, such as ', i('Which spaces are in our wiki?'), ' It should answer with your spaces’ names, having asked Tesria for them.'),
    panel('note', p(b('A certificate error?'), ' If your Tesria uses its own certificate (you open it at a local name such as ', c('wiki-server.local'), '), Claude Code does not trust it until told to. Download the certificate from ', c('http://your-server/ca.crt'), ', and start Claude Code with the variable ', c('NODE_EXTRA_CA_CERTS'), ' set to where you saved it, for example ', c('NODE_EXTRA_CA_CERTS="$HOME/Downloads/tesria-ca.crt" claude'), '. See ', pageLink('Trusting the local certificate'), ' for what that certificate is.')),

    h(2, 'Read-only or not'),
    p('The token decides what the assistant may do, so choose it for the job:'),
    ul(
      li(p(b('Read-only'), ' for answering questions, summarizing and finding things. It can use every tool that reads, and every tool that writes is refused with a message saying the token is read-only. This is the right choice most of the time.')),
      li(p(b('Not read-only'), ' when you want it to write: drafting pages, tidying labels, filling in a template. It can then change any page you can change, so give it to an assistant you are watching.')),
    ),
    panel('warning', p(b('The assistant can do whatever its token can.'), ' Make a separate token for each assistant, and when you stop using one, choose ', b('Revoke'), ' next to its token on your profile. It is cut off at once.')),

    h(2, 'When an assistant writes'),
    p('A page an assistant creates or changes is published straight away, as a new version credited to you, the token’s owner, with the assistant’s note on what it changed in the page’s history. Watchers are told, and webhooks fire, exactly as if you had done it.'),
    p('If someone has that page open in the editor at the time, they see the assistant’s change highlighted as tracked changes, marked as coming from MCP, with ', b('Accept all'), ' and ', b('Reject all'), ' above the page. Rejecting takes it out of their draft, so their ', b('Update'), ' publishes the page without it; the assistant’s version stays in the history either way. See ', pageLink('Changes from assistants and the API'), '.'),
    p('Next: ', pageLink('What an assistant can do'), ' lists its ten tools.'),
  ))

  // ------------------------------------------------- What an assistant can do
  await page('What an assistant can do', mcp, doc(
    p('An assistant uses Tesria through ', b('tools'), ': ten small, named actions, such as “search pages” or “get a page”. You do not call them yourself: you ask in plain words, and the assistant picks the tools and reads the answers. Knowing what the tools are tells you what to ask for.'),
    p('Every tool works only within what its token’s owner may see and do. A page or space you cannot see answers “not found”, the same as one that does not exist.'),

    h(2, 'Finding things'),
    p('These work with any token, read-only included.'),
    ul(
      li(p(c('list_spaces'), ': the spaces you can see, with their keys, names, descriptions and whether each is public. ', i('“What spaces do we have?”'))),
      li(p(c('get_space_tree'), ': a space’s pages, as a tree, leaving out any you cannot see and everything under them. ', i('“Show me how the Engineering space is organized.”'))),
      li(p(c('search_pages'), ': full-text search, everywhere or in one space, returning 20 pages (up to 50 if it asks) with the passage that matched. ', i('“Find everything about the office move.”'))),
      li(p(c('find_pages_by_label'), ': the pages with a label, everywhere or in one space. ', i('“List the pages labeled incident.”'))),
      li(p(c('list_labels'), ': the labels used in a space, most used first, counting only pages you can see. ', i('“Which labels does the Support space use?”'))),
    ),

    h(2, 'Reading'),
    ul(
      li(p(c('get_page'), ': a page as Markdown, with its title, labels, version, address and an outline of its headings. Live content, such as a list of child pages, comes filled in as you would see it. On a long page the assistant can ask for just one section by its heading, which keeps its answers focused. It can also ask for the editor’s own format, to copy a page exactly. ', i('“Read the onboarding checklist and tell me what a new starter does on day one.”'))),
    ),

    h(2, 'Writing'),
    p('These need a token that is not read-only. With a read-only token they are refused, with a message saying so.'),
    ul(
      li(p(c('create_page'), ': a new page in a space, from Markdown, optionally under another page. It is published straight away. ', i('“Write a page in the Team space summarizing this meeting.”'))),
      li(p(c('update_page'), ': replaces a page’s content from Markdown, and optionally its title, with a note for the history. Given only a title, it renames the page and leaves the content alone. It replaces the whole page, so a careful assistant reads it first and sends it back with its change. ', i('“Fix the typos on the Launch plan page.”'))),
      li(p(c('add_page_label'), ' and ', c('remove_page_label'), ': a page’s labels. Labels are lower-case letters, digits, dots, dashes and underscores, up to 50 characters. ', i('“Label every page about the office move with office-move.”'))),
    ),
    p('An assistant can only write to pages and spaces you can edit. A page it writes arrives exactly as your own change would: a new version, watchers told, webhooks sent, and anyone editing the page at the time sees it highlighted to accept or reject. See ', pageLink('Connecting an assistant'), ', under ', i('When an assistant writes'), '.'),

    h(2, 'What Markdown can say'),
    p('Assistants write in Markdown, and Tesria turns it into the editor’s elements. These come through as the real thing:'),
    ul(
      li(p('Headings, paragraphs, bold, italic, strikethrough, inline code and links.')),
      li(p('Bulleted, numbered and task lists.')),
      li(p('Code blocks with their language, quotes, tables, dividers and pictures.')),
    ),
    p('Panels, statuses, layouts, live content and the other rich elements have no Markdown, so an assistant cannot make them. Anything it writes that Tesria does not recognize arrives as plain text rather than being dropped. The idea is that an assistant writes the words and a person adds the finishing touches in the editor. When an assistant rewrites a page that already has panels or layouts, it should read and send the editor’s own format instead, which keeps them.'),

    h(2, 'What it cannot do'),
    p('There are deliberately no tools for these. Ask a person, or do them yourself in the browser:'),
    ul(
      li(p('Deleting, restoring or permanently deleting pages.')),
      li(p('Moving pages, and changing permissions or restrictions.')),
      li(p('Creating, archiving or deleting spaces.')),
      li(p('Uploading attachments, comments, and anything in Administration.')),
    ),
  ))
}
