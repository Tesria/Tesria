/**
 * The tips catalogue (dev-plan 10.3).
 *
 * A tip teaches one thing the product will not otherwise tell you, at the
 * moment it would have helped. The rules that keep it from being a nuisance
 * live in `TipHost`: one at a time, three a day, never over a dialog. What
 * lives here is which tips exist, what has to be true for each to be worth
 * showing, and what it points at.
 *
 * A tip whose anchor is not on the page is skipped rather than queued: the
 * moment has passed, and it will come round again.
 */

/** Where a tip may appear, matched against the current route. */
export type TipContext = 'editor' | 'space' | 'page' | 'search' | 'profile'

/** What the host knows about the moment, for a tip to judge itself against. */
export type TipState = {
  /** How many separate editing sessions this person has opened, ever. */
  editorSessions: number
  /** How many pages they have created. */
  pagesCreated: number
  /** How many searches they have run. */
  searches: number
  /** Visits to the page currently open. */
  visitsToThisPage: number
  /** Profile visits. */
  profileVisits: number
  /** Words in the current editor selection. */
  selectionWords: number
  /** The editor has been focused at least once this session. */
  editorFocused: boolean
  /** Pages in the space currently open. */
  pagesInSpace: number
  /** The open page has comments. */
  pageHasComments: boolean
  /** The open page has labels. */
  pageHasLabels: boolean
  /** The open page was written by somebody else. */
  pageByOther: boolean
  /** The open page is this person's own. */
  pageIsMine: boolean
  /** The open page contains a table. */
  pageHasTable: boolean
  /** The current document has a list of three or more items. */
  longList: boolean
  /** Text carrying formatting was pasted in this editing session. */
  pastedFormatting: boolean
  /** The page has more than one contributor, or any comment. */
  hasCollaborators: boolean
  /** Two-factor is off for this account. */
  twoFactorOff: boolean
}

export type Tip = {
  key: string
  context: TipContext
  /** A CSS selector for the control this is about. Absent means the corner. */
  anchor?: string
  title: string
  body: string
  /** An onboarding clip to show inside the card. */
  clip?: string
  /** Lower comes first. */
  priority: number
  trigger: (s: TipState) => boolean
}

export const TIPS: Tip[] = [
  {
    key: 'slash-menu',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Type / for anything',
    body: 'On an empty line, / opens a menu of everything you can insert: tables, panels, diagrams, charts, other pages.',
    clip: 'editor-slash',
    priority: 1,
    trigger: (s) => s.editorFocused,
  },
  {
    key: 'bubble-menu',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Select text to act on it',
    body: 'A menu follows your selection: bold and the rest, a link, or a comment on exactly those words.',
    clip: 'editor-toolbar',
    priority: 2,
    trigger: (s) => s.selectionWords > 3,
  },
  {
    key: 'link-shortcut',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Ctrl or Cmd and K makes a link',
    body: 'With text selected, it turns the selection into a link without reaching for the toolbar.',
    clip: 'link-shortcut',
    priority: 5,
    trigger: (s) => s.editorSessions >= 2,
  },
  {
    key: 'mention',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Type @ to bring someone in',
    body: 'They are told, and they get a link straight to the page.',
    clip: 'mention',
    priority: 4,
    trigger: (s) => s.hasCollaborators,
  },
  {
    key: 'emoji',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Type : for emoji',
    body: 'A colon followed by a word or two finds it: :tada, :warning, :eyes.',
    priority: 9,
    trigger: (s) => s.editorSessions >= 10,
  },
  {
    key: 'indent',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Nest a list without the mouse',
    body: 'Ctrl or Cmd with ] indents an item, and with [ pulls it back out.',
    priority: 7,
    trigger: (s) => s.longList,
  },
  {
    key: 'clear-formatting',
    context: 'editor',
    anchor: '.ProseMirror',
    title: 'Paste brought its formatting with it',
    body: 'Ctrl or Cmd with \\ strips it back to plain text, keeping the words.',
    priority: 3,
    trigger: (s) => s.pastedFormatting,
  },
  {
    key: 'templates',
    context: 'editor',
    anchor: '.page-actionbar',
    title: 'Save a page as a template',
    body: 'A page you would write again is a template. New pages can start from it.',
    clip: 'templates',
    priority: 8,
    trigger: (s) => s.pagesCreated >= 3,
  },
  {
    key: 'page-tree-drag',
    context: 'space',
    anchor: '.sidebar .tree-section',
    title: 'Drag pages to rearrange them',
    body: 'The pencil turns on reorder mode, and a page can be dropped anywhere in the tree.',
    clip: 'page-tree-drag',
    priority: 6,
    trigger: (s) => s.pagesInSpace >= 3,
  },
  {
    key: 'inline-comment',
    context: 'page',
    anchor: 'article',
    title: 'Comment on the exact words',
    body: 'Select any text and choose Comment. It stays attached to that phrase rather than the whole page.',
    clip: 'inline-comment',
    priority: 4,
    trigger: (s) => !s.pageHasComments && s.visitsToThisPage >= 2,
  },
  {
    key: 'watch',
    context: 'page',
    anchor: '.page-actionbar',
    title: 'Be told when this changes',
    body: 'Watch a page and edits and comments reach you, in the bell and by email if you want them.',
    clip: 'watch',
    priority: 6,
    trigger: (s) => s.pageByOther,
  },
  {
    key: 'labels',
    context: 'page',
    anchor: 'article',
    title: 'Labels group pages across spaces',
    body: 'A label gathers everything on one topic, wherever it lives. Add one from the page itself.',
    priority: 7,
    trigger: (s) => s.pageIsMine && !s.pageHasLabels,
  },
  {
    key: 'search-scope',
    context: 'search',
    anchor: 'input[aria-label="Search pages"]',
    title: 'Search reads more than titles',
    body: 'It covers the text of every page you can see, and labels as well. Narrow it to one space when a word is everywhere.',
    clip: 'search',
    priority: 5,
    trigger: (s) => s.searches >= 2,
  },
  {
    key: 'full-width',
    context: 'page',
    anchor: '.page-actionbar',
    title: 'Wide tables need the width',
    body: 'Full width gives the page the whole window, which a table with many columns usually wants.',
    priority: 8,
    trigger: (s) => s.pageHasTable,
  },
  {
    key: 'two-factor',
    context: 'profile',
    anchor: '.profile__section',
    title: 'Two-factor is worth the minute',
    body: 'A stolen password on its own stops being enough. Your authenticator app is all it needs.',
    priority: 2,
    trigger: (s) => s.twoFactorOff && s.profileVisits >= 3,
  },
]

/** Which context a path belongs to, or null where tips have no business. */
export function contextFor(pathname: string): TipContext | null {
  if (pathname.startsWith('/setup') || pathname.startsWith('/welcome')) return null
  if (/\/(new|edit)$/.test(pathname)) return 'editor'
  if (pathname.startsWith('/search')) return 'search'
  if (pathname.startsWith('/profile')) return 'profile'
  if (/^\/spaces\/[^/]+\/pages\//.test(pathname)) return 'page'
  if (/^\/spaces\/[^/]+/.test(pathname)) return 'space'
  return null
}

/**
 * The best tip for this moment, or null. Ordered by priority, and a tip whose
 * anchor is not on the page is passed over rather than held back.
 */
export function chooseTip(
  context: TipContext, state: TipState, dismissed: Set<string>,
): Tip | null {
  return TIPS
    .filter((t) => t.context === context && !dismissed.has(t.key))
    .sort((a, b) => a.priority - b.priority)
    .find((t) => t.trigger(state) && (!t.anchor || document.querySelector(t.anchor) !== null))
    ?? null
}
