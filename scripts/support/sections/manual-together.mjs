// User manual: Working together, Finding things, Exporting and publishing,
// and Your profile (dev-plan 10.5), rewritten to the owner's rules of
// 2026-09-23 (WRITING.md, and the pilot pages in pilot.mjs).
//
// Facts checked 2026-09-24 against CommentsPanel, commentThreads,
// MentionTextarea, SelectionBubbleMenu, InlineCommentPopover,
// ResolvedCommentStyles, NotificationBell, NotificationPreferences,
// WatchToggle, SearchPage, LabelsIndexPage, LabelPage, PageView,
// SpaceSettingsPage, SiteExportSection, PackExportSection, ImportPackForm,
// AdminSpacesPage, AdminSettingsPage, ProfilePage and its sections, and on
// the server CommentEndpoints, NotificationService, NotificationEmailService,
// WatchEndpoints, SearchEndpoints, ExportEndpoints, SiteExportEndpoints,
// SiteChrome (the exported site's filter), PackImportEndpoints and
// AdminEndpoints (publishing).
//
// Pictures are close-ups taken in a narrow window, desktop only (the phone
// has its own chapter, manual-mobile.mjs). Only Tesria Demo and the example
// accounts appear: lists that would show other spaces or people are
// filtered to them with an eval step, and a shot whose filter leaves nothing
// to mark fails rather than showing something else.
//
// No page is proposed for removal. New pages: none.

const NARROW = { width: 480, height: 900 }
// Tables need the computer layout (above 640 pixels): below it they scroll
// sideways and their last column is cut off.
const TABLE = { width: 800, height: 900 }
const PEOPLE = ['Alex Rivera', 'Sam Okafor', 'Priya Natarajan', 'Mei Chen', 'Jordan Brooks']
const DEMO_LABELS = ['launch', 'plan', 'meeting-notes', 'engineering', 'support']

const section = (title) => `section.profile__section:has(> h2:text-is("${title}"))`
const COMMENTS_TAB = [{ wait: 2500 }, { click: 'article .tabs button:has-text("Comments")' }, { wait: 800 }, { scrollTo: '.tab-panel' }]
const tag = (selector, text, name) =>
  `[...document.querySelectorAll(${JSON.stringify(selector)})].filter((b) => b.textContent.trim().startsWith(${JSON.stringify(text)})).forEach((b) => b.setAttribute('data-shot', ${JSON.stringify(name)}))`
// Search and label results from any space but Tesria Demo are taken out.
const ONLY_DEMO_RESULTS = "document.querySelectorAll('.search-result').forEach((r) => { if (r.querySelector('.badge')?.textContent.trim() !== 'DEMO') r.remove() })"
const ONLY_DEMO_LABELS = `document.querySelectorAll('.labels-index li').forEach((li) => { if (!${JSON.stringify(DEMO_LABELS)}.includes(li.querySelector('.label-chip')?.textContent.trim())) li.remove() })`
// Only people from Tesria Demo in the @ suggestions.
const ONLY_EXAMPLE_PEOPLE = `document.querySelectorAll('.mention-textarea__menu li').forEach((li) => { if (!${JSON.stringify(PEOPLE)}.includes(li.textContent.trim())) li.remove() })`
// Administration → Spaces lists every space and who made it: keep the Tesria
// Demo row, and only if an example account made it.
const ONLY_DEMO_SPACE = `document.querySelectorAll('table.admin-table tbody tr').forEach((r) => { const demo = r.querySelector('td .badge')?.textContent.trim() === 'DEMO'; const person = ${JSON.stringify(PEOPLE)}.some((n) => r.textContent.includes(n)); if (!(demo && person)) r.remove() }); document.querySelector('table.admin-table tbody tr td:nth-child(5) .link-btn')?.setAttribute('data-shot', 'publish')`
// The email address is the one field on a profile that could be a real one.
const EXAMPLE_EMAIL = "document.querySelectorAll('.profile__section input[type=\"email\"]').forEach((i) => { i.value = 'alex.rivera@example.com' })"

// The screenshot browser's sessions come from Docker's own addresses (::1,
// 192.168.65.1) and one says only "Browser"; these read like a real person's.
const EXAMPLE_SESSIONS = `[...document.querySelectorAll('#sessions tbody tr, .profile__section tbody tr')].slice(0, 3).forEach((row, n) => {
  const [ip, agent] = [['203.0.113.24', 'Chrome on Windows'], ['198.51.100.7', 'Safari on iPhone'], ['203.0.113.80', 'Firefox on macOS']][n]
  const code = row.querySelector('td code'); if (code) code.textContent = ip
  const small = row.querySelector('td .muted.small'); if (small) small.textContent = agent
})`

/**
 * Gives Alex something ordinary in the bell and the comments pictures
 * something to show. Alex is an administrator, so the bell otherwise holds
 * only security alerts, which is not what a reader's looks like.
 *
 * Once only, each part: Alex watches Tesria Demo, and Sam comments on one page
 * and updates another, while Alex has no page notifications; and Launch plan
 * gets a settled discussion (Sam asks, Alex answers and resolves it) if it
 * has no resolved thread yet, so the Comments tab has one under Show
 * resolved. No mention tokens in these comments: the bell shows a comment's
 * raw text, token and all.
 */
export async function prepare({ lib, author, demoId }) {
  let sam = null
  const samSession = async () => (sam ??= await lib.signIn(lib.need('SHOT2_EMAIL'), lib.need('SHOT2_PASSWORD')))

  const notes = await author.call('GET', '/api/notifications')
  if (!notes.some((n) => n.targetType === 'page')) {
    await author.call('POST', '/api/spaces/DEMO/watch')
    const s = await samSession()
    const faq = demoId('Support FAQ')
    await s.call('POST', `/api/pages/${faq}/comments`,
      { body: 'Added the answer about offline editing, from the design review.', parentCommentId: null, anchorJson: null })
    const arch = demoId('Architecture overview')
    const current = await s.call('GET', `/api/pages/${arch}`)
    await s.call('PUT', `/api/pages/${arch}`,
      { title: current.title, contentJson: current.contentJson, changeComment: 'Checked against the release candidate' })
  }

  const plan = demoId('Launch plan')
  const comments = await author.call('GET', `/api/pages/${plan}/comments`)
  if (!comments.some((c) => c.resolvedAt)) {
    const s = await samSession()
    const ask = await s.call('POST', `/api/pages/${plan}/comments`,
      { body: 'Can we move the press briefing to Monday, October 12? Mei is traveling that Friday.', parentCommentId: null, anchorJson: null })
    await author.call('POST', `/api/pages/${plan}/comments`,
      { body: 'Done: it is on the timeline for the 12th, and the press team knows.', parentCommentId: ask.id, anchorJson: null })
    await author.call('POST', `/api/comments/${ask.id}/resolve`)
    console.log('  added a resolved discussion to Launch plan')
  }
  return {}
}

export const shots = ({ demo }) => [
  // ---- Comments: where the inline comment button is (on a new page, so
  // no real page is touched; the next shot discards its draft), the @
  // suggestions, and a page's comments with a resolved thread shown.
  {
    name: 'comment-selection', url: '/spaces/DEMO/new', viewport: NARROW, phone: false,
    waitFor: '.ProseMirror',
    css: '.tip, .onboarding-tip { display: none !important; }',
    steps: [
      { wait: 1500 }, { click: '.ProseMirror' }, { wait: 300 },
      { keys: 'The press briefing moves to Monday, October 12.' }, { wait: 400 },
      { tripleClick: '.ProseMirror p' }, { wait: 600 },
    ],
    clipTo: ['.editor__content', 'button[title="Comment on this selection"]'], clipPad: 16,
    annotate: [{ type: 'box', target: 'button[title="Comment on this selection"]', pad: 4 }],
  },
  { name: 'comment-selection-closed', settle: 300, skipCapture: true, phone: false, steps: [{ click: '.page-actionbar button:has-text("Close")' }, { wait: 1200 }] },
  {
    name: 'comment-mention', url: demo('Launch plan'), viewport: NARROW, phone: false, settle: 800,
    steps: [...COMMENTS_TAB, { click: '.comment-form textarea' }, { keys: 'Thanks @Sam' }, { wait: 1000 }, { eval: ONLY_EXAMPLE_PEOPLE }],
    clipTo: ['.comment-form', '.mention-textarea__menu'], clipPad: 12,
    annotate: [{ type: 'box', target: '.mention-textarea__menu li', pad: 3 }],
  },
  {
    name: 'comments', url: demo('Launch plan'), viewport: NARROW, phone: false, settle: 1000,
    steps: [
      ...COMMENTS_TAB, { click: '.comments__resolved-toggle' }, { wait: 600 },
      { eval: tag('.comment-list:not(.comment-list--resolved) > .comment > .comment__actions .link-btn', 'Resolve', 'resolve') },
      { eval: tag('.comment-list--resolved > .comment > .comment__actions .link-btn', 'Reopen', 'reopen') },
    ],
    clipTo: ['article .tabs', '.tab-panel'], clipPad: 12,
    annotate: [
      { type: 'box', target: '[data-shot="resolve"]', pad: 3 },
      { type: 'box', target: '.comments__resolved-toggle', pad: 3 },
      { type: 'box', target: '[data-shot="reopen"]', pad: 3 },
    ],
  },

  // ---- Notifications and watching.
  {
    name: 'bell', url: demo('Launch plan'), viewport: NARROW, phone: false, settle: 800,
    // The security alerts an administrator also gets are left out: the
    // picture is of what anyone's bell looks like.
    steps: [{ wait: 2500 }, { click: '.notif__bell' }, { wait: 800 },
      { eval: "document.querySelectorAll('.notif__dropdown .notif__item').forEach((e) => { if (e.textContent.startsWith('Security')) e.closest('li').remove() })" }],
    clipTo: ['.notif__bell', '.notif__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: '.notif__bell', pad: 3 }],
  },
  {
    name: 'watch-menu', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }, { eval: tag('.overflow-menu__dropdown button', 'Watch', 'watch') }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [{ type: 'box', target: '[data-shot="watch"]', pad: 4 }],
  },

  // ---- Finding things.
  {
    name: 'search', url: '/search?q=launch', viewport: NARROW, phone: false, settle: 1200,
    steps: [{ wait: 2500 }, { eval: ONLY_DEMO_RESULTS }, { css: '.search-results li:nth-child(n+4) { display: none; }' }],
    clipTo: '.page-wrap', clipPad: 0,
  },
  {
    name: 'labels-index', url: '/labels', viewport: NARROW, phone: false, settle: 1200,
    steps: [{ wait: 2500 }, { eval: ONLY_DEMO_LABELS }],
    clipTo: '.page-wrap', clipPad: 0,
    annotate: [{ type: 'box', target: '.labels-index__filter', pad: 4 }],
  },

  // ---- Exporting and publishing.
  {
    name: 'export-menu', url: demo('Launch plan'), viewport: NARROW, phone: false,
    steps: [{ wait: 2500 }, { click: 'button[title="More actions"]' }, { wait: 400 }, { eval: tag('.overflow-menu__dropdown a', '↓ Export', 'export') }],
    clipTo: ['.page-actionbar', '.overflow-menu__dropdown'], clipPad: 8,
    annotate: [
      { type: 'box', target: '[data-shot="export"]', nth: 0, pad: 3 },
      { type: 'box', target: '[data-shot="export"]', nth: 1, pad: 3 },
      { type: 'box', target: '[data-shot="export"]', nth: 2, pad: 3 },
    ],
  },
  {
    name: 'site-export', url: '/spaces/DEMO/settings', viewport: NARROW, phone: false, settle: 1000,
    steps: [{ wait: 2500 }, { scrollTo: '#export' }],
    clipTo: '#export', clipPad: 12,
    annotate: [
      { type: 'box', target: '#export .setup__cards', pad: 5 },
      { type: 'box', target: '#export .btn--primary', pad: 4 },
    ],
  },
  {
    name: 'pack-import-button', url: '/spaces', viewport: NARROW, phone: false, settle: 800,
    steps: [{ wait: 2000 }, { eval: tag('.row-between .row-gap .btn', 'Import a pack', 'import') }],
    clipTo: ['.topbar', '.row-between'], clipPad: 0,
    annotate: [{ type: 'box', target: '[data-shot="import"]', pad: 5 }],
  },
  {
    name: 'admin-spaces', url: '/admin/spaces', viewport: { width: 1000, height: 800 }, phone: false, settle: 1200,
    steps: [{ wait: 2500 }, { eval: ONLY_DEMO_SPACE }],
    clipTo: 'table.admin-table', clipPad: 8,
    annotate: [{ type: 'box', target: '[data-shot="publish"]', pad: 4 }],
  },

  // ---- Your profile: the example account's own cards only.
  { name: 'profile-avatar', url: '/profile', viewport: NARROW, phone: false, settle: 1000, steps: [{ wait: 2500 }], clipTo: section('Avatar'), clipPad: 0 },
  // The account that takes these pictures signs in on every run, so its list
  // is long; the first three rows show what a list looks like.
  {
    name: 'sessions', url: '/profile', viewport: TABLE, phone: false, settle: 1000,
    steps: [{ wait: 2500 }, { eval: EXAMPLE_EMAIL }, { eval: EXAMPLE_SESSIONS }, { css: `${section('Sessions').replace(':has(> h2:text-is("Sessions"))', '')} tbody tr:nth-child(n+4) { display: none }` }],
    clipTo: section('Sessions'), clipPad: 0,
  },
  {
    // The other sections hidden, so nothing scrolls: scrolled, the boxes
    // were measured before the page moved and landed beside their targets.
    name: 'api-tokens', url: '/profile', viewport: NARROW, phone: false, settle: 1000,
    steps: [{ wait: 2500 }, { css: '.profile__section:not(#api-tokens) { display: none !important; }' }, { type: 'Nightly report', selector: '#api-tokens form input[placeholder="CI pipeline"]' }, { eval: 'document.activeElement && document.activeElement.blur()' }],
    clipTo: '#api-tokens form', clipPad: 12,
    annotate: [
      { type: 'box', target: '#api-tokens .api-tokens__expiry', pad: 4 },
      { type: 'box', target: '#api-tokens .api-tokens__scope', pad: 4 },
      { type: 'box', target: '#api-tokens form button[type="submit"]', pad: 4 },
    ],
  },
]

export async function build({ top, page, ensure, doc, p, h, text, bold, italic, code, ul, ol, li, panel, table, live, picture, pageLink }) {
  const manual = top['User manual']
  const b = (t) => text(t, bold)
  const c = (t) => text(t, code)
  const i = (t) => text(t, italic)
  const step = (n, title) => h(3, `Step ${n}: ${title}`)

  // Every page first, so links between them resolve on a first run.
  const together = await ensure('Working together', manual)
  await ensure('Editing at the same time', together)
  const comments = await ensure('Comments', together)
  const notify = await ensure('Mentions and notifications', together)
  const watching = await ensure('Watching', together)
  await ensure('Changes from assistants and the API', together)
  const finding = await ensure('Finding things', manual)
  const search = await ensure('Search', finding)
  const labels = await ensure('Label pages', finding)
  const exporting = await ensure('Exporting and publishing', manual)
  const exportPage = await ensure('Exporting a page', exporting)
  const site = await ensure('A space as a website', exporting)
  await ensure('Hosting an exported site', exporting)
  const packs = await ensure('Wiki packs', exporting)
  const pub = await ensure('Public reading', exporting)
  await ensure('Turning exports off', exporting)
  const profile = await ensure('Your profile', manual)
  const avatar = await ensure('Avatar, name and email', profile)
  const sessions = await ensure('Sessions', profile)
  await ensure('Email notifications', profile)
  const tokens = await ensure('API tokens', profile)
  await ensure('Password', profile)

  // ======================================================= Working together
  await page('Working together', manual, doc(
    p('A wiki earns its keep when a whole team writes in it: the person who knows the answer adds it, the person who spots a mistake fixes it, and nobody has to ask where the latest version is. Tesria is built for that. Several people can edit one page at the same time, anyone who can read a page can discuss it, and you can ask to hear about the pages you care about instead of checking them.'),
    p('This chapter covers:'),
    ul(
      li(p(b('Editing at the same time:'), ' what you see when someone else is in the same page.')),
      li(p(b('Comments:'), ' discussing a page, or a few words on it, and closing the discussion when it is settled.')),
      li(p(b('Mentions and notifications:'), ' the bell, and what puts something in it.')),
      li(p(b('Watching:'), ' choosing which pages and spaces you hear about.')),
      li(p(b('Changes from assistants and the API:'), ' what happens when a script or an AI assistant edits a page you have open.')),
    ),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Editing at the same time', together, doc(
    p('Two people fixing the same page used to mean one of them waiting, or one of them losing their work. Where your Tesria has live editing turned on, that does not happen: everyone in a page sees the others’ changes as they type, and each person’s cursor shows in its own color with their name beside it.'),
    h(2, 'Is it on?'),
    p('Look under the page’s title while you edit. A line there says how the connection is:'),
    ul(
      li(p(b('Live: changes are shared as you type.'), ' Everything is working.')),
      li(p(b('Connecting to collaboration…'), ' It is still getting in touch with the server. This normally takes a moment.')),
      li(p(b('Offline: your changes are local until reconnected.'), ' Your network dropped. Keep typing; your changes are sent when it comes back.')),
    ),
    p('If there is no such line at all, live editing is not turned on for your Tesria. See ', b('Without live editing'), ' below.'),
    h(2, 'How publishing works with others in the page'),
    ul(
      li(p(b('Changes to an existing page are kept in a shared draft.'), ' You can leave the editor and come back later, and so can everyone else: the draft is still there, with everyone’s changes in it.')),
      li(p(b('Update publishes everyone’s changes at once,'), ' as one new version of the page. Whoever presses it, the version has all of them.')),
      li(p(b('A brand-new page is written by one person'), ' until it is published for the first time. After that, anyone who can edit it can join in.')),
      li(p(b('If the page changed while you were editing'), ' (someone published it another way, or restored an older version), Tesria does not publish over it. It says ', i('This page changed while you were editing'), ', highlights the difference, and lets you accept or reject it before you publish again.')),
    ),
    h(2, 'Without live editing'),
    p('Live editing needs a setting on the server, ', c('COLLAB_SHARED_SECRET'), ', which an administrator sets when installing. Without it, each person edits on their own, and whoever publishes last wins: their version replaces the one before. If your team often edits the same pages, ask your administrator to turn it on.'),
  ))

  // ================================================================ Comments
  await page('Comments', together, doc(
    p('Comments are for talking about a page without changing it: a question for the author, a suggestion, a “this is out of date”. They sit below the page, so every reader sees the discussion, and when it is settled you can resolve it to fold it away.'),
    p('There are two kinds. A ', b('page comment'), ' is about the page as a whole. An ', b('inline comment'), ' is attached to a few words on it, which are highlighted so readers can see exactly what it is about. Anyone who can read a page can comment on it, once signed in.'),

    h(2, 'Commenting on a page'),
    step(1, 'Open the Comments tab'),
    p('Below every page there is a row of tabs: ', b('Comments'), ', ', b('Attachments'), ', ', b('History'), ' and ', b('Restrictions'), '. Comments is open when the page loads; scroll down past the end of the page to reach it.'),
    step(2, 'Write and post'),
    p('Type in ', b('Add a comment…'), ' and choose ', b('Post'), '. Your comment appears at once, with your name and the time.'),
    step(3, 'Reply to keep the conversation together'),
    p('To answer a comment, choose ', b('Reply'), ' under it, rather than starting a new one. Replies sit indented under the comment they answer, so a discussion reads top to bottom as one thread.'),

    h(2, 'Commenting on a few words'),
    p('When your comment is about one sentence, such as a date that looks wrong, attach it to those words. Readers then see the words highlighted and can click them to read the discussion right there.'),
    step(1, 'Choose Edit and select the words'),
    p('Inline comments are added in the editor. Choose ', b('Edit'), ' at the top of the page, then select the words you want to discuss, as you would to make them bold.'),
    step(2, 'Choose Comment on this selection'),
    p('A small menu appears above the selection. Its last button, the speech bubble, is ', b('Comment on this selection'), '.'),
    ...(await picture(comments, 'comment-selection', 'The selection menu with its comment button', 'Select some words in the editor; the comment button is the last one above them.')),
    step(3, 'Write, then choose Comment'),
    p('Type in ', b('Write a comment…'), ' and choose ', b('Comment'), '. The comment is saved straight away and appears in the Comments tab, marked ', b('inline'), '.'),
    step(4, 'Publish the page'),
    p('The highlight is part of the page, so choose ', b('Update'), ' (or ', b('Publish'), ' for a new page) to keep it. Until you do, the comment exists but readers cannot see which words it belongs to.'),
    p('Readers click the highlighted words to open the thread in a small box beside them, where they can reply without scrolling down to the tab.'),
    panel('success', p(b('Mentioning someone in an inline comment?'), ' The box for a new inline comment does not suggest names. Post the comment, then reply to it: replies offer names when you type @, as below.')),
    p('Pictures work the same way: in the editor, select a picture and choose the comment button in the menu above it. A picture has no highlight, so its comments are read in the Comments tab.'),

    h(2, 'Mentioning someone'),
    p('To bring someone into a discussion, type ', c('@'), ' followed by the start of their name. Tesria lists the people who match; choose one with the mouse, or with the arrow keys and ', b('Enter'), '. Their name appears as ', b('@Their Name'), ' in the comment.'),
    ...(await picture(comments, 'comment-mention', 'Suggestions after typing @ in a comment', 'Type @ and the start of a name, then choose the person.')),
    p('When you post, the person is told, in their bell and, if they chose it, by email. They are only told if they can open the page: a mention never shows anyone a page they are not allowed to see. Adding a name when you edit a comment tells that person too; people already mentioned are not told twice.'),

    h(2, 'Resolving a discussion'),
    p('When a question has been answered or a change has been made, choose ', b('Resolve'), ' under the thread’s first comment. The whole thread, replies and all, folds away, and the highlight of an inline comment disappears from the page. Nothing is deleted.'),
    p('Resolved threads are counted under the open ones, as ', b('Show resolved'), ' with their number. Choose it to see them again: each is marked ', b('resolved'), ' and says who resolved it and when. ', b('Reopen'), ' puts a thread back with the open ones, highlight included.'),
    ...(await picture(comments, 'comments', 'A page’s comments, with a resolved thread shown', 'Resolve on an open thread, Show resolved (here, Hide resolved) below the open ones, and Reopen on a resolved thread.')),
    p('A thread can be resolved or reopened by whoever wrote its first comment, and by anyone who can edit the page.'),

    h(2, 'Changing and deleting your comments'),
    ul(
      li(p(b('Edit'), ' under one of your own comments opens it for changes; ', b('Save'), ' keeps them.')),
      li(p(b('Delete'), ' asks first, then removes it. A deleted comment shows as ', b('[deleted]'), ', and any replies to it stay where they are, so the rest of the discussion still makes sense.')),
    ),
    p('You can edit and delete only your own comments.'),

    h(2, 'Who hears about a comment'),
    p('Everyone who watches the page, or the space it is in, is told about a new comment, along with anyone it mentions. See ', pageLink('Watching'), ' and ', pageLink('Mentions and notifications'), '.'),
    p('In a space that is published for public reading, readers without an account see comments only if an administrator allowed it for that space, and then they can read them but not reply. See ', pageLink('Public reading'), '.'),

    h(2, 'Good practice'),
    ul(
      li(p(b('Comment on the words when you can.'), ' “The date in step 3 is wrong” attached to that date needs no explaining.')),
      li(p(b('Mention whoever has to act.'), ' A question nobody is told about can wait a long time.')),
      li(p(b('Resolve when it is done.'), ' A page whose old discussions are folded away shows at a glance what is still open.')),
    ),
  ))

  // ============================================= Mentions and notifications
  await page('Mentions and notifications', together, doc(
    p('Notifications are how Tesria tells you something happened that you care about: someone mentioned you, commented on a page you follow, or changed it. They collect in the bell at the top of every page, and, if you like, arrive by email as well.'),
    p('Nothing is sent to you until you ask for it, apart from mentions: you choose what to hear about by watching pages and spaces. See ', pageLink('Watching'), '.'),

    h(2, 'The bell'),
    p('The bell is at the top right, beside your avatar. A red number on it counts what you have not read yet (it stops at 99+). It checks for new notifications every half minute, so there is nothing to refresh.'),
    p('Click the bell to see your latest 50 notifications, newest first, with the unread ones highlighted. Each says who did what, on which page:'),
    ...(await picture(notify, 'bell', 'The bell, open', 'The bell and its list. Click a notification to open the page.')),
    ul(
      li(p(b('Click a notification'), ' to open the page it is about. That also marks it read.')),
      li(p(b('Mark all read'), ' clears the count without opening anything.')),
      li(p(i('You’re all caught up.'), ' means there is nothing in the list.')),
    ),

    h(2, 'What you are told about'),
    table([
      ['When someone', 'You are told if you'],
      ['creates a page', 'watch its space'],
      ['updates a page', 'watch the page or its space'],
      ['comments on a page', 'watch the page or its space'],
      ['mentions you', 'can open the page'],
    ], [320, 380]),
    ul(
      li(p(b('Never about your own changes.'), ' Publishing a page you watch does not notify you.')),
      li(p(b('Never about a page you cannot open.'), ' If you lose access to a page later, its notifications disappear from your bell too.')),
      li(p(b('Administrators also get security alerts,'), ' such as a failed backup. Clicking one opens the Security page in Administration. See ', pageLink('Security (administration)'), '.')),
    ),

    h(2, 'Mentions'),
    p('A mention is the one notification you get without watching anything, because someone asked for your attention by name. You can be mentioned in two places:'),
    ul(
      li(p(b('On a page.'), ' Someone types @ and your name in the editor. You are told when they publish or update the page, and only the first time: mentioning you again in a later update does not notify you twice. See ', pageLink('Mention'), '.')),
      li(p(b('In a comment.'), ' You are told when the comment is posted, or when an edit adds your name. See ', pageLink('Comments'), '.')),
    ),
    p('Either way, you are only told if you can open the page.'),

    h(2, 'By email'),
    p('The same notifications can come by email, straight away or as one email a day. That is a choice on your profile, and it starts switched off. See ', pageLink('Email notifications'), '.'),
  ))

  // ================================================================ Watching
  await page('Watching', together, doc(
    p('Watching is how you choose what you hear about. Watch a page and you are told when it is updated or commented on; watch a whole space and you hear about every page in it, including new ones. It saves you from checking pages to see whether anything has changed.'),
    p('Nothing is watched until you ask, not even pages you create yourself.'),

    h(2, 'Watching a page'),
    p('Open the page, then its ', b('⋮'), ' menu at the top right, and choose ', b('Watch this page'), '.'),
    ...(await picture(watching, 'watch-menu', 'The page menu with Watch this page', 'Watch this page is in the page’s ⋮ menu.')),
    p('From then on the menu shows ', b('Watching'), ' instead. Choose it to stop.'),

    h(2, 'Watching a space'),
    p('Open the space’s home page (choose the space’s name, or pick it on the Spaces page) and choose ', b('Watch this space'), ' at the top right. You then hear about every page in the space: new pages, updates and comments. The button changes to ', b('Watching'), '; choose it again to stop. See ', pageLink('The space home and watching'), '.'),

    h(2, 'Which to choose'),
    ul(
      li(p(b('Watch a space'), ' when it is small, or when it is yours to look after: a team space you want to keep tidy, or a project you run.')),
      li(p(b('Watch a page'), ' in a busy space where only a few pages matter to you, such as the release checklist or the on-call rota.')),
    ),
    panel('info', p(b('Watching does not give access.'), ' You can only watch what you can see, and if a page is later restricted so that you cannot open it, you stop hearing about it.')),
  ))

  await page('Changes from assistants and the API', together, doc(
    p('People are not the only ones who change pages. A script can use Tesria’s REST API to update a status page every night, and an AI assistant connected through MCP can rewrite a paragraph you asked it to. If you happen to have that page open in the editor at the time, Tesria does not let your draft quietly overwrite the change, or the change quietly overwrite your draft. It shows you the change instead, for you to decide.'),
    h(2, 'What you see'),
    ul(
      li(p(b('Added text is highlighted,'), ' and removed text stays visible, struck through.')),
      li(p(b('Hovering over a change'), ' says who made it, how (the API, MCP, or another session), and how long ago.')),
      li(p(b('A bar above the page'), ' counts the changes, with ', b('Accept all'), ' and ', b('Reject all'), '. Changes are accepted or rejected together, not one by one.')),
      li(p(b('Update accepts anything still waiting,'), ' so publishing never throws a change away without you choosing to.')),
    ),
    p('Changes are compared paragraph by paragraph, so an edited paragraph shows as the old one struck through with the new one after it. If nobody has the page open, the change is simply there the next time someone opens it.'),
    p('See ', pageLink('REST API'), ' and ', pageLink('MCP'), ' for what scripts and assistants can do.'),
  ))

  // ========================================================= Finding things
  await page('Finding things', manual, doc(
    p('A wiki is only as useful as your ability to find what is in it. Tesria gives you three ways, for three different situations:'),
    ul(
      li(p(b('Search'), ' when you remember a word from the page but not where it is.')),
      li(p(b('The page tree’s filter'), ' when you remember roughly what a page is called and which space it is in. Type into ', b('Filter pages'), ' above a space’s page tree. See ', pageLink('The page tree and reordering'), '.')),
      li(p(b('Labels'), ' when pages about one subject are spread across spaces, such as every page labeled ', i('meeting-notes'), '.')),
    ),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Search', finding, doc(
    p('Search looks through the title and text of every page you can read, in every space, and lists the best matches first. It is the quickest way to a page when you remember something it says but not where it lives.'),

    h(2, 'Searching'),
    p('Type into ', b('Search pages…'), ' in the top bar and press ', b('Enter'), '. On a phone the search box is in the ☰ menu.'),
    p('Each result shows the page’s title, the key of the space it is in, and the passage that matched, with your words highlighted. Choose a title to open the page.'),
    ...(await picture(search, 'search', 'Search results for launch', 'Results for “launch”: the title, the space, and the matching passage.')),

    h(2, 'What is found'),
    ul(
      li(p(b('Different endings of a word match.'), ' Searching ', c('launching'), ' finds pages that say ', c('launch'), ', ', c('launched'), ' or ', c('launches'), '.')),
      li(p(b('Only published pages you can read,'), ' from every space you can see. Drafts, pages in the trash and pages restricted from you are never found.')),
      li(p(b('Up to 50 results.'), ' If what you want is not among them, add a word to narrow the search.')),
    ),
    p('Some things are not searched: comments, labels, the names of attached files, and the text inside mentions, statuses and dates. To find pages by label, use the label instead: see ', pageLink('Label pages'), '.'),

    h(2, 'Narrowing a search'),
    table([
      ['Type', 'Finds'],
      ['"release checklist"', 'The exact phrase'],
      ['launch or release', 'Either word'],
      ['launch -beta', 'launch, but not pages with beta'],
    ], [260, 440]),
    p('Capitals make no difference, and small words such as ', i('the'), ' and ', i('of'), ' are ignored.'),

    panel('info', p(b('Reading without an account?'), ' In a Tesria with public spaces, people who are not signed in can search too, and find pages in the public spaces only.')),
  ))

  await page('Label pages', finding, doc(
    p('A label is a word you attach to a page, such as ', i('meeting-notes'), ' or ', i('onboarding'), ', to group it with others on the same subject. Spaces divide pages by who owns them; labels cut across them. The pages with a label, and the list of every label, are each a page of their own. To add labels to a page, see ', pageLink('Labels'), '.'),

    h(2, 'Every page with a label'),
    p('Click a label under any page’s title. Tesria lists every page carrying it, from every space you can see, sorted by title, each with the key of its space.'),

    h(2, 'Every label'),
    p('At the top of a label’s page, ', b('All labels'), ' lists every label in use, in alphabetical order, with how many pages you can see carry it. With many labels, type into ', b('Filter labels'), ' to narrow the list; click one to see its pages.'),
    ...(await picture(labels, 'labels-index', 'The list of all labels', 'All labels, with the number of pages for each. Filter labels narrows the list.')),
    p('Labels are always in lower case, so ', i('Launch'), ' and ', i('launch'), ' are the same label.'),

    h(2, 'Keeping a list on a page'),
    p('To show pages with a label inside a page, such as every meeting note on a project’s home page, use the ', pageLink('Content by label'), ' live content block. The list stays up to date as pages are labeled. ', pageLink('Labels list'), ' shows the labels themselves, such as the most used ones in a space.'),
  ))

  // ================================================= Exporting and publishing
  await page('Exporting and publishing', manual, doc(
    p('Sooner or later a page has to leave the wiki: a policy sent to someone without an account, a handbook put on a public website, a whole space moved to another Tesria. This chapter covers each way out:'),
    ul(
      li(p(b('Exporting a page'), ' as a PDF, a web page or a Markdown file.')),
      li(p(b('A space as a website:'), ' every page as a static site that any web host can serve, like the Support site you are reading.')),
      li(p(b('Wiki packs:'), ' a whole space with its history, to import into another Tesria or keep as a copy.')),
      li(p(b('Public reading:'), ' letting anyone read a space in Tesria itself, without an account.')),
      li(p(b('Turning exports off'), ' for a space that should not leave.')),
    ),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Exporting a page', exporting, doc(
    p('Exporting a page makes a copy of it as a file: to send to someone who has no account, to print, or to keep with other documents. The page in Tesria is not changed.'),
    h(2, 'Exporting'),
    p('Open the page, then its ', b('⋮'), ' menu at the top right, and choose ', b('Export as Markdown'), ', ', b('Export as HTML'), ' or ', b('Export as PDF'), '. The file downloads, named after the page.'),
    ...(await picture(exportPage, 'export-menu', 'The page menu with the three exports', 'The three exports are at the top of the page’s ⋮ menu.')),

    h(2, 'Which format'),
    ul(
      li(p(b('PDF'), ' for reading and printing: the page as a document, laid out as Tesria shows it, always in the light theme. The one to send when someone just needs to read it.')),
      li(p(b('HTML'), ' for a single web page that looks like the page in Tesria, light and dark themes and your organization’s branding included. It opens in any browser, with nothing else needed.')),
      li(p(b('Markdown'), ' for plain text: the title and content as a ', c('.md'), ' file, which other tools and code repositories read. Pictures in it link back to the wiki rather than being copied in.')),
    ),

    h(2, 'What goes in'),
    ul(
      li(p(b('The title and the content,'), ' nothing else: comments, labels, attachments and history stay in Tesria.')),
      li(p(b('Live content as it was at that moment.'), ' A list of recently updated pages, for example, is frozen as it stood when you exported.')),
      li(p(b('Expand blocks open,'), ' so nothing is hidden on paper, and embedded videos and sites as a card with their link.')),
    ),

    h(2, 'If an export is missing or does not work'),
    ul(
      li(p(b('The options are not in the menu:'), ' your role does not allow exporting, or the space has turned that format off. See ', pageLink('Turning exports off'), '.')),
      li(p(b('HTML or PDF says there is no export renderer:'), ' those two are made by a separate part of Tesria, which your administrator has not set up. Export as Markdown instead.')),
    ),
    p('People reading a public page without signing in have the same exports as buttons at the top of the page, ', b('↓ Markdown'), ', ', b('↓ HTML'), ' and ', b('↓ PDF'), ', where the space and your Tesria allow it.'),
  ))

  await page('A space as a website', exporting, doc(
    p('A whole space can be exported as a website: a folder of ordinary web pages, with the space’s page tree down the side, that any web host can serve and anyone can read. There is no server to run and nothing to sign in to. Use it to put a handbook, a product manual or a set of guides on the internet, or to keep a copy you can read with no Tesria at all.'),
    p('The Support site at tesria.com, which you may be reading now, is one, exported from Tesria’s own wiki.'),

    step(1, 'Open the space’s settings'),
    p('In the space, choose ', b('Space settings'), ' at the bottom of the sidebar. The ', b('Details'), ' tab opens; scroll down to ', b('Export as a site'), '.'),
    ...(await picture(site, 'site-export', 'Export as a site in the space’s settings', 'Choose who the site is for, then Export as a site.')),
    step(2, 'Choose who it is for'),
    ul(
      li(p(b('As the public sees it:'), ' only what someone with no account can already read. The space has to be published for public reading first (see ', pageLink('Public reading'), '). Nothing private can get in, whatever you yourself can see. Choose this for anything going on the internet.')),
      li(p(b('As me:'), ' everything you can read, restricted pages included. Choose this for a copy you keep yourself, or a site behind your own sign-in. Treat it as private.')),
    ),
    step(3, 'Choose Export as a site'),
    p('Each page takes about a second to build, so a large space takes a minute or two. The site downloads as a zip named after the space’s key, such as ', c('team-site.zip'), '.'),

    h(2, 'What the site looks like'),
    ul(
      li(p(b('Like the space in Tesria,'), ' with the same page tree in a sidebar, the same look, and a menu to switch between light and dark and pick an accent color. If the space numbers its pages (', b('Space settings, Page tree'), '), the site’s tree is numbered the same way.')),
      li(p(b('Each page is a folder'), ' named after its title, with an ', c('index.html'), ' inside, so addresses read like ', c('getting-started/quick-start/'), '.')),
      li(p(b('Pictures and attached files are copied in,'), ' and links between pages point at each other’s files. A link to a page that was left out, such as a restricted one, is grayed out, and hovering over it says the page is not part of the export.')),
      li(p(b('Expand blocks open and close'), ', and code blocks keep their ', b('Copy'), ' button. Live content is frozen as it was when you exported.')),
      li(p(b('Every page ends with a line'), ' saying which Tesria it was exported from, and when.')),
    ),

    h(2, 'Finding a page in the site'),
    p('Above the page tree there is a ', b('Filter pages'), ' box, the same as the one in Tesria’s sidebar. Type part of a title, or a page’s number, and the tree narrows to the pages that match, with the matching words highlighted and the pages above them shown dimmed, so you can see where each one is.'),
    ul(
      li(p(b('Enter'), ' opens the first match. ', b('Escape'), ' clears the filter.')),
      li(p(b('The filter stays as you move from page to page,'), ' so you can open one match, come back to the tree and open the next without typing again.')),
      li(p(b('The button beside the box'), ' shows the pages under each match as well, so filtering for a chapter shows the whole chapter. It is on at first; click it to see only the pages whose titles match. The site remembers your choice in that browser.')),
    ),

    h(2, 'Limits'),
    ul(
      li(p(b('Up to 300 pages'), ' in one site.')),
      li(p(b('An export renderer'), ' has to be set up on your Tesria, the same one PDF exports use.')),
      li(p(b('Embedded videos and sites'), ' still load from where they live, so readers need an internet connection for those.')),
    ),
    p('Next: ', pageLink('Hosting an exported site'), '.'),
  ))

  await page('Hosting an exported site', exporting, doc(
    p('An exported site is a finished website in a zip file. Unzip it and it is ready: you can open ', c('index.html'), ' straight from your computer to read it, or put the folder on any static web host to publish it. A static host is one that serves files as they are, with no program running; most are free for a site this size.'),
    h(2, 'Cloudflare Pages'),
    ol(
      li(p('In the Cloudflare dashboard, create a Pages project and choose to upload your files directly, rather than connecting a code repository.')),
      li(p('Upload the unzipped folder. Cloudflare gives the site an address straight away, and you can add your own domain in the project’s settings.')),
      li(p('To update the site, export again and upload the new folder to the same project.')),
    ),
    p('The free plan allows files up to 25 MB and 20,000 files per site, which covers most spaces. A large video attached to a page is the thing most likely to go over.'),
    h(2, 'GitHub Pages, Netlify and others'),
    p('Anything that serves a folder of files works. Put the unzipped folder where the host expects it; there is no build step.'),
    panel('warning', p(b('Exported “As me”?'), ' Then the site can hold restricted pages. Do not put it on a public host. Export ', b('As the public sees it'), ' for anything the world will read.')),
  ))

  await page('Wiki packs', exporting, doc(
    p('A wiki pack is a whole space in one file, history and all, in a form Tesria can read back. Use one to move a space to another Tesria, for example from a trial server to the real one, or to keep a copy of a space outside the wiki.'),
    p('A pack is not a website: it is for Tesria, and what comes out of it is still editable. To publish pages for readers, export a site instead (see ', pageLink('A space as a website'), ').'),
    panel('success', p(b('A pack can live in Git.'), ' The files inside are readable text, and the same space always packs to exactly the same file, so committing a pack after each change shows what actually changed.')),

    h(2, 'Exporting a pack'),
    p('In the space, choose ', b('Space settings'), ', scroll down the ', b('Details'), ' tab to ', b('Export as a pack'), ', and choose the button of the same name. It downloads as a zip named after the space’s key, such as ', c('team-pack.zip'), '.'),
    p(b('What goes in:')),
    ul(
      li(p('Every page you can read, with every version of it, its comments, labels and attachments.')),
      li(p('The space’s name, description and icon, and its own templates.')),
      li(p('The authors’ display names, as a record of who wrote what.')),
    ),
    p(b('What stays behind:')),
    ul(
      li(p('Who may read what: space permissions and page restrictions. Only how many there were is noted.')),
      li(p('Accounts and email addresses.')),
      li(p('Watches, webhooks, drafts and the trash.')),
      li(p('Whether the space was public, and which exports it allowed.')),
    ),
    panel('warning', p(b('Treat the file as you would the space.'), ' It holds everything you can read, restricted pages included.')),

    h(2, 'Importing a pack'),
    step(1, 'Choose Import a pack'),
    p('On the ', b('Spaces'), ' page, choose ', b('Import a pack'), ', beside ', b('New space'), '.'),
    ...(await picture(packs, 'pack-import-button', 'The Spaces page with Import a pack', 'Import a pack is at the top right of the Spaces page.')),
    step(2, 'Choose the file and a key'),
    p('Choose the pack file, and give the new space a ', b('Key'), ' that no other space uses, such as HANDBOOK. The ', b('Name'), ' is optional; leave it empty to keep the one in the pack. Then choose ', b('Import'), '.'),
    step(3, 'Read what came in'),
    p('Tesria says how many pages, versions, attachments, comments and templates were imported. Two things are worth reading here:'),
    ul(
      li(p(b('Everything is credited to you.'), ' The original authors’ names are kept in the audit log, because a name in a pack is not an account on this Tesria.')),
      li(p(b('Restrictions did not come across.'), ' If the original had any, Tesria says how many, so you can set page restrictions again from each page’s ', b('Restrictions'), ' tab.')),
    ),
    step(4, 'Choose who should have access'),
    p('An imported space starts private: only you can see it. Right below the result, ', b('Who should have access?'), ' lets you add people and groups straight away. The ', b('Users'), ' group is everyone with an account, which is the choice for a space the whole team shares. You can change this later in ', b('Space settings, Permissions'), '. Then choose ', b('Open'), ' and the space’s name to go to it.'),

    h(2, 'Good to know'),
    ul(
      li(p(b('An import always makes a new space.'), ' Importing the same pack twice gives two spaces; it never merges into an existing one.')),
      li(p(b('A pack can be up to 500 MB,'), ' and each person can import 10 packs an hour.')),
      li(p(b('Importing needs the right to create spaces,'), ' which everyone has unless an administrator has turned it off for their role.')),
    ),
  ))

  await page('Public reading', exporting, doc(
    p('A space can be opened to the world, so that anyone can read it without an account: a product manual for your customers, say, or a handbook for volunteers. Readers see the space in Tesria itself, always up to date, and search engines can find its pages.'),
    p('Because it puts pages on the internet, it takes two separate switches, both in Administration, so nothing becomes public by accident. They are for administrators, unless your Tesria’s roles give them to someone else.'),

    step(1, 'Allow public spaces for your Tesria'),
    p('In ', b('Administration, Settings'), ', turn on ', b('Allow public spaces'), '. This alone publishes nothing: it allows spaces to be published. If your Tesria can be reached from the internet, go through the readiness checklist in ', c('docs/security.md'), ' first. See ', pageLink('Settings (administration)'), '.'),
    step(2, 'Publish the space'),
    p('In ', b('Administration, Spaces'), ', choose ', b('Publish'), ' beside the space. Tesria says exactly how many pages and attachments will become readable before it asks you to confirm.'),
    ...(await picture(pub, 'admin-spaces', 'Publish beside a space in Administration, Spaces', 'Publish is in the Public column. A public space shows Withdraw and a comments box there instead.')),
    p('Both steps may ask for your password again, if you have not signed in recently. Every administrator gets a security alert whenever a space is published or withdrawn, so nobody can do it unnoticed.'),

    h(2, 'What readers see'),
    ul(
      li(p(b('The public spaces,'), ' each with a read-only page tree, and every published page in them that has no restriction.')),
      li(p(b('Export buttons'), ' for PDF, HTML and Markdown, where the space allows them.')),
      li(p(b('Search,'), ' over the public spaces only.')),
      li(p(b('Comments, only if you allow them:'), ' tick ', b('comments'), ' beside the space in Administration, Spaces. Readers can then read the comments but not add any.')),
      li(p(b('A Sign in button,'), ' for people who do have an account.')),
    ),
    p('Restricted pages, drafts, the trash and page history are never public. Search engines find the public pages through the sitemap Tesria keeps for them.'),

    h(2, 'Taking it back'),
    ul(
      li(p(b('Withdraw'), ' beside a space in Administration, Spaces makes it private again. Readers without an account lose access within a minute.')),
      li(p(b('Turning off Allow public spaces'), ' hides every public space at once, and remembers which ones they were, for when it is turned on again.')),
    ),
    p('See ', pageLink('Spaces (administration)'), ' for the rest of that page.'),
  ))

  await page('Turning exports off', exporting, doc(
    p('Some spaces are more sensitive than the rest, such as salaries or a confidential project. For those, you can turn off any of the ways to download them, so the pages stay in Tesria.'),
    h(2, 'Turning a format off'),
    p('In the space, choose ', b('Space settings'), ' and scroll the ', b('Details'), ' tab to ', b('Exports'), '. Untick the formats this space should not allow, and choose ', b('Save'), ':'),
    ul(
      li(p(b('Markdown, HTML, PDF:'), ' a page as a file.')),
      li(p(b('Website:'), ' the whole space as a static site.')),
      li(p(b('Wiki pack:'), ' the whole space with its history.')),
    ),
    p('The Exports section is shown to people whose role may control a space’s exports: administrators, unless your Tesria has changed that. See ', pageLink('Roles'), '.'),
    h(2, 'What it does'),
    p('A format that is off disappears from the page’s ⋮ menu and from the space’s settings for everyone, administrators included, and the API refuses it, until someone with the right turns it back on.'),
    panel('info', p(b('This stops downloads, not reading.'), ' Anyone who can read a page can still copy what they read. To limit who can read a space, see ', pageLink('Who can see a space'), '.')),
  ))

  // ========================================================== Your profile
  await page('Your profile', manual, doc(
    p('Your profile is everything about your own account, on one page: how you appear to others, how you sign in, and what Tesria sends you. To open it, choose your avatar at the top right (your picture, or your initials in a colored circle).'),
    p('The page is a column of cards, one for each setting, in this order:'),
    ul(
      li(p(b('Avatar:'), ' your picture, or the color of your initials.')),
      li(p(b('Display name:'), ' the name shown on your pages, comments and history.')),
      li(p(b('Email address:'), ' where Tesria writes to you, and what you sign in with.')),
      li(p(b('Password:'), ' changing it signs out your other devices.')),
      li(p(b('Two-factor sign-in:'), ' a code from an app on your phone as well as your password. See ', pageLink('Two-factor and recovery codes'), '.')),
      li(p(b('Recovery codes:'), ' single-use codes that get you back in if you lose your phone or forget your password. Also in ', pageLink('Two-factor and recovery codes'), '.')),
      li(p(b('Trust this device:'), ' only on a Tesria that makes its own security certificate, a guide that stops the browser warning about it. See ', pageLink('Trusting the local certificate'), '.')),
      li(p(b('Email notifications:'), ' whether your notifications also come by email.')),
      li(p(b('Tour and tips:'), ' switch the tips on or off, take the tour again, or bring back tips you dismissed. See ', pageLink('The tour and tips'), '.')),
      li(p(b('Sessions:'), ' every browser signed in to your account, and a way to sign any of them out.')),
      li(p(b('API tokens:'), ' keys that let scripts and assistants use Tesria as you.')),
    ),
    p('Each card saves on its own. Accounts that sign in through single sign-on have no Tesria password: their email, password and two-factor sign-in are managed by the organization’s sign-in service.'),
    live('children', { depth: '1', sort: 'position' }),
  ))

  await page('Avatar, name and email', profile, doc(
    p('These three decide how you appear to everyone else: your avatar and name are beside everything you write, from pages to comments to the history of a page.'),
    h(2, 'Avatar'),
    p('Until you upload a picture, your avatar is your initials on a colored circle. Pick any of the twelve colors under ', b('Generated avatar'), ' to change it.'),
    ...(await picture(avatar, 'profile-avatar', 'The Avatar card', 'Your initials on the color you pick, or a picture you upload.')),
    p(b('Upload picture'), ' takes a PNG, JPEG or WebP image of up to 1 MB and crops it to a square from the middle, so a portrait keeps the face. Once you have a picture, ', b('Replace picture'), ' swaps it and ', b('Remove'), ' goes back to your initials.'),
    h(2, 'Display name'),
    p('The name shown on your pages, comments and version history, and the one people find when they type @ to mention you. Change it and choose ', b('Save name'), '.'),
    h(2, 'Email address'),
    p('Type the new address, enter your current password to show it is you, and choose ', b('Change email'), '. You sign in with the new address from then on.'),
    p('If your account signs in through single sign-on, the card says so: your organization’s sign-in service owns your email address, and it is changed there.'),
  ))

  await page('Sessions', profile, doc(
    p('A session is one browser, on one device, that is signed in to your account. The Sessions card lists every one, so you can see where you are signed in and sign out anywhere you should not be: a phone you lost, or a shared computer you forgot to sign out of.'),
    ...(await picture(sessions, 'sessions', 'The Sessions card', 'Each browser signed in to your account. The one you are using is marked this browser.')),
    p('Each row shows the network address it signed in from, the browser and system (such as ', i('Safari on iOS'), '), when it was last active, and when it signed in. The one you are using is marked ', b('this browser'), '.'),
    ul(
      li(p(b('Sign out'), ' beside a session ends it. That browser is signed out the next time it does anything.')),
      li(p(b('Sign out all other sessions'), ' ends every session but the one you are using.')),
    ),
    h(2, 'When sessions end by themselves'),
    ul(
      li(p('After two weeks without being used.')),
      li(p('Ninety days after signing in, however much it is used.')),
      li(p('When you change your password, or turn two-factor sign-in on or off: every other session is signed out.')),
    ),
  ))

  await page('Email notifications', profile, doc(
    p('Everything that arrives in the bell can come by email as well, so you hear about a comment or a mention without having Tesria open. Choose how in the ', b('Email notifications'), ' card on your profile.'),
    table([
      ['Setting', 'What you get'],
      ['Off', 'Only the bell. Everyone starts here.'],
      ['Immediately', 'An email within a minute of each notification.'],
      ['Daily digest', 'One email a day, when something happened.'],
    ], [180, 520]),
    ul(
      li(p(b('What you are told about'), ' is the same as in the bell, and is set by what you watch. See ', pageLink('Watching'), '.')),
      li(p(b('Your Tesria has to send email.'), ' If it does not yet, the card says so. Your choice is kept, and applies once an administrator sets up email (see ', pageLink('Email (SMTP)'), '). Nothing more than a day old is sent then, so turning email on does not bring a flood of old news.')),
      li(p(b('Administrators always get security alerts by email,'), ' whatever they choose here.')),
    ),
  ))

  await page('API tokens', profile, doc(
    p('An API token is a key that lets a script, another program or an AI assistant use Tesria as you, without a browser: a nightly job that updates a status page, say, or an assistant that looks up answers in your wiki.'),
    h(2, 'Making a token'),
    step(1, 'Name it after what will use it'),
    p('In the ', b('API tokens'), ' card, type a name such as ', i('Nightly report'), ' or ', i('CI pipeline'), '. You will be glad of it when you come to revoke one.'),
    step(2, 'Make it read-only if it only reads'),
    p('Tick ', b('Read-only'), ' for anything that only looks things up. A read-only token can read pages and search, and cannot change anything.'),
    step(3, 'Choose how long it lasts'),
    p('Under ', b('Expires'), ', choose 30 days, 90 days (the usual choice), a year, or ', b('Never'), '. A token that expires on its own is one fewer thing to remember to clean up, and one that leaked stops working without anyone noticing it had. Choose ', b('Never'), ' only for something long-lived that you look after, such as a kiosk screen.'),
    step(4, 'Choose Create token, and copy it'),
    p('The token is shown once, right there. It is never shown again, so copy it into the program that will use it before you choose ', b('Done'), '. The list below keeps only its first few characters, to tell tokens apart.'),
    ...(await picture(tokens, 'api-tokens', 'Making an API token', 'A name, when it expires, Read-only if it only reads, then Create token.')),
    h(2, 'Using and revoking tokens'),
    ul(
      li(p('The program sends the token with each request, as ', c('Authorization: Bearer <token>'), '. See ', pageLink('Getting started with the API'), '.')),
      li(p(b('A token can do almost anything you can,'), ' unless it is read-only. It cannot manage your account: making or revoking tokens, your sessions, your password, two-factor and your profile all need you signed in to a browser, so a token that leaks cannot lock you out or make more of itself. It stops working if your role loses the right to use tokens.')),
      li(p(b('Expiring tokens warn you first.'), ' A week before a token expires, the bell (and your email, if you get notifications by email) says so, with its name. Make a new one, put it in the program, and let the old one lapse. The list shows each token’s expiry date, in bold in its last week.')),
      li(p(b('Revoke'), ' a token you no longer need. The list shows when each was last used. Anything using a revoked token stops working at once.')),
    ),
    p('If the card says your role does not allow API tokens, an administrator can grant it in ', b('Administration, Roles'), '.'),
  ))

  await page('Password', profile, doc(
    p('Change your password whenever you think someone else might know it, or if it is one you use elsewhere.'),
    ol(
      li(p('In the ', b('Password'), ' card, enter your current password.')),
      li(p('Enter the new one twice. It must be at least 8 characters; a few unrelated words make a long password that is still easy to type.')),
      li(p('Choose ', b('Change password'), '.')),
    ),
    p('Every other device signed in to your account is signed out, so anyone who knew the old password is locked out. The browser you are using stays signed in.'),
    p('If you have forgotten your password, see ', pageLink('Resetting a password'), '. Accounts that sign in through single sign-on have no Tesria password.'),
  ))
}

// Pictures these pages no longer show: the first version's, and the phone
// copies figure() made beside each one.
const RETIRED = {
  'Comments': ['comments.phone.png'],
  'Mentions and notifications': ['bell.phone.png'],
  'Search': ['search.phone.png'],
  'Label pages': ['label-page.png', 'label-page.phone.png'],
  'A space as a website': ['site-export.phone.png'],
  'Wiki packs': ['pack-export.png', 'pack-export.phone.png', 'pack-import.png', 'pack-import.phone.png'],
  'Public reading': ['admin-spaces.phone.png'],
  'Turning exports off': ['exports-switches.png', 'exports-switches.phone.png'],
  'Your profile': ['profile.png', 'profile.phone.png'],
  'Sessions': ['sessions.phone.png'],
  'Email notifications': ['email-notifications.png', 'email-notifications.phone.png'],
  'API tokens': ['api-tokens.phone.png'],
}

// The profile's pages in the order its cards are in.
const PROFILE_ORDER = ['Avatar, name and email', 'Password', 'Email notifications', 'Sessions', 'API tokens']

export async function cleanup({ author }) {
  const space = await author.call('GET', '/api/spaces/SUPPORT')
  const treeNow = () => author.call('GET', `/api/pages/tree?spaceId=${space.id}`)
  const find = (nodes, title) => {
    for (const n of nodes) {
      if (n.title === title) return n
      const hit = find(n.children ?? [], title)
      if (hit) return hit
    }
    return null
  }

  // Match the Your profile pages to the order of the cards; anything else
  // under it keeps its place after them.
  for (let index = 0; index < PROFILE_ORDER.length; index++) {
    const profile = find(await treeNow(), 'Your profile')
    if (!profile) break
    const kids = profile.children ?? []
    const at = kids.findIndex((n) => n.title === PROFILE_ORDER[index])
    if (at < 0 || at === index) continue
    await author.call('PUT', `/api/pages/${kids[at].id}/move`, { parentPageId: profile.id, index })
    console.log(`  moved ${PROFILE_ORDER[index]} to place ${index + 1} under Your profile`)
  }

  const tree = await treeNow()
  for (const [title, files] of Object.entries(RETIRED)) {
    const node = find(tree, title)
    if (!node) continue
    for (const a of await author.call('GET', `/api/pages/${node.id}/attachments`)) {
      if (files.includes(a.filename)) {
        await author.call('DELETE', `/api/attachments/${a.id}`)
        console.log(`  - ${title}: ${a.filename}`)
      }
    }
  }
}
