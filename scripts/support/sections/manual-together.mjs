// User manual: Working together, Finding things, Exporting and publishing,
// and Your profile (dev-plan 10.5).
//
// Facts from the comments, mentions, notifications, collaboration, search,
// labels, export, site export, pack, public-reading and profile code,
// gathered 2026-09-23 after that day's fixes (notifications checked against
// access, export options shown only with the right, search filling up to 50).

const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`

/**
 * Gives Alex something ordinary in the bell. Alex is an administrator, so the
 * bell otherwise holds only security alerts, which is not what a reader's
 * looks like. Alex watches Tesria Demo, and Sam comments on one page and
 * updates another, once: only while Alex has no page notifications, so
 * running this again adds nothing.
 */
export async function prepare({ lib, author, demoId }) {
  const notes = await author.call('GET', '/api/notifications')
  if (notes.some((n) => n.targetType === 'page')) return
  await author.call('POST', '/api/spaces/DEMO/watch')
  const sam = await lib.signIn(lib.need('SHOT2_EMAIL'), lib.need('SHOT2_PASSWORD'))
  const faq = demoId('Support FAQ')
  await sam.call('POST', `/api/pages/${faq}/comments`,
    { body: 'Added the answer about offline editing, from the design review.', parentCommentId: null, anchorJson: null })
  const arch = demoId('Architecture overview')
  const current = await sam.call('GET', `/api/pages/${arch}`)
  await sam.call('PUT', `/api/pages/${arch}`,
    { title: current.title, contentJson: current.contentJson, changeComment: 'Checked against the release candidate' })
}

export const shots = ({ demo }) => [
  {
    name: 'comments', url: demo('Launch plan'), settle: 1200,
    steps: [{ wait: 2500 }, { click: 'article .tabs button:has-text("Comments")' }, { wait: 800 }, { scrollTo: '.tab-panel' }],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
  },
  {
    name: 'bell', url: demo('Launch plan'), settle: 800,
    // The security alerts an administrator also gets are left out: the
    // picture is of what anyone's bell looks like.
    steps: [{ wait: 2500 }, { click: '.notif__bell' }, { wait: 800 },
      { eval: "document.querySelectorAll('.notif__dropdown .notif__item').forEach((e) => { if (e.textContent.startsWith('Security')) e.remove() })" }],
    clipTo: '.notif__dropdown', clipPad: 8,
  },
  { name: 'search', url: '/search?q=launch', settle: 1500, steps: [{ wait: 2500 }], clipTo: '.page-wrap' },
  { name: 'label-page', url: '/labels/meeting-notes', settle: 1500, steps: [{ wait: 2500 }], clipTo: '.page-wrap' },
  { name: 'exports-switches', url: '/spaces/DEMO/settings', settle: 1200, steps: [{ wait: 2500 }], clipTo: '#exports' },
  { name: 'site-export', url: '/spaces/DEMO/settings', settle: 1200, steps: [{ wait: 2500 }], clipTo: '#export' },
  { name: 'pack-export', url: '/spaces/DEMO/settings', settle: 1200, steps: [{ wait: 2500 }], clipTo: '#pack' },
  {
    name: 'pack-import', url: '/spaces', settle: 800,
    steps: [{ wait: 2500 }, { click: 'button:has-text("Import a pack")' }, { wait: 500 }],
    clipTo: 'form.card', clipPad: 12,
  },
  { name: 'admin-spaces', url: '/admin/spaces', settle: 1500, steps: [{ wait: 2500 }], clipTo: 'table.admin-table' },
  { name: 'profile', url: '/profile', settle: 1500, steps: [{ wait: 2500 }] },
  // The account that takes these pictures signs in on every run, so its list
  // is long; the first few rows show what a list looks like.
  { name: 'sessions', url: '/profile', settle: 1200, steps: [{ wait: 2500 }, { css: `${section('Sessions').replace(':has(> h2:text-is("Sessions"))', '')} tbody tr:nth-child(n+4) { display: none }` }], clipTo: section('Sessions') },
  { name: 'email-notifications', url: '/profile', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('Email notifications') },
  { name: 'api-tokens', url: '/profile', settle: 1200, steps: [{ wait: 2500 }], clipTo: section('API tokens') },
]

export async function build({ top, page, ensure, figure, doc, p, h, text, bold, code, ul, ol, li, panel, table, live }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)

  // ======================================================= Working together
  const together = await ensure('Working together', manual)
  await page('Working together', manual, doc(
    p('Editing with other people, discussing a page, and hearing about changes.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Editing at the same time', together, doc(
    p('Where your Tesria has live editing turned on, several people can edit a page at once. Everyone’s changes appear as they type, and each person’s cursor shows in its own color with their name.'),
    p('The bar under the editor says how it is going: ', b('Live: changes are shared as you type'), ', ', b('Connecting to collaboration…'), ', or ', b('Offline: your changes are local until reconnected'), '.'),
    ul(
      li(p('Unpublished changes to an existing page are kept in a shared draft: you can leave and come back, and so can the others. ', b('Update'), ' publishes everyone’s changes as one new version.')),
      li(p('A brand-new page is edited by one person until it is published.')),
      li(p('If someone updates a page while you are editing it, publishing is refused and the difference is highlighted for you to accept or reject first.')),
    ),
    panel('note', p('Without live editing (an administrator turns it on with ', c('COLLAB_SHARED_SECRET'), '), each person edits alone, the last to update wins, and unpublished changes are lost when you leave the editor.')),
  ))

  const comments = await ensure('Comments', together)
  await page('Comments', together, doc(
    p('Anyone who can read a page can comment on it.'),
    ...(await figure(comments, 'comments', 'Comments under a page')),
    h(2, 'On the whole page'),
    p('Open the ', b('Comments'), ' tab under the page, write in ', b('Add a comment…'), ' and choose ', b('Post'), '. ', b('Reply'), ' answers a comment; replies nest under it.'),
    h(2, 'On part of a page'),
    ol(
      li(p('Choose ', b('Edit'), ', then select the words you want to discuss.')),
      li(p('Choose ', b('Comment on this selection'), ' in the bubble above the selection, write, and choose ', b('Comment'), '.')),
      li(p('Update the page, so the highlight is saved.')),
    ),
    p('Readers click the highlighted words to see the conversation and reply. Pictures have their own ', b('Comment'), ' button.'),
    h(2, 'Changing and deleting'),
    p('You can ', b('Edit'), ' and ', b('Delete'), ' your own comments. A deleted comment shows as ', b('[deleted]'), ' and its replies stay.'),
    h(2, 'Mentioning someone'),
    p('Type ', c('@'), ' in a comment and choose the person. They are told, if they can see the page.'),
    h(2, 'Resolving'),
    p(b('Resolve'), ' on a thread’s first comment closes the discussion: it folds away under ', b('Show resolved'), ', and an inline comment’s highlight goes. ', b('Reopen'), ' brings it back. The comment’s author and anyone who can edit the page may do either.'),
    p('Watchers of the page and the space are told about new comments.'),
  ))

  const notify = await ensure('Mentions and notifications', together)
  await page('Mentions and notifications', together, doc(
    h(2, 'Mentioning someone'),
    p('Type ', c('@'), ' and part of a name in the editor or in a comment, and choose the person. They are told, if they can see the page: in the editor when you publish or update, in a comment when you post it. See ', b('Mention'), ' under Elements.'),
    h(2, 'The bell'),
    p('The bell in the top bar counts what is new. Click it for the latest 50 notifications; clicking one opens the page and marks it read, and ', b('Mark all read'), ' clears the count.'),
    ...(await figure(notify, 'bell', 'Notifications')),
    table([
      ['You are told when someone', 'If you'],
      ['creates a page', 'watch the space'],
      ['updates a page', 'watch the page or its space'],
      ['comments on a page', 'watch the page or its space'],
      ['mentions you', 'can see the page'],
    ], [320, 380]),
    p('You are never told about your own changes, and never about a page you cannot open. Administrators also get security alerts here.'),
    h(2, 'By email'),
    p('Your profile’s ', b('Email notifications'), ' setting sends the same things by email: ', b('Off'), ', ', b('Immediately'), ', or a ', b('Daily digest'), '. It is off unless you turn it on, and works only when your Tesria sends email.'),
  ))

  await page('Watching', together, doc(
    p('Watching is how you choose what to hear about. Nothing is watched until you ask, including pages you create.'),
    ul(
      li(p(b('A page'), ': ', b('⋮ → Watch this page'), '.')),
      li(p(b('A space'), ': ', b('Watch this space'), ' on the space’s home page. It covers every page in it.')),
    ),
    p('The button then reads ', b('Watching'), '; choose it again to stop.'),
  ))

  await page('Changes from assistants and the API', together, doc(
    p('Scripts using the REST API, and AI assistants using MCP, can change pages too. If someone has the page open in the editor at the time, the change is shown to them instead of being overwritten by their draft.'),
    ul(
      li(p('Added text is highlighted, and removed text stays visible, struck through.')),
      li(p('Hovering shows who made the change, how (the API, MCP, or another session), and when.')),
      li(p('A bar above the page says how many changes there are, with ', b('Accept all'), ' and ', b('Reject all'), '. Changes are accepted or rejected together, not one by one.')),
      li(p(b('Update'), ' accepts anything still waiting.')),
    ),
    p('Changes are compared paragraph by paragraph, so an edited paragraph shows as the old one struck through with the new one after it. If nobody has the page open, the change is simply there next time.'),
  ))

  // ========================================================= Finding things
  const finding = await ensure('Finding things', manual)
  await page('Finding things', manual, doc(
    p('Search, and labels.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  const search = await ensure('Search', finding)
  await page('Search', finding, doc(
    p('Type in ', b('Search pages…'), ' in the top bar and press Return.'),
    ...(await figure(search, 'search', 'Search results')),
    ul(
      li(p('Titles and the text of pages are searched. Different endings of a word match (', c('launching'), ' finds ', c('launch'), '), and the best matches come first.')),
      li(p('Each result shows its space and the passage that matched.')),
      li(p('Only published pages you can read are found, from every space you can see, up to 50 results.')),
    ),
    h(2, 'Narrowing a search'),
    table([
      ['Type', 'Finds'],
      ['"release checklist"', 'The exact phrase'],
      ['launch or release', 'Either word'],
      ['launch -beta', 'launch, but not pages with beta'],
    ], [260, 440]),
    p('Comments, labels, file names, and the text inside mentions, statuses and dates are not searched. To find pages by label, use the label instead.'),
  ))

  const labelsFind = await ensure('Label pages', finding)
  await page('Label pages', finding, doc(
    p('Click a label on any page to see every page carrying it, across all the spaces you can see.'),
    ...(await figure(labelsFind, 'label-page', 'Every page with a label')),
    p('To keep such a list on a page, use the ', b('Content by label'), ' live content block. ', b('Labels list'), ' shows the labels themselves, such as the most used in a space.'),
  ))

  // ================================================= Exporting and publishing
  const exporting = await ensure('Exporting and publishing', manual)
  await page('Exporting and publishing', manual, doc(
    p('Getting pages out of Tesria: as files, as a website, as a pack for another Tesria, or to readers on the internet.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Exporting a page', exporting, doc(
    p('Open the page’s ', b('⋮'), ' menu and choose ', b('Export as Markdown'), ', ', b('Export as HTML'), ' or ', b('Export as PDF'), '. People reading a public page without signing in get the same as buttons.'),
    table([
      ['Format', 'You get'],
      ['Markdown', 'The title and content as a .md file. Pictures link back to the wiki.'],
      ['HTML', 'One self-contained web page that looks like the page in Tesria, with its light and dark themes and your instance’s branding.'],
      ['PDF', 'The page as a print-ready document, always in the light theme.'],
    ], [140, 560]),
    ul(
      li(p('Only the title and content are exported: not comments, labels, attachments or history.')),
      li(p('Live content is exported as it showed at that moment. Expands print open, and embeds print as a card with their link.')),
      li(p('HTML and PDF need your Tesria’s PDF renderer; without it, export as Markdown.')),
    ),
    p('The options appear only where your role allows exporting and the space has not turned that format off.'),
  ))

  const site = await ensure('A space as a website', exporting)
  await page('A space as a website', exporting, doc(
    p('A whole space can be exported as a static website: a folder of web pages with the space’s tree as a sidebar, which any web host can serve. The Support site you are reading is one.'),
    p('In ', b('Space settings → Details'), ', find ', b('Export as a site'), '.'),
    ...(await figure(site, 'site-export', 'Exporting a space as a website')),
    table([
      ['Choice', 'What goes in'],
      ['As the public sees it', 'Only what someone who is not signed in could read. The space must be published for public reading.'],
      ['As me', 'Everything you can read, restricted pages included. Treat the result as private.'],
    ], [220, 480]),
    p('Choose ', b('Export as a site'), '. Each page takes about a second to build, and the result downloads as ', c('KEY-site.zip'), '. Up to 300 pages.'),
    ul(
      li(p('Each page is a folder named after its title, with an ', c('index.html'), ', so addresses read like ', c('getting-started/quick-start/'), '.')),
      li(p('Pictures and attachments are copied in. Links between pages point at each other’s files; a link to a page that was left out is grayed out, with a note saying the page is not part of the export.')),
      li(p('Every page ends with a line saying where and when it was exported.')),
    ),
  ))

  await page('Hosting an exported site', exporting, doc(
    p('Unzip the export and it is ready: open ', c('index.html'), ' straight from your disk, or put the folder on any static web host.'),
    h(2, 'Cloudflare Pages'),
    ol(
      li(p('In the Cloudflare dashboard, create a Pages project and choose to upload files directly.')),
      li(p('Upload the unzipped folder. Cloudflare gives the site an address, and a custom domain can be added in the project’s settings.')),
    ),
    p('The free plan allows files up to 25 MB and 20,000 files per site, which covers most spaces. A large video attached to a page is the thing most likely to exceed it.'),
    h(2, 'GitHub Pages, Netlify and others'),
    p('Anything that serves a folder of files works. Put the unzipped folder where the host expects it; there is no build step.'),
    panel('info', p('Embeds, such as YouTube videos, still load from their own sites, so a reader needs an internet connection to see them.')),
  ))

  const packs = await ensure('Wiki packs', exporting)
  await page('Wiki packs', exporting, doc(
    p('A wiki pack carries one space, with its history, to another Tesria, or keeps a copy of it outside the wiki. The files inside are readable, and the same space always packs to the same bytes, so a pack can live in a Git repository.'),
    h(2, 'Exporting a pack'),
    p('In ', b('Space settings → Details'), ', choose ', b('Export as a pack'), '. It downloads as ', c('KEY-pack.zip'), '.'),
    ...(await figure(packs, 'pack-export', 'Exporting a pack')),
    table([
      ['Travels', 'Does not travel'],
      ['Every page you can read, with every version, comments, labels and attachments', 'Permissions and restrictions (only how many there were)'],
      ['The space’s name, description and icon', 'Accounts and email addresses'],
      ['The space’s own templates', 'Watches, webhooks, drafts and the trash'],
      ['The authors’ display names', 'Whether the space was public, and its export settings'],
    ], [350, 350]),
    panel('warning', p('A pack holds everything you can read, restricted pages included. Treat the file as you would the space.')),
    h(2, 'Importing a pack'),
    p('On the Spaces page, choose ', b('Import a pack'), ', choose the file, give the new space a key (and a name if you like), and choose ', b('Import'), '.'),
    ...(await figure(packs, 'pack-import', 'Importing a pack')),
    ul(
      li(p('The pack becomes a new space. Importing the same pack twice makes two spaces; it never merges.')),
      li(p('Everything is credited to you; the original authors are recorded in the audit log.')),
      li(p('The new space starts private: only you can see it. The next screen asks who else should have access, with the built-in groups offered first (Users is everyone with an account). Page restrictions from the original do not travel; the result says if there were any.')),
      li(p('A pack can be up to 500 MB. Each person can import 10 packs an hour.')),
    ),
  ))

  const pub = await ensure('Public reading', exporting)
  await page('Public reading', exporting, doc(
    p('A space can be published so that anyone can read it without an account. It takes two switches, both in Administration, so nothing becomes public by accident.'),
    ol(
      li(p(b('Allow public spaces'), ' in ', b('Administration → Settings'), ' turns public reading on for the instance.')),
      li(p(b('Publish'), ' beside a space in ', b('Administration → Spaces'), ' publishes that space.')),
    ),
    ...(await figure(pub, 'admin-spaces', 'Publishing a space in Administration → Spaces')),
    p('Each asks for your password, and every administrator is alerted when a space is published. ', b('Withdraw'), ' takes it back; people lose access within a minute.'),
    h(2, 'What readers see'),
    ul(
      li(p('The public spaces, a read-only page tree, and every published page with no restriction.')),
      li(p('Export buttons, where the space allows exports.')),
      li(p('Comments only if you tick ', b('comments'), ' for the space, and then read-only.')),
      li(p('Search, over public spaces only.')),
    ),
    p('Restricted pages, drafts, the trash and history are never public. Search engines can find public pages through the sitemap.'),
  ))

  const off = await ensure('Turning exports off', exporting)
  await page('Turning exports off', exporting, doc(
    p('Each space can turn off any of its exports: ', b('Markdown'), ', ', b('HTML'), ', ', b('PDF'), ', ', b('Website'), ' and ', b('Wiki pack'), '. The switches are in ', b('Space settings → Details → Exports'), ', for people whose role may control a space’s exports (administrators by default).'),
    ...(await figure(off, 'exports-switches', 'A space’s export switches')),
    p('A format that is off is gone from the page menu and settings for everyone, administrators and the owner included, and the API refuses it.'),
    panel('info', p('This stops the export buttons, not reading: anyone who can read a page can still copy what they read.')),
  ))

  // ========================================================== Your profile
  const profile = await ensure('Your profile', manual)
  await page('Your profile', manual, doc(
    p('Choose your name in the top bar. Everything about your own account is on this one page.'),
    ...(await figure(profile, 'profile', 'Your profile')),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Avatar, name and email', profile, doc(
    h(2, 'Avatar'),
    p(b('Upload picture'), ' takes a PNG, JPEG or WebP of up to 1 MB and crops it to a square. Without a picture, your initials are shown on one of 12 colors, which you can choose. ', b('Remove'), ' goes back to the initials.'),
    h(2, 'Display name'),
    p('Shown on your pages, comments and history. Up to 200 characters; ', b('Save name'), '.'),
    h(2, 'Email address'),
    p('Enter the new address and your current password, then ', b('Change email'), '. If you sign in through single sign-on, your identity provider holds your email, and it cannot be changed here.'),
  ))

  const sessions = await ensure('Sessions', profile)
  await page('Sessions', profile, doc(
    p('Every browser signed in to your account, with where, when it was last active, and when it signed in.'),
    ...(await figure(sessions, 'sessions', 'Your sessions')),
    ul(
      li(p(b('Sign out'), ' beside a session ends it. ', b('Sign out all other sessions'), ' ends every one but this.')),
      li(p('Sessions end by themselves after two weeks unused, and ninety days after signing in whatever you do.')),
      li(p('Changing your password, or turning two-factor on or off, signs out the others too.')),
    ),
  ))

  const emails = await ensure('Email notifications', profile)
  await page('Email notifications', profile, doc(
    ...(await figure(emails, 'email-notifications', 'Email notification settings')),
    table([
      ['Setting', 'What you get'],
      ['Off', 'Only the bell in the app. This is where everyone starts.'],
      ['Immediately', 'An email for each notification, within a minute.'],
      ['Daily digest', 'One email a day, when something happened.'],
    ], [180, 520]),
    p('Your choice is kept even if your Tesria does not send email yet, and applies once it does. What you are told about is set by what you watch: see ', b('Watching'), '.'),
  ))

  const tokens = await ensure('API tokens', profile)
  await page('API tokens', profile, doc(
    p('A token lets a script or another program use Tesria’s REST API as you, without a browser.'),
    ...(await figure(tokens, 'api-tokens', 'API tokens')),
    ol(
      li(p('Give it a name that says what uses it, such as ', c('CI pipeline'), '.')),
      li(p('Tick ', b('Read-only'), ' if it only needs to read.')),
      li(p('Choose ', b('Create token'), ' and copy the token. It is shown once.')),
    ),
    p('Send it as ', c('Authorization: Bearer <token>'), '. A token can do anything you can, unless it is read-only, and stops working if your role loses the right to use tokens. Tokens do not expire: ', b('Revoke'), ' one you no longer need. The list shows when each was last used.'),
    p('See ', b('REST API'), ' for what a token can do.'),
  ))

  await page('Password', profile, doc(
    p('Enter your current password and the new one twice (at least 8 characters), then ', b('Change password'), '. Every other device signed in to your account is signed out.'),
    p('If you have forgotten it, see ', b('Resetting a password'), '. Accounts that sign in through single sign-on have no Tesria password.'),
  ))
}
