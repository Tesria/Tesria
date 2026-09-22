import { useCallback, useEffect, useReducer, useState } from 'react'
import { createPortal } from 'react-dom'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { api, type Comment } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { useDismissable } from '../hooks/useDismissable'
import { CommentItem } from '../routes/panels/CommentsPanel'
import { buildThreads, findThread } from '../routes/panels/commentThreads'

const WIDTH = 360

/**
 * Clicking highlighted (inline-commented) text opens that comment's thread
 * right there, under the text: reply, edit and delete included, the same
 * component as the Comments tab. Before this, an inline comment could only
 * be read by scrolling to the bottom of the page and working out which one
 * it was.
 *
 * Rendered into the document body, positioned in page coordinates from the
 * highlight's rectangle, so it scrolls with the text and, in the editor,
 * is not inside the page's own <form>.
 */
export function InlineCommentPopover({ editor, getPageId }: { editor: TiptapEditor; getPageId?: () => Promise<string> }) {
  const { user } = useAuth()
  const [commentId, setCommentId] = useState<string | null>(null)
  const [pageId, setPageId] = useState<string | null>(null)
  const [comments, setComments] = useState<Comment[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [, rerender] = useReducer((n: number) => n + 1, 0)
  const close = useCallback(() => setCommentId(null), [])
  const ref = useDismissable<HTMLDivElement>(commentId !== null, close)

  // Open on a click on highlighted text: the innermost comment mark wins
  // when marks overlap.
  useEffect(() => {
    const dom = editor.view.dom
    function onClick(e: MouseEvent) {
      const el = e.target instanceof Element ? e.target.closest('[data-comment-id]') : null
      if (!el || !dom.contains(el)) return
      setCommentId(el.getAttribute('data-comment-id'))
    }
    dom.addEventListener('click', onClick)
    return () => dom.removeEventListener('click', onClick)
  }, [editor])

  const load = useCallback(async () => {
    if (!getPageId) return
    try {
      const id = await getPageId()
      setPageId(id)
      setComments(await api.comments.listForPage(id))
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load this comment.')
    }
  }, [getPageId])

  useEffect(() => {
    if (commentId === null) return
    setComments(null)
    void load()
  }, [commentId, load])

  // The highlight can move (typing above it, the window resizing); keep up.
  useEffect(() => {
    if (commentId === null) return
    window.addEventListener('resize', rerender)
    editor.on('transaction', rerender)
    return () => {
      window.removeEventListener('resize', rerender)
      editor.off('transaction', rerender)
    }
  }, [commentId, editor])

  if (commentId === null) return null
  const anchor = editor.view.dom.querySelector(`[data-comment-id="${CSS.escape(commentId)}"]`)
  if (!anchor) return null
  const rect = anchor.getBoundingClientRect()
  const docWidth = document.documentElement.clientWidth
  const width = Math.min(WIDTH, docWidth - 16)
  const left = Math.max(8, Math.min(rect.left, docWidth - width - 8)) + window.scrollX
  const top = rect.bottom + window.scrollY + 6

  const thread = comments ? findThread(buildThreads(comments), commentId) : null

  return createPortal(
    <div
      ref={ref}
      className="inline-comment-popover floating-menu"
      style={{ left, top, width }}
      role="dialog"
      aria-label="Comment"
    >
      <div className="inline-comment-popover__head">
        <span>Comment</span>
        <button type="button" className="popover__close popover__close--always" aria-label="Close" onClick={close}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
        </button>
      </div>
      {error ? (
        <p className="muted small">{error}</p>
      ) : !comments ? (
        <p className="muted small">Loading…</p>
      ) : !thread || !pageId ? (
        <p className="muted small">This comment is no longer on the page.</p>
      ) : (
        <ul className="comment-list">
          <CommentItem node={thread} pageId={pageId} onChanged={load} readOnly={!user} />
        </ul>
      )}
    </div>,
    document.body,
  )
}
