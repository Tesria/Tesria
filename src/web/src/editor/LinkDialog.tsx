import { useEffect, useState, type FormEvent } from 'react'
import { createPortal } from 'react-dom'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { getMarkRange } from '@tiptap/core'
import { HeadingLinkList } from './HeadingLinkList'

/** What the dialog starts from: the link under the cursor, or the selection. */
function initialState(editor: TiptapEditor): { href: string; text: string; existing: boolean } {
  const { state } = editor
  const linkType = state.schema.marks.link
  const range = linkType ? getMarkRange(state.selection.$from, linkType) : undefined
  if (range) {
    return {
      href: (editor.getAttributes('link').href as string | undefined) ?? '',
      text: state.doc.textBetween(range.from, range.to, ' '),
      existing: true,
    }
  }
  const { from, to } = state.selection
  const selected = from === to ? '' : state.doc.textBetween(from, to, ' ')
  // A selected address becomes the link's target as well as its text.
  const looksLikeUrl = /^(https?:\/\/|www\.)\S+$/i.test(selected.trim())
  return { href: looksLikeUrl ? selected.trim() : '', text: selected, existing: false }
}

/** "example.com/path" is what people type; the link needs a scheme. */
function normalise(href: string): string {
  const h = href.trim()
  if (!h) return h
  if (/^(https?:|mailto:|tel:|#|\/)/i.test(h)) return h
  return `https://${h}`
}

/**
 * The one place a link is made or changed: from "+ → Link", from Cmd/Ctrl+K,
 * from the selection bubble, and from the Edit button on a link's own
 * bubble. A dialog rather than a popover because it has two fields — the
 * address and the words that carry it — and on a phone a popover anchored
 * to a toolbar button has nowhere sensible to be.
 */
export function LinkDialog({ editor, open, onClose }: { editor: TiptapEditor; open: boolean; onClose: () => void }) {
  const [href, setHref] = useState('')
  const [text, setText] = useState('')
  const [existing, setExisting] = useState(false)

  useEffect(() => {
    if (!open) return
    const init = initialState(editor)
    setHref(init.href)
    setText(init.text)
    setExisting(init.existing)
  }, [open, editor])

  if (!open) return null

  function save(e: FormEvent) {
    // A form inside the editor's chrome: stop the page's own save form from
    // submitting too (see the architecture doc's editor gotcha).
    e.preventDefault()
    e.stopPropagation()
    const target = normalise(href)
    if (!target) return
    const label = text.trim() || target
    const chain = editor.chain().focus()
    if (existing) {
      // Replace the whole link — words and address — in one step.
      chain.extendMarkRange('link')
        .insertContent({ type: 'text', text: label, marks: [{ type: 'link', attrs: { href: target } }] })
        .run()
    } else {
      const { from, to } = editor.state.selection
      const selected = from === to ? '' : editor.state.doc.textBetween(from, to, ' ')
      if (selected && selected === label) {
        chain.setLink({ href: target }).run()
      } else {
        chain.insertContent({ type: 'text', text: label, marks: [{ type: 'link', attrs: { href: target } }] }).run()
      }
    }
    onClose()
  }

  function remove() {
    editor.chain().focus().extendMarkRange('link').unsetLink().run()
    onClose()
  }

  // A portal: the dialog is a fixed overlay, and its natural parent (the
  // toolbar) is translated while a phone keyboard is up, which would make
  // the bar the overlay's containing block.
  return createPortal(
    <div className="recovery-prompt link-dialog" role="dialog" aria-modal="true" aria-label={existing ? 'Edit link' : 'Add link'}>
      <form className="recovery-prompt__card link-dialog__card" onSubmit={save}>
        <h2>{existing ? 'Edit link' : 'Add link'}</h2>
        <label>
          Address
          <input
            autoFocus
            type="text"
            inputMode="url"
            value={href}
            onChange={(e) => setHref(e.target.value)}
            placeholder="https://…"
            onKeyDown={(e) => { if (e.key === 'Escape') onClose() }}
          />
        </label>
        <label>
          Display text
          <input
            type="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={href.trim() ? normalise(href) : 'The words that carry the link'}
            onKeyDown={(e) => { if (e.key === 'Escape') onClose() }}
          />
        </label>
        <div className="link-dialog__headings">
          <HeadingLinkList editor={editor} onPick={setHref} />
        </div>
        <div className="row-gap link-dialog__actions">
          <button type="submit" className="btn btn--primary" disabled={!href.trim()}>Save</button>
          {existing && (
            <button type="button" className="btn btn--danger" onClick={remove}>Remove link</button>
          )}
          <button type="button" className="btn btn--ghost" onClick={onClose}>Cancel</button>
        </div>
      </form>
    </div>,
    document.body,
  )
}
