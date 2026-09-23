import type { ComponentType } from 'react'
import type { BlockParams } from './dynamicBlock'
import {
  ChildrenBlockIcon, ClockIcon, LabelIcon, PaperclipIcon, HistoryIcon, PeopleIcon,
  IncludeIcon, ExcerptIcon, PropertiesIcon, TaskListIcon, TocIcon,
} from './icons'

/**
 * One param of a kind, as the generic editing form needs it. The server is
 * authoritative about defaults and validation; these mirror it so the form
 * shows sensible starting values and the right control.
 */
export type ParamField =
  | { key: string; label: string; type: 'select'; options: { value: string; label: string }[]; default: string }
  | { key: string; label: string; type: 'number'; min: number; max: number; default: number }
  | { key: string; label: string; type: 'text'; placeholder?: string; default?: string }
  /** Comma-separated label names; a plain text field with label-shaped help. */
  | { key: string; label: string; type: 'labels'; default?: string }
  /** A page id. A text field today; a page picker is the obvious upgrade. */
  | { key: string; label: string; type: 'page'; default?: string }

export type DynamicKind = {
  /** Kebab-case, as the server and the document know it. */
  kind: string
  /** Confluence's name for it, so a Confluence user finds what they expect. */
  title: string
  description: string
  icon: ComponentType
  keywords?: string[]
  params: ParamField[]
}

/**
 * The client catalogue of kinds (architecture.md, "Dynamic blocks",
 * decision 8). The slash menu and the + menu list it; `DynamicBlockMenu`
 * renders any kind's form from its `params`. Adding a kind here is the whole
 * client-side cost of adding one.
 */
export const DYNAMIC_KINDS: DynamicKind[] = [
  {
    kind: 'children',
    title: 'Children display',
    description: 'The pages beneath this one, kept up to date',
    icon: ChildrenBlockIcon,
    keywords: ['child', 'subpages', 'tree'],
    params: [
      { key: 'depth', label: 'Depth', type: 'number', min: 1, max: 3, default: 1 },
      {
        key: 'sort', label: 'Sort by', type: 'select', default: 'position',
        options: [
          { value: 'position', label: 'Tree order' },
          { value: 'title', label: 'Title' },
          { value: 'updated', label: 'Recently updated' },
        ],
      },
    ],
  },
  {
    kind: 'recently-updated',
    title: 'Recently updated',
    description: 'What changed most recently, and who changed it',
    icon: ClockIcon,
    keywords: ['recent', 'activity', 'changes'],
    params: [
      {
        key: 'scope', label: 'Look in', type: 'select', default: 'space',
        options: [{ value: 'space', label: 'This space' }, { value: 'tree', label: 'This page and below' }],
      },
      { key: 'limit', label: 'Show', type: 'number', min: 1, max: 50, default: 10 },
    ],
  },
  {
    kind: 'content-by-label',
    title: 'Content by label',
    description: 'Pages carrying the labels you name',
    icon: LabelIcon,
    keywords: ['label', 'tag', 'by label'],
    params: [
      { key: 'labels', label: 'Labels', type: 'labels' },
      {
        key: 'match', label: 'Match', type: 'select', default: 'any',
        options: [{ value: 'any', label: 'Any label' }, { value: 'all', label: 'All labels' }],
      },
      {
        key: 'scope', label: 'Look in', type: 'select', default: 'space',
        options: [{ value: 'space', label: 'This space' }, { value: 'all', label: 'Everywhere' }],
      },
      { key: 'limit', label: 'Show', type: 'number', min: 1, max: 100, default: 25 },
    ],
  },
  {
    kind: 'attachments',
    title: 'Attachments',
    description: 'The files attached to this page',
    icon: PaperclipIcon,
    keywords: ['files', 'uploads'],
    params: [],
  },
  {
    kind: 'change-history',
    title: 'Change history',
    description: 'This page\'s versions, newest first',
    icon: HistoryIcon,
    keywords: ['versions', 'history', 'revisions'],
    params: [{ key: 'limit', label: 'Show', type: 'number', min: 1, max: 50, default: 10 }],
  },
  {
    kind: 'contributors',
    title: 'Contributors',
    description: 'Who has edited this page, most edits first',
    icon: PeopleIcon,
    keywords: ['authors', 'who', 'editors'],
    params: [
      {
        key: 'scope', label: 'Count edits on', type: 'select', default: 'page',
        options: [{ value: 'page', label: 'This page' }, { value: 'tree', label: 'This page and below' }],
      },
    ],
  },
  {
    kind: 'include-page',
    title: 'Include page',
    description: 'Show another page\'s content here',
    icon: IncludeIcon,
    keywords: ['include', 'embed', 'transclude'],
    params: [{ key: 'page', label: 'Page', type: 'page' }],
  },
  {
    kind: 'excerpt-include',
    title: 'Excerpt include',
    description: 'Show the excerpt marked on another page',
    icon: ExcerptIcon,
    keywords: ['excerpt', 'summary', 'include'],
    params: [{ key: 'page', label: 'Page', type: 'page' }],
  },
  {
    kind: 'page-properties-report',
    title: 'Page properties report',
    description: 'A table of the properties on every labelled page',
    icon: PropertiesIcon,
    keywords: ['properties', 'report', 'metadata'],
    params: [
      { key: 'labels', label: 'Labels', type: 'labels' },
      { key: 'limit', label: 'Show', type: 'number', min: 1, max: 100, default: 25 },
    ],
  },
  {
    kind: 'labels',
    title: 'Labels list',
    description: 'This page\'s labels, the popular ones, or related ones',
    icon: LabelIcon,
    keywords: ['labels', 'tags', 'popular', 'related'],
    params: [
      {
        key: 'mode', label: 'Show', type: 'select', default: 'page',
        options: [
          { value: 'page', label: "This page's labels" },
          { value: 'popular', label: 'Popular in this space' },
          { value: 'related', label: 'Related labels' },
        ],
      },
      { key: 'limit', label: 'Limit', type: 'number', min: 1, max: 100, default: 20 },
    ],
  },
  {
    kind: 'task-report',
    title: 'Task report',
    description: 'Action items across pages, by assignee and state',
    icon: TaskListIcon,
    keywords: ['tasks', 'action items', 'todo', 'assigned'],
    params: [
      {
        key: 'scope', label: 'Look in', type: 'select', default: 'tree',
        options: [
          { value: 'tree', label: 'This page and below' },
          { value: 'space', label: 'This space' },
          { value: 'all', label: 'Everywhere' },
        ],
      },
      {
        key: 'status', label: 'State', type: 'select', default: 'open',
        options: [
          { value: 'open', label: 'Not done' },
          { value: 'done', label: 'Done' },
          { value: 'all', label: 'All' },
        ],
      },
      {
        key: 'assignee', label: 'Assigned to', type: 'select', default: 'any',
        options: [{ value: 'any', label: 'Anyone' }, { value: 'me', label: 'Me' }],
      },
      { key: 'limit', label: 'Show', type: 'number', min: 1, max: 100, default: 25 },
    ],
  },
  {
    kind: 'page-tree',
    title: 'Page tree',
    description: 'The page tree, from here or from the space root',
    icon: TocIcon,
    keywords: ['tree', 'index', 'sitemap', 'navigation'],
    params: [
      {
        key: 'root', label: 'Start at', type: 'select', default: 'host',
        options: [{ value: 'host', label: 'This page' }, { value: 'space', label: 'Space root' }],
      },
      { key: 'depth', label: 'Depth', type: 'number', min: 1, max: 6, default: 3 },
    ],
  },
]

export function kindOf(kind: string): DynamicKind | undefined {
  return DYNAMIC_KINDS.find((k) => k.kind === kind)
}

/** The params a fresh block starts with: every field at its default. */
export function defaultParams(kind: DynamicKind): BlockParams {
  return Object.fromEntries(
    kind.params.filter((f) => f.default !== undefined).map((f) => [f.key, String(f.default)]),
  )
}
