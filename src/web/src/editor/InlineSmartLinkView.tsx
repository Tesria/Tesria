import { useEffect, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { api, type LinkPreview } from '../api/client'
import { normalizeWebAddress } from './webAddress'

/**
 * A smart link inside a sentence (dev-plan 15.2): the target's title as a
 * link, where the text is. The block smart link's "Inline" display was a
 * block that looked short, so it could never sit in a line of text.
 *
 * Selected while editing, it offers its address and "Card", which turns it
 * back into the block form after its paragraph.
 */
export function InlineSmartLinkView({ node, editor, selected, updateAttributes, getPos }: ReactNodeViewProps) {
  const url = String(node.attrs.url ?? '')
  const [preview, setPreview] = useState<LinkPreview | null>(null)
  const [draft, setDraft] = useState(url)

  useEffect(() => setDraft(url), [url])
  useEffect(() => {
    setPreview(null)
    if (!url) return
    let canceled = false
    api.embeds.unfurl(url).then((p) => !canceled && setPreview(p)).catch(() => {})
    return () => { canceled = true }
  }, [url])

  function toCard() {
    const pos = typeof getPos === 'function' ? getPos() : undefined
    if (typeof pos !== 'number') return
    const $pos = editor.state.doc.resolve(pos)
    const after = $pos.after($pos.depth)
    editor.chain().focus()
      .insertContentAt(after, { type: 'smartLink', attrs: { url, display: 'card' } })
      .deleteRange({ from: pos, to: pos + node.nodeSize })
      .run()
  }

  const title = preview?.title ?? url
  return (
    <NodeViewWrapper as="span" className={selected ? 'smart-link-inline is-selected' : 'smart-link-inline'} contentEditable={false}>
      <a href={url} target="_blank" rel="noreferrer noopener" className="smart-link-inline__link">{title || 'Link'}</a>
      {editor.isEditable && selected && (
        <span className="smart-link-inline__edit">
          <input value={draft} onChange={(e) => setDraft(e.target.value)} aria-label="Smart link address"
            onKeyDown={(e) => {
              if (e.key === 'Enter') { e.preventDefault(); updateAttributes({ url: normalizeWebAddress(draft) }) }
            }} />
          <button type="button" className="link-btn" onClick={() => updateAttributes({ url: normalizeWebAddress(draft) })}>Update</button>
          <button type="button" className="link-btn" onClick={toCard}>Card</button>
        </span>
      )}
    </NodeViewWrapper>
  )
}
