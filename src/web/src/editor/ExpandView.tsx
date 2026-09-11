import { useState } from 'react'
import { NodeViewContent, NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { ChevronDownIcon } from './icons'

/**
 * Expand node view: a toggle + title row above the collapsible body.
 * Starts open while editing (hidden content is unreachable content) and
 * closed for readers, which is what an expand is for.
 */
export function ExpandView({ node, editor, updateAttributes }: ReactNodeViewProps) {
  const [open, setOpen] = useState(editor.isEditable)
  const title = (node.attrs.title as string | undefined) ?? ''

  return (
    <NodeViewWrapper className={open ? 'expand is-open' : 'expand'} data-type="expand">
      <div className="expand__header" contentEditable={false}>
        <button
          type="button"
          className="expand__toggle"
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          title={open ? 'Collapse' : 'Expand'}
        >
          <ChevronDownIcon />
        </button>
        {editor.isEditable ? (
          <input
            className="expand__title"
            value={title}
            placeholder="Give this expand a title…"
            onChange={(e) => updateAttributes({ title: e.target.value })}
            onKeyDown={(e) => {
              // Enter moves on to the body instead of doing nothing.
              if (e.key === 'Enter') {
                e.preventDefault()
                setOpen(true)
                editor.commands.focus()
              }
            }}
          />
        ) : (
          <button type="button" className="expand__title-text" onClick={() => setOpen((v) => !v)}>
            {title || 'Click to expand'}
          </button>
        )}
      </div>
      <NodeViewContent className="expand__body" hidden={!open} />
    </NodeViewWrapper>
  )
}
