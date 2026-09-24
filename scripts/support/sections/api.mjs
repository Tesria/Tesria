// REST API and MCP (dev-plan 10.5).
//
// Facts from src/Api (the Map* calls, the token handler, the scope and CSRF
// middleware, OpenApiSetup, the webhook sender and TesriaTools), gathered
// 2026-09-23 after that day's fixes (read-only tokens refused a live-editing
// token, a rename no longer empties a page, MCP space keys ignore case).
// The complete, current reference is the instance's own /api/docs; these
// pages are the guide around it.

export const shots = () => [
  { name: 'api-docs', url: '/api/docs', anon: true, settle: 2500, steps: [{ wait: 4000 }], phone: false },
]

export async function build({ top, page, ensure, figure, doc, p, h, text, bold, code, ul, ol, li, panel, table, codeBlock, live }) {
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)

  // ================================================================ REST API
  const api = top['REST API']
  await page('REST API', null, doc(
    p('Everything the web app does, it does through a JSON API under ', c('/api'), ', and scripts can use the same API with a token.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  const start = await ensure('Getting started with the API', api)
  await page('Getting started with the API', api, doc(
    ol(
      li(p('Create a token on your profile, under ', b('API tokens'), '. Tick ', b('Read-only'), ' if the script only reads.')),
      li(p('Send it with every request.')),
    ),
    codeBlock('bash', 'curl -H "Authorization: Bearer $TESRIA_TOKEN" https://wiki.example.com/api/spaces'),
    p('A token acts as you: it sees and changes exactly what you could in the browser, and stops working if you are suspended or your role loses ', b('Use API tokens'), '.'),
    h(2, 'The reference'),
    p('Every Tesria serves its own reference at ', c('/api/docs'), ', generated from the code, so it always matches the version you run. The same description, in OpenAPI format, is at ', c('/api/openapi.json'), ' for tools that generate clients.'),
    ...(await figure(start, 'api-docs', 'The API reference at /api/docs')),
    h(2, 'What a token cannot do'),
    ul(
      li(p(b('Read-only tokens'), ' are refused on every POST, PUT, PATCH and DELETE with ', c('403 {"code":"read_only_token"}'), ', and cannot join live editing.')),
      li(p('Actions that ask for your password again in the browser (changing roles, publishing a space, changing branding, the retention policy, permanently deleting a page, and a few more) always refuse a token: ', c('403 {"code":"reauth_required"}'), '.')),
    ),
    panel('warning', p('Tokens do not expire. Revoke the ones you no longer use, and give each script its own, so one can be revoked without breaking the others.')),
  ))

  await page('Requests and responses', api, doc(
    h(2, 'Page content'),
    p('A page’s body is sent and received as ', c('contentJson'), ': the document as a JSON string, in the editor’s own format (ProseMirror). The easiest way to see the format is to read a page you have written in the editor:'),
    codeBlock('bash', 'curl -H "Authorization: Bearer $TESRIA_TOKEN" https://wiki.example.com/api/pages/<page-id>'),
    p('To read a page as Markdown instead, export it: ', c('GET /api/pages/<id>/export?format=markdown'), '. To write Markdown, use MCP, which converts it.'),
    h(2, 'Updating without overwriting'),
    p(c('PUT /api/pages/<id>'), ' takes ', c('title'), ', ', c('contentJson'), ' and ', c('changeComment'), ', all optional: leave out ', c('contentJson'), ' to rename a page without changing its body. Add ', c('baseVersion'), ', the version number you read, and the update is refused with ', c('409'), ' and the current page if someone changed it since, instead of overwriting their change.'),
    h(2, 'Status codes'),
    table([
      ['Code', 'Means'],
      ['400', 'Something in the request is wrong; the body lists which field and why.'],
      ['401', 'Not signed in, or the token is not valid.'],
      ['403', 'You can see it but may not do this. The body says why when there is a reason code.'],
      ['404', 'It does not exist, or you are not allowed to know it exists: a restricted page answers 404, not 403.'],
      ['409', 'A conflict: a duplicate space key, the last administrator of a space, or a stale baseVersion.'],
      ['413', 'Too large: 25 MB for an attachment, 500 MB for a wiki pack, 100 MB for anything else.'],
      ['429', 'Too many requests; wait for the seconds in Retry-After.'],
      ['503', 'A restore is in progress and the wiki is read-only, or an export needs a renderer this instance does not have.'],
    ], [100, 600]),
    h(2, 'Other things to know'),
    ul(
      li(p('Enumerations are numbers. In permissions, ', c('principalType'), ' is 0 for a user and 1 for a group, and ', c('operation'), ' is 0 View, 1 Edit, 2 Admin.')),
      li(p('Lists return everything, except search (50), notifications and the audit log (up to 200), and a few others the reference notes.')),
      li(p('A browser session, as opposed to a token, must send ', c('X-Requested-With: Tesria'), ' on every change, which stops other websites acting with your cookie. Tokens do not need it.')),
      li(p('Signed-in requests and valid tokens are not rate-limited; anonymous requests are limited to 300 a minute per address.')),
    ),
  ))

  await page('What the API covers', api, doc(
    p('An outline; the reference at ', c('/api/docs'), ' has every endpoint and field.'),
    table([
      ['Area', 'Endpoints'],
      ['Spaces', 'List, create, read, update, archive, delete, export switches, import a pack'],
      ['Pages', 'Tree, trash, create, draft and publish, read, update, move, full width, delete, restore, purge'],
      ['Versions', 'List, read one, restore one'],
      ['Attachments', 'Upload, list, read, download, delete'],
      ['Comments', 'List, add, edit, delete'],
      ['Labels', 'Labels in use, pages with a label, add to and remove from a page'],
      ['Search', 'Full-text search, optionally in one space'],
      ['Export', 'A page as Markdown, HTML or PDF; a space as a website or a wiki pack'],
      ['Permissions', 'Space permissions and page restrictions'],
      ['Groups, users', 'Groups and their members; the list of accounts'],
      ['Watches, notifications', 'Watch pages and spaces; read and mark notifications'],
      ['Webhooks', 'List, create and delete a space’s webhooks'],
      ['Templates', 'List, create, rename, delete'],
      ['Live content', 'Render any live content block for a page'],
      ['Your account', 'Profile, email, password, sessions, two-factor, recovery codes, API tokens'],
      ['Administration', 'Settings, users, spaces, invites, security, roles, backups, branding, audit, dashboard, each needing its right'],
      ['Health', 'GET /api/health, without signing in'],
    ], [200, 500]),
  ))

  await page('Webhooks', api, doc(
    p('A webhook tells another system when something happens in a space, by sending it a POST. Space administrators set them up in ', b('Space settings → Webhooks'), ': a URL, and the events to send.'),
    table([
      ['Event', 'Sent when'],
      ['page.created', 'A page is published for the first time'],
      ['page.updated', 'A page is updated, or restored to an earlier version'],
      ['comment.created', 'Someone comments on a page'],
      ['*', 'All of the above'],
    ], [200, 500]),
    h(2, 'What arrives'),
    codeBlock('json', '{\n  "event": "page.updated",\n  "targetType": "page",\n  "targetId": "6f1c…",\n  "metadata": { "Title": "Launch plan" },\n  "timestamp": "2026-09-23T14:30:00Z"\n}'),
    p('For comments, ', c('metadata'), ' holds ', c('Body'), ': the comment’s first 140 characters. Note the capitalized keys inside ', c('metadata'), '.'),
    h(2, 'Checking it came from Tesria'),
    p('When you create a webhook, Tesria shows its signing secret once. Every delivery carries ', c('X-Webhook-Signature: sha256=<hex>'), ', an HMAC-SHA256 of the exact request body, keyed with the secret as text. Compute the same and compare:'),
    codeBlock('javascript', "import { createHmac, timingSafeEqual } from 'node:crypto'\n\nfunction fromTesria(rawBody, header, secret) {\n  const expected = 'sha256=' + createHmac('sha256', secret).update(rawBody).digest('hex')\n  return header.length === expected.length && timingSafeEqual(Buffer.from(header), Buffer.from(expected))\n}"),
    h(2, 'Delivery'),
    ul(
      li(p('Sent after the change is saved, one at a time, with up to three attempts (after 2 and then 4 seconds) if the receiver fails or does not answer within 5 seconds.')),
      li(p('Waiting deliveries are held in memory: a restart of Tesria loses them.')),
      li(p('A webhook cannot point at the server’s own network or any private address; Tesria refuses it when it is created and again when it sends.')),
    ),
  ))

  // ===================================================================== MCP
  const mcp = top['MCP']
  await page('MCP', null, doc(
    p('Tesria has a built-in Model Context Protocol (MCP) server, so AI assistants such as Claude can search, read and write your wiki as you, within your permissions.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Connecting an assistant', mcp, doc(
    p('The server is at ', c('https://wiki.example.com/mcp'), ' (your own address), over Streamable HTTP. It is always on, and it accepts API tokens only.'),
    ol(
      li(p('Create an API token on your profile. Tick ', b('Read-only'), ' if the assistant should only read.')),
      li(p('Add the server to your assistant with that token as a Bearer header.')),
    ),
    h(2, 'Claude Code'),
    codeBlock('bash', 'claude mcp add --transport http tesria https://wiki.example.com/mcp --header "Authorization: Bearer cct_your-token"'),
    h(2, 'Other clients'),
    p('Any client that supports remote MCP servers over HTTP works: give it the URL and the header ', c('Authorization: Bearer <token>'), '.'),
    panel('warning', p('The assistant can do whatever the token can. Use a read-only token unless it needs to write, and revoke the token to cut it off.')),
  ))

  await page('What an assistant can do', mcp, doc(
    table([
      ['Tool', 'Does'],
      ['list_spaces', 'The spaces you can see.'],
      ['get_space_tree', 'A space’s pages, as a tree.'],
      ['search_pages', 'Full-text search, optionally in one space, up to 50 results.'],
      ['find_pages_by_label', 'Pages with a label.'],
      ['list_labels', 'A space’s labels, most used first.'],
      ['get_page', 'A page as Markdown (or the editor’s JSON), with its outline. It can ask for just one section, by heading.'],
      ['create_page', 'A new page, from Markdown. Needs a token that can write.'],
      ['update_page', 'Replaces a page’s body from Markdown, or renames it when given only a title. Needs a token that can write.'],
      ['add_page_label, remove_page_label', 'Labels. Need a token that can write.'],
    ], [240, 460]),
    h(2, 'Writing from Markdown'),
    p('Headings, paragraphs, bold, italic, strikethrough, code, links, bullet, ordered and task lists, code blocks with a language, quotes, tables, dividers and pictures convert into the editor’s elements. Panels, statuses, layouts and live content cannot be written this way; they stay as plain text.'),
    h(2, 'When someone has the page open'),
    p('An assistant’s change appears in any editor that has the page open as tracked changes, marked as coming from MCP, for the person editing to accept or reject. See ', b('Changes from assistants and the API'), '. The new version is credited to the token’s owner.'),
    p('A page the token cannot see answers “not found”, the same as one that does not exist.'),
  ))
}
