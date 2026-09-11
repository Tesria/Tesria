import { BubbleMenu } from '@tiptap/react/menus'
import { findParentNode } from '@tiptap/core'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { LAYOUT_PRESETS, LAYOUT_PRESET_KEYS, LAYOUT_WIDTHS, LAYOUT_WIDTH_LABELS, presetOf, type LayoutWidth } from './layoutExtension'
import { LayoutPresetIcon } from './icons'

const findSection = findParentNode((n) => n.type.name === 'layoutSection')

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
      editor={editor}
      pluginKey="layoutMenu"
      shouldShow={({ editor }) => editor.isActive('layoutSection')}
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
