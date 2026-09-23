import { Node, mergeAttributes } from '@tiptap/core'

/**
 * Confluence-style panels: a colored block that calls out a piece of content.
 *
 * Mirrors Atlassian Document Format's own `panel` node, whose `panelType` is
 * exactly one of info/note/warning/success/error
 * (https://developer.atlassian.com/cloud/jira/platform/apis/document/nodes/panel/).
 * Confluence's older "Info/Tip/Note/Warning" macros map onto that same set,
 * the legacy Tip macro is today's `success` panel, so those four names all
 * have a home here without inventing a sixth type.
 *
 * The type-specific color and icon live entirely in index.css (`.panel--*`),
 * keyed off `data-panel-type`, so the icon is a `::before` pseudo-element
 * rather than a real DOM node: ProseMirror owns the children of this node and
 * an injected element would be fighting it. That also means read-only
 * rendering (PageView, history previews) gets the icon for free, with no node
 * view to mount.
 */
export const PANEL_TYPES = ['info', 'note', 'success', 'warning', 'error'] as const

export type PanelType = (typeof PANEL_TYPES)[number]

/** Labels shared by the toolbar dropdown and the slash menu, so they can't drift. */
export const PANEL_LABELS: Record<PanelType, string> = {
  info: 'Info',
  note: 'Note',
  success: 'Tip',
  warning: 'Warning',
  error: 'Error',
}

function isPanelType(value: unknown): value is PanelType {
  return typeof value === 'string' && (PANEL_TYPES as readonly string[]).includes(value)
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    panel: {
      /** Wrap the current block(s) in a panel, or retype the panel already there. */
      setPanel: (type: PanelType) => ReturnType
      /** Unwrap the panel around the cursor, leaving its content behind. */
      unsetPanel: () => ReturnType
      /** Set the panel type, or remove the panel if it already has that type. */
      togglePanel: (type: PanelType) => ReturnType
    }
  }
}

export const Panel = Node.create({
  name: 'panel',
  group: 'block',
  // `block+`, not ADF's narrower paragraph/heading/list set: this editor has
  // no separate "panel content" concept, and allowing a code block or table
  // inside a panel costs nothing and matches what the rest of the schema does
  // for blockquote.
  content: 'block+',
  defining: true,

  addAttributes() {
    return {
      panelType: {
        default: 'info' as PanelType,
        parseHTML: (element: HTMLElement) => {
          const raw = element.getAttribute('data-panel-type')
          return isPanelType(raw) ? raw : 'info'
        },
        renderHTML: (attributes: { panelType?: string }) => ({
          'data-panel-type': isPanelType(attributes.panelType) ? attributes.panelType : 'info',
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-panel-type]' }]
  },

  renderHTML({ HTMLAttributes, node }) {
    const type = isPanelType(node.attrs.panelType) ? node.attrs.panelType : 'info'
    return ['div', mergeAttributes(HTMLAttributes, { class: `panel panel--${type}` }), 0]
  },

  addCommands() {
    return {
      setPanel:
        (type) =>
        ({ commands }) => {
          // Already a panel: retype in place rather than nesting a second one.
          if (commands.updateAttributes(this.name, { panelType: type })) return true
          return commands.wrapIn(this.name, { panelType: type })
        },
      unsetPanel:
        () =>
        ({ commands }) =>
          commands.lift(this.name),
      togglePanel:
        (type) =>
        ({ editor, commands }) => {
          if (editor.isActive(this.name, { panelType: type })) return commands.lift(this.name)
          return commands.setPanel(type)
        },
    }
  },
})
