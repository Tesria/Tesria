import { BubbleMenu } from '@tiptap/react/menus'
import { findParentNode } from '@tiptap/core'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { ToolbarButton } from './ToolbarButton'
import { PANEL_LABELS, PANEL_TYPES, type PanelType } from './panelExtension'
import { ErrorPanelIcon, InfoPanelIcon, NotePanelIcon, SuccessPanelIcon, WarningPanelIcon } from './icons'

/** The elements that wrap other blocks, and what their Remove button says. */
const WRAPPERS: Record<string, string> = {
  panel: 'Remove panel',
  expand: 'Remove expand',
  decision: 'Remove decision',
  excerpt: 'Remove excerpt',
  pageProperties: 'Remove page properties',
}

const PANEL_ICONS: Record<PanelType, () => React.JSX.Element> = {
  info: InfoPanelIcon,
  note: NotePanelIcon,
  success: SuccessPanelIcon,
  warning: WarningPanelIcon,
  error: ErrorPanelIcon,
}

/** The innermost wrapper holding the cursor. */
const findWrapper = findParentNode((n) => n.type.name in WRAPPERS)

/**
 * Floats above the panel, expand, decision, excerpt or page properties
 * holding the cursor (dev-plan 10.5 step 1). Before this, none of them could
 * be taken off again once put on, and a panel's type could be changed only
 * by knowing to choose another panel from the slash menu while inside one.
 *
 * Remove unwraps the whole element and keeps everything in it. The existing
 * unset commands lift only the block under the cursor, which splits a
 * three-paragraph panel into a panel, a paragraph and another panel.
 */
export function WrapperMenu({ editor }: { editor: TiptapEditor }) {
  const current = useEditorState({
    editor,
    selector: ({ editor }) => {
      const found = findWrapper(editor.state.selection)
      return found ? { type: found.node.type.name, panelType: found.node.attrs.panelType as PanelType | undefined } : null
    },
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })

  const wrapperRect = () => {
    const found = findWrapper(editor.state.selection)
    const dom = found ? (editor.view.nodeDOM(found.pos) as HTMLElement | null) : null
    return dom ? { getBoundingClientRect: () => dom.getBoundingClientRect() } : null
  }

  const unwrap = () => {
    const found = findWrapper(editor.state.selection)
    if (!found) return
    const { tr } = editor.state
    tr.replaceWith(found.pos, found.pos + found.node.nodeSize, found.node.content)
    editor.view.dispatch(tr.scrollIntoView())
    editor.commands.focus()
  }

  const setPanelType = (panelType: PanelType) => {
    const found = findWrapper(editor.state.selection)
    if (!found || found.node.type.name !== 'panel') return
    editor.view.dispatch(editor.state.tr.setNodeMarkup(found.pos, undefined, { ...found.node.attrs, panelType }))
    editor.commands.focus()
  }

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="wrapperMenu"
      shouldShow={({ editor }) => editor.isEditable && findWrapper(editor.state.selection) !== undefined
        // A table inside page properties has its own controls; so does a
        // layout. This bar belongs to the wrapper, so it waits for a plain
        // text selection rather than competing with those.
        && !editor.isActive('table')}
      getReferencedVirtualElement={wrapperRect}
      options={{ placement: 'top-end' }}
    >
      <div className="toolbar toolbar--bubble wrapper-menu">
        {current?.type === 'panel' && (
          <>
            {PANEL_TYPES.map((t) => {
              const Icon = PANEL_ICONS[t]
              return (
                <ToolbarButton
                  key={t}
                  label={<Icon />}
                  isActive={current.panelType === t}
                  onClick={() => setPanelType(t)}
                  title={`${PANEL_LABELS[t]} panel`}
                />
              )
            })}
            <span className="toolbar__sep" />
          </>
        )}
        {current && (
          <ToolbarButton
            label={WRAPPERS[current.type]}
            isActive={false}
            onClick={unwrap}
            title="Remove it, keeping everything inside"
          />
        )}
      </div>
    </BubbleMenu>
  )
}
