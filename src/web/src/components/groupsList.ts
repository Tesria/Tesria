import type { AccessExplanation, GroupKind, GroupOverview, PageTreeNode } from '../api/client'

/**
 * The Groups page's logic (dev-plan 21.4), apart from its rendering so the
 * edge cases can be tested: the sections a list falls into, how members and
 * grants read, the emails in a pasted list, and the sentences that say why
 * someone can see a space.
 */

const LEVEL = ['View', 'Edit', 'Admin']

/** "View", "Edit", "Admin", or "nothing" for no access. */
export function levelName(level: number | null | undefined): string {
  return level == null ? 'nothing' : LEVEL[level] ?? 'nothing'
}

export type GroupSection = {
  key: string
  /** Which kind of group it holds; a space's section holds that space's four. */
  kind: GroupKind
  title: string
  /** For a space's section: its key, shown as a badge so "Handbook Admins" never reads like the built-in Admins. */
  spaceKey: string | null
  groups: GroupOverview[]
}

/**
 * Splits the list into the page's sections, keeping the server's order:
 * built in, global, custom, then one section per space. The built-in groups
 * are titled for what they are for (21.5): Owner, Admins and Users decide
 * who runs Tesria, and at the top of a list of access they read as if they
 * opened spaces.
 */
export function sectionsOf(groups: GroupOverview[]): GroupSection[] {
  const sections: GroupSection[] = []
  const byKey = new Map<string, GroupSection>()
  for (const g of groups) {
    const key = g.kind === 'space' ? `space:${g.spaceId}` : g.kind
    let section = byKey.get(key)
    if (!section) {
      section = {
        key,
        kind: g.kind,
        title: g.kind === 'builtin' ? 'Running Tesria'
          : g.kind === 'global' ? 'Global'
          : g.kind === 'custom' ? 'Custom'
          : g.spaceName ?? 'A space',
        spaceKey: g.kind === 'space' ? g.spaceKey : null,
        groups: [],
      }
      byKey.set(key, section)
      sections.push(section)
    }
    section.groups.push(g)
  }
  return sections
}

/** "3 members", "1 member, 1 suspended", "No members". */
export function memberCountText(g: Pick<GroupOverview, 'activeMembers' | 'suspendedMembers'>): string {
  const active = g.activeMembers === 0 && g.suspendedMembers === 0
    ? 'No members'
    : `${g.activeMembers} member${g.activeMembers === 1 ? '' : 's'}`
  return g.suspendedMembers > 0 ? `${active}, ${g.suspendedMembers} suspended` : active
}

/**
 * What a group gives, as short phrases ("Edit on Handbook"), the first few
 * and how many more. The global groups' View on every space comes first;
 * page restrictions are counted rather than named here.
 */
export function accessSummary(
  g: Pick<GroupOverview, 'everySpace' | 'grants' | 'restrictions'>,
  max = 3,
): { shown: string[]; more: number } {
  const all = [
    ...(g.everySpace ? ['View on every space'] : []),
    ...g.grants.map((x) => `${levelName(x.operation)} on ${x.spaceName}`),
  ]
  const restricted = g.restrictions.length
  if (restricted > 0) all.push(`named on ${restricted} page restriction${restricted === 1 ? '' : 's'}`)
  return { shown: all.slice(0, max), more: Math.max(0, all.length - max) }
}

const EMAIL = /^[^\s@<>"]+@[^\s@<>"]+$/

/**
 * The email addresses in a pasted list: separated by commas, semicolons,
 * spaces or lines, each bare or as `Name <address>` the way a mail program
 * copies them. Anything else comes back as not understood, so the page can
 * say so instead of dropping it. Repeats are kept once, ignoring case.
 */
export function parseEmailList(text: string): { emails: string[]; invalid: string[] } {
  const emails: string[] = []
  const invalid: string[] = []
  const seen = new Set<string>()
  const add = (email: string) => {
    const key = email.toLowerCase()
    if (!seen.has(key)) {
      seen.add(key)
      emails.push(email)
    }
  }
  // A quoted display name is only a name, and may hold a comma of its own;
  // a quoted address is still an address.
  const unquoted = text.replace(/"([^"]*)"/g, (_, inner: string) => (EMAIL.test(inner.trim()) ? inner.trim() : ' '))
  for (const part of unquoted.split(/[,;\n\r]+/)) {
    const item = part.trim()
    if (!item) continue
    const bracketed = item.match(/<([^<>]+)>/)
    if (bracketed) {
      const inner = bracketed[1].trim()
      if (EMAIL.test(inner)) add(inner)
      else invalid.push(item)
      continue
    }
    // Several addresses separated only by spaces; a name on its own is not one.
    for (const word of item.split(/\s+/)) {
      const bare = word.replace(/^["'(]+|["'),.]+$/g, '')
      if (EMAIL.test(bare)) add(bare)
      else if (bare) invalid.push(bare)
    }
  }
  return { emails, invalid }
}

type Reason = AccessExplanation['reasons'][number]

/** One reason, as a sentence. */
export function reasonText(r: Reason): string {
  const level = levelName(r.level)
  switch (r.kind) {
    case 'everyone':
      return `Everyone signed in may ${r.level === 2 ? 'administer' : level.toLowerCase()} this space.`
    case 'global':
      return `They are in ${r.label}, which can view every space.`
    case 'direct':
      return `They were given ${level} here by name.`
    default:
      return `They are in ${r.label}, which has ${level} here.`
  }
}

export type PageChoice = { id: string; title: string; depth: number }

/**
 * A space's page tree as a list to choose from, in tree order with each
 * page's depth, keeping only titles that contain the filter (all of them
 * without one). Depth is dropped while filtering: the matches are no longer
 * a tree.
 */
export function pageChoices(nodes: PageTreeNode[], filter = ''): PageChoice[] {
  const wanted = filter.trim().toLowerCase()
  const out: PageChoice[] = []
  const walk = (list: PageTreeNode[], depth: number) => {
    for (const node of list) {
      if (!wanted || node.title.toLowerCase().includes(wanted))
        out.push({ id: node.id, title: node.title, depth: wanted ? 0 : depth })
      walk(node.children, depth + 1)
    }
  }
  walk(nodes, 0)
  return out
}

/** The headline answer for a space: what they may do, or that they cannot see it. */
export function spaceAnswer(e: Pick<AccessExplanation, 'level' | 'person' | 'space'>): string {
  const who = e.person.displayName
  if (e.level == null) return `${who} cannot see ${e.space.name}.`
  const verb = e.level === 2 ? 'administer' : e.level === 1 ? 'edit' : 'view'
  return `${who} can ${verb} ${e.space.name}.`
}

/** How many people are in a space's groups, counting each group's active members (someone in two counts twice). */
export function sectionMembers(section: GroupSection): number {
  return section.groups.reduce((n, g) => n + g.activeMembers, 0)
}
