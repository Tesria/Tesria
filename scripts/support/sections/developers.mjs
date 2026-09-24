// Developers (dev-plan 17): the part of the Support site for people who build
// on Tesria or work on it, below the user sections. REST API and MCP (api.mjs)
// live under it; this section writes the landing page, two more REST API
// pages (code examples, and a reference generated from the OpenAPI
// document), and How Tesria is built, Contributing and Project documents.
//
// Facts checked 2026-09-24 against: docker-compose.yml and deploy/Caddyfile
// (services, routes, the one published port pair), ExportEndpoints
// (Pdf:AppOrigin, render tokens), docs/architecture.md (collaboration,
// persistence, backups contract), Domain/Permission.cs (space operations,
// inherited page restrictions, space admins past restrictions),
// InstancePermissions, DatabaseRoles (the least-privilege role),
// WikiPack/PackUpgrades, AppVersion, .github/workflows, README, CLAUDE.md's
// conventions, SECURITY.md, LICENSE and NOTICE. The code examples are the
// files in docs/examples, run against 0.6.0-dev.

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..')
const example = (file) => readFileSync(join(ROOT, 'docs', 'examples', file), 'utf8').trimEnd()

export const shots = []

/** What changed on these pages for a new version (dev-plan 16.2). */
export const changes = {}

/** The OpenAPI document, for the generated reference: read as the author sees it. */
export async function prepare({ author }) {
  return { openapi: await author.call('GET', '/api/openapi.json') }
}

/** Each area of the API, in the order the reference lists them, with what it is for. */
const AREAS = [
  ['Spaces', 'Spaces: list, create, read, change, archive, delete, and which exports each allows.'],
  ['Pages', 'Pages: the tree, reading and writing, history, moving and copying, drafts and the trash.'],
  ['Search', 'Full-text search across everything the caller may see.'],
  ['Labels', 'Labels on pages, and finding pages by label.'],
  ['Comments', 'Comments on a page or on a few words of it, and replies.'],
  ['Attachments', 'Files attached to pages: upload, list, download, remove.'],
  ['Templates', 'Page templates, for a space or the whole wiki.'],
  ['Watches', 'Watching a page or a space, to be notified of changes.'],
  ['Notifications', 'The bell: what the caller has been told about.'],
  ['Permissions', 'Who may view, edit or administer a space, and restrictions on single pages.'],
  ['Groups', 'Named sets of people, for sharing with a team at once.'],
  ['Export', 'A page as Markdown, HTML or PDF, a space as a website or a wiki pack, and importing a pack.'],
  ['Blocks', 'Live content (a page’s children, recent changes and the rest), resolved for the caller.'],
  ['Embeds', 'Link previews and embeds, from the sites on the allowed list.'],
  ['Media', 'Avatars and space icons.'],
  ['Webhooks', 'Webhooks for a space: calls to your program when something happens.'],
  ['ApiTokens', 'The caller’s own API tokens. Only from a browser session: a token cannot manage tokens.'],
  ['Auth', 'Signing in and out, the caller’s own account, sessions and two-factor. Only GET /api/auth/me answers a token.'],
  ['Collaboration', 'Editing at the same time: the short-lived token the editor opens a page with.'],
  ['Instance', 'What an anonymous visitor may know about this Tesria: its name, branding and version.'],
  ['Setup', 'The first-run setup wizard.'],
  ['Admin', 'Administration: people, roles, invites, spaces, security, backups, settings, branding, API tokens and the dashboard. Each needs its own right.'],
  ['Audit', 'The audit log.'],
  ['Tesria.Api', 'Whether this Tesria is running, and its version (/api/health).'],
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, codeBlock, live, toc, pageLink, adminAt, profileAt, openapi }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  const step = (n, title) => h(3, `Step ${n}: ${title}`)
  const developers = top['Developers']

  // Every page first, so links between them resolve on a first run.
  const api = await ensure('REST API', developers)
  await ensure('Code examples', api)
  await ensure('API reference', api)
  const built = await ensure('How Tesria is built', developers)
  for (const t of ['The parts of Tesria', 'Following a request', 'Editing together', 'Who can see what, in the code', 'Exports and wiki packs, in the code'])
    await ensure(t, built)
  const contributing = await ensure('Contributing', developers)
  for (const t of ['Building from source', 'Running the tests', 'How the code is organized', 'Proposing a change', 'Making a release'])
    await ensure(t, contributing)
  const project = await ensure('Project documents', developers)
  for (const t of ['Code of conduct', 'Governance', 'Getting help']) await ensure(t, project)

  // ============================================================= Developers
  await page('Developers', null, doc(
    p('This part of the site is for people who build on Tesria or work on it. The rest of the site is about using Tesria; nothing here is needed for that.'),
    ul(
      li(p(b('Connecting something to Tesria?'), ' ', pageLink('REST API'), ' is for scripts and other programs, and ', pageLink('MCP'), ' is for AI assistants. Both use an API token, which acts as the person who made it.')),
      li(p(b('Curious how it works?'), ' ', pageLink('How Tesria is built'), ' walks through the parts, what happens to a request, and where permissions are checked.')),
      li(p(b('Want to change it?'), ' ', pageLink('Contributing'), ' covers building it from source, the tests, and how a change is proposed.')),
      li(p(b('The project’s rules:'), ' its ', pageLink('Code of conduct'), ', ', pageLink('Governance', 'how decisions are made'), ', ', pageLink('Getting help', 'where to get help'), ', ', pageLink('Security', 'the security policy'), ' and ', pageLink('License and credits', 'the license'), '.')),
    ),
    live('children', { depth: '1', sort: 'position' }),
  ))

  // ========================================================== Code examples
  await page('Code examples', api, doc(
    p('The same small program twice, in JavaScript and in Python, doing the five things most scripts need: list the spaces a token can see, search one of them, write a page, change it without overwriting anyone, and label it. Copy one, change what it writes, and you have the skeleton of a real integration.'),
    p('Both were run against Tesria as it is described here. Neither needs anything installed beyond the language itself.'),
    panel('warning', p(b('They write pages.'), ' Point them at a space you can throw away, such as a new private space made for trying things.')),

    h(2, 'Before you run them'),
    step(1, 'Make a token'),
    p('Open ', ...profileAt('API tokens'), ', and make a token with full access (not read-only), since the scripts write. See ', pageLink('Getting started with the API'), ' for each step.'),
    step(2, 'Tell the script where and as whom'),
    p('The scripts read three settings from the environment rather than from the code, so a token never ends up in a file you might share. On a Mac or Linux:'),
    codeBlock('bash', 'export TESRIA_URL=https://your-server\nexport TESRIA_TOKEN=cct_your_token\nexport TESRIA_SPACE=SANDBOX'),
    p('In PowerShell on Windows:'),
    codeBlock('powershell', '$env:TESRIA_URL = "https://your-server"\n$env:TESRIA_TOKEN = "cct_your_token"\n$env:TESRIA_SPACE = "SANDBOX"'),
    p(c('your-server'), ' is the address you open Tesria at, and ', c('SANDBOX'), ' the key of the space to write in.'),
    step(3, 'If your Tesria has its own certificate'),
    p('A Tesria without a domain name uses a certificate it made itself, which a script refuses just as a browser warns about it (see ', pageLink('Trusting the local certificate'), '). Download the certificate from ', c('http://your-server/ca.crt'), ' and point the script at it: ', c('NODE_EXTRA_CA_CERTS=/path/to/tesria-ca.crt'), ' for JavaScript, ', c('SSL_CERT_FILE=/path/to/tesria-ca.crt'), ' for Python. Never turn certificate checks off instead: that would send your token to anyone who can intercept the connection.'),

    h(2, 'JavaScript'),
    p('Needs Node 18 or later. Save it as ', c('tesria.mjs'), ' and run ', c('node tesria.mjs'), '.'),
    codeBlock('javascript', example('tesria.mjs')),

    h(2, 'Python'),
    p('Needs Python 3.9 or later, and nothing else. Save it as ', c('tesria.py'), ' and run ', c('python3 tesria.py'), '.'),
    codeBlock('python', example('tesria.py')),

    h(2, 'What each part does'),
    ol(
      li(p(b('A helper that sends requests.'), ' Every request carries the token as ', c('Authorization: Bearer'), '. A refused request raises an error carrying the status and, when Tesria gives one, the ', c('code'), ' that says why, such as ', c('read_only_token'), '. See ', pageLink('Requests and responses'), ' for what each status means.')),
      li(p(b('Listing and searching.'), ' ', c('GET /api/spaces'), ' and ', c('GET /api/search'), ' answer with only what the token’s owner may see.')),
      li(p(b('Writing a page.'), ' A page’s content is a document of blocks (paragraphs, headings, lists), sent as JSON text in ', c('contentJson'), '. The scripts build the simplest one, a paragraph.')),
      li(p(b('Changing it safely.'), ' The script reads the page, changes it, and sends the version number it read as ', c('baseVersion'), '. If someone saved in between, Tesria answers ', c('409'), ' instead of overwriting them, and the script reads it again and retries.')),
      li(p(b('Labeling it.'), ' One request per label.')),
    ),
    p('For everything else the API can do, see ', pageLink('API reference'), '. To be told when something changes rather than asking, see ', pageLink('Webhooks'), ', which also has code to check that a message really came from Tesria.'),
  ))

  // =========================================================== API reference
  const methods = ['get', 'post', 'put', 'patch', 'delete']
  const schemaFields = (ref) => {
    const name = ref?.split('/').pop()
    const props = openapi.components?.schemas?.[name]?.properties
    return props ? Object.keys(props) : []
  }
  const endpointLine = (method, path, op) => {
    const query = (op.parameters ?? []).filter((x) => x.in === 'query').map((x) => x.name)
    const body = op.requestBody?.content?.['application/json']?.schema?.$ref
      ? schemaFields(op.requestBody.content['application/json'].schema.$ref)
      : op.requestBody?.content?.['multipart/form-data'] ? ['a file (multipart form)'] : []
    const parts = [text(`${method.toUpperCase()} ${path}`, code)]
    if (query.length) parts.push(text(`  query: ${query.join(', ')}`))
    if (body.length) parts.push(text(`  body: ${body.join(', ')}`))
    return li(p(...parts))
  }
  const byArea = new Map(AREAS.map(([tag]) => [tag, []]))
  for (const [path, item] of Object.entries(openapi.paths ?? {})) {
    for (const method of methods) {
      const op = item[method]
      if (!op) continue
      const tag = op.tags?.[0] ?? 'Tesria.Api'
      if (!byArea.has(tag)) byArea.set(tag, [])
      byArea.get(tag).push([method, path, op])
    }
  }
  const count = [...byArea.values()].reduce((n, list) => n + list.length, 0)
  const areaTitle = (tag) => ({ ApiTokens: 'API tokens', 'Tesria.Api': 'Health' })[tag] ?? tag
  const sections = []
  for (const [tag, list] of byArea) {
    if (!list.length) continue
    const what = AREAS.find(([t]) => t === tag)?.[1] ?? ''
    list.sort((x, y) => x[1].localeCompare(y[1]) || methods.indexOf(x[0]) - methods.indexOf(y[0]))
    sections.push(h(2, areaTitle(tag)), p(what), ul(...list.map(([m, path, op]) => endpointLine(m, path, op))))
  }
  await page('API reference', api, doc(
    p(`Every request this version of Tesria answers: ${count} of them, grouped by area. It is made from the OpenAPI document your Tesria serves, each time this site is published, so it lists what exists rather than what someone remembered to write down.`),
    p('Two more ways to see the same thing, on your own server:'),
    ul(
      li(p(c('https://your-server/api/docs'), ': the interactive reference, with every request’s fields and answers, and a ', b('Test Request'), ' button to try one. It needs no account to read.')),
      li(p(c('https://your-server/api/openapi.json'), ': the document itself, for tools that generate a client library from an OpenAPI description.')),
    ),
    h(2, 'Reading the list'),
    ul(
      li(p('Each line is the method and the path. ', c('{id}'), ' and the like are filled in: ', c('GET /api/pages/{id}'), ' becomes ', c('GET /api/pages/2f9a…'), '. After it come the query parameters it takes and the fields of its JSON body, when it has any.')),
      li(p(b('A read-only token may use any GET.'), ' Anything else needs a token with full access, and some of it needs a right on the instance or the space as well. What you may not see answers ', c('404'), ', exactly as if it did not exist.')),
      li(p(b('A token cannot manage its account:'), ' API tokens, the password, sessions, two-factor and the profile answer ', c('token_not_allowed'), '. Those are done in a browser.')),
      li(p(b('Administration'), ' is listed for completeness. Each of those requests needs the same right as its tab in Administration.')),
    ),
    toc(),
    ...sections,
  ))

  // ===================================================== How Tesria is built
  await page('How Tesria is built', developers, doc(
    p('A tour of the machinery, for anyone who wants to know what happens when they save a page, where their data lives, or where to start reading the code. It assumes you know what a web server and a database are; it does not assume you have read any of Tesria’s code.'),
    p('In one paragraph: Tesria is one web application (ASP.NET Core on .NET 10, with a React front end) in front of a PostgreSQL database, with a few small helper services beside it, all started together by Docker Compose. Every request is checked against the same permissions, whether it comes from the browser, a script or an assistant.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('The parts of Tesria', built, doc(
    p('Tesria is several programs, each in its own container, started together by ', c('docker compose up'), '. Only one of them, Caddy, is reachable from outside the computer; the rest talk to each other on a private network Docker makes for them.'),
    codeBlock('mermaid', [
      'flowchart LR',
      '  browser["Browser, script or assistant"] -->|"HTTPS"| caddy["caddy"]',
      '  subgraph server["The computer Tesria runs on"]',
      '    caddy -->|"everything else"| app["app"]',
      '    caddy -->|"/collab"| collab["collab"]',
      '    app --> db[("db: PostgreSQL")]',
      '    app --> uploads[("uploads volume")]',
      '    collab --> db',
      '    app -->|"render this page"| pdf["pdf"]',
      '    pdf -->|"reads it back"| app',
      '    db --> pgbackrest["pgbackrest"]',
      '    db --> backup["backup"]',
      '    uploads --> backup',
      '  end',
    ].join('\n')),
    ul(
      li(p(b('caddy'), ' answers on ports 80 and 443, and makes and renews the HTTPS certificate by itself: from Let’s Encrypt for a real domain, or its own for a name on your network. It sends ', c('/collab'), ' to the collaboration service and everything else to the app.')),
      li(p(b('app'), ' is Tesria itself: the REST API under ', c('/api'), ', the MCP server under ', c('/mcp'), ', and the web pages, all from one process. It runs as a database role that may read and write pages but cannot alter the audit log.')),
      li(p(b('db'), ' is PostgreSQL 18, with every page, version, comment and setting. Attachments are files in the ', b('uploads'), ' volume, not in the database.')),
      li(p(b('collab'), ' is a small Node program that lets several people edit a page at once (see ', pageLink('Editing together'), '). Optional: without its secret, the editor works for one person at a time.')),
      li(p(b('pdf'), ' is a headless browser. To export a page as HTML or PDF, or a space as a website, the app asks it to open the page the way a reader would and capture it (see ', pageLink('Exports and wiki packs, in the code'), '). Optional too.')),
      li(p(b('pgbackrest'), ' and ', b('backup'), ' back the database and the attachments up, and report what they did by writing to tables the app only reads (see ', pageLink('How backups work'), ').')),
      li(p(b('tailscale'), ', only when started with ', c('--profile tailscale'), ', puts Tesria on your tailnet (see ', pageLink('Reaching Tesria from anywhere with Tailscale'), ').')),
    ),
    p('Everything that must survive lives in named Docker volumes (the database, attachments, backups, and Caddy’s certificates), never inside a container, so rebuilding or upgrading a container loses nothing.'),
  ))

  await page('Following a request', built, doc(
    p('What happens between choosing ', b('Update'), ' on a page and seeing it saved, step by step. A script’s request takes the same path, with a token where the browser has a cookie.'),
    codeBlock('mermaid', [
      'sequenceDiagram',
      '  participant B as Browser',
      '  participant C as caddy',
      '  participant A as app',
      '  participant D as db',
      '  B->>C: PUT /api/pages/{id} (HTTPS)',
      '  C->>A: forwarded, with the address it came from',
      '  A->>A: who is this? (cookie or token)',
      '  A->>A: limits, CSRF header, token scope',
      '  A->>D: may this person edit this page?',
      '  D-->>A: yes',
      '  A->>D: save a new version',
      '  A-->>B: 200, the page as saved',
      '  A--)A: afterwards: notifications, webhooks',
    ].join('\n')),
    ol(
      li(p(b('Caddy'), ' takes the HTTPS connection and passes the request to the app, adding the address it came from. The app believes that address only from the private network Caddy is on, and only the entry Caddy adds, so a caller cannot claim to be someone else.')),
      li(p(b('Who is asking.'), ' A browser sends its session cookie; a script sends an API token as ', c('Authorization: Bearer'), '. Either way the app ends up with one person, and from then on treats them the same.')),
      li(p(b('Checks that apply to everyone:'), ' rate limits on signing in and on anonymous requests; for a browser, the ', c('X-Requested-With: Tesria'), ' header that other websites cannot send (so they cannot act with your session); for a token, whether it is read-only, and that it is not trying to manage its own account.')),
      li(p(b('Permission.'), ' Each request then asks the permission service the question it needs: may this person view this space, edit this page, use this right? Anything they may not see answers ', c('404'), ', exactly like something that does not exist. See ', pageLink('Who can see what, in the code'), '.')),
      li(p(b('The work.'), ' A page update checks the version it started from (a mismatch is ', c('409'), '), saves a new, permanent version, and writes the audit log for anything administrative.')),
      li(p(b('Afterwards,'), ' without holding up the answer: notifications to watchers, emails, and webhook calls to other programs.')),
    ),
    p('The web pages themselves are served by the same app: every address that is not ', c('/api'), ' or ', c('/mcp'), ' gets the React application, which then asks the API for what it shows.'),
  ))

  await page('Editing together', built, doc(
    p('A page is stored as a structured document, not as HTML: a list of blocks (paragraphs, headings, tables, panels), each with its text and settings. The editor, built on TipTap and ProseMirror, works on that same structure, so what is saved is exactly what was on screen.'),
    ul(
      li(p(b('One definition of every block.'), ' Which blocks exist, and what each may hold, is defined once, in the web app’s editor extensions. The editor, the collaboration service and the exports all use it, so a block cannot mean one thing in the editor and another in a PDF.')),
      li(p(b('Every save is a new version.'), ' A version is never changed after it is written, which is what makes a page’s history trustworthy and lets any version be brought back.')),
      li(p(b('Editing at the same time'), ' goes through the ', b('collab'), ' service, using Yjs, a library that merges changes from several people without conflicts. The app decides who may join: the editor asks it for a short-lived token for that one page, which the collaboration service checks before letting it in. The shared draft is kept in the database, so a restart loses nothing, and choosing ', b('Update'), ' still saves an ordinary version.')),
      li(p(b('Changes from scripts and assistants'), ' arrive while people may be editing. They are saved as a version like any other, and anyone with the page open sees them highlighted, to accept or reject. See ', pageLink('Changes from assistants and the API'), '.')),
    ),
  ))

  await page('Who can see what, in the code', built, doc(
    p('Tesria’s permissions come in two layers: what someone may do on the instance at all, and what they may do in each space. Every check goes through one permission service, whether the request came from the browser, the REST API, an assistant through MCP, live content on a page, search or an export.'),
    h(2, 'On the instance'),
    p('Each person has a role (Owner, Administrator, User, or one an administrator made), and a role is a set of named rights, such as ', i('create spaces'), ', ', i('use API tokens'), ' or ', i('see the user list'), '. Every administration request names the right it needs. See ', pageLink('Roles'), '.'),
    h(2, 'In a space'),
    ul(
      li(p(b('Open until closed.'), ' A space with no permissions set can be read and edited by everyone signed in. Once any permission is set, only the people and groups it names get in.')),
      li(p(b('Three levels:'), ' view, edit and administer, each including the ones before it.')),
      li(p(b('Page restrictions'), ' narrow a page further, to named people or groups, for viewing or for editing, and apply to every page under it too. The space’s administrators can always reach every page, so a space can always be looked after.')),
      li(p(b('Anonymous readers'), ' are a separate, deliberately weak principal: they see a space only if the whole wiki allows public spaces and that space has been published.')),
    ),
    h(2, 'Rules that hold everywhere'),
    ul(
      li(p(b('Not found, not forbidden.'), ' Something you may not see answers ', c('404'), ', so nobody can learn that a private page exists by guessing its address.')),
      li(p(b('A token is its owner.'), ' An API token carries exactly its owner’s permissions, or fewer if it is read-only, and never more. An export’s render token is narrower still: one page or one space, for fifteen minutes.')),
      li(p(b('Lists are filtered, not trimmed.'), ' Search, notifications, live content and the admin screens leave out what the person looking may not see, rather than showing it and hiding the link.')),
    ),
  ))

  await page('Exports and wiki packs, in the code', built, doc(
    h(2, 'Exports are photographs'),
    p('An HTML or PDF export, and every page of an exported website, is captured rather than rebuilt: the app asks the ', b('pdf'), ' service’s browser to open the page as a reader would and save what it shows. So an export looks exactly like the page, live content included, and a new kind of block needs no export code of its own.'),
    p('The browser signs in with a ', b('render token'), ': made for that export, valid for fifteen minutes, and good only for the one page (or the one space, for a website) being exported. It carries the exporter’s permissions, or none at all for a public website, which is why a website exported ', i('as the public sees it'), ' cannot contain anything private.'),
    h(2, 'Wiki packs are the documents'),
    p('A wiki pack is the opposite: not a picture of the space but the space itself, as readable JSON files in a zip, with every version, comment, label, template and attachment, so another Tesria can read it back. See ', pageLink('Wiki packs'), '.'),
    ul(
      li(p(b('A format number, checked first.'), ' Adding an optional field leaves it alone; changing what a field means raises it. A pack from a newer format is refused with a message naming the Tesria that made it.')),
      li(p(b('Older packs are upgraded before they are read,'), ' one format at a time, the way a database is migrated. A pack made by Tesria 0.5 or later imports into every later version, and the tests import one made by each release that changed the format.')),
      li(p(b('Untrusted until proven otherwise.'), ' A pack is an upload from somewhere else, so its size, its number of files and every file name are checked before anything in it is used, and imported pages are rebuilt from their content rather than trusted.')),
    ),
  ))

  // ============================================================ Contributing
  await page('Contributing', developers, doc(
    p('Tesria is open source, under the Apache License 2.0. You can read the code, run your own copy, change it, and propose your changes back. These pages cover what you need: building it, testing it, how the code is laid out, how a change is proposed, and how a release is made.'),
    p('Everyone taking part follows the ', pageLink('Code of conduct'), '. If you have found a security problem, do not open an issue: see ', pageLink('Security'), '.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Building from source', contributing, doc(
    p('Tesria is developed the same way it is run: with Docker. You change the code on your computer, rebuild the part you changed, and look at it in the browser.'),
    h(2, 'What you need'),
    ul(
      li(p(b('Docker'), ' with Compose v2: Docker Desktop on a Mac or Windows, or Docker Engine on Linux.')),
      li(p(b('Git.'))),
      li(p(b('For the tests:'), ' the .NET 10 SDK, and Node.js 22 with npm.')),
    ),
    step(1, 'Get the code'),
    codeBlock('bash', 'git clone https://github.com/Tesria/Tesria.git\ncd Tesria'),
    step(2, 'Make your settings file'),
    p('Copy ', c('.env.example'), ' to ', c('.env'), ' and fill in the passwords and keys it asks for. ', pageLink('Installing with Docker Compose'), ' explains each one. For development, ', c('DOMAIN=localhost'), ' is right.'),
    step(3, 'Build and start everything'),
    codeBlock('bash', 'docker compose up -d --build'),
    p('The first build takes several minutes: it compiles the .NET app and the web app, and downloads the other images. Then open ', c('https://localhost'), ' and the setup wizard makes your account.'),
    step(4, 'After a change, rebuild what you changed'),
    p('The web app and the API are built into one image, ', b('app'), ', so a change to either is:'),
    codeBlock('bash', 'docker compose up -d --build app'),
    p('A change to the collaboration service or the PDF service rebuilds ', c('collab'), ' or ', c('pdf'), ' the same way. Reload the browser to see it.'),
    panel('note', p(b('Start the whole stack, not the database alone.'), ' On a new database, PostgreSQL stops every few seconds until the ', c('pgbackrest'), ' service has set up its backups, because it cannot archive its changes yet. ', c('docker compose up -d'), ' starts both.')),
  ))

  await page('Running the tests', contributing, doc(
    p('Every change must keep the tests passing, and the same tests run on every push and pull request on GitHub. None of them needs Docker.'),
    h(2, 'The server'),
    codeBlock('bash', 'dotnet test tests/Api.Tests'),
    p('Over nine hundred tests, run against an in-memory SQLite database that stands in for PostgreSQL, so they need no database at all. They take about eight minutes. Most start the whole app in memory and talk to it over HTTP, the way a real client would.'),
    h(2, 'The web app'),
    codeBlock('bash', 'cd src/web\nnpm ci\nnpm run build\nnpm run lint\nnpm test'),
    p('The build includes a strict TypeScript check. The web app’s tests are for logic with edge cases, not for how screens look.'),
    h(2, 'Dependencies'),
    codeBlock('bash', 'scripts/audit.sh'),
    p('Checks every dependency, of the web app, the collaboration service and the server, against published vulnerabilities. It must pass before a release, and after any change to a package list.'),
    h(2, 'Looking at it'),
    p('Tests prove logic, not that a screen works. For a change you can see, rebuild the app and try it in a browser: every screen it touches, signed in and signed out, on a phone-sized window as well as a wide one.'),
  ))

  await page('How the code is organized', contributing, doc(
    ul(
      li(p(c('src/Api'), ': the server. ', c('Features/'), ' holds one folder per feature (Pages, Spaces, Export and so on), each with its own endpoints; ', c('Domain/'), ' the data; ', c('Infrastructure/'), ' what the features share, such as the database, permissions, authentication, email and storage; ', c('Program.cs'), ' wires it together.')),
      li(p(c('src/web'), ': the web app, in React and TypeScript. ', c('routes/'), ' holds a file per screen, ', c('components/'), ' the pieces they share, ', c('editor/'), ' the editor, and ', c('api/client.ts'), ' every call to the server.')),
      li(p(c('collab/'), ': the collaboration service. ', c('deploy/'), ': the Dockerfiles, Caddy’s configuration, and the backup services’ scripts.')),
      li(p(c('tests/Api.Tests'), ': the server’s tests. ', c('scripts/'), ': tools, including the screenshot harness and the scripts that write this site.')),
      li(p(c('docs/'), ': the architecture, security model, backup runbook, the changelog, and the plan.')),
    ),
    h(2, 'Conventions'),
    ul(
      li(p(b('Database changes are migrations.'), ' Change the model, then from ', c('src/Api'), ' run ', c('dotnet ef migrations add <Name> --output-dir Infrastructure/Migrations'), '. Migrations run by themselves when the app starts.')),
      li(p(b('A new kind of block'), ' is defined once, in the web app’s ', c('editor/extensions.ts'), ', never in one editor alone: the collaboration service and the exports rely on the same definition.')),
      li(p(b('What you may not see does not exist.'), ' A request for something the caller may not see answers ', c('404'), ', never ', c('403'), '.')),
      li(p(b('Every change is written down.'), ' An entry in ', c('docs/CHANGELOG.md'), ' saying what changed and why, and ', c('docs/architecture.md'), ' updated if how something works has changed.')),
      li(p(b('Words people read'), ' are in US English, plain and specific, without em dashes. The Support site follows ', c('scripts/support/WRITING.md'), '.')),
    ),
  ))

  await page('Proposing a change', contributing, doc(
    p('Tesria takes changes as pull requests on GitHub.'),
    step(1, 'Say what you want to change, first'),
    p('For anything more than a small fix, open an issue describing the problem and what you have in mind before you write the code. It is the quickest way to find out whether it fits, and whether someone is already on it.'),
    step(2, 'Make the change on a branch'),
    p('Fork the repository, make a branch, and keep it to one change: a fix and an unrelated tidy-up are two pull requests.'),
    step(3, 'Test it, and look at it'),
    p('The tests pass (see ', pageLink('Running the tests'), '), and you have tried what you changed in a browser. Add tests for new behavior.'),
    step(4, 'Open the pull request'),
    p('Say what it changes, why, and how you checked it. GitHub runs the tests on it. A maintainer reviews it, may ask for changes, and merges it when it is ready.'),
    h(2, 'The license of what you send'),
    p('Tesria is under the Apache License 2.0, and so is anything you contribute to it: the license itself says that a contribution you submit is under its terms unless you say otherwise. Only send what you have the right to send.'),
  ))

  await page('Making a release', contributing, doc(
    p('How a new version of Tesria is made. Versions are numbered like ', c('0.6.0'), ': the last number changes for fixes, the middle one for new features, and the first will become 1 when Tesria promises to stay compatible.'),
    step(1, 'Make sure everything passes'),
    p('The tests, ', c('scripts/audit.sh'), ', and a look at the app in a browser.'),
    step(2, 'Write the release down'),
    p('In ', c('docs/CHANGELOG.md'), ', rename the ', c('[Unreleased]'), ' section to the new version and today’s date, with a short list of highlights at its top, and start a new empty ', c('[Unreleased]'), '. Add the version’s page under ', pageLink('Release notes'), ', for readers of this site.'),
    step(3, 'Tag it'),
    codeBlock('bash', 'git tag -a v0.6.0 -m "Tesria 0.6.0"\ngit push origin v0.6.0'),
    p('The tag starts the release on GitHub: the tests run again, the app’s image is built with the tag’s version stamped into it, and a GitHub release is published with the changelog’s section for it.'),
    step(4, 'Start the next version'),
    p('Set the next version, with ', c('-dev'), ', in ', c('src/Api/Api.csproj'), ' and ', c('src/web/package.json'), ', so a build between releases can never be mistaken for one.'),
    panel('note', p(b('If the wiki pack format changed,'), ' the release also adds the upgrade from the previous format, and a pack made by the previous release to ', c('tests/Api.Tests/Packs'), ', so every later version is tested against it.')),
  ))

  // ======================================================= Project documents
  await page('Project documents', developers, doc(
    p('The rules and promises of the Tesria project itself: how people treat each other, who decides what, where to get help, how security problems are handled, and the license. Each is also a file in the repository, so it travels with the code.'),
    ul(
      li(p(pageLink('Code of conduct'), ' (', c('CODE_OF_CONDUCT.md'), ')')),
      li(p(pageLink('Governance'), ' (', c('GOVERNANCE.md'), ')')),
      li(p(pageLink('Getting help'), ' (', c('SUPPORT.md'), ')')),
      li(p(pageLink('Security', 'The security policy'), ' (', c('SECURITY.md'), ')')),
      li(p(pageLink('License and credits', 'The license and third-party notices'), ' (', c('LICENSE'), ', ', c('NOTICE'), ')')),
      li(p(pageLink('Release notes', 'The changes in each version'), ' (', c('docs/CHANGELOG.md'), ')')),
      li(p(pageLink('Contributing', 'How to contribute'), ' (', c('CONTRIBUTING.md'), ')')),
    ),
  ))

  await page('Code of conduct', project, doc(
    p('Tesria wants to be a project anyone can take part in: asking a question, reporting a bug, or proposing a change. That only works if people are treated well, so everyone taking part agrees to this.'),
    h(2, 'What we expect'),
    ul(
      li(p('Be kind and patient, especially with people who are new. Everyone was once.')),
      li(p('Disagree about ideas, not people. Assume good intent, and say what you think plainly and politely.')),
      li(p('Take criticism of your work gracefully, and give it carefully.')),
      li(p('Own your mistakes, and help put them right.')),
    ),
    h(2, 'What is not acceptable'),
    ul(
      li(p('Harassment, insults, or demeaning remarks about anyone, for any reason, including who they are or where they come from.')),
      li(p('Sexual language or attention, threats, and encouraging anyone to harm themselves.')),
      li(p('Publishing someone’s private information without their permission.')),
      li(p('Continuing something after being asked to stop.')),
    ),
    h(2, 'Where it applies'),
    p('Everywhere the project is: its repository, issues and pull requests, its discussions, and anywhere you represent it.'),
    h(2, 'Reporting a problem'),
    p('Write to the address in ', pageLink('Security'), ' with ', c('[tesria conduct]'), ' in the subject, saying what happened and where. Reports are read by the maintainer, kept confidential, and answered.'),
    p('Depending on what happened, the response may be a private word, a public warning, or a ban from the project’s spaces, for a time or for good.'),
    p(i('Inspired by the Contributor Covenant.')),
  ))

  await page('Governance', project, doc(
    p('How decisions about Tesria are made, stated plainly so nobody has to guess.'),
    ul(
      li(p(b('One maintainer, for now.'), ' Tesria has a single maintainer, who decides what goes in, reviews and merges changes, and makes releases.')),
      li(p(b('Decisions in the open.'), ' Proposals and the reasons for decisions are written down in issues and pull requests, and the direction of the project in its plan and roadmap in ', c('docs/'), '.')),
      li(p(b('Anyone may propose anything.'), ' See ', pageLink('Proposing a change'), '. A proposal may be turned down; when it is, the reason is given.')),
      li(p(b('This will change as the project grows.'), ' When others take on regular responsibility, they become maintainers, and this page will say how decisions are shared between them.')),
    ),
  ))

  await page('Getting help', project, doc(
    p('Stuck with something? Here is where to look, in order.'),
    ol(
      li(p(b('This site.'), ' Search it, or start at ', pageLink('Troubleshooting'), ' and the ', pageLink('FAQ'), '.')),
      li(p(b('Your administrator,'), ' if someone else runs your Tesria: account problems, access to a space and the like are theirs to solve.')),
      li(p(b('An issue on GitHub,'), ' for a bug or a question the site does not answer. Say which version you run (see ', pageLink('Release notes'), ' for where to find it), what you did, what you expected, and what happened instead.')),
    ),
    p('Security problems are never reported in an issue: see ', pageLink('Security'), '.'),
    p('Tesria is offered as it is, without a support contract. Questions are answered as time allows.'),
  ))
}
