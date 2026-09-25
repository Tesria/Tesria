import { BubbleMenu } from '@tiptap/react/menus'
import { findParentNode } from '@tiptap/core'
import { NodeSelection, type EditorState } from '@tiptap/pm/state'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { LAYOUT_PRESETS, LAYOUT_PRESET_KEYS, LAYOUT_WIDTHS, LAYOUT_WIDTH_LABELS, presetOf, type LayoutWidth } from './layoutExtension'
import { LayoutPresetIcon } from './icons'

const findSection = findParentNode((n) => n.type.name === 'layoutSection')
/** Blocks inside a column that float a menu of their own. */
const findInner = findParentNode((n) => ['panel', 'expand', 'decision', 'excerpt', 'pageProperties', 'table'].includes(n.type.name))

/**
 * Only the innermost thing's menu shows. A picture, a panel or a table in a
 * column has controls of its own, and this bar drawn over them was two menus
 * stacked on top of each other (seen in a screenshot, 2026-09-23). The
 * layout's bar comes back as soon as the cursor is in plain text.
 */
function layoutIsInnermost(state: EditorState): boolean {
  // A selected object (a picture, a chart, the layout itself) has its own
  // menu, and this bar's commands work from the cursor, not a selection.
  if (state.selection instanceof NodeSelection) return false
  const section = findSection(state.selection)
  if (!section) return false
  const inner = findInner(state.selection)
  return !inner || inner.depth < section.depth
}

/**
 * Floats above the layout section holding the cursor: the five presets, the
 * section's width, and "Remove layout". Anchored to the section itself, not
 * to the cursor, so it does not jump between columns as you type.
 */
export function LayoutMenu({ editor }: { editor: TiptapEditor }) {
  const current = useEditorState({
    editor,
    selector: ({ editor }) => {
      const found = findSection(editor.state.selection)
      return found ? { preset: presetOf(found.node), width: found.node.attrs.width as LayoutWidth } : null
    },
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })

  const sectionRect = () => {
    const found = findSection(editor.state.selection)
    const dom = found ? (editor.view.nodeDOM(found.pos) as HTMLElement | null) : null
    return dom ? { getBoundingClientRect: () => dom.getBoundingClientRect() } : null
  }

  const chain = () => editor.chain().focus()

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="layoutMenu"
      shouldShow={({ editor }) => layoutIsInnermost(editor.state)}
      getReferencedVirtualElement={sectionRect}
      options={{ placement: 'top-start' }}
    >
      <div className="toolbar toolbar--bubble layout-menu">
        {LAYOUT_PRESET_KEYS.map((key) => (
          <ToolbarButton
            key={key}
            label={<LayoutPresetIcon widths={LAYOUT_PRESETS[key].widths} />}
            isActive={current?.preset === key}
            onClick={() => chain().setLayoutPreset(key).run()}
            title={LAYOUT_PRESETS[key].label}
          />
        ))}
        <span className="toolbar__sep" />
        {LAYOUT_WIDTHS.map((w) => (
          <ToolbarButton
            key={w}
            label={LAYOUT_WIDTH_LABELS[w]}
            isActive={current?.width === w}
            onClick={() => chain().setLayoutWidth(w).run()}
            title={`${LAYOUT_WIDTH_LABELS[w]} section`}
          />
        ))}
        <span className="toolbar__sep" />
        <ToolbarButton label="Remove layout" isActive={false} onClick={() => chain().removeLayout().run()} title="Remove the columns, keeping their content" />
      </div>
    </BubbleMenu>
  )
}
