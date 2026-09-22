import { Extension } from '@tiptap/core'

/** Indent steps a block may take, and the width of one. Confluence stops at six; four is plenty here. */
export const MAX_INDENT = 4
export const INDENT_STEP_REM = 1.75

/** Blocks that can be indented: the same set TextAlign is configured for. */
const INDENTABLE = ['paragraph', 'heading'] as const

export function clampIndent(value: unknown): number {
  const n = typeof value === 'number' ? value : parseInt(String(value ?? ''), 10)
  return Number.isFinite(n) ? Math.min(MAX_INDENT, Math.max(0, Math.trunc(n))) : 0
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    textFormatting: {
      indent: () => ReturnType
      outdent: () => ReturnType
      /** Strip every mark, and any indent/alignment, returning a heading to body text. */
      clearFormatting: () => ReturnType
    }
  }
}

/**
 * Paragraph indent, as a `textIndent` attribute on the block rather than a
 * wrapper node: an indent is a property of the paragraph, and a wrapper
 * would have to be nested N deep and would fight list lifting.
 *
 * The rendered value is computed from a clamped integer, never from the
 * stored string, so document JSON can never put arbitrary CSS into a
 * `style` attribute (the same rule the export renderer follows).
 */
function shiftIndent(
  editor: { isActive: (type: string) => boolean; getAttributes: (type: string) => Record<string, unknown> },
  commands: { updateAttributes: (type: string, attrs: Record<string, unknown>) => boolean },
  by: number,
): boolean {
  const type = INDENTABLE.find((t) => editor.isActive(t))
  if (!type) return false
  return commands.updateAttributes(type, { textIndent: clampIndent(clampIndent(editor.getAttributes(type).textIndent) + by) })
}

export const TextIndent = Extension.create({
  name: 'textIndent',

  addGlobalAttributes() {
    return [
      {
        types: [...INDENTABLE],
        attributes: {
          textIndent: {
            default: 0,
            parseHTML: (element: HTMLElement) => clampIndent(element.getAttribute('data-indent')),
            renderHTML: (attributes: { textIndent?: number }) => {
              const level = clampIndent(attributes.textIndent)
              if (level === 0) return {}
              return { 'data-indent': String(level), style: `margin-left: ${level * INDENT_STEP_REM}rem` }
            },
          },
        },
      },
    ]
  },

  addCommands() {
    return {
      indent: () => ({ editor, commands }) => shiftIndent(editor, commands, 1),
      outdent: () => ({ editor, commands }) => shiftIndent(editor, commands, -1),
      clearFormatting:
        () =>
        ({ chain, editor }) => {
          const c = chain().unsetAllMarks()
          // Deliberately not clearNodes(): that would also unwrap a list, a
          // panel or a layout column, which is a structural edit, not a
          // formatting one. A heading is the one block "clear formatting"
          // should reset, because a heading *is* a text style.
          if (editor.isActive('heading')) c.setParagraph()
          for (const type of INDENTABLE) {
            if (editor.isActive(type)) c.updateAttributes(type, { textIndent: 0, textAlign: null })
          }
          return c.run()
        },
    }
  },

  addKeyboardShortcuts() {
    return {
      // Google Docs' and Confluence's bindings. Tab is deliberately left
      // alone: it already moves between table cells and nests list items.
      'Mod-]': () => this.editor.commands.indent(),
      'Mod-[': () => this.editor.commands.outdent(),
      'Mod-\\': () => this.editor.commands.clearFormatting(),
    }
  },
})
